// ==================================================================================================
//  ResponseCompressionTests.cs - content negotiation on the REST projection, and its exclusion of gRPC
// ==================================================================================================
//
//  WHAT THIS SUITE PINS, AND WHY THE SECOND HALF MATTERS MORE THAN THE FIRST. This service hosts C-03 and
//  C-04 as gRPC AND a thin REST projection of them, on ONE port. Enabling response compression for the
//  projection therefore puts a compression middleware in the path of every gRPC call as well, and gRPC
//  carries its own framing and its own per-message compression negotiation. The media-type allowlist is
//  what keeps them apart: it names only `application/json` and `application/problem+json`, and a gRPC
//  response is always `application/grpc`. The exclusion is asserted here rather than assumed, because a
//  later widening of that list to the framework's defaults would break the gRPC edge in a way no JSON
//  assertion would catch.
//
//  WHY THIS IS NOT THE BEHAVIOUR CHANGE C-B FORBIDS. C-B freezes LEGACY behaviour, and the legacy composes
//  no response at all - it is an in-process library handing a carrier over by pointer [AAP 0.1.5]. The
//  decisive assertion is the negative one: a caller that advertises no coding receives byte for byte what
//  it received before.
//
//  NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE (AAP 0.8.5). No ratio, no size threshold, no
//  duration and no budget is asserted.
// ==================================================================================================

using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using PowerFramework.Contracts.DataServices.V1;
using Xunit;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Tests for the negotiated content coding on this service's REST projection.
/// </summary>
/// <param name="host">The shared host fixture.</param>
public sealed class ResponseCompressionTests(DataServicesTestHostFactory host)
    : IClassFixture<DataServicesTestHostFactory>
{
    /// <summary>The anonymous readiness route, whose body is JSON and carries no credential.</summary>
    private const string HealthRoute = "/health";

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task AnAdvertisedCodingIsHonouredAndDecodesToTheUncodedBody(string acceptEncoding)
    {
        using HttpClient plain = host.CreateAnonymousClient();
        plain.DefaultRequestHeaders.AcceptEncoding.Clear();

        using HttpResponseMessage plainResponse = await plain.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Empty(plainResponse.Content.Headers.ContentEncoding);

        string expected = await plainResponse.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        using HttpClient coded = host.CreateAnonymousClient();
        coded.DefaultRequestHeaders.AcceptEncoding.Clear();
        coded.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);

        using HttpResponseMessage codedResponse = await coded.GetAsync(
            new Uri(HealthRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            acceptEncoding,
            Assert.Single(codedResponse.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", codedResponse.Headers.Vary);

        // DECODED EXPLICITLY. The test server's handler performs no automatic decompression, which is what
        // keeps the header assertion above meaningful - a transparently decoding client also strips the
        // header, so a suite relying on one could not distinguish a coded response from an uncoded one.
        await using Stream body = await codedResponse.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken);

        await using Stream decoder = acceptEncoding switch
        {
            "gzip" => new GZipStream(body, CompressionMode.Decompress),
            "br" => new BrotliStream(body, CompressionMode.Decompress),
            _ => throw new NotSupportedException(acceptEncoding),
        };

        using StreamReader reader = new(decoder);

        Assert.Equal(expected, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AGrpcCallIsUnaffectedEvenWhenTheChannelAdvertisesACoding()
    {
        // 🔴 THE ASSERTION THAT PROTECTS THE OTHER HALF OF THIS PORT. A gRPC response carries
        // application/grpc, which is not on the allowlist, so the middleware must leave it entirely alone -
        // framing, trailers and status included. Driven through a REAL channel over the test server rather
        // than by calling the service class directly, because the property under test is a MIDDLEWARE
        // property and calling the class would bypass the pipeline completely.
        using GrpcChannel channel = host.CreateAuthenticatedGrpcChannel();

        DataWindowService.DataWindowServiceClient client = new(channel);

        Metadata headers = new() { { "accept-encoding", "gzip, br" } };

        // WHAT IS ASSERTED IS THAT THE CALL COMPLETED AS gRPC AND ITS MESSAGE DESERIALIZED. A middleware
        // that had compressed an application/grpc response would corrupt the length-prefixed framing, and
        // the client would fault on the frame rather than hand back a message at all - so a readable
        // message carrying a defined in-band outcome is the whole proof.
        //
        // A BLANK HANDLE IS SENT DELIBERATELY. This operation refuses one IN BAND with
        // E_INVALID_ARGUMENT rather than as a status, which makes it the ideal subject: the refusal lives in
        // the message BODY, so reading it back correctly proves the body survived the pipeline intact, and
        // no session handle is created for the row to have to release.
        OpenValidationSessionResponse opened = await client.OpenValidationSessionAsync(
            new OpenValidationSessionRequest(),
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            (WireRetCode)(int)PowerFramework.Shared.Kernel.RetCode.E_INVALID_ARGUMENT,
            opened.RetCode);
        Assert.Empty(opened.SessionId);
    }
}
