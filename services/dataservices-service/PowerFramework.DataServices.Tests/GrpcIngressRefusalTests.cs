// ==================================================================================================
//  WHAT A REFUSED gRPC CALL IS TOLD, AND WHAT IT IS DELIBERATELY NOT TOLD
//
//  THE FINDING THESE ROWS CLOSE (issue INFO-3). Both transports of this service are bounded from the one
//  `Ingress` section, but only one of them used to say WHEN to come back. The request-layer limiter reads
//  the refused lease's retry metadata and emits `Retry-After` [Configuration/IngressHardening.cs], while
//  the gRPC limiter disposed that same lease and threw ResourceExhausted with nothing on it - so a caller
//  reaching this service over its PRIMARY transport learned strictly less from an identical refusal than
//  one reaching the REST projection beside it. Worse, the refusal read the same as a SESSION CEILING
//  refusal, whose remedy is the opposite one, so a client could not tell "wait" from "close something".
//
//  THE THREE PROPERTIES BEING PINNED, each with its own row because each fails independently:
//    * THE INTERVAL IS CARRIED WHEN THERE IS ONE. Read from the refused lease BEFORE it is disposed -
//      the order is the whole fix, because reading it afterwards does not fault, it simply answers
//      nothing, which is exactly how the hint went missing.
//    * THE INTERVAL IS ABSENT WHEN THERE IS NONE, and that is honest rather than incomplete. The chained
//      CONCURRENCY link frees a permit when a call in flight finishes, which is not a time; a fabricated
//      interval would be worse than none, because a caller honouring it would wait for a window that had
//      already replenished.
//    * IT IS ROUNDED UP, NEVER TRUNCATED. A sub-second remainder truncates to `0`, and "retry after 0
//      seconds" sends the caller straight back into the refusal it was just given.
//
//  NOTHING HERE ASSERTS A PERFORMANCE PROPERTY. The repository publishes no latency budget, no
//  throughput target and no availability commitment [Agent Action Plan 0.8.5], so no row below claims a
//  bound is generous enough or a rate fast enough. They assert only that a configured bound is the bound
//  that applies, and that a refusal reports what the caller needs and nothing about this deployment's
//  capacity or about its other callers.
//
//  WHY THERE IS NO LEGACY COUNTERPART TO PORT (constraint C-B). The legacy framework is an in-process
//  library with no listener at all [AAP 0.1.4]: no legacy path could be flooded by a stranger, so there
//  is no legacy refusal to preserve and no legacy behaviour these rows could be contradicting. Bounding
//  a newly created ingress - and reporting that bound coherently across both transports of the same
//  service - is required BY the decomposition rather than an improvement layered on top of it.
// ==================================================================================================

using System.Globalization;
using System.Threading.RateLimiting;

using Grpc.Core;

using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The refusal the gRPC ingress bound answers with, and the retry hint it carries.
/// </summary>
public sealed class GrpcIngressRefusalTests
{
    /// <summary>
    /// A refusal from the per-caller window states when the caller may come back.
    /// </summary>
    /// <remarks>
    /// The bound is one permit per window so that the SECOND call is refused deterministically, and the
    /// window is long enough that no replenishment can race the assertion. What is asserted is the shape
    /// - a trailer, present, parsing as whole seconds inside the configured window - not a particular
    /// number, because the number is a function of when in the window the call arrived.
    /// </remarks>
    [Fact]
    public async Task ARefusalFromThePerCallerWindowStatesWhenToComeBack()
    {
        using GrpcIngressLimiter limiter = Build(concurrency: 64, permits: 1, windowSeconds: 300);
        IngressCall caller = new("ipv4:127.0.0.1:42001");

        using RateLimitLease admitted = await limiter.AcquireAsync(caller);

        Assert.True(admitted.IsAcquired);

        RpcException refused = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using RateLimitLease beyond = await limiter.AcquireAsync(caller);
        });

        Assert.Equal(StatusCode.ResourceExhausted, refused.StatusCode);
        Assert.Equal(GrpcIngressLimiter.RefusalDetail, refused.Status.Detail);

        string? hint = refused.Trailers.GetValue(GrpcIngressLimiter.RetryAfterTrailer);

        Assert.NotNull(hint);
        Assert.True(
            int.TryParse(hint, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds),
            $"The hint must be whole seconds a caller can act on, and was '{hint}'.");
        Assert.InRange(seconds, 1, 300);
    }

    /// <summary>
    /// The stated interval is never the zero that truncation would produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ONE-SECOND WINDOW MAKES THE ROUNDING DIRECTION OBSERVABLE, which is why it is worth the
    /// deliberately small value: whatever the limiter reports is strictly inside one second, so
    /// truncation yields <c>0</c> and rounding up yields <c>1</c>. The row therefore fails against a
    /// truncating implementation and passes against this one, with no dependence on how long the row
    /// itself takes to run.
    /// </para>
    /// <para>
    /// The loop is not a retry of a flaky assertion. One permit per one-second window means an acquire
    /// succeeds at most once per window, so a refusal arrives on the second attempt; the loop exists only
    /// so that a scheduling pause between the two attempts - which could let the window replenish and
    /// admit the second call - costs one more iteration instead of a false failure.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheStatedIntervalIsNeverTheZeroTruncationWouldProduce()
    {
        using GrpcIngressLimiter limiter = Build(concurrency: 64, permits: 1, windowSeconds: 1);
        IngressCall caller = new("ipv4:127.0.0.1:42002");

        RpcException? refused = null;

        for (int attempt = 0; attempt < 64 && refused is null; attempt++)
        {
            try
            {
                using RateLimitLease lease = await limiter.AcquireAsync(caller);
            }
            catch (RpcException thrown)
            {
                refused = thrown;
            }
        }

        Assert.NotNull(refused);
        Assert.Equal("1", refused.Trailers.GetValue(GrpcIngressLimiter.RetryAfterTrailer));
    }

    /// <summary>
    /// A refusal from the concurrency link states no interval, because it has none to state.
    /// </summary>
    /// <remarks>
    /// The second caller is a DIFFERENT peer, so its own window is untouched and the only link that can
    /// refuse it is the global concurrency one. Asserting the trailer collection is EMPTY rather than
    /// merely lacking this one name is deliberate: it pins that nothing else was invented to fill the
    /// gap.
    /// </remarks>
    [Fact]
    public async Task ARefusalFromTheConcurrencyLinkStatesNoIntervalBecauseItHasNoneToState()
    {
        using GrpcIngressLimiter limiter = Build(concurrency: 1, permits: 10_000, windowSeconds: 300);
        IngressCall holder = new("ipv4:127.0.0.1:42003");
        IngressCall other = new("ipv4:127.0.0.1:42004");

        using RateLimitLease held = await limiter.AcquireAsync(holder);

        Assert.True(held.IsAcquired);

        RpcException refused = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using RateLimitLease beyond = await limiter.AcquireAsync(other);
        });

        Assert.Equal(StatusCode.ResourceExhausted, refused.StatusCode);
        Assert.Equal(GrpcIngressLimiter.RefusalDetail, refused.Status.Detail);
        Assert.Null(refused.Trailers.GetValue(GrpcIngressLimiter.RetryAfterTrailer));
        Assert.Empty(refused.Trailers);
    }

    /// <summary>
    /// The refusal points at the trailer and reports neither the bound nor this caller's consumption.
    /// </summary>
    /// <remarks>
    /// The digit test is the assertion that matters: a detail carrying any number would be quoting either
    /// the ceiling or how much of it has been spent, and both are facts about this deployment's capacity
    /// and about its other callers rather than about the refused call.
    /// </remarks>
    [Fact]
    public void TheRefusalNamesTheTrailerAndQuotesNoNumber()
    {
        Assert.Contains(
            GrpcIngressLimiter.RetryAfterTrailer,
            GrpcIngressLimiter.RefusalDetail,
            StringComparison.Ordinal);
        Assert.False(
            GrpcIngressLimiter.RefusalDetail.Any(char.IsDigit),
            "A refusal must not quote the bound or the caller's consumption of it.");

        // THE SAME NAME AND UNITS AS THE REST SURFACE'S HEADER, so a caller reading one transport learns
        // the same fact the same way on the other. Lower case because HTTP/2 header names are.
        Assert.Equal("retry-after", GrpcIngressLimiter.RetryAfterTrailer);
        Assert.Equal(
            GrpcIngressLimiter.RetryAfterTrailer,
            GrpcIngressLimiter.RetryAfterTrailer.ToLowerInvariant());
    }

    /// <summary>
    /// Traffic inside the bound is admitted, and one caller's refusal is not another's.
    /// </summary>
    /// <remarks>
    /// The regression this row guards is the one a bound most easily introduces: refusing work that was
    /// always meant to pass. The second half is the per-caller partition doing its job - a bound one
    /// caller can spend on everybody's behalf is a denial of service with a configuration key.
    /// </remarks>
    [Fact]
    public async Task TrafficInsideTheBoundIsAdmittedAndOneCallersRefusalIsNotAnothers()
    {
        using GrpcIngressLimiter limiter = Build(concurrency: 64, permits: 3, windowSeconds: 300);
        IngressCall busy = new("ipv4:127.0.0.1:42005");
        IngressCall quiet = new("ipv4:127.0.0.1:42006");

        for (int admitted = 0; admitted < 3; admitted++)
        {
            using RateLimitLease lease = await limiter.AcquireAsync(busy);

            Assert.True(lease.IsAcquired);
        }

        _ = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using RateLimitLease beyond = await limiter.AcquireAsync(busy);
        });

        using RateLimitLease unaffected = await limiter.AcquireAsync(quiet);

        Assert.True(unaffected.IsAcquired);
    }

    /// <summary>
    /// A call this server cannot attribute is still accounted, against its peer or one shared bucket.
    /// </summary>
    /// <remarks>
    /// A bound that stops applying whenever attribution fails is a bound with a bypass. There is no
    /// authenticated principal on a hand-built call context - reading the request raises, which the
    /// production accessor treats as a fact about the caller rather than a fault - so these two rows
    /// exercise exactly the fallback path a caller that presents no token takes.
    /// </remarks>
    [Fact]
    public void ACallThatCannotBeAttributedIsStillAccounted()
    {
        Assert.Equal(
            GrpcIngressLimiter.PeerPartitionPrefix + "ipv4:127.0.0.1:42007",
            GrpcIngressLimiter.PartitionKeyOf(new IngressCall("ipv4:127.0.0.1:42007")));
        Assert.Equal(
            GrpcIngressLimiter.UnattributedPartitionKey,
            GrpcIngressLimiter.PartitionKeyOf(new IngressCall(string.Empty)));
    }

    /// <summary>
    /// An ingress refusal and a session-ceiling refusal stay tellable apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THE FINDING ASKED FOR. Both conditions are capacity refusals and both reach a REST
    /// caller as 429 - <c>E_BUSY</c> projects to <c>ResourceExhausted</c> and then to 429, which
    /// <c>RestProjectionInBandStatusTests</c> pins - but their remedies are opposite: an ingress refusal
    /// clears on its own and should be retried after the stated interval, whereas a session-ceiling
    /// refusal clears only when a caller CLOSES a session, and retrying it merely spends ingress
    /// allowance.
    /// </para>
    /// <para>
    /// The two are told apart by shape, and both halves are asserted here rather than in separate files
    /// so that a change to either cannot quietly collapse the distinction. The ingress refusal is OUT OF
    /// BAND, as an <see cref="RpcException"/> carrying the interval; the admission ceiling is answered IN
    /// BAND, with no exception at all, as <c>E_BUSY</c> on the response message and with no trailer of any
    /// kind.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnIngressRefusalAndASessionCeilingRefusalStayTellableApart()
    {
        using GrpcIngressLimiter limiter = Build(concurrency: 64, permits: 1, windowSeconds: 300);
        IngressCall caller = new("ipv4:127.0.0.1:42008");

        using RateLimitLease admitted = await limiter.AcquireAsync(caller);

        RpcException ingress = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            using RateLimitLease beyond = await limiter.AcquireAsync(caller);
        });

        Assert.Equal(StatusCode.ResourceExhausted, ingress.StatusCode);
        Assert.NotNull(ingress.Trailers.GetValue(GrpcIngressLimiter.RetryAfterTrailer));

        DataServicesOptions options = new();
        options.Sessions.ValidationSession.MaxConcurrentSessions = 1;

        ValidationSessionRegistry registry = new(options);

        Assert.True(registry.Open("dw-ceiling").IsOpened);

        ValidationSessionOpenResult refused = registry.Open("dw-ceiling");

        Assert.False(refused.IsOpened);
        Assert.Null(refused.Session);
        Assert.Equal(RetCode.E_BUSY, refused.ReturnCode);
    }

    /// <summary>
    /// Builds the chained bound over explicit values, so a row that read a default would fail.
    /// </summary>
    /// <param name="concurrency">The global concurrency permit count.</param>
    /// <param name="permits">The per-caller permits per window.</param>
    /// <param name="windowSeconds">The window length, in seconds.</param>
    /// <returns>The limiter, which the caller disposes.</returns>
    private static GrpcIngressLimiter Build(int concurrency, int permits, int windowSeconds)
    {
        IngressOptions configured = new()
        {
            MaxConcurrentRequests = concurrency,
            RateLimitPermitsPerWindow = permits,
            RateLimitWindowSeconds = windowSeconds,

            // NO QUEUE, DELIBERATELY. A queued call is neither admitted nor refused, and this suite
            // asserts what a REFUSAL says; the shipped default is zero for the same reason.
            RateLimitQueueLimit = 0,
        };

        return new GrpcIngressLimiter(Options.Create(configured));
    }
}

/// <summary>
/// A call context carrying nothing but a transport peer.
/// </summary>
/// <remarks>
/// There is no request behind it, which is the point: the production partition resolver reads the
/// authenticated principal when there is one and falls back to the peer, and a hand-built context is how
/// that fallback is reached without standing a host up. Members beyond the peer, the cancellation token
/// and the trailers are never touched on this path.
/// </remarks>
/// <param name="peer">The transport peer the call is attributed to, or empty for none.</param>
/// <param name="cancellationToken">The token the limiter's acquire observes.</param>
internal sealed class IngressCall(string peer, CancellationToken cancellationToken = default)
    : ServerCallContext
{
    /// <inheritdoc/>
    protected override string MethodCore => "/dataservices.v1.DataWindowService/Retrieve";

    /// <inheritdoc/>
    protected override string HostCore => "localhost:5102";

    /// <inheritdoc/>
    protected override string PeerCore => peer;

    /// <inheritdoc/>
    protected override DateTime DeadlineCore => DateTime.MaxValue;

    /// <inheritdoc/>
    protected override Metadata RequestHeadersCore { get; } = [];

    /// <inheritdoc/>
    protected override CancellationToken CancellationTokenCore => cancellationToken;

    /// <inheritdoc/>
    protected override Metadata ResponseTrailersCore { get; } = [];

    /// <inheritdoc/>
    protected override Status StatusCore { get; set; }

    /// <inheritdoc/>
    protected override WriteOptions? WriteOptionsCore { get; set; }

    /// <inheritdoc/>
    protected override AuthContext AuthContextCore { get; } =
        new("ingress", new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

    /// <inheritdoc/>
    protected override ContextPropagationToken CreatePropagationTokenCore(
        ContextPropagationOptions? options) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}
