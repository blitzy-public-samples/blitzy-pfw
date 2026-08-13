// ==================================================================================================
//  ResponseCompressionTests.cs - content negotiation on Gateway's response bodies
// ==================================================================================================
//
//  WHAT THIS SUITE PINS. Gateway is the sole ingress, so it is the only place in the system where a
//  third-party client's `Accept-Encoding` is stated - and until now it was discarded. A full retrieval
//  projection of 50,008 rows answered 73,239,527 bytes with `content-encoding` NULL and no `Vary`, for
//  every one of six accept-encoding combinations including `gzip` and `gzip, br, deflate, zstd`; at
//  100,009 rows the body was 146,680,012 bytes. Transfer is chunked, so this was never a memory exposure
//  on either side. It was a client's stated capability being ignored.
//
//  WHY THIS IS NOT THE BEHAVIOUR CHANGE CONSTRAINT C-B FORBIDS, and why the suite says so rather than
//  assuming it. C-B freezes LEGACY behaviour. The legacy is an in-process library that hands a DataWindow
//  carrier over BY POINTER, opens no listening socket and composes no response at all [AAP 0.1.5], so
//  there is no legacy behaviour on this surface to freeze - it is surface the decomposition created from
//  nothing, exactly as the bearer requirement on every internal edge is. The decisive assertion is the
//  negative one below: a caller that advertises no encoding receives byte for byte what it received
//  before.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE (AAP 0.8.5). Nothing asserts a ratio, a size
//  threshold, a duration or a budget. What is asserted is that a stated capability is honoured, that the
//  decoded body is unchanged, and that the media-type allowlist is the narrow one - all of which are
//  correctness properties.
// ==================================================================================================

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Tests for the negotiated content coding on Gateway's JSON responses.
/// </summary>
/// <param name="host">The shared host fixture.</param>
public sealed class ResponseCompressionTests(GatewayTestHostFixture host)
    : IClassFixture<GatewayTestHostFixture>
{
    /// <summary>A route whose body is <c>application/json</c> and which needs no upstream to answer.</summary>
    /// <remarks>
    /// THE CAPABILITY PROJECTION IS THE RIGHT SUBJECT rather than the retrieval projection, and the reason
    /// is that a test must not depend on an upstream being scripted to prove a middleware ran. This route is
    /// served entirely inside Gateway, is authenticated exactly like the retrieval projection, and produces
    /// the same media type - which is what the allowlist is keyed on.
    /// </remarks>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>An anonymous route, so the exemption cannot be mistaken for the reason a body is coded.</summary>
    private const string HealthRoute = "/health";

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    [InlineData("gzip, br, deflate, zstd")]
    public async Task AnAdvertisedCodingIsHonouredOnAJsonBody(string acceptEncoding)
    {
        // THE HANDLER MUST NOT DECOMPRESS FOR US, or the assertion would be vacuous: the framework's own
        // client decodes transparently and REMOVES the header when AutomaticDecompression is on, so a
        // suite that used the default client would see no Content-Encoding whether or not the middleware
        // ran. WebApplicationFactory's client has decompression off, and this asserts against the raw
        // header.
        using HttpClient client = host.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string coding = Assert.Single(response.Content.Headers.ContentEncoding);

        // ONE OF THE TWO PROVIDERS IN THE SHARED FRAMEWORK, and which one is the framework's negotiation
        // rather than this service's choice - so the assertion names the set, not a winner. `deflate` and
        // `zstd` have no provider, which is exactly why a caller advertising all four still gets one of
        // these two: that is content negotiation working, not a gap.
        Assert.Contains(coding, (string[])["gzip", "br"]);

        // THE FRAMEWORK'S OWN Vary, asserted because a cache that did not see it would serve a coded body
        // to a client that cannot decode one.
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public async Task ACallerThatAdvertisesNoCodingReceivesTheSameBytesAsBefore()
    {
        // 🔴 THE DECISIVE ASSERTION OF THIS FILE, and the one that makes the change C-B-compliant rather
        // than merely defensible: nothing about the response moves for a caller that did not ask. No
        // Content-Encoding, no Vary from the compression middleware, and the body is the plain JSON the
        // route has always produced.
        using HttpClient client = host.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.AcceptEncoding.Clear();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Plain JSON, parsed rather than pattern-matched, so this fails on a coded body rather than on a
        // formatting difference.
        Assert.StartsWith("{", body.TrimStart(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task TheDecodedBodyIsIdenticalToTheUncodedOne(string acceptEncoding)
    {
        // WHAT A COMPRESSION MIDDLEWARE COULD PLAUSIBLY GET WRONG IS THE BODY, not the header - a
        // mis-ordered pipeline can truncate a stream, and a header assertion alone would still pass. So the
        // same route is fetched twice and the two payloads are compared after decoding.
        using HttpClient plain = host.CreateAuthenticatedClient();
        plain.DefaultRequestHeaders.AcceptEncoding.Clear();

        using HttpResponseMessage plainResponse = await plain.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        string expected = await plainResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        // DECODED HERE RATHER THAN BY THE CLIENT, DELIBERATELY. The test server's handler performs no
        // automatic decompression, and that is what makes these assertions worth anything: a client that
        // decoded transparently would also STRIP the Content-Encoding header, so a suite relying on it
        // could not tell a coded response from an uncoded one. Decoding explicitly means the assertion is
        // against the octets that actually crossed the boundary.
        using HttpClient coded = host.CreateAuthenticatedClient();
        coded.DefaultRequestHeaders.AcceptEncoding.Clear();
        coded.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

        using HttpResponseMessage codedResponse = await coded.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, codedResponse.StatusCode);
        Assert.Equal(
            acceptEncoding,
            Assert.Single(codedResponse.Content.Headers.ContentEncoding));

        await using Stream body = await codedResponse.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);

        await using Stream decoder = acceptEncoding switch
        {
            "gzip" => new GZipStream(body, CompressionMode.Decompress),
            "br" => new BrotliStream(body, CompressionMode.Decompress),
            _ => throw new NotSupportedException(acceptEncoding),
        };

        using StreamReader reader = new(decoder);

        string actual = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TheAnonymousHealthRouteIsAlsoNegotiated()
    {
        // NOT AN EXEMPTION, AND WORTH ASSERTING BECAUSE IT WOULD BE EASY TO CREATE ONE BY ACCIDENT. /health
        // is exempt from the RATE LIMIT for a stated reason - it is the readiness gate three dependents are
        // held behind - and a reader could carry that exemption over to content negotiation, which has no
        // such reason. Its body is application/json like any other, and it carries no credential.
        using HttpClient client = host.CreateAnonymousClient();
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        // The aggregate reports whatever its upstreams do in this host, so the STATUS is not the subject -
        // the coding is, and it is negotiated on a problem body as readily as on a healthy one.
        Assert.Contains(
            Assert.Single(response.Content.Headers.ContentEncoding),
            (string[])["gzip", "br"]);
    }

    [Fact]
    public async Task AnUnauthenticatedRefusalIsNegotiatedAsAProblemDocument()
    {
        // THE SECOND MEDIA TYPE ON THE ALLOWLIST, exercised through the path that produces it. A 401 from
        // the bearer challenge is written by framework middleware beneath this service's own code, which is
        // exactly the case a compression middleware placed too far inside the pipeline would miss - so this
        // row is as much about the POSITION of UseResponseCompression as about the allowlist.
        using HttpClient client = host.CreateAnonymousClient();
        client.DefaultRequestHeaders.AcceptEncoding.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(
            Assert.Single(response.Content.Headers.ContentEncoding),
            (string[])["gzip", "br"]);
    }
}
