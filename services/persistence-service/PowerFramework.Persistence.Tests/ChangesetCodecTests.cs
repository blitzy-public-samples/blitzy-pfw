// ==============================================================================================
//  ChangesetCodecTests.cs - PARITY TESTS FOR Buffers/ChangesetCodec.cs
//  --------------------------------------------------------------------------------------------
//  ORACLE        ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru        (send)
//                ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru  (receive)
//  FIXTURE SHAPE ws_objects/pfw.tests.pbl.src/dw_sqlite.srd - processing=1, sort="age A salary A ",
//                six columns id/name/age/address/salary/birth all update=yes updatewhereclause=yes,
//                updatewhere=1 updatekeyinplace=no.
//
//  WHY THESE TESTS CARRY THE PARITY WEIGHT. Eleven of the repository's twelve DataWindow definitions
//  declare processing=1, so the CHANGESET codec is the only one the legacy corpus exercises at all -
//  the full-state codec has no fixture. And the primary fixture is BOTH processing=1 and SORTED, so
//  the sorted multi-chunk workaround of DEFECT 1 is a MAIN PATH here, not an edge case.
//
//  NO DATABASE, NO CONTAINER, NO CLOCK, NO NETWORK. Every case below is a pure transform over an
//  in-memory carrier, which is what makes the per-service line-coverage gate attainable (C-H). The
//  inter-chunk yield is driven at TimeSpan.Zero through the injected TimeProvider so a many-chunk
//  sequence costs nothing, while the production default stays the legacy 0.02 seconds.
//
//  RULES POSITION. review_rules returns exactly one line, "No user rules provided.", so no
//  user-specified rule governs these tests and none is invented. The enterprise-standard baseline
//  applies and the binding constraints are the refactor plan's non-rule inventory.
// ==============================================================================================

using System.Globalization;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// A recording <see cref="IChangesetTransferSink"/>: it captures every handover verbatim so a test can
/// assert on the SEQUENCE the codec produced rather than only on its return code.
/// </summary>
internal sealed class RecordingTransferSink : IChangesetTransferSink
{
    private readonly List<(CarrierState? State, long ChunkCount, long ChunkIndex, bool FullState)>
        _chunks = [];

    private readonly List<(string ColumnName, CarrierState? State)> _children = [];
    private readonly List<(long ReturnCode, string ErrorText)> _errors = [];
    private readonly List<string> _createdFrom = [];

    /// <summary>Result the next chunk handover answers. Negative selects the failure arm.</summary>
    internal long ChunkResult { get; set; }

    /// <summary>Result the next child handover answers. Negative selects the failure arm.</summary>
    internal long ChildResult { get; set; }

    /// <summary>Runs after each chunk handover, so a test can mutate the source between chunks.</summary>
    internal Action? AfterChunk { get; set; }

    internal IReadOnlyList<(CarrierState? State, long ChunkCount, long ChunkIndex, bool FullState)>
        Chunks => _chunks;

    internal IReadOnlyList<(string ColumnName, CarrierState? State)> Children => _children;

    internal IReadOnlyList<(long ReturnCode, string ErrorText)> Errors => _errors;

    internal IReadOnlyList<string> CreatedFrom => _createdFrom;

    public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
    {
        _createdFrom.Add(syntax);

        return ValueTask.FromResult(0L);
    }

    public ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken)
    {
        // The state is cloned on receipt because the sender is entitled to drop its reference
        // immediately afterwards, reproducing `blbData = Blob("")`. A test asserts on this clone.
        _chunks.Add((
            chunk.State is null ? null : chunk.State.Clone(),
            chunk.ChunkCount,
            chunk.ChunkIndex,
            chunk.FullState));

        AfterChunk?.Invoke();

        return ValueTask.FromResult(ChunkResult);
    }

    public ValueTask<long> SendChildChunkAsync(
        ChangesetChildPayload payload,
        CancellationToken cancellationToken)
    {
        _children.Add((
            payload.ColumnName,
            payload.State is null ? null : payload.State.Clone()));

        return ValueTask.FromResult(ChildResult);
    }

    public void ReportError(long returnCode, string errorText)
    {
        _errors.Add((returnCode, errorText));
    }
}

/// <summary>
/// A substitutable payload codec whose answers are dictated by the test, which is the only way the
/// legacy's <c>GetChanges Failed</c> arm and its <c>rtCode &gt; 1</c> partial-apply coercion are
/// reachable at all - a real in-memory encoder never fails and never partially succeeds.
/// </summary>
internal sealed class ScriptedPayloadCodec : IChangesetPayloadCodec
{
    private readonly ChangesetPayloadCodec _real = new();

    /// <summary>Answer for every encode, or <see langword="null"/> to delegate to the real format.</summary>
    internal long? EncodeResult { get; set; }

    /// <summary>Answer for every apply, or <see langword="null"/> to delegate to the real format.</summary>
    internal long? ApplyResult { get; set; }

    /// <summary>
    /// State handed back when <see cref="EncodeResult"/> dictates the answer. A conforming state - three
    /// canonically ordered empty segments - so that a dictated SUCCESS produces something a receiver
    /// would accept, which is what keeps a dictated-success test about the arm under test rather than
    /// about segment validation.
    /// </summary>
    internal CarrierState EncodedState { get; set; } = new()
    {
        Processing = 1L,
        Segments =
        {
            new CarrierBufferSegment { Buffer = DwBuffer.Primary },
            new CarrierBufferSegment { Buffer = DwBuffer.Delete },
            new CarrierBufferSegment { Buffer = DwBuffer.Filter },
        },
    };

    public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
    {
        if (EncodeResult is not long dictated)
        {
            return _real.TryEncode(source, out state);
        }

        state = dictated < DataWindowBufferStore.DataStoreSuccess ? null : EncodedState;

        return dictated;
    }

    public long TryApply(DataWindowBufferStore target, CarrierState? state)
    {
        return ApplyResult ?? _real.TryApply(target, state);
    }
}

/// <summary>
/// Builds carriers shaped like the golden-master fixture so that no test has to know the format.
/// </summary>
internal static class FixtureCarrier
{
    /// <summary>The fixture's six column numbers, in the order it declares them [dw_sqlite.srd:L21-L26].</summary>
    internal const int ColumnCount = 6;

    /// <summary>
    /// Creates a carrier holding <paramref name="primaryRows"/> rows in <c>Primary!</c>,
    /// <paramref name="filteredRows"/> in <c>Filter!</c> and <paramref name="deletedRows"/> in
    /// <c>Delete!</c>, every row carrying the fixture's six columns and every row initially
    /// <see cref="ItemStatus.NotModified"/> - the state a freshly retrieved row is in.
    /// </summary>
    internal static DataWindowBufferStore Create(
        long primaryRows,
        long filteredRows = 0L,
        long deletedRows = 0L,
        long processing = 1L)
    {
        DataWindowBufferStore carrier = new()
        {
            Processing = new DataWindowProcessing(processing),
        };

        Populate(carrier, DwBuffer.Primary, primaryRows, 0L);
        Populate(carrier, DwBuffer.Filter, filteredRows, 1000L);
        Populate(carrier, DwBuffer.Delete, deletedRows, 2000L);

        return carrier;
    }

    /// <summary>The fixture's declared sort expression, trailing space included [dw_sqlite.srd:L14].</summary>
    internal static ChangesetSourceDefinition SortedDefinition =>
        new()
        {
            DataObjectName = "dw_sqlite",
            Syntax = "release 12.5;",
            SortExpression = "age A salary A ",
            FilterExpression = string.Empty,
        };

    internal static ChangesetSourceDefinition UnsortedDefinition =>
        SortedDefinition with { SortExpression = string.Empty };

    /// <summary>Appends rows carrying the fixture's six columns, seeded so each row is distinguishable.</summary>
    private static void Populate(
        DataWindowBufferStore carrier,
        DwBuffer dwBuffer,
        long rowCount,
        long seed)
    {
        for (long row = 1L; row <= rowCount; row++)
        {
            long identifier = seed + row;
            long rowNumber = carrier.AppendRow(dwBuffer, ItemStatus.NotModified);

            // SEEDED AS long RATHER THAN int, DELIBERATELY. The fixture's two integer columns are
            // `id integer primary key autoincrement` and `age integer not null`
            // [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469], and the production path reads
            // them through Microsoft.Data.Sqlite, which yields long for a SQLite INTEGER - it has no int
            // path at all. Seeding int here would additionally make every round-trip comparison in this
            // service assert against the DECLARED WIDTH rather than the value: common.v1.AnyValue
            // publishes ONE signed integer arm, so an int travels as it and returns as long, and the
            // widening - which is the contract's documented decision, asserted directly by
            // ChangesetPayloadCodecTests.EveryMappedValueTypeRoundTripsOntoItsDeclaredCarrierType -
            // would show up here as an unrelated inequality between two boxed ones.
            _ = carrier.SetItemValue(rowNumber, 1, dwBuffer, identifier);
            _ = carrier.SetItemValue(
                rowNumber,
                2,
                dwBuffer,
                string.Create(CultureInfo.InvariantCulture, $"name-{identifier}"));
            _ = carrier.SetItemValue(rowNumber, 3, dwBuffer, identifier % 90L);
            _ = carrier.SetItemValue(
                rowNumber,
                4,
                dwBuffer,
                string.Create(CultureInfo.InvariantCulture, $"address-{identifier}"));
            _ = carrier.SetItemValue(rowNumber, 5, dwBuffer, decimal.Divide(identifier, 4m));
            _ = carrier.SetItemValue(
                rowNumber,
                6,
                dwBuffer,
                new DateOnly(1980, 1, 1).AddDays((int)identifier));

            // A retrieved row's originals equal its currents, which is what ResetUpdate establishes on
            // the receiving side [n_cst_threading_task_sqlquery.sru:L240].
            carrier.RowAt(rowNumber, dwBuffer).Baseline();
        }
    }
}

/// <summary>
/// The chunk arithmetic, the branch guards and the pure decisions - every one of them exercisable with
/// no carrier at all.
/// </summary>
public sealed class ChangesetCodecDecisionTests
{
    /// <summary>
    /// <c>nChunkCnt = Ceiling((RowCount() + FilteredCount()) / _nChunkSize)</c>
    /// [n_cst_thread_task_sqlquery.sru:L145]. THE SUM SPANS BOTH BUFFERS.
    /// </summary>
    public static TheoryData<long, long, long, long> ChunkCountCases =>
        new()
        {
            // primary, filtered, chunkSize, expected
            { 0L, 0L, 10L, 0L },        // the empty-result arm [:L229]
            { 1L, 0L, 10L, 1L },        // one row under one chunk
            { 10L, 0L, 10L, 1L },       // exactly one chunk
            { 11L, 0L, 10L, 2L },       // one row over one chunk
            { 9L, 1L, 10L, 1L },        // mixed, exactly one chunk
            { 10L, 1L, 10L, 2L },       // mixed, one row over - primary alone would say ONE
            { 0L, 10L, 10L, 1L },       // filtered only, exactly one chunk
            { 0L, 11L, 10L, 2L },       // filtered only, one over
            { 5L, 5L, 10L, 1L },        // mixed halves
            { 6L, 5L, 10L, 2L },        // mixed, one over
            { 30000L, 0L, 10000L, 3L }, // an exact multiple at the production default
            { 30001L, 0L, 10000L, 4L },
            { 0L, 30000L, 10000L, 3L },
            { 15000L, 15000L, 10000L, 3L },
            { 1L, 1L, 1L, 2L },         // a chunk size of one is legal here; the 1000 floor is the
                                        // query task's guard [:L410], not this codec's
        };

    /// <summary>
    /// <c>if nChunkCnt &gt; 1 and sProp &lt;&gt; "?" and sProp &lt;&gt; ""</c>
    /// [n_cst_thread_task_sqlquery.sru:L151] - DEFECT 1's three-condition guard.
    /// </summary>
    public static TheoryData<long, string, bool> TemporaryCarrierCases =>
        new()
        {
            // chunkCount, sortExpression, expected
            { 1L, "age A salary A ", false },  // one chunk: the workaround is not needed
            { 2L, "age A salary A ", true },   // THE FIXTURE'S OWN SHAPE, multi-chunk
            { 3L, "age A", true },
            { 2L, "?", false },                // not applicable is ABSENT, distinctly from empty
            { 2L, "", false },                 // empty is a real answer meaning "no sort"
            { 1L, "?", false },
            { 1L, "", false },
            { 0L, "age A", false },            // no chunks at all
            { 2L, " ", true },                 // a single space is NOT the "?" marker and NOT empty
        };

    /// <summary>
    /// The four-part gate on clearing sort and filter [n_cst_thread_task_sqlquery.sru:L550, L559, L560].
    /// </summary>
    public static TheoryData<bool, bool, bool, bool, bool> ClearSortAndFilterCases =>
        new()
        {
            // hasDataObject, isMainThread, needsCreate, requiresFullState, expected
            { true, false, false, false, true },   // the only combination that clears
            { false, false, false, false, false }, // no data object [:L550]
            { true, true, false, false, false },   // on the main thread [:L559]
            { true, false, true, false, false },   // receiver needs a created object [:L559]
            { true, false, false, true, false },   // full-state arm SYNCHRONIZES instead [:L562-L575]
            { true, true, true, true, false },
        };

    /// <summary>
    /// <c>if sProp &lt;&gt; "yes" then continue</c> [:L116] and
    /// <c>if sProp &lt;&gt; "!" and sProp &lt;&gt; "?" then</c> [:L118].
    /// </summary>
    public static TheoryData<string, string, bool> ChildEligibilityCases =>
        new()
        {
            // autoRetrieve, dropDownName, expected
            { "yes", "d_lookup", true },
            { "no", "d_lookup", false },
            { "", "d_lookup", false },
            { "Yes", "d_lookup", false },   // the test is EXACT, and the legacy literal is lower case
            { "yes", "!", false },          // unreadable
            { "yes", "?", false },          // not applicable
            { "yes", "", true },            // empty is neither marker, so the column is eligible
        };

    [Theory]
    [MemberData(nameof(ChunkCountCases))]
    public void ComputeChunkCountSpansBothBuffers(
        long primaryRows,
        long filteredRows,
        long chunkSize,
        long expected)
    {
        Assert.Equal(expected, ChangesetCodec.ComputeChunkCount(primaryRows, filteredRows, chunkSize));
    }

    [Fact]
    public void ComputeChunkCountRejectsAStructurallyImpossibleInput()
    {
        // The refactor plan requires structural faults FAIL FAST rather than degrade into a code.
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ChangesetCodec.ComputeChunkCount(-1L, 0L, 10L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ChangesetCodec.ComputeChunkCount(0L, -1L, 10L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ChangesetCodec.ComputeChunkCount(1L, 0L, 0L));
    }

    [Theory]
    [MemberData(nameof(TemporaryCarrierCases))]
    public void RequiresTemporaryCarrierHonoursAllThreeConditions(
        long chunkCount,
        string sortExpression,
        bool expected)
    {
        ChangesetSourceDefinition definition =
            FixtureCarrier.SortedDefinition with { SortExpression = sortExpression };

        Assert.Equal(expected, ChangesetCodec.RequiresTemporaryCarrier(chunkCount, definition));
    }

    [Fact]
    public void RequiresTemporaryCarrierTreatsAnAbsentSortExpressionAsAbsent()
    {
        // Defensive: a null describe result must read as ABSENT rather than throwing, because the guard
        // it feeds decides which branch of DEFECT 1's workaround runs.
        ChangesetSourceDefinition definition =
            FixtureCarrier.SortedDefinition with { SortExpression = null! };

        Assert.True(definition.SortIsAbsent);
        Assert.False(ChangesetCodec.RequiresTemporaryCarrier(2L, definition));
    }

    [Theory]
    [MemberData(nameof(ClearSortAndFilterCases))]
    public void ShouldClearSortAndFilterHonoursTheWholeGate(
        bool hasDataObject,
        bool isMainThread,
        bool receiverNeedsCreatedObject,
        bool requiresFullStateTransfer,
        bool expected)
    {
        ChangesetSourceDefinition definition = FixtureCarrier.SortedDefinition with
        {
            DataObjectName = hasDataObject ? "dw_sqlite" : string.Empty,
        };

        Assert.Equal(
            expected,
            ChangesetCodec.ShouldClearSortAndFilter(
                definition,
                isMainThread,
                receiverNeedsCreatedObject,
                requiresFullStateTransfer));
    }

    [Fact]
    public void ClearSortAndFilterEmptiesBothAndDisturbsNothingElse()
    {
        // `data.SetSort("") : data.SetFilter("")` [:L579-L580]. EMPTY, not the "?" marker.
        ChangesetSourceDefinition original = FixtureCarrier.SortedDefinition with
        {
            FilterExpression = "age > 30",
        };

        ChangesetSourceDefinition cleared = ChangesetCodec.ClearSortAndFilter(original);

        Assert.Equal(string.Empty, cleared.SortExpression);
        Assert.Equal(string.Empty, cleared.FilterExpression);
        Assert.Equal(original.DataObjectName, cleared.DataObjectName);
        Assert.Equal(original.Syntax, cleared.Syntax);

        // The original is untouched, which is what makes the transition auditable.
        Assert.Equal("age A salary A ", original.SortExpression);
        Assert.Equal("age > 30", original.FilterExpression);
    }

    [Theory]
    [MemberData(nameof(ChildEligibilityCases))]
    public void IsChildEligibleScreensBothDescribeResults(
        string autoRetrieve,
        string dropDownName,
        bool expected)
    {
        Assert.Equal(expected, ChangesetCodec.IsChildEligible(autoRetrieve, dropDownName));
    }

    [Fact]
    public void IsChildEligibleKeepsTheLegacysExactTwoMarkerTest()
    {
        // The auto-retrieve test is an EXACT match against the lower-case word [:L116], so anything
        // else - including an absent describe result - skips the column.
        Assert.False(ChangesetCodec.IsChildEligible(null, "d_lookup"));

        // An absent NAME, by contrast, PASSES, and that is deliberate rather than an oversight. The
        // legacy screens only "!" and "?" [:L118], and PowerBuilder's Describe answers a string in every
        // case - a value, "!", "?" or "" - so a null has NO legacy counterpart and nothing in the oracle
        // says to reject it. A column that survives this predicate with an unusable name is filtered one
        // line later, where GetChild answers -1 and the loop continues [:L120]; that lookup is a
        // definition lookup and belongs to the caller, which is exactly where this predicate stops.
        Assert.True(ChangesetCodec.IsChildEligible("yes", null));
        Assert.True(ChangesetCodec.IsChildEligible("yes", string.Empty));
    }

    [Theory]
    [InlineData(1L, true)]   // the fixture's own processing kind [dw_sqlite.srd:L3]
    [InlineData(0L, true)]   // an unassigned carrier lands in the `case else` arm
    [InlineData(2L, true)]
    [InlineData(3L, true)]
    [InlineData(4L, false)]  // crosstab [:L94]
    [InlineData(5L, false)]  // composite [:L94]
    [InlineData(6L, true)]
    public void HandlesCarrierIsTheCaseElseArmOfTheSelector(long processing, bool expected)
    {
        DataWindowBufferStore carrier = FixtureCarrier.Create(1L, processing: processing);

        Assert.Equal(expected, ChangesetCodec.HandlesCarrier(carrier));
    }

    [Fact]
    public void FoldFilterBufferIntoPrimaryAppendsAtTheTail()
    {
        // `Data.RowsMove(1,Data.FilteredCount(),Filter!,Data,Data.RowCount() + 1,Primary!)` [:L109].
        // R9 item 4 - the append idiom. The folded rows must land AFTER the existing primary rows, and
        // the seeds make that assertable: primary rows are seeded from 0, filtered rows from 1000.
        DataWindowBufferStore carrier = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            ChangesetCodec.FoldFilterBufferIntoPrimary(carrier));

        Assert.Equal(5L, carrier.RowCount());
        Assert.Equal(0L, carrier.FilteredCount());

        // The three original primary rows keep their positions...
        Assert.Equal(1L, carrier.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(2L, carrier.GetItemValue(2L, 1, DwBuffer.Primary));
        Assert.Equal(3L, carrier.GetItemValue(3L, 1, DwBuffer.Primary));

        // ...and the two folded rows are at the TAIL, in their original order. Had the append landed
        // one position early, these two identifiers would appear at rows 3 and 4 instead.
        Assert.Equal(1001L, carrier.GetItemValue(4L, 1, DwBuffer.Primary));
        Assert.Equal(1002L, carrier.GetItemValue(5L, 1, DwBuffer.Primary));
    }

    [Fact]
    public void FoldFilterBufferIntoPrimaryIsANoOpWhenNothingIsFiltered()
    {
        // `1, FilteredCount()` becomes the range 1 through 0 - end before start - which PowerBuilder
        // answers with -1 while moving nothing. The legacy IGNORES that answer (C-B).
        DataWindowBufferStore carrier = FixtureCarrier.Create(primaryRows: 3L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            ChangesetCodec.FoldFilterBufferIntoPrimary(carrier));

        Assert.Equal(3L, carrier.RowCount());
        Assert.Equal(0L, carrier.FilteredCount());
    }

    [Fact]
    public void CreateTemporaryCarrierPrefersTheDataObjectNameAndFallsBackToTheSyntax()
    {
        // `if Data.DataObject <> "" then dsTmp.DataObject = ... else dsTmp.Create(...)` [:L153-L157].
        DataWindowBufferStore named = ChangesetCodec.CreateTemporaryCarrier(
            FixtureCarrier.SortedDefinition,
            new DataWindowProcessing(1L),
            out TemporaryCarrierProvenance namedShape);

        Assert.Equal(TemporaryCarrierProvenance.DataObjectName, namedShape);

        DataWindowBufferStore fromSyntax = ChangesetCodec.CreateTemporaryCarrier(
            FixtureCarrier.SortedDefinition with { DataObjectName = string.Empty },
            new DataWindowProcessing(1L),
            out TemporaryCarrierProvenance syntaxShape);

        Assert.Equal(TemporaryCarrierProvenance.Syntax, syntaxShape);

        // Both start empty and both agree with the source about which codec owns them, so the staging
        // buffer can never answer the selector differently from the carrier it stages for.
        foreach (DataWindowBufferStore temporary in new[] { named, fromSyntax })
        {
            Assert.Equal(0L, temporary.RowCount());
            Assert.Equal(0L, temporary.FilteredCount());
            Assert.Equal(0L, temporary.DeletedCount());
            Assert.False(temporary.RequiresFullStateTransfer);
        }
    }

    [Fact]
    public void ChunkCountersAreValidatedInTheOneBasedDomain()
    {
        // R9 item 2. A zero index would make the receive side's `if current = 1` reset test never fire,
        // and the target would accumulate rows across sequences with nothing failing loudly.
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChangesetChunk(state: null, 3L, 0L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChangesetChunk(state: null, 3L, 4L));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChangesetChunk(state: null, 0L, 1L));

        ChangesetChunk only = new(state: null, 1L, 1L);

        Assert.True(only.IsFirstChunk);
        Assert.True(only.IsLastChunk);
        Assert.True(only.IsEmptyPayload);
        Assert.False(only.FullState);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("?", false, false)]  // length 1, which is why the threshold is > 1 and not > 0
    [InlineData("!", false, false)]
    [InlineData("ab", true, true)]
    [InlineData("age A salary A ", true, true)]
    public void ReapplicationPredicatesUseTheLengthGreaterThanOneThreshold(
        string expression,
        bool filterExpected,
        bool sortExpected)
    {
        ChangesetSourceDefinition definition = FixtureCarrier.SortedDefinition with
        {
            FilterExpression = expression,
            SortExpression = expression,
        };

        Assert.Equal(filterExpected, definition.FilterIsSubstantial);
        Assert.Equal(sortExpected, definition.SortIsSubstantial);
    }
}
