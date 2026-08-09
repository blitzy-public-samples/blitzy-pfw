// ==============================================================================================
//  FullStateCodecTests - the full-state blob codec and the two documented defects it owns
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Buffers/FullStateCodec.cs
//
//  BEHAVIOURAL ORACLE (all READ ONLY per constraint C-C)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//          :L93-L101   the codec selector and the full-state send path
//          :L559-L581  DEFECT 3 at :L563 and the changeset arm's opposite note at :L578
//          :L672-L676  DEFECT 4 at :L673
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184-L251
//          the receive path and the sign-opposite result normalization
//      docs/PB多线程绕坑提示.md   the two threading hazards
//
//  ==============================================================================================
//  THESE ARE UNIT TESTS. THEY ARE NOT CHARACTERIZATION TESTS, AND THEY CANNOT BE.
//  ==============================================================================================
//  ALL TWELVE DataWindow definitions in this repository were scanned for their `processing=` value.
//  ELEVEN ARE `processing=1` AND ONE, ws_objects/pfw.tests.pbl.src/dw_barcode.srd, IS `processing=0`.
//  NOT ONE IS 4 OR 5 - and 4 and 5, Crosstab and Composite, are the only two values that select this
//  codec [n_cst_thread_task_sqlquery.sru:L94]. THERE IS THEREFORE NO LEGACY FIXTURE ON THIS PATH AND
//  NO RECORDING TO COMPARE AGAINST: neither of the two defects below can be characterized.
//
//  Every case in this file consequently runs against a SYNTHETIC carrier whose processing kind is set
//  to crosstab or composite by hand, and every test name carries the suffix `_UnitLevelNoOracle` or
//  `_SyntheticCrosstabNoOracle` so that its evidentiary status is legible at the point of failure
//  rather than buried in this header. NO CHARACTERIZATION COVERAGE IS CLAIMED OR IMPLIED anywhere.
//
//  That is a deliberate posture rather than an apology. The migration plan likewise declines to claim
//  a verified container bring-up where no Docker daemon was available, and requires reporting a
//  limitation instead of approximating past it. Because no oracle exists here, unit coverage is the
//  ONLY evidence available for this file, which is why this suite is exhaustive rather than
//  representative.
//
//  NO DATABASE, NO CONTAINER, NO CLOCK (C-E, C-H). Every case is a pure transform over in-memory
//  buffers. No connection is opened, no schema is assumed - and note that no crosstab schema exists
//  anywhere in the repository, so none is invented here - and no time is read.
//
//  C-F SELF-AUDIT: every value in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears in any form.
// ==============================================================================================

using System.Globalization;

using Google.Protobuf;

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;

using Xunit;

// RetCode IS DELIBERATELY NOT ALIASED HERE, for the same reason the sibling DataWindowBuffersTests
// records: GlobalUsings.cs owns this assembly's alias - `global using RetCode =
// PowerFramework.Shared.Kernel.RetCode;` - and repeating a file-local alias of the same name is error
// CS1537 rather than a harmless duplicate. The bare name already means the kernel class here.
namespace PowerFramework.Persistence.Tests;

/// <summary>
/// A recording stand-in for the four carrier operations the full-state path invokes, reproducing only
/// what <see cref="IFullStateCarrierSurface"/> declares.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a mocking package, because this service adds no package
/// reference for one (C-I) and four members do not warrant one. The CALL LOG is the point: DEFECT 4's
/// whole behaviour is that a modify call happens exactly once, or not at all, and that the read
/// precedes the write - none of which can be asserted from return values.
/// </remarks>
internal sealed class RecordingCarrierSurface : IFullStateCarrierSurface
{
    /// <summary>Every call, in order, as "member:argument".</summary>
    internal List<string> Calls { get; } = [];

    /// <summary>What <see cref="Describe"/> answers.</summary>
    internal string DescribeAnswer { get; set; } = string.Empty;

    /// <summary>What <see cref="Modify"/> answers. Empty means success.</summary>
    internal string ModifyAnswer { get; set; } = string.Empty;

    /// <summary>What <see cref="SetSort"/> answers. One means success.</summary>
    internal long SetSortAnswer { get; set; } = DataWindowBufferStore.DataStoreSuccess;

    /// <summary>What <see cref="SetFilter"/> answers. One means success.</summary>
    internal long SetFilterAnswer { get; set; } = DataWindowBufferStore.DataStoreSuccess;

    /// <inheritdoc/>
    public string Describe(string property)
    {
        Calls.Add("Describe:" + property);

        return DescribeAnswer;
    }

    /// <inheritdoc/>
    public string Modify(string modifyString)
    {
        Calls.Add("Modify:" + modifyString);

        return ModifyAnswer;
    }

    /// <inheritdoc/>
    public long SetSort(string sort)
    {
        Calls.Add("SetSort:" + sort);

        return SetSortAnswer;
    }

    /// <inheritdoc/>
    public long SetFilter(string filter)
    {
        Calls.Add("SetFilter:" + filter);

        return SetFilterAnswer;
    }
}

/// <summary>
/// Pins the full-state send and receive paths, the deterministic payload round trip, and both preserved
/// defects. UNIT LEVEL ONLY - see the file header for why no characterization evidence exists.
/// </summary>
public sealed class FullStateCodecTests
{
    #region Helpers

    /// <summary>
    /// The four processing kinds this codec must REFUSE, all of which land in the legacy's
    /// <c>case else</c> arm and therefore belong to <c>Buffers/ChangesetCodec.cs</c>. Two of them are the
    /// only values the repository's own DataWindows actually declare.
    /// </summary>
    public static TheoryData<long> ChangesetProcessingKinds => [0L, 1L, 2L, 3L, 6L, -1L];

    /// <summary>
    /// The two processing kinds that select this codec, named by the legacy's own comment at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L94</c>.
    /// </summary>
    public static TheoryData<long> FullStateProcessingKinds => [4L, 5L];

    /// <summary>
    /// Builds an empty carrier whose declared processing kind selects the full-state path.
    /// </summary>
    /// <param name="processingKind">4 for crosstab, 5 for composite.</param>
    /// <returns>The synthetic carrier.</returns>
    /// <remarks>
    /// SYNTHETIC BY NECESSITY. No DataWindow in the repository declares either kind, so there is nothing
    /// to load a fixture from; the kind is assigned directly.
    /// </remarks>
    private static DataWindowBufferStore NewCrosstabCarrier(long processingKind = 4L)
    {
        return new DataWindowBufferStore { Processing = new DataWindowProcessing(processingKind) };
    }

    /// <summary>
    /// Appends a row and gives it one string column, one status and nothing else.
    /// </summary>
    /// <param name="store">The carrier to seed.</param>
    /// <param name="buffer">Which buffer to seed.</param>
    /// <param name="value">The value for column one.</param>
    /// <param name="status">The row's item status.</param>
    /// <returns>The new row's one-based number.</returns>
    private static long SeedRow(
        DataWindowBufferStore store,
        DwBuffer buffer,
        string value,
        ItemStatus status = ItemStatus.NotModified)
    {
        long row = store.AppendRow(buffer, status);

        store.SetItemValue(row, 1, buffer, value);
        store.SetItemStatus(row, 0, buffer, status);

        return row;
    }

    /// <summary>
    /// A handover that records every chunk it is given and answers a configurable result.
    /// </summary>
    /// <param name="captured">The list every chunk is appended to.</param>
    /// <param name="answer">The value the handover answers. Below zero is failure.</param>
    /// <returns>The handover delegate.</returns>
    private static FullStateChunkHandover RecordingHandover(List<QueryDataChunk> captured, long answer)
    {
        return chunk =>
        {
            captured.Add(chunk);

            return answer;
        };
    }

    /// <summary>
    /// An error reporter that records every report it is given.
    /// </summary>
    /// <param name="reported">The list every report is appended to.</param>
    /// <returns>The reporter delegate.</returns>
    private static FullStateErrorReporter RecordingReporter(List<(long Code, string Message)> reported)
    {
        return (code, message) => reported.Add((code, message));
    }

    /// <summary>
    /// The gate value for which synchronization IS dispatched by processing kind - the legacy's
    /// <c>Not of_IsMainThread() and Not _of_NeedCreate()</c> at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L559</c>.
    /// </summary>
    private static FullStateTransferGate OpenGate =>
        new(IsMainThread: false, TaskNeedsCreate: false);

    #endregion

    #region The codec discriminator - total, with no overlap and no gap

    /// <summary>
    /// Every processing kind belongs to exactly one codec: 4 and 5 to this one, everything else to the
    /// changeset codec. The partition is TOTAL, which is what makes routing a payload to the wrong
    /// decoder impossible.
    /// </summary>
    [Theory]
    [MemberData(nameof(FullStateProcessingKinds))]
    public void ProcessingKindSelectsThisCodecForCrosstabAndComposite_UnitLevelNoOracle(long kind)
    {
        DataWindowBufferStore store = NewCrosstabCarrier(kind);

        Assert.True(store.RequiresFullStateTransfer);
    }

    /// <summary>
    /// The other side of the same partition. Note that 1 and 0 are the ONLY kinds the repository's own
    /// twelve DataWindows declare, which is precisely why this codec has no fixture.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void ProcessingKindSelectsTheChangesetCodecForEveryOtherValue_UnitLevelNoOracle(long kind)
    {
        DataWindowBufferStore store = NewCrosstabCarrier(kind);

        Assert.False(store.RequiresFullStateTransfer);
    }

    /// <summary>
    /// Sending a changeset-arm carrier through this codec is a programming error - the wrong codec was
    /// selected - and fails fast rather than producing an image the receiver would decode as a
    /// changeset.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void Send_RejectsACarrierBelongingToTheChangesetArm_UnitLevelNoOracle(long kind)
    {
        DataWindowBufferStore store = NewCrosstabCarrier(kind);
        List<QueryDataChunk> captured = [];
        List<(long Code, string Message)> reported = [];

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => FullStateCodec.Send(store, RecordingHandover(captured, 1L), RecordingReporter(reported)));

        Assert.Contains("ChangesetCodec", failure.Message, StringComparison.Ordinal);
        Assert.Empty(captured);
        Assert.Empty(reported);
    }

    /// <summary>
    /// The same guard on the synchronization entry point, so a mis-routed carrier cannot have its sort
    /// and filter synchronized by the arm that should have cleared them.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void SynchronizeSortAndFilter_RejectsACarrierBelongingToTheChangesetArm_UnitLevelNoOracle(
        long kind)
    {
        DataWindowBufferStore store = NewCrosstabCarrier(kind);
        RecordingCarrierSurface surface = new();
        List<(long Code, string Message)> reported = [];

        Assert.Throws<InvalidOperationException>(
            () => FullStateCodec.SynchronizeSortAndFilter(
                store,
                surface,
                "age A",
                "age > 1",
                OpenGate,
                RecordingReporter(reported)));

        Assert.Empty(surface.Calls);
    }

    #endregion

    #region The send path - single shot [:L94-L101]

    /// <summary>
    /// EXACTLY ONE CHUNK, carrying the one-based count and position of <c>:L97</c> and the full-state
    /// flag that is the receive-side selector.
    /// </summary>
    /// <remarks>
    /// R9: <c>1, 1</c> is a count and a POSITION, not an index. A zero-based reading is wrong by one for
    /// the whole stream.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FullStateProcessingKinds))]
    public void Send_EmitsExactlyOneChunkWithOneBasedCountAndIndexAndFullStateTrue_UnitLevelNoOracle(
        long kind)
    {
        DataWindowBufferStore store = NewCrosstabCarrier(kind);
        SeedRow(store, DwBuffer.Primary, "a");
        SeedRow(store, DwBuffer.Primary, "b");

        List<QueryDataChunk> captured = [];
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.Send(
            store,
            RecordingHandover(captured, DataWindowBufferStore.DataStoreSuccess),
            RecordingReporter(reported));

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(reported);

        QueryDataChunk chunk = Assert.Single(captured);

        Assert.Equal(1L, chunk.ChunkCount);
        Assert.Equal(1L, chunk.ChunkIndex);
        Assert.True(chunk.FullState);

        // A CAPTURED IMAGE IS PRESENT AND CARRIES THE CANONICAL THREE SEGMENTS, which is what
        // distinguishes it from the ABSENT state that means "clear the target" [:L207-L209].
        Assert.NotNull(chunk.State);
        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter],
            chunk.State.Segments.Select(segment => segment.Buffer));
    }

    /// <summary>
    /// STILL ONE CHUNK at a row count that would produce many changeset chunks. There is no chunk loop
    /// and no chunk-size arithmetic on this path.
    /// </summary>
    [Fact]
    public void Send_NeverChunksHoweverManyRowsTheCarrierHolds_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();

        for (int index = 0; index < 250; index++)
        {
            SeedRow(store, DwBuffer.Primary, "row");
        }

        List<QueryDataChunk> captured = [];

        FullStateCodec.Send(store, RecordingHandover(captured, 1L), RecordingReporter([]));

        Assert.Single(captured);
        Assert.Equal(1L, captured[0].ChunkCount);
    }

    /// <summary>
    /// <c>Data.Reset()</c> at <c>:L96</c> runs AFTER the capture, so the chunk carries the rows and the
    /// source is left empty. Resetting here is legal precisely because of that ordering - the same
    /// operation is forbidden mid-loop in the changeset codec.
    /// </summary>
    [Fact]
    public void Send_ResetsTheSourceAfterCapturingItSoTheChunkStillCarriesTheRows_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        SeedRow(store, DwBuffer.Primary, "kept");
        SeedRow(store, DwBuffer.Filter, "filtered");

        List<QueryDataChunk> captured = [];

        FullStateCodec.Send(store, RecordingHandover(captured, 1L), RecordingReporter([]));

        // The source is empty: Reset ran.
        Assert.Equal(0L, store.RowCount());
        Assert.Equal(0L, store.FilteredCount());

        // The image is not: the capture ran first.
        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, captured[0].State));
        Assert.Equal(1L, restored.RowCount());
        Assert.Equal(1L, restored.FilteredCount());
    }

    /// <summary>
    /// A handover result BELOW ZERO reports the verbatim text of <c>:L98</c> and returns the internal
    /// error of <c>:L99</c>.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(-27L)]
    [InlineData(long.MinValue)]
    public void Send_ReportsTransDataFailedVerbatimAndReturnsInternalError_UnitLevelNoOracle(
        long handoverResult)
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        SeedRow(store, DwBuffer.Primary, "a");

        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.Send(
            store,
            RecordingHandover([], handoverResult),
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);

        (long Code, string Message) report = Assert.Single(reported);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, report.Code);

        // BYTE EXACT. The text reaches log records and characterization recordings.
        Assert.Equal("TransData Failed", report.Message);
        Assert.Equal(FullStateCodec.TransDataFailedMessage, report.Message);
    }

    /// <summary>
    /// The legacy test is <c>&lt; 0</c> - A SIGN TEST - so zero and any positive value are both success.
    /// It is NOT a comparison against the DataStore success value, and reading it as one would turn a
    /// successful zero into a failure.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(long.MaxValue)]
    public void Send_TreatsZeroAndEveryPositiveHandoverResultAsSuccess_UnitLevelNoOracle(
        long handoverResult)
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        SeedRow(store, DwBuffer.Primary, "a");

        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.Send(
            store,
            RecordingHandover([], handoverResult),
            RecordingReporter(reported));

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(reported);
    }

    /// <summary>
    /// Null arguments are programming errors and fail fast, matching the fail-fast posture the migration
    /// preserves rather than degrading gracefully.
    /// </summary>
    [Fact]
    public void Send_RejectsNullArguments_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();

        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.Send(null!, RecordingHandover([], 1L), RecordingReporter([])));
        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.Send(store, null!, RecordingReporter([])));
        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.Send(store, RecordingHandover([], 1L), null!));
    }

    #endregion

    #region DEFECT 3 - full state requires sort and filter synchronization [:L563]

    /// <summary>
    /// With the gate open, both expressions are applied - SORT FIRST, then filter, which is the order of
    /// <c>:L564-L575</c>.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_AppliesSortThenFilterWhenTheGateIsOpen_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new();
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A salary A",
            "age > 1",
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(reported);
        Assert.Equal(["SetSort:age A salary A", "SetFilter:age > 1"], surface.Calls);
    }

    /// <summary>
    /// WITH THE GATE CLOSED, NOTHING IS APPLIED. The legacy takes its processing-agnostic else-branch at
    /// <c>:L582-L597</c> instead, which belongs to the SQL query task rather than to either codec, so
    /// this codec must not synchronize on that path and must not guess at that branch's condition.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SynchronizeSortAndFilter_AppliesNothingWhenTheGateIsClosed_UnitLevelNoOracle(
        bool isMainThread,
        bool taskNeedsCreate)
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new();
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A",
            "age > 1",
            new FullStateTransferGate(isMainThread, taskNeedsCreate),
            RecordingReporter(reported));

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(surface.Calls);
        Assert.Empty(reported);
    }

    /// <summary>
    /// The gate's own predicate, stated directly: it is the DOUBLE NEGATIVE of <c>:L559</c> and is open
    /// on exactly one of the four combinations.
    /// </summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void TransferGateIsOpenOnlyOffTheMainThreadWithNoCreateNeeded_UnitLevelNoOracle(
        bool isMainThread,
        bool taskNeedsCreate,
        bool expected)
    {
        FullStateTransferGate gate = new(isMainThread, taskNeedsCreate);

        Assert.Equal(expected, gate.SynchronizesByProcessingKind);
    }

    /// <summary>
    /// An EMPTY expression is skipped, exactly as <c>if _sNewSort &lt;&gt; ""</c> skips it at
    /// <c>:L564</c>. Null is treated as empty because an unassigned PowerBuilder string IS the empty
    /// string.
    /// </summary>
    [Theory]
    [InlineData(null, null, 0)]
    [InlineData("", "", 0)]
    [InlineData("age A", "", 1)]
    [InlineData("", "age > 1", 1)]
    [InlineData("age A", "age > 1", 2)]
    public void SynchronizeSortAndFilter_SkipsEmptyAndNullExpressions_UnitLevelNoOracle(
        string? sort,
        string? filter,
        int expectedCallCount)
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new();

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            sort,
            filter,
            OpenGate,
            RecordingReporter([]));

        Assert.Equal(RetCode.OK, result);
        Assert.Equal(expectedCallCount, surface.Calls.Count);
    }

    /// <summary>
    /// THE APPLIED VALUE IS TRIMMED AND THE REPORTED VALUE IS NOT. That asymmetry is in the source -
    /// <c>SetSort(Trim(_sNewSort))</c> at <c>:L565</c> against <c>"SetSort: " + _sNewSort</c> at
    /// <c>:L566</c>, the second with no <c>Trim</c> - and it is reproduced rather than tidied because the
    /// reported text is observable.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_TrimsWhatItAppliesButReportsTheUntrimmedValue_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new() { SetSortAnswer = DataWindowBufferStore.DataStoreFailure };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "   age A   ",
            null,
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);

        // Applied: trimmed.
        Assert.Equal(["SetSort:age A"], surface.Calls);

        // Reported: NOT trimmed.
        Assert.Equal("SetSort:    age A   ", "SetSort: " + "   age A   ");
        Assert.Equal("SetSort:    age A   ", Assert.Single(reported).Message);
    }

    /// <summary>
    /// The filter side of the same asymmetry.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_TrimsTheFilterButReportsItUntrimmed_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new()
        {
            SetFilterAnswer = DataWindowBufferStore.DataStoreFailure,
        };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            null,
            "  age > 1 ",
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal(["SetFilter:age > 1"], surface.Calls);
        Assert.Equal("SetFilter:   age > 1 ", Assert.Single(reported).Message);
    }

    /// <summary>
    /// The <c>SetSort: </c> prefix is BYTE EXACT, TRAILING SPACE INCLUDED, and the failure answers
    /// <c>E_INVALID_ARGUMENT</c> [<c>:L566-L567</c>].
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_ReportsTheSetSortPrefixByteExact_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new() { SetSortAnswer = DataWindowBufferStore.DataStoreFailure };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A",
            null,
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal("SetSort: ", FullStateCodec.SetSortMessagePrefix);
        Assert.Equal("SetSort: age A", Assert.Single(reported).Message);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, reported[0].Code);
    }

    /// <summary>
    /// The <c>SetFilter: </c> prefix is BYTE EXACT, TRAILING SPACE INCLUDED [<c>:L572-L573</c>].
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_ReportsTheSetFilterPrefixByteExact_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new()
        {
            SetFilterAnswer = DataWindowBufferStore.DataStoreFailure,
        };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            null,
            "age > 1",
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal("SetFilter: ", FullStateCodec.SetFilterMessagePrefix);
        Assert.Equal("SetFilter: age > 1", Assert.Single(reported).Message);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, reported[0].Code);
    }

    /// <summary>
    /// A REJECTED SORT RETURNS IMMEDIATELY, so the filter is never attempted - the consequence of the
    /// <c>return</c> at <c>:L567</c> sitting inside the sort block.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_NeverAttemptsTheFilterOnceTheSortIsRejected_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new() { SetSortAnswer = DataWindowBufferStore.DataStoreFailure };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A",
            "age > 1",
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal(["SetSort:age A"], surface.Calls);
        Assert.DoesNotContain("SetFilter:age > 1", surface.Calls);
        Assert.Single(reported);
    }

    /// <summary>
    /// The legacy test is <c>&lt;&gt; 1</c>, so ANY value other than the DataStore success value is a
    /// failure - INCLUDING ZERO, which the framework's own return-code algebra treats as success. The two
    /// conventions coexist and each value is checked against its own.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(2L)]
    public void SynchronizeSortAndFilter_TreatsAnythingButOneAsFailure_UnitLevelNoOracle(long answer)
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new() { SetSortAnswer = answer };

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A",
            null,
            OpenGate,
            RecordingReporter([]));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
    }

    /// <summary>
    /// Null arguments fail fast.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_RejectsNullArguments_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new();

        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.SynchronizeSortAndFilter(
                null!, surface, null, null, OpenGate, RecordingReporter([])));
        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.SynchronizeSortAndFilter(
                store, null!, null, null, OpenGate, RecordingReporter([])));
        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.SynchronizeSortAndFilter(
                store, surface, null, null, OpenGate, null!));
    }

    #endregion

    #region DEFECT 4 - the guarded no-user-prompt write [:L673]

    /// <summary>
    /// EXACTLY ONE MODIFY CALL when the property does not already read <c>"yes"</c>, and the modify
    /// string is byte exact. The two PowerBuilder error markers are covered because neither equals
    /// <c>"yes"</c> and the legacy's <c>&lt;&gt;</c> therefore writes on both.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData("Yes")]
    [InlineData("YES")]
    [InlineData("yes ")]
    public void ApplyNoUserPromptWorkaround_WritesExactlyOnceWhenNotAlreadyYes_UnitLevelNoOracle(
        string describeAnswer)
    {
        RecordingCarrierSurface surface = new() { DescribeAnswer = describeAnswer };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.False(outcome.PropertyAlreadySet);
        Assert.True(outcome.ModifyAttempted);
        Assert.True(outcome.Succeeded);

        // THE READ COMES FIRST AND THE WRITE HAPPENS ONCE. Both are asserted from the call log, because
        // neither is visible in a return value.
        Assert.Equal(
            ["Describe:DataWindow.NoUserPrompt", "Modify:DataWindow.NoUserPrompt=yes"],
            surface.Calls);
        Assert.Equal("DataWindow.NoUserPrompt", FullStateCodec.NoUserPromptProperty);
        Assert.Equal("DataWindow.NoUserPrompt=yes", FullStateCodec.NoUserPromptModifyString);
    }

    /// <summary>
    /// NO WRITE AT ALL when the property already reads <c>"yes"</c>. THE GUARD IS PART OF THE BEHAVIOUR:
    /// an unconditional write would emit a modify call the legacy would not have made, and the modify
    /// call is observable.
    /// </summary>
    [Fact]
    public void ApplyNoUserPromptWorkaround_DoesNotWriteAtAllWhenAlreadyYes_UnitLevelNoOracle()
    {
        RecordingCarrierSurface surface = new() { DescribeAnswer = "yes" };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.True(outcome.PropertyAlreadySet);
        Assert.False(outcome.ModifyAttempted);
        Assert.True(outcome.Succeeded);
        Assert.Equal(string.Empty, outcome.ModifyError);

        // The read happened; the write did not.
        Assert.Equal(["Describe:DataWindow.NoUserPrompt"], surface.Calls);
        Assert.DoesNotContain("Modify:DataWindow.NoUserPrompt=yes", surface.Calls);
    }

    /// <summary>
    /// The comparison is ORDINAL, so a differently-cased or padded value is NOT accepted as already set -
    /// which matters because accepting one would SKIP a modify call the legacy makes.
    /// </summary>
    [Fact]
    public void ApplyNoUserPromptWorkaround_ComparesTheGuardOrdinally_UnitLevelNoOracle()
    {
        Assert.Equal("yes", FullStateCodec.NoUserPromptEnabledValue);

        RecordingCarrierSurface upper = new() { DescribeAnswer = "YES" };
        RecordingCarrierSurface exact = new() { DescribeAnswer = "yes" };

        Assert.True(FullStateCodec.ApplyNoUserPromptWorkaround(upper).ModifyAttempted);
        Assert.False(FullStateCodec.ApplyNoUserPromptWorkaround(exact).ModifyAttempted);
    }

    /// <summary>
    /// THE MODIFY RESULT IS AN ERROR STRING AND EMPTY MEANS SUCCESS [<c>:L665</c>]. It is never inverted
    /// into a boolean, because the text is the whole of the diagnostic.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("Property Not Found", false)]
    public void ApplyNoUserPromptWorkaround_PreservesEmptyMeansSuccess_UnitLevelNoOracle(
        string modifyAnswer,
        bool expectedSuccess)
    {
        RecordingCarrierSurface surface = new()
        {
            DescribeAnswer = "no",
            ModifyAnswer = modifyAnswer,
        };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.True(outcome.ModifyAttempted);
        Assert.Equal(modifyAnswer, outcome.ModifyError);
        Assert.Equal(expectedSuccess, outcome.Succeeded);
    }

    /// <summary>
    /// Null argument fails fast.
    /// </summary>
    [Fact]
    public void ApplyNoUserPromptWorkaround_RejectsANullSurface_UnitLevelNoOracle()
    {
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.ApplyNoUserPromptWorkaround(null!));
    }

    #endregion

    #region The receive path - three targets, one shot, no reset, no bookkeeping [:L184-L251]

    /// <summary>
    /// The three legacy receiver shapes: a DataWindow receiver, a DataStore receiver, and the task's own
    /// carrier when no receiver is installed.
    /// </summary>
    [Fact]
    public void ResolveTarget_SelectsTheThreeLegacyReceiverShapes_UnitLevelNoOracle()
    {
        DataWindowBufferStore receiver = new();
        DataWindowBufferStore taskCarrier = new();

        FullStateTarget dataWindow = FullStateCodec.ResolveTarget(receiver, true, taskCarrier);
        FullStateTarget dataStore = FullStateCodec.ResolveTarget(receiver, false, taskCarrier);
        FullStateTarget own = FullStateCodec.ResolveTarget(null, false, taskCarrier);

        Assert.Equal(FullStateTargetKind.DataWindowReceiver, dataWindow.Kind);
        Assert.Same(receiver, dataWindow.Store);

        Assert.Equal(FullStateTargetKind.DataStoreReceiver, dataStore.Kind);
        Assert.Same(receiver, dataStore.Store);

        Assert.Equal(FullStateTargetKind.TaskCarrier, own.Kind);
        Assert.Same(taskCarrier, own.Store);
    }

    /// <summary>
    /// The receiver-kind flag is only consulted when a receiver IS installed, matching the legacy's
    /// nesting: <c>_receiver.TypeOf()</c> is read inside the <c>IsValid(_receiver)</c> arm.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveTarget_IgnoresTheReceiverKindWhenNoReceiverIsInstalled_UnitLevelNoOracle(
        bool receiverIsDataWindow)
    {
        DataWindowBufferStore taskCarrier = new();

        Assert.Equal(
            FullStateTargetKind.TaskCarrier,
            FullStateCodec.ResolveTarget(null, receiverIsDataWindow, taskCarrier).Kind);
    }

    /// <summary>
    /// THE FULL-STATE OPERATION IS IDENTICAL IN ALL THREE ARMS [<c>:L190, L216, L233</c>], so the same
    /// payload lands identically whichever shape resolved.
    /// </summary>
    /// <remarks>
    /// THE THEORY DATA CARRIES THE SHAPE'S NAME RATHER THAN THE ENUM VALUE, and that is not a style
    /// choice: <c>FullStateTargetKind</c> is internal to the system under test, and an xunit theory
    /// method must be public, so a parameter of that type is error CS0051 - inconsistent accessibility -
    /// which <c>TreatWarningsAsErrors</c> would make fatal anyway. This is the same accommodation the
    /// sibling <c>DataWindowBuffersTests</c> makes for its own internal discriminator, and it is the
    /// mirror image of the constraint that forces every type in the system under test to be internal in
    /// the first place.
    /// </remarks>
    [Theory]
    [InlineData("DataWindowReceiver")]
    [InlineData("DataStoreReceiver")]
    [InlineData("TaskCarrier")]
    public void Receive_AppliesIdenticallyToAllThreeTargetShapes_UnitLevelNoOracle(string shapeName)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "one", ItemStatus.DataModified);
        SeedRow(source, DwBuffer.Filter, "two");

        CarrierState? image = FullStateCodec.Capture(source);

        DataWindowBufferStore store = new();
        DataWindowBufferStore taskCarrier = new();

        FullStateTarget target = ResolveShape(shapeName, store, taskCarrier);

        Assert.Equal(ExpectedKind(shapeName), target.Kind);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, FullStateCodec.Receive(target, image));
        Assert.Equal(1L, target.Store.RowCount());
        Assert.Equal(1L, target.Store.FilteredCount());
        Assert.Equal(
            ItemStatus.DataModified,
            target.Store.GetItemStatus(1L, 0, DwBuffer.Primary));
    }

    /// <summary>
    /// Maps a receiver-shape NAME onto the resolution call the legacy would have made for it.
    /// </summary>
    /// <param name="shapeName">The shape's name, as the theory data carries it.</param>
    /// <param name="receiver">The receiver to install for the two receiver shapes.</param>
    /// <param name="taskCarrier">The task's own carrier.</param>
    /// <returns>The resolved target.</returns>
    private static FullStateTarget ResolveShape(
        string shapeName,
        DataWindowBufferStore receiver,
        DataWindowBufferStore taskCarrier)
    {
        return shapeName switch
        {
            "DataWindowReceiver" => FullStateCodec.ResolveTarget(receiver, true, taskCarrier),
            "DataStoreReceiver" => FullStateCodec.ResolveTarget(receiver, false, taskCarrier),
            "TaskCarrier" => FullStateCodec.ResolveTarget(null, false, taskCarrier),
            _ => throw new ArgumentOutOfRangeException(
                nameof(shapeName),
                shapeName,
                "The legacy dispatch has exactly three arms and this suite names all three."),
        };
    }

    /// <summary>
    /// The kind a shape name must resolve to.
    /// </summary>
    /// <param name="shapeName">The shape's name.</param>
    /// <returns>The expected kind.</returns>
    private static FullStateTargetKind ExpectedKind(string shapeName)
    {
        return shapeName switch
        {
            "DataWindowReceiver" => FullStateTargetKind.DataWindowReceiver,
            "DataStoreReceiver" => FullStateTargetKind.DataStoreReceiver,
            "TaskCarrier" => FullStateTargetKind.TaskCarrier,
            _ => throw new ArgumentOutOfRangeException(
                nameof(shapeName),
                shapeName,
                "The legacy dispatch has exactly three arms and this suite names all three."),
        };
    }

    /// <summary>
    /// NO PRECEDING RESET STEP AND NO UPDATE-RESET. The image REPLACES the target's contents - which is
    /// intrinsic to restoring a complete image and is exactly why the legacy needs no separate
    /// <c>Reset</c> here - and because no update-reset runs, THE STATUSES CARRIED IN THE IMAGE SURVIVE
    /// VERBATIM instead of being re-baselined to unmodified the way a changeset's last chunk re-baselines
    /// its rows.
    /// </summary>
    [Fact]
    public void Receive_ReplacesTheTargetAndLeavesTheImageStatusesIntact_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "fresh", ItemStatus.NewModified);

        CarrierState? image = FullStateCodec.Capture(source);

        // A target that ALREADY holds rows. Applying must replace them, not append to them.
        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "stale");
        SeedRow(target, DwBuffer.Primary, "stale");
        SeedRow(target, DwBuffer.Delete, "stale");

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Receive(
                FullStateCodec.ResolveTarget(null, false, target),
                image));

        Assert.Equal(1L, target.RowCount());
        Assert.Equal(0L, target.DeletedCount());

        // NOT re-baselined. An update-reset would have made this NotModified.
        Assert.Equal(ItemStatus.NewModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal("fresh", target.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// THE EMPTY-PAYLOAD CLEAR ARM [<c>:L207-L209</c>]: the result is set to one FIRST and the target is
    /// then reset, so the answer does not depend on what the reset returns. This arm is SHARED with the
    /// changeset codec - the legacy reaches it before testing the full-state flag at all.
    /// </summary>
    [Fact]
    public void Receive_ClearsTheTargetAndAnswersSuccessOnAnEmptyPayload_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "gone");
        SeedRow(target, DwBuffer.Filter, "gone");
        SeedRow(target, DwBuffer.Delete, "gone");

        long result = FullStateCodec.Receive(
            FullStateCodec.ResolveTarget(null, false, target),
            state: null);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, result);
        Assert.Equal(0L, target.RowCount());
        Assert.Equal(0L, target.FilteredCount());
        Assert.Equal(0L, target.DeletedCount());
    }

    /// <summary>
    /// THE NORMALIZATION IS SIGN-OPPOSITE TO THE CHANGESET ARM'S AND IS THE EASIEST THING HERE TO GET
    /// BACKWARDS: on the full-state path a code above one becomes SUCCESS [<c>:L248</c>], while on the
    /// changeset path the identical code becomes FAILURE [<c>:L250</c>]. A negative code is left alone by
    /// both, so a failure is never converted into a success.
    /// </summary>
    [Theory]
    [InlineData(2L, 1L)]
    [InlineData(7L, 1L)]
    [InlineData(long.MaxValue, 1L)]
    [InlineData(1L, 1L)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, -1L)]
    [InlineData(long.MinValue, long.MinValue)]
    public void NormalizeResult_MapsAnyCodeAboveOneToSuccessAndLeavesTheRestAlone_UnitLevelNoOracle(
        long raw,
        long expected)
    {
        Assert.Equal(expected, FullStateCodec.NormalizeResult(raw));
    }

    /// <summary>
    /// AN IMAGE BELONGING TO THE OTHER CODEC IS REJECTED rather than restored. This is the one way the two
    /// codecs can be crossed: a CHANGESET image - one whose processing kind selects the changeset arm -
    /// delivered with the full-state flag set. It is now a SEMANTIC rejection rather than a framing one,
    /// because both codecs publish the same message type and only the processing kind separates them.
    /// </summary>
    /// <param name="changesetKind">A processing kind that selects the changeset arm.</param>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void Receive_RejectsAnImageBelongingToTheChangesetArm_UnitLevelNoOracle(long changesetKind)
    {
        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "untouched");

        // Captured from a carrier of the OTHER kind, so the image is well formed and merely belongs
        // elsewhere - which is precisely the crossing worth stating.
        DataWindowBufferStore foreign = new() { Processing = new DataWindowProcessing(changesetKind) };

        SeedRow(foreign, DwBuffer.Primary, "changeset row");

        long result = FullStateCodec.Receive(
            FullStateCodec.ResolveTarget(null, false, target),
            FullStateCodec.Capture(foreign));

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    /// <summary>
    /// Null arguments still throw, because a null reference is a programming error rather than a rejected
    /// payload - with ONE deliberate exception, which is the image itself.
    /// </summary>
    /// <remarks>
    /// An ABSENT image is not a null-argument fault: it is the legacy's zero-length blob, which
    /// <see cref="FullStateCodec.Receive"/> answers on its own clear arm [<c>:L207-L209</c>] and
    /// <see cref="FullStateCodec.Apply"/> answers with the failure code. Both are asserted elsewhere in
    /// this suite; what is asserted here is that the TARGET and the CARRIER still fail fast.
    /// </remarks>
    [Fact]
    public void ReceiveAndApplyRejectNullArguments_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = new();

        Assert.Throws<ArgumentNullException>(() => FullStateCodec.Apply(null!, SomeImage()));
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.Apply(null!, state: null));
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.ResolveTarget(target, true, null!));
    }

    /// <summary>
    /// A default-constructed target carries no store, which the legacy dispatch has no arm for, so it is
    /// rejected rather than dereferenced.
    /// </summary>
    [Fact]
    public void Receive_RejectsATargetWithNoStore_UnitLevelNoOracle()
    {
        Assert.Throws<ArgumentException>(() => FullStateCodec.Receive(default, SomeImage()));
    }

    #endregion

    #region Capture and Apply - the deterministic round trip

    /// <summary>
    /// THE ROUND TRIP REPRODUCES ROW COUNT, FILTERED COUNT, DELETED COUNT AND EVERY STATUS.
    /// </summary>
    /// <remarks>
    /// THE SHAPE IS SYNTHETIC BECAUSE THE REPOSITORY CONTAINS NO CROSSTAB FIXTURE. All twelve DataWindow
    /// definitions declare <c>processing=1</c> or <c>processing=0</c>, so there is nothing to load and no
    /// recording to compare against; the carrier below is assembled by hand and its processing kind is
    /// set directly.
    /// </remarks>
    [Fact]
    public void CaptureAndApplyRoundTripReproducesCountsAndStatuses_SyntheticCrosstabNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier(5L);

        SeedRow(source, DwBuffer.Primary, "p1", ItemStatus.NotModified);
        SeedRow(source, DwBuffer.Primary, "p2", ItemStatus.DataModified);
        SeedRow(source, DwBuffer.Primary, "p3", ItemStatus.NewModified);
        SeedRow(source, DwBuffer.Filter, "f1", ItemStatus.New);
        SeedRow(source, DwBuffer.Filter, "f2", ItemStatus.DataModified);
        SeedRow(source, DwBuffer.Delete, "d1", ItemStatus.DataModified);

        CarrierState? image = FullStateCodec.Capture(source);

        DataWindowBufferStore restored = new();

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, FullStateCodec.Apply(restored, image));

        Assert.Equal(3L, restored.RowCount());
        Assert.Equal(2L, restored.FilteredCount());
        Assert.Equal(1L, restored.DeletedCount());

        Assert.Equal(ItemStatus.NotModified, restored.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, restored.GetItemStatus(2L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NewModified, restored.GetItemStatus(3L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.New, restored.GetItemStatus(1L, 0, DwBuffer.Filter));
        Assert.Equal(ItemStatus.DataModified, restored.GetItemStatus(2L, 0, DwBuffer.Filter));
        Assert.Equal(ItemStatus.DataModified, restored.GetItemStatus(1L, 0, DwBuffer.Delete));

        // Row ORDER within each buffer survives. Filter!'s order is inverted relative to the source in
        // the legacy, so it is preserved AS THE CARRIER REPORTS IT and never reversed (R9).
        Assert.Equal("p1", restored.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal("p3", restored.GetItemValue(3L, 1, DwBuffer.Primary));
        Assert.Equal("f1", restored.GetItemValue(1L, 1, DwBuffer.Filter));
        Assert.Equal("f2", restored.GetItemValue(2L, 1, DwBuffer.Filter));
    }

    /// <summary>
    /// THE ORIGINAL VALUES SURVIVE, AND THAT IS NOT DECORATION: <c>updatewhere=1</c> puts the ORIGINAL
    /// value of every marked column into the generated where clause
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so an image carrying only current
    /// values could not express optimistic concurrency at all.
    /// </summary>
    /// <remarks>Synthetic crosstab shape; see the round-trip test above for why.</remarks>
    [Fact]
    public void CaptureAndApplyRoundTripPreservesOriginalValues_SyntheticCrosstabNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();

        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, "before");
        source.RowAt(row, DwBuffer.Primary).Baseline();
        source.SetItemValue(row, 1, DwBuffer.Primary, "after");
        source.SetItemStatus(row, 1, DwBuffer.Primary, ItemStatus.DataModified);
        source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        // The carrier is in the state the concurrency check depends on before capture.
        Assert.Equal("after", source.GetItemValue(row, 1, DwBuffer.Primary));
        Assert.Equal("before", source.GetItemOriginalValue(row, 1, DwBuffer.Primary));

        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, FullStateCodec.Capture(source)));

        Assert.Equal("after", restored.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal("before", restored.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, restored.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, restored.GetItemStatus(1L, 0, DwBuffer.Primary));
    }

    /// <summary>
    /// An UNMODIFIED column round-trips with its original equal to its current value, which is what the
    /// carrier means by an empty capture set.
    /// </summary>
    [Fact]
    public void CaptureAndApplyRoundTripLeavesUnmodifiedColumnsWithOriginalEqualToCurrent_SyntheticCrosstabNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, "steady");
        source.RowAt(row, DwBuffer.Primary).Baseline();

        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, FullStateCodec.Capture(source)));

        Assert.Equal("steady", restored.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal("steady", restored.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// EVERY PUBLISHED VALUE ARM ROUND-TRIPS ONTO THE CARRIER TYPE THE CONTRACT DECLARES FOR IT, null
    /// included. Null is A VALUE here and not an absence - PowerBuilder has null for value types and the
    /// ported tri-state algebra depends on it, so it must survive distinctly from zero and from the empty
    /// string.
    /// </summary>
    /// <param name="value">The value written into the source carrier.</param>
    /// <param name="expected">The value - and runtime type - expected back out.</param>
    [Theory]
    [MemberData(nameof(EveryPublishedValueArm))]
    public void CaptureAndApplyRoundTripPreservesEveryPublishedValueArm_SyntheticCrosstabNoOracle(
        object? value,
        object? expected)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, value);

        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, FullStateCodec.Capture(source)));

        object? actual = restored.GetItemValue(1L, 1, DwBuffer.Primary);

        if (expected is byte[] expectedBlob)
        {
            Assert.Equal(expectedBlob, Assert.IsType<byte[]>(actual));

            return;
        }

        Assert.Equal(expected, actual);

        if (expected is not null)
        {
            Assert.IsType(expected.GetType(), actual);
        }
    }

    /// <summary>
    /// One value per <c>common.v1.AnyValue</c> arm, paired with the carrier type it comes back as, plus
    /// the cases that a careless encoding would lose: a trailing-zero decimal whose SCALE must survive,
    /// all three <c>DateTimeKind</c> values, and an empty blob.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PAIRING IS THE POINT, because <c>AnyValue</c>'s numeric arms are DELIBERATELY WIDE - one
    /// signed integer arm, one unsigned, one floating - and that widening is the contract's own decision
    /// rather than this codec's. An <see cref="int"/> travels as the signed arm and returns as
    /// <see cref="long"/>; a <see cref="uint"/> returns as <see cref="ulong"/>; a <see cref="float"/>
    /// returns as <see cref="double"/>. Nothing loses magnitude, sign, precision or scale - only the
    /// declared width changes, and stating it here is what keeps it visible rather than leaving a consumer
    /// to discover it by casting and throwing.
    /// </para>
    /// <para>
    /// The three <see cref="DateTime"/> kinds all return <see cref="DateTimeKind.Unspecified"/>, because
    /// the contract's datetime form is UNZONED - PowerBuilder's <c>datetime</c> carries no zone either, so
    /// preserving a kind would invent information the oracle does not have.
    /// </para>
    /// </remarks>
    public static TheoryData<object?, object?> EveryPublishedValueArm =>
        new()
        {
            // CAST DELIBERATELY. A bare `null` binds to TheoryData's row-typed Add overload rather than
            // its value-typed one, because TheoryDataRow<object?> is more derived than object, and that
            // overload's parameter is non-nullable - error CS8625. The cast makes the value-typed
            // overload the only applicable one, which is what carries null through as A VALUE.
            { (object?)null, (object?)null },
            { true, true },
            { false, false },

            // The signed integer family, widened onto the one signed arm.
            { 42, 42L },
            { int.MinValue, (long)int.MinValue },
            { 9_000_000_000L, 9_000_000_000L },
            { long.MaxValue, long.MaxValue },

            // The unsigned family, widened onto the one unsigned arm.
            { ulong.MaxValue, ulong.MaxValue },

            // The floating family. Both values below are exactly representable, so the equality is exact.
            { 1.5d, 1.5d },
            { double.NegativeInfinity, double.NegativeInfinity },

            // Decimals keep their own arm so that scale survives - see the scale test below.
            { 1.50m, 1.50m },
            { -0.000_001m, -0.000_001m },
            { decimal.MaxValue, decimal.MaxValue },

            { string.Empty, string.Empty },
            { "salary A age D", "salary A age D" },
            { "多线程", "多线程" },
            { new byte[] { 1, 2, 3, 250 }, new byte[] { 1, 2, 3, 250 } },
            { Array.Empty<byte>(), Array.Empty<byte>() },

            // The unzoned datetime form: three kinds in, Unspecified out, value identical.
            {
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Utc),
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified)
            },
            {
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Local),
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified)
            },
            {
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified),
                new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified)
            },

            { new DateOnly(1978, 6, 30), new DateOnly(1978, 6, 30) },

            // Millisecond resolution is inside the canonical six-digit fraction and survives exactly.
            { new TimeOnly(23, 59, 59, 999), new TimeOnly(23, 59, 59, 999) },
        };

    /// <summary>
    /// DECIMAL SCALE SURVIVES. The fixture's salary column is declared <c>decimal(2)</c> against a REAL
    /// database column, a mismatch the migration preserves as a defect, so a round trip that renormalised
    /// <c>1.50</c> to <c>1.5</c> would erase exactly that evidence.
    /// </summary>
    [Fact]
    public void CaptureAndApplyRoundTripPreservesDecimalScale_SyntheticCrosstabNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, 1.50m);

        DataWindowBufferStore restored = new();

        FullStateCodec.Apply(restored, FullStateCodec.Capture(source));

        decimal actual = Assert.IsType<decimal>(restored.GetItemValue(1L, 1, DwBuffer.Primary));

        Assert.Equal("1.50", actual.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The processing kind travels in the image and is restored, so a receiver that had none ends up
    /// declaring the source's.
    /// </summary>
    [Theory]
    [MemberData(nameof(FullStateProcessingKinds))]
    public void CaptureAndApplyRoundTripRestoresTheProcessingKind_SyntheticCrosstabNoOracle(long kind)
    {
        DataWindowBufferStore source = NewCrosstabCarrier(kind);
        DataWindowBufferStore restored = new();

        Assert.False(restored.RequiresFullStateTransfer);

        FullStateCodec.Apply(restored, FullStateCodec.Capture(source));

        Assert.Equal(kind, restored.Processing.Value);
        Assert.True(restored.RequiresFullStateTransfer);
    }

    /// <summary>
    /// THE ENCODING IS DETERMINISTIC. Two carriers in the same state produce byte-identical images, which
    /// is what the parity model's byte-for-byte comparison requires; a dictionary's unspecified key order
    /// would break it silently.
    /// </summary>
    [Fact]
    public void Capture_IsDeterministicForTwoCarriersInTheSameState_UnitLevelNoOracle()
    {
        static DataWindowBufferStore Build()
        {
            DataWindowBufferStore store = NewCrosstabCarrier();
            long row = store.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

            // Columns assigned OUT OF ORDER on purpose: the image must still order them ascending.
            store.SetItemValue(row, 6, DwBuffer.Primary, "sixth");
            store.SetItemValue(row, 1, DwBuffer.Primary, "first");
            store.SetItemValue(row, 4, DwBuffer.Primary, "fourth");

            return store;
        }

        CarrierState? first = FullStateCodec.Capture(Build());
        CarrierState? second = FullStateCodec.Capture(Build());

        Assert.Equal(first, second);

        // The BYTES too, because a recording holds bytes rather than a message instance.
        Assert.Equal(first!.ToByteArray(), second!.ToByteArray());
    }

    /// <summary>
    /// A CAPTURED IMAGE IS NEVER ABSENT AND NEVER SEGMENT-LESS, even for an empty carrier, because an
    /// ABSENT image is the legacy's "clear the target" signal [<c>:L207-L209</c>]. Confusing the two
    /// would silently turn a legitimately empty result into a clear.
    /// </summary>
    [Fact]
    public void Capture_NeverProducesAnAbsentImage_UnitLevelNoOracle()
    {
        CarrierState? empty = FullStateCodec.Capture(NewCrosstabCarrier());

        Assert.NotNull(empty);
        Assert.Equal(
            [DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter],
            empty.Segments.Select(segment => segment.Buffer));
        Assert.All(empty.Segments, segment => Assert.Empty(segment.Rows));

        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "kept");

        // Applying the image of an EMPTY carrier clears the target through the apply path rather than
        // through the empty-payload arm, and answers success either way.
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Receive(
                FullStateCodec.ResolveTarget(null, false, target),
                FullStateCodec.Capture(NewCrosstabCarrier())));
        Assert.Equal(0L, target.RowCount());
    }

    /// <summary>
    /// AN UNREPRESENTABLE VALUE IS A DEFINED FAILURE RATHER THAN A GUESS. Coercing it to its string form
    /// would round-trip as the wrong type and read as correct, so the contract is narrowed instead of
    /// widened.
    /// </summary>
    /// <remarks>
    /// ANSWERED AS AN ABSENT IMAGE RATHER THAN AS AN EXCEPTION, which is a deliberate change of failure
    /// channel and not a relaxation. The operation this reproduces - <c>GetFullState</c> - answers a CODE,
    /// and every legacy call site tests one [<c>:L95-L99</c>]; an escaping exception would be a failure
    /// mode the oracle does not have (C-B) and would surface as a 500 rather than as the transfer failure
    /// the contract publishes. <see cref="FullStateCodec.Send"/> converts the absence into the verbatim
    /// failure text and the internal-error code, which the sibling send tests assert.
    /// </remarks>
    /// <param name="unrepresentable">A value outside the published <c>AnyValue</c> arms.</param>
    [Theory]
    [MemberData(nameof(UnrepresentableValues))]
    public void Capture_AnswersAnAbsentImageForAnUnrepresentableValue_UnitLevelNoOracle(
        object unrepresentable)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, unrepresentable);

        Assert.Null(FullStateCodec.Capture(source));
    }

    /// <summary>
    /// The values the published <c>AnyValue</c> arms cannot express, one per reason.
    /// </summary>
    public static TheoryData<object> UnrepresentableValues =>
        new()
        {
            // No arm for an arbitrary reference type.
            new Uri("https://example.invalid/"),

            // TimeValue is a TIME OF DAY rather than a duration, so a TimeSpan has no arm either -
            // mapping it onto one would read back as a TimeOnly meaning something different.
            TimeSpan.FromMinutes(90L),

            // Finer than the canonical six-digit fraction, so it cannot be rendered without dropping
            // information, and truncating would make a recording comparison pass on a value nobody wrote.
            new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified).AddTicks(1L),
            new TimeOnly(23, 59, 59).Add(TimeSpan.FromTicks(1L)),
        };

    /// <summary>
    /// <see cref="FullStateCodec.Capture"/> is deliberately KIND-AGNOSTIC, because the legacy checks the
    /// processing kind at the DISPATCH site and its own <c>GetFullState</c> call is unconditional. The
    /// guard therefore lives on <see cref="FullStateCodec.Send"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void Capture_DoesNotItselfGuardTheProcessingKind_UnitLevelNoOracle(long kind)
    {
        CarrierState? image = FullStateCodec.Capture(NewCrosstabCarrier(kind));

        Assert.NotNull(image);
        Assert.Equal(kind, image.Processing);
    }

    /// <summary>
    /// Null argument fails fast.
    /// </summary>
    [Fact]
    public void Capture_RejectsANullSource_UnitLevelNoOracle()
    {
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.Capture(null!));
    }

    #endregion

    #region Malformed-image rejection - every defensive decode path

    // ==========================================================================================
    //  WHAT THIS REGION USED TO ASSERT, AND WHY IT NO LONGER DOES.
    //  ----------------------------------------------------------------------------------------
    //  It was written against a private binary image owned by this assembly, so it worked from
    //  DOCUMENTED BYTE OFFSETS: a format version byte, a buffer tag byte, little-endian row and column
    //  counts, an original-value presence flag, a value tag byte, a seven-bit string length, a
    //  DateTimeKind byte, and three hand-built maximal counts that proved a length was bounded before
    //  it reached `new byte[length]`. Every one of those tested a FRAMING LAYER that no longer exists.
    //
    //  The image is now `persistence.v1.CarrierState`, a published message (constraint C-A), because
    //  the review found what the private format cost: `QueryDataChunk.data` was an opaque `bytes` field
    //  described as the codecs' own concern, and DataServices - which references the contracts project
    //  and nothing else - could therefore neither produce nor consume the payload the C-05 and C-06
    //  contracts hand it. Framing, truncation, length bounding and UTF-8 validity are now the generated
    //  parser's concern, and a malformed frame never reaches this codec at all.
    //
    //  WHAT REPLACED THEM IS NOT A REDUCTION. A well-formed protobuf message can still be a nonsense
    //  image, and every semantic check the byte-level tests were mixed in with is still asserted here,
    //  several of them for the first time: a segment roster that is not exactly one per buffer in
    //  canonical order, a row filed under the wrong buffer, a non-positive row ordinal, an undeclared
    //  item status, a column identifier that is not one-based, a duplicated column, an original naming
    //  a column the row never carried, a value message with no arm set, and decimal or temporal text
    //  outside the canonical grammar.
    //
    //  ONE ASYMMETRY WITH THE SIBLING CHANGESET SUITE IS DELIBERATE AND IS NOT A GAP. This codec RESETS
    //  the target as part of restoring a complete image, so a fault found after the reset leaves the
    //  target empty rather than holding its previous contents. That is the legacy's own exposure -
    //  `SetFullState` replaces the carrier's contents and answers -1 on a payload it rejects
    //  [n_cst_threading_task_sqlquery.sru:L190] - so it is reproduced rather than corrected. Every
    //  STRUCTURALLY detectable fault is nevertheless hoisted ABOVE the reset, which is what the
    //  roster and processing tests below assert by checking the target still holds its rows.
    // ==========================================================================================

    /// <summary>
    /// A well-formed image of a carrier holding one Primary! row with one assigned column, for the tests
    /// whose subject is an argument check rather than the image.
    /// </summary>
    private static CarrierState SomeImage()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, "value");
        source.SetItemStatus(row, 1, DwBuffer.Primary, ItemStatus.DataModified);
        source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        CarrierState? image = FullStateCodec.Capture(source);

        Assert.NotNull(image);

        return image;
    }

    /// <summary>Builds an image from an explicit segment roster, canonical or not.</summary>
    /// <param name="segments">The segments, in the order they are to appear.</param>
    private static CarrierState ImageWith(params CarrierBufferSegment[] segments) =>
        ImageWith(FullStateProcessingKind, segments);

    /// <summary>Builds an image from an explicit segment roster and processing kind.</summary>
    /// <param name="processing">The processing kind to declare.</param>
    /// <param name="segments">The segments, in the order they are to appear.</param>
    private static CarrierState ImageWith(long processing, params CarrierBufferSegment[] segments)
    {
        CarrierState image = new() { Processing = processing };

        image.Segments.AddRange(segments);

        return image;
    }

    /// <summary>
    /// Builds an image with the canonical three segments, filing each row into the segment its own buffer
    /// tag names, so that only the fault under test is faulty.
    /// </summary>
    /// <param name="rows">The rows to file.</param>
    private static CarrierState CanonicalImageWith(params DataWindowRow[] rows)
    {
        return ImageWith(
            FullStateProcessingKind,
            [
                .. new[] { DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter }.Select(dwBuffer =>
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

    /// <summary>
    /// The crosstab processing kind this codec owns, so that no bare literal appears in the builders.
    /// </summary>
    private const long FullStateProcessingKind = 4L;

    /// <summary>
    /// Segment rosters that are not the canonical three, each of which must be refused.
    /// </summary>
    public static TheoryData<string, CarrierState> NonCanonicalRosters =>
        AsTheoryData(NonCanonicalRosterCases);

    /// <summary>The roster faults themselves, enumerable independently of the theory wrapper.</summary>
    private static IEnumerable<(string Because, CarrierState Image)> NonCanonicalRosterCases()
    {
        return
        [
            ("no segments at all", ImageWith()),
            ("one segment", ImageWith(Segment(DwBuffer.Primary))),
            ("two segments", ImageWith(Segment(DwBuffer.Primary), Segment(DwBuffer.Delete))),
            (
                "a fourth segment",
                ImageWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Primary))
            ),
            (
                "three segments all tagged Primary",
                ImageWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Primary))
            ),
            (
                "the canonical three in reverse order",
                ImageWith(
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Primary))
            ),
            (
                "Delete and Primary transposed",
                ImageWith(
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Filter))
            ),
            (
                "Delete omitted and Filter duplicated",
                ImageWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Filter),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a buffer ordinal outside the published domain",
                ImageWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete),
                    Segment((DwBuffer)99))
            ),
        ];
    }

    /// <summary>
    /// A NON-CANONICAL ROSTER IS REFUSED BEFORE THE RESET, so the target still holds what it held.
    /// </summary>
    /// <param name="because">The roster fault, named so a failure message identifies the case.</param>
    /// <param name="image">The non-canonical image.</param>
    [Theory]
    [MemberData(nameof(NonCanonicalRosters))]
    public void Apply_RefusesANonCanonicalRosterWithoutResettingTheTarget_UnitLevelNoOracle(
        string because,
        CarrierState image)
    {
        DataWindowBufferStore target = NewCrosstabCarrier();

        SeedRow(target, DwBuffer.Primary, "untouched");

        long result = FullStateCodec.Apply(target, image);

        Assert.True(
            result == DataWindowBufferStore.DataStoreFailure,
            $"An image with {because} answered {result} rather than the failure code.");

        // THE HOISTED-CHECK PROPERTY: a structurally detectable fault is found BEFORE the reset, so the
        // target's existing contents survive the refusal.
        Assert.Equal(1L, target.RowCount());
        Assert.Equal("untouched", target.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// Row-level and column-level faults inside an otherwise canonical roster.
    /// </summary>
    public static TheoryData<string, CarrierState> MalformedRowContent =>
        AsTheoryData(MalformedRowContentCases);

    /// <summary>The content faults themselves, enumerable independently of the theory wrapper.</summary>
    private static IEnumerable<(string Because, CarrierState Image)> MalformedRowContentCases()
    {
        return
        [
            (
                "a Filter row filed inside the Primary segment",
                ImageWith(
                    Segment(DwBuffer.Primary, Row(DwBuffer.Filter, 1L, ItemStatus.DataModified)),
                    Segment(DwBuffer.Delete),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a Primary row filed inside the Delete segment",
                ImageWith(
                    Segment(DwBuffer.Primary),
                    Segment(DwBuffer.Delete, Row(DwBuffer.Primary, 1L, ItemStatus.DataModified)),
                    Segment(DwBuffer.Filter))
            ),
            (
                "a zero row ordinal",
                CanonicalImageWith(Row(DwBuffer.Primary, 0L, ItemStatus.DataModified))
            ),
            (
                "a negative row ordinal",
                CanonicalImageWith(Row(DwBuffer.Primary, -1L, ItemStatus.DataModified))
            ),
            (
                "an item status outside the published domain",
                CanonicalImageWith(Row(DwBuffer.Primary, 1L, (ItemStatus)99))
            ),
            (
                "column number zero, which is the row-status sentinel",
                CanonicalImageWith(
                    Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(0L, 42)]))
            ),
            (
                "a negative column number",
                CanonicalImageWith(
                    Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(-3L, 42)]))
            ),
            (
                "the same column number twice",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42), Column(1L, 43)]))
            ),
            (
                "an original naming a column the row never carried",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42)],
                        [Column(2L, 41)]))
            ),
            (
                "the same column number twice among the originals",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42)],
                        [Column(1L, 41), Column(1L, 40)]))
            ),
            (
                "a per-column item status outside the published domain",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [Column(1L, 42, (ItemStatus)99)]))
            ),
            (
                "a value message with no arm set at all",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [new ColumnValue { ColumnId = 1L, Value = new AnyValue() }]))
            ),
            (
                "a column carrying no value message",
                CanonicalImageWith(
                    Row(
                        DwBuffer.Primary,
                        1L,
                        ItemStatus.DataModified,
                        [new ColumnValue { ColumnId = 1L }]))
            ),
            (
                "decimal text outside the canonical grammar",
                CanonicalImageWith(
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
                CanonicalImageWith(
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
                                    DateValue = new DateValue { Value = "30/06/1978" },
                                },
                            },
                        ]))
            ),
            (
                "time text outside the canonical grammar",
                CanonicalImageWith(
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
                                    TimeValue = new TimeValue { Value = "11:59 PM" },
                                },
                            },
                        ]))
            ),
            (
                "datetime text missing its separator",
                CanonicalImageWith(
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
                                    DatetimeValue = new DateTimeValue
                                    {
                                        Value = "2022-04-14 13:45:30",
                                    },
                                },
                            },
                        ]))
            ),
        ];
    }

    /// <summary>
    /// MALFORMED ROW CONTENT ANSWERS THE FAILURE CODE RATHER THAN THROWING.
    /// </summary>
    /// <param name="because">The content fault, named so a failure message identifies the case.</param>
    /// <param name="image">The malformed image.</param>
    /// <remarks>
    /// The target is NOT asserted to be untouched here, and that omission is deliberate: a row-level
    /// fault is discovered after the reset this codec performs as part of restoring a complete image, and
    /// that exposure is the legacy's own [<c>:L190</c>] rather than one added here. The sibling roster
    /// theory asserts the hoisted half.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MalformedRowContent))]
    public void Apply_RefusesMalformedRowContentWithoutThrowing_UnitLevelNoOracle(
        string because,
        CarrierState image)
    {
        long result = FullStateCodec.Apply(new DataWindowBufferStore(), image);

        Assert.True(
            result == DataWindowBufferStore.DataStoreFailure,
            $"An image with {because} answered {result} rather than the failure code.");
    }

    /// <summary>
    /// AN ABSENT IMAGE IS THE FAILURE CODE ON THIS PATH, not the clear arm. The clear arm belongs to
    /// <see cref="FullStateCodec.Receive"/>, which tests for it before consulting the decode at all
    /// [<c>:L207-L209</c>]; reaching the decode with nothing means a producer sent a chunk with neither an
    /// image nor the absence that means "clear".
    /// </summary>
    [Fact]
    public void Apply_RefusesAnAbsentImage_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = NewCrosstabCarrier();

        SeedRow(target, DwBuffer.Primary, "untouched");

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            FullStateCodec.Apply(target, state: null));

        Assert.Equal(1L, target.RowCount());
    }

    /// <summary>
    /// AN IMAGE WHOSE PROCESSING KIND DOES NOT SELECT THIS CODEC IS REFUSED BEFORE THE RESET. The two
    /// sides would otherwise disagree about which serialization is even applicable, and merging a
    /// changeset image into a crosstab carrier is what trusting the sender costs.
    /// </summary>
    /// <param name="changesetKind">A processing kind belonging to the changeset arm.</param>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void Apply_RefusesAnImageBelongingToTheChangesetArm_UnitLevelNoOracle(long changesetKind)
    {
        DataWindowBufferStore target = NewCrosstabCarrier();

        SeedRow(target, DwBuffer.Primary, "untouched");

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            FullStateCodec.Apply(target, CanonicalImageWith(changesetKind)));

        Assert.Equal(1L, target.RowCount());
    }

    /// <summary>
    /// A FULL-STATE IMAGE WHOSE KIND DISAGREES WITH THE TARGET'S IS REFUSED, even though both kinds select
    /// this codec: crosstab and composite are different shapes and one is not restorable into the other.
    /// </summary>
    [Fact]
    public void Apply_RefusesAFullStateImageWhoseKindDisagreesWithTheTarget_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = NewCrosstabCarrier(4L);

        SeedRow(target, DwBuffer.Primary, "untouched");

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            FullStateCodec.Apply(target, CanonicalImageWith(5L)));

        Assert.Equal(1L, target.RowCount());
        Assert.Equal(4L, target.Processing.Value);
    }

    /// <summary>
    /// AN UNASSIGNED TARGET ADOPTS THE IMAGE'S KIND, because that is not a disagreement. This is the arm
    /// every round-trip test in this suite depends on, since a freshly constructed carrier has no kind.
    /// </summary>
    [Theory]
    [MemberData(nameof(FullStateProcessingKinds))]
    public void Apply_LetsAnUnassignedTargetAdoptTheImagesKind_UnitLevelNoOracle(long kind)
    {
        DataWindowBufferStore target = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(
                target,
                CanonicalImageWith(
                    kind,
                    Row(DwBuffer.Primary, 1L, ItemStatus.DataModified, [Column(1L, "adopted")]))));

        Assert.Equal(kind, target.Processing.Value);
        Assert.Equal("adopted", target.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// Builds a canonical image with an explicit processing kind and rows.
    /// </summary>
    /// <param name="processing">The processing kind to declare.</param>
    /// <param name="rows">The rows to file.</param>
    private static CarrierState CanonicalImageWith(long processing, params DataWindowRow[] rows)
    {
        return ImageWith(
            processing,
            [
                .. new[] { DwBuffer.Primary, DwBuffer.Delete, DwBuffer.Filter }.Select(dwBuffer =>
                    Segment(dwBuffer, [.. rows.Where(row => row.Buffer == dwBuffer)])),
            ]);
    }

    /// <summary>
    /// NO MALFORMED IMAGE EVER ESCAPES AS AN EXCEPTION. Driven over the union of both matrices plus the
    /// absent image, so a case added to either is covered here for free rather than needing a second
    /// entry.
    /// </summary>
    [Fact]
    public void Apply_NeverThrowsForAnyMalformedImage_UnitLevelNoOracle()
    {
        List<CarrierState?> candidates = [null];

        candidates.AddRange(NonCanonicalRosterCases().Select(entry => (CarrierState?)entry.Image));
        candidates.AddRange(MalformedRowContentCases().Select(entry => (CarrierState?)entry.Image));

        foreach (CarrierState? candidate in candidates)
        {
            long result = FullStateCodec.Apply(new DataWindowBufferStore(), candidate);

            Assert.True(
                result == DataWindowBufferStore.DataStoreSuccess
                    || result == DataWindowBufferStore.DataStoreFailure,
                $"A malformed image answered {result}, which is neither success nor failure.");
        }
    }

    /// <summary>
    /// Wraps a named-case producer as the theory data a <see cref="MemberDataAttribute"/> consumes.
    /// </summary>
    /// <param name="cases">The producer.</param>
    /// <remarks>
    /// The cases are authored as a plain sequence rather than directly as theory data so that a test which
    /// needs the IMAGES ALONE - the exception sweep above - can enumerate them without reaching through
    /// the theory wrapper's row shape. Every case therefore has exactly one definition site.
    /// </remarks>
    private static TheoryData<string, CarrierState> AsTheoryData(
        Func<IEnumerable<(string Because, CarrierState Image)>> cases)
    {
        TheoryData<string, CarrierState> data = [];

        foreach ((string because, CarrierState image) in cases())
        {
            data.Add(because, image);
        }

        return data;
    }

    #endregion

    #region End to end across the two seams

    /// <summary>
    /// The whole path in one case: synchronize, apply the workaround, send, then receive - in the order
    /// the legacy performs them.
    /// </summary>
    /// <remarks>
    /// Synthetic throughout; there is no fixture and no recording for any step of this sequence.
    /// </remarks>
    [Fact]
    public void SendAndReceiveCarryTheCarrierAcrossTheSeamInLegacyOrder_SyntheticCrosstabNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        RecordingCarrierSurface surface = new() { DescribeAnswer = "no" };
        List<(long Code, string Message)> reported = [];

        // 1. DEFECT 3 - synchronize, because the image will REPLACE the receiver's definition [:L563].
        Assert.Equal(
            RetCode.OK,
            FullStateCodec.SynchronizeSortAndFilter(
                source,
                surface,
                " age A salary A ",
                null,
                OpenGate,
                RecordingReporter(reported)));

        // 2. DEFECT 4 - the guarded write, BEFORE anything captures [:L674-L676].
        Assert.True(FullStateCodec.ApplyNoUserPromptWorkaround(surface).ModifyAttempted);

        SeedRow(source, DwBuffer.Primary, "carried", ItemStatus.DataModified);
        SeedRow(source, DwBuffer.Filter, "excluded");

        // 3. Capture, reset, hand over one chunk [:L95-L101].
        List<QueryDataChunk> captured = [];

        Assert.Equal(
            RetCode.OK,
            FullStateCodec.Send(source, RecordingHandover(captured, 1L), RecordingReporter(reported)));
        Assert.Empty(reported);

        // 4. Receive it on the far side [:L184-L248].
        DataWindowBufferStore receiver = new();
        QueryDataChunk chunk = Assert.Single(captured);

        Assert.True(chunk.FullState);
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Receive(
                FullStateCodec.ResolveTarget(receiver, true, new DataWindowBufferStore()),
                chunk.State));

        Assert.Equal(1L, receiver.RowCount());
        Assert.Equal(1L, receiver.FilteredCount());
        Assert.Equal("carried", receiver.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, receiver.GetItemStatus(1L, 0, DwBuffer.Primary));

        // The ordering the legacy performs, asserted from the surface's own call log.
        Assert.Equal(
            [
                "SetSort:age A salary A",
                "Describe:DataWindow.NoUserPrompt",
                "Modify:DataWindow.NoUserPrompt=yes",
            ],
            surface.Calls);
    }

    #endregion
}
