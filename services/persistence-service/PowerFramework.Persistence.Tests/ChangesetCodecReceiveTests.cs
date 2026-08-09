// ==============================================================================================
//  ChangesetCodecReceiveTests.cs - THE RECEIVE PATH, THE CHILD PATH AND THE OPAQUE PAYLOAD FORMAT
//  --------------------------------------------------------------------------------------------
//  ORACLE   ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L93-L155, L180-L258
//           ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L112-L142 (child send)
//
//  The two behaviours here that are easiest to get backwards, and are therefore asserted explicitly:
//    * DEFECT 2 POSITION (d) - the receiver resets on the FIRST chunk only [:L192], while the CHILD
//      receiver resets UNCONDITIONALLY [:L103]. Two different rules, two different reasons.
//    * THE STRICT COERCION - a partial apply is a FAILURE on the changeset path [:L250] and a SUCCESS
//      on the full-state path [:L248]. Getting the direction backwards reports a definition mismatch
//      as a clean transfer, and no row-count assertion would notice.
// ==============================================================================================

using System.Globalization;

namespace PowerFramework.Persistence.Tests;

public sealed class ChangesetCodecReceiveTests
{
    private static ChangesetCodec CreateCodec(IChangesetPayloadCodec? payloadCodec = null)
    {
        return new ChangesetCodec(TimeProvider.System, payloadCodec);
    }

    /// <summary>Encodes a carrier with the real format so a receive test has a genuine payload.</summary>
    private static ReadOnlyMemory<byte> Encode(DataWindowBufferStore source)
    {
        ChangesetPayloadCodec payloadCodec = new();

        // Every row must be stamped for the extraction to see it, exactly as the send path stamps them
        // [n_cst_thread_task_sqlquery.sru:L196-L198].
        for (long row = 1L; row <= source.RowCount(); row++)
        {
            _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);
        }

        for (long row = 1L; row <= source.FilteredCount(); row++)
        {
            _ = source.SetItemStatus(row, 0, DwBuffer.Filter, ItemStatus.DataModified);
        }

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            payloadCodec.TryEncode(source, out ReadOnlyMemory<byte> payload));

        return payload;
    }

    [Fact]
    public void FirstChunkClearsTheTargetAndLaterChunksAccumulateOntoIt()
    {
        // DEFECT 2 POSITION (d). `if current = 1 then ... Reset()` [:L192-L196, :L218-L220, :L235-L237].
        // The reset is LEGAL here because it PRECEDES the apply; chunks after the first must accumulate,
        // which is why it is gated on the first chunk alone.
        ChangesetCodec codec = CreateCodec();
        ReadOnlyMemory<byte> payload = Encode(FixtureCarrier.Create(primaryRows: 2L));

        // The target starts with rows that a first chunk must clear away.
        DataWindowBufferStore target = FixtureCarrier.Create(primaryRows: 4L);

        ChangesetApplyOutcome first = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 2L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, first.Result);
        Assert.True(first.TargetCleared);
        Assert.False(first.UpdateFlagsReset);
        Assert.True(first.Notified);
        Assert.Equal(2L, target.RowCount());

        ChangesetApplyOutcome second = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 2L, 2L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, second.Result);
        Assert.False(second.TargetCleared);
        Assert.True(second.UpdateFlagsReset);
        Assert.Equal(4L, target.RowCount());
    }

    [Fact]
    public void LastChunkClearsTheUpdateFlagsSoDeliveredRowsReadAsUnmodified()
    {
        // `if count = current then dw.ResetUpdate()` [:L198-L199, :L222-L223, :L239-L240]. This is what
        // makes freshly delivered rows read as unmodified with their originals equal to their currents,
        // and it is NOT presentational - it appears in all three receiver arms, unlike GroupCalc and
        // SetRedraw, which appear only in the DataWindow arm and are the documented non-port.
        ChangesetCodec codec = CreateCodec();
        ReadOnlyMemory<byte> payload = Encode(FixtureCarrier.Create(primaryRows: 2L));
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 1L, 1L),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.UpdateFlagsReset);

        for (long row = 1L; row <= target.RowCount(); row++)
        {
            Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(row, 0, DwBuffer.Primary));
        }
    }

    [Fact]
    public void EmptyPayloadClearsTheTargetAndAnswersSuccess()
    {
        // `else rtCode = 1 : ds.Reset()` [:L207-L210, :L226-L229]. AN EMPTY PAYLOAD MEANS CLEAR, and it
        // is exactly the shape the send side's empty-result arm produces at :L230.
        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore target = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(ReadOnlyMemory<byte>.Empty, 1L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.Result);
        Assert.True(outcome.TargetCleared);
        Assert.True(outcome.Notified);

        // ResetUpdate is NOT reached on this arm: the legacy's empty branch does not call it.
        Assert.False(outcome.UpdateFlagsReset);

        Assert.Equal(0L, target.RowCount());
        Assert.Equal(0L, target.FilteredCount());
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(100L)]
    public void PartialApplyIsCoercedToFailureOnTheChangesetPath(long partialResult)
    {
        // `if rtCode > 1 then rtCode = -1` on the CHANGESET arm [:L250], against
        // `if rtCode > 1 then rtCode = 1` on the FULL-STATE arm [:L248]. PowerBuilder answers a value
        // above one when the payload and the target disagree about their definitions, and this path
        // treats that as a FAILURE. No notification is published, because the notify at :L253 is gated
        // on exactly one.
        ScriptedPayloadCodec payloadCodec = new() { ApplyResult = partialResult };
        ChangesetCodec codec = CreateCodec(payloadCodec);
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(new byte[] { 1, 2, 3 }, 1L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.Result);
        Assert.True(outcome.PartialApplyCoerced);
        Assert.False(outcome.Notified);

        // The update flags are STILL cleared on the last chunk: the legacy does not consult the apply
        // result before calling ResetUpdate [:L198-L199].
        Assert.True(outcome.UpdateFlagsReset);
    }

    [Fact]
    public void AFailedApplyStillClearsTheUpdateFlagsOnTheLastChunk()
    {
        ScriptedPayloadCodec payloadCodec = new()
        {
            ApplyResult = DataWindowBufferStore.DataStoreFailure,
        };

        ChangesetCodec codec = CreateCodec(payloadCodec);
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(new byte[] { 9 }, 1L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.Result);
        Assert.False(outcome.PartialApplyCoerced);
        Assert.True(outcome.UpdateFlagsReset);
        Assert.False(outcome.Notified);
    }

    [Fact]
    public void ACancelledApplyAnswersTheContinueValueAndTouchesNothing()
    {
        // `if of_IsCancelled() then return 0` [:L182]. ZERO - the continue value - and not one and not
        // minus one, which also means nothing is published because the notify is gated on exactly one.
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore target = FixtureCarrier.Create(primaryRows: 3L);

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(ReadOnlyMemory<byte>.Empty, 1L, 1L),
            cancellation.Token);

        Assert.Equal(DataWindowBufferStore.EventContinue, outcome.Result);
        Assert.False(outcome.TargetCleared);
        Assert.False(outcome.UpdateFlagsReset);
        Assert.False(outcome.PartialApplyCoerced);
        Assert.False(outcome.Notified);

        // Not cleared, because the guard returns before the reset.
        Assert.Equal(3L, target.RowCount());
    }

    [Fact]
    public void ChildApplyResetsUnconditionallyBeforeApplyingAndReportsBothReapplicationDecisions()
    {
        // `dwc.Reset()` [:L103, :L116, :L128] is UNCONDITIONAL and runs before the payload is inspected,
        // unlike the main receiver which resets only on the first chunk - a child result is transferred
        // WHOLE, so there is no sequence for a first-chunk condition to discriminate.
        //
        // `if Len(dwc.Describe("DataWindow.Table.Filter")) > 1 then dwc.Filter()` [:L145-L147] and the
        // sort equivalent [:L148-L150] are reproduced as DECISIONS: executing them needs a DataWindow
        // expression evaluator, which the refactor plan assigns to DataServices, so the flag is surfaced
        // and nothing is silently dropped.
        ChangesetCodec codec = CreateCodec();
        ReadOnlyMemory<byte> payload = Encode(FixtureCarrier.Create(primaryRows: 2L));
        DataWindowBufferStore child = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetChildApplyOutcome outcome = codec.ApplyChildPayload(
            child,
            new ChangesetChildPayload("age", payload),
            FixtureCarrier.SortedDefinition with { FilterExpression = "age > 30" },
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.Result);
        Assert.True(outcome.TargetCleared);
        Assert.True(outcome.UpdateFlagsReset);
        Assert.True(outcome.RequiresFilterReapplication);
        Assert.True(outcome.RequiresSortReapplication);
        Assert.True(outcome.Notified);

        // The five pre-existing rows were cleared and the two decoded rows took their place.
        Assert.Equal(2L, child.RowCount());
    }

    [Fact]
    public void ChildApplyWithNoConditionsReportsNeitherReapplication()
    {
        // The threshold is length GREATER THAN ONE, not greater than zero, because a single question mark
        // is PowerBuilder's not-applicable marker and has length 1.
        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore child = new() { Processing = new DataWindowProcessing(1L) };

        ChangesetChildApplyOutcome outcome = codec.ApplyChildPayload(
            child,
            new ChangesetChildPayload("age", ReadOnlyMemory<byte>.Empty),
            new ChangesetSourceDefinition { SortExpression = "?", FilterExpression = string.Empty },
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.Result);
        Assert.True(outcome.TargetCleared);
        Assert.False(outcome.RequiresFilterReapplication);
        Assert.False(outcome.RequiresSortReapplication);
    }

    [Fact]
    public void ChildApplyCoercesAPartialApplyToFailureAndSkipsEverythingElse()
    {
        // `if rtCode > 1 then rtCode = -1` [:L141], then `if rtCode = 1 then` guards ResetUpdate, both
        // re-application decisions and the notification [:L143-L152]. A failure therefore does NOT clear
        // the update flags here - which is the opposite of the main receiver, where the last chunk clears
        // them regardless.
        ScriptedPayloadCodec payloadCodec = new() { ApplyResult = 3L };
        ChangesetCodec codec = CreateCodec(payloadCodec);
        DataWindowBufferStore child = new() { Processing = new DataWindowProcessing(1L) };

        ChangesetChildApplyOutcome outcome = codec.ApplyChildPayload(
            child,
            new ChangesetChildPayload("age", new byte[] { 7 }),
            FixtureCarrier.SortedDefinition,
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, outcome.Result);
        Assert.True(outcome.PartialApplyCoerced);
        Assert.True(outcome.TargetCleared);
        Assert.False(outcome.UpdateFlagsReset);
        Assert.False(outcome.RequiresFilterReapplication);
        Assert.False(outcome.RequiresSortReapplication);
        Assert.False(outcome.Notified);
    }

    [Fact]
    public void ACancelledChildApplyAnswersTheContinueValueAndDoesNotEvenReset()
    {
        // `if of_IsCancelled() then return 0` [:L96] precedes the unconditional reset at :L103.
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ChangesetCodec codec = CreateCodec();
        DataWindowBufferStore child = FixtureCarrier.Create(primaryRows: 2L);

        ChangesetChildApplyOutcome outcome = codec.ApplyChildPayload(
            child,
            new ChangesetChildPayload("age", ReadOnlyMemory<byte>.Empty),
            FixtureCarrier.SortedDefinition,
            cancellation.Token);

        Assert.Equal(DataWindowBufferStore.EventContinue, outcome.Result);
        Assert.False(outcome.TargetCleared);
        Assert.Equal(2L, child.RowCount());
    }

    [Fact]
    public async Task ChildTransferStampsBothBuffersResetsTheChildAndHandsTheColumnNameOver()
    {
        // `for nRow = 1 to nRowCnt : SetItemStatus(nRow,0,Primary!,DataModified!)` [:L121-L124], the same
        // for Filter! [:L125-L128], then GetChanges [:L129], then DEFECT 2 POSITION (c) - `dwcSrc.Reset()`
        // [:L133], which is legal because the capture has already happened - then the handover [:L134].
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        DataWindowBufferStore first = FixtureCarrier.Create(primaryRows: 2L, filteredRows: 1L);
        DataWindowBufferStore second = FixtureCarrier.Create(primaryRows: 1L);

        ChangesetChildTransferOutcome outcome = await codec.TransferChildrenAsync(
            [new ChangesetChildSource("age", first), new ChangesetChildSource("address", second)],
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(2, outcome.ChildrenSent);
        Assert.True(outcome.PayloadCleared);
        Assert.Null(outcome.ErrorText);

        Assert.Equal(["age", "address"], sink.Children.Select(child => child.ColumnName));
        Assert.All(sink.Children, child => Assert.NotEmpty(child.Payload));

        // Both children were reset AFTER their capture.
        Assert.Equal(0L, first.RowCount());
        Assert.Equal(0L, first.FilteredCount());
        Assert.Equal(0L, second.RowCount());
    }

    [Fact]
    public async Task ChildExtractionFailureReportsTheBracketedVerbatimText()
    {
        // `Event OnError(RetCode.E_INTERNAL_ERROR,"[" + sColName + "] GetChanges Failed")` [:L130]. One
        // opening bracket, the column name, one closing bracket, one space, then the text.
        ScriptedPayloadCodec payloadCodec = new()
        {
            EncodeResult = DataWindowBufferStore.DataStoreFailure,
        };

        ChangesetCodec codec = CreateCodec(payloadCodec);
        RecordingTransferSink sink = new();

        ChangesetChildTransferOutcome outcome = await codec.TransferChildrenAsync(
            [new ChangesetChildSource("salary", FixtureCarrier.Create(primaryRows: 1L))],
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal(0, outcome.ChildrenSent);
        Assert.Equal("[salary] GetChanges Failed", outcome.ErrorText);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("[salary] GetChanges Failed", errorText);
        Assert.Empty(sink.Children);
    }

    [Fact]
    public async Task ChildHandoverFailureReportsTheBracketedVerbatimText()
    {
        // `Event OnError(RetCode.E_INTERNAL_ERROR,"[" + sColName + "] TransData Failed")` [:L135].
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new() { ChildResult = -1L };

        ChangesetChildTransferOutcome outcome = await codec.TransferChildrenAsync(
            [new ChangesetChildSource("birth", FixtureCarrier.Create(primaryRows: 1L))],
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal("[birth] TransData Failed", outcome.ErrorText);
        Assert.False(outcome.PayloadCleared);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("[birth] TransData Failed", errorText);
    }

    [Fact]
    public async Task ChildTransferStopsAtACancellationInsideTheLoop()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L139] - INSIDE the loop, so the walk stops
        // rather than the cancellation merely being noticed after every child has been sent.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        CancellingChildSink sink = new(cancellation);

        ChangesetChildTransferOutcome outcome = await codec.TransferChildrenAsync(
            [
                new ChangesetChildSource("age", FixtureCarrier.Create(primaryRows: 1L)),
                new ChangesetChildSource("address", FixtureCarrier.Create(primaryRows: 1L)),
            ],
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(1, outcome.ChildrenSent);
        Assert.True(outcome.PayloadCleared);
        Assert.Equal(1, sink.HandoverCount);
    }

    [Fact]
    public async Task AnEmptyChildListStillObservesTheCancellationCheckThatFollowsTheLoop()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L142] is a SECOND check, not a duplicate:
        // it is the only one that fires when there is no child at all.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        ChangesetChildTransferOutcome cancelled = await codec.TransferChildrenAsync(
            [],
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, cancelled.ReturnCode);
        Assert.Equal(0, cancelled.ChildrenSent);

        ChangesetChildTransferOutcome clean = await codec.TransferChildrenAsync(
            [],
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, clean.ReturnCode);
        Assert.Empty(sink.Children);
    }

    /// <summary>
    /// A sink that cancels as it accepts the first child, so the in-loop check at
    /// <c>n_cst_thread_task_sqlquery.sru:L139</c> is reachable.
    /// </summary>
    private sealed class CancellingChildSink(CancellationTokenSource cancellation)
        : IChangesetTransferSink
    {
        internal int HandoverCount { get; private set; }

        public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
        {
            Assert.Fail("The child transfer never raises the create-data handover.");

            return ValueTask.FromResult(0L);
        }

        public ValueTask<long> SendChunkAsync(
            ChangesetChunk chunk,
            CancellationToken cancellationToken)
        {
            Assert.Fail("The child transfer never hands over a main-result chunk.");

            return ValueTask.FromResult(0L);
        }

        public ValueTask<long> SendChildChunkAsync(
            ChangesetChildPayload payload,
            CancellationToken cancellationToken)
        {
            HandoverCount++;
            cancellation.Cancel();

            return ValueTask.FromResult(0L);
        }

        public void ReportError(long returnCode, string errorText)
        {
            Assert.Fail("A cancellation is not an error and must not be reported as one.");
        }
    }
}

/// <summary>
/// The opaque binary payload format itself: which rows it carries, that it round-trips both value sets
/// exactly, and that every malformed input answers a CODE rather than throwing.
/// </summary>
public sealed class ChangesetPayloadCodecTests
{
    /// <summary>
    /// The PowerBuilder scalar family and its declared .NET mapping. Each case asserts that the RUNTIME
    /// TYPE survives as well as the value: the refactor plan maps PowerBuilder's <c>any</c> onto
    /// <c>object?</c>, so a widened integer would compare unequal even though the number matched.
    /// </summary>
    public static TheoryData<object> ValueRoundTripCases =>
        new()
        {
            "a string",
            string.Empty,
            "汉字 and an emoji \U0001F600",
            true,
            false,
            (byte)7,
            (short)-9,
            42,
            long.MinValue,
            (uint)4000000000,
            ulong.MaxValue,
            1.5f,
            -2.25d,
            1500.00m,
            0.000001m,
            new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local),
            new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
            new DateOnly(1990, 6, 15),
            new TimeOnly(13, 45, 30),
            TimeSpan.FromMinutes(90),
            new byte[] { 0, 1, 250, 255 },
            Array.Empty<byte>(),
        };

    [Theory]
    [MemberData(nameof(ValueRoundTripCases))]
    public void EveryMappedValueTypeRoundTripsExactly(object value)
    {
        AssertColumnValueRoundTrips(value);
    }

    [Fact]
    public void ANullColumnValueRoundTripsAsANullRatherThanAsAnAbsence()
    {
        // PowerBuilder has null for value types and the framework's tri-state predicates depend on it, so
        // null is a FIRST-CLASS VALUE here. Collapsing it to zero on the wire would convert "neither
        // succeeded nor failed" into "succeeded" for anything reading it back.
        AssertColumnValueRoundTrips(null);
    }

    private static void AssertColumnValueRoundTrips(object? value)
    {
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, value);

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryEncode(source, out ReadOnlyMemory<byte> payload));

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, payload));
        Assert.Equal(1L, target.RowCount());
        Assert.Equal(value, target.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    [Fact]
    public void ADecimalKeepsItsDeclaredScale()
    {
        // The fixture declares `salary decimal(2)` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L12], and
        // the scale is OBSERVABLE: normalising 1500.00 to 1500 would silently change what the legacy
        // declared. The four constituent words carry it losslessly.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 5, DwBuffer.Primary, 1500.00m);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        _ = codec.TryApply(target, payload);

        object? restored = target.GetItemValue(1L, 5, DwBuffer.Primary);

        Assert.Equal(1500.00m, restored);
        Assert.Equal("1500.00", Assert.IsType<decimal>(restored).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BothTheCurrentAndTheOriginalValueSurviveTheRoundTrip()
    {
        // THE STATE A NAIVE ROWSET WOULD LOSE. `updatewhere=1` [dw_sqlite.srd:L14] with
        // `updatewhereclause=yes` on all six columns [:L8-L13] puts the ORIGINAL value of every
        // updateable column into the generated where clause, so a payload carrying only current values
        // could not express optimistic concurrency at all.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        _ = source.SetItemValue(row, 3, DwBuffer.Primary, 30);
        source.RowAt(row, DwBuffer.Primary).Baseline();

        // Now the edit: the original becomes 30 and the current becomes 31.
        _ = source.SetItemValue(row, 3, DwBuffer.Primary, 31);
        _ = source.SetItemStatus(row, 3, DwBuffer.Primary, ItemStatus.DataModified);
        _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, payload));
        Assert.Equal(31, target.GetItemValue(1L, 3, DwBuffer.Primary));
        Assert.Equal(30, target.GetItemOriginalValue(1L, 3, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));
    }

    [Theory]
    [InlineData(0, false)]  // NotModified - a retrieved row nobody stamped
    [InlineData(1, true)]   // DataModified - what the send path stamps
    [InlineData(2, true)]   // New - inserted, not yet edited; carried so the payload is not lossy
    [InlineData(3, true)]   // NewModified
    public void MembershipCoversTheModifiedPairPlusNewForTheLiveBuffers(int status, bool expected)
    {
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };

        _ = source.AppendRow(DwBuffer.Primary, (ItemStatus)status);
        _ = source.AppendRow(DwBuffer.Filter, (ItemStatus)status);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        _ = codec.TryApply(target, payload);

        Assert.Equal(expected ? 1L : 0L, target.RowCount());
        Assert.Equal(expected ? 1L : 0L, target.FilteredCount());
    }

    [Fact]
    public void EveryDeleteBufferRowIsCarriedRegardlessOfItsStatus()
    {
        // A row's PRESENCE in Delete! is the change, so the modified predicate does not apply there. This
        // buffer is empty on the retrieve path the codec serves, which is why the branch asymmetry
        // documented on IsPayloadRow is unreachable rather than merely tolerated.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };

        _ = source.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        _ = source.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, payload));
        Assert.Equal(2L, target.DeletedCount());
    }

    [Fact]
    public void AnUnmappedRuntimeTypeAnswersTheFailureCodeAndNeverThrows()
    {
        // This is one of the two ways the legacy's `if ... GetChanges(ref blbData) < 0 then` arm
        // [:L129, :L171, :L205] becomes reachable, and the code must be a CODE: the legacy reacts to one.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, Guid.NewGuid());

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryEncode(source, out ReadOnlyMemory<byte> payload));

        Assert.True(payload.IsEmpty);
    }

    [Fact]
    public void AMalformedPayloadAnswersTheFailureCodeAndNeverThrows()
    {
        // Every receive-side call site tests a code [:L197, :L221, :L238], so a truncated, reordered or
        // foreign payload must answer -1 exactly as SetChanges does.
        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 2L);

        for (long row = 1L; row <= source.RowCount(); row++)
        {
            _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);
        }

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        byte[] whole = payload.ToArray();

        Assert.All(
            new[]
            {
                Array.Empty<byte>(),                        // nothing at all
                new byte[] { 1, 2, 3 },                     // too short for the header
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 0 },// a foreign magic
                whole[..(whole.Length / 2)],                // truncated mid-row
                Corrupt(whole, 4, 99),                      // an unknown format version
                Corrupt(whole, 5, 42),                      // a non-zero reserved flag
            },
            malformed => Assert.Equal(
                DataWindowBufferStore.DataStoreFailure,
                codec.TryApply(
                    new DataWindowBufferStore { Processing = new DataWindowProcessing(1L) },
                    malformed)));
    }

    [Fact]
    public void NoCorruptionOfAnyByteEverEscapesAsAnException()
    {
        // THE CONTRACT THIS ASSERTS IS THE WHOLE REASON THE DECODER IS WRITTEN THE WAY IT IS. Every
        // receive-side call site tests a CODE [n_cst_threading_task_sqlquery.sru:L197, :L221, :L238], so
        // a payload that is truncated, reordered, foreign or bit-rotted must answer -1 the way SetChanges
        // does. An escaping exception would be a failure mode the oracle does not have (C-B), and it
        // would surface as a 500 rather than as the conflict-or-failure result the contract publishes.
        //
        // Driven as an exhaustive single-byte mutation plus an exhaustive truncation over a real payload,
        // because the interesting arms - an undefined buffer ordinal, a negative count, an undefined item
        // status, a zero column number, an unknown value tag, an undefined date kind, a blob length past
        // the end - are all reachable by corrupting one byte, and enumerating them by offset would be
        // brittle against any future field.
        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        // A row spanning several tag kinds, so the mutation reaches the value decoders too.
        _ = source.SetItemValue(row, 1, DwBuffer.Primary, 42);
        _ = source.SetItemValue(row, 2, DwBuffer.Primary, "abc");
        _ = source.SetItemValue(row, 3, DwBuffer.Primary, new byte[] { 1, 2, 3 });
        _ = source.SetItemValue(
            row,
            4,
            DwBuffer.Primary,
            new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        _ = codec.TryEncode(source, out ReadOnlyMemory<byte> payload);

        byte[] whole = payload.ToArray();

        Assert.NotEmpty(whole);

        foreach (byte replacement in new byte[] { 0x00, 0x01, 0x7F, 0xFF })
        {
            for (int offset = 0; offset < whole.Length; offset++)
            {
                byte[] mutated = [.. whole];

                mutated[offset] = replacement;

                AssertAnswersACodeAndDoesNotThrow(codec, mutated);
            }
        }

        for (int length = 0; length < whole.Length; length++)
        {
            AssertAnswersACodeAndDoesNotThrow(codec, whole[..length]);
        }
    }

    [Fact]
    public void AHandBuiltPayloadWithTheWrongSegmentCountAnswersFailure()
    {
        // The format writes one segment per buffer and exactly three buffers exist, so a payload claiming
        // any other number cannot be this format's. Built by hand rather than mutated, because the count
        // is a whole field and a plausible-but-wrong value is the case worth stating.
        ChangesetPayloadCodec codec = new();

        foreach (int segmentCount in new[] { 0, 1, 2, 4, -1 })
        {
            using MemoryStream buffer = new();
            using BinaryWriter writer = new(buffer);

            writer.Write(0x43574650u);  // the format magic
            writer.Write((byte)1);      // the format version
            writer.Write((byte)0);      // the reserved flags
            writer.Write(1L);           // the processing kind
            writer.Write(segmentCount);
            writer.Flush();

            Assert.Equal(
                DataWindowBufferStore.DataStoreFailure,
                codec.TryApply(
                    new DataWindowBufferStore { Processing = new DataWindowProcessing(1L) },
                    buffer.ToArray()));
        }
    }

    private static void AssertAnswersACodeAndDoesNotThrow(
        ChangesetPayloadCodec codec,
        ReadOnlyMemory<byte> candidate)
    {
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        long result = codec.TryApply(target, candidate);

        Assert.True(
            result == DataWindowBufferStore.DataStoreSuccess
                || result == DataWindowBufferStore.DataStoreFailure,
            $"A corrupted payload answered {result}, which is neither success nor failure.");
    }

    [Fact]
    public void EncodingTheSameCarrierTwiceProducesByteIdenticalPayloads()
    {
        // The Golden-Master technique's one hard prerequisite is repeatability. Column order is ascending
        // and the buffer order is fixed, so equal input must yield equal bytes - otherwise a stored
        // recording could never be compared against a fresh capture.
        ChangesetPayloadCodec codec = new();

        DataWindowBufferStore first = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);
        DataWindowBufferStore second = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        foreach (DataWindowBufferStore carrier in new[] { first, second })
        {
            for (long row = 1L; row <= carrier.RowCount(); row++)
            {
                _ = carrier.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);
            }

            for (long row = 1L; row <= carrier.FilteredCount(); row++)
            {
                _ = carrier.SetItemStatus(row, 0, DwBuffer.Filter, ItemStatus.DataModified);
            }
        }

        _ = codec.TryEncode(first, out ReadOnlyMemory<byte> firstPayload);
        _ = codec.TryEncode(second, out ReadOnlyMemory<byte> secondPayload);

        Assert.Equal(firstPayload.ToArray(), secondPayload.ToArray());
    }

    private static byte[] Corrupt(byte[] payload, int offset, byte value)
    {
        byte[] copy = [.. payload];

        copy[offset] = value;

        return copy;
    }
}
