// ==================================================================================================
//  HealthEndpoints - GET /health ON THE SECURITY SERVICE, ANONYMOUS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT
//  The readiness probe of contract C-10, as this LEAF service publishes it. Security reports its own
//  readiness and nothing else: it is called BY the other three and calls none of them, so it has no
//  upstream to aggregate. Gateway alone combines this report with those of Persistence and DataServices
//  and reports healthy only after all three do.
//
//  WHY ANONYMOUS, AND WHY THAT IS PARTICULARLY UNAVOIDABLE HERE
//  On any service, the thing that probes this route holds no token and probes precisely while the
//  service is still starting. On THIS service the circularity is total: Security is the sole token
//  issuer, so requiring a token to answer its own readiness probe would mean the probe could not
//  succeed until issuance was live, and issuance is what the probe exists to gate. AllowAnonymous is
//  written EXPLICITLY rather than left to the absence of a policy, because Program.cs installs a
//  fallback policy that requires an authenticated user: omitting a policy here would close this route,
//  not open it.
//
//  THE PROBE IS CHEAP, AND DELIBERATELY DOES NO CRYPTOGRAPHIC WORK
//  It never touches the signing key, never mints a token, never derives or verifies anything. Gateway's
//  readiness is gated on this answer through the orchestration health condition, so a probe that
//  performed key operations would make the readiness gate depend on the most expensive and most
//  sensitive path in the system.
//
//  WHAT THE ANONYMOUS BODY MAY NOT CARRY (constraints C-F and C-G)
//  No key material of any kind, no key identifier, no issuer, no audience, no algorithm name, no
//  environment-variable value, no configuration value, no host name, no other service's port, no
//  exception text and no stack trace. Check NAMES are identifiers only. This is the one route an
//  unauthenticated caller can always reach on the service that holds the system's single signing
//  secret, so it is also the one route where a leak would be both unconditional and maximally costly.
//
//  FAIL CLOSED. Anything that is neither Healthy nor Degraded is reported as unavailable, including a
//  status value this build does not recognise, which is why the test below is for the two ready states
//  rather than against the one failed state.
// ==================================================================================================

using System.Text.Json.Serialization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Declares and serves this service's anonymous readiness probe.
/// </summary>
/// <remarks>
/// The route pattern, the anonymity, the declared responses and the projection onto the wire shape all
/// live here so that a reader comparing the running service against contract C-10 has one file to read.
/// <c>Program.cs</c> owns registration - the authentication scheme, the authorization services and the
/// health-check registry - and deliberately owns nothing about this route.
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>The probe's route, fixed by the environment's readiness gate.</summary>
    private const string HealthRoutePattern = "/health";

    /// <summary>This service's canonical name, as it appears in the report and in Gateway's aggregate.</summary>
    private const string ServiceIdentifier = "security";

    /// <summary>The ready verdict.</summary>
    private const string StatusHealthy = "Healthy";

    /// <summary>The not-ready-but-not-failed verdict, reported with 200 rather than 503.</summary>
    private const string StatusDegraded = "Degraded";

    /// <summary>The failed verdict, reported with 503.</summary>
    private const string StatusUnhealthy = "Unhealthy";

    /// <summary>The name of this endpoint's own contribution to the report.</summary>
    private const string SelfCheckName = "self";

    /// <summary>The operation identifier, matching the published contract's own spelling.</summary>
    private const string OperationName = "getHealth";

    /// <summary>The tag the operation is grouped under.</summary>
    private const string OperationTag = "Health";

    /// <summary>The single extension member the problem document is permitted to carry.</summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>The problem title for the unavailable verdict.</summary>
    private const string UnavailableProblemTitle = "Service Unavailable";

    /// <summary>The logger category a failed readiness evaluation is recorded under.</summary>
    private const string LoggerCategoryName = "PowerFramework.Security.Endpoints.HealthEndpoints";

    /// <summary>The operation summary published on the route.</summary>
    private const string OperationSummary = "Report this service's own readiness. Anonymous.";

    /// <summary>The operation description published on the route.</summary>
    private const string OperationDescription =
        "Reports the readiness of the Security service alone. Security is called by the other three "
        + "services and calls none of them, so this report aggregates no upstream; Gateway combines it "
        + "with those of Persistence and DataServices. Anonymous on all four services - and "
        + "unavoidably so here, because Security is the sole token issuer and a probe that required a "
        + "token could not succeed until issuance was already live. The probe performs no "
        + "cryptographic work and never touches signing material. Degraded is reported with 200 so "
        + "that a probe tearing down on any non-2xx does not kill a service that is merely still "
        + "starting; Unhealthy is reported with 503. The body carries no key, key identifier, issuer, "
        + "audience, algorithm name, configuration value or exception text.";

    /// <summary>
    /// Maps the anonymous readiness probe.
    /// </summary>
    /// <param name="app">The route builder to declare the route on.</param>
    /// <returns>The same <paramref name="app"/> instance, so that mapping calls compose.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(HealthRoutePattern, ReportReadinessAsync)
           // Explicit, and load bearing: this survives the fallback authorization policy Program.cs
           // installs, whereas merely omitting a policy would not.
           .AllowAnonymous()
           .WithName(OperationName)
           .WithTags(OperationTag)
           .WithSummary(OperationSummary)
           .WithDescription(OperationDescription)
           // Exactly two declared responses: the report on 200 and a problem document on 503. The
           // handler returns IResult rather than a typed union so that no additional 500 is inferred
           // from the problem result's own metadata and published as a response this operation cannot
           // produce.
           .Produces<ServiceHealthReport>(StatusCodes.Status200OK)
           .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Evaluates this process's own readiness and projects it onto the wire.
    /// </summary>
    /// <param name="httpContext">
    /// The request context, used only to reach the request-scoped service provider. Nothing about the
    /// request influences the verdict, and nothing about it is echoed back.
    /// </param>
    /// <param name="loggerFactory">
    /// Used exclusively to record a readiness evaluation that failed outright, which is otherwise
    /// invisible because it is reported to the caller as a status rather than raised as an exception.
    /// </param>
    /// <param name="cancellationToken">
    /// The request's abort token, so a disconnected caller stops the evaluation rather than leaving it
    /// running against a response nobody will read.
    /// </param>
    /// <returns>
    /// 200 carrying a <see cref="ServiceHealthReport"/> for <c>Healthy</c> or <c>Degraded</c>; 503
    /// carrying a problem document for <c>Unhealthy</c> or anything unrecognised.
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
            HealthCheckService? healthCheckService =
                httpContext.RequestServices.GetService<HealthCheckService>();

            if (healthCheckService is null)
            {
                // No component check is registered, so this leaf service's whole readiness is the
                // statement that its process is running and answering requests - which the execution
                // of this handler has just demonstrated. A truthful Healthy, not an assumed one.
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
            // The handler REPORTS rather than throws: a probe that faulted would take the container
            // with it and the readiness gate could never recover. The exception is logged here, where
            // an operator can see it, and is deliberately not projected onto the anonymous response -
            // which matters more on this service than on any other, because an exception raised while
            // evaluating readiness could name a key store or a key identifier.
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

        if (status is not (HealthStatus.Healthy or HealthStatus.Degraded))
        {
            return BuildUnavailableProblem(checks);
        }

        // The clock is a substitutable seam rather than a direct read, because the characterization
        // model requires every clock read to be maskable from both the master and the candidate
        // recording. Resolving it optionally honours a deterministic double when the host registers one
        // and needs no registration when it does not.
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
    /// Projects a shared-framework health report onto the wire shape the authored contract fixes.
    /// </summary>
    /// <param name="report">The evaluated report.</param>
    /// <returns>
    /// One entry per registered check, preceded by this endpoint's own <c>self</c> entry unless a
    /// registered check already occupies that name.
    /// </returns>
    /// <remarks>
    /// Only the name, the status and the description cross the boundary. The entry's exception, data and
    /// duration are dropped: the first two can carry internal detail on an anonymous route and the third
    /// is a timing figure this refactor may make no claim about. An empty or whitespace description
    /// becomes <see langword="null"/> so the member is omitted rather than serialized as noise.
    /// </remarks>
    private static IReadOnlyList<ServiceHealthCheck> ProjectChecks(HealthReport report)
    {
        List<ServiceHealthCheck> projected = new(report.Entries.Count + 1);
        bool selfAlreadyReported = false;

        foreach (KeyValuePair<string, HealthReportEntry> entry in report.Entries)
        {
            // The shared framework registers checks in a case-insensitive dictionary, so the collision
            // test is case-insensitive too.
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
    /// <see cref="HealthStatus.Unhealthy"/> when the readiness evaluation itself could not be completed.
    /// </param>
    /// <returns>The <c>self</c> entry.</returns>
    /// <remarks>
    /// The unhealthy description states THAT the evaluation failed and never WHY: the reason is an
    /// exception, the response is anonymous, and the two must not meet. The reason is in the log.
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
    /// The failing check is NAMED in the detail, because an operator reading a failed probe needs to know
    /// which check is not satisfied rather than only that something is not. Names are identifiers, so
    /// nothing here carries a description, an exception or a configuration value. The legacy return code
    /// travels in the single permitted extension member rather than in a second error schema defined for
    /// this one response.
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

        // The empty case is reachable and handled rather than assumed away: an aggregate can be Unhealthy
        // while no individual entry is, for instance when a check reports Degraded under a registration
        // that treats degradation as failure.
        string detail = failing.Count == 0
            ? "The Security service is not ready."
            : "The Security service is not ready. Readiness check(s) reporting "
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
    /// Maps a shared-framework status onto the closed three-token vocabulary the contract publishes.
    /// </summary>
    /// <param name="status">The status to translate.</param>
    /// <returns>One of <c>Healthy</c>, <c>Degraded</c> or <c>Unhealthy</c>.</returns>
    /// <remarks>
    /// Translated through an explicit switch rather than by the enumeration's own string conversion, so
    /// that the published vocabulary is stated in this file and cannot drift silently if a future
    /// framework version renames or adds a member. An unrecognised status maps to <c>Unhealthy</c>, which
    /// is the same fail-closed choice the status-code mapping makes.
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
/// "unhealthy": <c>Healthy</c> is fully ready and is reported with 200; <c>Degraded</c> is not ready but
/// not failed and is also reported with 200, so that a probe tearing down on any non-2xx does not kill a
/// service that is merely still starting; <c>Unhealthy</c> has failed and is reported with 503.
/// </param>
/// <param name="Service">
/// The reporting service, so Gateway's aggregate can name each upstream individually rather than
/// returning one opaque verdict. Always <c>security</c> on this service.
/// </param>
/// <param name="CheckedAt">
/// When this report was produced, so a consumer can detect a stale cached report.
/// </param>
/// <param name="Checks">
/// The individual component checks behind the verdict, present so an operator can tell WHICH check is not
/// yet satisfied rather than only that something is not.
/// </param>
/// <remarks>
/// This shape mirrors the leaf-service health schema in
/// <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c> member for member. It is deliberately
/// NOT an aggregate: Security calls no other service, so it has no upstream to report on.
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
/// The check's stable identifier - never a file path, a provider type name, a key identifier or any other
/// internal detail of this service.
/// </param>
/// <param name="Status">This check's own verdict, using the same three tokens as the overall report.</param>
public sealed record ServiceHealthCheck(
    string Name,
    string Status)
{
    /// <summary>
    /// An optional human-readable note. Carries no configuration value and no key material, and never
    /// carries an exception message or a stack trace.
    /// </summary>
    /// <remarks>
    /// An init-only PROPERTY rather than a fourth positional parameter, deliberately: a constructor
    /// parameter without a default is generated as REQUIRED in the OpenAPI document, while the authored
    /// contract lists only the name and the status as required and closes the object. The serialization
    /// rule then makes the member ABSENT rather than null when there is no note, which is what "optional"
    /// means on the wire.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}
