// =================================================================================================
//  HealthEndpoints.cs : the anonymous readiness probe for the DataServices service.
//
//  Contract C-10 (health and readiness), readiness half. Served on port 5102 at exactly /health.
//
//  THIS ENDPOINT IS LOAD BEARING INFRASTRUCTURE, NOT DECORATION.
//  It is the probe that orchestration/docker-compose.yml's `depends_on: condition: service_healthy`
//  waits on. Together with the Persistence and Security probes it is what makes Gateway report
//  healthy only after all three of its upstreams do, which docs/ARCHITECTURE.md 4.2 names the most
//  important readiness property in the orchestration and 10.4 records as the single requirement that
//  ruled out the .NET Aspire publisher: `depends_on` with a `service_healthy` condition was not
//  expressible through the generated model. If this endpoint is authenticated, or throws, or fails to
//  answer, the whole readiness gate fails with it (constraint C-J).
//
//  ------------------------------------------------------------------------------------------------
//  DECISION RECORD (constraint C-K). Each entry states a decision, its reason and its evidence, so
//  that none of the deliberate omissions below is later "completed" by a helpful reader.
//  ------------------------------------------------------------------------------------------------
//
//  1. WHY IT IS ANONYMOUS, AND WHY .AllowAnonymous() IS CALLED EXPLICITLY (constraint C-G).
//     /health is the ONE documented anonymous exception in a system where every other boundary is
//     authenticated, and the reason is mechanical rather than a convenience: a readiness probe must
//     be reachable before any token exists. The caller that probes it - a container orchestrator, a
//     load balancer, an operator - holds no token, and it probes precisely during the window in
//     which this service is still starting. Requiring a token here would make readiness depend on
//     the very service being probed AND on Security's token issuance being already live, a circular
//     dependency that cannot resolve during a cold start. The authored contract states the same
//     reasoning for every service: shared/PowerFramework.Contracts/OpenApi/security.v1.yaml and
//     OpenApi/gateway.v1.yaml both override the document level bearer requirement on this one
//     operation with an empty requirement list.
//     .AllowAnonymous() is called EXPLICITLY rather than left implicit in the absence of a policy,
//     so that the intent is legible in the route declaration and cannot be silently revoked by a
//     later global RequireAuthorization() fallback in Program.cs. Anonymous access here is a
//     deliberate, single, documented exception - it is not the absence of a decision.
//
//  2. WHY IT DISCLOSES NOTHING (constraints C-G and C-F). Because the endpoint is anonymous, its
//     body is world readable by anything that can reach port 5102, so everything it reports is
//     public. The report therefore carries an overall status token, this service's own name, the
//     time the report was produced, and A CLOSED TWO-MEMBER VOCABULARY of component identifiers -
//     `self` and `components` - each with a coarse verdict. It carries no upstream address, no
//     credential, no connection string, no certificate, no environment variable value, no
//     configuration value, no host name, no other service's port, no exception text and no stack
//     trace.
//
//     EVERY VALUE IN THE BODY IS AUTHORED IN THIS FILE. NOTHING A REGISTRATION SUPPLIES REACHES IT,
//     AND THE TEMPTING ALTERNATIVE IS THE UNSAFE ONE. Echoing each registered check's own Name and
//     Description looks defensible, on the reasoning that the authored contract models those
//     members and requires their authors to keep them free of internal detail. That requirement is a
//     rule on the author of each registration, which is not something this file can enforce - so a
//     check named for a database host, or a description carrying a provider name, a URL, an
//     exception summary or a configuration hint, would be published to anything able to reach
//     the port. The vocabulary is closed here instead, which IS enforceable. Component checks are
//     aggregated into ONE entry rather than reported individually, because the count of registered
//     checks is itself the shape of this service's dependency graph.
//     Three shared framework members are available on every check result and all three are
//     deliberately dropped rather than projected: Exception (an exception message is not authored
//     for disclosure and routinely carries connection strings, addresses and file paths), Data (an
//     arbitrary bag a check may fill with anything at all) and Duration (see item 6).
//     The per-check detail is NOT lost. Each check's name and verdict go to the OPERATOR channel,
//     whose audience is authenticated by having access to this service's logs rather than by being
//     able to reach a port - the same caller-versus-operator split the unhandled-fault handler
//     applies. Descriptions, exceptions and data members are excluded from that record too, because a
//     log is not exempt from C-F merely because its reader is trusted.
//
//  3. WHY IT DOES NOT AGGREGATE ITS UPSTREAMS. THIS IS NOT AN OVERSIGHT. DO NOT "FIX" IT.
//     DataServices calls Persistence and Security functionally, so fanning out to them from here
//     looks natural and is wrong. Contract C-10 and docs/ARCHITECTURE.md 4.2 assign upstream
//     aggregation to GATEWAY ALONE: Gateway reports healthy only after Persistence, DataServices and
//     Security each report healthy, and its aggregate names each upstream with its own state. The
//     ordering is supplied by the orchestration manifest's health condition, so aggregating here
//     would duplicate that ordering in application code, make a probe that runs on every interval do
//     network work, and let one unreachable upstream mask this service's own readiness. This file
//     therefore opens no channel to Persistence, calls nothing on Security, and injects no typed
//     client, channel, storage handle or other upstream reference of any kind - the Clients folder
//     beside this one is deliberately unreferenced from here. It reports THIS PROCESS ONLY -
//     which is exactly what the authored leaf contract in security.v1.yaml says a non Gateway
//     service's report is: "the readiness of this service only ... never an aggregate".
//
//  4. WHY READINESS NEVER GATES STARTUP, AND WHY THE HANDLER CANNOT THROW (constraint C-J).
//     The host must start even when its upstreams are unreachable and simply report its state.
//     Fail fast belongs to startup configuration validation in Program.cs, through the options
//     pattern's validate on start, not to this probe: a probe that faulted while a dependency was
//     down would take the container with it and the readiness gate could never recover. So the
//     handler returns a STATUS rather than throwing, and every failure path inside it - including a
//     readiness check that throws out of the shared framework's own evaluation - is caught, logged
//     and reported as Unhealthy. The one exception deliberately allowed to propagate is
//     OperationCanceledException, which means the caller disconnected: there is no longer a response
//     to write, and swallowing it would only produce a second failure while trying.
//     The legacy analogue is REFERENCE ONLY and is not reproduced here.
//     ws_objects/pfw.pbl.src/pfw.sra:L111-L144 - the framework application, not the same-named
//     packager object at ws_objects/pfw.pack.pbl.src/pfw.sra - unpacks a seven field assert
//     payload split on a carriage return line feed
//     pair and then executes HALT CLOSE. That fail fast posture is real and is preserved - in
//     Gateway's Diagnostics/SystemErrorHandler.cs, which owns it. This endpoint REPORTS unhealthy;
//     it never terminates the process, and not one line of pfw.sra is ported into this file.
//
//  5. WHY THE HEALTH CHECK ABSTRACTIONS COME FROM THE SHARED FRAMEWORK AND NOTHING IS ADDED.
//     Microsoft.Extensions.Diagnostics.HealthChecks, its Abstractions assembly and
//     Microsoft.AspNetCore.Diagnostics.HealthChecks all ship inside the Microsoft.AspNetCore.App
//     shared framework, so HealthCheckService, HealthStatus, HealthReport and HealthReportEntry are
//     reachable with ZERO package references. The Agent Action Plan 0.5.3 excludes the
//     Microsoft.Extensions.Diagnostics.HealthChecks package and the URI probe health check packages
//     by name, and PowerFramework.DataServices.csproj carries exactly five package references, none
//     of them a health check package - its own comment says /health needs none. Adding one would be
//     a build policy violation rather than a convenience, and would also break the single sourced
//     central package version rule. Absent for exactly the same reason, each named on 0.5.3's
//     deliberately excluded list rather than repeated here: any metrics, tracing or structured
//     logging sink, since the shared framework's own logging abstractions are what this file uses and
//     are sufficient; and any interactive OpenAPI user interface, since the OpenAPI reference already
//     produces the specification and no requirement asks for a viewer.
//     The route is mapped with MapGet rather than the framework's MapHealthChecks for one specific
//     reason: MapHealthChecks resolves HealthCheckService as a REQUIRED service and throws while the
//     endpoint is being built if AddHealthChecks was never called. Program.cs owns registration, so
//     binding this probe's liveness to a registration it does not own would be precisely the startup
//     gating item 4 forbids. Resolving the service OPTIONALLY, as the handler below does, honours
//     whatever Program.cs registered while remaining correct when it registered nothing.
//
//  6. NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE (Agent Action Plan 0.8.5). The
//     repository publishes no service level agreement, no latency budget, no throughput target and
//     no availability commitment, so none may be stated, implied or used to justify a decision here.
//     That is why each check's measured Duration is not projected onto the wire: publishing it
//     anonymously would invite exactly the objective that does not exist. Independent scalability is
//     structural - one container per service - and is not this file's claim to make.
//
//  7. WHY ONLY Healthy IS 200, AND WHY Degraded IS 503 ALONGSIDE Unhealthy. Contract C-10 requires
//     the response to distinguish "not ready" from "unhealthy", because a service still completing
//     its startup validation is not the same as one whose dependency has failed and an operator must
//     be able to tell them apart. THAT DISTINCTION LIVES IN THE `status` MEMBER, WHICH KEEPS ALL
//     THREE TOKENS - it does not live in the status code, and an earlier form of this file put it
//     there by answering 200 for Degraded as well as Healthy.
//     THAT WAS WRONG, AND THE REASON IS MECHANICAL RATHER THAN A MATTER OF TASTE. This endpoint is
//     what `depends_on: condition: service_healthy` waits on, and what an orchestrator observes is
//     the STATUS CODE. `Degraded` means NOT READY. Answered 200, a service that had just said it was
//     not ready would satisfy the gate, Gateway would start before its upstreams were usable, and
//     the single most important readiness property in the orchestration would be silently defeated -
//     while every document still claimed it held. The earlier reasoning was that a probe tearing down
//     on any non-2xx must not kill a service that is merely still starting; that concern is real and
//     belongs to a LIVENESS probe, which is a different question from readiness and is not what this
//     path answers.
//     So Healthy is 200; Degraded, Unhealthy and any status this build does not recognise are 503,
//     with the verdict named in the body so the three remain distinguishable. Testing FOR the one
//     ready state rather than against the failed ones is what makes an unrecognised value fail
//     CLOSED.
//
//  8. WHY THE 503 BODY IS A PROBLEM DOCUMENT WITH A retCode. Both authored OpenAPI specifications in
//     shared/PowerFramework.Contracts/OpenApi answer a failed /health with application/problem+json
//     rather than with a second error schema, name the failing check in the problem detail, and carry
//     the legacy PowerFramework return code in the single permitted extension member. This file does
//     the same, and reuses the legacy vocabulary rather than inventing a parallel one:
//     RetCode.E_RETRY, documented in shared/PowerFramework.Shared.Kernel/RetCode.cs as "the
//     operation should be retried", is the legacy code whose meaning matches Service Unavailable.
//
//  9. WHY THERE IS NO VERSION MEMBER ON THE BODY. A contract or API version would be safe to
//     disclose, and it is deliberately still omitted. Both authored health schemas declare
//     additionalProperties false over exactly {status, service, checkedAt, checks} and neither
//     carries a version member, so adding one would make this service's body the only member of the
//     C-10 family with an extra field - a gratuitous divergence, which constraint C-B forbids as
//     firmly as it forbids removing behaviour. Contract identity is disclosed where it belongs, in
//     the OpenAPI metadata declared on the route below. Recorded here so the omission reads as a
//     decision that was taken rather than a field that was forgotten.
//
// 10. WHY THE RESPONSE TYPES ARE NOT NAMED HealthReport AND HealthCheckResult. Those are the names
//     the authored schemas use, and both collide with shared framework types this file imports -
//     Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport and .HealthCheckResult. The wire
//     types are therefore ServiceHealthReport and ServiceHealthCheck. The MEMBER SET is what the
//     contract fixes and it is mirrored exactly; only the schema title differs, and "Service" carries
//     the useful signal that this is one service's own report rather than Gateway's aggregate.
//
// 11. WHY NOTHING HERE TOUCHES THE PHASE 2 CAPABILITIES (constraint C-D). Four of the eight target
//     capability areas are deferred in full - not partially, and not as stubs. Their four reserved
//     not implemented routes are declared on Gateway's Endpoints/DeferredCapabilityEndpoints.cs
//     alone, which together with docs/DEFERRED.md is the only pair of places in the whole refactor
//     where those four areas may be named at all. This file therefore declares no reserved route,
//     implements nothing on their behalf, and does not name them - not in a route, not in a type, not
//     in a status token and not in a comment. Read docs/DEFERRED.md if you need the roster; it is
//     deliberately absent from here, which is why a repository wide search for those four names
//     returns nothing from this file.
// =================================================================================================

using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Endpoints;

/// <summary>
/// Maps the anonymous <c>GET /health</c> readiness probe that contract C-10 requires of every
/// service in the decomposition, reporting the readiness of THIS process only.
/// </summary>
/// <remarks>
/// <para>
/// Upstream aggregation is deliberately absent: Gateway is the single aggregator, and it reports
/// healthy only after Persistence, DataServices and Security each report healthy. See the decision
/// record at the head of this file, in particular items 1, 3, 4 and 5, before changing anything
/// here - several of the omissions look like gaps and are not.
/// </para>
/// <para>
/// The class is static and holds no state. It registers no authentication scheme, no options type
/// and no dependency injection service; <c>Program.cs</c> owns host construction, options binding
/// with validate on start, and bearer registration. This file maps one route and nothing more.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>
    /// The route pattern, fixed by the attached environment's acceptance criteria (constraint C-L)
    /// as the anonymous per service health path on the 5101 to 5105 band. Neither the path nor the
    /// port may be renamed or relocated: the orchestration probe and every readiness gate name them.
    /// </summary>
    private const string HealthRoutePattern = "/health";

    /// <summary>
    /// This service's identifier on the wire. Fixed by the authored contract rather than chosen:
    /// <c>OpenApi/gateway.v1.yaml</c>'s upstream health schema closes the reporting service
    /// enumeration at exactly <c>persistence</c>, <c>dataservices</c> and <c>security</c>, so
    /// Gateway's aggregate can name this service individually.
    /// </summary>
    private const string ServiceIdentifier = "dataservices";

    /// <summary>The wire token for a fully ready service. Reported with HTTP 200.</summary>
    private const string StatusHealthy = "Healthy";

    /// <summary>
    /// The wire token for "not ready, but not failed" - a service still completing startup
    /// validation. Reported with HTTP 200; see decision record item 7.
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
    /// It is not a registered check's own name and never derived from one: the anonymous body must not
    /// disclose what a registration chose to call itself, nor how many registrations exist. See
    /// <see cref="ProjectChecks"/> for the full reasoning and
    /// <see cref="WriteOperatorRecord"/> for where the per-check detail goes instead.
    /// </remarks>
    /// <summary>
    /// The second member: whether this service holds the credential material its token bootstrap
    /// requires. A fixed identifier authored in this file; it carries no path, no certificate subject, no
    /// address and no configuration value of any kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IT HAS AN ENTRY OF ITS OWN RATHER THAN BEING FOLDED INTO <see cref="ComponentsCheckName"/>, FOR
    /// THE SAME REASON THE CHECK EXISTS AT ALL. Every call this service makes outward - to Persistence
    /// for contracts C-05..C-08, to Security for C-02 - carries a bearer token, and the only way to
    /// obtain one is the issuance edge. Contract C-01 protects that edge with EITHER OF TWO CREDENTIALS -
    /// an HTTP Basic client credential or a client certificate - and requires one of them, because a
    /// caller cannot present a bearer token in order to obtain its first bearer token. A deployment
    /// presenting NEITHER therefore started, reported ready, satisfied the Compose dependency gate, and
    /// then had every outward call refused for want of a caller identity. Naming the entry is what lets
    /// the thing polling this route see which precondition is unmet rather than only that something is.
    /// </para>
    /// <para>
    /// Doubles as the REGISTRATION name of <see cref="TokenBootstrapHealthCheck"/>, which is what lets
    /// <see cref="ProjectChecks"/> recognise that check by name and report it as its own entry while
    /// every other registration is folded into <see cref="ComponentsCheckName"/>.
    /// </para>
    /// </remarks>
    internal const string CredentialsCheckName = "credentials";

    /// <summary>The tag the checks this file registers are grouped under.</summary>
    private const string ReadinessTag = "ready";

    /// <summary>The fixed prose for a service holding the credential material it needs.</summary>
    private const string CredentialsReadyDescription =
        "The credential material this service must present in order to obtain a service token is "
        + "configured.";

    /// <summary>
    /// The fixed prose for a service that cannot obtain a token. It names WHAT is missing in capability
    /// terms and never a path, a subject, an address or a configuration key: the route is anonymous, and
    /// a path names where private key material is mounted. The configuration keys go to the operator
    /// channel instead.
    /// </summary>
    private const string CredentialsNotReadyDescription =
        "The credential material this service must present in order to obtain a service token is not "
        + "configured, so every call that needs a token would be refused.";

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

    /// <summary>
    /// The operator-channel record carrying the per-check detail the anonymous body withholds.
    /// </summary>
    /// <remarks>
    /// Three structured fields: the reporting service, the aggregate verdict, and each check's name
    /// with its own verdict. No description, no exception and no arbitrary data member is included -
    /// see <see cref="WriteOperatorRecord"/>.
    /// </remarks>
    private const string OperatorReadinessRecord =
        "Readiness evaluated for the {Service} service: {Status}. Component checks: {ComponentChecks}.";

    /// <summary>The OpenAPI operation identifier, matching the authored contracts' spelling.</summary>
    private const string OperationName = "getHealth";

    /// <summary>The OpenAPI tag, matching the authored contracts' grouping.</summary>
    private const string OperationTag = "Health";

    /// <summary>
    /// The single permitted problem document extension member carrying the legacy PowerFramework
    /// return code, spelled as the authored contract spells it.
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
    /// The logger category. A constant string rather than <c>ILogger&lt;T&gt;</c> because <c>T</c>
    /// cannot be a static class, and this class is deliberately static.
    /// </summary>
    private const string LoggerCategoryName = "PowerFramework.DataServices.Endpoints.HealthEndpoints";

    /// <summary>The summary published on the operation.</summary>
    private const string OperationSummary = "Report this service's own readiness. Anonymous.";

    /// <summary>
    /// The description published on the operation. It restates the four properties a consumer must
    /// know - anonymous deliberately, never an aggregate, the two ready states, and the disclosure
    /// boundary - and asserts nothing about how quickly or how often the endpoint answers, because no
    /// such objective is published anywhere in the repository (Agent Action Plan 0.8.5).
    /// </summary>
    private const string OperationDescription = """
        Reports the readiness of **this service only** (contract C-10, readiness half).

        **Anonymous deliberately, for a mechanical reason.** A readiness probe must be reachable before
        any token exists. The caller that probes it - a container orchestrator, a load balancer, an
        operator - holds no token, and it probes precisely during the window in which this service is
        still starting. Requiring a token here would make readiness depend on the very service being
        probed and on Security's token issuance being already live, a circular dependency that cannot
        resolve during a cold start. `/health` is therefore anonymous on all four services, and that
        uniformity is part of contract C-10 rather than a per-service choice. Every other operation on
        this service requires a bearer token.

        **Never an aggregate.** This report covers this process only. Gateway is the single
        aggregator: it reports healthy only after Persistence, DataServices and Security each report
        healthy, and its aggregate names each upstream with its own state so an operator can tell
        which one is not ready. The ordering is expressed in the orchestration manifest as a
        dependency condition on upstream health. This endpoint therefore opens no channel to
        Persistence and calls nothing on Security.

        **"Not ready" and "unhealthy" are different states, and the difference is in the body rather
        than in the status code.** Only `Healthy` is answered with 200. `Degraded` means *not ready*
        and is answered with 503 alongside `Unhealthy`, because the orchestration readiness gate
        observes the status code - a not-ready service answering 200 would open that gate early. The
        `status` member keeps all three tokens, so an operator can still tell a service completing its
        startup validation from one whose dependency has failed. A 503 carries a problem document
        naming the not-ready component and the legacy PowerFramework return code.

        **The body is public, and is scoped accordingly.** Every value in it is authored by this
        service: the component identifiers are a closed vocabulary - `self`, `credentials` and
        `components` - rather than the names whatever registered a check chose for it, and every other
        component check is aggregated into one entry so that the number of them is not disclosed either. The body carries no upstream
        address, no credential, no connection string, no certificate, no configuration value, no host
        name, no other service's port, no exception text and no stack trace. The per-component detail
        is available to an operator through this service's own telemetry.
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
    /// Thrown when <paramref name="app"/> is <see langword="null"/>. This is the ONLY throw in the
    /// file and it is a programming error at composition time, not a request time failure: the
    /// request handler itself never throws, because a probe that faults would take the container with
    /// it and the readiness gate could never recover (decision record item 4).
    /// </exception>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthRoutePattern, ReportReadinessAsync)
           // Explicit, and load bearing. See decision record item 1: this survives a later global
           // RequireAuthorization() fallback in Program.cs, whereas merely omitting a policy would
           // not. It is the one documented anonymous exception in the system.
           .AllowAnonymous()
           .WithName(OperationName)
           .WithTags(OperationTag)
           .WithSummary(OperationSummary)
           .WithDescription(OperationDescription)
           // Exactly two declared responses, matching the authored contract: the report on 200, and
           // a problem document on 503. The handler returns IResult rather than a typed result union
           // for precisely this reason - a union would have the framework infer an additional 500
           // from ProblemHttpResult's own metadata, publishing a response this operation cannot
           // produce. Decision record item 7 governs which verdicts reach which member of that
           // two-response set, never the set itself.
           .Produces<ServiceHealthReport>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Evaluates this process's own readiness and projects it onto the wire.
    /// </summary>
    /// <param name="httpContext">
    /// The request context, used only to reach the request scoped service provider. Nothing about the
    /// request itself influences the verdict, and nothing about the request is echoed back.
    /// </param>
    /// <param name="loggerFactory">
    /// Resolved by the framework from the host's own logging abstractions. Used exclusively to record
    /// a readiness evaluation that failed outright, which is otherwise invisible because it is
    /// reported to the caller as a status rather than raised as an exception.
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
                // No component check is registered, so the whole of this leaf service's readiness is
                // the statement that its process is running and answering requests - which the
                // execution of this handler has just demonstrated. That is a truthful Healthy, not an
                // assumed one.
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

                // THE OPERATOR CHANNEL, WHICH IS WHERE THE DETAIL BELONGS (decision record item 2).
                // The response above carries only this file's own fixed public vocabulary, so the
                // registration-supplied check names - the thing an operator actually needs in order to
                // find a failing component - would otherwise be lost entirely. They are recorded here
                // instead, where the audience is authenticated by having access to the service's logs
                // rather than by being able to reach a port.
                WriteOperatorRecord(loggerFactory, report);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Decision record item 4: the handler reports rather than throws, because a probe that
            // faulted would take the container with it and the readiness gate could never recover.
            // The fault is recorded HERE, where the operator can see it, and is deliberately NOT
            // projected onto the anonymous response (item 2). OperationCanceledException is excluded
            // from this catch on purpose: it means the caller disconnected, so there is no longer a
            // response to write and attempting one would only fail again.
            //
            // 🔴 "LOGGED HERE, WHERE THE OPERATOR CAN SEE IT" WAS TRUE OF THE OPERATOR AND OF EVERYONE
            // ELSE THE RECORD REACHES. A readiness probe reaches both upstream clients, so the fault
            // arriving here can be a transport fault quoting a configured address or an upstream's own
            // status detail; an attached exception is rendered by every provider with its whole message
            // chain and its stack. The type chain replaces it and still says which probe failed and why.
            loggerFactory
                .CreateLogger(LoggerCategoryName)
                .LogError(
                    "Readiness evaluation failed for the {Service} service; reporting {Status}. "
                        + "FaultTypes={FaultTypes}",
                    ServiceIdentifier,
                    StatusUnhealthy,
                    ExceptionChain.DescribeTypes(exception));

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
        // candidate recording. Resolving TimeProvider optionally honours a deterministic double when
        // the host registers one and registers nothing itself when it does not.
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
    /// Projects a shared framework health report onto the wire shape the authored contract fixes,
    /// using ONLY this file's fixed public component vocabulary.
    /// </summary>
    /// <param name="report">The evaluated report.</param>
    /// <returns>
    /// Between one and three entries, named exclusively from <see cref="SelfCheckName"/>,
    /// <see cref="CredentialsCheckName"/> and <see cref="ComponentsCheckName"/>: this endpoint's own
    /// statement that the process is answering, whether the credential material the token bootstrap needs
    /// is configured, and - when any OTHER component check is registered - one aggregated entry standing
    /// for all of those.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NOTHING A REGISTRATION SUPPLIED CROSSES THIS BOUNDARY, AND THAT IS THE WHOLE POINT OF THE
    /// METHOD (decision record item 2). An earlier form of this projection echoed each registered
    /// check's own <c>Name</c> and <c>Description</c>. Both are supplied by whatever registered the
    /// check rather than authored for disclosure, and this endpoint is ANONYMOUS - so a name like
    /// <c>npgsql-primary-eu-west-1</c> or a description carrying a provider name, a host, a URL, a
    /// database identifier, an exception summary or a configuration hint would have been published to
    /// anything able to reach the port. The contract's requirement that check names stay free of
    /// internal detail was a rule on the AUTHOR of each registration, which is not a control this
    /// file can enforce; a closed vocabulary declared here is.
    /// </para>
    /// <para>
    /// The names are therefore fixed constants, and their <c>Status</c> is the only thing that varies.
    /// The credential entry is recognised BY NAME rather than by position, and its own description is
    /// still not echoed: only its status is read, and the prose is chosen from the two constants above -
    /// so the enforcement stays structural even for a check this file registers itself. Aggregating every
    /// OTHER component check into one entry is deliberate: reporting the COUNT of
    /// registered checks, or one anonymous entry per check, would leak the shape of this service's
    /// dependency graph to an unauthenticated caller - which is the same disclosure in a different
    /// unit. The per-check detail is not lost; it goes to the operator channel through
    /// <see cref="WriteOperatorRecord"/>.
    /// </para>
    /// <para>
    /// The report entry's <c>Exception</c>, <c>Data</c> and <c>Duration</c> members are dropped for the
    /// reasons in decision record items 2 and 6.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ServiceHealthCheck> ProjectChecks(HealthReport report)
    {
        List<ServiceHealthCheck> projected = [BuildSelfCheck(HealthStatus.Healthy)];

        if (report.Entries.Count == 0)
        {
            return projected;
        }

        HealthStatus? credentials = null;
        HealthStatus? othersWorst = null;

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            // The shared framework keys its registry case-insensitively, so the recognition test is too -
            // otherwise a differently-cased spelling would silently fall into the aggregated bucket
            // instead of being reported as the credential entry.
            if (string.Equals(entry.Key, CredentialsCheckName, StringComparison.OrdinalIgnoreCase))
            {
                // Worst-wins if the same name were somehow registered twice: the framework forbids it,
                // but a probe must not depend on that guarantee to stay fail-closed.
                credentials = credentials is HealthStatus known && known < entry.Value.Status
                    ? known
                    : entry.Value.Status;

                continue;
            }

            // The worst status across every OTHER registered check, computed here rather than taken from
            // report.Status: the report's own aggregate can be shaped by a registration's result
            // predicate, whereas this entry stands for the checks themselves and must say what they said.
            othersWorst = othersWorst is HealthStatus worst && worst < entry.Value.Status
                ? worst
                : entry.Value.Status;
        }

        if (credentials is HealthStatus credentialStatus)
        {
            projected.Add(
                new ServiceHealthCheck(CredentialsCheckName, ToWireStatus(credentialStatus))
                {
                    Description = credentialStatus == HealthStatus.Healthy
                        ? CredentialsReadyDescription
                        : CredentialsNotReadyDescription,
                });
        }

        if (othersWorst is HealthStatus othersStatus)
        {
            projected.Add(
                new ServiceHealthCheck(ComponentsCheckName, ToWireStatus(othersStatus))
                {
                    Description = othersStatus == HealthStatus.Healthy
                        ? ComponentsHealthyDescription
                        : ComponentsNotReadyDescription,
                });
        }

        return projected;
    }

    /// <summary>
    /// Records the per-check detail on the operator channel, which is the audience it is authored for.
    /// </summary>
    /// <param name="loggerFactory">The host's logging abstraction.</param>
    /// <param name="report">The evaluated report.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS: the anonymous response deliberately carries a closed vocabulary, so without
    /// this record the one piece of information an operator needs from a failed probe - WHICH component
    /// is not ready - would exist nowhere. The split is the same one the system applies to an unhandled
    /// fault: the caller learns the class of failure, the operator learns the detail.
    /// </para>
    /// <para>
    /// WHAT IS RECORDED AND WHAT IS NOT. Each check's NAME and STATUS travel; its
    /// <c>Description</c>, <c>Exception</c> and <c>Data</c> do NOT. A name is an identifier the
    /// registration chose and is safe to hold in this service's own logs, whereas a description or an
    /// exception message is free text that routinely carries a connection string, an address or a file
    /// path - and a log record is not exempt from that concern merely because its reader is trusted
    /// (constraint C-F). The names are joined into one field rather than logged per check so that a
    /// probe polled continuously by an orchestrator produces one record per evaluation.
    /// </para>
    /// <para>
    /// The level is chosen from the verdict: a not-ready evaluation is a warning an operator should
    /// see, while a healthy one is trace-level so that continuous probing does not fill a log with
    /// records that say nothing happened.
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
    /// <see cref="HealthStatus.Unhealthy"/> when the readiness evaluation itself could not be
    /// completed.
    /// </param>
    /// <returns>The <c>self</c> entry.</returns>
    /// <remarks>
    /// The unhealthy description states THAT the evaluation failed and never WHY: the reason is an
    /// exception, the response is anonymous, and the two must not meet (decision record item 2). The
    /// reason is in the log instead.
    /// </remarks>
    private static ServiceHealthCheck BuildSelfCheck(HealthStatus status)
    {
        string description = status == HealthStatus.Healthy
            ? "The service process is running and answering requests."
            : "The readiness evaluation could not be completed.";

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
    /// The authored contract requires the failing check to be named in the problem detail and the
    /// legacy PowerFramework return code to travel in the single permitted extension member, rather
    /// than a second error schema being defined for this one response. Every name that reaches this
    /// method comes from the fixed public component vocabulary this file declares, so it is an
    /// identifier this file chose rather than one a registration supplied; no description, exception
    /// or configuration value is carried here (decision record item 2).
    /// </para>
    /// <para>
    /// DEGRADED AND UNHEALTHY BOTH ARRIVE HERE AND ARE DISTINGUISHED IN THE BODY RATHER THAN IN THE
    /// STATUS. Both are answered 503, because both mean "not ready" and the orchestration gate reads
    /// the status code. The verdict travels in <see cref="ServiceStatusExtensionMember"/> so that an
    /// operator - and Gateway's aggregator, which reads that member off this body - can tell a service
    /// still completing startup validation from one whose dependency has failed. It cannot travel in a
    /// <c>status</c> member: RFC 9457 uses that name for the integer HTTP status, and this response IS
    /// a problem document rather than a <see cref="ServiceHealthReport"/>.
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
            ? "The DataServices service is not ready (" + wireStatus + ")."
            : "The DataServices service is not ready (" + wireStatus + "). Readiness check(s) not "
              + "reporting " + StatusHealthy + ": " + string.Join(", ", notReady) + ".";

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
    /// conversion, so that the published vocabulary is stated in this file and cannot drift silently
    /// if a future framework version renames or adds a member. An unrecognised status maps to
    /// <c>Unhealthy</c>, which is the same fail closed choice the status code mapping makes.
    /// </remarks>
    private static string ToWireStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusHealthy,
        HealthStatus.Degraded => StatusDegraded,
        HealthStatus.Unhealthy => StatusUnhealthy,
        _ => StatusUnhealthy,
    };

    // ==============================================================================================
    //  REGISTRATION
    // ==============================================================================================

    /// <summary>
    /// Registers the readiness evaluator and this service's own component checks.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// IDEMPOTENT, AND GUARDED BY NAME. A second registration under one name makes the framework fault
    /// while building its evaluator, and a probe that faults is worse than a probe that reports - so the
    /// guard reads the existing registrations and stands aside for a host or a test that deliberately
    /// substituted the verdict under this same name.
    /// </para>
    /// <para>
    /// NO PACKAGE IS REQUIRED FOR ANY OF THIS. <see cref="HealthCheckService"/> and its registration
    /// builder ship inside the <c>Microsoft.AspNetCore.App</c> shared framework, which is why the project
    /// file carries no health-check package reference (AAP 0.5.3).
    /// </para>
    /// <para>
    /// STILL NO COMPONENT CHECK FOR EITHER UPSTREAM, deliberately. C-10 gives the AGGREGATION to Gateway,
    /// and a leaf service that reported itself unready because an upstream was slow would make the
    /// Compose <c>depends_on: condition: service_healthy</c> chain oscillate rather than settle. The check
    /// registered here reads BOUND CONFIGURATION ONLY and opens no channel to anything.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Registers the evaluator and its infrastructure. Called for that effect only; the registration
        // itself is added below so it can be guarded.
        _ = services.AddHealthChecks();

        services.Configure<HealthCheckServiceOptions>(static options =>
        {
            foreach (HealthCheckRegistration existing in options.Registrations)
            {
                if (string.Equals(
                        existing.Name,
                        CredentialsCheckName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            options.Registrations.Add(
                new HealthCheckRegistration(
                    CredentialsCheckName,

                    // The same activation the framework's own typed registration uses: resolved from the
                    // container when it is registered there, otherwise constructed with its dependencies
                    // injected. Nothing is resolved statically or globally.
                    static provider =>
                        ActivatorUtilities.GetServiceOrCreateInstance<TokenBootstrapHealthCheck>(provider),

                    // DEGRADED RATHER THAN UNHEALTHY, AND THE DISTINCTION IS THE POINT. Unhealthy means
                    // "has failed"; an unmounted credential has not failed, it has not arrived. The
                    // orchestration layer owns the mount, so this is precisely the "not ready, but not
                    // failed" state Degraded exists for - and an operator reading it is told to supply
                    // material rather than to investigate a fault. Both verdicts are answered 503, so the
                    // readiness gate behaves identically either way.
                    failureStatus: HealthStatus.Degraded,
                    tags: [ReadinessTag]));
        });

        return services;
    }
}

// ==================================================================================================
//  THE ONE COMPONENT CHECK - CAN THIS SERVICE OBTAIN A TOKEN AT ALL
// ==================================================================================================

/// <summary>
/// Reports whether this service holds the credential material its token bootstrap requires, without
/// requesting a token, opening a channel or touching any state.
/// </summary>
/// <remarks>
/// <para>
/// WHY IT EXISTS, STATED AS THE GAP IT CLOSES. Every outward call this service makes carries a bearer
/// token: Persistence requires one on all four of its contracts, and so does each of Security's
/// eighteen cryptographic operations. The only way to obtain one is <c>POST /v1/tokens</c>, which
/// contract C-01 protects with MUTUAL TLS and with nothing else - because a caller cannot present a
/// bearer token in order to obtain its first bearer token. A deployment that mounts no client
/// certificate is a legitimate startup state and is deliberately not a startup failure, but it is a
/// state in which this service cannot reach the issuance edge at all. Before this check, such an
/// instance reported READY, satisfied the Compose dependency gate, let Gateway start behind it, and then
/// refused every request it received. Readiness is exactly the question it was answering wrongly.
/// </para>
/// <para>
/// IT IS NON-DESTRUCTIVE IN THE STRONGEST SENSE: IT PERFORMS NO I/O AT ALL. No token is minted, no
/// handshake is attempted, no metadata is fetched, no file is opened and no cache is populated. It reads
/// two bound configuration values and compares them against the two preconditions the shipped client
/// itself enforces on every call. A probe that minted a token would mutate the client's credential cache
/// and put load on the issuance edge from an anonymous, unauthenticated route.
/// </para>
/// <para>
/// AND IT DOES NOT PROBE THE UPSTREAM, WHICH IS A SEPARATE DECISION FROM THE ONE ABOVE. Whether Security
/// is reachable is an upstream question, and C-10 gives every upstream question to Gateway: a leaf that
/// reported itself unready because an upstream was still starting would make the Compose dependency
/// chain oscillate instead of settle. What this check asserts is entirely local - the material is here,
/// or it is not.
/// </para>
/// <para>
/// THE PRECONDITIONS BELONG TO THE SHIPPED CLIENT, SO THEY ARE ASSERTED ONLY WHEN THE SHIPPED CLIENT IS
/// THE ONE REGISTERED. A host that supplies its own <see cref="IServiceTokenProvider"/> is asserting that
/// it obtains credentials by its own means - a different mechanism with different preconditions, which
/// this file cannot know and must not invent a verdict about. For such a host the entry reports ready and
/// says so, which is truthful: the bootstrap this check knows how to evaluate is not the one in use.
/// </para>
/// </remarks>
internal sealed class TokenBootstrapHealthCheck : IHealthCheck
{
    /// <summary>The fixed prose for a service holding the credential material it needs.</summary>
    /// <remarks>
    /// AUTHORED HERE AND ALSO IN <c>HealthEndpoints</c>, WHICH IS NOT AN OVERSIGHT: the projection never
    /// echoes a check's own description, because a description is chosen by whatever registered the check
    /// and this route is anonymous. Each side therefore authors its own prose.
    /// </remarks>
    private const string ReadyDescription =
        "The credential material this service must present in order to obtain a service token is "
        + "configured.";

    /// <summary>The fixed prose for a service that cannot obtain a token.</summary>
    private const string NotReadyDescription =
        "The credential material this service must present in order to obtain a service token is not "
        + "configured, so every call that needs a token would be refused.";

    /// <summary>The fixed prose for a bootstrap this check cannot evaluate because a host supplied it.</summary>
    private const string SubstitutedDescription =
        "The service token bootstrap is supplied by the host rather than by this service's own client, so "
        + "this service holds no credential precondition of its own.";

    /// <summary>The bound options the two preconditions are read from.</summary>
    private readonly IOptions<DataServicesOptions> _options;

    /// <summary>The registered token bootstrap, whose IDENTITY decides whether the preconditions apply.</summary>
    private readonly IServiceTokenProvider _tokens;

    /// <summary>The operator channel, which is where the unmet setting is named.</summary>
    private readonly ILogger<TokenBootstrapHealthCheck> _logger;

    /// <summary>Creates the check.</summary>
    /// <param name="options">The bound options.</param>
    /// <param name="tokens">The registered token bootstrap.</param>
    /// <param name="logger">The operator channel.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public TokenBootstrapHealthCheck(
        IOptions<DataServicesOptions> options,
        IServiceTokenProvider tokens,
        ILogger<TokenBootstrapHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _tokens = tokens;
        _logger = logger;
    }

    /// <summary>
    /// Establishes whether a service token could be obtained, without obtaining one.
    /// </summary>
    /// <param name="context">
    /// The registration being evaluated. Its failure status is honoured rather than assumed, so a host
    /// that registered this check as failing rather than degrading gets what it asked for.
    /// </param>
    /// <param name="cancellationToken">The evaluation's cancellation token.</param>
    /// <returns>
    /// A healthy result when a token could be obtained; otherwise a result carrying the registration's
    /// failure status.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled - the request was aborted, so there is no longer a response to write.
    /// </exception>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        cancellationToken.ThrowIfCancellationRequested();

        if (_tokens is not SecurityClient)
        {
            return Task.FromResult(HealthCheckResult.Healthy(SubstitutedDescription));
        }

        HealthStatus failureStatus = context.Registration.FailureStatus;
        SecurityClientOptions security = _options.Value.Security;

        if (string.IsNullOrWhiteSpace(security.BaseAddress))
        {
            // The options validator requires this, so reaching it means the validator was bypassed or a
            // host bound the group itself. The configuration KEY is named on the operator channel because
            // it is what an operator acts on; it is a key rather than a value, and it still never reaches
            // the anonymous response.
            _logger.LogError(
                "No address is configured for the token-issuance edge, so no service token can be "
                    + "requested. Set '{Setting}'. Reporting the {Check} readiness check not ready.",
                $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}"
                    + $":{nameof(SecurityClientOptions.BaseAddress)}",
                HealthEndpoints.CredentialsCheckName);

            return Task.FromResult(new HealthCheckResult(failureStatus, NotReadyDescription));
        }

        // EITHER ACCEPTED SCHEME MAKES THIS HOST READY, AND DEMANDING THE CERTIFICATE PAIR WAS AN OUTAGE
        // RATHER THAN STRICTNESS. Contract C-01 accepts TWO caller credentials on POST /v1/tokens - a
        // shared secret presented as an HTTP Basic credential, or a client certificate - as alternatives.
        // Requiring BOTH HALVES OF THE CERTIFICATE PAIR here and nothing else is the strict-looking
        // reading, and under the documented bring-up it is an outage: that bring-up supplies
        // SECURITY_CLIENT_SECRET_DATASERVICES and leaves both certificate paths deliberately EMPTY
        // ("this deployment presents no client certificate, which is a supported state" -
        // orchestration/.env.example section 6.3), so a certificate-only check reports NOT READY forever.
        // And this service's readiness is one of the three Gateway's own readiness gate waits on, so the
        // consequence is not a misleading warning: it is a stack that never comes up, under exactly the
        // configuration the orchestration layer documents.
        //
        // THE PAIR IS STILL TESTED AS A PAIR, because half a pair cannot complete a handshake: a
        // certificate without its key cannot be loaded and a key with no certificate has nothing to
        // present. So a certificate-only deployment needs both halves, while a secret-configured
        // deployment needs neither. That is what `HasIssuanceCredential` cannot express on its own - it
        // is an OR over "did the operator INTEND a scheme" - which is why the condition is written out
        // here rather than delegated: readiness asks whether a credential could actually be presented.
        bool presentsSecret = !string.IsNullOrWhiteSpace(security.ClientSecret);

        bool presentsCertificate = !string.IsNullOrWhiteSpace(security.MutualTls.CertificatePath)
            && !string.IsNullOrWhiteSpace(security.MutualTls.CertificateKeyPath);

        if (!presentsSecret && !presentsCertificate)
        {
            // NO VALUE AND NO PATH IS REPRODUCED, not even here. A path names where private key material
            // is mounted and a secret is a credential; a log is exempt from neither concern. The SETTING
            // NAMES are what an operator needs in order to act.
            _logger.LogWarning(
                "This host presents no caller credential, so it cannot obtain a service token: the "
                    + "issuance operation accepts a shared secret as an HTTP Basic credential or a client "
                    + "certificate, and a request presenting neither can only be refused. Set '{Secret}' "
                    + "- the documented bring-up path - or both '{Certificate}' and '{Key}' to the "
                    + "material this deployment mounts. Reporting the {Check} readiness check not ready.",
                SecurityClientOptions.ClientSecretConfigurationKey,
                $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}"
                    + $":{nameof(SecurityClientOptions.MutualTls)}"
                    + $":{nameof(MutualTlsClientOptions.CertificatePath)}",
                $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}"
                    + $":{nameof(SecurityClientOptions.MutualTls)}"
                    + $":{nameof(MutualTlsClientOptions.CertificateKeyPath)}",
                HealthEndpoints.CredentialsCheckName);

            return Task.FromResult(new HealthCheckResult(failureStatus, NotReadyDescription));
        }

        // Trace level on the ready path, because the Compose health condition polls continuously and an
        // information-level line per poll would drown the channel in records saying nothing happened.
        _logger.LogTrace(
            "The credential material the token bootstrap requires is configured for the {Check} readiness "
                + "check.",
            HealthEndpoints.CredentialsCheckName);

        return Task.FromResult(HealthCheckResult.Healthy(ReadyDescription));
    }
}

/// <summary>
/// The readiness of THIS service only, as published on <c>GET /health</c>.
/// </summary>
/// <param name="Status">
/// The overall verdict, carrying the distinction contract C-10 requires between "not ready" and
/// "unhealthy": <c>Healthy</c> is fully ready and is the ONLY verdict reported with 200;
/// <c>Degraded</c> is not ready but not failed, for instance a service still completing its startup
/// validation; <c>Unhealthy</c> has failed. The latter two are both reported with 503, because both
/// mean not ready and the orchestration readiness gate observes the status code - this member is what
/// keeps them distinguishable.
/// </param>
/// <param name="Service">
/// The reporting service, so that Gateway's aggregate can name each upstream individually rather
/// than returning one opaque verdict. Always <c>dataservices</c> on this service.
/// </param>
/// <param name="CheckedAt">
/// When this report was produced, so a consumer can detect a stale cached report.
/// </param>
/// <param name="Checks">
/// The components behind the verdict, named from a CLOSED public vocabulary authored by this service -
/// never from a registration's own check name, and never one entry per registered check. See
/// <c>HealthEndpoints.ProjectChecks</c>: an anonymous body must disclose neither what a registration
/// called itself nor how many registrations exist, and the per-check detail an operator needs is
/// carried on this service's operator telemetry instead.
/// </param>
/// <remarks>
/// <para>
/// This shape mirrors the authored leaf service health schema in
/// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> member for member. It is
/// deliberately NOT an aggregate: Gateway alone combines this report with those of Persistence and
/// Security, and reports healthy only after all three do.
/// </para>
/// <para>
/// Because <c>GET /health</c> is anonymous, everything in this record is public. It therefore carries
/// no upstream address, no credential, no connection string, no certificate, no environment variable
/// value, no configuration value, no host name, no other service's port, no exception text and no
/// stack trace, and no member of it can be used to enumerate internal topology.
/// </para>
/// <para>
/// All four members are declared REQUIRED, and that is a deliberate strengthening rather than a
/// divergence. The authored contract requires only the status and the service, leaving the timestamp
/// and the check list optional so that a leaner report is permitted; this service always populates all
/// four, so declaring them required states what it actually sends. A consumer written against the
/// looser family schema is unaffected, because a stricter guarantee on an existing member cannot break
/// it - whereas declaring an always present member optional would understate the payload.
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
/// registration-supplied check name, a file path, a provider type name or any other internal detail.
/// </param>
/// <param name="Status">
/// This check's own verdict, using the same three tokens as the overall report.
/// </param>
public sealed record ServiceHealthCheck(
    string Name,
    string Status)
{
    /// <summary>
    /// An optional human readable note. FIXED PROSE authored in this file, identical for every
    /// occurrence of a given verdict: it carries no configuration value, no key material, no component
    /// name and no count, and never an exception message or a stack trace.
    /// </summary>
    /// <remarks>
    /// DECLARED AS A PROPERTY WITH AN OMIT WHEN NULL RULE RATHER THAN AS A FOURTH POSITIONAL
    /// PARAMETER, AND BOTH HALVES OF THAT ARE DELIBERATE. This was MEASURED against the generated
    /// OpenAPI specification rather than assumed. As a positional parameter the OpenAPI generator
    /// marked it REQUIRED, because a constructor parameter without a default is required by
    /// definition, while the authored contract in
    /// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> lists only the name and the
    /// status as required and closes the object with additional properties disallowed. A required
    /// nullable member would therefore have been doubly wrong: it diverges from the contract family,
    /// and a body carrying an explicit null for it does not validate against a schema whose type for
    /// it is a plain string. An init only property is not a constructor parameter, so it is generated
    /// as optional, and the serialization rule makes the member ABSENT rather than null when there is
    /// no note - which is exactly what "optional" means on the wire.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}
