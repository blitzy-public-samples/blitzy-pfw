// ==================================================================================================
//  THE BOUNDS THIS SERVICE PLACES ON ITS OWN INGRESS, AND THE POSTURE IT VERIFIES PEERS WITH
//
//  WHAT THESE ROWS DEFEND, AND WHY EACH NEEDS ITS OWN
//    * THE SERVER HEADER. Kestrel announces its implementation on every response, including responses no
//      application code composes - the bearer challenge, an unmatched route, a rejected method - which
//      tells an unauthenticated caller what to look advisories up for before it has been granted
//      anything (CWE-200). The suppression is a listener setting, so it cannot be observed through the
//      in-process test server: TestServer does not write the header whether or not it is suppressed, so a
//      response assertion would pass with the fix reverted. The listener OPTIONS are what carry it, and
//      those are real registrations resolvable from the host - which is what the rows below read.
//    * THE TRANSPORT BOUNDS. Every one of them replaces a framework default that is either far too
//      generous for a service on an internal mesh (30,000,000 bytes of request body) or absent
//      altogether (no connection ceiling at all, so opening sockets exhausts this process's descriptors
//      without a single complete request being sent).
//    * THE REQUEST-LAYER PARTITION. A bound that accounts every caller against one bucket is a bound one
//      caller can spend on everybody's behalf, and a bound with an unattributed pass-through is a bound
//      with a bypass. Both are asserted, in both directions.
//    This service verifies no internal PEER, so it carries no InternalTls posture; the caller-certificate
//    posture it DOES carry - the revocation mode the listener and the issuance credential now share, and
//    the lifetime ceiling that substitutes for revocation while that mode is NoCheck - has its own suite in
//    CallerCertificatePostureTests.
//
//  NOTHING HERE ASSERTS A PERFORMANCE PROPERTY. The repository publishes no latency budget, no
//  throughput target and no availability commitment (AAP 0.8.5), so no row claims a rate is fast enough
//  or a ceiling generous enough. They assert only that a configured bound is the bound that applies.
//
//  RULES POSITION. No user rules were provided for this project - the rules document says exactly that,
//  and nothing is invented in its place. What governs this file is the enterprise baseline of AAP 0.7.2
//  together with the named constraints, of which C-B (no behaviour improvements), C-F (no credential in
//  source) and C-K (document the decisions) apply here and are cited where they do.
// ==================================================================================================

using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The listener bounds, the request-layer partition and the internal-TLS revocation posture.
/// </summary>
public sealed class IngressBoundTests
{
    /// <summary>
    /// The listener does not announce its implementation.
    /// </summary>
    /// <remarks>
    /// Asserted on the OPTIONS rather than on a response, for the reason this file's banner records: the
    /// in-process test server writes no <c>Server</c> header either way, so a response assertion would
    /// pass against the unfixed code and prove nothing.
    /// </remarks>
    [Fact]
    public void TheListenerDoesNotAnnounceItsImplementation()
    {
        KestrelServerOptions kestrel = new();

        Assert.True(kestrel.AddServerHeader, "The framework default is expected to be enabled.");

        new IngressOptions().Apply(kestrel);

        Assert.False(kestrel.AddServerHeader);
    }

    /// <summary>
    /// Every configured transport bound is the bound the listener enforces.
    /// </summary>
    /// <remarks>
    /// Deliberately non-default values, so a row that silently read the framework default instead of the
    /// configured one would fail. The upgraded-connection ceiling is asserted alongside the ordinary one
    /// because a connection that upgrades escapes the first ceiling and would otherwise be unbounded.
    /// </remarks>
    [Fact]
    public void EveryConfiguredTransportBoundReachesTheListener()
    {
        IngressOptions configured = new()
        {
            MaxRequestBodyBytes = 123_456,
            MaxRequestHeadersTotalBytes = 4_096,
            MaxConcurrentConnections = 17,
            MaxHttp2StreamsPerConnection = 7,
            RequestHeadersTimeoutSeconds = 3,
        };

        KestrelServerOptions kestrel = new();

        configured.Apply(kestrel);

        Assert.Equal(123_456, kestrel.Limits.MaxRequestBodySize);
        Assert.Equal(4_096, kestrel.Limits.MaxRequestHeadersTotalSize);
        Assert.Equal(17, kestrel.Limits.MaxConcurrentConnections);
        Assert.Equal(17, kestrel.Limits.MaxConcurrentUpgradedConnections);
        Assert.Equal(TimeSpan.FromSeconds(3), kestrel.Limits.RequestHeadersTimeout);
        Assert.Equal(7, kestrel.Limits.Http2.MaxStreamsPerConnection);
    }

    /// <summary>
    /// The rate-limit window derives from the seconds-valued setting rather than from a second source.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    [InlineData(3_600)]
    public void TheWindowDerivesFromTheConfiguredSeconds(int seconds)
    {
        IngressOptions configured = new() { RateLimitWindowSeconds = seconds };

        Assert.Equal(TimeSpan.FromSeconds(seconds), configured.RateLimitWindow);
    }

    /// <summary>
    /// The shipped defaults are bounds rather than the framework's own values.
    /// </summary>
    /// <remarks>
    /// The two that matter are asserted as strict inequalities against the framework's own behaviour: the
    /// body ceiling must be below Kestrel's 30,000,000-byte default, and a connection ceiling must exist
    /// at all - the framework's default is unlimited, so any finite value is a change of kind rather than
    /// of degree. The exact numbers are a deployment decision and live in the settings file; what a test
    /// can meaningfully hold is that the shipped values are not the unbounded ones.
    /// </remarks>
    [Fact]
    public void TheShippedDefaultsAreActualBounds()
    {
        IngressOptions shipped = new();

        Assert.True(shipped.MaxRequestBodyBytes < 30_000_000);
        Assert.True(shipped.MaxConcurrentConnections > 0);
        Assert.True(shipped.MaxConcurrentRequests > 0);
        Assert.True(shipped.RateLimitPermitsPerWindow > 0);

        // ZERO, AND THE ROW EXISTS BECAUSE THE OPPOSITE IS THE TEMPTING DEFAULT. A queued request holds
        // its connection, its buffers and its authentication result while it waits, so a deep queue turns
        // a refusal the caller can see and retry into memory this process cannot reclaim.
        Assert.Equal(0, shipped.RateLimitQueueLimit);
    }

    /// <summary>
    /// The readiness gate is exempt from the request-layer bound, and nothing else on the REST surface is.
    /// </summary>
    /// <remarks>
    /// The exemption is load-bearing rather than a convenience: <c>/health</c> is the gate the
    /// orchestration layer holds three dependents behind, so a rate-limited probe would turn a busy
    /// service into a permanently unready one and take the stack down with it. The negative rows are what
    /// stop the exemption widening - a prefix match, for instance, would exempt <c>/healthz-probe</c> and
    /// anything else a caller cared to append.
    /// </remarks>
    [Theory]
    [InlineData("/health", true)]
    [InlineData("/HEALTH", true)]
    [InlineData("/health/detail", false)]
    [InlineData("/healthy", false)]
    [InlineData("/v1/ping", false)]
    [InlineData("/v1/capabilities", false)]
    [InlineData("/", false)]
    public void OnlyTheReadinessGateIsExempt(string path, bool expected)
    {
        DefaultHttpContext context = new();

        context.Request.Path = path;

        Assert.Equal(expected, IngressHardening.IsExempt(context));
    }

    /// <summary>
    /// A gRPC request is exempt here because it is bounded at the interceptor instead.
    /// </summary>
    /// <remarks>
    /// Gateway publishes no gRPC contract, so this arm is inert on this service and is asserted anyway:
    /// the classifier is written identically in all four services, and a divergence in the one where it
    /// does nothing is exactly the divergence nobody would notice.
    /// </remarks>
    [Theory]
    [InlineData("application/grpc", true)]
    [InlineData("application/grpc+proto", true)]
    [InlineData("APPLICATION/GRPC", true)]
    [InlineData("application/json", false)]
    [InlineData(null, false)]
    public void AGrpcRequestIsExemptFromTheRequestLayerBound(string? contentType, bool expected)
    {
        DefaultHttpContext context = new();

        context.Request.Path = "/v1/ping";
        context.Request.ContentType = contentType;

        Assert.Equal(expected, IngressHardening.IsExempt(context));
    }

    /// <summary>
    /// The partition is the authenticated principal when there is one.
    /// </summary>
    /// <remarks>
    /// The principal is preferred over the address because inside a container network every request from
    /// one upstream shares one address: accounting on the address would put a whole service in one bucket
    /// and make one caller's excess indistinguishable from its neighbour's.
    /// </remarks>
    [Fact]
    public void ThePartitionIsThePrincipalWhenThereIsOne()
    {
        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "caller-a")], "Bearer")),
        };

        Assert.Equal(
            IngressHardening.PrincipalPartitionPrefix + "caller-a",
            IngressHardening.PartitionKeyOf(context));
    }

    /// <summary>
    /// The protocol claim name is read too, because the bearer handler is configured not to map it.
    /// </summary>
    /// <remarks>
    /// This service sets <c>MapInboundClaims</c> false, so a token's subject arrives spelled <c>sub</c>
    /// rather than as the framework's long claim type. A partitioner that read only the mapped spelling
    /// would silently account every real caller as unattributed - one shared bucket for the whole roster.
    /// </remarks>
    [Fact]
    public void ThePartitionReadsTheProtocolSubjectClaim()
    {
        DefaultHttpContext context = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "caller-b")], "Bearer")),
        };

        Assert.Equal(
            IngressHardening.PrincipalPartitionPrefix + "caller-b",
            IngressHardening.PartitionKeyOf(context));
    }

    /// <summary>
    /// Two callers hold two partitions, and one caller from two addresses holds one.
    /// </summary>
    /// <remarks>
    /// The property the partition exists for, asserted directly rather than inferred from the two rows
    /// above: separate callers must not share an allowance, and one caller must not be granted two.
    /// </remarks>
    [Fact]
    public void EachCallerHoldsExactlyOnePartitionRegardlessOfAddress()
    {
        static DefaultHttpContext Requesting(string subject, string address) => new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "Bearer")),
            Connection = { RemoteIpAddress = System.Net.IPAddress.Parse(address) },
        };

        Assert.NotEqual(
            IngressHardening.PartitionKeyOf(Requesting("caller-a", "10.0.0.1")),
            IngressHardening.PartitionKeyOf(Requesting("caller-b", "10.0.0.1")));

        Assert.Equal(
            IngressHardening.PartitionKeyOf(Requesting("caller-a", "10.0.0.1")),
            IngressHardening.PartitionKeyOf(Requesting("caller-a", "10.0.0.2")));
    }

    /// <summary>
    /// An unauthenticated request is accounted against its address, and an unattributable one against a
    /// shared bucket - never passed through.
    /// </summary>
    /// <remarks>
    /// A bound that stops applying whenever attribution fails is a bound with a bypass, so the fallback
    /// chain has no arm that returns nothing. The prefixes keep the two namespaces apart, so a caller
    /// whose subject happens to spell an address cannot be accounted against that address's bucket.
    /// </remarks>
    [Fact]
    public void AnUnattributedRequestIsStillAccounted()
    {
        DefaultHttpContext addressed = new()
        {
            Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.9") },
        };

        Assert.Equal(
            IngressHardening.AddressPartitionPrefix + "10.0.0.9",
            IngressHardening.PartitionKeyOf(addressed));

        DefaultHttpContext anonymous = new();

        Assert.Equal(
            IngressHardening.UnattributedPartitionKey,
            IngressHardening.PartitionKeyOf(anonymous));

        // The namespaces cannot collide: a subject spelling an address resolves to the principal bucket.
        DefaultHttpContext impostor = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "10.0.0.9")], "Bearer")),
        };

        Assert.NotEqual(
            IngressHardening.PartitionKeyOf(addressed),
            IngressHardening.PartitionKeyOf(impostor));
    }

}

/// <summary>
/// The same bounds, read from the host the deployment actually runs.
/// </summary>
/// <remarks>
/// The unit rows above prove the options type applies what it is given. THIS class proves the host asks
/// it to: a composition root that never called the registration would satisfy every row above and ship
/// an unbounded listener. It is the wiring proof, and it is also the agreement proof - the values it
/// reads are the ones <c>appsettings.json</c> declares, so a settings file and an options type that
/// drifted apart would fail here.
/// </remarks>
public sealed class IngressBoundHostTests
{

    /// <summary>
    /// The host this deployment runs suppresses the server header and carries every declared bound.
    /// </summary>
    /// <remarks>
    /// <c>ConfigureKestrel</c> registers a plain options configuration, so the listener options are
    /// resolvable from the container even though the in-process test host substitutes the server itself.
    /// That is what makes this assertion possible at all, and what makes it a genuine wiring proof rather
    /// than a restatement of the unit rows.
    /// </remarks>
    [Fact]
    public void TheDeployedHostAppliesTheDeclaredBounds()
    {
        // Booted per row rather than shared through a class fixture, because the host type this suite
        // provides is internal and an xUnit class fixture would have to be named in a public constructor.
        using SecurityAppFactory host = new();
        using IServiceScope scope = host.Services.CreateScope();

        KestrelServerOptions kestrel = scope.ServiceProvider
            .GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value;

        Assert.False(kestrel.AddServerHeader);

        IngressOptions declared = scope.ServiceProvider
            .GetRequiredService<IOptions<IngressOptions>>()
            .Value;

        Assert.Equal(declared.MaxRequestBodyBytes, kestrel.Limits.MaxRequestBodySize);
        Assert.Equal(declared.MaxRequestHeadersTotalBytes, kestrel.Limits.MaxRequestHeadersTotalSize);
        Assert.Equal(declared.MaxConcurrentConnections, kestrel.Limits.MaxConcurrentConnections);
        Assert.Equal(
            TimeSpan.FromSeconds(declared.RequestHeadersTimeoutSeconds),
            kestrel.Limits.RequestHeadersTimeout);
        Assert.Equal(
            declared.MaxHttp2StreamsPerConnection,
            kestrel.Limits.Http2.MaxStreamsPerConnection);

        // AND THEY ARE BOUNDS RATHER THAN THE FRAMEWORK'S OWN VALUES. Without this the row would pass on a
        // host that had bound the section to nothing at all and compared two sets of defaults.
        Assert.NotNull(kestrel.Limits.MaxConcurrentConnections);
        Assert.True(kestrel.Limits.MaxRequestBodySize < 30_000_000);
    }
}
