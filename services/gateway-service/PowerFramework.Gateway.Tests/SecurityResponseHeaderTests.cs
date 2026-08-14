// =====================================================================================================
//  SecurityResponseHeaderTests - THE PROTECTIVE RESPONSE HEADERS, ASSERTED ON REAL RESPONSES
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Gateway.Endpoints.SecurityResponseHeaders, and its installation in
//            PowerFramework.Gateway/Program.cs.
//
//  WHY THIS FILE EXISTS. This is the SOLE INGRESS - the one service external clients actually reach, and
//  therefore the one whose responses a shared cache is most likely to sit in front of. Measured before the
//  middleware existed, every route here - /health, /v1/ping, /v1/capabilities and every /v1/datawindow/**
//  projection - answered with only Content-Type, Date, Server and Transfer-Encoding.
//
//  WHAT IS DELIBERATELY NOT ASSERTED. No case here opens a socket or terminates TLS, so the
//  Strict-Transport-Security arm cannot be reached through the test host - the in-process transport reports
//  a plaintext request, and RFC 6797 section 7.2 forbids asserting transport security over one. That arm is
//  driven directly against a response whose request reports HTTPS, which is the only way to reach it
//  without a listener.
// =====================================================================================================

using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PowerFramework.Gateway.Endpoints;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Asserts the protective response headers, both against the middleware directly and on a response the
/// booted host actually writes.
/// </summary>
public sealed class SecurityResponseHeaderTests
{
    /// <summary>A response with no directives of its own receives all three.</summary>
    /// <remarks>
    /// THE HTTPS CONDITION IS PART OF THIS CASE. RFC 6797 section 7.2 states that a host must not assert
    /// transport security over an insecure transport, so the header is emitted only where the request
    /// arrived over one - and that is asserted here rather than assumed, because the framework's own HSTS
    /// middleware would have excluded loopback and emitted nothing at all on the estate this repository
    /// runs.
    /// </remarks>
    [Fact]
    public void ABareResponseReceivesEveryDirective()
    {
        DefaultHttpContext secure = new();
        secure.Request.IsHttps = true;

        SecurityResponseHeaders.Apply(secure.Response);

        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            secure.Response.Headers.XContentTypeOptions);
        Assert.Equal(SecurityResponseHeaders.CacheControlValue, secure.Response.Headers.CacheControl);
        Assert.Equal(SecurityResponseHeaders.PragmaValue, secure.Response.Headers.Pragma);
        Assert.Equal(
            SecurityResponseHeaders.StrictTransportSecurityValue,
            secure.Response.Headers.StrictTransportSecurity);

        // OVER PLAINTEXT THE TRANSPORT ASSERTION IS ABSENT, and the other two are not.
        DefaultHttpContext insecure = new();

        SecurityResponseHeaders.Apply(insecure.Response);

        Assert.True(StringValues.IsNullOrEmpty(insecure.Response.Headers.StrictTransportSecurity));
        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            insecure.Response.Headers.XContentTypeOptions);
        Assert.Equal(SecurityResponseHeaders.CacheControlValue, insecure.Response.Headers.CacheControl);
    }

    /// <summary>A directive the route set for itself is left exactly as it set it.</summary>
    /// <remarks>
    /// THIS IS WHAT MAKES THE MIDDLEWARE A FLOOR RATHER THAN A CEILING. An endpoint that ever does need to
    /// be cacheable can say so itself, and nothing here contradicts it. Without this property the
    /// middleware would be a policy no route could opt out of, which is the shape that makes a future
    /// correct change require editing the middleware instead of the route.
    /// </remarks>
    [Fact]
    public void ADirectiveTheRouteSetIsNotOverwritten()
    {
        DefaultHttpContext context = new();
        context.Request.IsHttps = true;

        context.Response.Headers.CacheControl = "public, max-age=300";
        context.Response.Headers.XContentTypeOptions = "nosniff-but-mine";
        context.Response.Headers.StrictTransportSecurity = "max-age=1";

        SecurityResponseHeaders.Apply(context.Response);

        Assert.Equal("public, max-age=300", context.Response.Headers.CacheControl);
        Assert.Equal("nosniff-but-mine", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("max-age=1", context.Response.Headers.StrictTransportSecurity);

        // AND NO PRAGMA IS ADDED beside a cache directive this middleware did not write, because Pragma is
        // the HTTP/1.0 companion of `no-cache` and asserting it beside `public` would contradict the route.
        Assert.True(StringValues.IsNullOrEmpty(context.Response.Headers.Pragma));
    }

    /// <summary>The booted ingress carries the directives on a real response.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THROUGH THE DEPLOYED COMPOSITION ROOT, because the defect was never in the header values - it was
    /// that nothing set them. A case that drove the middleware alone would have passed against the broken
    /// service.
    /// </para>
    /// <para>
    /// THE STATUS IS NOT ASSERTED, DELIBERATELY. This ingress reports healthy only once its three upstreams
    /// do, and no upstream is reachable from an in-process host - so the readiness answer here is legitimately
    /// a refusal. The header assertion holds either way, which is the point: a problem document written
    /// beneath the handler is exactly the response a header middleware in the wrong position would miss.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheBootedIngressCarriesTheDirectivesOnARealResponseAsync()
    {
        await using GatewayTestHostFixture host = new();

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }
}
