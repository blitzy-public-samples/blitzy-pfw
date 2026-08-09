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
//     time the report was produced, and the name plus verdict of each readiness check. It carries no
//     upstream address, no credential, no connection string, no certificate, no environment variable
//     value, no configuration value, no host name, no other service's port, no exception text and no
//     stack trace. Three shared framework members are available on every check result and all three
//     are deliberately dropped rather than projected: Exception (an exception message is not authored
//     for disclosure and routinely carries connection strings, addresses and file paths), Data (an
//     arbitrary bag a check may fill with anything at all) and Duration (see item 6). Only
//     Description is echoed, because the authored contract models exactly that member and requires
//     its author to keep it free of configuration values and key material.
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
//     The legacy analogue is REFERENCE ONLY and is not reproduced here. ws_objects/pfw.pbl.src/
//     pfw.sra:L111-L144 unpacks a seven field assert payload split on a carriage return line feed
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
//  7. WHY Degraded IS 200 AND ONLY Unhealthy IS 503. Contract C-10 requires the response to
//     distinguish "not ready" from "unhealthy", because a service still completing its startup
//     validation is not the same as one whose dependency has failed and an operator must be able to
//     tell them apart. The authored contract fixes the mapping and the reason: Healthy and Degraded
//     are both reported with 200 "so a probe that tears down on any non-2xx does not kill a service
//     that is merely still starting", while Unhealthy is reported with 503. This is also the shared
//     framework's own default result status code mapping, so the two agree rather than compete.
//     Anything that is neither Healthy nor Degraded fails CLOSED to 503.
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

        **"Not ready" and "unhealthy" are different states.** `Healthy` and `Degraded` are both
        reported with 200, so a probe that treats any non-2xx as a failure does not tear down a
        service that is merely still completing its startup validation. `Unhealthy` is reported with
        503 and a problem document that names the failing check and carries the legacy PowerFramework
        return code.

        **The body is public, and is scoped accordingly.** Because the endpoint is anonymous it
        discloses no upstream address, no credential, no connection string, no certificate, no
        configuration value, no host name, no other service's port, no exception text and no stack
        trace.
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
           // produce.
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
    /// HTTP 200 carrying a <see cref="ServiceHealthReport"/> when the verdict is
    /// <c>Healthy</c> or <c>Degraded</c>; HTTP 503 carrying a problem document when it is
    /// <c>Unhealthy</c> or anything unrecognised.
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
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Decision record item 4: the handler reports rather than throws, because a probe that
            // faulted would take the container with it and the readiness gate could never recover.
            // The exception is logged HERE, where the operator can see it, and is deliberately NOT
            // projected onto the anonymous response (item 2). OperationCanceledException is excluded
            // from this catch on purpose: it means the caller disconnected, so there is no longer a
            // response to write and attempting one would only fail again.
            loggerFactory
                .CreateLogger(LoggerCategoryName)
                .LogError(
                    exception,
                    "Readiness evaluation failed for the {Service} service; reporting {Status}.",
                    ServiceIdentifier,
                    StatusUnhealthy);

            status = HealthStatus.Unhealthy;
            checks = [BuildSelfCheck(HealthStatus.Unhealthy)];
        }

        // Fails CLOSED: anything that is neither Healthy nor Degraded is reported as unavailable,
        // including a status value this build does not recognise. Testing for the two ready states
        // rather than against the one failed state is what makes that true (decision record item 7).
        if (status is not (HealthStatus.Healthy or HealthStatus.Degraded))
        {
            return BuildUnavailableProblem(checks);
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
    /// Projects a shared framework health report onto the wire shape the authored contract fixes.
    /// </summary>
    /// <param name="report">The evaluated report.</param>
    /// <returns>
    /// One entry per registered check, preceded by this endpoint's own <c>self</c> entry unless a
    /// registered check already occupies that name.
    /// </returns>
    /// <remarks>
    /// Only <c>Name</c>, <c>Status</c> and <c>Description</c> cross the boundary. The report entry's
    /// <c>Exception</c>, <c>Data</c> and <c>Duration</c> members are dropped for the reasons in
    /// decision record items 2 and 6. An empty or whitespace description becomes
    /// <see langword="null"/> so that it is omitted from the body rather than serialized as noise.
    /// </remarks>
    private static IReadOnlyList<ServiceHealthCheck> ProjectChecks(HealthReport report)
    {
        List<ServiceHealthCheck> projected = new(report.Entries.Count + 1);
        bool selfAlreadyReported = false;

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            // The shared framework registers checks in a case insensitive dictionary, so the
            // collision test is case insensitive too.
            if (string.Equals(entry.Key, SelfCheckName, StringComparison.OrdinalIgnoreCase))
            {
                selfAlreadyReported = true;
            }

            projected.Add(
                new ServiceHealthCheck(entry.Key, ToWireStatus(entry.Value.Status))
                {
                    Description = string.IsNullOrWhiteSpace(entry.Value.Description)
                        ? null
                        : entry.Value.Description,
                });
        }

        if (!selfAlreadyReported)
        {
            projected.Insert(0, BuildSelfCheck(HealthStatus.Healthy));
        }

        return projected;
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
    /// Builds the 503 problem document for an unhealthy verdict.
    /// </summary>
    /// <param name="checks">The projected checks, from which the failing names are taken.</param>
    /// <returns>A problem document result carrying the legacy return code.</returns>
    /// <remarks>
    /// The authored contract requires the failing check to be named in the problem detail and the
    /// legacy PowerFramework return code to travel in the single permitted extension member, rather
    /// than a second error schema being defined for this one response. Check NAMES are identifiers,
    /// which the contract requires their authors to keep free of paths, type names and any other
    /// internal detail; no description, exception or configuration value is carried here.
    /// </remarks>
    private static IResult BuildUnavailableProblem(IReadOnlyList<ServiceHealthCheck> checks)
    {
        List<string> failing = [];

        foreach (ServiceHealthCheck check in checks)
        {
            if (string.Equals(check.Status, StatusUnhealthy, StringComparison.Ordinal))
            {
                failing.Add(check.Name);
            }
        }

        // The empty case is reachable and is handled rather than assumed away: an aggregate can be
        // Unhealthy while no individual entry is, for instance when a check reports Degraded under a
        // registration that treats degradation as failure.
        string detail = failing.Count == 0
            ? "The DataServices service is not ready."
            : "The DataServices service is not ready. Readiness check(s) reporting "
              + StatusUnhealthy + ": " + string.Join(", ", failing) + ".";

        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: UnavailableProblemTitle,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
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
}

/// <summary>
/// The readiness of THIS service only, as published on <c>GET /health</c>.
/// </summary>
/// <param name="Status">
/// The overall verdict, carrying the distinction contract C-10 requires between "not ready" and
/// "unhealthy": <c>Healthy</c> is fully ready and is reported with 200; <c>Degraded</c> is not ready
/// but not failed, for instance a service still completing its startup validation, and is also
/// reported with 200 so that a probe tearing down on any non-2xx does not kill it; <c>Unhealthy</c>
/// has failed and is reported with 503.
/// </param>
/// <param name="Service">
/// The reporting service, so that Gateway's aggregate can name each upstream individually rather
/// than returning one opaque verdict. Always <c>dataservices</c> on this service.
/// </param>
/// <param name="CheckedAt">
/// When this report was produced, so a consumer can detect a stale cached report.
/// </param>
/// <param name="Checks">
/// The individual component checks behind the verdict, present so an operator can tell WHICH check is
/// not yet satisfied rather than only that something is not.
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
/// The check's stable identifier - never a file path, a provider type name or any other internal
/// detail of this service.
/// </param>
/// <param name="Status">
/// This check's own verdict, using the same three tokens as the overall report.
/// </param>
public sealed record ServiceHealthCheck(
    string Name,
    string Status)
{
    /// <summary>
    /// An optional human readable note. Carries no configuration value and no key material, and never
    /// carries an exception message or a stack trace.
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
