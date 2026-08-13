// ==================================================================================================
//  THE INGRESS BOUNDS, WIRED INTO THE HOST
//
//  Configuration/IngressOptions.cs states WHAT the bounds are and argues why bounding a newly created
//  network ingress is required by the decomposition rather than a behaviour improvement. This file is
//  the wiring: it binds that group, applies its transport half to the listener before the host is
//  built, and registers the request-layer limiter that enforces the rest.
//
//  TWO CHAINED LIMITERS, IN THIS ORDER, AND THE ORDER IS THE DESIGN.
//    1. A GLOBAL CONCURRENCY BOUND, one partition for the whole process. It is consulted first because
//       it is the bound that protects this process from its whole caller roster at once; a per-caller
//       allowance cannot, since a roster of callers each inside its own allowance still adds up.
//    2. A PER-CALLER FIXED WINDOW, partitioned by authenticated principal. It is the bound that stops
//       one caller starving the others, which the global bound cannot express.
//  A chained limiter acquires from every link and releases all of them if any link refuses, so a
//  request refused by the window never holds a global permit while it is being refused.
//
//  WHY THE LIMITER RUNS AFTER AUTHENTICATION. Its partition is the CALLER, and the caller is not known
//  until the bearer handler has run. Inside a container network every request from one upstream shares
//  one source address, so partitioning on the address before authentication would put a whole service
//  in one bucket and make one caller's excess indistinguishable from its neighbour's. The cost of that
//  ordering is that a request is authenticated before it is counted, and the transport bounds in
//  IngressOptions.Apply are what answer that: a connection ceiling, a header ceiling and a header
//  deadline all apply beneath every middleware, which is the only place a bound on unauthenticated
//  work can live.
//
//  THE REFUSAL IS CONTRACT-SHAPED. It answers 429 with a ProblemDetails body written through this
//  service's own problem-details service, so the `retCode` member every other failure carries is
//  present here too - E_BUSY, which this service's own status classifier already maps 429 onto. A
//  `Retry-After` header is added whenever the limiter can say when the caller may return.
//
//  gRPC IS EXEMPT FROM THIS LIMITER, AND IS NOT UNBOUNDED. This service's primary transport is gRPC, and
//  a 429 carrying no gRPC status trailer is not a status any conforming gRPC client can surface - it would
//  reach a caller as an opaque transport fault rather than as a refusal it can classify and retry. gRPC
//  calls are therefore bounded at the server INTERCEPTOR instead, by the same two chained limiters over
//  the same configured values, where the refusal is RESOURCE_EXHAUSTED and travels correctly. Exempting
//  them here is what stops a call being counted twice.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace PowerFramework.DataServices.Configuration;

/// <summary>
/// Registers the ingress bounds this service enforces on itself.
/// </summary>
internal static class IngressHardening
{
    /// <summary>The one route exempt from the request-layer limiter.</summary>
    /// <remarks>
    /// The readiness gate the orchestration layer holds this service's dependents behind. Rate-limiting
    /// it would turn a busy service into a permanently unready one.
    /// </remarks>
    internal const string HealthPath = "/health";

    /// <summary>The single partition key the global concurrency bound accounts against.</summary>
    internal const string GlobalPartitionKey = "global";

    /// <summary>The partition key an exempt request is accounted against.</summary>
    internal const string ExemptPartitionKey = "exempt";

    /// <summary>Prefix of a partition key derived from an authenticated principal.</summary>
    internal const string PrincipalPartitionPrefix = "principal:";

    /// <summary>Prefix of a partition key derived from a remote address.</summary>
    internal const string AddressPartitionPrefix = "address:";

    /// <summary>The partition key used when neither a principal nor an address is available.</summary>
    internal const string UnattributedPartitionKey = "unattributed";

    /// <summary>The media-type prefix every gRPC request carries.</summary>
    private const string GrpcContentTypePrefix = "application/grpc";

    /// <summary>The problem title the refusal carries.</summary>
    private const string RefusalTitle = "Too Many Requests";

    /// <summary>The problem detail the refusal carries.</summary>
    /// <remarks>
    /// It names neither the ceiling nor how much of it this caller has consumed. Which bound was met,
    /// and how close another caller is to it, are facts about this deployment's capacity and about its
    /// other callers, and a refusal is not the place to publish either.
    /// </remarks>
    private const string RefusalDetail =
        "The request was refused because an ingress bound was met. Retry after the interval the "
            + "Retry-After header states, if one is present. Neither the bound nor this caller's "
            + "consumption of it is reported.";

    /// <summary>
    /// Binds <c>Ingress</c>, applies its transport half to the listener, and registers the
    /// request-layer limiter.
    /// </summary>
    /// <param name="builder">The host builder being configured.</param>
    /// <returns>The bound group, so a caller can assert on the values that reached the listener.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The configured group violates its own declared ranges. Structural, and therefore fatal: the
    /// listener is configured from these values BEFORE the options graph is validated, so a value the
    /// listener cannot accept must be reported here rather than surfacing as an argument exception from
    /// inside the framework with no configuration key in it.
    /// </exception>
    internal static IngressOptions AddIngressHardening(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // BOUND TWICE, DELIBERATELY, BECAUSE THE TWO CONSUMERS RUN AT DIFFERENT TIMES. The listener is
        // configured while the host is being built, before any options instance can be resolved; the
        // limiter resolves its group from the container. Both read the same section, so they cannot
        // disagree, and the eager copy is range-checked here so that a bad value is reported against
        // its configuration key instead of as a framework argument exception.
        IngressOptions configured =
            builder.Configuration.GetSection(IngressOptions.SectionName).Get<IngressOptions>()
                ?? new IngressOptions();

        ValidateEagerly(configured);

        _ = builder.Services
            .AddOptions<IngressOptions>()
            .Bind(builder.Configuration.GetSection(IngressOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.WebHost.ConfigureKestrel(configured.Apply);

        _ = builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                CreateGlobalConcurrencyLimiter(configured),
                CreatePerCallerWindowLimiter(configured));

            options.OnRejected = WriteRefusalAsync;
        });

        return configured;
    }

    /// <summary>
    /// Installs the request-layer limiter into the pipeline.
    /// </summary>
    /// <param name="app">The application being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be installed AFTER authentication, because the per-caller partition is the authenticated
    /// principal. See this file's banner for why that ordering is the right one and what answers the
    /// pre-authentication flood it leaves to the transport layer.
    /// </remarks>
    internal static void UseIngressHardening(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.UseRateLimiter();
    }

    /// <summary>
    /// Reports whether a request is exempt from the request-layer limiter.
    /// </summary>
    /// <param name="context">The request to classify.</param>
    /// <returns><see langword="true"/> when the request is exempt.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    internal static bool IsExempt(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.Equals(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? contentType = context.Request.ContentType;

        return contentType is not null
            && contentType.StartsWith(GrpcContentTypePrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports the partition a request is accounted against.
    /// </summary>
    /// <param name="context">The request to classify.</param>
    /// <returns>The partition key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The principal is preferred and the address is the fallback, never the reverse: two callers behind
    /// one address must not share an allowance, and one caller reaching this service from two addresses
    /// must not receive two. The prefixes keep the two namespaces apart, so a caller whose subject
    /// happens to spell an address cannot be accounted against that address's bucket.
    /// </remarks>
    internal static string PartitionKeyOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            string? principal =
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.User.FindFirst("sub")?.Value
                    ?? context.User.Identity.Name;

            if (!string.IsNullOrWhiteSpace(principal))
            {
                return PrincipalPartitionPrefix + principal;
            }
        }

        string? address = context.Connection.RemoteIpAddress?.ToString();

        return string.IsNullOrWhiteSpace(address)
            ? UnattributedPartitionKey
            : AddressPartitionPrefix + address;
    }

    /// <summary>Builds the unpartitioned concurrency bound.</summary>
    /// <param name="configured">The bound group.</param>
    /// <returns>A limiter accounting every non-exempt request against one partition.</returns>
    private static PartitionedRateLimiter<HttpContext> CreateGlobalConcurrencyLimiter(
        IngressOptions configured) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsExempt(context)
            ? RateLimitPartition.GetNoLimiter(ExemptPartitionKey)
            : RateLimitPartition.GetConcurrencyLimiter(
                GlobalPartitionKey,
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = configured.MaxConcurrentRequests,
                    QueueLimit = 0,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                }));

    /// <summary>Builds the per-caller fixed window.</summary>
    /// <param name="configured">The bound group.</param>
    /// <returns>A limiter accounting each caller against its own window.</returns>
    private static PartitionedRateLimiter<HttpContext> CreatePerCallerWindowLimiter(
        IngressOptions configured) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsExempt(context)
            ? RateLimitPartition.GetNoLimiter(ExemptPartitionKey)
            : RateLimitPartition.GetFixedWindowLimiter(
                PartitionKeyOf(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = configured.RateLimitPermitsPerWindow,
                    Window = configured.RateLimitWindow,
                    QueueLimit = configured.RateLimitQueueLimit,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));

    /// <summary>
    /// Writes the refusal as a contract-shaped problem document.
    /// </summary>
    /// <param name="context">The rejection, carrying the request and the refused lease.</param>
    /// <param name="cancellationToken">Abandons the write when the caller has already gone.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    private static async ValueTask WriteRefusalAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        // ONLY WHEN THE LIMITER CAN ACTUALLY SAY WHEN. A fabricated interval would be worse than none:
        // a caller that honours it would wait for a window that had already replenished, and one that
        // does not would be refused again immediately with a header that had misinformed it.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        if (http.Response.HasStarted)
        {
            return;
        }

        IProblemDetailsService? problems =
            http.RequestServices.GetService<IProblemDetailsService>();

        if (problems is null)
        {
            return;
        }

        _ = await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = RefusalTitle,
                Detail = RefusalDetail,
            },
        }).ConfigureAwait(false);

        _ = cancellationToken;
    }

    /// <summary>
    /// Range-checks the eagerly bound copy before the listener is configured from it.
    /// </summary>
    /// <param name="configured">The bound group.</param>
    /// <exception cref="InvalidOperationException">Any declared range is violated.</exception>
    private static void ValidateEagerly(IngressOptions configured)
    {
        List<ValidationResult> failures = [];

        if (Validator.TryValidateObject(
            configured,
            new ValidationContext(configured),
            failures,
            validateAllProperties: true))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The '{IngressOptions.SectionName}' section is not usable, so this service will not "
                + "start: the listener is configured from it before the options graph is validated, and "
                + "a bound the listener cannot accept must be reported against its configuration key "
                + "rather than as an argument exception from inside the framework. "
                + string.Join(
                    " ",
                    failures.Select(static failure => failure.ErrorMessage ?? "A bound is out of range.")));
    }
}
