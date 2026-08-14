// ==================================================================================================
//  THE INGRESS BOUND FOR THIS SERVICE'S PRIMARY TRANSPORT
//
//  WHY gRPC IS NOT BOUNDED BY THE REQUEST-LAYER MIDDLEWARE. The framework's rate-limiting middleware
//  answers a refusal as HTTP 429 with no gRPC status trailer, and a response with no `grpc-status` is
//  not something a conforming gRPC client can classify: it surfaces as an opaque transport fault rather
//  than as a refusal the caller can recognise, log meaningfully and retry. So the middleware exempts
//  every request carrying a gRPC media type - see Configuration/IngressHardening.cs - and this file is
//  the other half of that decision rather than an omission from it.
//
//  THE SAME TWO CHAINED BOUNDS, OVER THE SAME CONFIGURED VALUES, IN THE SAME ORDER. A global
//  concurrency bound protects this process from its whole caller roster at once; a per-caller fixed
//  window stops one caller starving the others. Neither expresses the other, which is why both are
//  present here exactly as they are on the REST surface. The values come from the one
//  `Ingress` section, so the two transports cannot be bounded differently by accident.
//
//  THE REFUSAL IS RESOURCE_EXHAUSTED, which is the canonical gRPC status for a bound met rather than a
//  fault: a caller distinguishes it from UNAVAILABLE (retry the call) and from ABORTED (the status this
//  service forwards when Persistence reports an update conflict), so adding it takes no meaning away
//  from either. It names neither the bound nor this caller's consumption of it, for the reason the REST
//  refusal gives: both are facts about this deployment's capacity and its other callers.
//
//  THE LIMITER IS A SINGLETON AND THE INTERCEPTOR IS NOT, AND THAT SPLIT IS LOAD BEARING. gRPC
//  activates an interceptor registered by type ONCE PER CALL, so an interceptor that owned its limiter
//  would build a fresh empty limiter for every call and bound nothing at all. The limiter therefore
//  lives in the container as a singleton and the per-call interceptor resolves it.
//
//  BOUNDING A NEWLY CREATED INGRESS IS REQUIRED BY THE DECOMPOSITION, NOT A BEHAVIOUR IMPROVEMENT
//  (constraint C-B). The legacy was an in-process library with no listener [Agent Action Plan 0.1.4]:
//  no legacy path could be flooded by a stranger, so there is no legacy limit to port and no legacy
//  behaviour to preserve here. See Configuration/IngressOptions.cs for the argument in full.
// ==================================================================================================

using System.Globalization;
using System.Threading.RateLimiting;

using Grpc.Core;
using Grpc.Core.Interceptors;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;

namespace PowerFramework.DataServices.Grpc;

/// <summary>
/// The chained ingress bound every gRPC call on this server is accounted against.
/// </summary>
/// <remarks>
/// Registered as a singleton. See this file's banner for why the interceptor cannot own it.
/// </remarks>
internal sealed class GrpcIngressLimiter : IDisposable
{
    /// <summary>The single partition key the global concurrency bound accounts against.</summary>
    internal const string GlobalPartitionKey = "global";

    /// <summary>Prefix of a partition key derived from an authenticated principal.</summary>
    internal const string PrincipalPartitionPrefix = "principal:";

    /// <summary>Prefix of a partition key derived from a transport peer.</summary>
    internal const string PeerPartitionPrefix = "peer:";

    /// <summary>The partition key used when neither a principal nor a peer is available.</summary>
    internal const string UnattributedPartitionKey = "unattributed";

    /// <summary>
    /// The trailer name a refusal carries its retry interval on, in whole seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SAME NAME AND THE SAME UNITS AS THE REST SURFACE'S HEADER, DELIBERATELY.</b> The
    /// request-layer limiter already emits <c>Retry-After</c> in whole seconds from the same limiter
    /// metadata [<c>Configuration/IngressHardening.cs</c>], so a caller reading one transport learns the
    /// same fact the same way on the other. gRPC declares no standard for this - the alternative would be a
    /// <c>google.rpc.RetryInfo</c> detail, which needs a package this refactor deliberately does not add
    /// (AAP 0.5.3) - so an ASCII trailer named for the header it mirrors is the least surprising form.
    /// </para>
    /// <para>
    /// <b>IT IS WHAT MAKES THE TWO REFUSALS TELLABLE APART, WHICH IS THE POINT (issue INFO-3).</b> Both this
    /// bound and the per-caller HANDLE ceiling reach a caller as <c>ResourceExhausted</c> - and at the REST
    /// edge as 429 - but the remedies are opposite: an ingress refusal clears on its own and should be
    /// retried after the stated interval, whereas a handle refusal clears only when the caller RELEASES a
    /// handle and retrying changes nothing. The handle refusal deliberately carries no interval, and names
    /// <c>Handles:MaxPerPrincipal</c> in its own diagnostic instead, so the presence of this trailer is the
    /// discriminator.
    /// </para>
    /// </remarks>
    internal const string RetryAfterTrailer = "retry-after";

    /// <summary>The status detail a refused call carries.</summary>
    /// <remarks>
    /// IT POINTS AT THE TRAILER RATHER THAN QUOTING THE INTERVAL, and the conditional wording is not
    /// hedging: the limiter reports an interval only when it can actually say when, so a detail that
    /// promised one unconditionally would be wrong for the chained concurrency link. Neither the bound nor
    /// this caller's consumption of it is reported, because both are facts about this deployment's capacity
    /// and about its other callers.
    /// </remarks>
    internal const string RefusalDetail =
        "The call was refused because an ingress bound was met. Retry after the interval the retry-after "
            + "trailer states, if one is present. Neither the bound nor this caller's consumption of it is "
            + "reported.";

    private readonly PartitionedRateLimiter<ServerCallContext> _limiter;

    /// <summary>
    /// Builds the chained bound from the configured ingress group.
    /// </summary>
    /// <param name="options">The bound <c>Ingress</c> group.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public GrpcIngressLimiter(IOptions<IngressOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        IngressOptions configured = options.Value;

        _limiter = PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<ServerCallContext, string>(
                _ => RateLimitPartition.GetConcurrencyLimiter(
                    GlobalPartitionKey,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = configured.MaxConcurrentRequests,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    })),
            PartitionedRateLimiter.Create<ServerCallContext, string>(
                context => RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKeyOf(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configured.RateLimitPermitsPerWindow,
                        Window = configured.RateLimitWindow,
                        QueueLimit = configured.RateLimitQueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    })));
    }

    /// <summary>
    /// Reports the partition a call is accounted against.
    /// </summary>
    /// <param name="context">The call to classify.</param>
    /// <returns>The partition key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The principal is preferred and the transport peer is the fallback, never the reverse: two callers
    /// arriving from one address must not share an allowance, and one caller arriving from two addresses
    /// must not receive two. The prefixes keep the namespaces apart, so a subject that happens to spell a
    /// peer address cannot be accounted against that peer's bucket.
    /// </remarks>
    internal static string PartitionKeyOf(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        HttpContext? http = HttpContextOf(context);

        if (http?.User.Identity?.IsAuthenticated == true)
        {
            string? principal =
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? http.User.FindFirst("sub")?.Value
                    ?? http.User.Identity.Name;

            if (!string.IsNullOrWhiteSpace(principal))
            {
                return PrincipalPartitionPrefix + principal;
            }
        }

        // NO PASS-THROUGH WHEN THE PRINCIPAL IS UNAVAILABLE. A call this server cannot attribute is
        // still accounted - against its transport peer, and failing that against one shared bucket -
        // because a bound that stops applying whenever attribution fails is a bound with a bypass.
        string? peer = context.Peer;

        return string.IsNullOrWhiteSpace(peer)
            ? UnattributedPartitionKey
            : PeerPartitionPrefix + peer;
    }

    /// <summary>
    /// Reports the request behind a call, or <see langword="null"/> when there is none to read.
    /// </summary>
    /// <param name="context">The call to read.</param>
    /// <returns>The request, or <see langword="null"/>.</returns>
    /// <remarks>
    /// THE CATCH IS THE PUBLIC API'S OWN CONTRACT RATHER THAN DEFENSIVE PADDING. The accessor's only way
    /// of reporting "this call was not served over HTTP" is to raise, and a call context constructed
    /// directly - which is how every unit test in this service's suite drives a service method - is
    /// exactly that case. Reading the request is therefore attempted and its absence is a fact about the
    /// caller, not a fault: the partition falls back to the transport peer, so a call this server cannot
    /// attribute is still accounted rather than being let through unbounded.
    /// </remarks>
    private static HttpContext? HttpContextOf(ServerCallContext context)
    {
        try
        {
            return context.GetHttpContext();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotImplementedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Takes a permit for one call, or refuses it.
    /// </summary>
    /// <param name="context">The call being admitted.</param>
    /// <returns>The lease, which the caller must dispose when the call completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">
    /// A bound was met. <see cref="StatusCode.ResourceExhausted"/>, carrying a detail that reports
    /// neither the bound nor this caller's consumption of it, and - when the limiter could state one - a
    /// <see cref="RetryAfterTrailer"/> trailer carrying the retry interval in whole seconds.
    /// </exception>
    internal async ValueTask<RateLimitLease> AcquireAsync(ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        RateLimitLease lease = await _limiter
            .AcquireAsync(context, permitCount: 1, context.CancellationToken)
            .ConfigureAwait(false);

        if (lease.IsAcquired)
        {
            return lease;
        }

        // 🔴 THE INTERVAL IS READ BEFORE THE LEASE IS RELEASED, AND THE ORDER IS THE WHOLE FIX (issue
        //    INFO-3). The limiter puts its retry interval on the REFUSED LEASE, so the disposal below is the
        //    last moment it can be read - and reading it afterwards is not a bug that shows up as an
        //    exception, it simply answers nothing. That is exactly what used to happen: the REST surface
        //    emitted `Retry-After` from this same metadata while the gRPC surface discarded it, so one
        //    transport told a caller when to come back and the other did not.
        //
        //    ONLY WHEN THE LIMITER CAN ACTUALLY SAY WHEN, on the same terms the REST path states: a
        //    fabricated interval would be worse than none, because a caller that honoured it would wait for
        //    a window that had already replenished and one that ignored it would be refused again at once
        //    with a hint that had misinformed it. The chained CONCURRENCY link carries no interval - a
        //    permit frees when a call in flight finishes, which is not a time - so a refusal from that link
        //    legitimately arrives without one.
        bool hasInterval = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter);

        // RELEASED BEFORE REFUSING. A refused lease still holds whatever the chain acquired before the
        // link that said no, and a lease left undisposed on the refusal path leaks a permit per refusal -
        // which turns a flood into a permanent outage rather than a temporary refusal.
        lease.Dispose();

        // THE TYPE NAME IS SPELLED OUT RATHER THAN TARGET-TYPED, DELIBERATELY. UpdateServiceTests'
        // ThisTypeIsTheSoleAbortedThrowSiteInTheService counts the EXPLICIT status constructions in this
        // file by source scan, to pin the interceptor to exactly one status and stop a later edit
        // repurposing it into a general-purpose throw site. A target-typed construction is the same thing
        // to the compiler and invisible to that guard, so this spelling is what keeps the guard load
        // bearing. One status, two throw sites: the only difference between them is the trailer.
        Status status = new Status(StatusCode.ResourceExhausted, RefusalDetail);

        if (!hasInterval)
        {
            throw new RpcException(status);
        }

        // WHOLE SECONDS, ROUNDED UP, which is the REST header's own form: a caller that waits the stated
        // interval must land AFTER the window replenishes, and truncation would land it just before.
        Metadata trailers = new()
        {
            {
                RetryAfterTrailer,
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
            },
        };

        throw new RpcException(status, trailers);
    }

    /// <inheritdoc/>
    public void Dispose() => _limiter.Dispose();
}

/// <summary>
/// Accounts every gRPC call on this server against the chained ingress bound.
/// </summary>
/// <remarks>
/// Activated once per call by the gRPC hosting layer, which is why it resolves the singleton limiter
/// rather than owning one. All four call shapes are intercepted: a bound that applied to unary calls
/// alone would be bypassed by every streaming contract this service publishes.
/// </remarks>
internal sealed class GrpcIngressLimitInterceptor : Interceptor
{
    private readonly GrpcIngressLimiter _limiter;

    /// <summary>Creates an interceptor over the process-wide bound.</summary>
    /// <param name="limiter">The singleton bound.</param>
    /// <exception cref="ArgumentNullException"><paramref name="limiter"/> is <see langword="null"/>.</exception>
    public GrpcIngressLimitInterceptor(GrpcIngressLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(limiter);

        _limiter = limiter;
    }

    /// <inheritdoc/>
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        using RateLimitLease lease = await _limiter.AcquireAsync(context).ConfigureAwait(false);

        return await continuation(request, context).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        using RateLimitLease lease = await _limiter.AcquireAsync(context).ConfigureAwait(false);

        return await continuation(requestStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        using RateLimitLease lease = await _limiter.AcquireAsync(context).ConfigureAwait(false);

        await continuation(request, responseStream, context).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        using RateLimitLease lease = await _limiter.AcquireAsync(context).ConfigureAwait(false);

        await continuation(requestStream, responseStream, context).ConfigureAwait(false);
    }
}
