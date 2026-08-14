// ==================================================================================================
//  THE BOUNDS THIS SERVICE PLACES ON ITS OWN INGRESS
//
//  WHY THIS FILE EXISTS AT ALL, AND WHY IT IS NOT A BEHAVIOUR IMPROVEMENT (constraint C-B)
//  PowerFramework was a LIBRARY. It opened no listening socket, registered no route and received no
//  unsolicited request [Agent Action Plan 0.1.4], so no legacy code path could be flooded by a remote
//  caller and there was nothing for a legacy limit to bound. Decomposition creates the system's
//  first-ever ingress, and the failure mode it creates - an authenticated caller, or one holding a
//  connection open without ever authenticating, consuming this process's memory, its sockets and its
//  CPU until nothing is left for anyone else - is a failure mode the TRANSITION introduced. Handling
//  it is therefore required BY the transition, in exactly the sense the Agent Action Plan uses when it
//  justifies adding outbound resilience: "an in-process call cannot fail in transit and a network call
//  can" [0.5.3]. An in-process call also cannot be issued ten thousand times a second by a stranger.
//
//  NO PACKAGE IS ADDED. `Microsoft.AspNetCore.RateLimiting` and `System.Threading.RateLimiting` are
//  both assemblies of the `Microsoft.AspNetCore.App` shared framework this project already targets, so
//  the limiter below is framework code rather than a dependency - which is what keeps it inside the
//  deliberately-excluded-package list of Agent Action Plan 0.5.3.
//
//  THE TWO LAYERS, AND WHY NEITHER ALONE IS SUFFICIENT
//    * THE TRANSPORT LAYER bounds what a caller can consume BEFORE this service has authenticated it,
//      and therefore before it knows who is asking. A body ceiling, a header ceiling, a connection
//      ceiling, a stream ceiling and a header-completion deadline are all enforced by Kestrel itself,
//      beneath every middleware, which is the only place a bound on UNAUTHENTICATED work can live. The
//      framework's own defaults are far too generous for a service on an internal mesh: 30,000,000
//      bytes of request body and no connection ceiling whatsoever.
//    * THE REQUEST LAYER bounds what an AUTHENTICATED caller can consume, and it is partitioned by
//      principal rather than by address because the unit that must not starve the others is the CALLER,
//      not the socket. Inside a container network every request from one peer shares one address, so an
//      address partition would put an entire upstream service in one bucket and make one caller's
//      excess indistinguishable from its neighbour's. That is why the limiter runs AFTER authentication
//      and why the transport layer above it, not the limiter, is what answers the pre-authentication
//      flood.
//
//  WHAT IS DELIBERATELY NOT BOUNDED HERE. `/health` is exempt, and that exemption is load-bearing
//  rather than a convenience: it is the readiness gate the orchestration layer holds three dependents
//  behind [Agent Action Plan 0.3.2.2], so a rate-limited probe would turn a busy service into an
//  unrecoverably unready one and take the whole stack down with it. It is anonymous by contract
//  (C-10), it reads no caller input, and its cost is bounded by the transport layer like every other
//  request, so exempting it removes no control that was doing work.
//
//  EVERY VALUE IS DECLARED IN appsettings.json RATHER THAN LEFT TO A CODE DEFAULT, for the reason
//  Persistence's own handle-ceiling section records: a bound too low refuses correct callers and one
//  too high delays the discovery of an abuse, and neither is visible from anywhere but the settings
//  file. The defaults below are deliberately CONSERVATIVE - they are sized so that no legitimate
//  documented workflow meets them - and an operator sizing them to a measured workload is expected to
//  move them DOWN.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace PowerFramework.Persistence.Configuration;

/// <summary>
/// The bounds this service places on its own ingress, bound from <c>Gateway:Ingress</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a plain scalar and none is a credential, so the whole group is safe to declare in a
/// committed settings file and safe to quote in a validation failure (constraint C-F).
/// </para>
/// <para>
/// THE SECONDS-VALUED MEMBERS ARE INTEGERS RATHER THAN <see cref="TimeSpan"/>, which follows the
/// spelling the sibling services' handle and pool sections already use. A duration written as a number
/// of seconds needs no format convention to round-trip through an environment variable, which is the
/// ingress an operator overriding one of these will actually use.
/// </para>
/// </remarks>
public sealed class IngressOptions
{
    /// <summary>The configuration section this group binds from.</summary>
    public const string SectionName = "Ingress";

    /// <summary>
    /// The largest request body this listener accepts, in bytes.
    /// </summary>
    /// <remarks>
    /// Kestrel's own default is 30,000,000 bytes. This listener carries both protocols, so the ceiling
    /// applies to a REST body and to a gRPC request frame alike - and for gRPC the message ceiling below
    /// is the tighter of the two, which is the intended layering.
    /// </remarks>
    [Range(4_096, 268_435_456)]
    public long MaxRequestBodyBytes { get; set; } = 16_777_216;

    /// <summary>The largest total request header block this listener accepts, in bytes.</summary>
    /// <remarks>
    /// A bearer token is the only large header this surface expects. The framework default is 32,768
    /// and is restated rather than changed, so the value is visible where the others are.
    /// </remarks>
    [Range(2_048, 1_048_576)]
    public int MaxRequestHeadersTotalBytes { get; set; } = 32_768;

    /// <summary>
    /// The largest number of simultaneously open connections this listener accepts.
    /// </summary>
    /// <remarks>
    /// KESTREL'S DEFAULT IS UNLIMITED, which is the single most consequential value in this file: with
    /// no ceiling, opening connections is a way to exhaust this process's file descriptors without ever
    /// sending a byte of a request, and no middleware can bound it because no middleware runs.
    /// </remarks>
    [Range(8, 100_000)]
    public long MaxConcurrentConnections { get; set; } = 256;

    /// <summary>The largest number of concurrent HTTP/2 streams accepted on one connection.</summary>
    /// <remarks>
    /// LIVE ON THIS SERVICE, unlike on the REST-only ingress: this listener declares Http1AndHttp2 because
    /// gRPC is its primary transport, so a single connection can multiplex many concurrent calls and the
    /// stream ceiling is what bounds how many. The framework default is 100 and is restated here so the
    /// value sits beside the other bounds instead of being invisible.
    /// </remarks>
    [Range(1, 10_000)]
    public int MaxHttp2StreamsPerConnection { get; set; } = 100;

    /// <summary>
    /// How long a connection may take to send its complete request headers, in seconds.
    /// </summary>
    /// <remarks>
    /// The bound on a slow-header attack: a connection that has sent a partial header block holds a
    /// connection slot without ever becoming a request. Kestrel's default is 30 seconds.
    /// </remarks>
    [Range(1, 300)]
    public int RequestHeadersTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// The largest number of requests this service will process at once, across all callers.
    /// </summary>
    /// <remarks>
    /// THE ONE UNPARTITIONED BOUND, and it is unpartitioned on purpose. A per-caller ceiling cannot
    /// bound the total, so a roster of callers each inside its own allowance can still exhaust this
    /// process together. Requests over the ceiling are refused rather than queued, because a queue
    /// deep enough to matter is itself the memory the ceiling exists to protect.
    /// </remarks>
    [Range(1, 100_000)]
    public int MaxConcurrentRequests { get; set; } = 512;

    /// <summary>The number of requests one caller may make per window.</summary>
    /// <remarks>
    /// Partitioned by authenticated principal, falling back to the remote address for a request that
    /// reached the limiter without authenticating. Sized so that no documented workflow meets it.
    /// </remarks>
    [Range(1, 10_000_000)]
    public int RateLimitPermitsPerWindow { get; set; } = 4_000;

    /// <summary>The length of the rate-limit window, in seconds.</summary>
    [Range(1, 3_600)]
    public int RateLimitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// How many requests over the per-caller allowance are queued rather than refused.
    /// </summary>
    /// <remarks>
    /// Zero, and that is the deliberate value: a queued request holds its connection, its buffers and
    /// its authentication result for as long as it waits, so a deep queue converts a refusal this
    /// caller can see and retry into memory this process cannot reclaim.
    /// </remarks>
    [Range(0, 10_000)]
    public int RateLimitQueueLimit { get; set; }

    /// <summary>The largest gRPC request message this server accepts, in bytes.</summary>
    /// <remarks>
    /// The framework's own default is 4 MiB and is restated rather than raised, so the value is visible
    /// beside the others and a later widening is a deliberate edit to a declared bound rather than an
    /// invisible inheritance. It is the TIGHTER of the two bounds a gRPC request meets - the request-body
    /// ceiling above applies first at the transport, this one applies to the decoded message - which is
    /// the intended layering: a caller cannot reach the message decoder with more than the transport
    /// allowed, and cannot reach a handler with more than this allows.
    /// </remarks>
    [Range(4_096, 268_435_456)]
    public int MaxReceiveMessageBytes { get; set; } = 4_194_304;

    /// <summary>The largest gRPC response message this server sends, in bytes.</summary>
    /// <remarks>
    /// THE FRAMEWORK DEFAULT FOR THIS ONE IS UNLIMITED, which is why it is declared. A carrier or a
    /// result chunk assembled from a caller-influenced request can be arbitrarily large, and an
    /// unbounded send ceiling means this process serialises the whole of it into memory before the
    /// transport ever pushes back. Exceeding it faults the call rather than silently truncating it, so a
    /// bound met is a visible failure rather than a wrong answer.
    /// </remarks>
    [Range(4_096, 268_435_456)]
    public int MaxSendMessageBytes { get; set; } = 33_554_432;

    /// <summary>The rate-limit window as a duration.</summary>
    public TimeSpan RateLimitWindow => TimeSpan.FromSeconds(RateLimitWindowSeconds);

    /// <summary>
    /// Applies the transport half of this group to the listener.
    /// </summary>
    /// <param name="kestrel">The listener options being configured.</param>
    /// <exception cref="ArgumentNullException"><paramref name="kestrel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE SERVER HEADER IS SUPPRESSED HERE, and it is the one member of this method that is not a
    /// bound. Kestrel announces itself in a <c>Server</c> response header on every response, which
    /// tells an unauthenticated caller which server implementation to look up advisories for before it
    /// has been granted anything at all (CWE-200). Nothing in the published contract carries the
    /// header, no consumer reads it, and suppressing it at the listener removes it from responses this
    /// service never composes itself - the bearer challenge, an unmatched route, a rejected method -
    /// which a response-header middleware cannot reach as reliably.
    /// </para>
    /// <para>
    /// Applied through <c>ConfigureKestrel</c> rather than by editing the settings file's listener
    /// block, because the estate's configuration coherence suite pins that block to an address and a
    /// protocol set and forbids it carrying transport settings.
    /// </para>
    /// </remarks>
    public void Apply(KestrelServerOptions kestrel)
    {
        ArgumentNullException.ThrowIfNull(kestrel);

        kestrel.AddServerHeader = false;

        kestrel.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        kestrel.Limits.MaxRequestHeadersTotalSize = MaxRequestHeadersTotalBytes;
        kestrel.Limits.MaxConcurrentConnections = MaxConcurrentConnections;
        kestrel.Limits.MaxConcurrentUpgradedConnections = MaxConcurrentConnections;
        kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(RequestHeadersTimeoutSeconds);
        kestrel.Limits.Http2.MaxStreamsPerConnection = MaxHttp2StreamsPerConnection;
    }
}
