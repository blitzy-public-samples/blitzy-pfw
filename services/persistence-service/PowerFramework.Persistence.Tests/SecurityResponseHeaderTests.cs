// =====================================================================================================
//  SecurityResponseHeaderTests - THE PROTECTIVE RESPONSE HEADERS, ASSERTED ON REAL RESPONSES
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Persistence.Endpoints.SecurityResponseHeaders, and its installation in
//            PowerFramework.Persistence/Program.cs.
//
//  WHY THIS FILE EXISTS. Measured before the middleware existed, this service's REST surface - /health and
//  /v1/ping - answered with only Content-Type, Date, Server and Transfer-Encoding. This is the only service
//  in the estate that holds a storage provider, so a cached response of its is a cached row.
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
using PowerFramework.Persistence.Endpoints;
using Xunit;

namespace PowerFramework.Persistence.Tests;

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

    /// <summary>The booted service carries the directives on a real response.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THROUGH THE DEPLOYED COMPOSITION ROOT, because the defect was never in the header values - it was
    /// that nothing set them. A case that drove the middleware alone would have passed against the broken
    /// service. The status is not asserted: this host provisions no schema, so the readiness answer is
    /// legitimately a refusal, and the header assertion holds either way.
    /// </remarks>
    [Fact]
    public async Task TheBootedServiceCarriesTheDirectivesOnARealResponseAsync()
    {
        using CompositionHost host = CompositionHost.Create();

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
