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
using Google.Protobuf;

namespace PowerFramework.Persistence.Tests;

public sealed class ChangesetCodecReceiveTests
{
    private static ChangesetCodec CreateCodec(IChangesetPayloadCodec? payloadCodec = null)
    {
        return new ChangesetCodec(TimeProvider.System, payloadCodec);
    }

    /// <summary>Encodes a carrier with the real format so a receive test has a genuine payload.</summary>
    private static CarrierState Encode(DataWindowBufferStore source)
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
            payloadCodec.TryEncode(source, out CarrierState? payload));

        return payload!;
    }

    /// <summary>
    /// A carrier state that is merely PRESENT, for the receive tests whose subject is the receiver's own
    /// control flow rather than the image.
    /// </summary>
    /// <remarks>
    /// Those tests pair this with a <see cref="ScriptedPayloadCodec"/>, so the image is never decoded and
    /// its content is irrelevant - what matters is that it is not <see langword="null"/>, because
    /// <see cref="ChangesetChunk.IsEmptyPayload"/> is what selects the receiver's empty-payload arm
    /// [<c>n_cst_threading_task_sqlquery.sru:L194-L196</c>]. It is nevertheless built CONFORMING - the
    /// canonical three segments in canonical order - so that a future test which does decode it is not
    /// silently exercising a rejection.
    /// </remarks>
    private static CarrierState SomeState()
    {
        CarrierState state = new() { Processing = 1L };

        foreach (DwBuffer dwBuffer in ChangesetPayloadCodec.SerializedBuffers)
        {
            state.Segments.Add(new CarrierBufferSegment { Buffer = dwBuffer });
        }

        return state;
    }

    /// <summary>
    /// A not-a-number double is refused at the decode seam, and the two infinities are not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE DISTINCTION IS THE PROVIDER'S AND WAS MEASURED, NOT ASSUMED.</b> Binding
    /// <c>double.NaN</c> raises <c>InvalidOperationException("Cannot store 'NaN' values.")</c> - a fault
    /// rather than a database error, so it passes straight through the update walk's provider-error arm and
    /// escapes the RPC as an unhandled <c>Internal</c> with no diagnostic - while both infinities bind and
    /// store normally.
    /// </para>
    /// <para>
    /// Refusing it here converts that into each caller's own defined refusal before a single statement is
    /// generated, which is also what keeps a multi-row payload from half-applying. A CONTRACT NARROWING,
    /// taken deliberately: the legacy has no NaN at all - PowerScript has no literal for it - so there is no
    /// legacy behaviour to preserve, and substituting null or zero would write a value the caller never
    /// sent.
    /// </para>
    /// </remarks>
    [Fact]
    public void ANotANumberDoubleIsRefusedAndTheInfinitiesAreNot()
    {
        Assert.False(CarrierValue.TryFromWire(new AnyValue { DoubleValue = double.NaN }, out object? refused));
        Assert.Null(refused);

        Assert.True(
            CarrierValue.TryFromWire(
                new AnyValue { DoubleValue = double.PositiveInfinity },
                out object? positive));
        Assert.Equal(double.PositiveInfinity, Assert.IsType<double>(positive));

        Assert.True(
            CarrierValue.TryFromWire(
                new AnyValue { DoubleValue = double.NegativeInfinity },
                out object? negative));
        Assert.Equal(double.NegativeInfinity, Assert.IsType<double>(negative));

        // AN ORDINARY DOUBLE IS UNTOUCHED, so the guard cannot have narrowed the arm itself.
        Assert.True(CarrierValue.TryFromWire(new AnyValue { DoubleValue = 4321.5 }, out object? ordinary));
        Assert.Equal(4321.5, Assert.IsType<double>(ordinary));
    }

    [Fact]
    public void FirstChunkClearsTheTargetAndLaterChunksAccumulateOntoIt()
    {
        // DEFECT 2 POSITION (d). `if current = 1 then ... Reset()` [:L192-L196, :L218-L220, :L235-L237].
        // The reset is LEGAL here because it PRECEDES the apply; chunks after the first must accumulate,
        // which is why it is gated on the first chunk alone.
        ChangesetCodec codec = CreateCodec();
        CarrierState payload = Encode(FixtureCarrier.Create(primaryRows: 2L));

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
        CarrierState payload = Encode(FixtureCarrier.Create(primaryRows: 2L));
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
            new ChangesetChunk(state: null, 1L, 1L),
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
            new ChangesetChunk(SomeState(), 1L, 1L),
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
            new ChangesetChunk(SomeState(), 1L, 1L),
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
            new ChangesetChunk(state: null, 1L, 1L),
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
        CarrierState payload = Encode(FixtureCarrier.Create(primaryRows: 2L));
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
            new ChangesetChildPayload("age", state: null),
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
            new ChangesetChildPayload("age", SomeState()),
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
            new ChangesetChildPayload("age", state: null),
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
        Assert.All(sink.Children, child => Assert.NotNull(child.State));

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
/// The published carrier-state projection itself: which rows it carries, that both value sets survive the
/// round trip onto their declared carrier types, and that every structurally invalid state answers a CODE
/// rather than throwing - refused in full, before a single row reaches the target.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS SUITE USED TO ASSERT, AND WHY IT NO LONGER DOES. It was written against a private binary
/// payload owned by this assembly, so more than half of it - a format magic, a format version, a reserved
/// flag byte, single-byte mutation sweeps, truncation sweeps, and three hand-built weaponised counts that
/// proved a length was bounded before it reached <c>new byte[length]</c> - tested a FRAMING LAYER that no
/// longer exists. The chunk payload is now <c>persistence.v1.CarrierState</c>, a published message
/// (constraint C-A), and framing, bounds and truncation are protobuf's concern rather than this codec's:
/// a truncated or foreign frame never reaches <see cref="ChangesetPayloadCodec"/> at all, because the
/// generated parser rejects it at the transport edge.
/// </para>
/// <para>
/// WHAT REPLACED THEM IS NOT A REDUCTION. The framing checks are gone because the framing is gone; the
/// SEMANTIC checks they were mixed in with are all still here and several are new, because a well-formed
/// protobuf message can still be a nonsense carrier image: three segments all tagged Primary, a Filter!
/// row filed inside the Primary segment, a zero row ordinal, an undefined item status, column number
/// zero, a duplicated column, an original naming a column the row never carried, or a processing kind
/// that disagrees with the target's. Every one of those is a way to load the wrong rows into the wrong
/// buffer and get a row count nothing disagrees with, and every one of them is asserted below.
/// </para>
/// </remarks>
public sealed class ChangesetPayloadCodecTests
{
    /// <summary>
    /// The PowerBuilder scalar family and the carrier type each one round-trips as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EACH CASE IS AN INPUT AND ITS EXPECTED RESTORED VALUE, NOT A SINGLE VALUE ASSERTED AGAINST ITSELF,
    /// AND THE DIFFERENCE IS THE POINT. <c>common.v1.AnyValue</c> publishes eleven arms, and the numeric
    /// ones are DELIBERATELY WIDE: one signed integer arm, one unsigned, one floating. That is the
    /// contract's own decision - documented on the message as the widening rule - and it means a
    /// <see cref="byte"/>, a <see cref="short"/> and an <see cref="int"/> all travel as the signed arm and
    /// all come back as <see cref="long"/>, a <see cref="uint"/> comes back as <see cref="ulong"/>, and a
    /// <see cref="float"/> comes back as <see cref="double"/>.
    /// </para>
    /// <para>
    /// The private format this suite used to exercise over-provisioned a distinct tag per runtime type, so
    /// its predecessor asserted exact runtime-type identity and passed. Stating the widening as an
    /// input-to-expected matrix is what makes it VISIBLE rather than something a reader discovers when a
    /// consumer casts to <see cref="int"/> and throws. The values themselves are exact either way: nothing
    /// here loses magnitude, sign, precision or scale.
    /// </para>
    /// <para>
    /// The three <see cref="DateTime"/> cases differ only in their <see cref="DateTimeKind"/> and all three
    /// expect <see cref="DateTimeKind.Unspecified"/>, because the contract's datetime form is UNZONED -
    /// PowerBuilder's <c>datetime</c> carries no zone either, so preserving a kind would invent
    /// information the oracle does not have.
    /// </para>
    /// </remarks>
    public static TheoryData<object, object> ValueRoundTripCases =>
        new()
        {
            { "a string", "a string" },
            { string.Empty, string.Empty },
            { "汉字 and an emoji \U0001F600", "汉字 and an emoji \U0001F600" },
            { true, true },
            { false, false },

            // The signed integer family, all widened onto the one signed arm.
            { (byte)7, 7L },
            { (short)-9, -9L },
            { 42, 42L },
            { long.MinValue, long.MinValue },

            // The unsigned family, all widened onto the one unsigned arm.
            { (uint)4000000000, 4000000000UL },
            { ulong.MaxValue, ulong.MaxValue },

            // The floating family. 1.5 and -2.25 are both exactly representable in binary32 and binary64,
            // so the widening is lossless here and the equality is exact rather than approximate.
            { 1.5f, 1.5d },
            { -2.25d, -2.25d },

            // Decimals keep their own arm precisely so scale survives - see ADecimalKeepsItsDeclaredScale.
            { 1500.00m, 1500.00m },
            { 0.000001m, 0.000001m },

            // The unzoned datetime form: three kinds in, Unspecified out, value identical.
            {
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
            },
            {
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local),
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
            },
            {
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified),
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified)
            },

            // Microsecond resolution is inside the canonical grammar and survives exactly.
            {
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(123450L),
                new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(123450L)
            },

            { new DateOnly(1990, 6, 15), new DateOnly(1990, 6, 15) },
            { new TimeOnly(13, 45, 30), new TimeOnly(13, 45, 30) },
            { new byte[] { 0, 1, 250, 255 }, new byte[] { 0, 1, 250, 255 } },
            { Array.Empty<byte>(), Array.Empty<byte>() },
        };

    /// <summary>
    /// Every runtime type the boundary maps arrives as the carrier type the contract declares for it.
    /// </summary>
    /// <param name="value">The value written into the source carrier.</param>
    /// <param name="expected">The value - and runtime type - expected back out of the target carrier.</param>
    [Theory]
    [MemberData(nameof(ValueRoundTripCases))]
    public void EveryMappedValueTypeRoundTripsOntoItsDeclaredCarrierType(object value, object expected)
    {
        AssertColumnValueRoundTrips(value, expected);
    }

    [Fact]
    public void ANullColumnValueRoundTripsAsANullRatherThanAsAnAbsence()
    {
        // PowerBuilder has null for value types and the framework's tri-state predicates depend on it, so
        // null is a FIRST-CLASS VALUE here. Collapsing it to zero on the wire would convert "neither
        // succeeded nor failed" into "succeeded" for anything reading it back - which is why the contract
        // spends a whole AnyValue arm on it rather than relying on an absent field.
        AssertColumnValueRoundTrips(null, null);
    }

    private static void AssertColumnValueRoundTrips(object? value, object? expected)
    {
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, value);

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryEncode(source, out CarrierState? state));

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(1L, target.RowCount());

        object? restored = target.GetItemValue(1L, 1, DwBuffer.Primary);

        Assert.Equal(expected, restored);

        // The runtime type is asserted SEPARATELY from the value, because Assert.Equal on two boxed
        // numbers of different widths can succeed while the cast a consumer performs throws. A null
        // expectation has no type to assert.
        if (expected is not null)
        {
            Assert.IsType(expected.GetType(), restored);
        }
    }

    [Fact]
    public void ADecimalKeepsItsDeclaredScale()
    {
        // The fixture declares `salary decimal(2)` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L12], and
        // the scale is OBSERVABLE: normalising 1500.00 to 1500 would silently change what the legacy
        // declared. The contract's DecimalValue carries canonical invariant TEXT rather than a double for
        // exactly this reason, and decimal.ToString preserves the trailing zeroes.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 5, DwBuffer.Primary, 1500.00m);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out CarrierState? state);

        // Asserted on the WIRE too, not only after the round trip: a scale that survived only because
        // both ends happened to normalise identically would still be wrong on the published boundary.
        Assert.Equal(
            "1500.00",
            state!.Segments[0].Rows[0].Columns[0].Value.DecimalValue.Value);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        _ = codec.TryApply(target, state, CarrierBaselineTrust.AsStated);

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
        // could not express optimistic concurrency at all. This is the single assertion that justifies
        // `common.v1.DataWindowRow` carrying `original_values` beside `columns`.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        _ = source.SetItemValue(row, 3, DwBuffer.Primary, 30);
        source.RowAt(row, DwBuffer.Primary).Baseline();

        // Now the edit: the original becomes 30 and the current becomes 31.
        _ = source.SetItemValue(row, 3, DwBuffer.Primary, 31);
        _ = source.SetItemStatus(row, 3, DwBuffer.Primary, ItemStatus.DataModified);
        _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out CarrierState? state);

        // Both halves are visible on the published message, in the two separate repeated fields.
        DataWindowRow projected = state!.Segments[0].Rows[0];

        Assert.Equal(31L, projected.Columns[0].Value.Int64Value);
        Assert.Equal(30L, Assert.Single(projected.OriginalValues).Value.Int64Value);
        Assert.Equal(3L, projected.OriginalValues[0].ColumnId);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(31L, target.GetItemValue(1L, 3, DwBuffer.Primary));
        Assert.Equal(30L, target.GetItemOriginalValue(1L, 3, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));
    }

    [Fact]
    public void AColumnWhoseOriginalEqualsItsCurrentStillCarriesItsBaseline()
    {
        // 🔴 THE PRODUCER RULE CHANGED, AND THIS ROW IS WHERE THE OLD ONE WAS PINNED. The contract used to
        // let a producer OMIT a column whose original equalled its current value and require the consumer
        // to read the omission as "unchanged since the last baseline". That is an inference this codec's
        // own receive half can make and a consumer of the published contract cannot be asked to: it left a
        // FRESHLY RETRIEVED row - every column agreeing, by definition - carrying no baseline at all, so a
        // caller had to reconstruct the `updatewhere=1` predicate from an absence, and the other legal
        // reading of that absence ("no baseline exists") drops the predicate and silently overwrites.
        // AAP 0.6.3.2 admits no exemption: per row, both the current AND the original value of every
        // marked column. So one entry travels per column the row carries, agreeing or not.
        //
        // THE DECODER'S FALLBACK IS UNCHANGED AND IS STILL COVERED, by
        // AColumnWithNoStatedOriginalAdoptsTheRowsStatusRatherThanMeasuringItselfAgainstItself below: a
        // CALLER-composed payload may still state no original, and an unstated one is still read as the
        // current value rather than as a null that would match no row.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 2, DwBuffer.Primary, "Contoso");
        source.RowAt(row, DwBuffer.Primary).Baseline();

        // THE ROW STATUS IS STAMPED AFTER THE BASELINE, and the order is not cosmetic: baselining CLEARS
        // the statuses, so a row stamped before it reads as unmodified afterwards and the extraction
        // would not carry it at all. This is the same ordering the send path uses
        // [n_cst_thread_task_sqlquery.sru:L196-L198].
        _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out CarrierState? state);

        DataWindowRow projected = state!.Segments[0].Rows[0];

        Assert.Equal("Contoso", Assert.Single(projected.Columns).Value.StringValue);

        // ONE BASELINE, FOR THE ONE COLUMN THE ROW CARRIES - and it agrees with the current value, which
        // is the whole point: agreement is stated rather than left to be inferred from silence.
        ColumnValue baseline = Assert.Single(projected.OriginalValues);
        Assert.Equal("Contoso", baseline.Value.StringValue);
        Assert.Equal(2L, baseline.ColumnId);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal("Contoso", target.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("Contoso", target.GetItemOriginalValue(1L, 2, DwBuffer.Primary));

        // AND THE ADDED BASELINE CANNOT CHANGE HOW A PRODUCED PAYLOAD IS READ, which is what makes the
        // producer rule safe to change at all. The encoder stamps ItemStatus on EVERY column it projects,
        // and assigning a proto3 `optional` field sets its presence bit whatever the value - so a payload
        // this service produced always takes the honour-them-exactly branch and never the row-status
        // inference the extra original could otherwise have interacted with. The row keeps its own stamp
        // and the column keeps the unmodified status the baseline left it with.
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void ACarrierChangesetIsPositionalSoNoColumnNameTravels()
    {
        // The contract records this as a producer rule on common.v1.DataWindowRow: on a carrier changeset
        // the rows are POSITIONAL, so `column_name` is empty and `column_id` is authoritative. Stating it
        // as a test stops a well-meaning future change from populating the name "for readability" and
        // making two producers disagree about which field a consumer must trust.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 4, DwBuffer.Primary, "Redmond");

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out CarrierState? state);

        ColumnValue projected = Assert.Single(state!.Segments[0].Rows[0].Columns);

        Assert.Equal(string.Empty, projected.ColumnName);
        Assert.Equal(4L, projected.ColumnId);
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

        _ = codec.TryEncode(source, out CarrierState? state);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        _ = codec.TryApply(target, state, CarrierBaselineTrust.AsStated);

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

        _ = codec.TryEncode(source, out CarrierState? state);

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(2L, target.DeletedCount());
    }

    [Fact]
    public void EveryEncodedStateCarriesTheCanonicalThreeSegmentsEvenWhenTwoAreEmpty()
    {
        // The encoder is the reference producer for its own decoder, so the shape it emits is worth
        // pinning independently: exactly three segments, in Primary / Delete / Filter order, present even
        // when they hold nothing. An encoder that omitted an empty segment would produce a state its own
        // TryApply refuses.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };

        _ = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryEncode(source, out CarrierState? state));

        Assert.Equal(1L, state!.Processing);
        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter],
            state.Segments.Select(segment => segment.Buffer));
        Assert.Equal([1, 0, 0], state.Segments.Select(segment => segment.Rows.Count));
    }

    // ==========================================================================================
    //  THE UNREPRESENTABLE VALUES: A CODE, NEVER A THROW, AND NEVER A TRUNCATION
    //  ----------------------------------------------------------------------------------------
    //  Two of the legacy's `if ... GetChanges(ref blbData) < 0 then` arms [:L129, :L171, :L205] become
    //  reachable this way, and every receive-side call site tests a CODE [:L197, :L221, :L238]. An
    //  escaping exception would be a failure mode the oracle does not have (C-B), and it would surface
    //  as a 500 rather than as the failure result the contract publishes.
    // ==========================================================================================

    [Fact]
    public void AnUnmappedRuntimeTypeAnswersTheFailureCodeAndNeverThrows()
    {
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, Guid.NewGuid());

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryEncode(source, out CarrierState? state));

        // The partially built state is DISCARDED rather than sent, so a caller that ignored the code
        // cannot hand a half-projected image to the sink.
        Assert.Null(state);
    }

    [Fact]
    public void ATimeSpanIsUnrepresentableRatherThanCoercedOntoTheTimeArm()
    {
        // The contract's TimeValue is a TIME OF DAY, not a duration, and PowerBuilder's `time` is the
        // same. Mapping a 90-minute TimeSpan onto it would render "01:30:00" and read back a TimeOnly -
        // a value that compares unequal to what was written and means something different. Refusing is
        // the narrowing the plan mandates: a defined error, never a widened guess.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, TimeSpan.FromMinutes(90));

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryEncode(source, out CarrierState? state));

        Assert.Null(state);
    }

    [Theory]
    [MemberData(nameof(SubMicrosecondCases))]
    public void SubMicrosecondPrecisionIsRefusedRatherThanSilentlyTruncated(object value)
    {
        // The canonical fractional form is six digits - microseconds - so a tick-resolution value cannot
        // be rendered without dropping information. Truncating would make an equality test that a
        // characterization recording relies on pass with a value nobody wrote.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 1, DwBuffer.Primary, value);

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryEncode(source, out CarrierState? state));

        Assert.Null(state);
    }

    /// <summary>
    /// The two temporal types whose .NET resolution is finer than the contract's canonical form.
    /// </summary>
    public static TheoryData<object> SubMicrosecondCases =>
        new()
        {
            new TimeOnly(13, 45, 30).Add(TimeSpan.FromTicks(1L)),
            new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).AddTicks(1L),
        };

    // ==========================================================================================
    //  THE SEGMENT ROSTER: WHY COUNTING TO THREE IS NOT ENOUGH
    //  ----------------------------------------------------------------------------------------
    //  `persistence.v1.CarrierState` requires EXACTLY ONE segment per buffer IN CANONICAL ORDER, and
    //  the decoder tests that positionally. A decoder that only counted three segments would accept
    //  three all tagged Primary - which loads the Delete! and Filter! rows into the primary buffer and
    //  answers a row count nothing disagrees with. It would equally accept a Filter! row filed inside
    //  the Primary segment, and the Filter buffer's row order is INVERTED relative to the source
    //  [n_cst_thread_task_sqlupdate.sru:L235-L238], so a mis-filed row is then read in the wrong
    //  direction and pairs with the wrong data.
    //
    //  EVERY CASE BELOW ALSO ASSERTS THAT NOTHING WAS ADMITTED. This apply MERGES into a live target
    //  rather than replacing it, so a fault detected half way through would leave the target holding
    //  part of an image it then reports as rejected, and the caller has no way to unwind that.
    // ==========================================================================================

    /// <summary>
    /// Segment rosters that are not the canonical three, each of which must be refused.
    /// </summary>
    public static TheoryData<string, CarrierState> NonCanonicalRosters =>
        AsTheoryData(NonCanonicalRosterCases);

    /// <summary>The roster faults themselves, enumerable independently of the theory wrapper.</summary>
    private static IEnumerable<(string Because, CarrierState State)> NonCanonicalRosterCases()
    {
        return
        [
            ("no segments at all", StateWith()),
            ("one segment", StateWith(Segment(DwBuffer.Primary))),
            (
                "two segments",
                StateWith(Segment(DwBuffer.Primary), Segment(DwBuffer.Delete))
            ),
            (
                "a fourth segment",
                StateWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Primary))
            ),
            (
                "three segments all tagged Primary",
                StateWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Primary))
            ),
            (
                "the canonical three in reverse order",
                StateWith(
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Primary))
            ),
            (
                "Delete and Primary transposed",
                StateWith(
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Filter))
            ),
            (
                "Delete omitted and Filter duplicated",
                StateWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a buffer ordinal outside the published domain",
                StateWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete),
                    Segment((DwBuffer)99))
            ),
        ];
    }

    /// <param name="because">The roster fault, named so a failure message identifies the case.</param>
    /// <param name="state">The non-canonical state.</param>
    [Theory]
    [MemberData(nameof(NonCanonicalRosters))]
    public void ASegmentRosterThatIsNotTheCanonicalThreeIsRefusedWithoutAdmittingAnything(
        string because,
        CarrierState state)
    {
        AssertRefusedWithoutAdmitting(state, because);
    }

    /// <summary>
    /// Row-level and column-level faults inside an otherwise canonical roster, each of which must be
    /// refused before anything is admitted.
    /// </summary>
    public static TheoryData<string, CarrierState> MalformedRowContent =>
        AsTheoryData(MalformedRowContentCases);

    /// <summary>The content faults themselves, enumerable independently of the theory wrapper.</summary>
    private static IEnumerable<(string Because, CarrierState State)> MalformedRowContentCases()
    {
        return
        [
            (
                "a Filter row filed inside the Primary segment",
                StateWith(
                    Segment(DwBuffer.Primary, Row(DwBuffer.Filter, 1L, ItemStatus.DataModified)),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a Primary row filed inside the Delete segment",
                StateWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete, Row(DwBuffer.Primary, 1L, ItemStatus.DataModified)),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a zero row ordinal",
                CanonicalStateWith(Row(DwBuffer.Primary, 0L, ItemStatus.DataModified))
            ),
            (
                "a negative row ordinal",
                CanonicalStateWith(Row(DwBuffer.Primary, -1L, ItemStatus.DataModified))
            ),
            (
                "an item status outside the published domain",
                CanonicalStateWith(Row(DwBuffer.Primary, 1L, (ItemStatus)99))
            ),
            (
                "column number zero, which is the row-status sentinel",
                CanonicalStateWith(
                    Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(0L, 42)]))
            ),
            (
                "a negative column number",
                CanonicalStateWith(
                    Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(-3L, 42)]))
            ),
            (
                "the same column number twice",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42), Column(1L, 43)]))
            ),
            (
                "an original naming a column the row never carried",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42)],
                        [Column(2L, 41)]))
            ),
            (
                "the same column number twice among the originals",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42)],
                        [Column(1L, 41), Column(1L, 40)]))
            ),
            (
                "a per-column item status outside the published domain",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42, (ItemStatus)99)]))
            ),
            (
                "a value message with no arm set at all",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [new ColumnValue { ColumnId = 1L, Value = new AnyValue() }]))
            ),
            (
                "a column carrying no value message",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [new ColumnValue { ColumnId = 1L }]))
            ),
            (
                "decimal text outside the canonical grammar",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [
                            new ColumnValue
                            {
                                ColumnId = 1L,
                                Value = new AnyValue
                                {
                                    DecimalValue = new DecimalValue { Value = "1.5e3" },
                                },
                            },
                        ]))
            ),
            (
                "date text outside the canonical grammar",
                CanonicalStateWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [
                            new ColumnValue
                            {
                                ColumnId = 1L,
                                Value = new AnyValue
                                {
                                    DateValue = new DateValue { Value = "15/06/1990" },
                                },
                            },
                        ]))
            ),
        ];
    }

    /// <param name="because">The content fault, named so a failure message identifies the case.</param>
    /// <param name="state">The malformed state.</param>
    [Theory]
    [MemberData(nameof(MalformedRowContent))]
    public void MalformedRowContentIsRefusedWithoutAdmittingAnything(string because, CarrierState state)
    {
        AssertRefusedWithoutAdmitting(state, because);
    }

    [Fact]
    public void AGoodRowIsNotAdmittedWhenALaterSegmentCarriesABadOne()
    {
        // THE ORDERING PROPERTY, ASSERTED DIRECTLY. The fault is in the LAST segment and the first
        // segment is perfectly valid, so an interleaved decoder would have written the primary row before
        // discovering the problem and then reported a rejection the target does not reflect.
        CarrierState state = StateWith(
            Segment(
                DwBuffer.Primary,
                Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(1L, 42)])),
            Segment(DwBuffer.Delete),
            Segment(DwBuffer.Filter, Row(DwBuffer.Filter, 0L, ItemStatus.DataModified)));

        AssertRefusedWithoutAdmitting(state, "a valid primary row ahead of an invalid filter row");
    }

    [Fact]
    public void AnUnrepresentableValueInALaterRowStopsTheWholeApply()
    {
        // The pre-flight read exists for exactly this: an AnyValue that cannot be read back is discovered
        // BEFORE the first admission, not on the third row.
        CarrierState state = CanonicalStateWith(
            Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(1L, 42)]),
            Row(DwBuffer.Primary, 2L, ItemStatus.DataModified, [Column(1L, 43)]),
            Row(
                DwBuffer.Primary,
                3L,
                ItemStatus.DataModified,
                [new ColumnValue { ColumnId = 1L, Value = new AnyValue() }]));

        AssertRefusedWithoutAdmitting(state, "two good rows ahead of an unreadable value");
    }

    // ==========================================================================================
    //  THE PROCESSING KIND IS RECONCILED, NOT ADOPTED
    //  ----------------------------------------------------------------------------------------
    //  It used to be read and discarded, under the reading that detection is the caller's business. It
    //  is not: the two sides built their carriers from their own definitions, so a disagreement means
    //  they disagree about which serialization is even applicable, and merging a crosstab image into a
    //  tabular carrier is what trusting the sender costs. An UNASSIGNED target - a carrier whose data
    //  object has not been set - is not a disagreement, so it adopts.
    // ==========================================================================================

    [Fact]
    public void AProcessingKindThatDisagreesWithTheTargetIsRefused()
    {
        CarrierState state = CanonicalStateWith(
            processing: 2L,
            Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(1L, 42)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(0L, target.RowCount());

        // And the target's own kind is untouched by the refusal.
        Assert.Equal(1L, target.Processing.Value);
    }

    [Fact]
    public void AnUnassignedTargetAdoptsThePayloadsProcessingKind()
    {
        CarrierState state = CanonicalStateWith(
            processing: 2L,
            Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(1L, 42)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = DataWindowProcessing.Unassigned };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(2L, target.Processing.Value);
        Assert.Equal(1L, target.RowCount());
    }

    [Fact]
    public void AnAbsentStateIsRefusedBecauseTheCallerOwnsTheEmptyPayloadArm()
    {
        // A NULL STATE IS NOT THE LEGACY'S ZERO-LENGTH BLOB. The receiver has its own empty-payload arm
        // for that [n_cst_threading_task_sqlquery.sru:L194-L196], reached before the codec is consulted;
        // reaching the codec with nothing means a producer sent a chunk with neither a payload nor the
        // emptiness the caller checks for, and that is a fault rather than an empty transfer.
        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, codec.TryApply(target, state: null, CarrierBaselineTrust.AsStated));
        Assert.Equal(0L, target.RowCount());
    }

    [Fact]
    public void NoStructurallyInvalidStateEverEscapesAsAnException()
    {
        // THE CONTRACT THIS ASSERTS IS THE WHOLE REASON THE DECODER IS WRITTEN THE WAY IT IS. Every
        // receive-side call site tests a CODE [n_cst_threading_task_sqlquery.sru:L197, :L221, :L238], so
        // every rejected image must answer -1 the way SetChanges does. Driven over the union of both
        // malformed matrices plus the absent state, so a future case added to either is covered here for
        // free rather than needing a second entry.
        ChangesetPayloadCodec codec = new();

        List<CarrierState?> candidates = [null];

        candidates.AddRange(NonCanonicalRosterCases().Select(entry => (CarrierState?)entry.State));
        candidates.AddRange(MalformedRowContentCases().Select(entry => (CarrierState?)entry.State));

        foreach (CarrierState? candidate in candidates)
        {
            DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

            long result = codec.TryApply(target, candidate, CarrierBaselineTrust.AsStated);

            Assert.True(
                result == DataWindowBufferStore.DataStoreSuccess
                    || result == DataWindowBufferStore.DataStoreFailure,
                $"A malformed state answered {result}, which is neither success nor failure.");
        }
    }

    [Fact]
    public void EncodingTheSameCarrierTwiceProducesIdenticalStatesAndIdenticalBytes()
    {
        // The Golden-Master technique's one hard prerequisite is repeatability. Column order is ascending
        // and the buffer order is fixed, so equal input must yield an equal message AND equal bytes -
        // otherwise a stored recording could never be compared against a fresh capture. The byte
        // assertion is kept alongside the message assertion because it is the bytes a recording holds.
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

        _ = codec.TryEncode(first, out CarrierState? firstState);
        _ = codec.TryEncode(second, out CarrierState? secondState);

        Assert.Equal(firstState, secondState);
        Assert.Equal(firstState!.ToByteArray(), secondState!.ToByteArray());
    }

    [Fact]
    public void ColumnsAreProjectedInAscendingColumnOrderWhateverOrderTheyWereWritten()
    {
        // The ascending order is what makes the byte-identical property above hold at all, so it is
        // asserted directly rather than only through its consequence.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        _ = source.SetItemValue(row, 5, DwBuffer.Primary, 500.00m);
        _ = source.SetItemValue(row, 1, DwBuffer.Primary, 1);
        _ = source.SetItemValue(row, 3, DwBuffer.Primary, 30);

        ChangesetPayloadCodec codec = new();

        _ = codec.TryEncode(source, out CarrierState? state);

        Assert.Equal(
            [1L, 3L, 5L],
            state!.Segments[0].Rows[0].Columns.Select(column => column.ColumnId));
    }

    // ==========================================================================================
    //  THE TYPED-STATE BUILDERS
    //  ----------------------------------------------------------------------------------------
    //  Hand-built states rather than mutated encoder output, because the faults worth stating are
    //  WHOLE FIELDS - a buffer tag, a row ordinal, a duplicated column number - and a mutation sweep
    //  over serialized bytes would test the generated parser rather than this decoder.
    // ==========================================================================================

    /// <summary>
    /// Wraps a named-case producer as the theory data a <see cref="MemberDataAttribute"/> consumes.
    /// </summary>
    /// <param name="cases">The producer.</param>
    /// <remarks>
    /// The cases are authored as a plain sequence rather than directly as theory data so that a test which
    /// needs the STATES ALONE - the exception sweep below - can enumerate them without reaching through
    /// the theory wrapper's row shape. Every case therefore has exactly one definition site, and a case
    /// added to either matrix is covered by the sweep for free.
    /// </remarks>
    private static TheoryData<string, CarrierState> AsTheoryData(
        Func<IEnumerable<(string Because, CarrierState State)>> cases)
    {
        TheoryData<string, CarrierState> data = [];

        foreach ((string because, CarrierState state) in cases())
        {
            data.Add(because, state);
        }

        return data;
    }

    /// <summary>Builds a state from an explicit segment roster, canonical or not.</summary>
    /// <param name="segments">The segments, in the order they are to appear.</param>
    private static CarrierState StateWith(params CarrierBufferSegment[] segments) =>
        StateWith(1L, segments);

    /// <summary>Builds a state from an explicit segment roster and processing kind.</summary>
    /// <param name="processing">The processing kind to declare.</param>
    /// <param name="segments">The segments, in the order they are to appear.</param>
    private static CarrierState StateWith(long processing, params CarrierBufferSegment[] segments)
    {
        CarrierState state = new() { Processing = processing };

        state.Segments.AddRange(segments);

        return state;
    }

    /// <summary>
    /// Builds a state with the canonical three segments, filing each row into the segment its own buffer
    /// tag names, so that only the fault under test is faulty.
    /// </summary>
    /// <param name="rows">The rows to file.</param>
    private static CarrierState CanonicalStateWith(params DataWindowRow[] rows) =>
        CanonicalStateWith(1L, rows);

    /// <summary>
    /// Builds a state with the canonical three segments and an explicit processing kind.
    /// </summary>
    /// <param name="processing">The processing kind to declare.</param>
    /// <param name="rows">The rows to file.</param>
    private static CarrierState CanonicalStateWith(long processing, params DataWindowRow[] rows)
    {
        return StateWith(
            processing,
            [
                .. ChangesetPayloadCodec.SerializedBuffers.Select(dwBuffer =>
                    Segment(dwBuffer, [.. rows.Where(row => row.Buffer == dwBuffer)])),
            ]);
    }

    /// <summary>Builds one buffer segment.</summary>
    /// <param name="dwBuffer">The buffer tag to declare.</param>
    /// <param name="rows">The rows it carries.</param>
    private static CarrierBufferSegment Segment(DwBuffer dwBuffer, params DataWindowRow[] rows)
    {
        CarrierBufferSegment segment = new() { Buffer = dwBuffer };

        segment.Rows.AddRange(rows);

        return segment;
    }

    /// <summary>Builds one inbound row.</summary>
    /// <param name="dwBuffer">The row's own buffer tag.</param>
    /// <param name="row">The one-based row ordinal. R9: never rebased.</param>
    /// <param name="status">The row's item status.</param>
    /// <param name="columns">The current values, or none.</param>
    /// <param name="originals">The original values, or none.</param>
    private static DataWindowRow Row(
        DwBuffer dwBuffer,
        long row,
        ItemStatus status,
        ColumnValue[]? columns = null,
        ColumnValue[]? originals = null)
    {
        DataWindowRow projected = new()
        {
            Buffer = dwBuffer,
            Row = row,
            ItemStatus = status,
        };

        projected.Columns.AddRange(columns ?? []);
        projected.OriginalValues.AddRange(originals ?? []);

        return projected;
    }

    /// <summary>Builds one column value through the same mapper the encoder uses.</summary>
    /// <param name="columnId">The column number to declare, valid or not.</param>
    /// <param name="value">The value, which must be one the mapper can express.</param>
    /// <param name="status">The column's own status, or none for an absent one.</param>
    private static ColumnValue Column(long columnId, object? value, ItemStatus? status = null)
    {
        Assert.True(
            CarrierValue.TryToWire(value, out AnyValue? wire),
            "A builder value must be representable; an unrepresentable one belongs in its own test.");

        ColumnValue column = new() { ColumnId = columnId, Value = wire };

        if (status is not null)
        {
            column.ItemStatus = status.Value;
        }

        return column;
    }

    // ==============================================================================================
    //  THE ROW-STATUS CONVENTION ON A CALLER-COMPOSED UPDATE PAYLOAD
    //  --------------------------------------------------------------------------------------------
    //  `common.v1.ColumnValue.item_status` is proto3 `optional` and its own contract says absence is
    //  the NORMAL case, so the payload the published contract describes - a row stamped DataModified!
    //  carrying its current values and its originals - supplies no per-column status at all. Reading
    //  every such column as NotModified! made the update carrier generate no SET list, report zero
    //  affected rows, and be classified as an optimistic-concurrency conflict: the caller was told
    //  another writer had changed a row nothing had touched, and the only way to make an update apply
    //  was to send a field the contract calls optional.
    // ==============================================================================================

    [Fact]
    public void AModifiedRowThatSuppliedNoPerColumnStatusAdoptsItForTheColumnsThatMoved()
    {
        // The legacy reads A ROW's status with column index 0 - GetItemStatus(nRow, 0, Primary!)
        // [n_cst_thread_task_sqlupdate.sru:L160] - and a row is DataModified! precisely BECAUSE a column
        // of it was modified. IT IS NOT because EVERY column of it was: the runtime flips a column's
        // status only where SetItem changed a value, which is why the generated SET list names the
        // changed columns rather than the whole updatable set. The payload carries both halves of every
        // marked column anyway - updatewhere=1 needs the originals for the predicate - so the pair itself
        // says which columns moved, and reading it reconstructs exactly the per-column state the
        // in-process runtime would hold.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [Column(1, 25L), Column(2, "edited"), Column(3, 77L)],
                [Column(1, 25L), Column(2, "original"), Column(3, 77L)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        // ONLY column 2 moved, so only column 2 is modified. Columns 1 and 3 restated their stored
        // values, which is what a caller assembling a payload from a retrieval does for every column it
        // did not touch - and stamping THOSE modified widened the SET list past the oracle's and, on the
        // key column, turned an ordinary update into a delete-plus-insert.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));

        // The row's own status is unchanged, and BOTH value sets survive on EVERY column - the originals
        // are what the updatewhere=1 predicate is built from, including for the columns that did not
        // move, so an unmoved column staying NotModified! does not remove it from the predicate.
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal("edited", target.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("original", target.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
        Assert.Equal(25L, target.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(77L, target.GetItemOriginalValue(1L, 3, DwBuffer.Primary));
    }

    [Fact]
    public void AColumnRestatedInADifferentNumericArmIsNotReadAsAMove()
    {
        // 🔴 THE ROUND-TRIP CASE, AND THE ONE THAT MADE AN ORDINARY UPDATE EMIT A DELETE PLUS AN INSERT.
        // AnyValue is a union in which one number has several faithful spellings: a retrieval answers a
        // legacy `number` column through double_value, and a caller re-sending that same number as
        // int64_value is equally within the contract. Decoded, those are 28.0d and 28L - which Equals
        // reports DIFFERENT and which SQLite compares EQUAL under numeric affinity. Reading the wire arm
        // rather than the value made the KEY column of a round-tripped payload look modified, and
        // updatekeyinplace=no then turned the update into a delete-plus-insert that reported
        // rowsUpdated = 0.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [Column(1, 28L), Column(2, "edited"), Column(3, 20000.50m)],
                [Column(1, 28.0d), Column(2, "original"), Column(3, 20000.5d)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        // The key spelled int64 against an original spelled double: the same number, so unmoved.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));

        // The genuinely edited column still moves.
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));

        // decimal 20000.50 against double 20000.5 is one number in two arms, not a two-place difference.
        // NOT A TOLERANCE: 20000.51m against 20000.5d would be a move, which the next assertion pins.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));

        Assert.False(CarrierValue.AreEquivalent(20000.51m, 20000.5d));
        Assert.True(CarrierValue.AreEquivalent(20000.50m, 20000.5d));
        Assert.True(CarrierValue.AreEquivalent(28L, 28.0d));
        Assert.False(CarrierValue.AreEquivalent(28L, "28"));
        Assert.False(CarrierValue.AreEquivalent(1L, true));
        Assert.True(CarrierValue.AreEquivalent(null, null));
        Assert.False(CarrierValue.AreEquivalent(null, 0L));
        Assert.True(CarrierValue.AreEquivalent(new byte[] { 1, 2 }, new byte[] { 1, 2 }));
        Assert.False(CarrierValue.AreEquivalent(new byte[] { 1, 2 }, new byte[] { 1, 3 }));
    }

    [Fact]
    public void AColumnWithNoStatedOriginalReadsUnchangedOnARowThatIsNotAnInsert()
    {
        // 🔴 ABSENCE OF AN ORIGINAL MEANS UNCHANGED, NEVER "THE CURRENT VALUE IS THE BASELINE".
        //
        // This column used to ADOPT the row's modified status, on the reasoning that a column with no
        // baseline cannot be measured against one. The premise holds; the conclusion was backwards for an
        // update. Stamped modified, the column entered the generated SET list - while the predicate
        // beside it read its baseline as the value being WRITTEN, because the codec substituted the
        // current value for the missing original. So an update that changed a value and omitted its
        // original was aimed at whichever row already held the NEW value: a write to a different row than
        // the one the caller read, reported as success. For a KEY column it was worse still, because
        // SqlUpdateCarrier.HasKeyChange then saw current == original and took the ordinary UPDATE path,
        // bypassing the updatekeyinplace=no DELETE-plus-INSERT the fixture demands.
        //
        // The contract's own encoding says what absence means: TryProjectRow emits an original ONLY where
        // it differs from the current value, so no original IS a statement that the column did not move.
        // That is now what it reads as - which also makes this the transfer path's exact reading rather
        // than a lenient one.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [Column(1, 25L), Column(2, "edited")],
                [Column(1, 25L)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        // Column 1 stated an original that proves it did not move.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));

        // Column 2 stated none, so it reads unchanged - and its baseline equals its current value, which
        // is precisely what "unchanged" means and is no longer a claim about a value that moved.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
        Assert.Equal(
            "edited",
            target.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void AnInsertShapedRowStillAdoptsItsStatusForColumnsThatStateNoOriginal()
    {
        // THE ONE CASE WHERE ADOPTING IS RIGHT, AND IT IS WHY THE RULE IS CONDITIONED ON THE ROW RATHER
        // THAN APPLIED FLATLY. A New!/NewModified! row has NO prior state, so it carries no originals at
        // all - and it generates an INSERT, which has no where clause, so nothing here can be aimed at
        // the wrong row. Resolving its columns to NotModified! instead would not be the state the
        // in-process runtime holds for a new row, and would leave the insert with nothing marked.
        CarrierState state = CanonicalStateWith(
            Row(DwBuffer.Primary, 1L, ItemStatus.NewModified, [Column(2, "inserted"), Column(3, 41L)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        Assert.Equal(ItemStatus.NewModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NewModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));
    }

    // ==============================================================================================
    //  THE UPDATE PATH'S BASELINE REQUIREMENT - CarrierBaselineTrust.RequiredOnChangedRows
    //  --------------------------------------------------------------------------------------------
    //  The originals in an update payload ARE the optimistic-concurrency check, and the caller that
    //  composes them is the same party whose values are being written. So on that path an unstated
    //  baseline is refused outright rather than inferred - which is no more than AAP 0.6.3.2 already
    //  requires a payload to transmit. The transfer path keeps the producer's own encoding, because
    //  there the producer is this service.
    // ==============================================================================================

    [Fact]
    public void TheUpdatePathRefusesAChangedRowThatLeftAColumnsBaselineUnstated()
    {
        // The identical payload the transfer path admits above. Here it is refused, because here the
        // originals become a predicate and a caller is not a trustworthy source of a value it is
        // simultaneously overwriting. Refused WHOLE: nothing is admitted, so a half-applied payload
        // cannot be left behind for the caller to unwind.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [Column(1, 25L), Column(2, "edited")],
                [Column(1, 25L)]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryApply(target, state, CarrierBaselineTrust.RequiredOnChangedRows));

        Assert.Equal(0L, target.RowCount());
    }

    [Fact]
    public void TheUpdatePathAdmitsAChangedRowThatStatesEveryBaseline()
    {
        // THE CONTROL, and the shape a conforming caller sends: both halves of every column it carries.
        // Nothing about the requirement is unsatisfiable - it is one field per column, and the retrieval
        // the caller read the row from answered every one of them.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [Column(1, 25L), Column(2, "edited")],
                [Column(1, 25L), Column(2, "original")]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryApply(target, state, CarrierBaselineTrust.RequiredOnChangedRows));

        // The unmoved column reads unchanged, the moved one reads modified, and the moved one's BASELINE
        // is the value the caller read rather than the value it wrote - which is the whole point.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
        Assert.Equal("original", target.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("edited", target.GetItemValue(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void TheUpdatePathExemptsAnInsertShapedRowFromTheBaselineRequirement()
    {
        // An insert states no originals because it has no prior state, and generates no predicate. Both
        // members of the pair are exempt, because ItemStatusMachine treats both as insert-shaped on the
        // way out of this codec too.
        foreach (ItemStatus status in (ItemStatus[])[ItemStatus.New, ItemStatus.NewModified])
        {
            CarrierState state = CanonicalStateWith(
                Row(DwBuffer.Primary, 1L, status, [Column(2, "inserted")]));

            ChangesetPayloadCodec codec = new();
            DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

            Assert.Equal(
                DataWindowBufferStore.DataStoreSuccess,
                codec.TryApply(target, state, CarrierBaselineTrust.RequiredOnChangedRows));

            Assert.Equal(1L, target.RowCount());
        }
    }

    [Fact]
    public void TheUpdatePathRefusesADeleteBufferRowThatStatesNoBaseline()
    {
        // A DELETE IS A PREDICATE TOO, and the review's own wording puts deleted rows beside modified
        // ones for exactly that reason: DELETE ... WHERE is built from the same originals. A delete row
        // that states none would have been aimed at the caller's current values.
        CarrierState state = CanonicalStateWith(
            Row(DwBuffer.Delete, 1L, ItemStatus.NotModified, [Column(1, 25L), Column(2, "gone")]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            codec.TryApply(target, state, CarrierBaselineTrust.RequiredOnChangedRows));
    }

    [Fact]
    public void ANewModifiedRowThatSuppliedNoPerColumnStatusAdoptsItToo()
    {
        // Both members of the modified pair, because ItemStatusMachine.IsModified is the predicate and
        // an insert-shaped row reaches the same codec.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.NewModified,
                [Column(2, "inserted")]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(ItemStatus.NewModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void OneExplicitlyStampedColumnMakesTheProducerExplicitAboutEveryColumn()
    {
        // THE PARTIAL UPDATE MUST STAY EXACT. A caller that stamps the one column it changed is stating
        // something about all of them, so the unstamped ones stay NotModified! and the generated SET list
        // still writes exactly one column - which is what stops an unmodified value clobbering a
        // concurrent writer's.
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [
                    Column(1, 25L),
                    Column(2, "edited", ItemStatus.DataModified),
                    Column(3, 77L),
                ]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 3, DwBuffer.Primary));
    }

    [Fact]
    public void AnExplicitNotModifiedIsAStatementAndIsHonouredEvenOnAModifiedRow()
    {
        // Presence, not value, is the test. A caller that says NotModified! outright is heard, and the
        // resulting empty SET list is answered as the payload contradiction it is by the update
        // classifier - never as a concurrency conflict [ConflictDetector.InvalidUpdateData].
        CarrierState state = CanonicalStateWith(
            Row(
                DwBuffer.Primary,
                1L,
                ItemStatus.DataModified,
                [
                    Column(1, 25L, ItemStatus.NotModified),
                    Column(2, "edited", ItemStatus.NotModified),
                ]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void AnUnmodifiedRowThatSuppliedNoPerColumnStatusLeavesItsColumnsUnmodified()
    {
        // The inference is gated on the ROW being modified. A NotModified! row states nothing that could
        // be adopted, so its columns stay exactly where they were.
        CarrierState state = CanonicalStateWith(
            Row(DwBuffer.Primary, 1L, ItemStatus.NotModified, [Column(2, "retrieved")]));

        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
    }

    [Fact]
    public void APayloadThisServiceProducedNeverTakesTheInferenceBranch()
    {
        // 🔴 THE PROPERTY THAT KEEPS THE RETRIEVE PATH UNTOUCHED. TryProjectBufferSegment assigns
        // ItemStatus on EVERY projected column, and assigning a proto3 `optional` field sets its presence
        // bit whatever the value - so a round-tripped payload always takes the honour-them-exactly branch
        // and the inference is reachable only from a caller-composed payload. Asserted on the WIRE, which
        // is where the property actually lives.
        DataWindowBufferStore source = new() { Processing = new DataWindowProcessing(1L) };
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        _ = source.SetItemValue(row, 2, DwBuffer.Primary, "retrieved");
        source.RowAt(row, DwBuffer.Primary).Baseline();
        _ = source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        ChangesetPayloadCodec codec = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            codec.TryEncode(source, out CarrierState? state));

        DataWindowRow projected = state!.Segments[0].Rows[0];

        Assert.Equal(ItemStatus.DataModified, projected.ItemStatus);
        Assert.All(projected.Columns, column => Assert.True(column.HasItemStatus));

        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, codec.TryApply(target, state, CarrierBaselineTrust.AsStated));

        // The column was never modified in its own right, and the round trip preserves that even though
        // the ROW is stamped modified - which is exactly the retrieve path's shape.
        Assert.Equal(ItemStatus.NotModified, target.GetItemStatus(1L, 2, DwBuffer.Primary));
    }

    /// <summary>
    /// Asserts that <paramref name="state"/> answers the legacy failure code and that the target is
    /// completely untouched afterwards.
    /// </summary>
    /// <param name="state">The malformed state.</param>
    /// <param name="because">The fault, quoted into the failure message.</param>
    private static void AssertRefusedWithoutAdmitting(CarrierState state, string because)
    {
        ChangesetPayloadCodec codec = new();
        DataWindowBufferStore target = new() { Processing = new DataWindowProcessing(1L) };

        long result = codec.TryApply(target, state, CarrierBaselineTrust.AsStated);

        Assert.True(
            result == DataWindowBufferStore.DataStoreFailure,
            $"A state with {because} answered {result} rather than the failure code.");

        // A PARTIALLY APPLIED IMAGE IS WORSE THAN A REJECTED ONE, because the caller's next read would
        // see rows the payload never justified.
        Assert.Equal(0L, target.RowCount());
        Assert.Equal(0L, target.DeletedCount());
        Assert.Equal(0L, target.FilteredCount());
    }
}
