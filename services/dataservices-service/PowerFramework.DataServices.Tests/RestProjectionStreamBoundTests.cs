// =====================================================================================================
//  F-15 - THE STREAMED-ELEMENT BOUND ON THE REST PROJECTION
// =====================================================================================================
//
//  WHAT THIS PROJECTION IS DOING AND WHY IT NEEDS A BOUND. A server-streaming gRPC method has no single
//  answer, but a REST caller receives ONE document, so the projection must hold the sequence before it can
//  answer at all. That is deliberate - it is what lets a mid-stream upstream failure produce a clean
//  problem body rather than a half-written success - and it is precisely why the buffering must be finite.
//  An unbounded upstream must not be able to make this service hold an unbounded response.
//
//  REFUSE, NEVER TRUNCATE - the property this suite exists to pin. A truncated JSON array is
//  indistinguishable from a complete one, so dropping the tail would answer a retrieval with the WRONG
//  answer under a SUCCESS status, which is worse than either erroring or holding more. The bound therefore
//  raises at the moment it is crossed, which also stops the producer rather than merely ignoring it.
//
//  THE BOUND IS A RESOURCE BOUND AND NOT A PERFORMANCE CLAIM. No latency, throughput or availability
//  objective is asserted anywhere in this refactor, because the repository publishes none (AAP 0.8.5).
// =====================================================================================================

using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Endpoints;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The element bound the projected server streams are collected under.
/// </summary>
public sealed class RestProjectionStreamBoundTests
{
    /// <summary>
    /// A sequence at exactly the bound is accepted whole.
    /// </summary>
    /// <remarks>
    /// THE BOUNDARY IS INCLUSIVE, and it is asserted rather than assumed: an off-by-one here would refuse a
    /// legitimate response that sat exactly on the configured value, which reads to an operator as an
    /// upstream fault rather than as a bound they chose.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(10)]
    public async Task ASequenceAtExactlyTheBoundIsAcceptedWhole(int bound)
    {
        RestProjectionEndpoints.CollectingStreamWriter<RetrieveChunk> writer = new(bound);

        for (int index = 0; index < bound; index++)
        {
            await writer.WriteAsync(new RetrieveChunk { ChunkIndex = index + 1 });
        }

        Assert.Equal(bound, writer.Written.Count);

        // Write order is preserved, because a chunk sequence is ordered and re-ordering it would change the
        // answer rather than merely its shape.
        Assert.Equal(
            Enumerable.Range(1, bound).Select(index => (long)index),
            writer.Written.Select(chunk => chunk.ChunkIndex));
    }

    /// <summary>
    /// The element past the bound is refused, and the refusal names the bound.
    /// </summary>
    /// <remarks>
    /// <b>IT RAISES RATHER THAN RETURNING, AND THAT IS THE DESIGN.</b> Returning would leave the producer
    /// writing into a sink that silently discarded, which is truncation by another name. Raising stops the
    /// producer at the crossing, so nothing beyond the bound is ever allocated. The bound travels on the
    /// exception so the answer can state the value the operator configured instead of a generic refusal.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task TheElementPastTheBoundIsRefusedAndTheRefusalNamesTheBound(int bound)
    {
        RestProjectionEndpoints.CollectingStreamWriter<RetrieveChunk> writer = new(bound);

        for (int index = 0; index < bound; index++)
        {
            await writer.WriteAsync(new RetrieveChunk { ChunkIndex = index + 1 });
        }

        RestProjectionEndpoints.StreamedResponseTooLargeException refusal =
            await Assert.ThrowsAsync<RestProjectionEndpoints.StreamedResponseTooLargeException>(
                () => writer.WriteAsync(new RetrieveChunk { ChunkIndex = bound + 1 }));

        Assert.Equal(bound, refusal.Limit);

        // NOTHING WAS TRUNCATED AND NOTHING WAS APPENDED: the collector holds exactly what it accepted, so
        // a caller can never be handed a short array that reads as complete.
        Assert.Equal(bound, writer.Written.Count);
        Assert.DoesNotContain(writer.Written, chunk => chunk.ChunkIndex == bound + 1);
    }

    /// <summary>
    /// A refused collector stays refused rather than accepting again.
    /// </summary>
    /// <remarks>
    /// A producer that swallowed the first refusal and kept writing must not be able to slip the next
    /// element in. The test matters because the bound is a count test rather than a latch, and a count test
    /// would keep refusing only if the accepted set is never rolled back - which this asserts.
    /// </remarks>
    [Fact]
    public async Task ARefusedCollectorKeepsRefusing()
    {
        RestProjectionEndpoints.CollectingStreamWriter<RetrieveChunk> writer = new(1);

        await writer.WriteAsync(new RetrieveChunk { ChunkIndex = 1 });

        for (int attempt = 0; attempt < 3; attempt++)
        {
            _ = await Assert.ThrowsAsync<RestProjectionEndpoints.StreamedResponseTooLargeException>(
                () => writer.WriteAsync(new RetrieveChunk { ChunkIndex = 99 }));
        }

        RetrieveChunk only = Assert.Single(writer.Written);

        Assert.Equal(1L, only.ChunkIndex);
    }

    /// <summary>
    /// An empty stream is collected as an empty sequence rather than refused.
    /// </summary>
    /// <remarks>
    /// A retrieval that legitimately produced nothing is an ordinary answer, and the bound has nothing to
    /// say about it. Asserted because a bound implemented as a pre-decrement would refuse it.
    /// </remarks>
    [Fact]
    public void AnEmptyStreamIsCollectedAsAnEmptySequence() =>
        Assert.Empty(new RestProjectionEndpoints.CollectingStreamWriter<RetrieveChunk>(10).Written);
}
