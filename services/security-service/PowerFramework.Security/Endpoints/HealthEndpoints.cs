// =================================================================================================
//  HealthEndpoints.cs : the anonymous readiness probe for the Security service.
//
//  Contract C-10 (health and readiness), readiness half. Served on port 5104 at exactly /health, over
//  the one TLS listener this service declares.
//
//  THIS ENDPOINT IS LOAD BEARING INFRASTRUCTURE, NOT DECORATION.
//  It is what orchestration's `depends_on: condition: service_healthy` waits on. Together with the
//  Persistence and DataServices probes it is what makes Gateway report healthy only after all three of
//  its upstreams do - which docs/ARCHITECTURE.md 4.2 names the most important readiness property in
//  the orchestration, and 10.4 records as the single requirement that ruled out the .NET Aspire
//  publisher, because `depends_on` with a `service_healthy` condition was not expressible through the
//  generated model. The container definition's own HEALTHCHECK targets this same route. If this
//  endpoint is authenticated, or throws, or fails to answer, the whole readiness gate fails with it
//  (constraint C-J).
//
//  ------------------------------------------------------------------------------------------------
//  DECISION RECORD (constraint C-K). Each entry states a decision, its reason and its evidence, so
//  that none of the deliberate omissions below is later "completed" by a helpful reader.
//  ------------------------------------------------------------------------------------------------
//
//  1. WHY IT IS ANONYMOUS, AND WHY .AllowAnonymous() IS CALLED EXPLICITLY (constraint C-G).
//     /health is one of exactly three anonymous routes on this service - this probe and the two
//     `/.well-known/*` publications - in a system where every other boundary is authenticated. The
//     reason is mechanical rather than a convenience: a readiness probe must be reachable before any
//     token exists. The caller that probes it - a container orchestrator, a load balancer, an operator
//     - holds no token, and it probes precisely during the window in which this service is still
//     starting.
//     ON THIS SERVICE THE CIRCULARITY IS TOTAL, WHICH IS WHY THE DECISION IS NOT MERELY UNIFORM BUT
//     UNAVOIDABLE. Security is the SOLE token issuer. Requiring a token to answer its own readiness
//     probe would mean the probe could not succeed until issuance was already live - and issuance
//     going live is the very thing the probe exists to gate. No cold start could ever resolve it.
//     .AllowAnonymous() is therefore called EXPLICITLY rather than left implicit in the absence of a
//     policy. Program.cs installs a fallback authorization policy requiring an authenticated user, so
//     on this service omitting a policy here would CLOSE this route rather than open it, and the
//     readiness chain would fail with a 401 that looked like a configuration error. Written
//     explicitly, the intent is legible at the declaration and survives any later tightening of that
//     fallback. The authored contract states the same requirement structurally:
//     shared/PowerFramework.Contracts/OpenApi/security.v1.yaml overrides the document level bearer
//     requirement on this one operation with an EMPTY requirement list.
//
//  2. WHY IT DISCLOSES NOTHING (constraints C-G and C-F), AND WHY THAT MATTERS MOST HERE.
//     Because the endpoint is anonymous, its body is world readable by anything that can reach port
//     5104 - and 5104 belongs to the one service that holds the system's single signing secret. A leak
//     here would be both unconditional and maximally costly. The report therefore carries an overall
//     status token, this service's own name, the time the report was produced, and A CLOSED
//     TWO-MEMBER VOCABULARY of component identifiers - `self` and `components` - each with a coarse
//     verdict.
//     IT CARRIES NO KEY MATERIAL, no key identifier, no signing algorithm name, no issuer, no
//     audience, no keyRef, no key store path or prefix, no certificate, no environment variable value,
//     no configuration value, no host name, no other service's port, no exception text and no stack
//     trace. Configuration/SecurityOptions.cs is deliberately not referenced from this file at all:
//     the safest way to guarantee a configuration value cannot reach an anonymous body is to have no
//     access to one.
//     EVERY VALUE IN THE BODY IS AUTHORED IN THIS FILE. NOTHING A REGISTRATION SUPPLIED REACHES IT,
//     AND THAT IS A CORRECTION RATHER THAN A REFINEMENT. An earlier form of this file echoed each
//     registered check's own Name and Description, on the reasoning that the authored contract models
//     those members and asks their authors to keep them free of internal detail. That request is a
//     rule on the AUTHOR of each registration, which is not a control this file can enforce - so a
//     check named for a key vault host, or a description carrying a provider name, a URL, a key
//     identifier, an exception summary or a configuration hint, would have been published to anything
//     able to reach the port. The vocabulary is closed here instead, which IS enforceable. Component
//     checks are aggregated into ONE entry rather than reported individually, because the count of
//     registered checks is itself the shape of this service's dependency graph.
//     Three shared framework members are available on every check result and all three are
//     deliberately dropped rather than projected: Exception (an exception message is not authored for
//     disclosure and routinely carries paths, addresses and identifiers), Data (an arbitrary bag a
//     check may fill with anything at all) and Duration (see item 6).
//     The per-check detail is NOT lost. Each check's name and verdict go to the OPERATOR channel,
//     whose audience is authenticated by having access to this service's logs rather than by being
//     able to reach a port. Descriptions, exceptions and data members are excluded from that record
//     too, because a log is not exempt from C-F merely because its reader is trusted.
//
//  3. WHY IT AGGREGATES NOTHING. THIS IS NOT AN OVERSIGHT. DO NOT "FIX" IT.
//     Security is a LEAF. It is called BY Gateway, DataServices and Persistence and calls none of
//     them, so there is no upstream to report on and no symmetry with Gateway's probe to restore.
//     Contract C-10 and docs/ARCHITECTURE.md 4.2 assign upstream aggregation to GATEWAY ALONE, whose
//     aggregate names each of its three upstreams with its own state so an operator can tell WHICH one
//     is not ready. This file therefore injects no message-sending client of any kind - no factory, no
//     typed client, no channel, no generated stub and no other cross-service reference (constraint
//     C-A) - and adds no "for symmetry" fan-out loop. It reports THIS PROCESS ONLY, which is exactly
//     what the authored leaf contract says a non Gateway service's report is: "the readiness of this
//     service only ... never an aggregate".
//
//  4. WHY READINESS NEVER GATES STARTUP, AND WHY THE HANDLER CANNOT THROW (constraint C-J).
//     The host must start even when a component is unusable and simply report its state. FAIL FAST
//     BELONGS TO STARTUP, NOT HERE: Program.cs validates its configuration as the host is built, and a
//     structural fault terminates the process there. By the time this handler runs, that validation
//     has already passed or has already killed the process, so re-running structural validation from
//     inside the probe would duplicate a decision that was made earlier and could only disagree with
//     it. A probe that faulted while a component was down would take the container with it and the
//     readiness gate could never recover. So the handler returns a STATUS rather than throwing, and
//     every failure path inside it - including a readiness check that throws out of the shared
//     framework's own evaluation - is caught, logged and reported as Unhealthy. The one exception
//     deliberately allowed to propagate is OperationCanceledException, which means the caller
//     disconnected: there is no longer a response to write, and swallowing it would only produce a
//     second failure while trying.
//     THE LEGACY ANALOGUE IS REFERENCE ONLY AND IS NOT REPRODUCED HERE. ws_objects/pfw.pbl.src/
//     pfw.sra:L111-L143 unpacks a seven field assert payload split on a carriage return line feed pair
//     and then executes HALT CLOSE at :L143, the framework's fail fast posture in its original form.
//     That posture is real and is preserved - in Gateway's Diagnostics/SystemErrorHandler.cs, which
//     owns it, and in startup validation. This endpoint REPORTS unhealthy; it never terminates the
//     process, and not one line of pfw.sra is ported into this file. The legacy tree is read only and
//     is cited here purely as provenance (constraint C-C).
//
//  5. WHY THE HEALTH CHECK ABSTRACTIONS COME FROM THE SHARED FRAMEWORK AND NOTHING IS ADDED.
//     Microsoft.Extensions.Diagnostics.HealthChecks, its Abstractions assembly and
//     Microsoft.AspNetCore.Diagnostics.HealthChecks all ship inside the Microsoft.AspNetCore.App
//     shared framework, so HealthCheckService, HealthStatus, HealthReport and HealthReportEntry are
//     reachable with ZERO package references. PowerFramework.Security.csproj declares exactly four
//     package references - the OpenAPI document generator, the pinned OpenAPI object model, the
//     inbound bearer handler and the token minting library - and its own comment records that a health
//     check package is not among them for precisely this reason. Adding a fifth would be a build policy
//     violation rather than a convenience and would breach the single sourced central package version
//     rule as well.
//     The route is mapped with MapGet rather than the framework's MapHealthChecks for one specific
//     reason: MapHealthChecks resolves HealthCheckService as a REQUIRED service and throws while the
//     endpoint is being built if AddHealthChecks was never called. Program.cs owns registration, so
//     binding this probe's liveness to a registration it does not own would be precisely the startup
//     gating item 4 forbids. Resolving the service OPTIONALLY, as the handler below does, honours
//     whatever Program.cs registered while remaining correct when it registered nothing.
//
//  6. THE PROBE IS CHEAP, AND DELIBERATELY DOES NO CRYPTOGRAPHIC WORK.
//     It never resolves the signing key layer, never reads or dereferences a signing key, never mints,
//     signs, verifies, derives, encrypts or decrypts anything, never resolves a keyRef against the key
//     store, calls nothing in the Crypto folder beside this one, opens no file and makes no outbound
//     network call. Gateway's readiness is gated on this answer through the orchestration health
//     condition, so a probe that performed key operations would make the single most important
//     readiness property in the orchestration depend on the most expensive and most sensitive path in
//     the system - and would turn a readiness probe into a liveness hazard.
//     NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE (Agent Action Plan 0.8.5). The
//     repository publishes no service level agreement, no latency budget, no throughput target and no
//     availability commitment, so none may be stated, implied or used to justify a decision here.
//     "Cheap" above is a statement about which code paths are NOT touched, not a timing claim. That is
//     also why each check's measured Duration is not projected onto the wire: publishing it
//     anonymously would invite exactly the objective that does not exist.
//
//  7. WHY ONLY Healthy IS 200, AND WHY Degraded IS 503 ALONGSIDE Unhealthy. Contract C-10 requires
//     the response to distinguish "not ready" from "unhealthy", because a service still completing its
//     startup validation is not the same as one whose component has failed and an operator must be able
//     to tell them apart. THAT DISTINCTION LIVES IN THE `status` MEMBER, WHICH KEEPS ALL THREE TOKENS -
//     it does not live in the status code, and an earlier form of this file put it there by answering
//     200 for Degraded as well as Healthy.
//     THAT WAS WRONG, AND THE REASON IS MECHANICAL RATHER THAN A MATTER OF TASTE. This endpoint is
//     what `depends_on: condition: service_healthy` waits on, and what an orchestrator observes is the
//     STATUS CODE. `Degraded` means NOT READY. Answered 200, a service that had just said it was not
//     ready would satisfy the gate, Gateway would start before its upstreams were usable, and the
//     single most important readiness property in the orchestration would be silently defeated - while
//     every document still claimed it held. The earlier reasoning was that a probe tearing down on any
//     non-2xx must not kill a service that is merely still starting; that concern is real and belongs
//     to a LIVENESS probe, which is a different question from readiness and is not what this path
//     answers.
//     So Healthy is 200; Degraded, Unhealthy and any status this build does not recognise are 503,
//     with the verdict named in the body so the three remain distinguishable. Testing FOR the one ready
//     state rather than against the failed ones is what makes an unrecognised value fail CLOSED.
//     The authored contract states it in the same terms: security.v1.yaml's /health operation says
//     "Only `Healthy` is answered with 200", and docs/ARCHITECTURE.md 4.2 repeats it for all four
//     services. Where that document and docs/CONTRACTS.md ever disagree about anything on the wire,
//     THE DOCUMENT WINS.
//
//  8. WHY THE 503 BODY IS A PROBLEM DOCUMENT WITH A retCode. Both authored OpenAPI specifications in
//     shared/PowerFramework.Contracts/OpenApi answer a failed /health with application/problem+json
//     rather than with a second error schema, name the not-ready component in the problem detail, and
//     carry the legacy PowerFramework return code in the single permitted extension member - the one
//     schema in the document that sets additionalProperties true, because RFC 9457 permits extension
//     members. This file does the same and reuses the legacy vocabulary rather than inventing a
//     parallel one: RetCode.E_RETRY, transcribed from ws_objects/pfw.shared.pbl.src/retcode.sru and
//     documented in shared/PowerFramework.Shared.Kernel/RetCode.cs as "the operation should be
//     retried", is the legacy code whose meaning matches Service Unavailable.
//
//  9. WHY THERE IS NO VERSION MEMBER ON THE BODY. A contract or API version would be safe to disclose,
//     and it is deliberately still omitted. The authored health schema declares additionalProperties
//     false over exactly {status, service, checkedAt, checks} and carries no version member, so adding
//     one would make this service's body the only member of the C-10 family with an extra field - a
//     gratuitous divergence, which constraint C-B forbids as firmly as it forbids removing behaviour.
//     Contract identity is disclosed where it belongs, in the OpenAPI metadata declared on the route
//     below. Recorded here so the omission reads as a decision taken rather than a field forgotten.
//
// 10. WHY THE RESPONSE TYPES ARE NOT NAMED HealthReport AND HealthCheckResult. Those are the names the
//     authored schemas use, and both collide with shared framework types this file imports -
//     Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport and .HealthCheckResult. The wire types
//     are therefore ServiceHealthReport and ServiceHealthCheck. The MEMBER SET is what the contract
//     fixes and it is mirrored exactly; only the schema title differs, and "Service" carries the useful
//     signal that this is one service's own report rather than Gateway's aggregate.
//
// 11. WHY NOTHING HERE TOUCHES THE PHASE 2 CAPABILITIES (constraint C-D). Four of the eight target
//     capability areas are deferred in full - not partially, and not as stubs. Their four reserved not
//     implemented routes are declared on Gateway's Endpoints/DeferredCapabilityEndpoints.cs alone,
//     which together with docs/DEFERRED.md is the only pair of places in the whole refactor where those
//     four areas may be named at all. This file therefore declares no reserved route, implements
//     nothing on their behalf, returns no not-implemented status, throws no not-implemented exception,
//     and does not name them - not in a route, not in a type, not in a status token and not in a
//     comment. Read docs/DEFERRED.md if you need the roster; it is deliberately absent from here,
//     which is why a repository wide search for those four names returns nothing from this file.
//
// 12. WHY THERE IS NO PRESENTATION SURFACE OF ANY KIND. Phase 1 is API and service level only, and the
//     Agent Action Plan records that the Design System Alignment Protocol is not triggered: no
//     component library is named, no design input exists, and the legacy application disables its own
//     theming outright at ws_objects/pfw.pbl.src/pfw.sra:L25. This endpoint therefore emits JSON and
//     problem JSON and nothing else - no HTML, no view, no static asset, no styling.
//
//  LOCATOR CONVENTION: every bare `pfw.sra:L...` in this file means ws_objects/pfw.pbl.src/pfw.sra,
//  the framework application - never the same-named packager object at
//  ws_objects/pfw.pack.pbl.src/pfw.sra (AAP 0.8.6 R7).
// =================================================================================================

using System.Net.Mime;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Maps the anonymous <c>GET /health</c> readiness probe that contract C-10 requires of every service
/// in the decomposition, reporting the readiness of THIS process only.
/// </summary>
/// <remarks>
/// <para>
/// Upstream aggregation is deliberately absent, and on this service there is nothing to aggregate:
/// Security is called by Gateway, DataServices and Persistence and calls none of them. Gateway is the
/// single aggregator, and it reports healthy only after Persistence, DataServices and Security each
/// report healthy. See the decision record at the head of this file - in particular items 1, 2, 3, 6
/// and 7 - before changing anything here, because several of the omissions look like gaps and are not.
/// </para>
/// <para>
/// The class is static and holds no state. It registers no authentication scheme, no options type and
/// no dependency injection service; <c>Program.cs</c> owns host construction, options binding with
/// validate on start, bearer registration and the fallback authorization policy. This file maps one
/// route and nothing more.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>
    /// The route pattern, fixed by the authored contract and by the attached environment's acceptance
    /// criteria (constraint C-L) as the anonymous per service health path on the 5101 to 5105 band.
    /// </summary>
    /// <remarks>
    /// Neither the path nor the port may be renamed or relocated: the container HEALTHCHECK, the
    /// orchestration health condition and every readiness gate name them. The literal is taken from
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> rather than from configuration
    /// because <c>Configuration/SecurityOptions.cs</c> deliberately exposes no health path - it carries
    /// the token, key and metadata settings, none of which this file may read (decision record item 2).
    /// </remarks>
    private const string HealthRoutePattern = "/health";

    /// <summary>
    /// This service's identifier on the wire. Fixed by the authored contract rather than chosen: the
    /// health schema pins <c>service</c> to the constant <c>security</c>, and
    /// <c>OpenApi/gateway.v1.yaml</c>'s upstream health schema closes the reporting service enumeration
    /// at exactly <c>persistence</c>, <c>dataservices</c> and <c>security</c> so that Gateway's
    /// aggregate can name this service individually.
    /// </summary>
    private const string ServiceIdentifier = "security";

    /// <summary>The wire token for a fully ready service. The ONLY verdict reported with HTTP 200.</summary>
    private const string StatusHealthy = "Healthy";

    /// <summary>
    /// The wire token for "not ready, but not failed" - a service still completing startup validation.
    /// Reported with HTTP 503 alongside <see cref="StatusUnhealthy"/>; see decision record item 7.
    /// </summary>
    private const string StatusDegraded = "Degraded";

    /// <summary>The wire token for a failed service. Reported with HTTP 503.</summary>
    private const string StatusUnhealthy = "Unhealthy";

    /// <summary>
    /// The name of this endpoint's own contribution to the report: the statement that the process is
    /// running and answering requests, which is the whole of a leaf service's readiness before any
    /// component check is registered. A stable identifier, never a type name or a file path.
    /// </summary>
    private const string SelfCheckName = "self";

    /// <summary>
    /// The name of the single aggregated entry standing for every registered component check.
    /// </summary>
    /// <remarks>
    /// A FIXED PUBLIC IDENTIFIER, and the second and last member of this endpoint's closed vocabulary.
    /// It is not a registered check's own name and is never derived from one: the anonymous body must
    /// not disclose what a registration chose to call itself, nor how many registrations exist. See
    /// <see cref="ProjectChecks"/> for the full reasoning and <see cref="WriteOperatorRecord"/> for
    /// where the per-check detail goes instead.
    /// </remarks>
    private const string ComponentsCheckName = "components";

    /// <summary>The public note on the aggregated entry when every component check is ready.</summary>
    /// <remarks>
    /// FIXED PROSE, IDENTICAL FOR EVERY OCCURRENCE, so that the member is useful to a reader without
    /// being a channel. It names no component, no provider, no host and no count.
    /// </remarks>
    private const string ComponentsHealthyDescription =
        "Every registered component readiness check reports ready.";

    /// <summary>The public note on the aggregated entry when any component check is not ready.</summary>
    /// <remarks>
    /// It states THAT a component is not ready and never WHICH, for the same reason the unhealthy self
    /// note states that the evaluation failed and never why. The which is on the operator channel.
    /// </remarks>
    private const string ComponentsNotReadyDescription =
        "At least one registered component readiness check is not ready. The component is named in this "
        + "service's operator telemetry and is deliberately not disclosed here.";

    /// <summary>The public note on the self entry when the process is answering requests.</summary>
    private const string SelfHealthyDescription =
        "The service process is running and answering requests.";

    /// <summary>The public note on the self entry when the readiness evaluation itself failed.</summary>
    /// <remarks>
    /// It states THAT the evaluation failed and never WHY. The reason is an exception, the response is
    /// anonymous, and on the service holding the system's single signing secret the two must not meet
    /// (decision record item 2). The reason goes to the log instead.
    /// </remarks>
    private const string SelfEvaluationFailedDescription =
        "The readiness evaluation could not be completed.";

    /// <summary>
    /// The operator-channel record carrying the per-check detail the anonymous body withholds.
    /// </summary>
    /// <remarks>
    /// Three structured fields: the reporting service, the aggregate verdict, and each check's name with
    /// its own verdict. No description, no exception and no arbitrary data member is included - see
    /// <see cref="WriteOperatorRecord"/>. Declared as a constant so that the logging analyzer sees a
    /// compile-time constant message template rather than a computed string.
    /// </remarks>
    private const string OperatorReadinessRecord =
        "Readiness evaluated for the {Service} service: {Status}. Component checks: {ComponentChecks}.";

    /// <summary>
    /// The operator-channel record for a readiness evaluation that failed outright.
    /// </summary>
    /// <remarks>
    /// The exception travels as the log record's exception argument, where an operator can see it, and
    /// is never projected onto the anonymous response.
    /// </remarks>
    private const string OperatorEvaluationFailedRecord =
        "Readiness evaluation failed for the {Service} service; reporting {Status}.";

    /// <summary>The OpenAPI operation identifier, matching the authored contract's own spelling.</summary>
    private const string OperationName = "getHealth";

    /// <summary>The OpenAPI tag, matching the authored contract's grouping.</summary>
    private const string OperationTag = "Health";

    /// <summary>
    /// The single permitted problem document extension member carrying the legacy PowerFramework return
    /// code, spelled as the authored contract spells it.
    /// </summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>
    /// The problem document extension member carrying the readiness verdict, spelled as the authored
    /// contract spells it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A NAME OF ITS OWN, BECAUSE <c>status</c> IS ALREADY TAKEN AND IS A DIFFERENT TYPE. RFC 9457 uses
    /// <c>status</c> for the integer HTTP status and the authored contract types it that way, so the
    /// verdict token cannot occupy that name on a problem document. Without a member of its own the
    /// token would exist only inside the prose of <c>detail</c>, leaving
    /// <see cref="RetCodeExtensionMember"/> as the sole machine-readable member - and that carries
    /// <c>E_RETRY</c> for <c>Degraded</c> and for <c>Unhealthy</c> alike, so Gateway's aggregator could
    /// not tell a service still starting up from one whose dependency has failed and would flatten both
    /// into a failure.
    /// </para>
    /// <para>
    /// The same spelling is emitted by all four services, because contract C-10 publishes one readiness
    /// shape across the estate and Gateway reads this member off each upstream's not-ready body.
    /// </para>
    /// </remarks>
    private const string ServiceStatusExtensionMember = "serviceStatus";

    /// <summary>
    /// The problem document title for the 503. Stated explicitly rather than left to the framework's
    /// defaults table, which covers the common 4xx statuses and 500 but not 503, so an omitted title
    /// would simply be absent from the body.
    /// </summary>
    private const string UnavailableProblemTitle = "Service Unavailable";

    /// <summary>
    /// The logger category. A constant string rather than <c>ILogger&lt;T&gt;</c> because <c>T</c> cannot
    /// be a static class, and this class is deliberately static. It is also the selector a test uses to
    /// separate this endpoint's operator record from the framework's own health-check record.
    /// </summary>
    private const string LoggerCategoryName = "PowerFramework.Security.Endpoints.HealthEndpoints";

    /// <summary>The summary published on the operation, matching the authored contract verbatim.</summary>
    private const string OperationSummary = "Report this service's own readiness. Anonymous.";

    /// <summary>
    /// The description published on the operation. It restates the four properties a consumer must know
    /// - anonymous deliberately, never an aggregate, which verdict earns which status code, and the
    /// disclosure boundary - and asserts nothing about how quickly or how often the endpoint answers,
    /// because no such objective is published anywhere in the repository (Agent Action Plan 0.8.5).
    /// </summary>
    private const string OperationDescription = """
        Reports the readiness of **this service only** (contract C-10, readiness half).

        **Anonymous deliberately, and on this service unavoidably so.** A readiness probe must be
        reachable before any token exists: the caller that probes it - a container orchestrator, a load
        balancer, an operator - holds no token, and it probes precisely during the window in which the
        service is still starting. Security is the sole token issuer, so requiring a token here would
        mean the probe could not succeed until issuance was already live - and issuance going live is
        the very thing the probe exists to gate, a circular dependency no cold start can resolve.
        `/health` is therefore anonymous on all four services, and that uniformity is part of contract
        C-10 rather than a per-service choice. Every other operation on this service authenticates its
        caller.

        **Never an aggregate.** This report covers this process only. Security is called by Gateway,
        DataServices and Persistence and calls none of them, so it has no upstream to report on.
        Gateway is the single aggregator: it reports healthy only after Persistence, DataServices and
        Security each report healthy, and its aggregate names each upstream with its own state so an
        operator can tell which one is not ready. The ordering is expressed in the orchestration
        manifest as a dependency condition on upstream health.

        **The probe performs no cryptographic work.** It never reads or dereferences a signing key,
        never mints, signs or verifies anything, and never resolves a key reference against the key
        store. Readiness is gated on this answer, so a probe that touched the signing path would make
        the orchestration's readiness gate depend on the most sensitive path in the system.

        **"Not ready" and "unhealthy" are different states, and the difference is in the body rather
        than in the status code.** Only `Healthy` is answered with 200. `Degraded` means *not ready*
        and is answered with 503 alongside `Unhealthy`, because the orchestration readiness gate
        observes the status code - a not-ready service answering 200 would open that gate early. The
        `status` member keeps all three tokens, so an operator can still tell a service completing its
        startup validation from one whose component has failed. A 503 carries a problem document naming
        the not-ready component and the legacy PowerFramework return code.

        **The body is public, and is scoped accordingly.** Every value in it is authored by this
        service: the component identifiers are a closed vocabulary - `self` and `components` - rather
        than the names whatever registered a check chose for it, and component checks are aggregated
        into one entry so that the number of them is not disclosed either. The body carries no key
        material, no key identifier, no signing algorithm, no issuer, no audience, no key reference, no
        credential, no certificate, no configuration value, no host name, no other service's port, no
        exception text and no stack trace. The per-component detail is available to an operator through
        this service's own telemetry.
        """;

    /// <summary>
    /// Maps <c>GET /health</c> onto the supplied route builder as an anonymous readiness probe.
    /// </summary>
    /// <param name="app">
    /// The route builder to map onto. Supplied by <c>Program.cs</c>, which owns the host.
    /// </param>
    /// <returns>
    /// The same <paramref name="app"/>, so that endpoint registrations may be chained by the caller.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="app"/> is <see langword="null"/>. This is the ONLY throw in the file
    /// and it is a programming error at composition time, not a request time failure: the request
    /// handler itself never throws, because a probe that faulted would take the container with it and
    /// the readiness gate could never recover (decision record item 4).
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHY THIS ROUTE IS ANONYMOUS AND WHY THAT IS DECLARED HERE RATHER THAN INFERRED (constraint C-K,
    /// decision record item 1). The container definition's <c>HEALTHCHECK</c> targets this route, and
    /// the orchestration manifest gates Gateway behind Persistence, DataServices and Security with
    /// <c>depends_on</c> using a <c>service_healthy</c> condition - the readiness property that ruled
    /// out the alternative orchestration approach recorded in <c>docs/ARCHITECTURE.md</c> 10.4. The
    /// thing that performs both probes holds no token and probes while the service is still starting.
    /// On the sole token issuer the dependency is circular as well: a probe requiring a token could not
    /// answer until issuance was live, and issuance is what the probe gates. <c>Program.cs</c> installs
    /// a fallback authorization policy requiring an authenticated user, so the explicit
    /// <c>AllowAnonymous()</c> call below is what keeps this route open; omitting a policy would close
    /// it, and the readiness chain would fail with a 401 that read as a configuration error.
    /// </para>
    /// <para>
    /// WHY THE PROBE IS CHEAP (decision record item 6). Because readiness is gated on it, the handler
    /// touches no signing key, performs no cryptographic operation, resolves no key reference, opens no
    /// file and makes no outbound call. No timing claim is made or implied; the repository publishes no
    /// performance objective of any kind.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthRoutePattern, ReportReadinessAsync)
           // Explicit, and load bearing. See decision record item 1: this survives the fallback
           // authorization policy Program.cs installs, whereas merely omitting a policy would close the
           // route rather than open it. It is one of exactly three documented anonymous exceptions on
           // this service.
           .AllowAnonymous()
           .WithName(OperationName)
           .WithTags(OperationTag)
           .WithSummary(OperationSummary)
           .WithDescription(OperationDescription)
           // Exactly two declared responses, matching the authored contract: the report on 200 and a
           // problem document on 503. The handler returns IResult rather than a typed result union for
           // precisely this reason - a union would have the framework infer an additional 500 from
           // ProblemHttpResult's own metadata, publishing a response this operation cannot produce.
           .Produces<ServiceHealthReport>(
               StatusCodes.Status200OK,
               MediaTypeNames.Application.Json)
           .ProducesProblem(
               StatusCodes.Status503ServiceUnavailable,
               MediaTypeNames.Application.ProblemJson);

        return app;
    }


    /// <summary>
    /// Evaluates this process's own readiness and projects it onto the wire.
    /// </summary>
    /// <param name="httpContext">
    /// The request context, used ONLY to reach the request scoped service provider. Nothing about the
    /// request influences the verdict, and nothing about the request is echoed back - no header, no
    /// query value, no presented credential and no caller identity.
    /// </param>
    /// <param name="loggerFactory">
    /// Resolved by the framework from the host's own logging abstractions. Used exclusively for the
    /// operator channel: the per-check detail the anonymous body withholds, and a readiness evaluation
    /// that failed outright - which is otherwise invisible, because it is reported to the caller as a
    /// status rather than raised as an exception.
    /// </param>
    /// <param name="cancellationToken">
    /// The request's abort token, so a disconnected caller stops the evaluation rather than leaving it
    /// running against a response nobody will read.
    /// </param>
    /// <returns>
    /// HTTP 200 carrying a <see cref="ServiceHealthReport"/> when the verdict is <c>Healthy</c>; HTTP
    /// 503 carrying a problem document when it is <c>Degraded</c>, <c>Unhealthy</c> or anything
    /// unrecognised - <c>Degraded</c> included, because it means not ready and the orchestration gate
    /// reads the status code (decision record item 7).
    /// </returns>
    /// <remarks>
    /// <para>
    /// A NAMED METHOD RATHER THAN AN INLINE LAMBDA, DELIBERATELY. The verdict mapping is the property
    /// the whole readiness chain rests on, so it has to be reachable by a test on its own terms rather
    /// than only through whatever the registration happens to close over. It also keeps
    /// <see cref="MapHealthEndpoints"/> a declaration of the route's shape and nothing else.
    /// </para>
    /// <para>
    /// NO CRYPTOGRAPHIC WORK HAPPENS ANYWHERE BELOW (decision record item 6). The only two services
    /// resolved are the optional health check registry and the optional clock; neither the signing key
    /// layer, nor any provider in the <c>Crypto</c> folder, nor the key store, nor
    /// <c>Configuration/SecurityOptions</c> is touched, and no file is opened and no outbound call made.
    /// </para>
    /// <para>
    /// FAIL FAST IS NOT RE-RUN HERE (decision record item 4). Startup validation in <c>Program.cs</c>
    /// has already passed or has already terminated the process by the time this runs, exactly as the
    /// legacy framework's own posture at <c>ws_objects/pfw.pbl.src/pfw.sra:L111-L143</c> terminates on a
    /// decoded assertion failure. This handler reports; it never validates structure and never halts.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReportReadinessAsync(
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        HealthStatus status;
        IReadOnlyList<ServiceHealthCheck> checks;

        try
        {
            // OPTIONAL resolution, deliberately. Program.cs owns registration, and this probe must
            // answer whether or not it registered any health check - see decision record item 5. The
            // framework's own MapHealthChecks would instead treat the service as required and throw
            // while the endpoint was being built, turning a probe into a startup gate.
            HealthCheckService? healthCheckService =
                httpContext.RequestServices.GetService<HealthCheckService>();

            if (healthCheckService is null)
            {
                // No health check registry at all, so the whole of this leaf service's readiness is the
                // statement that its process is running and answering requests - which the execution of
                // this handler has just demonstrated. That is a truthful Healthy, not an assumed one.
                status = HealthStatus.Healthy;
                checks = [BuildSelfCheck(HealthStatus.Healthy)];
            }
            else
            {
                HealthReport report = await healthCheckService
                    .CheckHealthAsync(cancellationToken)
                    .ConfigureAwait(false);

                status = report.Status;
                checks = ProjectChecks(report);

                // THE OPERATOR CHANNEL, WHICH IS WHERE THE DETAIL BELONGS (decision record item 2). The
                // response above carries only this file's own fixed public vocabulary, so the
                // registration-supplied check names - the thing an operator actually needs in order to
                // find a failing component - would otherwise be lost entirely. They are recorded here
                // instead, where the audience is authenticated by having access to this service's logs
                // rather than by being able to reach a port.
                WriteOperatorRecord(loggerFactory, report);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Decision record item 4: the handler REPORTS rather than throws, because a probe that
            // faulted would take the container with it and the readiness gate could never recover. The
            // exception is logged HERE, where an operator can see it, and is deliberately NOT projected
            // onto the anonymous response (item 2) - which matters more on this service than on any
            // other, because an exception raised while evaluating readiness could name a key store, a
            // key identifier or a configuration path. OperationCanceledException is excluded from this
            // catch on purpose: it means the caller disconnected, so there is no longer a response to
            // write and attempting one would only fail again.
            loggerFactory
                .CreateLogger(LoggerCategoryName)
                .LogError(
                    exception,
                    OperatorEvaluationFailedRecord,
                    ServiceIdentifier,
                    StatusUnhealthy);

            status = HealthStatus.Unhealthy;
            checks = [BuildSelfCheck(HealthStatus.Unhealthy)];
        }

        // Fails CLOSED: ONLY Healthy is reported as ready. Degraded means "not ready, but not failed",
        // and the thing that reads this endpoint reads the STATUS CODE, so a not-ready service that
        // answered 200 would satisfy the orchestrator's health condition and open the dependency gate
        // early - which is the one ordering property the gate exists to enforce (decision record item
        // 7). Testing FOR the single ready state rather than against the failed ones is what makes an
        // unrecognised status fail closed as well.
        if (status is not HealthStatus.Healthy)
        {
            return BuildNotReadyProblem(status, checks);
        }

        // The clock is a substitutable seam rather than a direct read, because the Agent Action Plan's
        // characterization model requires every clock read to be maskable from both the master and the
        // candidate recording. Resolving TimeProvider optionally honours the deterministic double a test
        // host registers and needs no registration of its own when the host registers nothing - which
        // keeps this probe independent of a composition it does not own (decision record item 5).
        TimeProvider timeProvider =
            httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

        return TypedResults.Ok(
            new ServiceHealthReport(
                ToWireStatus(status),
                ServiceIdentifier,
                timeProvider.GetUtcNow(),
                checks));
    }

    /// <summary>
    /// Projects a shared framework health report onto the wire shape the authored contract fixes, using
    /// ONLY this file's fixed public component vocabulary.
    /// </summary>
    /// <param name="report">The evaluated report.</param>
    /// <returns>
    /// Two entries at most, both named from <see cref="SelfCheckName"/> and
    /// <see cref="ComponentsCheckName"/>: this endpoint's own statement that the process is answering,
    /// and - when any component check is registered - one aggregated entry standing for all of them.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NOTHING A REGISTRATION SUPPLIED CROSSES THIS BOUNDARY, AND THAT IS THE WHOLE POINT OF THE METHOD
    /// (decision record item 2). An earlier form of this projection echoed each registered check's own
    /// <c>Name</c> and <c>Description</c>. Both are supplied by whatever registered the check rather
    /// than authored for disclosure, and this endpoint is ANONYMOUS on the service that holds the
    /// system's single signing secret - so a name identifying a key vault or a description carrying a
    /// provider name, a host, a URL, a key identifier, an exception summary or a configuration hint
    /// would have been published to anything able to reach the port. The contract's requirement that
    /// check names stay free of internal detail is a rule on the AUTHOR of each registration, which is
    /// not a control this file can enforce; a closed vocabulary declared here is.
    /// </para>
    /// <para>
    /// The names are therefore two fixed constants, and their <c>Status</c> is the only thing that
    /// varies. Aggregating every component check into one entry is deliberate: reporting the COUNT of
    /// registered checks, or one anonymous entry per check, would leak the shape of this service's
    /// dependency graph to an unauthenticated caller - which is the same disclosure in a different unit.
    /// The per-check detail is not lost; it goes to the operator channel through
    /// <see cref="WriteOperatorRecord"/>.
    /// </para>
    /// <para>
    /// An EMPTY report yields the self entry alone rather than an aggregated entry over zero checks,
    /// because a <c>components</c> verdict about a dependency graph with no members would be a claim
    /// about nothing. That is a real registration state, not a theoretical one: a composition root that
    /// enables the health check registry before any component check exists produces exactly it.
    /// </para>
    /// <para>
    /// The report entry's <c>Exception</c>, <c>Data</c> and <c>Duration</c> members are dropped for the
    /// reasons in decision record items 2 and 6.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ServiceHealthCheck> ProjectChecks(HealthReport report)
    {
        ServiceHealthCheck self = BuildSelfCheck(HealthStatus.Healthy);

        if (report.Entries.Count == 0)
        {
            return [self];
        }

        // The worst status across every registered check, computed here rather than taken from
        // report.Status: the report's own aggregate can be shaped by a registration's result predicate,
        // whereas this entry stands for the checks themselves and must say what they said. HealthStatus
        // is ordered worst first - Unhealthy is 0, Degraded is 1, Healthy is 2 - so the worse of two is
        // the smaller value.
        HealthStatus worst = HealthStatus.Healthy;

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            if (entry.Value.Status < worst)
            {
                worst = entry.Value.Status;
            }
        }

        return
        [
            self,
            new ServiceHealthCheck(ComponentsCheckName, ToWireStatus(worst))
            {
                Description = worst == HealthStatus.Healthy
                    ? ComponentsHealthyDescription
                    : ComponentsNotReadyDescription,
            },
        ];
    }

    /// <summary>
    /// Records the per-check detail on the operator channel, which is the audience it is authored for.
    /// </summary>
    /// <param name="loggerFactory">The host's logging abstraction.</param>
    /// <param name="report">The evaluated report.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS: the anonymous response deliberately carries a closed vocabulary, so without this
    /// record the one piece of information an operator needs from a failed probe - WHICH component is
    /// not ready - would exist nowhere. The split is the same one the system applies to an unhandled
    /// fault: the caller learns the class of failure, the operator learns the detail.
    /// </para>
    /// <para>
    /// WHAT IS RECORDED AND WHAT IS NOT. Each check's NAME and STATUS travel; its <c>Description</c>,
    /// <c>Exception</c> and <c>Data</c> do NOT. A name is an identifier the registration chose and is
    /// safe to hold in this service's own logs, whereas a description or an exception message is free
    /// text that routinely carries a connection string, an address, a key identifier or a file path -
    /// and a log record is not exempt from that concern merely because its reader is trusted (constraint
    /// C-F). The names are joined into one field rather than logged per check so that a probe polled
    /// continuously by an orchestrator produces one record per evaluation.
    /// </para>
    /// <para>
    /// The level is chosen from the verdict: a not-ready evaluation is a warning an operator should see,
    /// while a healthy one is trace level so that continuous probing does not fill a log with records
    /// that say nothing happened. The level test runs BEFORE the names are joined, so a disabled channel
    /// costs no allocation.
    /// </para>
    /// </remarks>
    private static void WriteOperatorRecord(ILoggerFactory loggerFactory, HealthReport report)
    {
        ILogger logger = loggerFactory.CreateLogger(LoggerCategoryName);

        bool ready = report.Status == HealthStatus.Healthy;

        if (!ready ? !logger.IsEnabled(LogLevel.Warning) : !logger.IsEnabled(LogLevel.Trace))
        {
            return;
        }

        List<string> named = new(report.Entries.Count);

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            named.Add(string.Concat(entry.Key, "=", ToWireStatus(entry.Value.Status)));
        }

        string detail = string.Join(", ", named);

        if (ready)
        {
            logger.LogTrace(OperatorReadinessRecord, ServiceIdentifier, ToWireStatus(report.Status), detail);
        }
        else
        {
            logger.LogWarning(OperatorReadinessRecord, ServiceIdentifier, ToWireStatus(report.Status), detail);
        }
    }

    /// <summary>
    /// Builds this endpoint's own contribution to the report.
    /// </summary>
    /// <param name="status">
    /// <see cref="HealthStatus.Healthy"/> when the process is answering requests, or
    /// <see cref="HealthStatus.Unhealthy"/> when the readiness evaluation itself could not be completed.
    /// </param>
    /// <returns>The <c>self</c> entry.</returns>
    /// <remarks>
    /// Both descriptions are fixed prose authored in this file. The unhealthy one states THAT the
    /// evaluation failed and never WHY: the reason is an exception, the response is anonymous, and on
    /// this service the two must not meet (decision record item 2). The reason is in the log instead.
    /// </remarks>
    private static ServiceHealthCheck BuildSelfCheck(HealthStatus status)
    {
        string description = status == HealthStatus.Healthy
            ? SelfHealthyDescription
            : SelfEvaluationFailedDescription;

        return new ServiceHealthCheck(SelfCheckName, ToWireStatus(status)) { Description = description };
    }

    /// <summary>
    /// Builds the 503 problem document for any verdict that is not fully ready.
    /// </summary>
    /// <param name="status">
    /// The evaluated status, which is <see cref="HealthStatus.Degraded"/>,
    /// <see cref="HealthStatus.Unhealthy"/>, or a value this build does not recognise.
    /// </param>
    /// <param name="checks">The projected checks, from which the not-ready names are taken.</param>
    /// <returns>A problem document result carrying the legacy return code.</returns>
    /// <remarks>
    /// <para>
    /// The authored contract requires the not-ready component to be named in the problem detail and the
    /// legacy PowerFramework return code to travel in the single permitted extension member, rather than
    /// a second error schema being defined for this one response (decision record item 8). Every name
    /// that reaches this method comes from the fixed public component vocabulary this file declares, so
    /// it is an identifier this file chose rather than one a registration supplied; no description,
    /// exception, key identifier or configuration value is carried here (decision record item 2).
    /// </para>
    /// <para>
    /// DEGRADED AND UNHEALTHY BOTH ARRIVE HERE AND ARE DISTINGUISHED IN THE BODY RATHER THAN IN THE
    /// STATUS. Both are answered 503, because both mean "not ready" and the orchestration gate reads the
    /// status code. The wire status token is written into the detail for a human AND into
    /// <see cref="ServiceStatusExtensionMember"/> for a machine, so an operator can tell a service still
    /// completing startup validation from one whose component has failed - and so can Gateway's
    /// aggregator, which reads that member off this body and would otherwise have only
    /// <see cref="RetCodeExtensionMember"/>, which is identical for both verdicts. The token cannot
    /// travel in a <c>status</c> member: RFC 9457 uses that name for the integer HTTP status, and this
    /// response is a problem document rather than a <see cref="ServiceHealthReport"/>.
    /// </para>
    /// </remarks>
    private static IResult BuildNotReadyProblem(
        HealthStatus status,
        IReadOnlyList<ServiceHealthCheck> checks)
    {
        string wireStatus = ToWireStatus(status);

        List<string> notReady = [];

        foreach (ServiceHealthCheck check in checks)
        {
            if (!string.Equals(check.Status, StatusHealthy, StringComparison.Ordinal))
            {
                notReady.Add(check.Name);
            }
        }

        // The empty case is reachable and is handled rather than assumed away: an aggregate can be
        // Degraded or Unhealthy while every individual entry reports Healthy, for instance under a
        // registration whose own result predicate degrades the whole on a condition no single entry
        // reports.
        string detail = notReady.Count == 0
            ? "The Security service is not ready (" + wireStatus + ")."
            : "The Security service is not ready (" + wireStatus + "). Readiness check(s) not reporting "
              + StatusHealthy + ": " + string.Join(", ", notReady) + ".";

        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: UnavailableProblemTitle,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ServiceStatusExtensionMember] = wireStatus,
                [RetCodeExtensionMember] = RetCode.E_RETRY,
            });
    }

    /// <summary>
    /// Maps a shared framework status onto the closed three token vocabulary the contract publishes.
    /// </summary>
    /// <param name="status">The status to translate.</param>
    /// <returns>One of <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</returns>
    /// <remarks>
    /// Translated through an explicit switch rather than by calling the enumeration's own string
    /// conversion, so that the published vocabulary is stated in this file and cannot drift silently if a
    /// future framework version renames or adds a member. An unrecognised status maps to
    /// <c>Unhealthy</c>, which is the same fail closed choice the status code mapping makes.
    /// </remarks>
    private static string ToWireStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusHealthy,
        HealthStatus.Degraded => StatusDegraded,
        HealthStatus.Unhealthy => StatusUnhealthy,
        _ => StatusUnhealthy,
    };
}

/// <summary>
/// The readiness of THIS service only, as published on <c>GET /health</c>.
/// </summary>
/// <param name="Status">
/// The overall verdict, carrying the distinction contract C-10 requires between "not ready" and
/// "unhealthy": <c>Healthy</c> is fully ready and is the ONLY verdict reported with 200; <c>Degraded</c>
/// is not ready but not failed, for instance a service still completing its startup validation;
/// <c>Unhealthy</c> has failed. The latter two are both reported with 503, because both mean not ready
/// and the orchestration readiness gate observes the status code - this member is what keeps them
/// distinguishable.
/// </param>
/// <param name="Service">
/// The reporting service, so that Gateway's aggregate can name each upstream individually rather than
/// returning one opaque verdict. Always <c>security</c> on this service, which the authored schema pins
/// as a constant rather than leaving open.
/// </param>
/// <param name="CheckedAt">
/// When this report was produced, so a consumer can detect a stale cached report. Read through a
/// substitutable clock so that a characterization recording can mask it on both sides of a comparison.
/// </param>
/// <param name="Checks">
/// The components behind the verdict, named from a CLOSED public vocabulary authored by this service -
/// never from a registration's own check name, and never one entry per registered check. See
/// <c>HealthEndpoints.ProjectChecks</c>: an anonymous body must disclose neither what a registration
/// called itself nor how many registrations exist, and the per-check detail an operator needs is carried
/// on this service's operator telemetry instead.
/// </param>
/// <remarks>
/// <para>
/// This shape mirrors the authored leaf service health schema in
/// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> member for member. It is deliberately
/// NOT an aggregate: Security is called by Gateway, DataServices and Persistence and calls none of them,
/// so it has no upstream to report on. Gateway alone combines this report with those of Persistence and
/// DataServices, and reports healthy only after all three do.
/// </para>
/// <para>
/// Because <c>GET /health</c> is anonymous, everything in this record is public - and this is the service
/// that holds the system's single signing secret. It therefore carries no key material, no key
/// identifier, no signing algorithm, no issuer, no audience, no key reference, no key store path or
/// prefix, no credential, no certificate, no environment variable value, no configuration value, no host
/// name, no other service's port, no exception text and no stack trace, and no member of it can be used
/// to enumerate internal topology.
/// </para>
/// <para>
/// All four members are declared REQUIRED, and that is a deliberate strengthening rather than a
/// divergence. The authored contract requires only the status and the service, leaving the timestamp and
/// the check list optional so that a leaner report is permitted; this service always populates all four,
/// so declaring them required states what it actually sends. A consumer written against the looser
/// family schema is unaffected, because a stricter guarantee on an existing member cannot break it -
/// whereas declaring an always present member optional would understate the payload.
/// </para>
/// </remarks>
public sealed record ServiceHealthReport(
    string Status,
    string Service,
    DateTimeOffset CheckedAt,
    IReadOnlyList<ServiceHealthCheck> Checks);

/// <summary>
/// One component check contributing to a <see cref="ServiceHealthReport"/>.
/// </summary>
/// <param name="Name">
/// The component's stable identifier, drawn from this service's own closed public vocabulary - never a
/// registration-supplied check name, a file path, a provider type name, a key identifier or any other
/// internal detail.
/// </param>
/// <param name="Status">
/// This check's own verdict, using the same three tokens as the overall report.
/// </param>
public sealed record ServiceHealthCheck(
    string Name,
    string Status)
{
    /// <summary>
    /// An optional human readable note. FIXED PROSE authored in this file, identical for every occurrence
    /// of a given verdict: it carries no configuration value, no key material, no component name and no
    /// count, and never an exception message or a stack trace.
    /// </summary>
    /// <remarks>
    /// DECLARED AS A PROPERTY WITH AN OMIT WHEN NULL RULE RATHER THAN AS A THIRD POSITIONAL PARAMETER,
    /// AND BOTH HALVES OF THAT ARE DELIBERATE. As a positional parameter the OpenAPI generator marks it
    /// REQUIRED, because a constructor parameter without a default is required by definition, while the
    /// authored contract in <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> lists only
    /// the name and the status as required and closes the object with additional properties disallowed. A
    /// required nullable member would therefore have been doubly wrong: it diverges from the contract
    /// family, and a body carrying an explicit null for it does not validate against a schema whose type
    /// for it is a plain string. An init only property is not a constructor parameter, so it is generated
    /// as optional, and the serialization rule makes the member ABSENT rather than null when there is no
    /// note - which is exactly what "optional" means on the wire.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}
