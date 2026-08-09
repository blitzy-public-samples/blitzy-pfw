// ==============================================================================================
//  ChangesetCodecTransferTests.cs - THE SEND PATH, INCLUDING BOTH NAMED DEFECTS AND THE FIFTH QUIRK
//  --------------------------------------------------------------------------------------------
//  ORACLE   ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L102-L231
//  FIXTURE  ws_objects/pfw.tests.pbl.src/dw_sqlite.srd - processing=1 AND sort="age A salary A ", so
//           the sorted multi-chunk workaround of DEFECT 1 is a MAIN PATH and is exercised as one.
//
//  Every case runs with InterChunkYield = TimeSpan.Zero so a many-chunk sequence costs nothing, while
//  the production default stays the legacy 0.02 seconds [:L189]. No database, no clock, no network.
// ==============================================================================================

namespace PowerFramework.Persistence.Tests;

public sealed class ChangesetCodecTransferTests
{
    private static ChangesetCodec CreateCodec(IChangesetPayloadCodec? payloadCodec = null)
    {
        return new ChangesetCodec(TimeProvider.System, payloadCodec);
    }

    private static ChangesetTransferRequest Request(
        DataWindowBufferStore source,
        ChangesetSourceDefinition definition,
        long chunkSize,
        bool receiverNeedsCreatedObject = false)
    {
        return new ChangesetTransferRequest
        {
            Source = source,
            Definition = definition,
            ChunkSize = chunkSize,
            ReceiverNeedsCreatedObject = receiverNeedsCreatedObject,
            InterChunkYield = TimeSpan.Zero,
        };
    }

    /// <summary>
    /// Applies a recorded sequence back onto a fresh carrier, which is what makes a round trip
    /// assertable end to end.
    /// </summary>
    private static DataWindowBufferStore Apply(
        ChangesetCodec codec,
        RecordingTransferSink sink,
        CancellationToken cancellationToken)
    {
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        foreach ((CarrierState? state, long chunkCount, long chunkIndex, bool fullState) in sink.Chunks)
        {
            Assert.False(fullState);

            ChangesetApplyOutcome outcome = codec.ApplyChunk(
                target,
                new ChangesetChunk(state, chunkCount, chunkIndex),
                cancellationToken);

            Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.Result);
        }

        return target;
    }

    /// <summary>
    /// Compares two carriers field for field: counts, per-row statuses, and both the current and the
    /// ORIGINAL value of every column - the original being exactly the state a naive rowset would lose
    /// and the state the <c>updatewhere=1</c> concurrency check depends on
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>].
    /// </summary>
    private static void AssertCarriersMatch(
        DataWindowBufferStore expected,
        DataWindowBufferStore actual)
    {
        Assert.Equal(expected.RowCount(), actual.RowCount());
        Assert.Equal(expected.FilteredCount(), actual.FilteredCount());
        Assert.Equal(expected.DeletedCount(), actual.DeletedCount());

        AssertBufferMatches(expected, actual, DwBuffer.Primary, expected.RowCount());
        AssertBufferMatches(expected, actual, DwBuffer.Filter, expected.FilteredCount());
    }

    private static void AssertBufferMatches(
        DataWindowBufferStore expected,
        DataWindowBufferStore actual,
        DwBuffer dwBuffer,
        long rowCount)
    {
        // R9: one-based bounds, inclusive at both ends, matching every legacy loop.
        for (long row = 1L; row <= rowCount; row++)
        {
            Assert.Equal(
                expected.GetItemStatus(row, 0, dwBuffer),
                actual.GetItemStatus(row, 0, dwBuffer));

            for (int column = 1; column <= FixtureCarrier.ColumnCount; column++)
            {
                Assert.Equal(
                    expected.GetItemValue(row, column, dwBuffer),
                    actual.GetItemValue(row, column, dwBuffer));

                Assert.Equal(
                    expected.GetItemOriginalValue(row, column, dwBuffer),
                    actual.GetItemOriginalValue(row, column, dwBuffer));

                Assert.Equal(
                    expected.RowAt(row, dwBuffer).GetColumnStatus(column),
                    actual.RowAt(row, dwBuffer).GetColumnStatus(column));
            }
        }
    }

    [Fact]
    public async Task EmptyCarrierEmitsExactlyOneEmptyChunkWithOneBasedCounters()
    {
        // `else tasking.Event OnDataChunk(ref blbData,1,1,false)` [:L229-L231]. Counters 1 and 1 even
        // though the arithmetic answered zero, and an EMPTY payload, which is what tells the receiving
        // side to CLEAR rather than that there is nothing to do.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(0L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 10L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(1L, outcome.ChunkCount);
        Assert.Equal(1L, outcome.ChunksSent);
        Assert.False(outcome.UsedTemporaryCarrier);
        Assert.Null(outcome.TemporaryCarrierShape);
        Assert.True(outcome.PayloadCleared);
        Assert.Null(outcome.ErrorText);

        (CarrierState? state, long chunkCount, long chunkIndex, bool fullState) =
            Assert.Single(sink.Chunks);

        // THE EMPTY-CARRIER ARM SENDS NO STATE AT ALL, which is this contract's spelling of the legacy's
        // zero-length blob [:L230] - and it is what tells the receiving side to CLEAR rather than merge.
        // A state carrying three EMPTY segments would be a different message with a different meaning:
        // "here is an image, and it happens to hold nothing".
        Assert.Null(state);
        Assert.Equal(1L, chunkCount);
        Assert.Equal(1L, chunkIndex);
        Assert.False(fullState);
    }

    [Fact]
    public async Task EmptyCarrierHandoverResultIsNotTestedOnThatArm()
    {
        // The empty-result arm at :L230 is the ONE handover the legacy does not guard with
        // `if ... < 0 then`, unlike :L183 and :L219. A negative answer therefore does NOT become an
        // error, and reproducing that omission is the point of this case (C-B).
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new() { ChunkResult = -1L };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(FixtureCarrier.Create(0L), FixtureCarrier.SortedDefinition, 10L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Empty(sink.Errors);
    }

    [Fact]
    public async Task SingleChunkSortedCarrierTakesTheInPlaceBranch()
    {
        // DEFECT 1's guard needs chunkCount > 1 as well as a sort expression [:L151]. A sorted carrier
        // that fits in one chunk therefore does NOT get the workaround.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 10L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(1L, outcome.ChunkCount);
        Assert.False(outcome.UsedTemporaryCarrier);
        Assert.Null(outcome.TemporaryCarrierShape);
    }

    [Fact]
    public async Task MultiChunkSortedCarrierTakesTheTemporaryCarrierBranchAndNeverResetsTheSource()
    {
        // DEFECT 1 [:L148] plus DEFECT 2 POSITION (a) [:L176], asserted together and discriminated by
        // the DELETE buffer. The temporary branch moves rows only between Primary! and Filter!
        // [:L160, :L166] and calls Reset NOWHERE, so a row sitting in Delete! must survive the whole
        // transfer. If a Reset had crept into the mid-loop clear it would be gone.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L, deletedRows: 1L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(3L, outcome.ChunkCount);
        Assert.Equal(3L, outcome.ChunksSent);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(TemporaryCarrierProvenance.DataObjectName, outcome.TemporaryCarrierShape);

        // The rows were MOVED out, so the source is drained...
        Assert.Equal(0L, source.RowCount());
        Assert.Equal(0L, source.FilteredCount());

        // ...but NOT reset: the Delete! row is still there.
        Assert.Equal(1L, source.DeletedCount());
    }

    [Fact]
    public async Task MultiChunkUnsortedCarrierResetsTheSourceOnlyOnTheFinalChunk()
    {
        // DEFECT 2 POSITION (b) [:L209-L218]. Reset IS used, once, on the final chunk and only after the
        // changeset has already been captured at :L205 - which is exactly why it is safe here and
        // forbidden in position (a). The Delete! row disappearing is the proof that Reset ran, because
        // RowsDiscard never touches that buffer.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L, deletedRows: 1L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.UnsortedDefinition, 2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(3L, outcome.ChunkCount);
        Assert.Equal(3L, outcome.ChunksSent);
        Assert.False(outcome.UsedTemporaryCarrier);

        Assert.Equal(0L, source.RowCount());
        Assert.Equal(0L, source.FilteredCount());
        Assert.Equal(0L, source.DeletedCount());
    }

    [Fact]
    public async Task EveryEmittedChunkCarriesFullStateFalseAndOneBasedCounters()
    {
        // The selector is how the receiving side chooses its decoder [:L189, :L215, :L232], so a single
        // wrong value routes a payload to the wrong codec. R9 item 2: the first chunk is 1, never 0.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(FixtureCarrier.Create(primaryRows: 7L, filteredRows: 3L),
                FixtureCarrier.SortedDefinition,
                3L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(4L, outcome.ChunkCount);
        Assert.Equal(4, sink.Chunks.Count);

        long expectedIndex = 1L;

        foreach ((CarrierState? state, long chunkCount, long chunkIndex, bool fullState) in sink.Chunks)
        {
            Assert.False(fullState);
            Assert.Equal(4L, chunkCount);
            Assert.Equal(expectedIndex, chunkIndex);
            Assert.NotNull(state);
            Assert.NotEmpty(state.Segments);
            expectedIndex++;
        }
    }

    [Fact]
    public async Task FoldRunsBeforeTheChunkArithmeticWhenTheReceiverNeedsACreatedObject()
    {
        // `if tasking._of_NeedCreate() then OnCreateData(...) ... RowsMove(...)` [:L103-L110], and the
        // fold PRECEDES the arithmetic at :L145. The observable consequence is not the chunk count - the
        // sum of both buffers is invariant under the fold - but WHICH BUFFER the rows arrive in.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 10L, receiverNeedsCreatedObject: true),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.FoldedFilterBuffer);
        Assert.Equal("release 12.5;", Assert.Single(sink.CreatedFrom));

        DataWindowBufferStore applied = Apply(codec, sink, TestContext.Current.CancellationToken);

        // All five rows arrive in Primary! and nothing is filtered, which is the whole point of the NOTE
        // at :L106-L108: an object built from the current syntax re-applies filtering itself.
        Assert.Equal(5L, applied.RowCount());
        Assert.Equal(0L, applied.FilteredCount());
    }

    [Fact]
    public async Task WithoutTheFoldTheFilterBufferIsTransferredAsAFilterBuffer()
    {
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L),
                FixtureCarrier.SortedDefinition,
                10L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.False(outcome.FoldedFilterBuffer);
        Assert.Empty(sink.CreatedFrom);

        DataWindowBufferStore applied = Apply(codec, sink, TestContext.Current.CancellationToken);

        Assert.Equal(3L, applied.RowCount());
        Assert.Equal(2L, applied.FilteredCount());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangesetExtractionFailureReportsTheVerbatimTextOnBothBranches(bool sorted)
    {
        // `Event OnError(RetCode.E_INTERNAL_ERROR,"GetChanges Failed")` [:L172] in the temporary branch
        // and [:L206] in the in-place branch, then `return RetCode.E_INTERNAL_ERROR`.
        ScriptedPayloadCodec payloadCodec = new()
        {
            EncodeResult = DataWindowBufferStore.DataStoreFailure,
        };

        ChangesetCodec codec = CreateCodec(payloadCodec);
        RecordingTransferSink sink = new();

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                FixtureCarrier.Create(primaryRows: 5L),
                sorted ? FixtureCarrier.SortedDefinition : FixtureCarrier.UnsortedDefinition,
                2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal(sorted, outcome.UsedTemporaryCarrier);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.Equal("GetChanges Failed", outcome.ErrorText);
        Assert.Empty(sink.Chunks);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("GetChanges Failed", errorText);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandoverFailureReportsTheVerbatimTextAndLeavesThePayloadUncleared(bool sorted)
    {
        // `Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")` [:L184, :L220]. The legacy returns
        // BEFORE the blob clear at :L187 and :L223, so the payload is still held on this path - which is
        // why PayloadCleared reports the actual state rather than asserting success.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new() { ChunkResult = -1L };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                FixtureCarrier.Create(primaryRows: 5L),
                sorted ? FixtureCarrier.SortedDefinition : FixtureCarrier.UnsortedDefinition,
                2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal("TransData Failed", outcome.ErrorText);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.False(outcome.PayloadCleared);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("TransData Failed", errorText);

        // The failing handover still happened, so exactly one chunk reached the sink.
        _ = Assert.Single(sink.Chunks);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PayloadIsClearedAfterEveryHandover(bool sorted)
    {
        // `blbData = Blob("")` [:L187, :L223]. DECISION 6 - the ownership handover is complete, so the
        // sender drops the payload; it bounds peak memory across the sequence.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                FixtureCarrier.Create(primaryRows: 6L),
                sorted ? FixtureCarrier.SortedDefinition : FixtureCarrier.UnsortedDefinition,
                2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(3L, outcome.ChunksSent);
        Assert.True(outcome.PayloadCleared);

        // Each recorded copy is still intact, which proves the clear released the SENDER's reference
        // rather than mutating a payload the receiver already owns.
        Assert.All(sink.Chunks, chunk => Assert.NotNull(chunk.State));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationMidSequenceReturnsCancelledAndStopsTheSequence(bool sorted)
    {
        // `if nChunkIdx < nChunkCnt then if Not of_Wait(0.02) then return RetCode.CANCELLED`
        // [:L188-L190, :L224-L226]. Cancellation is never swallowed into success, and no
        // OperationCanceledException escapes - the legacy answers a CODE and a caller tests one.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new()
        {
            AfterChunk = cancellation.Cancel,
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                FixtureCarrier.Create(primaryRows: 9L),
                sorted ? FixtureCarrier.SortedDefinition : FixtureCarrier.UnsortedDefinition,
                3L),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(3L, outcome.ChunkCount);

        // Exactly one chunk got out before the yield observed the cancellation.
        Assert.Equal(1L, outcome.ChunksSent);
        _ = Assert.Single(sink.Chunks);
        Assert.Empty(sink.Errors);
    }

    [Fact]
    public async Task CancellationBeforeTheChunkSequenceReturnsCancelledWithoutEmittingAnything()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L142] - the guard between the child loop
        // and the chunk sequence.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(FixtureCarrier.Create(primaryRows: 5L), FixtureCarrier.SortedDefinition, 2L),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(0L, outcome.ChunkCount);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.False(outcome.FoldedFilterBuffer);
        Assert.Empty(sink.Chunks);
    }

    [Fact]
    public async Task CancellationAfterTheCreateDataHandoverReturnsCancelledWithoutFolding()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L105] sits BETWEEN the create-data
        // handover at :L104 and the fold at :L109, so a cancellation there leaves both buffers untouched.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);
        CancellingSink sink = new(cancellation);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 2L, receiverNeedsCreatedObject: true),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.False(outcome.FoldedFilterBuffer);

        // The fold never ran, so the two buffers still hold what they held.
        Assert.Equal(3L, source.RowCount());
        Assert.Equal(2L, source.FilteredCount());
    }

    [Fact]
    public async Task StaleFilteredCountDiscardsAnAlreadyEmptiedBufferWithoutThrowing()
    {
        // THE FIFTH QUIRK. `long nRow,nRowCnt,nFilterCnt` is declared ONCE outside the chunk loop
        // [:L75] and nFilterCnt is assigned only inside the guard at :L164, so on a later iteration
        // where that guard is false the discard at :L181 runs with the PREVIOUS iteration's count
        // against an already-emptied Filter! buffer.
        //
        // Under an undisturbed loop the quirk stays LATENT, because each chunk consumes exactly
        // min(chunkSize, remaining) rows and the primary buffer always drains before the filter buffer
        // is touched. It becomes REACHABLE the moment the handover mutates the source between chunks,
        // which it can: the handover at :L183 runs while the source is still live. This case drives
        // exactly that, and the assertion is the one that matters - THE STALE DISCARD MUST ANSWER A
        // FAILURE CODE RATHER THAN THROW, because the legacy discards the answer and an exception here
        // would be a regression the legacy could not have had.
        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 1L, filteredRows: 3L);
        RecordingTransferSink sink = new();

        sink.AfterChunk = () =>
        {
            // One handover only: two fresh primary rows, so the next iteration's row count equals the
            // chunk size and the filter top-up guard goes false with a non-zero count still in hand.
            if (sink.Chunks.Count != 1)
            {
                return;
            }

            for (int added = 0; added < 2; added++)
            {
                _ = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
            }
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, 2L),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(2L, outcome.ChunkCount);
        Assert.Equal(2L, outcome.ChunksSent);
        Assert.Empty(sink.Errors);
    }

    [Fact]
    public async Task CancellationArrivingDuringTheYieldReturnsCancelledWithoutAnExceptionEscaping()
    {
        // `of_Wait(0.02)` [:L189, :L225] answers FALSE when the task is cancelled, and the legacy turns
        // that into `return RetCode.CANCELLED`. The managed yield therefore has to convert an
        // OperationCanceledException into that same answer: a caller written against this contract tests a
        // CODE and would never catch an exception, so letting one escape would make RetCode.CANCELLED
        // unreachable and turn an orderly cancellation into a fault.
        //
        // The cancellation is timed to arrive WHILE the yield is in flight, which is the only way to reach
        // the conversion - a token already cancelled on entry is answered by the guard before the delay.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetTransferRequest request = new()
        {
            Source = FixtureCarrier.Create(primaryRows: 9L),
            Definition = FixtureCarrier.SortedDefinition,
            ChunkSize = 3L,
            InterChunkYield = TimeSpan.FromSeconds(30),
        };

        sink.AfterChunk = () => cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            request,
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(1L, outcome.ChunksSent);
        Assert.Empty(sink.Errors);
    }

    [Theory]
    [InlineData(1L, 3L, 2L)]  // the primary buffer drains first, so chunk 1 discards BOTH buffers
    [InlineData(2L, 4L, 3L)]
    [InlineData(0L, 5L, 2L)]  // filtered only
    public async Task InPlaceBranchDiscardsBothBuffersBetweenChunksAndRoundTrips(
        long primaryRows,
        long filteredRows,
        long chunkSize)
    {
        // DEFECT 2 POSITION (b), the OTHER half: `if nChunkIdx < nChunkCnt then` discard Primary! AND
        // discard Filter! [:L210-L215], with the Reset reserved for the final chunk. These populations
        // reach the filter discard on a NON-final chunk, which the sorted branch never does because it
        // moves rows out instead.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows, filteredRows);
        DataWindowBufferStore expected = FixtureCarrier.Create(primaryRows, filteredRows);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.UnsortedDefinition, chunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.False(outcome.UsedTemporaryCarrier);
        Assert.True(outcome.ChunkCount > 1L);

        DataWindowBufferStore applied = Apply(codec, sink, TestContext.Current.CancellationToken);

        AssertCarriersMatch(expected, applied);
    }

    [Fact]
    public void AnOutOfRangeDiscardAnswersAFailureCodeAndNeverThrows()
    {
        // The direct statement of what the fifth quirk depends on, asserted without having to arrange the
        // whole loop: an unaddressable range is a benign no-op that answers -1.
        DataWindowBufferStore carrier = FixtureCarrier.Create(primaryRows: 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            carrier.RowsDiscard(1L, 1L, DwBuffer.Filter));

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            carrier.RowsDiscard(1L, 5L, DwBuffer.Primary));

        Assert.Equal(2L, carrier.RowCount());
    }

    [Theory]
    [InlineData(3L, 0L, 10L, false)]  // one chunk, in place
    [InlineData(3L, 2L, 10L, false)]  // one chunk, mixed, in place
    [InlineData(5L, 0L, 2L, true)]    // multi-chunk sorted - DEFECT 1's path
    [InlineData(3L, 2L, 2L, true)]    // multi-chunk sorted, mixed - DEFECT 1's path
    [InlineData(1L, 5L, 2L, true)]    // multi-chunk sorted, filter-heavy - DEFECT 1's path
    [InlineData(7L, 3L, 3L, true)]
    public async Task RoundTripReproducesCountsStatusesAndBothValueSetsOnBothBranches(
        long primaryRows,
        long filteredRows,
        long chunkSize,
        bool expectTemporaryCarrier)
    {
        // The round trip that carries the parity weight: a carrier shaped like dw_sqlite.srd, chunked
        // out and applied back, must reproduce the row count, the filtered count, every row status and
        // both the current and the ORIGINAL value of all six columns - including across a multi-chunk
        // SORTED source, which is DEFECT 1's path and the fixture's own shape.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows, filteredRows);
        DataWindowBufferStore expected = FixtureCarrier.Create(primaryRows, filteredRows);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, chunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(expectTemporaryCarrier, outcome.UsedTemporaryCarrier);

        DataWindowBufferStore applied = Apply(codec, sink, TestContext.Current.CancellationToken);

        AssertCarriersMatch(expected, applied);
    }

    /// <summary>
    /// A sink that cancels while answering the create-data handover, so the check at
    /// <c>n_cst_thread_task_sqlquery.sru:L105</c> is reachable.
    /// </summary>
    private sealed class CancellingSink(CancellationTokenSource cancellation) : IChangesetTransferSink
    {
        public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
        {
            cancellation.Cancel();

            return ValueTask.FromResult(0L);
        }

        public ValueTask<long> SendChunkAsync(
            ChangesetChunk chunk,
            CancellationToken cancellationToken)
        {
            Assert.Fail("No chunk may be handed over after a cancellation at :L105.");

            return ValueTask.FromResult(0L);
        }

        public ValueTask<long> SendChildChunkAsync(
            ChangesetChildPayload payload,
            CancellationToken cancellationToken)
        {
            Assert.Fail("No child payload may be handed over by the main transfer.");

            return ValueTask.FromResult(0L);
        }

        public void ReportError(long returnCode, string errorText)
        {
            Assert.Fail("A cancellation is not an error and must not be reported as one.");
        }
    }
}
