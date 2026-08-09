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
        Assert.NotEmpty(chunk.Data.ToByteArray());
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
            FullStateCodec.Apply(restored, captured[0].Data.ToByteArray()));
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

        byte[] image = FullStateCodec.Capture(source);

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

        byte[] image = FullStateCodec.Capture(source);

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
            []);

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
    /// A payload this codec did not write is REJECTED with the failure code rather than decoded as
    /// garbage. The realistic cause is the one way the two codecs can be crossed: a changeset blob
    /// delivered with the full-state flag set.
    /// </summary>
    [Fact]
    public void Receive_RejectsAPayloadThisCodecDidNotWrite_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "untouched");

        long result = FullStateCodec.Receive(
            FullStateCodec.ResolveTarget(null, false, target),
            [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    /// <summary>
    /// A TRUNCATED image is rejected the way <c>SetFullState</c> rejects a payload - by ANSWERING the
    /// failure code, not by throwing - because the legacy caller stores that value and carries on.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    [InlineData(13)]
    public void Receive_RejectsATruncatedImageWithoutThrowing_UnitLevelNoOracle(int keptBytes)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "a");

        byte[] image = FullStateCodec.Capture(source);
        byte[] truncated = image[..keptBytes];

        DataWindowBufferStore target = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            FullStateCodec.Receive(FullStateCodec.ResolveTarget(null, false, target), truncated));
    }

    /// <summary>
    /// Null arguments still throw, because a null reference is a programming error rather than a rejected
    /// payload.
    /// </summary>
    [Fact]
    public void ReceiveAndApplyRejectNullArguments_UnitLevelNoOracle()
    {
        DataWindowBufferStore target = new();

        Assert.Throws<ArgumentNullException>(
            () => FullStateCodec.Receive(FullStateCodec.ResolveTarget(null, false, target), null!));
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.Apply(target, null!));
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.Apply(null!, []));
        Assert.Throws<ArgumentNullException>(() => FullStateCodec.ResolveTarget(target, true, null!));
    }

    /// <summary>
    /// A default-constructed target carries no store, which the legacy dispatch has no arm for, so it is
    /// rejected rather than dereferenced.
    /// </summary>
    [Fact]
    public void Receive_RejectsATargetWithNoStore_UnitLevelNoOracle()
    {
        Assert.Throws<ArgumentException>(() => FullStateCodec.Receive(default, []));
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

        byte[] image = FullStateCodec.Capture(source);

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
    /// EVERY TAGGED VALUE TYPE ROUND-TRIPS WITH ITS RUNTIME TYPE INTACT, null included. Null is A VALUE
    /// here and not an absence - PowerBuilder has null for value types and the ported tri-state algebra
    /// depends on it, so it must survive distinctly from zero and from the empty string.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryTaggedValue))]
    public void CaptureAndApplyRoundTripPreservesEveryTaggedValueType_SyntheticCrosstabNoOracle(
        object? value)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, value);

        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, FullStateCodec.Capture(source)));

        object? actual = restored.GetItemValue(1L, 1, DwBuffer.Primary);

        if (value is byte[] expectedBlob)
        {
            Assert.Equal(expectedBlob, Assert.IsType<byte[]>(actual));

            return;
        }

        Assert.Equal(value, actual);

        if (value is not null)
        {
            Assert.IsType(value.GetType(), actual);
        }
    }

    /// <summary>
    /// One value per <c>FullStateValueTag</c> member, plus the cases that a careless encoding would lose:
    /// a trailing-zero decimal whose SCALE must survive, all three <c>DateTimeKind</c> values, and an
    /// empty blob.
    /// </summary>
    public static TheoryData<object?> EveryTaggedValue =>
    [
        // CAST DELIBERATELY. A bare `null` here binds to TheoryData's row-typed Add overload rather
        // than its value-typed one, because TheoryDataRow<object?> is more derived than object, and
        // that overload's parameter is non-nullable - error CS8625. The cast makes the value-typed
        // overload the only applicable one, which is what carries null through as A VALUE.
        (object?)null,
        true,
        false,
        42,
        int.MinValue,
        9_000_000_000L,
        long.MaxValue,
        ulong.MaxValue,
        1.5d,
        double.NegativeInfinity,
        1.50m,
        -0.000_001m,
        decimal.MaxValue,
        "",
        "salary A age D",
        "多线程",
        new byte[] { 1, 2, 3, 250 },
        Array.Empty<byte>(),
        new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Utc),
        new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Local),
        new DateTime(2022, 4, 14, 13, 45, 30, DateTimeKind.Unspecified),
        new DateOnly(1978, 6, 30),
        new TimeOnly(23, 59, 59, 999),
    ];

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

        Assert.Equal(FullStateCodec.Capture(Build()), FullStateCodec.Capture(Build()));
    }

    /// <summary>
    /// A CAPTURED IMAGE IS NEVER EMPTY, even for an empty carrier, because an EMPTY payload is the
    /// legacy's "clear the target" signal [<c>:L207-L209</c>]. Confusing the two would silently turn a
    /// legitimately empty result into a clear.
    /// </summary>
    [Fact]
    public void Capture_NeverProducesAnEmptyImage_UnitLevelNoOracle()
    {
        Assert.NotEmpty(FullStateCodec.Capture(NewCrosstabCarrier()));

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
    /// AN UNTAGGED VALUE TYPE IS A DEFINED ERROR RATHER THAN A GUESS. Coercing it to its string form would
    /// round-trip as the wrong type and read as correct, so the contract is narrowed instead of widened.
    /// </summary>
    [Fact]
    public void Capture_RaisesADefinedErrorForAnUntaggedValueType_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, new Uri("https://example.invalid/"));

        NotSupportedException failure =
            Assert.Throws<NotSupportedException>(() => FullStateCodec.Capture(source));

        Assert.Contains("FullStateValueTag", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <see cref="FullStateCodec.Capture"/> is deliberately KIND-AGNOSTIC, because the legacy checks the
    /// processing kind at the DISPATCH site and its own <c>GetFullState</c> call is unconditional. The
    /// guard therefore lives on <see cref="FullStateCodec.Send"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChangesetProcessingKinds))]
    public void Capture_DoesNotItselfGuardTheProcessingKind_UnitLevelNoOracle(long kind)
    {
        Assert.NotEmpty(FullStateCodec.Capture(NewCrosstabCarrier(kind)));
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

    #region Corrupt-image rejection - every defensive decode path

    // THE BYTE OFFSETS BELOW ARE DERIVED FROM THE FORMAT, NOT GUESSED, and they hold for an image of a
    // carrier holding exactly ONE Primary! row with exactly ONE assigned column:
    //
    //      0..3    magic                       13      first buffer tag (Primary! = 0)
    //      4       format version              14..21  that buffer's row count
    //      5..12   processing kind             22..25  row 1 item status
    //                                          26..29  row 1 column count
    //                                          30..33  column number
    //                                          34..37  column item status
    //                                          38      original-value presence flag
    //                                          39      value tag
    //                                          40..    value payload
    //
    // Patching one field at a time is what isolates each rejection path. EVERY CASE MUST ANSWER THE
    // FAILURE CODE RATHER THAN THROW, because that is what SetFullState does with a payload it rejects
    // and the legacy caller stores that value and carries on [n_cst_threading_task_sqlquery.sru:L190].
    private const int VersionOffset = 4;
    private const int FirstBufferTagOffset = 13;
    private const int FirstBufferRowCountOffset = 14;
    private const int RowStatusOffset = 22;
    private const int ColumnCountOffset = 26;
    private const int ColumnNumberOffset = 30;
    private const int ColumnStatusOffset = 34;
    private const int OriginalPresenceOffset = 38;
    private const int ValueTagOffset = 39;
    private const int ValuePayloadOffset = 40;

    /// <summary>
    /// Captures an image of a carrier holding one Primary! row whose column one holds
    /// <paramref name="value"/>, so that the documented offsets above apply to it.
    /// </summary>
    /// <param name="value">The single column value.</param>
    /// <returns>The image.</returns>
    private static byte[] SingleRowImage(object? value)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, value);
        source.SetItemStatus(row, 1, DwBuffer.Primary, ItemStatus.DataModified);
        source.SetItemStatus(row, 0, DwBuffer.Primary, ItemStatus.DataModified);

        return FullStateCodec.Capture(source);
    }

    /// <summary>
    /// Applies an image to a fresh carrier and answers the result code.
    /// </summary>
    /// <param name="image">The image, corrupt or otherwise.</param>
    /// <returns>The result of the apply.</returns>
    private static long ApplyToFreshCarrier(byte[] image)
    {
        return FullStateCodec.Apply(new DataWindowBufferStore(), image);
    }

    /// <summary>Overwrites one byte and answers the image.</summary>
    /// <param name="image">The image to patch in place.</param>
    /// <param name="offset">The byte offset.</param>
    /// <param name="value">The replacement byte.</param>
    /// <returns><paramref name="image"/>.</returns>
    private static byte[] PatchByte(byte[] image, int offset, byte value)
    {
        image[offset] = value;

        return image;
    }

    /// <summary>Overwrites a little-endian 32-bit field and answers the image.</summary>
    /// <param name="image">The image to patch in place.</param>
    /// <param name="offset">The byte offset of the field.</param>
    /// <param name="value">The replacement value.</param>
    /// <returns><paramref name="image"/>.</returns>
    private static byte[] PatchInt32(byte[] image, int offset, int value)
    {
        BitConverter.GetBytes(value).CopyTo(image, offset);

        return image;
    }

    /// <summary>Overwrites a little-endian 64-bit field and answers the image.</summary>
    /// <param name="image">The image to patch in place.</param>
    /// <param name="offset">The byte offset of the field.</param>
    /// <param name="value">The replacement value.</param>
    /// <returns><paramref name="image"/>.</returns>
    private static byte[] PatchInt64(byte[] image, int offset, long value)
    {
        BitConverter.GetBytes(value).CopyTo(image, offset);

        return image;
    }

    /// <summary>
    /// An image whose FORMAT VERSION this codec does not know is rejected rather than decoded on the
    /// assumption that the layout is unchanged.
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)255)]
    public void Apply_RejectsAnUnknownFormatVersion_UnitLevelNoOracle(byte version)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchByte(SingleRowImage("a"), VersionOffset, version)));
    }

    /// <summary>
    /// The buffer order is written into the image and VERIFIED ON READ, so a mismatch is caught rather
    /// than silently applied to the wrong buffer - which would move rows between Primary!, Delete! and
    /// Filter! and pass every count assertion.
    /// </summary>
    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)7)]
    public void Apply_RejectsAnImageWhoseBufferOrderDisagrees_UnitLevelNoOracle(byte bufferTag)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchByte(SingleRowImage("a"), FirstBufferTagOffset, bufferTag)));
    }

    /// <summary>
    /// A NEGATIVE row count is structurally impossible - row counts are one-based counts - and is
    /// rejected instead of being fed to a loop.
    /// </summary>
    [Fact]
    public void Apply_RejectsANegativeRowCount_UnitLevelNoOracle()
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt64(SingleRowImage("a"), FirstBufferRowCountOffset, -1L)));
    }

    /// <summary>
    /// A NEGATIVE column count, likewise.
    /// </summary>
    [Fact]
    public void Apply_RejectsANegativeColumnCount_UnitLevelNoOracle()
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt32(SingleRowImage("a"), ColumnCountOffset, -1)));
    }

    /// <summary>
    /// A COLUMN NUMBER BELOW THE ONE-BASED FLOOR is rejected. R9: column numbers are never rebased in
    /// this port, so a zero here means the image was written by something that rebased them - and
    /// accepting it would shift every column by one while leaving every column count intact, which is
    /// precisely the defect class no count assertion can detect.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_RejectsAColumnNumberBelowTheOneBasedFloor_UnitLevelNoOracle(int columnNumber)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt32(SingleRowImage("a"), ColumnNumberOffset, columnNumber)));
    }

    /// <summary>
    /// AN ITEM STATUS OUTSIDE THE PUBLISHED FOUR-MEMBER DOMAIN IS REJECTED. This port adds no fifth
    /// status, so a corrupt image must not be able to plant one that then reads as legitimate everywhere
    /// downstream. Both the row status and the column status are validated.
    /// </summary>
    [Theory]
    [InlineData(RowStatusOffset, 4)]
    [InlineData(RowStatusOffset, -1)]
    [InlineData(RowStatusOffset, 99)]
    [InlineData(ColumnStatusOffset, 4)]
    [InlineData(ColumnStatusOffset, int.MaxValue)]
    public void Apply_RejectsAnItemStatusOutsideThePublishedDomain_UnitLevelNoOracle(
        int offset,
        int status)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt32(SingleRowImage("a"), offset, status)));
    }

    /// <summary>
    /// The original-value presence flag has exactly two legal values; anything else means the image was
    /// not written by this codec.
    /// </summary>
    [Theory]
    [InlineData((byte)2)]
    [InlineData((byte)255)]
    public void Apply_RejectsAnInvalidOriginalValuePresenceFlag_UnitLevelNoOracle(byte flag)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchByte(SingleRowImage("a"), OriginalPresenceOffset, flag)));
    }

    /// <summary>
    /// AN UNRECOGNISED VALUE TAG IS REJECTED. Tags are only ever appended, so an unknown one means the
    /// image came from a newer or a different encoder and its layout cannot be assumed.
    /// </summary>
    [Theory]
    [InlineData((byte)12)]
    [InlineData((byte)200)]
    public void Apply_RejectsAnUnrecognisedValueTag_UnitLevelNoOracle(byte tag)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchByte(SingleRowImage("a"), ValueTagOffset, tag)));
    }

    /// <summary>
    /// A NEGATIVE BLOB LENGTH is rejected, and a length longer than the remaining bytes is reported as a
    /// truncation rather than silently substituting a short blob for the real one.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000)]
    public void Apply_RejectsAnInvalidBlobLength_UnitLevelNoOracle(int length)
    {
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(
                PatchInt32(SingleRowImage(new byte[] { 1, 2, 3 }), ValuePayloadOffset, length)));
    }

    /// <summary>
    /// A timestamp whose KIND byte is not a defined <see cref="DateTimeKind"/> is rejected. The kind
    /// travels with the ticks precisely because dropping it would turn an unspecified timestamp into a
    /// local or UTC one, so an undefined value cannot be quietly defaulted either.
    /// </summary>
    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)200)]
    public void Apply_RejectsATimestampWithAnUndefinedKind_UnitLevelNoOracle(byte kind)
    {
        byte[] image = SingleRowImage(new DateTime(2022, 4, 14, 0, 0, 0, DateTimeKind.Utc));

        // The kind byte follows the eight ticks bytes.
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchByte(image, ValuePayloadOffset + 8, kind)));
    }

    /// <summary>
    /// A timestamp, date or time of day OUTSIDE ITS REPRESENTABLE RANGE is rejected rather than escaping
    /// as an out-of-range exception from inside the decoder.
    /// </summary>
    [Fact]
    public void Apply_RejectsTemporalValuesOutsideTheirRepresentableRange_UnitLevelNoOracle()
    {
        byte[] timestamp = SingleRowImage(new DateTime(2022, 4, 14, 0, 0, 0, DateTimeKind.Utc));
        byte[] date = SingleRowImage(new DateOnly(2022, 4, 14));
        byte[] time = SingleRowImage(new TimeOnly(12, 0));

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt64(timestamp, ValuePayloadOffset, long.MaxValue)));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt32(date, ValuePayloadOffset, int.MaxValue)));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt64(time, ValuePayloadOffset, long.MaxValue)));
    }

    /// <summary>
    /// A decimal whose four component integers are not a legal encoding is rejected. The components are
    /// written verbatim so that SCALE survives, which is exactly why an illegal scale has to be caught on
    /// the way back in.
    /// </summary>
    [Fact]
    public void Apply_RejectsADecimalWithAnIllegalComponentEncoding_UnitLevelNoOracle()
    {
        byte[] image = SingleRowImage(1.50m);

        // The fourth component carries the sign and the scale. A scale of 255 is not representable.
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ApplyToFreshCarrier(PatchInt32(image, ValuePayloadOffset + 12, 0x00FF_0000)));
    }

    // ==========================================================================================
    //  THE TWO STRING FAULTS, WHICH ARE THE ONES THAT USED TO ESCAPE
    //  ----------------------------------------------------------------------------------------
    //  A string value is written through BinaryWriter.Write(string), which emits a 7-BIT ENCODED
    //  LENGTH followed by the UTF-8 bytes, and it is read back through BinaryReader.ReadString. The
    //  encoding this codec uses is deliberately constructed with throwOnInvalidBytes: true, so that a
    //  value which cannot round-trip is a DETECTED fault rather than a silent replacement character.
    //  That decision has a consequence the decode path has to honour: malformed bytes make ReadString
    //  THROW, and a malformed 7-bit length makes it throw something different again.
    //
    //  Both faults are as plainly "the payload is bad" as a truncation is, and both must therefore
    //  answer the failure code SetFullState answers [n_cst_threading_task_sqlquery.sru:L190]. Neither
    //  is reachable by patching a field offset - one needs invalid UTF-8 in the value bytes and the
    //  other needs an illegal continuation pattern in the length prefix - which is why they are built
    //  by hand and why they went unnoticed.
    // ==========================================================================================

    /// <summary>
    /// A string value whose bytes are not valid UTF-8 is rejected with the failure code rather than
    /// escaping as a decoder exception.
    /// </summary>
    /// <remarks>
    /// <c>0xC3</c> introduces a two-byte sequence and <c>0x28</c> cannot continue one, so the pair is
    /// invalid UTF-8 in the one way a strict decoder is obliged to notice. The length prefix stays
    /// honest at two bytes, so this test isolates the DECODE fault from any length fault.
    /// </remarks>
    [Fact]
    public void Apply_RejectsMalformedUtf8InAStringValue_UnitLevelNoOracle()
    {
        // "ab" is two ASCII bytes, so its 7-bit length prefix is the single byte 0x02 and the two value
        // bytes sit immediately after it - which is what makes the substitution below exact.
        byte[] image = SingleRowImage("ab");

        Assert.Equal(0x02, image[ValuePayloadOffset]);

        image[ValuePayloadOffset + 1] = 0xC3;   // leads a two-byte sequence
        image[ValuePayloadOffset + 2] = 0x28;   // cannot continue one

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, ApplyToFreshCarrier(image));
    }

    /// <summary>
    /// A string value whose 7-BIT ENCODED LENGTH PREFIX is malformed is rejected with the failure code.
    /// </summary>
    /// <remarks>
    /// Every byte of a 7-bit encoded integer but the last sets its high bit, and the encoding admits at
    /// most five bytes. Five continuation bytes in a row therefore describe no integer at all, which
    /// raises a format fault from inside the length read - before any character has been decoded.
    /// </remarks>
    [Fact]
    public void Apply_RejectsAMalformedSevenBitStringLength_UnitLevelNoOracle()
    {
        byte[] image = SingleRowImage("abcdefgh");

        Assert.Equal(0x08, image[ValuePayloadOffset]);

        for (int offset = 0; offset < 5; offset++)
        {
            image[ValuePayloadOffset + offset] = 0xFF;
        }

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, ApplyToFreshCarrier(image));
    }

    // ==========================================================================================
    //  THE COUNTS A HOSTILE IMAGE CAN WEAPONISE, REJECTED BEFORE THEY REACH AN ALLOCATION
    //  ----------------------------------------------------------------------------------------
    //  An image arrives from a peer, so its row count, its per-row column count and any blob length
    //  are numbers chosen by whoever produced the bytes. The column count is the sharpest of the
    //  three, because ReadRow sizes FIVE arrays from it before reading a single column, and a blob
    //  length is next, because ReadBytes allocates from it BEFORE discovering the stream is shorter.
    //
    //  A negative value was already rejected, and the sibling cases above cover that. What these
    //  cases add is the MAXIMAL POSITIVE value, which a negative check does not touch, together with
    //  the assertion that matters more than the result code: THE ANSWER IS PRODUCED WITHOUT
    //  ALLOCATING. A decoder that reserved gigabytes and then failed would return the same code on a
    //  host with the memory to spare, and would take the host down on one without.
    // ==========================================================================================

    /// <summary>
    /// An image declaring <see cref="long.MaxValue"/> rows in a buffer is rejected before the row loop
    /// starts, and without allocating.
    /// </summary>
    [Fact]
    public void Apply_RejectsAnOversizedRowCountBeforeAllocating_UnitLevelNoOracle()
    {
        AssertRejectedWithoutAllocating(
            PatchInt64(SingleRowImage("a"), FirstBufferRowCountOffset, long.MaxValue));
    }

    /// <summary>
    /// An image declaring <see cref="int.MaxValue"/> columns on a row is rejected before the five decode
    /// arrays are sized from it, and without allocating.
    /// </summary>
    [Fact]
    public void Apply_RejectsAnOversizedColumnCountBeforeAllocating_UnitLevelNoOracle()
    {
        AssertRejectedWithoutAllocating(
            PatchInt32(SingleRowImage("a"), ColumnCountOffset, int.MaxValue));
    }

    /// <summary>
    /// An image declaring <see cref="int.MaxValue"/> blob bytes is rejected before
    /// <see cref="BinaryReader.ReadBytes"/> allocates from the length, and without allocating.
    /// </summary>
    [Fact]
    public void Apply_RejectsAnOversizedBlobLengthBeforeAllocating_UnitLevelNoOracle()
    {
        AssertRejectedWithoutAllocating(
            PatchInt32(SingleRowImage(new byte[] { 1, 2, 3 }), ValuePayloadOffset, int.MaxValue));
    }

    /// <summary>
    /// Asserts that <paramref name="image"/> answers the failure code and that producing that answer
    /// allocated a trivial amount of memory.
    /// </summary>
    /// <param name="image">The hand-patched image declaring a maximal count.</param>
    /// <remarks>
    /// <para>
    /// The budget is generous rather than tight on purpose: the decoder legitimately constructs a
    /// stream, a reader and a carrier, all of which allocate, and this assertion must not become
    /// brittle against a change in any of them. What it has to separate is KILOBYTES from GIGABYTES,
    /// and a one-megabyte budget does that with three orders of magnitude to spare.
    /// </para>
    /// <para>
    /// The reading is per-thread and counts allocation REQUESTS rather than surviving objects, which is
    /// exactly the quantity of interest here.
    /// </para>
    /// <para>
    /// THE CARRIER'S STATE IS DELIBERATELY NOT ASSERTED, and the reason is a real property of this
    /// codec rather than a gap in the test. <c>Apply</c> RESETS the target and then restores into it,
    /// and <see cref="FullStateCodec"/>'s row restore appends the row BEFORE decoding its columns -
    /// which it must, because the three-pass value restore addresses the row by the number
    /// <c>AppendRow</c> hands back. So a fault detected part-way through a row leaves that row present
    /// and incomplete. That matches the operation being reproduced: <c>SetFullState</c> answering -1
    /// leaves the DataStore in an unspecified state and the legacy caller reads the CODE, not the
    /// carrier [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L190</c>].
    /// Asserting emptiness here would therefore be asserting a guarantee the legacy does not give, and
    /// changing the append ordering to provide one would be a behavioural change (C-B). The sibling
    /// changeset codec IS asserted for emptiness, because its row decode reads every column into local
    /// arrays before admitting anything.
    /// </para>
    /// </remarks>
    private static void AssertRejectedWithoutAllocating(byte[] image)
    {
        const long allocationBudgetBytes = 1L << 20;

        DataWindowBufferStore target = new();

        long before = GC.GetAllocatedBytesForCurrentThread();

        long result = FullStateCodec.Apply(target, image);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);

        Assert.True(
            allocated < allocationBudgetBytes,
            $"Decoding a {image.Length}-byte image that declared a maximal count allocated {allocated} "
                + $"bytes, over the {allocationBudgetBytes}-byte budget. The count is reaching an "
                + "allocation before it is bounded.");
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
                chunk.Data.ToByteArray()));

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
