// =====================================================================================================
//  F-15 - INCREMENTAL STREAMING AND THE ELEMENT BOUND AT THE INGRESS
// =====================================================================================================
//
//  WHY THE GATEWAY STREAMS WHERE THE DATASERVICES PROJECTION COLLECTS. Both answer a server-streaming gRPC
//  method as one JSON document, but they trade differently and the asymmetry is deliberate. The
//  DataServices projection collects first so that a mid-stream upstream failure can still produce a clean
//  problem body. Here the buffering would cost far more, because the gateway is the process EVERY request
//  in the system passes through - so each element is formatted and written as it arrives and one element is
//  live at a time.
//
//  THE PRICE, RECORDED HONESTLY RATHER THAN GLOSSED. Once the status line and headers are sent they cannot
//  be changed, so a fault after the first element ends the body WITHOUT its closing bracket. A caller
//  detects that as malformed JSON, which is a DETECTABLE failure. What this deliberately never produces is
//  a short array that closes cleanly, because that is indistinguishable from a complete one.
//
//  THE BOUND IS A RESOURCE BOUND AND NOT A PERFORMANCE CLAIM (AAP 0.8.5).
// =====================================================================================================

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Endpoints;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The incremental streaming and element bound of the proxied server-stream projection.
/// </summary>
public sealed class DataServicesProxyStreamingTests
{
    /// <summary>A field the JSON mapping emits exactly once per projected element.</summary>
    private const string ElementMarker = "chunkIndex";

    /// <summary>
    /// A sequence within the bound is written as a single well-formed JSON array.
    /// </summary>
    /// <remarks>
    /// The shape is asserted from the actual response bytes rather than from an intermediate collection,
    /// because what a caller receives is the bytes: a separator written in the wrong place or a missing
    /// bracket is invisible to any assertion made before serialization.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ASequenceWithinTheBoundIsWrittenAsOneWellFormedArray(int elements)
    {
        (string body, int pulled) = await StreamAsync(elements, maximumElements: 10);

        Assert.StartsWith("[", body, StringComparison.Ordinal);
        Assert.EndsWith("]", body, StringComparison.Ordinal);
        Assert.Equal(elements, CountElements(body));
        Assert.Equal(elements, pulled);
    }

    /// <summary>
    /// The sequence is never drained whole: the producer is stopped at the bound.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE INCREMENTALITY CLAIM, AND IT IS THE ONE THAT WOULD OTHERWISE BE PROSE.</b> An
    /// implementation that materialized the sequence and then trimmed it would satisfy every byte-level
    /// assertion in this file while still holding an unbounded response in the gateway's memory. Counting
    /// how many elements the producer was ASKED for is what separates the two: an upstream that keeps
    /// producing must be stopped, not merely ignored.
    /// </remarks>
    [Fact]
    public async Task TheProducerIsStoppedAtTheBoundRatherThanDrainedAndTrimmed()
    {
        (string body, int pulled) = await StreamAsync(elements: 50, maximumElements: 3);

        // Pulled at most one past the bound - the element that revealed the crossing. Never all fifty.
        Assert.InRange(pulled, 3, 4);
        Assert.Equal(3, CountElements(body));
    }

    /// <summary>
    /// Exceeding the bound abandons the document WITHOUT its closing bracket.
    /// </summary>
    /// <remarks>
    /// <b>THE ABSENT BRACKET IS THE POINT, NOT AN OVERSIGHT.</b> The status line was already sent, so the
    /// refusal cannot become a problem response; leaving the array unterminated is what makes the caller
    /// detect an incomplete answer. Closing it would hand back a short array that reads as complete, which
    /// is the one outcome worse than an error.
    /// </remarks>
    [Fact]
    public async Task ExceedingTheBoundAbandonsTheDocumentWithoutItsClosingBracket()
    {
        (string body, _) = await StreamAsync(elements: 10, maximumElements: 2);

        Assert.StartsWith("[", body, StringComparison.Ordinal);

        // Tested as "does not END with a bracket" rather than "contains none": an element's own payload
        // carries brackets of its own - a chunk's row list is an array - so a containment test would pass
        // for the wrong reason and fail once an element's shape changed.
        Assert.False(
            body.TrimEnd().EndsWith(']'),
            "The abandoned document must not be terminated, or a short array reads as a complete one.");

        Assert.Equal(2, CountElements(body));
    }

    /// <summary>
    /// A caller that has gone away stops the forwarding rather than draining the upstream for nobody.
    /// </summary>
    /// <remarks>
    /// An abandoned response is the ordinary way a streamed request ends early, and continuing to pull from
    /// the upstream after it would spend the whole system's most contended process on a body nobody will
    /// read.
    /// </remarks>
    [Fact]
    public async Task AnAbortedRequestStopsTheForwarding()
    {
        using CancellationTokenSource aborted = new();

        // Cancelled synchronously: the async overload accepts a token the analyzer requires threading, and
        // the token this test needs cancelled is the one being cancelled - so the synchronous form is the
        // honest expression of the intent rather than a token laundered through an unrelated one.
        aborted.Cancel();

        int pulled = 0;

        DefaultHttpContext context = NewContext();
        context.RequestAborted = aborted.Token;

        DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk> result =
            new(Produce(50, () => pulled++, TestContext.Current.CancellationToken), maximumElements: 10);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.ExecuteAsync(context));

        Assert.InRange(pulled, 0, 1);
    }

    /// <summary>Runs the projection and returns the response body with the producer's pull count.</summary>
    /// <param name="elements">How many elements the upstream offers.</param>
    /// <param name="maximumElements">The configured bound.</param>
    /// <returns>The body text and the number of elements the producer was asked for.</returns>
    private static async Task<(string Body, int Pulled)> StreamAsync(int elements, int maximumElements)
    {
        int pulled = 0;

        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk> result =
            new(Produce(elements, () => pulled++, TestContext.Current.CancellationToken), maximumElements);

        await result.ExecuteAsync(context);

        await context.Response.BodyWriter.FlushAsync(TestContext.Current.CancellationToken);

        return (Encoding.UTF8.GetString(body.ToArray()), pulled);
    }

    /// <summary>Builds a context with the services the projection resolves for its diagnostics.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewContext()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/datawindow/retrieve";

        return context;
    }

    /// <summary>A lazy producer that records how many elements it was asked for.</summary>
    /// <param name="count">How many it will offer.</param>
    /// <param name="onPull">Called once per element produced.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// LAZY BY CONSTRUCTION, WHICH IS WHAT MAKES THE PULL COUNT MEANINGFUL. An eager collection would report
    /// every element as pulled the moment it was built and could prove nothing about incrementality.
    /// </remarks>
    private static async IAsyncEnumerable<RetrieveChunk> Produce(
        int count,
        Action onPull,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            onPull();

            await Task.Yield();

            yield return new RetrieveChunk { ChunkIndex = index + 1 };
        }
    }

    /// <summary>Counts the encoded elements in a written body.</summary>
    /// <param name="body">The body text.</param>
    /// <returns>The element count.</returns>
    /// <remarks>
    /// Counted by a field the protobuf JSON mapping emits EXACTLY ONCE PER ELEMENT rather than by splitting
    /// on the separator or counting braces: an element's own payload contains both commas and braces, so
    /// either of those would count the wrong thing and would drift the moment an element's shape changed.
    /// The chunk ordinal is always present because the projection sets it on every chunk from one.
    /// </remarks>
    private static int CountElements(string body)
    {
        int elements = 0;

        for (int index = body.IndexOf(ElementMarker, StringComparison.Ordinal);
            index >= 0;
            index = body.IndexOf(ElementMarker, index + ElementMarker.Length, StringComparison.Ordinal))
        {
            elements++;
        }

        return elements;
    }
}
