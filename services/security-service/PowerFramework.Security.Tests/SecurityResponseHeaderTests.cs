// =====================================================================================================
//  SecurityResponseHeaderTests - THE PROTECTIVE RESPONSE HEADERS, ASSERTED ON REAL RESPONSES
//  ---------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Security.Endpoints.SecurityResponseHeaders, and its installation in
//            PowerFramework.Security/Program.cs.
//
//  WHY THIS FILE EXISTS. Measured on this service before the middleware existed, `POST /v1/tokens` - the
//  one response in the whole estate that IS a credential - answered with only Content-Type, Date, Server
//  and Transfer-Encoding. A bearer token with no cache directive is a token an intermediary is entitled to
//  store and to serve again, and RFC 6749 section 5.1 requires the pair that prevents it. The route
//  assertions below go through the BOOTED HOST rather than the middleware alone, because the defect was
//  never in the header values - it was that nothing set them.
//
//  WHAT IS DELIBERATELY NOT ASSERTED. No case here opens a socket or terminates TLS, so the
//  Strict-Transport-Security arm cannot be reached through the test host - the in-process transport reports
//  a plaintext request, and RFC 6797 section 7.2 forbids asserting transport security over one. That arm is
//  driven directly against a response whose request reports HTTPS, which is the only way to reach it
//  without a listener.
// =====================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Asserts the protective response headers, both against the middleware directly and on responses the
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
    /// be cacheable - a public document, say - can say so itself, and nothing here contradicts it. Without
    /// this property the middleware would be a policy no route could opt out of, which is the shape that
    /// makes a future correct change require editing the middleware instead of the route.
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

    /// <summary>
    /// The credential response the finding named carries the cache directives, through the booted host.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE SUCCESS PATH IS THE ONE THAT MATTERS, so this case obtains an actual token. It presents the
    /// published <c>clientCredential</c> scheme, which is the only credential an in-process host can
    /// present - it terminates no TLS, so no certificate can be handshaked.
    /// </para>
    /// <para>
    /// THE REFUSAL PATH IS ASSERTED IN THE SAME ROW, because a problem document is written by middleware
    /// beneath the handler and would be exactly the response a header-setting middleware placed in the
    /// wrong position would miss.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheTokenResponseCarriesTheCacheDirectivesThroughTheBootedHostAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();
        SecurityClientOptions client = options.Clients[0];

        using HttpClient authenticated = factory.CreateClient();

        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(client.Subject + ":" + SecurityAppFactory.RosterSecret)));

        using HttpResponseMessage issued = await authenticated.PostAsJsonAsync(
            new Uri(options.TokenEndpointPath, UriKind.Relative),
            IssuanceFixture.Body(
                subject: client.Subject,
                audience: client.Audiences[0],
                scopes: [client.Scopes[0]]),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);

        // RFC 6749 section 5.1 - both directives, on the response that IS a credential.
        Assert.True(issued.Headers.CacheControl?.NoStore);
        Assert.True(issued.Headers.CacheControl?.NoCache);
        Assert.Contains(
            SecurityResponseHeaders.PragmaValue,
            issued.Headers.Pragma.Select(static directive => directive.Name),
            StringComparer.Ordinal);
        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            Assert.Single(issued.Headers.GetValues("X-Content-Type-Options")));

        // THE REFUSAL PATH TOO: a problem document written beneath the handler carries them as well.
        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage refused = await anonymous.PostAsJsonAsync(
            new Uri(options.TokenEndpointPath, UriKind.Relative),
            IssuanceFixture.Body(),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.True(refused.Headers.CacheControl?.NoStore);
        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            Assert.Single(refused.Headers.GetValues("X-Content-Type-Options")));
    }

    /// <summary>The anonymous health route carries them too.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// ANONYMOUS IS NOT EXEMPT. The finding named the token route, but the middleware is estate-wide by
    /// design, and the health route is the one a reader is most likely to assume was skipped - so it is
    /// asserted rather than left to inference.
    /// </remarks>
    [Fact]
    public async Task TheAnonymousHealthRouteCarriesTheDirectivesAsync()
    {
        await using SecurityAppFactory factory = new();

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.True(health.Headers.CacheControl?.NoStore);
        Assert.Equal(
            SecurityResponseHeaders.ContentTypeOptionsValue,
            Assert.Single(health.Headers.GetValues("X-Content-Type-Options")));
    }
}
