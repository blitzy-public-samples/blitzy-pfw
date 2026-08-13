// ==============================================================================================
//  ChangesetCodecTests.cs - THE CHARACTERIZATION SUITE FOR Buffers/ChangesetCodec.cs
//  --------------------------------------------------------------------------------------------
//  ORACLE, READ ONLY (C-C). Every path below is cited, never copied and never edited:
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru        the SEND side
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru    the RECEIVE side
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru     main-thread carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru  worker-thread carrier
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                            the golden-master shape
//      docs/PB多线程绕坑提示.md                                               the threading hazards
//  Bare `:Lnnn` locators below refer to n_cst_thread_task_sqlquery.sru; the receive side is always
//  named. The fixture's own values are consumed from DwSqliteFixture, which is this suite's single
//  transcription of dw_sqlite.srd - no literal from the oracle is re-typed here.
//
//  ============================================================================================
//  THIS FILE IS A CHARACTERIZATION SUITE, WHICH IS NOT THE SAME THING AS A CORRECTNESS SUITE
//  ============================================================================================
//  Buffers/ChangesetCodec.cs reproduces behaviour the legacy documented AGAINST ITSELF, and C-B
//  forbids correcting it. Two of the four documented cross-thread transfer defects live on this
//  path and this file OWNS them:
//
//    DEFECT 1 [:L147-L149]  a sorted DataWindow spanning more than one block may LOSE ROWS through
//                           SetChanges; the legacy routes around it with a temporary DataStore.
//    DEFECT 2 [:L175-L176]  Reset must NOT be used to clear the data, because doing so makes
//                           SetChanges fail to apply to rows; the legacy discards rows instead.
//
//  Every assertion about either is written as an assertion that the DEFECT AND ITS WORKAROUND ARE
//  STILL PRESENT. Nothing here asserts that rows are never lost, and nothing here asserts that
//  Reset is safe. Each such assertion carries a comment naming it a preserved legacy defect and
//  citing its line, so that a reviewer can tell a pinned defect from a test bug at a glance (C-K).
//  If one of these tests ever fails because the codec "got better", the codec regressed.
//
//  ============================================================================================
//  WHY THE PARITY WEIGHT RESTS HERE
//  ============================================================================================
//  The codec selector at :L93 sends crosstab and composite carriers to Buffers/FullStateCodec.cs
//  and EVERYTHING ELSE here. Counted across the repository, ALL TWELVE DataWindow definitions take
//  THIS arm - eleven declare processing=1 and dw_barcode.srd declares processing=0, and neither
//  value is the 4 or 5 that the selector routes elsewhere - so the changeset arm is the only arm
//  the legacy corpus exercises at all. The only UPDATABLE definition, dw_sqlite.srd, is both
//  processing=1 [:L3] AND sorted [:L14], which makes DEFECT 1's workaround a MAIN PATH for the
//  golden-master fixture rather than an edge case.
//
//  ============================================================================================
//  CONSTRAINTS THAT GOVERN THIS FILE, AND HOW EACH IS DISCHARGED
//  ============================================================================================
//  C-B   Every defect is asserted as EXPECTED. No corrected behaviour is asserted anywhere.
//  C-C   The oracle is cited by locator and consumed through DwSqliteFixture. Nothing is edited.
//  C-E   NO FABRICATED DATABASE. Every case runs over the in-memory carrier; there is no SQLite
//        file, no connection, no DbContext and no dialect client in this file, and a reflection
//        test at the end holds the codec itself to the same standard.
//  C-K   Both defects are documented at their assertion sites, and so is the deliberate
//        DISAGREEMENT between this codec's arm - which CLEARS sort and filter [:L577-L580] - and
//        the full-state arm, which SYNCHRONIZES them [:L562-L563]. A future reader must not
//        "unify" the two arms; FullStateCodecTests asserts the other side of that opposition.
//  C-H   The chunk matrix, both defects, both failure arms and the receive side together drive
//        ChangesetCodec.cs to high line coverage without a database or a container.
//  0.8.5 NO PERFORMANCE ASSERTION. The legacy NOTE at :L578 remarks that sort and filter can even
//        hurt performance; this file asserts only the BEHAVIOUR - that both are cleared - and
//        makes no claim about speed, throughput or latency anywhere.
//  0.6.7 DETERMINISM. Chunking and ordering are driven entirely by the doubles. The only clock is
//        the injected FakeTimeProvider, the inter-chunk yield is driven at TimeSpan.Zero, and no
//        test depends on execution order, on wall-clock time or on another test having run.
//  0.7.2 Nullable reference types and TreatWarningsAsErrors are inherited from
//        Directory.Build.props. No SCREAMING_SNAKE identifier is declared here: the AAP 0.4.5.3
//        preservation rule applies to PORTED CONSTANTS, and this file ports none.
//
//  RULES POSITION (UR4). review_rules returns exactly one line - "No user rules provided." - so no
//  user-specified rule governs this file and none is invented. The enterprise-standard baseline
//  applies in their place, and the binding constraints are the refactor plan's non-rule inventory
//  cited above.
//
//  ============================================================================================
//  THE THREE SHARED HELPERS BELOW ARE A CONTRACT WITH TWO OTHER FILES - DO NOT NARROW THEM
//  ============================================================================================
//  RecordingTransferSink, ScriptedPayloadCodec and FixtureCarrier are file-scope types consumed by
//  ChangesetCodecTransferTests.cs and ChangesetCodecReceiveTests.cs as well as by this file.
//  Members may be ADDED to them; removing or retyping one breaks the module build. The additions
//  this file makes - the ordered sink event log and the capture-time carrier observations - are
//  purely additive for exactly that reason.
// ==============================================================================================

using System.Globalization;
using System.Reflection;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Which handover a recorded sink interaction was.
/// </summary>
/// <remarks>
/// Tagging each interaction with its verb is what lets ONE ordered log carry all four of the sink's
/// members without losing which one a given entry arrived through - the same decomposition
/// <c>CarrierCallKind</c> in <c>TestDoubles.cs</c> applies to the describe and modify surface.
/// </remarks>
internal enum ChangesetSinkEventKind
{
    /// <summary><c>tasking.Event OnCreateData(Data.Describe("DataWindow.Syntax"))</c> [<c>:L104</c>].</summary>
    CreateData,

    /// <summary>
    /// <c>tasking.Event OnDataChunk(ref blbData,nChunkCnt,nChunkIdx,false)</c> [<c>:L183</c>,
    /// <c>:L219</c>, <c>:L230</c>].
    /// </summary>
    Chunk,

    /// <summary>
    /// <c>tasking.Event OnChildDataReceived(sColName,ref blbData)</c> [<c>:L134</c>].
    /// </summary>
    ChildChunk,

    /// <summary>
    /// <c>Event OnError(RetCode.E_INTERNAL_ERROR,...)</c> [<c>:L130</c>, <c>:L135</c>, <c>:L172</c>,
    /// <c>:L184</c>, <c>:L206</c>, <c>:L220</c>].
    /// </summary>
    Error,
}

/// <summary>One sink interaction, with its position in call order.</summary>
/// <param name="Ordinal">The one-based position of this interaction among all of them.</param>
/// <param name="Kind">Which member was invoked.</param>
/// <param name="Detail">
/// A rendering of the interaction - the chunk counters, the child column name or the error text -
/// so one log can carry every verb.
/// </param>
internal readonly record struct RecordedSinkEvent(
    int Ordinal,
    ChangesetSinkEventKind Kind,
    string Detail);

/// <summary>
/// What a carrier looked like AT THE MOMENT the codec captured a changeset from it - the managed
/// equivalent of standing at <c>GetChanges(ref blbData)</c> [<c>:L171</c>, <c>:L205</c>] and reading
/// the carrier before anything else touches it.
/// </summary>
/// <param name="Ordinal">The one-based position of this capture among all captures.</param>
/// <param name="PrimaryRows"><c>RowCount()</c> at capture time.</param>
/// <param name="FilteredRows"><c>FilteredCount()</c> at capture time.</param>
/// <param name="DeletedRows"><c>DeletedCount()</c> at capture time.</param>
/// <param name="PrimaryIdentifiers">
/// The identifier column of every <c>Primary!</c> row, in buffer order, so a test can assert WHICH
/// rows a chunk staged rather than only how many.
/// </param>
/// <param name="FilterIdentifiers">The same for <c>Filter!</c>.</param>
/// <param name="PrimaryRowStatuses">
/// The ROW status of every <c>Primary!</c> row, read through the column-zero convention that
/// <c>SetItemStatus(nRow,0,Primary!,DataModified!)</c> [<c>:L162</c>] writes through.
/// </param>
/// <param name="FilterRowStatuses">The same for <c>Filter!</c>.</param>
internal readonly record struct RecordedCapture(
    int Ordinal,
    long PrimaryRows,
    long FilteredRows,
    long DeletedRows,
    IReadOnlyList<object?> PrimaryIdentifiers,
    IReadOnlyList<object?> FilterIdentifiers,
    IReadOnlyList<ItemStatus> PrimaryRowStatuses,
    IReadOnlyList<ItemStatus> FilterRowStatuses)
{
    /// <summary>Reads a carrier's whole observable shape without mutating it.</summary>
    /// <param name="ordinal">The one-based capture position.</param>
    /// <param name="carrier">The carrier presented for capture.</param>
    /// <returns>The observation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="carrier"/> is <see langword="null"/>.</exception>
    internal static RecordedCapture Of(int ordinal, DataWindowBufferStore carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        long primaryRows = carrier.RowCount();
        long filteredRows = carrier.FilteredCount();

        return new RecordedCapture(
            ordinal,
            primaryRows,
            filteredRows,
            carrier.DeletedCount(),
            ReadIdentifiers(carrier, DwBuffer.Primary, primaryRows),
            ReadIdentifiers(carrier, DwBuffer.Filter, filteredRows),
            ReadRowStatuses(carrier, DwBuffer.Primary, primaryRows),
            ReadRowStatuses(carrier, DwBuffer.Filter, filteredRows));
    }

    private static List<object?> ReadIdentifiers(
        DataWindowBufferStore carrier,
        DwBuffer dwBuffer,
        long rowCount)
    {
        List<object?> identifiers = [];

        // R9: one-based, inclusive at both ends, exactly as every legacy loop is written.
        for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
        {
            identifiers.Add(carrier.GetItemValue(row, DwSqliteFixture.IdColumnNumber, dwBuffer));
        }

        return identifiers;
    }

    private static List<ItemStatus> ReadRowStatuses(
        DataWindowBufferStore carrier,
        DwBuffer dwBuffer,
        long rowCount)
    {
        List<ItemStatus> statuses = [];

        for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
        {
            // Column ZERO is not a column; it means "the row itself", which is the convention the
            // legacy's own stamping loop uses [:L161-L163, :L167-L169].
            statuses.Add(
                carrier.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, dwBuffer));
        }

        return statuses;
    }
}

/// <summary>
/// A recording <see cref="IChangesetTransferSink"/>: it captures every handover verbatim, and in ONE
/// ORDERED LOG, so a test can assert on the SEQUENCE the codec produced rather than only on its
/// return code.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY ORDER AND NOT ONLY CONTENT.</b> Several of this codec's behaviours are contracts about
/// WHERE a step happens rather than about whether it happens at all. The clear that DEFECT 2
/// [<c>:L175-L176</c>] constrains runs BETWEEN the capture at <c>:L171</c> and the handover at
/// <c>:L183</c>, and a port that issued it after the handover would still satisfy every
/// content-only assertion while reproducing none of the behaviour. <see cref="Events"/> is
/// therefore the primary instrument for the defect assertions, and it is modelled on
/// <c>ScriptedCarrierSurface</c>'s recorder in <c>TestDoubles.cs</c> - one log, one ordinal per
/// entry, tagged by verb - because <c>ChangesetCodec</c> never touches the describe or modify
/// surface that recorder covers, so its log has nothing to say about this path.
/// </para>
/// <para>
/// <b>SHARED CONTRACT.</b> <c>ChangesetCodecTransferTests.cs</c> and
/// <c>ChangesetCodecReceiveTests.cs</c> consume <see cref="ChunkResult"/>,
/// <see cref="ChildResult"/>, <see cref="AfterChunk"/>, <see cref="Chunks"/>,
/// <see cref="Children"/>, <see cref="Errors"/> and <see cref="CreatedFrom"/>. Those members are
/// load-bearing outside this file; <see cref="Events"/> and the ordinal helpers are additions.
/// </para>
/// </remarks>
internal sealed class RecordingTransferSink : IChangesetTransferSink
{
    private readonly List<(CarrierState? State, long ChunkCount, long ChunkIndex, bool FullState)>
        _chunks = [];

    private readonly List<(string ColumnName, CarrierState? State)> _children = [];
    private readonly List<(long ReturnCode, string ErrorText)> _errors = [];
    private readonly List<string> _createdFrom = [];
    private readonly List<RecordedSinkEvent> _events = [];

    /// <summary>Result the next chunk handover answers. Negative selects the failure arm.</summary>
    internal long ChunkResult { get; set; }

    /// <summary>Result the next child handover answers. Negative selects the failure arm.</summary>
    internal long ChildResult { get; set; }

    /// <summary>Runs after each chunk handover, so a test can mutate the source between chunks.</summary>
    /// <remarks>
    /// The legacy's handover at <c>:L183</c> and <c>:L219</c> runs while the source is still live, so
    /// a receiver genuinely can disturb it mid-sequence. That is not a hypothetical: it is the one
    /// route by which the stale filtered count of the codec's DECISION 3 becomes reachable.
    /// </remarks>
    internal Action? AfterChunk { get; set; }

    /// <summary>Every main-result handover, in order, with its payload cloned on receipt.</summary>
    internal IReadOnlyList<(CarrierState? State, long ChunkCount, long ChunkIndex, bool FullState)>
        Chunks => _chunks;

    /// <summary>Every drop-down child handover, in order.</summary>
    internal IReadOnlyList<(string ColumnName, CarrierState? State)> Children => _children;

    /// <summary>Every reported fault, in order, with its text verbatim.</summary>
    internal IReadOnlyList<(long ReturnCode, string ErrorText)> Errors => _errors;

    /// <summary>Every syntax the create-data handover was asked to build from.</summary>
    internal IReadOnlyList<string> CreatedFrom => _createdFrom;

    /// <summary>Every interaction of every kind, in one ordered log.</summary>
    internal IReadOnlyList<RecordedSinkEvent> Events => _events;

    /// <summary>
    /// The one-based ordinal of the first interaction of a kind, or zero when there was none.
    /// </summary>
    /// <param name="kind">The verb to look for.</param>
    /// <returns>The ordinal, or zero.</returns>
    internal int OrdinalOf(ChangesetSinkEventKind kind)
    {
        foreach (RecordedSinkEvent recorded in _events)
        {
            if (recorded.Kind == kind)
            {
                return recorded.Ordinal;
            }
        }

        return 0;
    }

    /// <summary>
    /// Whether the first interaction of one kind happened before the first of another.
    /// </summary>
    /// <param name="first">The kind expected earlier.</param>
    /// <param name="second">The kind expected later.</param>
    /// <returns>
    /// <see langword="true"/> when both were observed and the first precedes the second.
    /// </returns>
    internal bool Precedes(ChangesetSinkEventKind first, ChangesetSinkEventKind second)
    {
        int firstOrdinal = OrdinalOf(first);
        int secondOrdinal = OrdinalOf(second);

        return firstOrdinal > 0 && secondOrdinal > 0 && firstOrdinal < secondOrdinal;
    }

    /// <summary>How many interactions of a kind were observed.</summary>
    /// <param name="kind">The verb to count.</param>
    /// <returns>The count.</returns>
    internal int CountOf(ChangesetSinkEventKind kind)
    {
        int count = 0;

        foreach (RecordedSinkEvent recorded in _events)
        {
            if (recorded.Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    /// <inheritdoc/>
    public ValueTask<long> CreateDataAsync(string syntax, CancellationToken cancellationToken)
    {
        _createdFrom.Add(syntax);
        Record(ChangesetSinkEventKind.CreateData, syntax);

        return ValueTask.FromResult(0L);
    }

    /// <inheritdoc/>
    public ValueTask<long> SendChunkAsync(ChangesetChunk chunk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        // The state is cloned on receipt because the sender is entitled to drop its reference
        // immediately afterwards, reproducing `blbData = Blob("")` [:L187, :L223]. A test asserts on
        // this clone, so it cannot be fooled by a codec that kept and later mutated the original.
        _chunks.Add((
            chunk.State is null ? null : chunk.State.Clone(),
            chunk.ChunkCount,
            chunk.ChunkIndex,
            chunk.FullState));

        Record(
            ChangesetSinkEventKind.Chunk,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{chunk.ChunkIndex}/{chunk.ChunkCount} fullState={chunk.FullState} payload={chunk.State is not null}"));

        AfterChunk?.Invoke();

        return ValueTask.FromResult(ChunkResult);
    }

    /// <inheritdoc/>
    public ValueTask<long> SendChildChunkAsync(
        ChangesetChildPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _children.Add((
            payload.ColumnName,
            payload.State is null ? null : payload.State.Clone()));

        Record(ChangesetSinkEventKind.ChildChunk, payload.ColumnName);

        return ValueTask.FromResult(ChildResult);
    }

    /// <inheritdoc/>
    public void ReportError(long returnCode, string errorText)
    {
        _errors.Add((returnCode, errorText));
        Record(ChangesetSinkEventKind.Error, errorText);
    }

    private void Record(ChangesetSinkEventKind kind, string detail)
    {
        _events.Add(new RecordedSinkEvent(_events.Count + 1, kind, detail));
    }
}

/// <summary>
/// A substitutable payload codec whose answers are dictated by the test, and which records what each
/// carrier looked like at the moment its changeset was captured.
/// </summary>
/// <remarks>
/// <para>
/// <b>SCRIPTING IS THE ONLY ROUTE TO TWO LEGACY ARMS.</b> The real
/// <c>Buffers/ChangesetPayloadCodec</c> never fails and never partially succeeds over an in-memory
/// carrier, so <c>if ... GetChanges(ref blbData) &lt; 0 then</c> [<c>:L171</c>, <c>:L205</c>] and the
/// receive side's <c>if rtCode &gt; 1 then rtCode = -1</c>
/// [<c>n_cst_threading_task_sqlquery.sru:L250</c>] would both be dead code. Dictating the answer is
/// the only honest way to cover them, and a dictated FAILURE yields no payload because a legacy
/// <c>GetChanges</c> that failed has not filled its <c>ref blob</c>.
/// </para>
/// <para>
/// <b>THE CAPTURE OBSERVATIONS ARE THIS FILE'S WINDOW ONTO THE STAGING CARRIER.</b> DEFECT 1's
/// workaround stages each chunk through a temporary DataStore the codec creates for itself
/// [<c>:L152</c>], which no test can reach from outside - except here, because the codec presents
/// exactly that object to the encode step at <c>:L171</c>. <see cref="Captures"/> therefore records
/// the whole observable shape of whatever carrier was presented, and
/// <see cref="EncodedCarriers"/> keeps the references so a later assertion can look at the same
/// object again once the mid-loop clear has run. That is what makes the RowsDiscard-versus-Reset
/// discrimination of DEFECT 2 decidable rather than merely plausible.
/// </para>
/// <para>
/// <b>SHARED CONTRACT.</b> <see cref="EncodeResult"/>, <see cref="ApplyResult"/> and
/// <see cref="EncodedState"/> are consumed by <c>ChangesetCodecTransferTests.cs</c> and
/// <c>ChangesetCodecReceiveTests.cs</c>; everything else here is an addition.
/// </para>
/// </remarks>
internal sealed class ScriptedPayloadCodec : IChangesetPayloadCodec
{
    private readonly ChangesetPayloadCodec _real = new();
    private readonly List<RecordedCapture> _captures = [];
    private readonly List<DataWindowBufferStore> _encodedCarriers = [];
    private readonly List<CarrierState?> _appliedStates = [];

    /// <summary>Answer for every encode, or <see langword="null"/> to delegate to the real format.</summary>
    internal long? EncodeResult { get; set; }

    /// <summary>Answer for every apply, or <see langword="null"/> to delegate to the real format.</summary>
    internal long? ApplyResult { get; set; }

    /// <summary>
    /// State handed back when <see cref="EncodeResult"/> dictates the answer. A conforming state -
    /// three canonically ordered empty segments - so that a dictated SUCCESS produces something a
    /// receiver would accept, which is what keeps a dictated-success test about the arm under test
    /// rather than about segment validation.
    /// </summary>
    internal CarrierState EncodedState { get; set; } = new()
    {
        Processing = DwSqliteFixture.ProcessingValue,
        Segments =
        {
            new CarrierBufferSegment { Buffer = DwBuffer.Primary },
            new CarrierBufferSegment { Buffer = DwBuffer.Delete },
            new CarrierBufferSegment { Buffer = DwBuffer.Filter },
        },
    };

    /// <summary>
    /// Runs immediately AFTER the carrier's shape has been recorded and BEFORE the encode answers, so
    /// a test can plant a sentinel in the very object the codec is about to clear.
    /// </summary>
    /// <remarks>
    /// The arguments are the carrier presented for capture and the one-based capture ordinal.
    /// </remarks>
    internal Action<DataWindowBufferStore, int>? OnEncode { get; set; }

    /// <summary>Every capture, in order, with the carrier's whole observable shape.</summary>
    internal IReadOnlyList<RecordedCapture> Captures => _captures;

    /// <summary>
    /// The carriers presented for capture, in order and by reference - the staging carrier of
    /// DEFECT 1's workaround among them.
    /// </summary>
    internal IReadOnlyList<DataWindowBufferStore> EncodedCarriers => _encodedCarriers;

    /// <summary>Every state presented to <see cref="TryApply"/>, in order.</summary>
    internal IReadOnlyList<CarrierState?> AppliedStates => _appliedStates;

    /// <inheritdoc/>
    public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
    {
        ArgumentNullException.ThrowIfNull(source);

        int ordinal = _captures.Count + 1;

        // Recorded BEFORE the hook runs, so the observation is of the carrier as the codec presented
        // it - `dsTmp.GetChanges(ref blbData)` [:L171] or `Data.GetChanges(ref blbData)` [:L205].
        _captures.Add(RecordedCapture.Of(ordinal, source));
        _encodedCarriers.Add(source);

        OnEncode?.Invoke(source, ordinal);

        if (EncodeResult is not long dictated)
        {
            return _real.TryEncode(source, out state);
        }

        state = dictated < DataWindowBufferStore.DataStoreSuccess ? null : EncodedState;

        return dictated;
    }

    /// <inheritdoc/>
    public long TryApply(
        DataWindowBufferStore target,
        CarrierState? state,
        CarrierBaselineTrust baselineTrust)
    {
        ArgumentNullException.ThrowIfNull(target);

        _appliedStates.Add(state is null ? null : state.Clone());

        return ApplyResult ?? _real.TryApply(target, state, baselineTrust);
    }
}

/// <summary>
/// Builds carriers shaped like the golden-master fixture so that no test has to know the format.
/// </summary>
/// <remarks>
/// <para>
/// <b>C-C - THE FIXTURE'S OWN VALUES COME FROM THE TRANSCRIPTION, NOT FROM A RE-TYPED LITERAL.</b>
/// The data-object name, the processing kind, the column count and - most importantly - the sort
/// expression with its trailing space are read from <c>DwSqliteFixture</c>, which is this suite's
/// single transcription of <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd</c>. Re-typing
/// <c>sort="age A salary A "</c> here would create a second copy of an oracle value that must never
/// be copied, and a copy that lost the trailing space would silently change which side of DEFECT 1's
/// gate the fixture falls on.
/// </para>
/// <para>
/// <b>C-E - NO DATABASE.</b> Rows are admitted through the carrier's own append seam. Nothing here
/// opens a connection, touches a file or names a dialect.
/// </para>
/// </remarks>
internal static class FixtureCarrier
{
    /// <summary>
    /// The fixture's six columns
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L13</c>], taken from the transcription.
    /// </summary>
    internal const int ColumnCount = DwSqliteFixture.ColumnCount;

    /// <summary>The seed the <c>Filter!</c> rows are numbered from, so they are distinguishable.</summary>
    internal const long FilterSeed = 1000L;

    /// <summary>The seed the <c>Delete!</c> rows are numbered from.</summary>
    internal const long DeleteSeed = 2000L;

    /// <summary>
    /// Creates a carrier holding <paramref name="primaryRows"/> rows in <c>Primary!</c>,
    /// <paramref name="filteredRows"/> in <c>Filter!</c> and <paramref name="deletedRows"/> in
    /// <c>Delete!</c>, every row carrying the fixture's six columns and every row initially
    /// <see cref="ItemStatus.NotModified"/> - the state a freshly retrieved row is in.
    /// </summary>
    /// <param name="primaryRows">Rows to admit into <c>Primary!</c>.</param>
    /// <param name="filteredRows">Rows to admit into <c>Filter!</c>.</param>
    /// <param name="deletedRows">Rows to admit into <c>Delete!</c>.</param>
    /// <param name="processing">The processing kind, defaulting to the fixture's own.</param>
    /// <returns>The carrier.</returns>
    internal static DataWindowBufferStore Create(
        long primaryRows,
        long filteredRows = 0L,
        long deletedRows = 0L,
        long processing = DwSqliteFixture.ProcessingValue)
    {
        DataWindowBufferStore carrier = new()
        {
            Processing = new DataWindowProcessing(processing),
        };

        Populate(carrier, DwBuffer.Primary, primaryRows, 0L);
        Populate(carrier, DwBuffer.Filter, filteredRows, FilterSeed);
        Populate(carrier, DwBuffer.Delete, deletedRows, DeleteSeed);

        return carrier;
    }

    /// <summary>
    /// The fixture's declared sort expression, trailing space included
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>], read from the transcription.
    /// </summary>
    internal static ChangesetSourceDefinition SortedDefinition =>
        new()
        {
            DataObjectName = DwSqliteFixture.DataObjectName,
            Syntax = FixtureSyntax,
            SortExpression = DwSqliteFixture.SortExpression,
            FilterExpression = string.Empty,
        };

    /// <summary>The same definition with no sort, which takes the in-place branch.</summary>
    internal static ChangesetSourceDefinition UnsortedDefinition =>
        SortedDefinition with { SortExpression = string.Empty };

    /// <summary>
    /// A stand-in for <c>Describe("DataWindow.Syntax")</c> [<c>:L104</c>, <c>:L156</c>]. Only its
    /// identity matters to this codec, which passes it through untouched.
    /// </summary>
    private static string FixtureSyntax =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"release {DwSqliteFixture.Release};");

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
            // declared `ID INTEGER PRIMARY KEY NOT NULL` - annotated /*自增列*/, the auto-increment
            // column - and `AGE INT NOT NULL`
            // [ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469], and the production path reads
            // them through Microsoft.Data.Sqlite, which yields long for a SQLite INTEGER - it has no int
            // path at all. Seeding int here would additionally make every round-trip comparison in this
            // service assert against the DECLARED WIDTH rather than the value: common.v1.AnyValue
            // publishes ONE signed integer arm, so an int travels as it and returns as long, and the
            // widening - which is the contract's documented decision, asserted directly by
            // ChangesetPayloadCodecTests.EveryMappedValueTypeRoundTripsOntoItsDeclaredCarrierType -
            // would show up here as an unrelated inequality between two boxed ones.
            _ = carrier.SetItemValue(rowNumber, DwSqliteFixture.IdColumnNumber, dwBuffer, identifier);
            _ = carrier.SetItemValue(
                rowNumber,
                DwSqliteFixture.NameColumnNumber,
                dwBuffer,
                string.Create(CultureInfo.InvariantCulture, $"name-{identifier}"));
            _ = carrier.SetItemValue(
                rowNumber,
                DwSqliteFixture.AgeColumnNumber,
                dwBuffer,
                identifier % 90L);
            _ = carrier.SetItemValue(
                rowNumber,
                DwSqliteFixture.AddressColumnNumber,
                dwBuffer,
                string.Create(CultureInfo.InvariantCulture, $"address-{identifier}"));
            _ = carrier.SetItemValue(
                rowNumber,
                DwSqliteFixture.SalaryColumnNumber,
                dwBuffer,
                decimal.Divide(identifier, 4m));
            _ = carrier.SetItemValue(
                rowNumber,
                DwSqliteFixture.BirthColumnNumber,
                dwBuffer,
                new DateOnly(1980, 1, 1).AddDays((int)identifier));

            // A retrieved row's originals equal its currents, which is what ResetUpdate establishes on
            // the receiving side [n_cst_threading_task_sqlquery.sru:L240].
            carrier.RowAt(rowNumber, dwBuffer).Baseline();
        }
    }
}

/// <summary>
/// The chunk arithmetic, the branch guards and the pure decisions - every one of them exercisable
/// with no carrier, no thread, no clock and no database at all.
/// </summary>
public sealed class ChangesetCodecDecisionTests
{
    /// <summary>
    /// <c>nChunkCnt = Ceiling((RowCount() + FilteredCount()) / _nChunkSize)</c> [<c>:L145</c>].
    /// THE SUM SPANS BOTH BUFFERS: filtered rows are in the dividend.
    /// </summary>
    public static TheoryData<long, long, long, long> ChunkCountCases =>
        new()
        {
            // primary, filtered, chunkSize, expected
            { 0L, 0L, 10L, 0L },        // the empty-result arm [:L229]
            { 1L, 0L, 10L, 1L },        // one row under one chunk
            { 10L, 0L, 10L, 1L },       // EXACT division, one chunk
            { 11L, 0L, 10L, 2L },       // a remainder of one
            { 19L, 0L, 10L, 2L },       // a remainder of nine
            { 20L, 0L, 10L, 2L },       // EXACT division, two chunks
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
    /// The four combinations of DEFECT 1's gate
    /// <c>if nChunkCnt &gt; 1 and sProp &lt;&gt; "?" and sProp &lt;&gt; ""</c> [<c>:L151</c>], plus the
    /// boundary values that show the two sort tests are not one test.
    /// </summary>
    public static TheoryData<long, string, bool> TemporaryCarrierCases =>
        new()
        {
            // chunkCount, sortExpression, expected
            { 1L, DwSqliteFixture.SortExpression, false },  // single chunk + SORTED
            { 2L, DwSqliteFixture.SortExpression, true },   // multi chunk + SORTED - ENGAGES
            { 2L, "", false },                              // multi chunk + UNSORTED
            { 2L, "?", false },                             // multi chunk + the "?" marker
            { 3L, "age A", true },
            { 1L, "?", false },
            { 1L, "", false },
            { 0L, "age A", false },                         // no chunks at all
            { 2L, " ", true },                              // a single space is neither marker
        };

    /// <summary>
    /// The four-part gate on clearing sort and filter [<c>:L550</c>, <c>:L559</c>, <c>:L560</c>].
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
    /// <c>if sProp &lt;&gt; "yes" then continue</c> [<c>:L116</c>] and
    /// <c>if sProp &lt;&gt; "!" and sProp &lt;&gt; "?" then</c> [<c>:L118</c>].
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
    public void FilteredRowsAreInTheDividendSoPrimaryAloneWouldUnderChunk()
    {
        // `Ceiling((Data.RowCount() + Data.FilteredCount()) / _nChunkSize)` [:L145]. Stated as a
        // contrast because this is the single easiest line in the codec to get wrong: counting the
        // primary buffer alone under-chunks and the tail of the FILTER buffer is then never emitted at
        // all - a silent data loss no row-count assertion on the primary buffer would notice.
        const long primaryRows = 10L;
        const long filteredRows = 15L;
        const long chunkSize = 10L;

        long spanningBoth = ChangesetCodec.ComputeChunkCount(primaryRows, filteredRows, chunkSize);
        long primaryOnly = ChangesetCodec.ComputeChunkCount(primaryRows, 0L, chunkSize);

        Assert.Equal(3L, spanningBoth);
        Assert.Equal(1L, primaryOnly);
        Assert.True(spanningBoth > primaryOnly);
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
        // PRESERVED LEGACY DEFECT 1 [:L148] - this is the gate that decides whether the workaround for
        // "a sorted DataWindow spanning more than one block may lose rows through SetChanges" engages.
        // The assertion is that the WORKAROUND is selected, never that rows survive without it.
        ChangesetSourceDefinition definition =
            FixtureCarrier.SortedDefinition with { SortExpression = sortExpression };

        Assert.Equal(expected, ChangesetCodec.RequiresTemporaryCarrier(chunkCount, definition));
    }

    [Fact]
    public void RequiresTemporaryCarrierTreatsAnAbsentSortExpressionAsAbsent()
    {
        // Defensive: a null describe result must read as ABSENT rather than throwing, because the guard
        // it feeds decides which branch of DEFECT 1's workaround runs [:L151].
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
            DataObjectName = hasDataObject ? DwSqliteFixture.DataObjectName : string.Empty,
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
    public void ClearSortAndFilterIsThePortOfBothLegacyCalls()
    {
        // `data.SetSort("")` [:L579] and `data.SetFilter("")` [:L580]. BOTH calls, and both to EMPTY
        // rather than to the "?" marker - an empty expression is a real answer meaning "no condition",
        // which is exactly what the legacy installs.
        //
        // C-K - THE TWO ARMS DISAGREE ON PURPOSE AND MUST NOT BE UNIFIED. The NOTE guarding this arm
        // [:L577-L578] says the SetChanges transfer style does not need sort and filter conditions,
        // whereas the crosstab arm one line above SYNCHRONIZES them under its own NOTE [:L562-L563]
        // because SetFullState does need them. FullStateCodecTests asserts that other side. Neither
        // arm is the "right" one; the disagreement is the behaviour.
        //
        // 0.8.5 - the legacy NOTE also remarks that the conditions can hurt performance. That remark is
        // deliberately NOT asserted: no performance claim is made here or anywhere in this file.
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
        Assert.Equal(DwSqliteFixture.SortExpression, original.SortExpression);
        Assert.Equal("age > 30", original.FilterExpression);
    }

    [Fact]
    public void TheChangesetArmClearsExactlyWhereTheFullStateArmSynchronizes()
    {
        // C-K, stated as an executable assertion so the opposition cannot be "tidied away". The gate at
        // :L560 dispatches on the carrier's processing kind: the crosstab and composite arm
        // synchronizes [:L561-L575] and the `case else` arm clears [:L576-L580]. Flipping the
        // full-state flag - and NOTHING else - must flip the answer.
        ChangesetSourceDefinition definition = FixtureCarrier.SortedDefinition;

        Assert.True(
            ChangesetCodec.ShouldClearSortAndFilter(
                definition,
                isMainThread: false,
                receiverNeedsCreatedObject: false,
                requiresFullStateTransfer: false));

        Assert.False(
            ChangesetCodec.ShouldClearSortAndFilter(
                definition,
                isMainThread: false,
                receiverNeedsCreatedObject: false,
                requiresFullStateTransfer: true));
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
    [InlineData(DwSqliteFixture.ProcessingValue, true)]  // the fixture's own kind [dw_sqlite.srd:L3]
    [InlineData(0L, true)]                               // an unassigned carrier is `case else` too
    [InlineData(2L, true)]
    [InlineData(3L, true)]
    [InlineData(DataWindowProcessing.CrosstabValue, false)]   // crosstab [:L94]
    [InlineData(DataWindowProcessing.CompositeValue, false)]  // composite [:L94]
    [InlineData(6L, true)]
    public void HandlesCarrierIsTheCaseElseArmOfTheSelector(long processing, bool expected)
    {
        DataWindowBufferStore carrier = FixtureCarrier.Create(1L, processing: processing);

        Assert.Equal(expected, ChangesetCodec.HandlesCarrier(carrier));
    }

    [Fact]
    public void FoldFilterBufferIntoPrimaryAppendsAtTheOneBasedTail()
    {
        // `Data.RowsMove(1,Data.FilteredCount(),Filter!,Data,Data.RowCount() + 1,Primary!)` [:L109],
        // under its NOTE at :L106-L108. A SELF-MOVE: source and destination are the same carrier.
        //
        // R9 item 4 - THE APPEND IDIOM. `RowCount() + 1` is one past the last valid row in a one-based
        // domain, and the folded rows must therefore land AFTER every existing primary row. The seeds
        // make that assertable: primary rows are numbered from 0 and filtered rows from 1000, so an
        // off-by-one insertion point would put 1001 and 1002 at rows 3 and 4 while leaving the row
        // COUNT correct - which is precisely the class of defect a count assertion cannot catch.
        DataWindowBufferStore carrier = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            ChangesetCodec.FoldFilterBufferIntoPrimary(carrier));

        Assert.Equal(5L, carrier.RowCount());
        Assert.Equal(0L, carrier.FilteredCount());

        // The three original primary rows keep their positions...
        Assert.Equal(1L, carrier.GetItemValue(1L, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary));
        Assert.Equal(2L, carrier.GetItemValue(2L, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary));
        Assert.Equal(3L, carrier.GetItemValue(3L, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary));

        // ...and the two folded rows are at the TAIL, in their original order.
        Assert.Equal(
            FixtureCarrier.FilterSeed + 1L,
            carrier.GetItemValue(4L, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary));
        Assert.Equal(
            FixtureCarrier.FilterSeed + 2L,
            carrier.GetItemValue(5L, DwSqliteFixture.IdColumnNumber, DwBuffer.Primary));
    }

    [Fact]
    public void FoldFilterBufferIntoPrimaryIsANoOpWhenNothingIsFiltered()
    {
        // `1, FilteredCount()` becomes the range 1 through 0 - end before start - which PowerBuilder
        // answers with -1 while moving nothing. The legacy IGNORES that answer (C-B), so the code is
        // returned rather than raised.
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
        // PRESERVED LEGACY DEFECT 1 [:L148], workaround part one:
        // `if Data.DataObject <> "" then dsTmp.DataObject = ... else dsTmp.Create(...)` [:L153-L157].
        // BOTH ARMS are asserted, because the fallback is what makes the workaround usable on a carrier
        // built from generated syntax rather than from a named data object.
        DataWindowBufferStore named = ChangesetCodec.CreateTemporaryCarrier(
            FixtureCarrier.SortedDefinition,
            DwSqliteFixture.Processing,
            out TemporaryCarrierProvenance namedShape);

        Assert.Equal(TemporaryCarrierProvenance.DataObjectName, namedShape);

        DataWindowBufferStore fromSyntax = ChangesetCodec.CreateTemporaryCarrier(
            FixtureCarrier.SortedDefinition with { DataObjectName = string.Empty },
            DwSqliteFixture.Processing,
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
        // R9 item 2. A zero index would make the receive side's `if current = 1` reset test
        // [n_cst_threading_task_sqlquery.sru:L192] never fire, and the target would accumulate rows
        // across sequences with nothing failing loudly.
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
    [InlineData(DwSqliteFixture.SortExpression, true, true)]
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

/// <summary>
/// The two documented transfer defects this codec owns, the chunk protocol as it reaches the wire,
/// the two verbatim error texts, and the receive-side behaviours - all pinned as EXPECTED behaviour.
/// </summary>
/// <remarks>
/// <para>
/// <b>READ THE HEADER OF THIS FILE BEFORE CHANGING ANYTHING HERE.</b> Every assertion below that
/// mentions a defect asserts that THE DEFECT AND ITS WORKAROUND ARE STILL PRESENT. None of them
/// asserts that rows survive, and none of them asserts that <c>Reset</c> is safe. A failure here means
/// the codec stopped reproducing the legacy, which under C-B is a regression however much it looks
/// like an improvement.
/// </para>
/// <para>
/// <b>THE INSTRUMENTS.</b> <c>RecordingTransferSink.Events</c> gives an ORDERED log of every handover
/// and every reported fault, and <c>ScriptedPayloadCodec.Captures</c> gives the shape of whatever
/// carrier the codec presented at each <c>GetChanges</c> [<c>:L171</c>, <c>:L205</c>]. Between them
/// every step of the per-chunk sequence is observable IN POSITION rather than merely in aggregate,
/// which is what the two defects require: both are statements about WHERE a step happens.
/// </para>
/// <para>
/// <b>0.6.7 - DETERMINISM.</b> The clock is a <c>FakeTimeProvider</c> that is never advanced, and the
/// inter-chunk yield is driven at <see cref="TimeSpan.Zero"/>, which short-circuits without arming a
/// timer. Chunk boundaries are therefore decided purely by the row counts and the chunk size, and no
/// case depends on wall-clock time or on test-execution order.
/// </para>
/// </remarks>
public sealed class ChangesetCodecDefectTests
{
    /// <summary>
    /// The chunk sizes the multi-chunk cases use. Small on purpose: the 1000 floor is the query task's
    /// guard at <c>:L410</c>, not this codec's, so a handful of rows can drive a many-chunk sequence.
    /// </summary>
    private const long SmallChunkSize = 2L;

    /// <summary>
    /// Every combination of DEFECT 1's gate, driven END TO END through
    /// <c>TransferAsync</c> rather than through the predicate alone, so the assertion is about the
    /// branch actually taken.
    /// </summary>
    public static TheoryData<string, long, long, string, bool> WorkaroundGateCases =>
        new()
        {
            // because, primaryRows, chunkSize, sortExpression, workaroundEngages
            {
                "single chunk + sorted",
                2L,
                10L,
                DwSqliteFixture.SortExpression,
                false
            },
            {
                "multi chunk + sorted - THE FIXTURE'S OWN SHAPE",
                5L,
                SmallChunkSize,
                DwSqliteFixture.SortExpression,
                true
            },
            {
                "multi chunk + unsorted",
                5L,
                SmallChunkSize,
                "",
                false
            },
            {
                "multi chunk + the \"?\" not-available marker, which is not a sort",
                5L,
                SmallChunkSize,
                ChangesetSourceDefinition.DescribeNotApplicable,
                false
            },
        };

    /// <summary>
    /// Row and chunk shapes that produce a known chunk count, used for the wire-protocol assertions.
    /// </summary>
    public static TheoryData<long, long, long, long> WireProtocolCases =>
        new()
        {
            // primaryRows, filteredRows, chunkSize, expectedChunkCount
            { 1L, 0L, 10L, 1L },   // one chunk
            { 4L, 0L, SmallChunkSize, 2L },   // EXACT division
            { 5L, 0L, SmallChunkSize, 3L },   // a remainder
            { 2L, 2L, SmallChunkSize, 2L },   // filtered rows in the dividend, exact
            { 3L, 2L, SmallChunkSize, 3L },   // filtered rows in the dividend, a remainder
            { 0L, 3L, SmallChunkSize, 2L },   // filtered rows only
        };

    /// <summary>Both send branches, so every branch-agnostic assertion runs on each.</summary>
    public static TheoryData<bool> BothBranches =>
        new()
        {
            true,   // sorted - the temporary-carrier branch of DEFECT 1's workaround
            false,  // unsorted - the in-place branch
        };

    // ==========================================================================================
    //  PHASE 1 - THE CHUNK PROTOCOL AS IT REACHES THE WIRE
    // ==========================================================================================

    [Theory]
    [MemberData(nameof(WireProtocolCases))]
    public async Task EveryEmittedChunkCarriesOneBasedCountersAndFullStateFalse(
        long primaryRows,
        long filteredRows,
        long chunkSize,
        long expectedChunkCount)
    {
        // `tasking.Event OnDataChunk(ref blbData,nChunkCnt,nChunkIdx,false)` [:L183, :L219].
        //
        // THREE THINGS ARE BEING PINNED AT ONCE.
        //  1. The count on the wire is the arithmetic of :L145, filtered rows INCLUDED in the dividend.
        //  2. `nChunkIdx` runs `1 to nChunkCnt` [:L158, :L194] - ONE-BASED, and the last index EQUALS
        //     the count. The receive side tests `current = 1` to decide whether to clear
        //     [n_cst_threading_task_sqlquery.sru:L192] and `count = current` to decide whether to clear
        //     the update flags [:L198]; a zero-based index would silently disable both.
        //  3. The fourth argument is the literal `false` at EVERY site on this path. That boolean is
        //     what routes the payload on the receive side [n_cst_threading_task_sqlquery.sru:L189], so a
        //     true value here would send a changeset down the SetFullState path.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows, filteredRows);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, chunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(expectedChunkCount, outcome.ChunkCount);
        Assert.Equal(expectedChunkCount, outcome.ChunksSent);
        Assert.Equal((int)expectedChunkCount, sink.Chunks.Count);

        long expectedIndex = 1L;

        foreach ((CarrierState? state, long count, long current, bool fullState) in sink.Chunks)
        {
            Assert.False(fullState);
            Assert.Equal(expectedChunkCount, count);
            Assert.Equal(expectedIndex, current);
            Assert.NotNull(state);

            expectedIndex++;
        }

        // `current` ran 1..count inclusive and stopped exactly at the count.
        Assert.Equal(expectedChunkCount + 1L, expectedIndex);
        Assert.Equal(expectedChunkCount, sink.Chunks[^1].ChunkIndex);
    }

    [Fact]
    public async Task AnEmptyCarrierStillEmitsOneEmptyChunkWithOneBasedCountersAndFullStateFalse()
    {
        // `else tasking.Event OnDataChunk(ref blbData,1,1,false)` [:L229-L231]. The arithmetic answered
        // ZERO, yet the counters on the wire are 1 and 1 and the payload is EMPTY - which is precisely
        // the shape the receive side's empty-payload arm exists to react to
        // [n_cst_threading_task_sqlquery.sru:L207-L210]. The handover result is NOT tested on this arm
        // alone, unlike :L183 and :L219, and that omission is reproduced rather than tidied up (C-B).
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new() { ChunkResult = -1L };
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 0L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(1L, outcome.ChunkCount);
        Assert.Equal(1L, outcome.ChunksSent);
        Assert.False(outcome.UsedTemporaryCarrier);
        Assert.Null(outcome.TemporaryCarrierShape);

        (CarrierState? state, long count, long current, bool fullState) = Assert.Single(sink.Chunks);

        Assert.Null(state);
        Assert.Equal(1L, count);
        Assert.Equal(1L, current);
        Assert.False(fullState);

        // A negative handover result on this arm is ignored, so no fault is reported.
        Assert.Empty(sink.Errors);
    }

    [Theory]
    [MemberData(nameof(BothBranches))]
    public async Task ThePayloadIsClearedAfterEveryHandover(bool sorted)
    {
        // `blbData = Blob("")` [:L187, :L223]. The legacy drops the blob the instant the handover
        // returns, and docs/PB多线程绕坑提示.md is why the shape matters at all: hazard 1 warns that a
        // worker thread synchronously calling a main-thread function or event that returns a string or
        // a blob can fault, which is what forced the `ref blob` out-parameter the clear then empties.
        //
        // Observable in two independent ways, and both are asserted: the codec reports that it held no
        // payload when it returned, and each recorded chunk is an INDEPENDENT CLONE, so a codec that
        // retained and later mutated the original could not pass.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, DefinitionFor(sorted), SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.PayloadCleared);
        Assert.Equal(sorted, outcome.UsedTemporaryCarrier);
        Assert.Equal(3, sink.Chunks.Count);

        // Every handed-over payload is a distinct object carrying a distinct segment roster, which is
        // what "the sender no longer owns it" means in managed terms.
        List<CarrierState> delivered = [];

        foreach ((CarrierState? state, long _, long _, bool _) in sink.Chunks)
        {
            Assert.NotNull(state);
            delivered.Add(state);
        }

        Assert.Equal(delivered.Count, delivered.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(BothBranches))]
    public async Task CancellationBetweenChunksAnswersCancelledAndStopsEmission(bool sorted)
    {
        // `if nChunkIdx < nChunkCnt then if Not of_Wait(0.02) then return RetCode.CANCELLED`
        // [:L188-L190, :L224-L226]. The cooperative inter-chunk yield is the ONLY place the send loop
        // can be interrupted once it has started, and a false answer from the wait becomes
        // RetCode.CANCELLED - never OK, and never an exception. A caller written against the legacy
        // reacts to a code and would not catch an OperationCanceledException.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        // Cancel as the FIRST handover returns, so the yield that follows it observes the cancellation.
        sink.AfterChunk = cancellation.Cancel;

        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 6L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, DefinitionFor(sorted), SmallChunkSize),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(3L, outcome.ChunkCount);

        // EMISSION STOPPED rather than completed: one chunk out of three, and the sequence ended there.
        Assert.Equal(1L, outcome.ChunksSent);
        Assert.Single(sink.Chunks);
        Assert.Equal(1, sink.CountOf(ChangesetSinkEventKind.Chunk));

        // A cancellation is not a fault and is not reported as one.
        Assert.Empty(sink.Errors);
        Assert.Equal(0, sink.OrdinalOf(ChangesetSinkEventKind.Error));
        Assert.True(outcome.PayloadCleared);
    }

    [Theory]
    [MemberData(nameof(BothBranches))]
    public async Task TheYieldIsSkippedAfterTheFinalChunkSoALateCancellationStillSucceeds(bool sorted)
    {
        // The condition at :L188 and :L224 is STRICTLY `nChunkIdx < nChunkCnt`, so there is no yield
        // after the last chunk. A cancellation that arrives as the final handover returns therefore
        // cannot turn a completed transfer into a cancelled one - the loop has nothing left to wait on.
        using CancellationTokenSource cancellation = new();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 4L);

        sink.AfterChunk = () =>
        {
            if (sink.Chunks.Count == 2)
            {
                cancellation.Cancel();
            }
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, DefinitionFor(sorted), SmallChunkSize),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(2L, outcome.ChunkCount);
        Assert.Equal(2L, outcome.ChunksSent);
    }

    [Fact]
    public async Task CancellationBeforeTheChunkSequenceEmitsNothingAtAll()
    {
        // `if of_IsCancelled() then return RetCode.CANCELLED` [:L142] - the guard that stands between
        // the drop-down child loop and the chunk sequence.
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            cancellation.Token);

        Assert.Equal(RetCode.CANCELLED, outcome.ReturnCode);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.Empty(sink.Chunks);
        Assert.Empty(sink.Events);

        // The source is untouched, because the cancellation preceded every mutation.
        Assert.Equal(5L, source.RowCount());
    }

    // ==========================================================================================
    //  PHASE 2 - PRESERVED LEGACY DEFECT 1 [:L148]: SORTED MULTI-CHUNK ROW LOSS
    //  ------------------------------------------------------------------------------------------
    //  //FIXME                                                                          [:L147]
    //  //带排序的DW大于1块时使用SetChanges可能会丢失行                                    [:L148]
    //  //采用临时的DS来绕开此问题                                                        [:L149]
    //
    //  "When a sorted DataWindow spans more than one block, using SetChanges may lose rows; a
    //  temporary DataStore is used to work around it." THE WORKAROUND IS THE BEHAVIOUR. Nothing below
    //  asserts that rows are never lost - the legacy makes no such claim and neither does the port.
    //  What is asserted is that the workaround still engages on exactly the legacy's conditions and
    //  still stages each chunk exactly the way the legacy stages it.
    // ==========================================================================================

    [Theory]
    [MemberData(nameof(WorkaroundGateCases))]
    public async Task TheSortedMultiChunkWorkaroundEngagesOnlyOnAllThreeConditions(
        string because,
        long primaryRows,
        long chunkSize,
        string sortExpression,
        bool workaroundEngages)
    {
        // PRESERVED LEGACY DEFECT 1 [:L148]. The gate is
        // `if nChunkCnt > 1 and sProp <> "?" and sProp <> "" then` [:L151], where sProp is
        // `Data.Describe("DataWindow.Table.Sort")` [:L150].
        //
        // THE TWO SORT TESTS ARE NOT ONE TEST, which is why the "?" row exists. A single question mark
        // is PowerBuilder's NOT-AVAILABLE marker - the property could not be answered - whereas an
        // empty string is a real answer meaning "there is no sort". The legacy compares against each
        // separately; a null-or-empty test would collapse them and lose the marker's identity.
        Assert.False(string.IsNullOrEmpty(because));

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                source,
                FixtureCarrier.SortedDefinition with { SortExpression = sortExpression },
                chunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(workaroundEngages, outcome.UsedTemporaryCarrier);
        Assert.Equal(workaroundEngages, outcome.TemporaryCarrierShape is not null);
    }

    // The provenance is chosen inside rather than carried as a theory argument: the enum is `internal`
    // and this class is `public`, so a parameter of that type would be less accessible than the method
    // declaring it. Passing the CONDITION and deriving the expectation keeps both arms in one case.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheStagingCarrierTakesItsShapeFromTheDataObjectNameOrElseFromTheSyntax(
        bool hasDataObject)
    {
        TemporaryCarrierProvenance expected = hasDataObject
            ? TemporaryCarrierProvenance.DataObjectName
            : TemporaryCarrierProvenance.Syntax;

        // PRESERVED LEGACY DEFECT 1 [:L148], workaround part one, BOTH ARMS:
        //     dsTmp = Create datastore                                        [:L152]
        //     if Data.DataObject <> "" then
        //         dsTmp.DataObject = Data.DataObject                          [:L154]
        //     else
        //         dsTmp.Create(Data.Describe("DataWindow.Syntax"))            [:L156]
        //
        // The fallback is not decoration: a carrier built from generated syntax has no data object name
        // at all, and without this arm the workaround would have no shape to stage through.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetSourceDefinition definition = FixtureCarrier.SortedDefinition with
        {
            DataObjectName = hasDataObject ? DwSqliteFixture.DataObjectName : string.Empty,
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, definition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(expected, outcome.TemporaryCarrierShape);
    }

    [Fact]
    public async Task EachChunkStagesExactlyItsOwnRowsAndTheStagingCarrierNeverAccumulates()
    {
        // PRESERVED LEGACY DEFECT 1 [:L148], workaround part two, the per-chunk sequence:
        //     nRowCnt = Min(_nChunkSize,Data.RowCount())                      [:L159]
        //     Data.RowsMove(1,nRowCnt,Primary!,dsTmp,1,Primary!)              [:L160]
        //
        // THE POINT OF THE WORKAROUND is that the rows a chunk carries are the ONLY rows the staging
        // carrier ever holds, so each capture must show exactly this chunk's rows and never a
        // carried-over row from the previous one. The identifiers make that assertable: had the staging
        // carrier accumulated, capture 2 would report four rows instead of two.
        //
        // `Min` is re-evaluated every iteration because the source SHRINKS as rows move out, which is
        // why the final chunk reports one row rather than two.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(3, payload.Captures.Count);

        // Min(2, 5) then Min(2, 3) then Min(2, 1).
        Assert.Equal([2L, 2L, 1L], payload.Captures.Select(capture => capture.PrimaryRows));

        // The rows are taken from the HEAD of the source buffer, in order, and each chunk carries only
        // its own - which is the whole of the staging behaviour.
        Assert.Equal<object?>([1L, 2L], payload.Captures[0].PrimaryIdentifiers);
        Assert.Equal<object?>([3L, 4L], payload.Captures[1].PrimaryIdentifiers);
        Assert.Equal<object?>([5L], payload.Captures[2].PrimaryIdentifiers);

        // Every capture came from the SAME staging object [:L152 creates exactly one], and it is not
        // the source: the blob is taken from dsTmp at :L171, never from Data.
        Assert.Single(payload.EncodedCarriers.Distinct());
        Assert.DoesNotContain(source, payload.EncodedCarriers);

        // The source was drained by the moves, not copied.
        Assert.Equal(0L, source.RowCount());
    }

    [Fact]
    public async Task EveryStagedRowIsStampedDataModifiedThroughTheRowStatusColumn()
    {
        // PRESERVED LEGACY DEFECT 1 [:L148], workaround part two, the stamping:
        //     for nRow = 1 to nRowCnt
        //         dsTmp.SetItemStatus(nRow,0,Primary!,DataModified!)          [:L162]
        //     next
        //
        // COLUMN ZERO IS NOT A COLUMN; it addresses THE ROW ITSELF. Stamping is what makes the moved
        // rows visible to the changeset extraction at :L171, so exactly this chunk's rows are the ones
        // captured - and a port that wrote a per-column status instead would capture nothing at all.
        //
        // The rows arrive NotModified, because that is the state a freshly retrieved row is in, so the
        // observed DataModified is something the codec did rather than something the fixture supplied.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 4L);

        Assert.Equal(
            ItemStatus.NotModified,
            source.GetItemStatus(
                ItemStatusMachine.FirstRowNumber,
                ItemStatusMachine.RowStatusColumn,
                DwBuffer.Primary));

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(2, payload.Captures.Count);

        foreach (RecordedCapture capture in payload.Captures)
        {
            Assert.Equal(2, capture.PrimaryRowStatuses.Count);
            Assert.All(
                capture.PrimaryRowStatuses,
                status => Assert.Equal(ItemStatus.DataModified, status));
        }
    }

    [Fact]
    public async Task TheFilterBufferTopsUpAChunkOnlyWhenThePrimaryBufferCouldNotFillIt()
    {
        // PRESERVED LEGACY DEFECT 1 [:L148], workaround part two, the conditional filter move:
        //     if nRowCnt < _nChunkSize and Data.FilteredCount() > 0 then      [:L164]
        //         nFilterCnt = Min(_nChunkSize - nRowCnt,Data.FilteredCount())[:L165]
        //         Data.RowsMove(1,nFilterCnt,Filter!,dsTmp,1,Filter!)         [:L166]
        //         for nRow = 1 to nFilterCnt
        //             dsTmp.SetItemStatus(nRow,0,Filter!,DataModified!)       [:L168]
        //         next
        //     end if
        //
        // BOTH HALVES OF THE GUARD MATTER. Because the top-up runs only when the primary buffer could
        // not fill the chunk, THE PRIMARY BUFFER ALWAYS DRAINS FIRST - and the filtered rows travel as
        // FILTERED rows, in the Filter! buffer, rather than being folded into the primary buffer.
        // (Folding is the other path entirely, gated on the receiver needing a created object [:L109].)
        //
        // Three rows primary, two filtered, chunk size two, so the arithmetic of :L145 gives three
        // chunks and the three iterations exercise all three shapes of the guard.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.False(outcome.FoldedFilterBuffer);
        Assert.Equal(3, payload.Captures.Count);

        // Chunk 1: Min(2,3) = 2 primary rows fill the chunk exactly, so `nRowCnt < _nChunkSize` is
        // FALSE and the filter buffer is not touched.
        Assert.Equal(2L, payload.Captures[0].PrimaryRows);
        Assert.Equal(0L, payload.Captures[0].FilteredRows);

        // Chunk 2: Min(2,1) = 1 primary row leaves room, and Min(2 - 1, 2) = 1 filtered row tops it up.
        Assert.Equal(1L, payload.Captures[1].PrimaryRows);
        Assert.Equal(1L, payload.Captures[1].FilteredRows);
        Assert.Equal<object?>([3L], payload.Captures[1].PrimaryIdentifiers);
        Assert.Equal<object?>(
            [FixtureCarrier.FilterSeed + 1L],
            payload.Captures[1].FilterIdentifiers);

        // Chunk 3: no primary rows remain, so Min(2 - 0, 1) = 1 takes the last filtered row.
        Assert.Equal(0L, payload.Captures[2].PrimaryRows);
        Assert.Equal(1L, payload.Captures[2].FilteredRows);
        Assert.Equal<object?>(
            [FixtureCarrier.FilterSeed + 2L],
            payload.Captures[2].FilterIdentifiers);

        // The topped-up rows are stamped under Filter!, not under Primary! [:L168].
        Assert.All(
            payload.Captures[1].FilterRowStatuses,
            status => Assert.Equal(ItemStatus.DataModified, status));
        Assert.All(
            payload.Captures[2].FilterRowStatuses,
            status => Assert.Equal(ItemStatus.DataModified, status));

        Assert.Equal(0L, source.RowCount());
        Assert.Equal(0L, source.FilteredCount());
    }

    [Fact]
    public async Task TheFixturesOwnSortValueSatisfiesTheGateTrailingSpaceIncluded()
    {
        // WHY DEFECT 1's PATH IS NOT HYPOTHETICAL, stated as an executable assertion.
        //
        // The only updatable DataWindow in the repository declares
        //     ... updatewhere=1 updatekeyinplace=no  sort="age A salary A "
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14] and processing=1 [:L3]. So the golden-master
        // fixture is BOTH on this codec's arm AND sorted, and the moment its row count plus filtered
        // count exceeds the chunk size the workaround runs. This is a MAIN PATH, not an edge case.
        //
        // THE TRAILING SPACE IS PART OF THE VALUE. It is preserved in DwSqliteFixture and consumed from
        // there rather than re-typed (C-C), and it matters here: the gate tests the string against "?"
        // and against "" [:L151], and a transcription that trimmed the value would still pass the gate,
        // whereas one that mistook the value for absent would silently disable the workaround.
        Assert.EndsWith(" ", DwSqliteFixture.SortExpression, StringComparison.Ordinal);
        Assert.NotEmpty(DwSqliteFixture.SortExpression);

        // The two answers the gate screens - the "?" marker and the empty string - both read as ABSENT,
        // and the fixture's value reads as PRESENT. Asserted through the predicate the gate actually
        // consults, so the distinction is measured where it is used.
        Assert.True(
            (FixtureCarrier.SortedDefinition with
            {
                SortExpression = ChangesetSourceDefinition.DescribeNotApplicable,
            }).SortIsAbsent);
        Assert.True(
            (FixtureCarrier.SortedDefinition with { SortExpression = string.Empty }).SortIsAbsent);

        ChangesetSourceDefinition definition = FixtureCarrier.SortedDefinition;

        Assert.Equal(DwSqliteFixture.SortExpression, definition.SortExpression);
        Assert.False(definition.SortIsAbsent);
        Assert.True(ChangesetCodec.RequiresTemporaryCarrier(2L, definition));

        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new();

        // The fixture's own processing kind, so the carrier really is on this codec's arm.
        DataWindowBufferStore source = FixtureCarrier.Create(
            primaryRows: 5L,
            processing: DwSqliteFixture.ProcessingValue);

        Assert.True(ChangesetCodec.HandlesCarrier(source));

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, definition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.Equal(TemporaryCarrierProvenance.DataObjectName, outcome.TemporaryCarrierShape);
    }

    // ==========================================================================================
    //  PHASE 3 - PRESERVED LEGACY DEFECT 2 [:L176]: THE RESET PROHIBITION
    //  ------------------------------------------------------------------------------------------
    //  //FIXME                                                                          [:L175]
    //  //不能使用Reset来清除数据，会使SetChanges应用不到行                                 [:L176]
    //
    //  "Reset must not be used to clear the data, because doing so makes SetChanges fail to apply to
    //  rows." The legacy discards the rows instead [:L177-L182].
    //
    //  THE PROHIBITION IS NARROW, AND THAT NARROWNESS IS THE HARD PART. A port that banned Reset
    //  everywhere would be as wrong as one that reset freely, because the legacy itself calls Reset in
    //  three other places on this very path. The three positions asserted below are the ones the
    //  refactor requirements name, and they disagree with one another on purpose:
    //
    //    (a) STAGING CARRIER, MID-LOOP        RowsDiscard, NEVER Reset          [:L177-L182]
    //    (b) IN-PLACE BRANCH, FINAL CHUNK     Reset, and legitimately           [:L216-L218]
    //    (d) RECEIVE SIDE, FIRST CHUNK        Reset, and legitimately           [receive :L192-L196]
    //
    //  What reconciles them: RESET IS UNSAFE ONLY BETWEEN A CAPTURE AND A LATER CAPTURE FROM THE SAME
    //  CARRIER. At (b) the capture at :L205 has already happened and no later capture follows; at (d)
    //  the reset PRECEDES the apply rather than interleaving with it.
    //
    //  HOW THE DISCRIMINATION IS MADE DECIDABLE. Reset clears ALL THREE buffers; RowsDiscard removes
    //  one inclusive range of ONE buffer. A row parked in the Delete! buffer therefore SURVIVES a
    //  discard and does NOT survive a reset, which turns "which one ran" into an observation rather
    //  than an inference. RowsDiscardAndResetAreNotInterchangeable below pins that primitive first, so
    //  the three position tests rest on something asserted rather than assumed.
    // ==========================================================================================

    [Fact]
    public void RowsDiscardAndResetAreNotInterchangeable()
    {
        // The primitive the three position assertions rest on, pinned before they use it.
        //
        // `RowsDiscard(1,nRowCnt,Primary!)` [:L178] removes ONE inclusive range of ONE buffer and
        // leaves everything else standing. `Reset()` [:L217] empties the carrier. They are not two
        // spellings of the same operation, and DEFECT 2 [:L176] is a statement about which of the two
        // may appear at one specific point.
        DataWindowBufferStore discarded = FixtureCarrier.Create(
            primaryRows: 3L,
            filteredRows: 2L,
            deletedRows: 1L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            discarded.RowsDiscard(ItemStatusMachine.FirstRowNumber, 3L, DwBuffer.Primary));

        Assert.Equal(0L, discarded.RowCount());
        Assert.Equal(2L, discarded.FilteredCount());  // untouched
        Assert.Equal(1L, discarded.DeletedCount());   // untouched

        DataWindowBufferStore reset = FixtureCarrier.Create(
            primaryRows: 3L,
            filteredRows: 2L,
            deletedRows: 1L);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, reset.Reset());

        Assert.Equal(0L, reset.RowCount());
        Assert.Equal(0L, reset.FilteredCount());
        Assert.Equal(0L, reset.DeletedCount());

        // AND WHY EACH DISCARD IS GATED ON A POSITIVE COUNT [:L177, :L180, :L210, :L213]. Without the
        // gate the range would be 1 through 0 - end before start - which answers a failure code the
        // legacy would then discard. The gate is therefore not defensive padding; it is what keeps a
        // no-op from being spelled as a failed operation, and it is why a zero-length clear is never
        // attempted at all.
        DataWindowBufferStore populated = FixtureCarrier.Create(primaryRows: 2L, filteredRows: 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            populated.RowsDiscard(ItemStatusMachine.FirstRowNumber, 0L, DwBuffer.Primary));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            populated.RowsDiscard(ItemStatusMachine.FirstRowNumber, 0L, DwBuffer.Filter));

        // Nothing moved, so an ungated zero-length discard really would be inert rather than harmful -
        // which is exactly why the codec must not rely on that and reproduces the gate instead.
        Assert.Equal(2L, populated.RowCount());
        Assert.Equal(2L, populated.FilteredCount());
    }

    [Fact]
    public async Task MidLoopTheStagingCarrierIsClearedByRowsDiscardAndNeverByReset()
    {
        // PRESERVED LEGACY DEFECT 2 [:L176], POSITION (a) - THE PROHIBITED POSITION:
        //     if nRowCnt > 0 then
        //         dsTmp.RowsDiscard(1,nRowCnt,Primary!)                       [:L178]
        //     end if
        //     if nFilterCnt > 0 then
        //         dsTmp.RowsDiscard(1,nFilterCnt,Filter!)                     [:L181]
        //     end if
        //
        // A Delete!-buffer sentinel is planted in the staging carrier DURING the capture at :L171 -
        // which is the only moment a test can reach that object - and must still be there after the
        // mid-loop clear. Surviving proves the clear was a RANGE-SCOPED DISCARD of the moved rows;
        // disappearing would prove a Reset ran, which DEFECT 2 forbids at exactly this point.
        //
        // BOTH DISCARDS ARE GATED ON A POSITIVE COUNT [:L177, :L180], so a zero-length range is never
        // even attempted - and the staged primary rows themselves are gone, which is what makes the
        // NEXT capture carry only the next chunk's rows.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 1L);
        DataWindowBufferStore? staging = null;

        payload.OnEncode = (carrier, ordinal) =>
        {
            if (ordinal != 1)
            {
                return;
            }

            staging = carrier;

            // Parked in Delete!, which neither the moves nor the discards address.
            _ = carrier.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);
        };

        // The state is dictated so the sentinel cannot influence the payload the sink records; this
        // case is about the buffers, not about the format.
        payload.EncodeResult = DataWindowBufferStore.DataStoreSuccess;

        // Observed as the SECOND handover returns, by which point iteration two's capture and clear have
        // both run - so the sentinel has survived two mid-loop clears rather than none.
        long deletedAfterFirstClear = -1L;
        long primaryAfterFirstClear = -1L;
        long filteredAfterFirstClear = -1L;

        sink.AfterChunk = () =>
        {
            if (sink.Chunks.Count != 1 || staging is null)
            {
                return;
            }

            deletedAfterFirstClear = staging.DeletedCount();
            primaryAfterFirstClear = staging.RowCount();
            filteredAfterFirstClear = staging.FilteredCount();
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.SortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.UsedTemporaryCarrier);
        Assert.NotNull(staging);

        // THE DISCRIMINATION. The sentinel survived, so the clear was RowsDiscard and not Reset.
        Assert.Equal(1L, deletedAfterFirstClear);

        // And the clear really did happen: both live buffers were emptied of the staged rows.
        Assert.Equal(0L, primaryAfterFirstClear);
        Assert.Equal(0L, filteredAfterFirstClear);

        // Still there at the end of the whole sequence, so no later Reset crept in either.
        Assert.Equal(1L, staging.DeletedCount());

        // Positional, from the ordered log: the clear sits between the capture and the handover, so by
        // the time a chunk leaves, the staging carrier is already empty [:L177-L183].
        Assert.Equal(2, payload.Captures.Count);
        Assert.Equal(2, sink.CountOf(ChangesetSinkEventKind.Chunk));
        Assert.Equal(0, sink.OrdinalOf(ChangesetSinkEventKind.Error));
    }

    [Fact]
    public async Task TheInPlaceBranchDiscardsMidLoopAndResetsOnlyOnTheFinalChunkAfterTheCapture()
    {
        // PRESERVED LEGACY DEFECT 2 [:L176], POSITION (b) - THE PERMITTED POSITION:
        //     if nChunkIdx < nChunkCnt then
        //         if nRowCnt > 0 then Data.RowsDiscard(1,nRowCnt,Primary!)    [:L211]
        //         if nFilterCnt > 0 then Data.RowsDiscard(1,nFilterCnt,Filter!)[:L214]
        //     else
        //         Data.Reset()                                                [:L217]
        //     end if
        //
        // Reset IS used here, and it is legitimate for one specific reason: it runs AFTER the capture at
        // :L205, so there is no later capture from this carrier for it to break. That is exactly the
        // distinction position (a)'s prohibition turns on, and it is why a codec that banned Reset
        // outright would be wrong.
        //
        // The Delete!-buffer sentinel discriminates again, this time on the SOURCE, and in BOTH
        // directions: present after the mid-loop clear, absent after the final one.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();

        DataWindowBufferStore source = FixtureCarrier.Create(
            primaryRows: 4L,
            filteredRows: 0L,
            deletedRows: 1L);

        long deletedAfterMidLoopClear = -1L;
        long primaryAfterMidLoopClear = -1L;

        sink.AfterChunk = () =>
        {
            if (sink.Chunks.Count != 1)
            {
                return;
            }

            deletedAfterMidLoopClear = source.DeletedCount();
            primaryAfterMidLoopClear = source.RowCount();
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.UnsortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);

        // The in-place branch: no staging carrier, so the capture is taken FROM THE SOURCE at :L205.
        Assert.False(outcome.UsedTemporaryCarrier);
        Assert.Null(outcome.TemporaryCarrierShape);
        Assert.Equal(2, payload.EncodedCarriers.Count);
        Assert.All(payload.EncodedCarriers, carrier => Assert.Same(source, carrier));

        // MID-LOOP: the sentinel survived, so the clear was RowsDiscard - and it really cleared.
        Assert.Equal(1L, deletedAfterMidLoopClear);
        Assert.Equal(2L, primaryAfterMidLoopClear);

        // FINAL CHUNK: the sentinel is gone, so Reset ran - and it ran after the capture, which is why
        // the last chunk still carries its two rows.
        Assert.Equal(0L, source.DeletedCount());
        Assert.Equal(0L, source.RowCount());
        Assert.Equal(0L, source.FilteredCount());
        Assert.Equal(2L, outcome.ChunksSent);
    }

    [Fact]
    public void TheReceiveSideResetsTheTargetOnlyOnTheFirstChunkOfASequence()
    {
        // PRESERVED LEGACY DEFECT 2 [:L176], POSITION (d) - THE OTHER PERMITTED POSITION:
        //     if current = 1 then
        //         ...
        //         dw.Reset()                    [n_cst_threading_task_sqlquery.sru:L195]
        //     end if
        //     rtCode = dw.SetChanges(blbData)   [receive :L197]
        //
        // Reset IS used here, and legitimately, because it PRECEDES the apply rather than interleaving
        // with a later capture. Chunks after the first must ACCUMULATE onto the target - resetting on
        // every chunk would deliver only the last one, and resetting on none would let a second
        // sequence pile onto the residue of the first.
        //
        // A THREE-CHUNK sequence, because all three receive-side positions must be separated: the FIRST
        // chunk resets, a MIDDLE chunk does neither, and the LAST chunk clears the update flags. A
        // two-chunk sequence would conflate the middle case with the last, and the last clears Delete!
        // through ResetUpdate [receive :L198-L199] - which would consume the very sentinel the middle
        // case needs.
        //
        // The payload carries no Delete! rows of its own, so the Delete! buffer of the target is a clean
        // instrument: it can only change because the codec changed it.
        ChangesetCodec codec = CreateCodec();
        CarrierState payload = EncodeStamped(FixtureCarrier.Create(primaryRows: 4L));

        Assert.Empty(SegmentOf(payload, DwBuffer.Delete).Rows);

        DataWindowBufferStore target = FixtureCarrier.Create(
            primaryRows: 2L,
            filteredRows: 0L,
            deletedRows: 1L);

        // ---- CHUNK 1 OF 3: Reset runs, then the apply ---------------------------------------------
        ChangesetApplyOutcome firstOutcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 3L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, firstOutcome.Result);
        Assert.True(firstOutcome.TargetCleared);
        Assert.False(firstOutcome.UpdateFlagsReset);

        // The two pre-existing primary rows AND the Delete!-buffer sentinel are gone, and only the
        // payload's four rows remain: a Reset ran, and it ran BEFORE the apply.
        Assert.Equal(0L, target.DeletedCount());
        Assert.Equal(4L, target.RowCount());

        // ---- CHUNK 2 OF 3: neither Reset nor ResetUpdate ------------------------------------------
        _ = target.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);

        ChangesetApplyOutcome middleOutcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 3L, 2L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, middleOutcome.Result);
        Assert.False(middleOutcome.TargetCleared);
        Assert.False(middleOutcome.UpdateFlagsReset);

        // ACCUMULATED rather than replaced, and the fresh sentinel survived: no Reset on a middle chunk.
        Assert.Equal(8L, target.RowCount());
        Assert.Equal(1L, target.DeletedCount());

        // ---- CHUNK 3 OF 3: still no Reset, but ResetUpdate runs -----------------------------------
        ChangesetApplyOutcome lastOutcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(payload, 3L, 3L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, lastOutcome.Result);
        Assert.False(lastOutcome.TargetCleared);
        Assert.True(lastOutcome.UpdateFlagsReset);

        // `if count = current then dw.ResetUpdate()` [receive :L198-L199]. ResetUpdate IS NOT Reset:
        // every delivered row STAYS - twelve of them - while the update flags are cleared and Delete! is
        // emptied, which is half of what clearing the update flags means.
        Assert.Equal(12L, target.RowCount());
        Assert.Equal(0L, target.DeletedCount());
    }

    [Fact]
    public async Task TheThreeResetPositionsDisagreeAndThatDisagreementIsTheContract()
    {
        // C-K, and the reason DEFECT 2 [:L176] is so easy to port wrongly in BOTH directions. The three
        // verdicts are put side by side here so that a future reader cannot conclude from any one of
        // them that Reset is either always forbidden or always fine.
        //
        //   POSITION (a)  staging carrier, mid-loop   [:L177-L182]      Reset FORBIDDEN
        //   POSITION (b)  in-place, final chunk       [:L216-L218]      Reset USED
        //   POSITION (d)  receive side, current = 1   [receive :L192]   Reset USED
        //
        // Each is measured independently below, through the codec's own reported outcomes and through
        // the Delete!-buffer sentinel.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);

        // ---- POSITION (a): the staging carrier is never reset --------------------------------------
        RecordingTransferSink stagedSink = new();
        DataWindowBufferStore stagedSource = FixtureCarrier.Create(primaryRows: 4L);
        DataWindowBufferStore? staging = null;

        payload.OnEncode = (carrier, ordinal) =>
        {
            if (ordinal != 1)
            {
                return;
            }

            staging = carrier;
            _ = carrier.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);
        };

        ChangesetTransferOutcome staged = await codec.TransferAsync(
            Request(stagedSource, FixtureCarrier.SortedDefinition, SmallChunkSize),
            stagedSink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, staged.ReturnCode);
        Assert.True(staged.UsedTemporaryCarrier);
        Assert.NotNull(staging);
        Assert.Equal(1L, staging.DeletedCount());  // FORBIDDEN position: no Reset ever ran

        // ---- POSITION (b): the in-place source IS reset, on the final chunk only -------------------
        ScriptedPayloadCodec inPlacePayload = new();
        ChangesetCodec inPlaceCodec = CreateCodec(inPlacePayload);
        RecordingTransferSink inPlaceSink = new();

        DataWindowBufferStore inPlaceSource = FixtureCarrier.Create(
            primaryRows: 4L,
            filteredRows: 0L,
            deletedRows: 1L);

        ChangesetTransferOutcome inPlace = await inPlaceCodec.TransferAsync(
            Request(inPlaceSource, FixtureCarrier.UnsortedDefinition, SmallChunkSize),
            inPlaceSink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, inPlace.ReturnCode);
        Assert.False(inPlace.UsedTemporaryCarrier);
        Assert.Equal(0L, inPlaceSource.DeletedCount());  // PERMITTED position: Reset ran

        // ---- POSITION (d): the receive target IS reset, on the first chunk only --------------------
        // The payload is taken from a carrier with NO Delete! rows, so the target's Delete! buffer can
        // only change because the codec changed it. Reusing the position (b) payload would not do: that
        // source carried a Delete! row, every row of Delete! travels regardless of status, and the apply
        // would then re-deliver one - which looks exactly like a reset that did not happen.
        CarrierState receivePayload = EncodeStamped(FixtureCarrier.Create(primaryRows: 2L));

        Assert.Empty(SegmentOf(receivePayload, DwBuffer.Delete).Rows);

        DataWindowBufferStore target = FixtureCarrier.Create(
            primaryRows: 1L,
            filteredRows: 0L,
            deletedRows: 1L);

        ChangesetApplyOutcome applied = inPlaceCodec.ApplyChunk(
            target,
            new ChangesetChunk(receivePayload, 2L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, applied.Result);
        Assert.True(applied.TargetCleared);
        Assert.Equal(0L, target.DeletedCount());  // PERMITTED position: Reset ran
        Assert.Equal(2L, target.RowCount());      // and only the payload's rows remain
    }

    // ==========================================================================================
    //  PHASE 4 - THE TWO ERROR TEXTS, EXACTLY AS THEY ARE
    //  ------------------------------------------------------------------------------------------
    //  Event OnError(RetCode.E_INTERNAL_ERROR,"GetChanges Failed")        [:L172, :L206]
    //  Event OnError(RetCode.E_INTERNAL_ERROR,"TransData Failed")         [:L184, :L220]
    //
    //  BOTH ARE ENGLISH, and that is worth stating because most diagnostics in this service are
    //  Chinese - the SAME LEGACY OBJECT reports 无效的DataObject from its ondotask event [:L554], and
    //  the update task reports 无效的列名: and 没有可更新的表 from its own
    //  [n_cst_thread_task_sqlupdate.sru]. The inconsistency is LEGACY BEHAVIOUR,
    //  asserted here as-is and NOT harmonized (C-B): a reworded or translated message would invalidate
    //  every stored characterization comparison that holds it, and Persistence carries no localization
    //  reference at all, so there is nothing to route either text through even if harmonizing were
    //  allowed. The AAP assigns localization to a shared library consumed by DataServices, not here.
    // ==========================================================================================

    [Theory]
    [MemberData(nameof(BothBranches))]
    public async Task AFailedExtractionReportsTheVerbatimEnglishTextAndTheInternalErrorCode(bool sorted)
    {
        // `if dsTmp.GetChanges(ref blbData) < 0 then` [:L171] on the staging branch and
        // `if Data.GetChanges(ref blbData) < 0 then` [:L205] on the in-place branch. BOTH report the
        // same text, so both are driven here.
        //
        // Reached through the payload double's scriptable failure, which is the only route to this arm:
        // a managed encoder over an in-memory carrier never fails, so without the seam the whole arm
        // would be dead code and its text would never be compared against anything.
        ScriptedPayloadCodec payload = new()
        {
            EncodeResult = DataWindowBufferStore.DataStoreFailure,
        };

        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, DefinitionFor(sorted), SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal("GetChanges Failed", outcome.ErrorText);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("GetChanges Failed", errorText);

        // The transfer returned at :L173 / :L207, so nothing was handed over, and the fault was
        // reported BEFORE the codec returned - which is the order the legacy writes.
        Assert.Empty(sink.Chunks);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.Equal(1, sink.OrdinalOf(ChangesetSinkEventKind.Error));
    }

    [Theory]
    [MemberData(nameof(BothBranches))]
    public async Task AFailedHandoverReportsTheVerbatimEnglishTextAndTheInternalErrorCode(bool sorted)
    {
        // `if tasking.Event OnDataChunk(ref blbData,nChunkCnt,nChunkIdx,false) < 0 then` [:L183, :L219].
        // A NEGATIVE result means failure; zero and positive both mean success, and the codec never
        // interprets the magnitude.
        ChangesetCodec codec = CreateCodec();
        RecordingTransferSink sink = new() { ChunkResult = -1L };
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 5L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, DefinitionFor(sorted), SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, outcome.ReturnCode);
        Assert.Equal("TransData Failed", outcome.ErrorText);

        (long returnCode, string errorText) = Assert.Single(sink.Errors);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, returnCode);
        Assert.Equal("TransData Failed", errorText);

        // The first handover was attempted and refused, then the sequence stopped at :L185 / :L221.
        Assert.Single(sink.Chunks);
        Assert.Equal(0L, outcome.ChunksSent);
        Assert.True(sink.Precedes(ChangesetSinkEventKind.Chunk, ChangesetSinkEventKind.Error));

        // PRESERVED LEGACY BEHAVIOUR, and a subtle one: the legacy returns at :L185 / :L221 WITHOUT
        // reaching the `blbData = Blob("")` at :L187 / :L223, so on this path alone the blob is still
        // populated when the event returns. The codec reports that state rather than asserting the
        // tidier one.
        Assert.False(outcome.PayloadCleared);
    }

    [Fact]
    public void TheTwoErrorTextsAreByteExactAndEnglish()
    {
        // Character for character against :L172 / :L206 and :L184 / :L220. Asserted structurally as
        // well as by equality, because the ways these strings decay are specific: a lower-cased word, a
        // double space, a trailing space, or a translation. Each of those would still "look right" in a
        // diff and would still break every characterization recording that holds the text.
        Assert.Equal("GetChanges Failed", ChangesetCodec.GetChangesFailedText);
        Assert.Equal("TransData Failed", ChangesetCodec.TransDataFailedText);

        foreach (string text in new[]
        {
            ChangesetCodec.GetChangesFailedText,
            ChangesetCodec.TransDataFailedText,
        })
        {
            // Exactly one space, and it is neither leading nor trailing.
            Assert.Equal(1, text.Count(character => character == ' '));
            Assert.Equal(text, text.Trim());
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);

            // Two words, each capitalised - the legacy's own casing.
            string[] words = text.Split(' ');

            Assert.Equal(2, words.Length);
            Assert.All(words, word => Assert.True(char.IsUpper(word[0])));
            Assert.EndsWith("Failed", text, StringComparison.Ordinal);

            // ENGLISH, and therefore pure ASCII. This is the assertion that would fail if a future
            // change "harmonized" these two with the Chinese diagnostics elsewhere in the service.
            Assert.All(text, character => Assert.InRange(character, ' ', '~'));
        }

        // The two are distinct: they mark different failures - extraction versus handover - and
        // collapsing them would lose which step failed.
        Assert.NotEqual(ChangesetCodec.GetChangesFailedText, ChangesetCodec.TransDataFailedText);
    }

    // ==========================================================================================
    //  PHASE 5 - THE REMAINING BEHAVIOURS
    // ==========================================================================================

    [Fact]
    public async Task TheFilterBufferFoldsIntoThePrimaryBufferAtTheOneBasedAppendPosition()
    {
        // `Data.RowsMove(1,Data.FilteredCount(),Filter!,Data,Data.RowCount() + 1,Primary!)` [:L109],
        // reached through the real transfer rather than through the helper alone, because ORDER is part
        // of the behaviour: the fold runs BEFORE the chunk arithmetic of :L145, so the chunk count is
        // computed over the POST-FOLD counts.
        //
        // A SELF-FOLD - source and destination are the same carrier - and the destination row is
        // `RowCount() + 1`, the one-based position one past the last primary row. Three primary plus two
        // filtered rows therefore become five primary rows and zero filtered rows, and the two folded
        // rows must be at the TAIL. R9 item 4: carrying the "+ 1" into a zero-based insert would put
        // them one position early while leaving the COUNT correct.
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 3L, filteredRows: 2L);

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(
                source,
                FixtureCarrier.UnsortedDefinition,
                chunkSize: 10L,
                receiverNeedsCreatedObject: true),
            sink,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.True(outcome.FoldedFilterBuffer);

        // `tasking.Event OnCreateData(Data.Describe("DataWindow.Syntax"))` [:L104] precedes the fold,
        // and the syntax travels through untouched.
        Assert.Equal(FixtureCarrier.SortedDefinition.Syntax, Assert.Single(sink.CreatedFrom));
        Assert.True(sink.Precedes(ChangesetSinkEventKind.CreateData, ChangesetSinkEventKind.Chunk));

        // Five rows in ONE chunk, so the arithmetic saw the post-fold counts.
        RecordedCapture capture = Assert.Single(payload.Captures);

        Assert.Equal(5L, capture.PrimaryRows);
        Assert.Equal(0L, capture.FilteredRows);

        // THE APPEND POSITION, asserted by identity rather than by count: the three original rows keep
        // positions 1..3 and the two folded rows land at 4 and 5, in their original order.
        Assert.Equal<object?>(
            [
                1L,
                2L,
                3L,
                FixtureCarrier.FilterSeed + 1L,
                FixtureCarrier.FilterSeed + 2L,
            ],
            capture.PrimaryIdentifiers);

        Assert.Equal(1L, outcome.ChunkCount);
    }

    [Fact]
    public void AnEmptyPayloadClearsTheTargetAndAnswersSuccess()
    {
        // `else rtCode = 1 : dw.Reset()` [n_cst_threading_task_sqlquery.sru:L207-L210, :L226-L229].
        // AN EMPTY PAYLOAD MEANS CLEAR, not "nothing to do", and it is exactly the shape the send side's
        // empty-result arm produces at :L230. Note the ORDER the legacy writes: the code is set first
        // and the reset happens second, and the reset is unconditional on this arm.
        ChangesetCodec codec = CreateCodec();

        DataWindowBufferStore target = FixtureCarrier.Create(
            primaryRows: 3L,
            filteredRows: 1L,
            deletedRows: 1L);

        ChangesetApplyOutcome outcome = codec.ApplyChunk(
            target,
            new ChangesetChunk(state: null, 1L, 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, outcome.Result);
        Assert.True(outcome.TargetCleared);
        Assert.True(outcome.Notified);
        Assert.False(outcome.PartialApplyCoerced);

        // Reset, not ResetUpdate: all three buffers are empty.
        Assert.Equal(0L, target.RowCount());
        Assert.Equal(0L, target.FilteredCount());
        Assert.Equal(0L, target.DeletedCount());
    }

    [Fact]
    public void TheLastChunkClearsTheUpdateFlagsSoDeliveredRowsReadAsUnmodified()
    {
        // `if count = current then dw.ResetUpdate()`
        // [n_cst_threading_task_sqlquery.sru:L198-L199, :L222-L223, :L239-L240]. It runs on the LAST
        // chunk and it runs WITHOUT consulting the apply result, which the legacy does not consult
        // either.
        //
        // WHAT IT IS FOR: the send side stamped every row DataModified [:L162, :L197] purely to make it
        // visible to the extraction, so a freshly delivered row would otherwise arrive looking edited.
        // ResetUpdate re-baselines it - originals equal currents - which is the state a retrieved row is
        // actually in and the state the updatewhere=1 concurrency check is measured against
        // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14].
        ChangesetCodec codec = CreateCodec();
        CarrierState state = EncodeStamped(FixtureCarrier.Create(primaryRows: 2L));

        DataWindowBufferStore target = FixtureCarrier.Create(primaryRows: 0L);

        // Chunk 1 of 2: not the last, so the flags stay.
        ChangesetApplyOutcome middle = codec.ApplyChunk(
            target,
            new ChangesetChunk(state, 2L, 1L),
            TestContext.Current.CancellationToken);

        Assert.False(middle.UpdateFlagsReset);
        Assert.Equal(
            ItemStatus.DataModified,
            target.GetItemStatus(
                ItemStatusMachine.FirstRowNumber,
                ItemStatusMachine.RowStatusColumn,
                DwBuffer.Primary));

        // Chunk 2 of 2: the last, so the flags are cleared and every delivered row reads unmodified.
        ChangesetApplyOutcome last = codec.ApplyChunk(
            target,
            new ChangesetChunk(state, 2L, 2L),
            TestContext.Current.CancellationToken);

        Assert.True(last.UpdateFlagsReset);

        // Two rows per chunk, accumulated across two chunks.
        long delivered = target.RowCount();

        Assert.Equal(4L, delivered);

        for (long row = ItemStatusMachine.FirstRowNumber; row <= delivered; row++)
        {
            Assert.Equal(
                ItemStatus.NotModified,
                target.GetItemStatus(row, ItemStatusMachine.RowStatusColumn, DwBuffer.Primary));

            // Re-baselined: the original of every column now equals its current.
            for (int column = 1; column <= FixtureCarrier.ColumnCount; column++)
            {
                Assert.Equal(
                    target.GetItemValue(row, column, DwBuffer.Primary),
                    target.GetItemOriginalValue(row, column, DwBuffer.Primary));
            }
        }
    }

    [Fact]
    public async Task AStaleFilteredCountAnswersAFailureCodeRatherThanThrowing()
    {
        // A THIRD, LATENT QUIRK, PRESERVED (C-B). `long nRow,nRowCnt,nFilterCnt` is declared ONCE,
        // OUTSIDE the chunk loop [:L75]. `nRowCnt` is reassigned at the top of every iteration
        // [:L159, :L195], but `nFilterCnt` is assigned ONLY inside the guard at :L164 / :L199. On an
        // iteration where that guard is false, the later `if nFilterCnt > 0 then ... RowsDiscard(...)`
        // [:L181, :L214] therefore runs with the PREVIOUS iteration's count against a Filter buffer that
        // has already been emptied.
        //
        // The legacy DISCARDS the return value, so an out-of-range discard is a benign no-op there - and
        // that is precisely why RowsDiscard must answer a code rather than throw. An exception escaping
        // here would be a failure mode the oracle does not have, and on this service it would cross a
        // gRPC boundary as an UNSTRUCTURED FAULT rather than as the structured DbError the contract
        // promises.
        //
        // The impl notes the quirk stays latent through an undisturbed loop and becomes reachable the
        // moment the handover sink mutates the source between chunks - which the legacy handover at
        // :L183 / :L219 genuinely permits, because it runs while the source is still live. That is what
        // this case does.
        //
        // The arithmetic, step by step, with two primary rows, five filtered rows and a chunk size of
        // two, so :L145 gives four chunks:
        //   chunk 1  nRowCnt = Min(2,2) = 2, so the guard is false and nFilterCnt stays 0
        //   chunk 2  nRowCnt = Min(2,0) = 0, guard true, nFilterCnt = Min(2,5) = 2, filter drops to 3
        //            -- the sink then discards the remaining three filtered rows --
        //   chunk 3  nRowCnt = 0 and FilteredCount() = 0, so the guard is FALSE and nFilterCnt is
        //            STILL 2: the discard at :L214 addresses rows 1..2 of an EMPTY buffer
        //   chunk 4  the same, and this is the final chunk, so Reset() runs instead
        ScriptedPayloadCodec payload = new();
        ChangesetCodec codec = CreateCodec(payload);
        RecordingTransferSink sink = new();
        DataWindowBufferStore source = FixtureCarrier.Create(primaryRows: 2L, filteredRows: 5L);

        sink.AfterChunk = () =>
        {
            if (sink.Chunks.Count != 2)
            {
                return;
            }

            long remaining = source.FilteredCount();

            if (remaining > 0L)
            {
                _ = source.RowsDiscard(ItemStatusMachine.FirstRowNumber, remaining, DwBuffer.Filter);
            }
        };

        ChangesetTransferOutcome outcome = await codec.TransferAsync(
            Request(source, FixtureCarrier.UnsortedDefinition, SmallChunkSize),
            sink,
            TestContext.Current.CancellationToken);

        // NO EXCEPTION ESCAPED, and the transfer completed with the legacy's own success code.
        Assert.Equal(RetCode.OK, outcome.ReturnCode);
        Assert.Equal(4L, outcome.ChunkCount);
        Assert.Equal(4L, outcome.ChunksSent);
        Assert.Empty(sink.Errors);

        // Chunk 3 is the iteration that ran with the stale count: no rows in either buffer.
        Assert.Equal(0L, payload.Captures[2].PrimaryRows);
        Assert.Equal(0L, payload.Captures[2].FilteredRows);

        // The primitive itself, asserted directly: an unaddressable range answers a code, never throws.
        DataWindowBufferStore empty = FixtureCarrier.Create(primaryRows: 0L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            empty.RowsDiscard(ItemStatusMachine.FirstRowNumber, 2L, DwBuffer.Filter));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            empty.RowsDiscard(ItemStatusMachine.FirstRowNumber, 2L, DwBuffer.Primary));
    }

    [Fact]
    public void TheCodecCarriesNoDocumentSerializerNoDatabaseTypeAndNoCrosstabHandling()
    {
        // THREE NEGATIVE STATEMENTS, each of which is a real constraint rather than a tidiness claim.
        //
        // C-D / no document serializer. JSON and XML belong to the deferred Documents capability area
        // [AAP 0.4.1], so reaching for either here would pull a deferred library into scope. The payload
        // travels as the PUBLISHED CarrierState contract instead.
        //
        // C-E / no fabricated database. This codec is a pure buffer transform: it must not name a
        // dialect, hold a connection or reference a DbContext. DatabaseType exists in the published
        // contract - persistence.v1.proto declares it with exactly two members, SQL Server and Oracle -
        // and it belongs to the paging rewriters, not here.
        //
        // C-K / no crosstab handling. The selector at :L93 routes crosstab and composite carriers to
        // Buffers/FullStateCodec.cs, and THAT arm is where GetFullState, the sort and filter
        // SYNCHRONIZATION of :L562-L563 and the too-many-columns crash all live. Duplicating any of it
        // here would give the repository two answers to "which serialization applies".
        Type codec = typeof(ChangesetCodec);

        const BindingFlags everything =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        List<Type> surface = [];

        foreach (MethodInfo method in codec.GetMethods(everything))
        {
            surface.Add(method.ReturnType);

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                surface.Add(parameter.ParameterType);
            }
        }

        foreach (FieldInfo field in codec.GetFields(everything))
        {
            surface.Add(field.FieldType);
        }

        foreach (PropertyInfo property in codec.GetProperties(everything))
        {
            surface.Add(property.PropertyType);
        }

        HashSet<Type> expanded = [];

        foreach (Type type in surface)
        {
            Expand(type, expanded);
        }

        Assert.NotEmpty(expanded);

        string[] forbiddenNamespaces =
        [
            "System.Text.Json",
            "System.Xml",
            "System.Data",
            "Microsoft.Data.Sqlite",
            "Microsoft.EntityFrameworkCore",
        ];

        foreach (Type type in expanded)
        {
            string space = type.Namespace ?? string.Empty;

            foreach (string forbidden in forbiddenNamespaces)
            {
                Assert.False(
                    space.Equals(forbidden, StringComparison.Ordinal)
                        || space.StartsWith(forbidden + ".", StringComparison.Ordinal),
                    $"{codec.Name} must not touch {forbidden}; it reaches {type.FullName}.");
            }

            Assert.NotEqual(typeof(DatabaseType), type);
        }

        // No member NAME advertises any of the three either, which catches a helper that took a plain
        // string or a plain long and so would not show up in the type surface above.
        foreach (MemberInfo member in codec.GetMembers(everything))
        {
            foreach (string forbidden in new[] { "Json", "Xml", "Crosstab", "Composite", "Database" })
            {
                Assert.DoesNotContain(forbidden, member.Name, StringComparison.Ordinal);
            }
        }

        // And the behavioural half: a crosstab or composite carrier is NOT this codec's business, and a
        // changeset chunk cannot carry the other selector value even by mistake.
        Assert.False(
            ChangesetCodec.HandlesCarrier(
                FixtureCarrier.Create(1L, processing: DataWindowProcessing.CrosstabValue)));
        Assert.False(
            ChangesetCodec.HandlesCarrier(
                FixtureCarrier.Create(1L, processing: DataWindowProcessing.CompositeValue)));
        Assert.False(ChangesetChunk.ChangesetSelector);
    }

    // ==========================================================================================
    //  HELPERS
    // ==========================================================================================

    /// <summary>
    /// Builds a codec whose only clock is a fake that is never advanced (0.6.7).
    /// </summary>
    /// <param name="payloadCodec">The payload format, or <see langword="null"/> for the real one.</param>
    /// <returns>The codec.</returns>
    private static ChangesetCodec CreateCodec(IChangesetPayloadCodec? payloadCodec = null)
    {
        return new ChangesetCodec(new FakeTimeProvider(), payloadCodec);
    }

    /// <summary>
    /// Builds a transfer request whose inter-chunk yield is zero.
    /// </summary>
    /// <param name="source">The carrier to transfer.</param>
    /// <param name="definition">The source's definition properties.</param>
    /// <param name="chunkSize">Rows per chunk.</param>
    /// <param name="receiverNeedsCreatedObject">Whether the create-data handover and fold run.</param>
    /// <returns>The request.</returns>
    /// <remarks>
    /// The production default stays the legacy's own 0.02 seconds [<c>:L189</c>, <c>:L225</c>]; only a
    /// caller that says otherwise gets something else, and a test is exactly such a caller. A zero
    /// interval still yields control once, so the cancellation check between chunks is still observed -
    /// which is what <see cref="CancellationBetweenChunksAnswersCancelledAndStopsEmission"/> relies on.
    /// </remarks>
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
    /// The sorted definition, which takes DEFECT 1's staging branch, or the unsorted one, which takes
    /// the in-place branch.
    /// </summary>
    /// <param name="sorted">Whether the sorted definition is wanted.</param>
    /// <returns>The definition.</returns>
    private static ChangesetSourceDefinition DefinitionFor(bool sorted)
    {
        return sorted ? FixtureCarrier.SortedDefinition : FixtureCarrier.UnsortedDefinition;
    }

    /// <summary>
    /// Stamps every live row <see cref="ItemStatus.DataModified"/> through the row-status column and
    /// then captures a changeset from the carrier.
    /// </summary>
    /// <param name="source">The carrier to stamp and capture. It is modified.</param>
    /// <returns>The captured state, which is never <see langword="null"/> on success.</returns>
    /// <remarks>
    /// THE TWO STEPS ARE THE SEND SIDE'S OWN SEQUENCE and the order is not optional. The stamping at
    /// <c>:L161-L169</c> and <c>:L196-L203</c> exists precisely because a changeset carries CHANGED rows
    /// only: a freshly retrieved row is <see cref="ItemStatus.NotModified"/>, so an unstamped carrier
    /// captures an EMPTY payload. A receive-side case that skipped the stamp would therefore be
    /// asserting against nothing while looking as though it asserted against a full result.
    /// </remarks>
    private static CarrierState EncodeStamped(DataWindowBufferStore source)
    {
        StampRowStatuses(source, DwBuffer.Primary, source.RowCount());
        StampRowStatuses(source, DwBuffer.Filter, source.FilteredCount());

        ChangesetPayloadCodec format = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            format.TryEncode(source, out CarrierState? state));
        Assert.NotNull(state);

        return state;
    }

    /// <summary>
    /// Stamps a whole buffer's rows through the column-zero row-status convention.
    /// </summary>
    /// <param name="carrier">The carrier to stamp.</param>
    /// <param name="dwBuffer">The buffer to stamp.</param>
    /// <param name="rowCount">How many rows the buffer holds.</param>
    private static void StampRowStatuses(
        DataWindowBufferStore carrier,
        DwBuffer dwBuffer,
        long rowCount)
    {
        for (long row = ItemStatusMachine.FirstRowNumber; row <= rowCount; row++)
        {
            _ = carrier.SetItemStatus(
                row,
                ItemStatusMachine.RowStatusColumn,
                dwBuffer,
                ItemStatus.DataModified);
        }
    }

    /// <summary>
    /// The one segment a captured state carries for a buffer.
    /// </summary>
    /// <param name="state">The captured state.</param>
    /// <param name="dwBuffer">The buffer wanted.</param>
    /// <returns>That buffer's segment.</returns>
    /// <remarks>
    /// <c>persistence.v1.CarrierState</c> requires EXACTLY ONE segment per buffer in the canonical order
    /// primary, delete, filter, so a single match is an assertion in its own right.
    /// </remarks>
    private static CarrierBufferSegment SegmentOf(CarrierState state, DwBuffer dwBuffer)
    {
        return Assert.Single(state.Segments, segment => segment.Buffer == dwBuffer);
    }

    /// <summary>
    /// Walks a type and every type reachable from it - generic arguments, array and by-reference
    /// element types - so a nested reference cannot hide inside a wrapper.
    /// </summary>
    /// <param name="type">The type to expand.</param>
    /// <param name="into">The accumulating set.</param>
    private static void Expand(Type type, HashSet<Type> into)
    {
        if (!into.Add(type))
        {
            return;
        }

        if (type.HasElementType)
        {
            Type? element = type.GetElementType();

            if (element is not null)
            {
                Expand(element, into);
            }
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            Expand(argument, into);
        }
    }
}
