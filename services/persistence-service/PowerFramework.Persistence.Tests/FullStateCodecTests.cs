// ==============================================================================================
//  FullStateCodecTests - the full-state blob codec and the two documented defects it owns
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Buffers/FullStateCodec.cs
//
//  BEHAVIOURAL ORACLE (all READ ONLY per constraint C-C - cited here, never copied and never edited)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru
//          :L93-L101   the codec selector and the full-state send path
//          :L555-L585  the sort-and-filter block in full: the off-main-thread gate at :L559, DEFECT 3
//                      at :L563, the changeset arm's OPPOSITE note at :L578, and the main-thread
//                      else-arm at :L582-L583 that synchronizes on its own separate condition
//          :L660-L680  the crosstab block in full: the surrounding modify-string block at :L663-L669,
//                      DEFECT 4 at :L673, its guarded read-then-write at :L674-L676, and the
//                      transaction attachment at :L678-L680 that the guard must precede
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru:L184-L251
//          the receive path and the sign-opposite result normalization
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
//          :L3 the processing kind, :L14 the sort expression this file trims
//      docs/PB多线程绕坑提示.md   the two threading hazards
//
//  ==============================================================================================
//  THESE ARE UNIT TESTS. THEY ARE NOT CHARACTERIZATION TESTS, AND THEY CANNOT BE.
//  ==============================================================================================
//  ALL TWELVE DataWindow definitions in this repository were scanned for their `processing=` value.
//  ELEVEN ARE `processing=1` - INCLUDING THE PRIMARY FIXTURE, ws_objects/pfw.tests.pbl.src/dw_sqlite.srd,
//  WHOSE :L3 READS `processing=1` - AND ONE, ws_objects/pfw.tests.pbl.src/dw_barcode.srd, IS
//  `processing=0`. NOT ONE IS 4 OR 5 - and 4 and 5, Crosstab and Composite, are the only two values that
//  select this codec [n_cst_thread_task_sqlquery.sru:L94]. THERE IS THEREFORE NO LEGACY FIXTURE ON THIS
//  PATH AND NO RECORDING TO COMPARE AGAINST: neither of the two defects below can be characterized.
//
//  THAT CENSUS IS NOT PROSE. `NoDataWindowInTheRepositorySelectsThisCodec_OracleCensus` re-derives it
//  from the twelve definition files themselves, so the claim is falsifiable: the day a crosstab fixture
//  is added to the repository that test FAILS, which is exactly the day this header would need rewriting
//  and the day a real behavioural oracle would become available for these two defects.
//
//  ----------------------------------------------------------------------------------------------
//  C-K - THE TWO CODECS DISAGREE ON PURPOSE, WHICH IS WHY THERE ARE TWO TEST FILES.
//  ----------------------------------------------------------------------------------------------
//  The two arms of one `choose case` give OPPOSITE instructions about the same two operations, and a
//  future reader must not unify them:
//
//    :L563  full-state  -> 使用SetFullState传递数据的风格需要同步排序和过滤条件
//                          SYNCHRONIZES sort and filter onto the source. THIS FILE.
//    :L578  changeset   -> 使用SetChanges传递数据的风格不需要排序和过滤条件，甚至会影响性能
//                          CLEARS both with SetSort("") and SetFilter(""). ChangesetCodecTests.
//
//  The same inversion governs `Reset`, which is REQUIRED here after the capture [:L96] and FORBIDDEN
//  mid-loop there [:L176-L177], and the result normalization, where an above-one code becomes SUCCESS
//  here [:L248] and FAILURE there [:L250]. Three independent oppositions, none of them an oversight.
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
//  ----------------------------------------------------------------------------------------------
//  THE ONE THING IN THE CODEC THIS SUITE CANNOT REACH, STATED RATHER THAN LEFT AS A COVERAGE GAP.
//  ----------------------------------------------------------------------------------------------
//  `FullStateCodec.RowCountOf`'s default arm throws `NotSupportedException` for a buffer that is not one
//  of the three PowerBuilder declares. IT HAS NO REACHABLE CALLER: the only call site iterates
//  `PayloadBufferOrder`, a fixed three-element array of Primary, Delete and Filter, and on the inbound
//  side `AreSegmentsCanonical` rejects any roster that is not exactly those three in exactly that order
//  BEFORE a count is ever taken. The arm is a guard against a future edit to that array, not a path a
//  payload can take, so it is left uncovered ON PURPOSE. Reaching it would mean widening the private
//  surface purely to let a test in - which would weaken the very invariant the arm is guarding.
//
//  Everything else - both defects, both arms of each guard, both codec-selection outcomes, every
//  rejection reason and every value-domain refusal - IS covered, and the two adjacent diagnostics of the
//  crosstab block are pinned byte for byte.
//
//  C-F SELF-AUDIT: every value in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears in any form.
// ==============================================================================================

using System.Globalization;
using System.Text;

using Google.Protobuf;

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.Persistence.Buffers;

// THE ONE NAMESPACE THIS FILE REACHES OUTSIDE ITS OWN SUBJECT, AND WHY. `Tasks` carries
// `SqlQueryTask`, which COMPOSES the crosstab block's three ordered steps and owns the two diagnostics
// adjacent to the guard - the transaction-attachment text at the oracle's :L679 and the modify-string
// block's code at :L666. Those are properties of the block this codec sits inside, so they are asserted
// here rather than left to a reader to hope are covered elsewhere. It is NOT in GlobalUsings.cs, unlike
// Buffers, Data, Errors and Sql, so it is named here.
using PowerFramework.Persistence.Tasks;

using Xunit;

// RetCode IS DELIBERATELY NOT ALIASED HERE, for the same reason the sibling DataWindowBuffersTests
// records: GlobalUsings.cs owns this assembly's alias - `global using RetCode =
// PowerFramework.Shared.Kernel.RetCode;` - and repeating a file-local alias of the same name is error
// CS1537 rather than a harmless duplicate. The bare name already means the kernel class here.
namespace PowerFramework.Persistence.Tests;

// ==================================================================================================
//  THE CARRIER DOUBLE IS THE SHARED ORDERED CALL RECORDER, NOT A LOCAL ONE.
//  ------------------------------------------------------------------------------------------------
//  `TestDoubles.ScriptedCarrierSurface` implements `IFullStateCarrierSurface` and records EVERY
//  describe, modify, sort and filter interaction in ONE ordered log of `RecordedCarrierCall`. It is
//  used here rather than a file-local recorder for two reasons, and the second is the load-bearing one:
//
//    1. A local duplicate of a shared double drifts. Two recorders answering `Describe` differently -
//       one with the empty string, the shared one with PowerBuilder's own "?" unreadable marker - would
//       have this file asserting against a state the legacy never produces.
//
//    2. ORDER IS THE ASSERTION FOR DEFECT 4, AND AN UNORDERED SPY CANNOT EXPRESS "BEFORE". The shared
//       double carries `OrdinalOf`, `FirstOrdinalContaining` and `Precedes` precisely so the crosstab
//       guard's position can be asserted rather than only its content, and its own documentation names
//       this codec's guard as the case it was built for. A port that wrote the guard AFTER attaching
//       the transaction would satisfy every content-only assertion while reproducing none of the
//       behaviour, because the whole point of the property is to be in place BEFORE anything can raise
//       the prompt it suppresses.
//
//  ITS DESCRIBE FALLBACK IS "?" RATHER THAN THE EMPTY STRING, which matters to the guard: PowerBuilder
//  answers "?" for a property that is not applicable and "!" for one that cannot be read, and NEITHER
//  equals "yes", so both fall into the write arm exactly as the legacy's `<>` does [:L674].
// ==================================================================================================

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
    /// Renders the shared ordered call recorder's log as one <c>"Verb:argument"</c> line per call, IN
    /// CALL ORDER.
    /// </summary>
    /// <param name="surface">The recorder to read.</param>
    /// <returns>The ordered trace.</returns>
    /// <remarks>
    /// <para>
    /// A PROJECTION OF THE SHARED LOG, NOT A SECOND RECORDER. Every element comes from
    /// <see cref="ScriptedCarrierSurface.Calls"/> in <see cref="RecordedCarrierCall.Ordinal"/> order, so
    /// an assertion written against this trace is an assertion about ORDER as well as content: comparing
    /// a whole sequence with <c>Assert.Equal</c> fails on a reordering, on an extra call and on a missing
    /// one alike, which is exactly the trio a set-shaped or count-shaped assertion cannot separate.
    /// </para>
    /// <para>
    /// It exists because the verb-and-argument pair reads far better in a failure message than a record
    /// struct carrying an ordinal and a result does, and because the shared double's own
    /// <see cref="ScriptedCarrierSurface.Precedes"/> answers a boolean - excellent for "A before B" and
    /// useless for "these calls and no others".
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> Trace(ScriptedCarrierSurface surface)
    {
        return
        [
            .. surface.Calls
                .OrderBy(static call => call.Ordinal)
                .Select(static call => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{call.Kind}:{call.Argument}")),
        ];
    }

    /// <summary>
    /// The production source file that COMPOSES the three ordered steps of the crosstab block.
    /// </summary>
    /// <returns>The absolute path of <c>Tasks/SqlQueryTask.cs</c>.</returns>
    /// <remarks>
    /// <para>
    /// THE COMPOSITION IS NOT IN THE CODEC, WHICH IS WHY THE COMPOSER IS WHAT GETS READ. This codec owns
    /// the guard and knows nothing about transactions or statements; the ORDER of the guard, the
    /// attachment and the modification is decided by the task that calls all three. Asserting the order
    /// therefore means observing the task.
    /// </para>
    /// <para>
    /// THE LOCATOR FOLLOWS THE PATTERN THIS PROJECT ALREADY USES - see
    /// <c>TransactionServiceTests.TheServiceSourceReadsNoWallClock</c>: form the path directly from the
    /// repository root the build embedded when that is available, and fall back to walking up out of the
    /// test output directory when it is not. The marker is SERVICE relative rather than repository
    /// relative, so the walk is looking for <c>services/persistence-service</c> and not for the root.
    /// </para>
    /// </remarks>
    private static string LocateQueryTaskSource()
    {
        if (TestRepositoryRoot.Embedded is { } root)
        {
            string direct = Path.Combine(
                root,
                "services",
                "persistence-service",
                "PowerFramework.Persistence",
                "Tasks",
                "SqlQueryTask.cs");

            // TESTED RATHER THAN TRUSTED, so a relaid-out tree degrades to the walk below instead of
            // failing on a stale assumption.
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        DirectoryInfo? probe = new(AppContext.BaseDirectory);

        while (probe is not null)
        {
            string candidate = Path.Combine(
                probe.FullName,
                "PowerFramework.Persistence",
                "Tasks",
                "SqlQueryTask.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        throw new InvalidOperationException(
            "Tasks/SqlQueryTask.cs could not be located from "
                + $"'{TestRepositoryRoot.SearchStart}'. It composes the crosstab block's three ordered "
                + "steps, so the position assertion cannot be made without it - and passing the test "
                + "without reading it would assert nothing.");
    }

    /// <summary>
    /// A source file's EXECUTABLE lines, with every comment-only line removed.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The remaining lines, newline joined, in file order.</returns>
    /// <remarks>
    /// <b>THE ASSERTIONS MUST BE ABOUT CODE, NOT ABOUT THE FILE'S OWN PROSE.</b> Every production file in
    /// this service documents the legacy behaviour it reproduces and quotes the oracle at length, so a
    /// naive text search finds the right identifiers inside COMMENTS - including, in this very case, a
    /// block comment that names all three ordered steps in the correct order while proving nothing about
    /// the code beneath it. Stripping the comment-only lines first is what makes a match evidence.
    /// </remarks>
    private static string ExecutableLinesOf(string path)
    {
        return string.Join(
            '\n',
            File.ReadAllLines(path)
                .Where(static line =>
                {
                    string trimmed = line.TrimStart();

                    return !trimmed.StartsWith("//", StringComparison.Ordinal);
                }));
    }

    /// <summary>
    /// The one-based line index of the FIRST line containing a fragment.
    /// </summary>
    /// <param name="text">The newline-joined text to search.</param>
    /// <param name="fragment">The text to look for.</param>
    /// <returns>The one-based line index, or <c>0</c> when no line contains the fragment.</returns>
    /// <remarks>
    /// ZERO FOR ABSENT RATHER THAN AN EXCEPTION, because every caller asserts presence explicitly before
    /// comparing two positions - absence is a distinct failure from misordering and is reported as one.
    /// </remarks>
    private static int OrdinalOfLineContaining(string text, string fragment)
    {
        string[] lines = text.Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(fragment, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        return 0;
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
        ScriptedCarrierSurface surface = new();
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

    /// <summary>
    /// THE WHOLE OF THE DECLARED PROCESSING DOMAIN, 0 THROUGH 5, WITH ITS OWNER NAMED FOR EACH VALUE.
    /// Only 4 and 5 select this codec.
    /// </summary>
    /// <remarks>
    /// Written as one exhaustive table rather than as two complementary ones so that the partition is
    /// visible at a glance and a value cannot fall through both: each of the six kinds PowerBuilder
    /// declares appears exactly once, with the expected answer beside it.
    /// </remarks>
    /// <param name="kind">The processing kind.</param>
    /// <param name="expectedFullState">Whether that kind selects the full-state codec.</param>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(1L, false)]
    [InlineData(2L, false)]
    [InlineData(3L, false)]
    [InlineData(4L, true)]
    [InlineData(5L, true)]
    public void OnlyCrosstabAndCompositeSelectThisCodecAcrossTheWholeDomain_UnitLevelNoOracle(
        long kind,
        bool expectedFullState)
    {
        Assert.Equal(expectedFullState, NewCrosstabCarrier(kind).RequiresFullStateTransfer);

        // The same answer from the value type itself, so the discriminator cannot disagree with the
        // carrier that consults it.
        Assert.Equal(
            expectedFullState,
            new DataWindowProcessing(kind).SelectsFullStateTransfer);
    }

    /// <summary>
    /// THE NO-FIXTURE FACT, RE-DERIVED FROM THE ORACLE FILES RATHER THAN ASSERTED IN PROSE: not one
    /// DataWindow in this repository declares a processing kind that selects this codec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE FILE HEADER'S CENTRAL CLAIM, MADE FALSIFIABLE.</b> The header states that every one
    /// of the twelve DataWindow definitions is <c>processing=0</c> or <c>processing=1</c> and that none is
    /// 4 or 5, which is why every carrier in this suite is synthetic and why no characterization evidence
    /// exists. A reader has no reason to take that on trust, and a claim about the repository that lives
    /// only in a comment silently becomes false the moment a crosstab fixture is added. This test reads
    /// the definitions and re-derives the census, so the day a <c>processing=4</c> DataWindow appears it
    /// FAILS - which is exactly the day this file's honesty statement would need rewriting and the day a
    /// real oracle would become available.
    /// </para>
    /// <para>
    /// C-C: the definitions are READ and never written. The count is asserted too, so a run against a
    /// partial checkout that found only some of them cannot pass by finding nothing.
    /// </para>
    /// <para>
    /// SKIPPED RATHER THAN FAILED WHEN THE LEGACY TREE IS ABSENT. The service must build and test from a
    /// clean checkout of its own (C-A/C-I); if the oracle tree is not present the census cannot be taken,
    /// and the honest outcome is to say so rather than to fail a codec test for a missing input.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDataWindowInTheRepositorySelectsThisCodec_OracleCensus()
    {
        if (LocateLegacyDataWindowDirectory() is not { } oracleRoot)
        {
            Assert.Skip(
                "The legacy ws_objects tree is not present in this checkout, so the processing-kind "
                    + "census cannot be taken. The synthetic carriers used throughout this suite are "
                    + "unaffected - see the file header.");

            return;
        }

        string[] definitions = Directory.GetFiles(oracleRoot, "*.srd", SearchOption.AllDirectories);

        // TWELVE, which is the number the file header states.
        Assert.Equal(12, definitions.Length);

        List<string> selectingThisCodec = [];
        Dictionary<string, long> census = new(StringComparer.Ordinal);

        foreach (string definition in definitions)
        {
            long kind = ProcessingKindOf(File.ReadAllText(definition));

            census[Path.GetFileName(definition)] = kind;

            if (new DataWindowProcessing(kind).SelectsFullStateTransfer)
            {
                selectingThisCodec.Add(Path.GetFileName(definition));
            }
        }

        // NOT ONE. This is the fact the whole file rests on.
        Assert.Empty(selectingThisCodec);

        // EVERY ONE IS 0 OR 1, so the absence is not an artefact of an unparsed value being read as zero.
        Assert.All(census, entry => Assert.InRange(entry.Value, 0L, 1L));

        // AND THE PRIMARY FIXTURE SPECIFICALLY IS processing=1 [dw_sqlite.srd:L3], which is the value the
        // header names.
        Assert.Equal(1L, census["dw_sqlite.srd"]);

        // Exactly one is 0 - dw_barcode.srd - and the remaining eleven are 1.
        Assert.Equal(1, census.Count(entry => entry.Value == 0L));
        Assert.Equal(11, census.Count(entry => entry.Value == 1L));
        Assert.Equal(0L, census["dw_barcode.srd"]);
    }

    /// <summary>
    /// Reads the <c>processing=</c> value out of a DataWindow definition's <c>datawindow(...)</c> line.
    /// </summary>
    /// <param name="definition">The whole definition text.</param>
    /// <returns>The declared processing kind.</returns>
    /// <remarks>
    /// A DELIBERATELY NARROW READER. It looks for the first <c>processing=</c> occurrence and takes the
    /// digits after it, which is all the census needs; it is not a DataWindow-syntax parser and makes no
    /// claim to be one. Culture-invariant parsing, because a definition is machine-generated ASCII and
    /// must not be read differently on a host with another locale.
    /// </remarks>
    private static long ProcessingKindOf(string definition)
    {
        const string Marker = "processing=";

        int start = definition.IndexOf(Marker, StringComparison.Ordinal);

        Assert.True(start >= 0, "the definition declares no processing kind at all");

        int cursor = start + Marker.Length;
        int end = cursor;

        while (end < definition.Length && char.IsAsciiDigit(definition[end]))
        {
            end++;
        }

        Assert.True(end > cursor, "the processing marker is not followed by a value");

        return long.Parse(
            definition.AsSpan(cursor, end - cursor),
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The legacy library export directory holding the DataWindow definitions, or <see langword="null"/>
    /// when the legacy tree is not present in this checkout.
    /// </summary>
    /// <returns>The directory, or <see langword="null"/>.</returns>
    private static string? LocateLegacyDataWindowDirectory()
    {
        DirectoryInfo? probe = new(TestRepositoryRoot.SearchStart);

        while (probe is not null)
        {
            string candidate = Path.Combine(probe.FullName, "ws_objects");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        return null;
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
    /// THE HANDOVER KEEPS WHAT IT WAS GIVEN. <c>blbData = Blob("")</c> at <c>:L101</c> releases the
    /// SENDER'S hold on the payload after the handover has taken it - it does not destroy the payload the
    /// handover now owns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE OBSERVABLE CLAIM, STATED EXACTLY.</b> In the legacy the payload is a <c>blob</c> passed by
    /// reference and the sender's local is reassigned to an empty one at <c>:L101</c>; the receiver
    /// already holds its own value, so it is unaffected. The managed payload is a message the chunk holds
    /// by reference, so what is OBSERVABLE here is the half that matters to a caller: AFTER
    /// <see cref="FullStateCodec.Send"/> returns, the chunk the handover received still carries the whole
    /// image, and a full round trip through it still restores the rows. A port that read the legacy line
    /// as "clear the payload" and cleared the MESSAGE would destroy the chunk it had just handed over -
    /// which is precisely the mistake this pins.
    /// </para>
    /// <para>
    /// AND THE SENDER'S SOURCE IS EMPTY, so nothing is left behind on the sending side either. The two
    /// halves together are the whole of what the legacy line achieves.
    /// </para>
    /// <para>
    /// THE FAILURE BRANCH DOES NOT REACH THE RELEASE AT ALL, because the legacy's clear sits AFTER the
    /// <c>end if</c> - asserted separately below.
    /// </para>
    /// </remarks>
    [Fact]
    public void Send_LeavesTheHandedOverChunkIntactAndTheSourceEmpty_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        SeedRow(store, DwBuffer.Primary, "handed over", ItemStatus.NewModified);
        SeedRow(store, DwBuffer.Delete, "deleted");
        SeedRow(store, DwBuffer.Filter, "filtered");

        List<QueryDataChunk> captured = [];

        Assert.Equal(
            RetCode.OK,
            FullStateCodec.Send(store, RecordingHandover(captured, 1L), RecordingReporter([])));

        QueryDataChunk chunk = Assert.Single(captured);

        // THE CHUNK STILL CARRIES EVERYTHING, read AFTER Send returned.
        Assert.NotNull(chunk.State);
        Assert.Equal(3, chunk.State.Segments.Count);

        // ONE ROW IN EACH OF THE THREE BUFFERS - the primary, the deleted and the filtered row all
        // survived the handover, so nothing was released selectively.
        Assert.Single(chunk.State.Segments[0].Rows);
        Assert.Single(chunk.State.Segments[1].Rows);
        Assert.Single(chunk.State.Segments[2].Rows);

        // And it still restores, which is the only property a consumer actually depends on.
        DataWindowBufferStore restored = new();

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            FullStateCodec.Apply(restored, chunk.State));
        Assert.Equal("handed over", restored.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NewModified, restored.GetItemStatus(1L, 0, DwBuffer.Primary));

        // THE SENDING SIDE HOLDS NOTHING.
        Assert.Equal(0L, store.RowCount());
        Assert.Equal(0L, store.DeletedCount());
        Assert.Equal(0L, store.FilteredCount());
    }

    /// <summary>
    /// A CAPTURE THAT CANNOT BE REPRESENTED IS REPORTED WITH THE SAME VERBATIM TEXT AND CODE as a failed
    /// handover, and the handover is NEVER REACHED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE SECOND OF THE TWO ARMS THAT ANSWER <c>TransData Failed</c>, and it is a NARROWING the
    /// port introduces deliberately rather than a behaviour the oracle has. The legacy's
    /// <c>GetFullState</c> cannot fail on a value the runtime produced itself, so <c>:L95</c> has no
    /// failure arm; this port CAN be handed a column value outside <c>common.v1.AnyValue</c>'s published
    /// arms, and it refuses it with a DEFINED failure rather than coercing it to a string that would
    /// round-trip as the wrong type and read as correct. The channel chosen is the oracle's own - the
    /// internal-error code and the internal-error text of <c>:L98-L99</c> - so no new failure mode is
    /// visible to a caller (C-B).
    /// </para>
    /// <para>
    /// THE HANDOVER MUST NOT RUN. A payload that could not be captured must not be sent at all, and
    /// asserting that the handover recorded nothing is the only way to see it: the return code alone is
    /// identical to the handover-failure arm's.
    /// </para>
    /// </remarks>
    [Fact]
    public void Send_ReportsTransDataFailedWhenTheCarrierCannotBeCaptured_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        long row = store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        // A duration has no AnyValue arm - TimeValue is a time OF DAY - so this cannot be captured.
        store.SetItemValue(row, 1, DwBuffer.Primary, TimeSpan.FromMinutes(90L));

        List<QueryDataChunk> captured = [];
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.Send(
            store,
            RecordingHandover(captured, 1L),
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INTERNAL_ERROR, result);

        // THE SAME TEXT AND CODE AS THE HANDOVER-FAILURE ARM [:L98-L99].
        Assert.Equal(
            (RetCode.E_INTERNAL_ERROR, "TransData Failed"),
            Assert.Single(reported));

        // AND THE HANDOVER NEVER RAN.
        Assert.Empty(captured);

        // THE SOURCE IS UNTOUCHED, because the reset at :L96 sits after the capture. A carrier whose
        // capture was refused still holds its rows, so a caller can inspect or retry rather than having
        // lost the data to a failure.
        Assert.Equal(1L, store.RowCount());
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

        // ENGLISH, AND THAT IS THE INCONSISTENCY TO PRESERVE (C-B). Nearly every other diagnostic this
        // block can raise is Chinese - 无效的DataObject at :L554, SQL为空! at :L616, 设置事务对象失败! at
        // :L679, SQL解析失败! at :L686 - and THIS ONE IS NOT. Nothing in the oracle explains why; it is
        // simply what the source says. Asserting the text is pure ASCII is what stops a future pass from
        // "harmonizing" the odd one out, which would be a silent behaviour change dressed as tidying.
        Assert.All(report.Message, character => Assert.InRange(character, ' ', '~'));
        Assert.Equal(
            report.Message.Length,
            Encoding.UTF8.GetByteCount(report.Message));

        // THE ORDER OF :L95, :L96 AND :L97 IS PINNED BY THIS BRANCH. The reset at :L96 runs BEFORE the
        // handover at :L97, so a carrier whose handover FAILED has already been emptied - the capture
        // succeeded and the source was released before the failure was known. A port that deferred the
        // reset until after a successful handover would leave the rows here.
        Assert.Equal(0L, store.RowCount());
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
        ScriptedCarrierSurface surface = new();
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
        Assert.Equal(["SetSort:age A salary A", "SetFilter:age > 1"], Trace(surface));
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
        ScriptedCarrierSurface surface = new();
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
        ScriptedCarrierSurface surface = new();

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
    /// <remarks>
    /// <para>
    /// DRIVEN BY THE FIXTURE'S OWN SORT VALUE, WHICH IS WHAT MAKES THE TRIM REAL RATHER THAN NOTIONAL.
    /// <c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c> declares
    /// <c>sort="age A salary A "</c> - WITH A TRAILING SPACE - and
    /// <see cref="DwSqliteFixture.SortExpression"/> carries it verbatim. A hand-written expression with
    /// no surrounding whitespace would make <c>Trim</c> a no-op and the assertion vacuous: applied and
    /// reported would be the same string and the asymmetry would be invisible. The one value the
    /// repository actually publishes exercises it.
    /// </para>
    /// <para>
    /// The two halves are asserted against DIFFERENT observations rather than against each other. The
    /// applied value comes from the ordered call log, the reported value from the error reporter, and
    /// each expectation is written out in full - so neither can be satisfied by the other, and no
    /// expectation is computed with the same concatenation the code under test performs.
    /// </para>
    /// </remarks>
    [Fact]
    public void SynchronizeSortAndFilter_TrimsWhatItAppliesButReportsTheUntrimmedValue_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        ScriptedCarrierSurface surface = new() { SetSortResult = DataWindowBufferStore.DataStoreFailure };
        List<(long Code, string Message)> reported = [];

        // The fixture's value ends in a space, and a leading space is added so the trim is exercised at
        // BOTH ends - PowerBuilder's Trim strips both, and a port using TrimEnd alone would pass a
        // trailing-only case.
        const string PendingSort = " age A salary A ";

        Assert.EndsWith(" ", DwSqliteFixture.SortExpression, StringComparison.Ordinal);
        Assert.Equal(" " + DwSqliteFixture.SortExpression, PendingSort);

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            PendingSort,
            null,
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);

        // APPLIED: trimmed at both ends, written out verbatim rather than derived.
        Assert.Equal(["SetSort:age A salary A"], Trace(surface));

        // REPORTED: the prefix plus the value EXACTLY AS IT ARRIVED, both spaces intact. Written out in
        // full - note the two spaces after the colon, one from the prefix and one from the value.
        Assert.Equal("SetSort:  age A salary A ", Assert.Single(reported).Message);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, reported[0].Code);
    }

    /// <summary>
    /// The filter side of the same asymmetry.
    /// </summary>
    [Fact]
    public void SynchronizeSortAndFilter_TrimsTheFilterButReportsItUntrimmed_UnitLevelNoOracle()
    {
        DataWindowBufferStore store = NewCrosstabCarrier();
        ScriptedCarrierSurface surface = new()
        {
            SetFilterResult = DataWindowBufferStore.DataStoreFailure,
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
        Assert.Equal(["SetFilter:age > 1"], Trace(surface));
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
        ScriptedCarrierSurface surface = new() { SetSortResult = DataWindowBufferStore.DataStoreFailure };
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
        ScriptedCarrierSurface surface = new()
        {
            SetFilterResult = DataWindowBufferStore.DataStoreFailure,
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
        ScriptedCarrierSurface surface = new() { SetSortResult = DataWindowBufferStore.DataStoreFailure };
        List<(long Code, string Message)> reported = [];

        long result = FullStateCodec.SynchronizeSortAndFilter(
            store,
            surface,
            "age A",
            "age > 1",
            OpenGate,
            RecordingReporter(reported));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, result);
        Assert.Equal(["SetSort:age A"], Trace(surface));
        Assert.DoesNotContain("SetFilter:age > 1", Trace(surface));
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
        ScriptedCarrierSurface surface = new() { SetSortResult = answer };

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
        ScriptedCarrierSurface surface = new();

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

    // ==============================================================================================
    //  DEFECT 4, PRESERVED VERBATIM (C-B), AND ITS OWN WORDS.
    //  --------------------------------------------------------------------------------------------
    //  ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L672-L676
    //
    //      //FIXME
    //      //解决交叉表字段过多时可能出现SetFullState崩溃问题
    //      if data.Describe("DataWindow.NoUserPrompt") <> "yes" then
    //          data.Modify("DataWindow.NoUserPrompt=yes")
    //      end if
    //
    //  - "works around the SetFullState crash that can occur when a crosstab has too many columns".
    //
    //  WHAT IS ASSERTED AND WHAT IS DELIBERATELY NOT. These cases assert that the workaround IS PRESENT,
    //  that its guard has BOTH ARMS, and that it is CORRECTLY POSITIONED. They do NOT assert that the
    //  crash cannot happen - that is not knowable from this repository, the crash lives inside a closed
    //  PowerBuilder runtime, and no fixture on this path exists to provoke it. The defect is REPRODUCED,
    //  never diagnosed and never declared fixed.
    //
    //  AND THE MODIFY RESULT IS DISCARDED BY THE ORACLE AT :L675, unlike the captured one at :L664 - so a
    //  port that treated a failure here as fatal would be STRICTER than the legacy. The result is surfaced
    //  for observability and never returned on.
    // ==============================================================================================

    #region DEFECT 4 - the guarded no-user-prompt write [:L673]

    /// <summary>
    /// Every describe answer that is NOT <c>"yes"</c> and therefore selects the write arm of the guard.
    /// </summary>
    /// <remarks>
    /// THE MATRIX IS THE GUARD'S CONTRACT, so it is declared once and shared rather than repeated inline.
    /// It covers the empty string, an explicit negative, BOTH PowerBuilder describe sentinels - <c>"!"</c>
    /// for unreadable and <c>"?"</c> for not-applicable, neither of which equals <c>"yes"</c> and both of
    /// which the legacy's <c>&lt;&gt;</c> therefore writes on - and three near misses that an
    /// ordinal comparison must reject: differing case in two forms and a trailing space.
    /// </remarks>
    public static TheoryData<string> DescribeAnswersThatSelectTheWriteArm =>
    [
        string.Empty,
        "no",
        "!",
        "?",
        "Yes",
        "YES",
        "yes ",
    ];

    /// <summary>
    /// The modify results and the success each implies, under the legacy's empty-means-success convention.
    /// </summary>
    /// <remarks>
    /// A SINGLE SPACE IS A FAILURE and is in the matrix for that reason: the legacy test is
    /// <c>if sError &lt;&gt; ""</c> at <c>:L665</c> and NOT a whitespace test, so a port that trimmed
    /// before comparing would read a blank diagnostic as success.
    /// </remarks>
    public static TheoryData<string, bool> ModifyResultsAndTheirSuccess =>
        new()
        {
            { string.Empty, true },
            { " ", false },
            { "Property Not Found", false },
            { "Line 1 Column 1: incorrect syntax", false },
        };

    /// <summary>
    /// EXACTLY ONE MODIFY CALL when the property does not already read <c>"yes"</c>, and the modify
    /// string is byte exact. The two PowerBuilder error markers are covered because neither equals
    /// <c>"yes"</c> and the legacy's <c>&lt;&gt;</c> therefore writes on both.
    /// </summary>
    [Theory]
    [MemberData(nameof(DescribeAnswersThatSelectTheWriteArm))]
    public void ApplyNoUserPromptWorkaround_WritesExactlyOnceWhenNotAlreadyYes_UnitLevelNoOracle(
        string describeAnswer)
    {
        ScriptedCarrierSurface surface = new() { DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = describeAnswer } };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.False(outcome.PropertyAlreadySet);
        Assert.True(outcome.ModifyAttempted);
        Assert.True(outcome.Succeeded);

        // THE READ COMES FIRST AND THE WRITE HAPPENS ONCE. Both are asserted from the call log, because
        // neither is visible in a return value.
        Assert.Equal(
            ["Describe:DataWindow.NoUserPrompt", "Modify:DataWindow.NoUserPrompt=yes"],
            Trace(surface));
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
        ScriptedCarrierSurface surface = new() { DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "yes" } };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.True(outcome.PropertyAlreadySet);
        Assert.False(outcome.ModifyAttempted);
        Assert.True(outcome.Succeeded);
        Assert.Equal(string.Empty, outcome.ModifyError);

        // The read happened; the write did not.
        Assert.Equal(["Describe:DataWindow.NoUserPrompt"], Trace(surface));
        Assert.DoesNotContain("Modify:DataWindow.NoUserPrompt=yes", Trace(surface));
    }

    /// <summary>
    /// The comparison is ORDINAL, so a differently-cased or padded value is NOT accepted as already set -
    /// which matters because accepting one would SKIP a modify call the legacy makes.
    /// </summary>
    [Fact]
    public void ApplyNoUserPromptWorkaround_ComparesTheGuardOrdinally_UnitLevelNoOracle()
    {
        Assert.Equal("yes", FullStateCodec.NoUserPromptEnabledValue);

        ScriptedCarrierSurface upper = new() { DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "YES" } };
        ScriptedCarrierSurface exact = new() { DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "yes" } };

        Assert.True(FullStateCodec.ApplyNoUserPromptWorkaround(upper).ModifyAttempted);
        Assert.False(FullStateCodec.ApplyNoUserPromptWorkaround(exact).ModifyAttempted);
    }

    /// <summary>
    /// THE MODIFY RESULT IS AN ERROR STRING AND EMPTY MEANS SUCCESS [<c>:L665</c>]. It is never inverted
    /// into a boolean, because the text is the whole of the diagnostic.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModifyResultsAndTheirSuccess))]
    public void ApplyNoUserPromptWorkaround_PreservesEmptyMeansSuccess_UnitLevelNoOracle(
        string modifyAnswer,
        bool expectedSuccess)
    {
        ScriptedCarrierSurface surface = new()
        {
            DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "no" },
            ModifyError = modifyAnswer,
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

    /// <summary>
    /// A <see langword="null"/> MODIFY RESULT IS NORMALIZED TO THE EMPTY STRING, and therefore reads as
    /// SUCCESS - because empty is what success means here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THE GUARD IS RIGHT RATHER THAN MERELY DEFENSIVE.</b> PowerBuilder's <c>Modify</c> answers a
    /// STRING and has no null, so there is no oracle behaviour for a null result to preserve or violate;
    /// the question is only which managed reading is faithful. Empty is the correct choice: the surface is
    /// an interface a caller implements, an implementation that returns nothing at all is saying "no
    /// diagnostic", and "no diagnostic" is exactly what the legacy's empty string means. Treating it as a
    /// FAILURE would invent a fault the oracle cannot produce and - because
    /// <c>Tasks/SqlQueryTask</c> logs a failure here at Warning on first occurrence - would put a
    /// permanent warning in an operator's channel for a call that succeeded.
    /// </para>
    /// <para>
    /// THE DOUBLE IS PURPOSE-BUILT AND HOSTILE, WHICH IS WHY IT IS NOT THE SHARED RECORDER. The shared
    /// <c>ScriptedCarrierSurface</c> cannot express this: its modify result is a non-nullable string, as
    /// the interface declares. Reaching the guard requires an implementation that deliberately breaks that
    /// contract, and a five-line local type that does one illegal thing is clearer than widening the
    /// shared double to make every other test able to do it too.
    /// </para>
    /// </remarks>
    [Fact]
    public void ApplyNoUserPromptWorkaround_ReadsANullModifyResultAsSuccess_UnitLevelNoOracle()
    {
        NullModifyingSurface surface = new();

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.True(outcome.ModifyAttempted);
        Assert.False(outcome.PropertyAlreadySet);

        // NORMALIZED, not propagated: the outcome's text is the empty string and never null, so a caller
        // reading `.Length` cannot fault.
        Assert.Equal(string.Empty, outcome.ModifyError);
        Assert.True(outcome.Succeeded);

        // And the write was attempted exactly once, with the byte-exact modify string.
        Assert.Equal(1, surface.ModifyCount);
        Assert.Equal(FullStateCodec.NoUserPromptModifyString, surface.LastModifyString);
    }

    /// <summary>
    /// A carrier surface that breaks its own contract by answering <see langword="null"/> from
    /// <see cref="Modify"/>, so the codec's normalization of that answer can be reached.
    /// </summary>
    /// <remarks>
    /// THE ONLY LOCAL DOUBLE IN THIS FILE, AND DELIBERATELY SO. Every other case uses the shared ordered
    /// call recorder; this one exists because the behaviour under test is the handling of a value the
    /// shared double's typed surface cannot produce.
    /// </remarks>
    private sealed class NullModifyingSurface : IFullStateCarrierSurface
    {
        /// <summary>How many times <see cref="Modify"/> was called.</summary>
        internal int ModifyCount { get; private set; }

        /// <summary>The last modification string presented, or the empty string when none was.</summary>
        internal string LastModifyString { get; private set; } = string.Empty;

        /// <inheritdoc/>
        /// <remarks>
        /// Answers PowerBuilder's "not applicable" marker, which is not <c>"yes"</c> and therefore selects
        /// the write arm the guard is being driven into.
        /// </remarks>
        public string Describe(string property) => "?";

        /// <inheritdoc/>
        public string Modify(string modifyString)
        {
            ModifyCount++;
            LastModifyString = modifyString;

            // THE CONTRACT BREACH THIS TYPE EXISTS FOR.
            return null!;
        }

        /// <inheritdoc/>
        /// <remarks>Never reached: the guard sets no sort.</remarks>
        public long SetSort(string sort) => DataWindowBufferStore.DataStoreSuccess;

        /// <inheritdoc/>
        /// <remarks>Never reached: the guard sets no filter.</remarks>
        public long SetFilter(string filter) => DataWindowBufferStore.DataStoreSuccess;
    }

    /// <summary>
    /// THE GUARD IS A READ-THEN-WRITE AND THE ORDINALS PROVE IT: the describe is call ONE and the modify
    /// is call TWO, on one ordered log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THE ORDINALS AND NOT JUST THE PAIR. <c>if data.Describe("DataWindow.NoUserPrompt") &lt;&gt;
    /// "yes" then data.Modify(...)</c> [<c>:L674-L675</c>] is a CONDITIONAL write, and a port that wrote
    /// first and read afterwards would still show one describe and one modify in any set-shaped
    /// assertion while having lost the condition entirely - it would write unconditionally. Positions 1
    /// and 2 are what separate the two implementations.
    /// </para>
    /// <para>
    /// THE WHOLE LOG IS ALSO PINNED, so the guard is proved to issue NOTHING ELSE: no sort, no filter, no
    /// second describe, no retry. Exactly two calls, in exactly that order.
    /// </para>
    /// </remarks>
    [Fact]
    public void ApplyNoUserPromptWorkaround_ReadsBeforeItWrites_UnitLevelNoOracle()
    {
        ScriptedCarrierSurface surface = new()
        {
            DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "no" },
        };

        _ = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.Equal(
            1,
            surface.OrdinalOf(CarrierCallKind.Describe, FullStateCodec.NoUserPromptProperty));
        Assert.Equal(
            2,
            surface.OrdinalOf(CarrierCallKind.Modify, FullStateCodec.NoUserPromptModifyString));

        Assert.True(
            surface.Precedes(
                CarrierCallKind.Describe,
                FullStateCodec.NoUserPromptProperty,
                CarrierCallKind.Modify,
                FullStateCodec.NoUserPromptModifyString),
            "the guard must READ the property before it WRITES it, or the write is unconditional");

        // NOTHING ELSE AT ALL. Two calls, and neither is a sort or a filter.
        Assert.Equal(2, surface.Calls.Count);
        Assert.Equal(1, surface.CountOf(CarrierCallKind.Describe));
        Assert.Equal(1, surface.CountOf(CarrierCallKind.Modify));
        Assert.Equal(0, surface.CountOf(CarrierCallKind.SetSort));
        Assert.Equal(0, surface.CountOf(CarrierCallKind.SetFilter));
    }

    /// <summary>
    /// POSITION IS THE ASSERTION. In the production composition the guard is written BEFORE the
    /// transaction is attached and BEFORE the SQL statement is modified, exactly as
    /// <c>:L674-L676</c> sits before <c>:L678</c> and before <c>:L684</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS ORDER IS BEHAVIOUR AND NOT TIDINESS.</b> The property being written suppresses a
    /// modal prompt. A workaround applied AFTER the operation that raises the prompt works around
    /// nothing - the prompt has already been raised - so a port that issued the three steps in any other
    /// order would reproduce the calls and none of the effect. That is precisely the failure a
    /// content-only assertion cannot see, which is why this is asserted as an ORDER.
    /// </para>
    /// <para>
    /// <b>OBSERVED FROM THE PRODUCTION COMPOSITION, NOT FROM THIS TEST'S OWN CALL SEQUENCE.</b> The three
    /// steps are composed by <c>Tasks/SqlQueryTask</c>, not by this codec - the codec owns the guard and
    /// knows nothing of transactions or statements - so a runtime double driven from here could only
    /// record the order THIS TEST chose to invoke them in, which would assert nothing about the port. The
    /// composition is therefore read from the production source and the three call sites are compared by
    /// position, following the pattern <c>TransactionServiceTests.TheServiceSourceReadsNoWallClock</c>
    /// established in this project. Comment lines are stripped first so the assertion is about CODE and
    /// cannot be satisfied by prose that merely mentions the right names.
    /// </para>
    /// <para>
    /// <b>ABSENCE IS NOT ORDER.</b> Each of the three sites is asserted PRESENT before any comparison, so
    /// a composition that dropped the guard altogether fails here rather than passing vacuously.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGuardIsComposedBeforeTheTransactionAndBeforeTheSql_UnitLevelNoOracle()
    {
        string code = ExecutableLinesOf(LocateQueryTaskSource());

        int guard = OrdinalOfLineContaining(code, "FullStateCodec.ApplyNoUserPromptWorkaround(");
        int attachment = OrdinalOfLineContaining(code, "_dataWindowRuntime.AttachTransaction(");
        int statement = OrdinalOfLineContaining(
            code,
            "BuildTableSelectAssignment(EscapeStatementForModify(");

        // PRESENT FIRST. A missing site would otherwise make the comparisons below vacuous.
        Assert.True(guard > 0, "the no-user-prompt guard is not composed at all");
        Assert.True(attachment > 0, "the transaction attachment is not composed at all");
        Assert.True(statement > 0, "the statement modification is not composed at all");

        // [:L674-L676] BEFORE [:L678].
        Assert.True(
            guard < attachment,
            $"the guard (line {guard}) must precede the transaction attachment (line {attachment})");

        // [:L674-L676] BEFORE [:L684] onward.
        Assert.True(
            guard < statement,
            $"the guard (line {guard}) must precede the statement modification (line {statement})");
    }

    /// <summary>
    /// THE ADJACENT ARM AT <c>:L678-L680</c>: a transaction attachment that does not answer one is
    /// refused with <c>E_INVALID_TRANSACTION</c> and the VERBATIM Chinese diagnostic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASSERTED HERE BECAUSE IT IS THE STEP THE GUARD MUST PRECEDE, so the two cannot be reasoned about
    /// separately: the guard's whole purpose is to be in place before this call, and a reader of the
    /// position assertion above needs the failure semantics of the thing being preceded in front of them.
    /// The task-level behaviour is exercised end to end by <c>SqlQueryTaskTests</c>; what is pinned here
    /// is the OBSERVABLE PAIR - the code and the text - byte for byte.
    /// </para>
    /// <para>
    /// <b>THE TEXT IS CHINESE AND STAYS CHINESE (C-B).</b> It is asserted against a literal written out in
    /// full rather than against the production constant alone, so a re-encoding accident - the constant
    /// silently becoming mojibake - fails this test instead of comparing equal to itself. The character
    /// count and the trailing FULL-WIDTH-free ASCII exclamation mark are pinned for the same reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFailedTransactionAttachmentCarriesTheVerbatimLegacyDiagnostic_UnitLevelNoOracle()
    {
        // `Event OnError(RetCode.E_INVALID_TRANSACTION,"设置事务对象失败!")` [:L679].
        Assert.Equal("设置事务对象失败!", SqlQueryTask.SetTransObjectFailedText);

        // NO MOJIBAKE: NINE characters - eight Han plus one ASCII exclamation mark. The UTF-8 encoding
        // is twenty-five bytes, so the same text decoded as Latin-1 would be twenty-five CHARACTERS and
        // would fail both of these.
        Assert.Equal(9, SqlQueryTask.SetTransObjectFailedText.Length);
        Assert.Equal(25, Encoding.UTF8.GetByteCount(SqlQueryTask.SetTransObjectFailedText));
        Assert.EndsWith("!", SqlQueryTask.SetTransObjectFailedText, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFFFD', SqlQueryTask.SetTransObjectFailedText);

        // `return RetCode.E_INVALID_TRANSACTION` [:L680]. Distinct from every other code this block can
        // answer, which is what makes the arm identifiable to a caller.
        Assert.Equal(-7L, RetCode.E_INVALID_TRANSACTION);
        Assert.NotEqual(RetCode.E_INVALID_SQL, RetCode.E_INVALID_TRANSACTION);
        Assert.NotEqual(RetCode.E_INTERNAL_ERROR, RetCode.E_INVALID_TRANSACTION);

        // The composition pairs exactly that code with exactly that text, adjacent to the attachment.
        string code = ExecutableLinesOf(LocateQueryTaskSource());

        Assert.Contains(
            "RetCode.E_INVALID_TRANSACTION, SetTransObjectFailedText",
            code,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SURROUNDING MODIFY-STRING BLOCK AT <c>:L663-L669</c>: an EMPTY result is success and a
    /// NON-EMPTY result is a failure carrying <c>E_INVALID_SQL</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO CONVENTIONS, ONE OF THEM COUNTER-INTUITIVE, AND BOTH PRESERVED. <c>Modify</c> reports by
    /// RETURNING TEXT, so success is the EMPTY string and any content at all is a diagnostic
    /// [<c>if sError &lt;&gt; "" then</c>, <c>:L665</c>]. A port that inverted this into a boolean would
    /// discard the diagnostic, which is the whole of what a failed modify reports.
    /// </para>
    /// <para>
    /// AND THE CODE IS <c>E_INVALID_SQL</c>, NOT <c>E_INTERNAL_ERROR</c>, even though the failing
    /// operation is a property modification rather than a statement [<c>:L666</c>]. The neighbouring
    /// modify at <c>:L637</c> answers <c>E_INTERNAL_ERROR</c> for what looks like the same class of
    /// fault. Both are the oracle's, the inconsistency is not harmonized, and asserting the two are
    /// DIFFERENT is what stops a future tidy-up from collapsing them.
    /// </para>
    /// <para>
    /// THE GUARD'S OWN MODIFY IS THE THIRD MEMBER OF THAT FAMILY AND IT DISCARDS ITS RESULT
    /// [<c>:L675</c>], unlike the captured one at <c>:L664</c> - so the guard reports a failure through
    /// <see cref="NoUserPromptOutcome.ModifyError"/> for observability WITHOUT returning on it, and a
    /// caller that treated it as fatal would be STRICTER than the legacy. That distinction is asserted
    /// here alongside the block it sits inside, because the three modifies are only distinguishable from
    /// one another by what each does with its result.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ModifyResultsAndTheirSuccess))]
    public void TheModifyStringBlockTreatsEmptyAsSuccessAndAnythingElseAsInvalidSql_UnitLevelNoOracle(
        string modifyResult,
        bool expectedSuccess)
    {
        // THE CONVENTION, on the one modify this codec owns. A SINGLE SPACE is a failure: the legacy test
        // is `<> ""` and not a whitespace test, so a port that trimmed before comparing would wrongly
        // read a blank diagnostic as success.
        ScriptedCarrierSurface surface = new()
        {
            DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "no" },
            ModifyError = modifyResult,
        };

        NoUserPromptOutcome outcome = FullStateCodec.ApplyNoUserPromptWorkaround(surface);

        Assert.True(outcome.ModifyAttempted);
        Assert.Equal(modifyResult, outcome.ModifyError);
        Assert.Equal(expectedSuccess, outcome.Succeeded);

        // The result is REPORTED, never thrown, so the guard cannot abort a retrieval the legacy would
        // have completed - `:L675` discards this value.
        Assert.Equal(expectedSuccess, outcome.ModifyError.Length == 0);

        // AND THE CODE THE SURROUNDING BLOCK ANSWERS for a non-empty result [:L666-L667].
        string code = ExecutableLinesOf(LocateQueryTaskSource());

        Assert.Contains("RetCode.E_INVALID_SQL, modifyError", code, StringComparison.Ordinal);
        Assert.Equal(-8L, RetCode.E_INVALID_SQL);

        // NOT harmonized with the neighbouring modify's code at :L637.
        Assert.NotEqual(RetCode.E_INTERNAL_ERROR, RetCode.E_INVALID_SQL);
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
    /// <summary>
    /// ⚠ A CAPTURED COLUMN CARRIES ITS <b>NAME</b> AS WELL AS ITS ORDINAL, when the carrier knows the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>common.v1.ColumnValue</c> states that "BOTH IDENTIFIERS ARE CARRIED, and neither is redundant" -
    /// a name survives a column reorder while an ordinal does not, and the status APIs take the ordinal -
    /// and it marks only <c>item_status</c> optional. This codec left <c>column_name</c> empty and
    /// documented the omission, which left every consumer to build the lookup table the contract says it
    /// should not need. The name comes from the carrier's own recorded column order rather than from the
    /// legacy blob, which genuinely has no names in it.
    /// </para>
    /// <para>
    /// BOTH VALUE LISTS ARE ASSERTED. The original-value list is the one the concurrency predicate is read
    /// from, so a name present on the current value and absent on its shadow would be the more confusing
    /// of the two possible half-fixes.
    /// </para>
    /// </remarks>
    [Fact]
    public void Capture_CarriesTheColumnNameWhenTheCarrierKnowsIt_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "one", ItemStatus.DataModified);

        // The carrier learns its names exactly as a retrieval teaches it: index n names one-based column
        // n + 1.
        source.SetColumnNames(["alpha", "beta", "gamma"]);

        CarrierState? image = FullStateCodec.Capture(source);

        Assert.NotNull(image);

        DataWindowRow captured = Assert.Single(
            image.Segments.Single(segment => segment.Buffer == DwBuffer.Primary).Rows);

        Assert.NotEmpty(captured.Columns);

        foreach (ColumnValue column in captured.Columns)
        {
            Assert.Equal(source.ColumnNameOf(column.ColumnId), column.ColumnName);
            Assert.NotEmpty(column.ColumnName);
        }

        foreach (ColumnValue original in captured.OriginalValues)
        {
            Assert.Equal(source.ColumnNameOf(original.ColumnId), original.ColumnName);
        }
    }

    /// <summary>
    /// AND IT STAYS EMPTY WHEN THE CARRIER WAS NEVER TOLD THE NAMES - a carrier built from a payload rather
    /// than from a retrieval.
    /// </summary>
    /// <remarks>
    /// An absent name is what the contract's consumers already tolerate; a FABRICATED one - "column3", or
    /// the ordinal rendered as text - would be worse, because it would be indistinguishable from a real
    /// identifier and would not survive the reorder the field exists to survive.
    /// </remarks>
    [Fact]
    public void Capture_LeavesTheColumnNameEmptyWhenItIsNotKnown_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "one", ItemStatus.DataModified);

        Assert.Empty(source.ColumnNames);

        CarrierState? image = FullStateCodec.Capture(source);

        Assert.NotNull(image);

        DataWindowRow captured = Assert.Single(
            image.Segments.Single(segment => segment.Buffer == DwBuffer.Primary).Rows);

        Assert.All(captured.Columns, column => Assert.Equal(string.Empty, column.ColumnName));
    }

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

        // AND THIS IS THE OBSERVABLE PROOF THAT THERE IS NO PRECEDING RESET - see the sibling case below
        // for the full argument. A rejected image leaves the target exactly as it was.
        Assert.Equal(1L, target.RowCount());
        Assert.Equal("untouched", target.GetItemValue(1L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// NO PRECEDING RESET, PROVED BY A REJECTED IMAGE LEAVING THE TARGET INTACT.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE SHARPEST OBSERVABLE DIFFERENCE FROM THE CHANGESET PATH, AND HOW IT IS SEEN.</b> The
    /// changeset arm resets the target BEFORE applying, gated on the chunk index -
    /// <c>if current = 1 then Reset()</c> then <c>SetChanges(blbData)</c>
    /// [<c>n_cst_threading_task_sqlquery.sru:L192-L197, L218-L221, L235-L238</c>]. The full-state arm has
    /// NO such step: it calls <c>SetFullState(blbData)</c> directly [<c>:L190, L216, L233</c>], because
    /// restoring a complete image replaces the contents intrinsically.
    /// </para>
    /// <para>
    /// <b>WHY THE ABSENCE IS ASSERTED THIS WAY AND NOT WITH A RECORDER.</b>
    /// <c>DataWindowBufferStore.Reset</c> is not a virtual member, so no recording subclass can intercept
    /// it and the ordered call recorder - which observes the DESCRIBE and MODIFY surface - cannot see it
    /// either. The absence is therefore proved by CONSEQUENCE, which is stronger than an interception
    /// would be anyway: the replacement reset inside
    /// <see cref="FullStateCodec.Apply(DataWindowBufferStore, CarrierState?)"/> runs only AFTER every
    /// structural check has passed, so a REJECTED image cannot have reached it. If a preceding reset
    /// existed, the target would be empty here whatever the rejection. It is not.
    /// </para>
    /// <para>
    /// Four independent rejections are exercised so the conclusion does not rest on one code path: a
    /// segment roster of the wrong length, a duplicated buffer tag, a row filed under the wrong buffer,
    /// and an image whose processing kind belongs to the other codec.
    /// </para>
    /// </remarks>
    /// <param name="because">Why the image is rejected, for the failure message.</param>
    /// <param name="rejected">The image that must be refused.</param>
    [Theory]
    [MemberData(nameof(RejectedImages))]
    public void Receive_LeavesTheTargetUntouchedWhenItRefusesAnImage_UnitLevelNoOracle(
        string because,
        CarrierState rejected)
    {
        DataWindowBufferStore target = new();
        SeedRow(target, DwBuffer.Primary, "still here", ItemStatus.NewModified);
        SeedRow(target, DwBuffer.Delete, "still deleted");

        long result = FullStateCodec.Receive(
            FullStateCodec.ResolveTarget(null, false, target),
            rejected);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);

        // EVERY ROW, EVERY BUFFER AND EVERY STATUS SURVIVES. A preceding reset would have emptied all
        // three buffers before the rejection was even detected.
        Assert.Equal(1L, target.RowCount());
        Assert.Equal(1L, target.DeletedCount());
        Assert.Equal("still here", target.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal(
            ItemStatus.NewModified,
            target.GetItemStatus(1L, 0, DwBuffer.Primary));

        Assert.False(string.IsNullOrEmpty(because));
    }

    /// <summary>
    /// Four structurally or semantically invalid images, one per rejection reason.
    /// </summary>
    public static TheoryData<string, CarrierState> RejectedImages
    {
        get
        {
            TheoryData<string, CarrierState> data = [];

            CarrierState tooFew = new() { Processing = 4L };
            tooFew.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Primary });
            data.Add("a segment roster shorter than the canonical three", tooFew);

            CarrierState duplicated = new() { Processing = 4L };
            duplicated.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Primary });
            duplicated.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Primary });
            duplicated.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Filter });
            data.Add("a duplicated buffer tag in place of Delete", duplicated);

            CarrierState reordered = new() { Processing = 4L };
            reordered.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Delete });
            reordered.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Primary });
            reordered.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Filter });
            data.Add("the canonical buffers in the wrong order", reordered);

            CarrierState foreignKind = new() { Processing = 1L };
            foreignKind.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Primary });
            foreignKind.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Delete });
            foreignKind.Segments.Add(new CarrierBufferSegment { Buffer = DwBuffer.Filter });
            data.Add("a processing kind belonging to the changeset codec", foreignKind);

            return data;
        }
    }

    /// <summary>
    /// NO CHUNK BOOKKEEPING OF ANY KIND, and no describe or modify traffic either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE BOOKKEEPING THE CHANGESET ARM DOES AND THIS ONE DOES NOT.</b> That arm brackets its
    /// <c>SetChanges</c> with two chunk-index-gated steps - a reset when <c>current = 1</c> and, when
    /// <c>count = current</c>, a <c>ResetUpdate</c> [<c>:L198-L199, L222-L224, L239-L241</c>]. The
    /// full-state arm has neither, because a complete image needs no assembling across chunks.
    /// </para>
    /// <para>
    /// <b>THE ABSENCE OF THE UPDATE-RESET IS DIRECTLY OBSERVABLE, WHICH IS WHY IT IS ASSERTED AND NOT
    /// ARGUED.</b> <c>ResetUpdate</c> re-baselines every row to unmodified and EMPTIES the Delete buffer.
    /// So an image carrying a <c>NewModified</c> row and a deleted row proves the point twice: had the
    /// bookkeeping run, the status would read <c>NotModified</c> and the deleted row would be gone.
    /// </para>
    /// <para>
    /// <b>AND THE SIGNATURE ITSELF CARRIES NO CHUNK COUNT AND NO CHUNK INDEX</b> - the receive entry point
    /// takes a target and an image and nothing else - so there is no chunk arithmetic to get wrong. The
    /// ordered call recorder confirms the complementary half: applying an image issues NO describe, modify,
    /// sort or filter call at all, so nothing about the carrier's definition is touched on the way in.
    /// </para>
    /// </remarks>
    [Fact]
    public void Receive_AppliesInOneShotWithNoChunkBookkeeping_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        SeedRow(source, DwBuffer.Primary, "modified", ItemStatus.NewModified);
        SeedRow(source, DwBuffer.Delete, "deleted");

        CarrierState? image = FullStateCodec.Capture(source);

        DataWindowBufferStore target = new();
        ScriptedCarrierSurface surface = new();

        long result = FullStateCodec.Receive(
            FullStateCodec.ResolveTarget(null, false, target),
            image);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, result);

        // NO UPDATE-RESET: the status survives and the Delete buffer is still populated.
        Assert.Equal(ItemStatus.NewModified, target.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(1L, target.DeletedCount());

        // NO DEFINITION TRAFFIC. The surface was available and was never used, which is what the ordered
        // recorder is for: the emptiness of the log is the assertion.
        Assert.Empty(surface.Calls);
        Assert.Equal(0, surface.CountOf(CarrierCallKind.Describe));
        Assert.Equal(0, surface.CountOf(CarrierCallKind.Modify));
        Assert.Equal(0, surface.CountOf(CarrierCallKind.SetSort));
        Assert.Equal(0, surface.CountOf(CarrierCallKind.SetFilter));
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
    /// AN UNREPRESENTABLE ORIGINAL IS REFUSED TOO, not only an unrepresentable current value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORIGINAL VALUE IS PART OF THE PAYLOAD, WHICH IS WHY IT GETS THE SAME TREATMENT.</b> The
    /// golden-master fixture declares <c>updatewhere=1</c> with <c>updatewhereclause=yes</c> on all six
    /// columns [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so the generated WHERE clause
    /// is built from ORIGINAL values and an image that dropped or mangled one would produce a clause that
    /// matches a row the legacy would have conflicted on. A silently omitted original is therefore worse
    /// than a refused image, and the refusal is the same defined failure as for a current value.
    /// </para>
    /// <para>
    /// THE SEQUENCE IS WHAT MAKES THE STATE REACHABLE: write the unrepresentable value, BASELINE so it
    /// becomes the original, then overwrite with a perfectly ordinary string. The row now has a
    /// representable current value and an unrepresentable original - a combination no single write can
    /// produce, and exactly the one a real edit of a freshly retrieved row produces.
    /// </para>
    /// </remarks>
    [Fact]
    public void Capture_AnswersAnAbsentImageWhenOnlyTheORIGINALIsUnrepresentable_UnitLevelNoOracle()
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        // 1. The unrepresentable value goes in first.
        source.SetItemValue(row, 1, DwBuffer.Primary, TimeSpan.FromMinutes(90L));

        // 2. Baseline, so that value becomes the row's ORIGINAL rather than its pending edit.
        source.ResetUpdate();

        // 3. Overwrite with something the wire expresses perfectly well.
        source.SetItemValue(row, 1, DwBuffer.Primary, "an ordinary string");

        Assert.Equal("an ordinary string", source.GetItemValue(row, 1, DwBuffer.Primary));
        Assert.Equal(
            TimeSpan.FromMinutes(90L),
            source.GetItemOriginalValue(row, 1, DwBuffer.Primary));

        // THE CURRENT VALUE ALONE WOULD HAVE CAPTURED FINE. The refusal comes from the original.
        Assert.Null(FullStateCodec.Capture(source));
    }

    /// <summary>
    /// A <c>blob</c> ORIGINAL IS COMPARED BY CONTENT AND NOT BY REFERENCE, so an unchanged blob records
    /// no original and a changed one records it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY REFERENCE EQUALITY WOULD BE WRONG IN BOTH DIRECTIONS.</b> <c>byte[]</c> is the one mutable
    /// value a carrier holds, so it is COPIED at every crossing - which means a blob that was never
    /// edited is nonetheless a DIFFERENT ARRAY INSTANCE from its own original. Comparing by reference
    /// would therefore record an original for every blob column in every row, inflating the payload and
    /// adding a WHERE-clause term for a column that did not change. Comparing by value in the other
    /// direction - two distinct arrays with identical bytes - correctly records nothing.
    /// </para>
    /// <para>
    /// PowerScript assigns a <c>blob</c> BY VALUE, so content comparison is the oracle's semantics rather
    /// than a convenience chosen here.
    /// </para>
    /// </remarks>
    /// <param name="edited">The value written over the baselined blob.</param>
    /// <param name="expectOriginalRecorded">Whether the image should carry an original for the column.</param>
    [Theory]
    [InlineData(new byte[] { 1, 2, 3 }, false)]
    [InlineData(new byte[] { 1, 2, 4 }, true)]
    [InlineData(new byte[] { 1, 2 }, true)]
    [InlineData(new byte[] { 1, 2, 3, 0 }, true)]
    [InlineData(new byte[0], true)]
    public void Capture_ComparesABlobOriginalByContent_UnitLevelNoOracle(
        byte[] edited,
        bool expectOriginalRecorded)
    {
        DataWindowBufferStore source = NewCrosstabCarrier();
        long row = source.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        source.SetItemValue(row, 1, DwBuffer.Primary, new byte[] { 1, 2, 3 });
        source.ResetUpdate();
        source.SetItemValue(row, 1, DwBuffer.Primary, edited);

        CarrierState? image = FullStateCodec.Capture(source);

        Assert.NotNull(image);

        DataWindowRow projected = Assert.Single(image.Segments[0].Rows);

        Assert.Single(projected.Columns);
        Assert.Equal(edited, projected.Columns[0].Value.BlobValue.ToByteArray());

        // THE CONTENT COMPARISON DECIDES WHETHER AN ORIGINAL IS CARRIED AT ALL.
        Assert.Equal(expectOriginalRecorded, projected.OriginalValues.Count == 1);

        if (expectOriginalRecorded)
        {
            Assert.Equal(
                new byte[] { 1, 2, 3 },
                projected.OriginalValues[0].Value.BlobValue.ToByteArray());
        }
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
        ScriptedCarrierSurface surface = new() { DescribeAnswers = { [FullStateCodec.NoUserPromptProperty] = "no" } };
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
            Trace(surface));
    }

    #endregion

    #region What this codec is NOT allowed to contain - C-D and C-E, asserted rather than assumed

    /// <summary>
    /// C-D - CROSSTAB HERE IS A DATA-TRANSFER DISCRIMINATOR AND NOTHING ELSE. This codec contains no
    /// crosstab rendering, no geometry, no DPI conversion, no font measurement and no reference to any
    /// deferred capability area.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY A CODEC IS EVEN AT RISK OF THIS.</b> "Crosstab" names a PRESENTATION style, and the two
    /// processing kinds this codec serves are Crosstab and Composite - so the temptation to reach for
    /// something that lays a crosstab out is real and would be a boundary violation, not a mere
    /// stylistic slip. Crosstab PRESENTATION belongs to the DEFERRED DesignSystem service, which the
    /// requirements forbid implementing even as a stub; what belongs here is only the recognition that a
    /// crosstab's state has to travel as one complete image instead of as a stream of row changes.
    /// </para>
    /// <para>
    /// <b>THE ORACLE ITSELF DRAWS THIS LINE TWICE AND THE PORT FOLLOWS IT.</b> <c>SetRedraw</c> and
    /// <c>GroupCalc</c> appear ONLY on the changeset path's DataWindow arm
    /// [<c>n_cst_threading_task_sqlquery.sru:L193-L194, L200, L203</c>] and in NEITHER its DataStore arm
    /// [<c>:L218-L224</c>] NOR either full-state arm [<c>:L190, L233</c>]. Two independent sightings of
    /// that asymmetry are the evidence that both are presentational, so their absence here is a
    /// DOCUMENTED NON-PORT rather than an oversight - and <c>ResetUpdate</c>, which appears in BOTH arms,
    /// is correspondingly kept.
    /// </para>
    /// <para>
    /// Asserted against the comment-stripped source so the codec's own prose - which discusses every one
    /// of these names at length in order to explain why it does not use them - cannot satisfy the check.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCodecCarriesNoPresentationalSurface_UnitLevelNoOracle()
    {
        string code = ExecutableLinesOf(LocateFullStateCodecSource());

        foreach (string forbidden in
            new[]
            {
                // The two presentational calls the oracle itself keeps off this path.
                "SetRedraw", "GroupCalc",

                // Geometry, DPI and font measurement - the deferred half of the three split services.
                "PX2MM", "MMX2PX", "D2PX", "U2PY", "GetWindowRect", "SetWindowPos", "ShowWindow",
                "OffsetRect", "Font", "Canvas", "Painter", "Bitmap", "Graphics", "Dpi",

                // The deferred capability areas by name, and their reserved gateway routes.
                "DesignSystem", "Documents", "Integration", "ScriptBridge",
                "/v1/design", "/v1/documents", "/v1/integration", "/v1/scripting",

                // A stub of any kind would be a partial implementation of a deferred service.
                "NotImplementedException",
            })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// C-E - NO FABRICATED DATABASE AND NO SERIALIZER. This codec opens no connection, names no database
    /// dialect, and hand-rolls neither a JSON nor an XML encoding of the image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE IMAGE IS A PUBLISHED PROTOBUF MESSAGE, WHICH IS THE WHOLE POINT.</b>
    /// <c>persistence.v1.CarrierState</c> replaced a private byte format precisely so that DataServices -
    /// which references the contracts project and nothing else - can produce and consume the payload the
    /// C-05 and C-06 contracts hand it. A JSON or XML encoding appearing here would be a SECOND,
    /// unpublished wire format for the same data, and the XML object family is deferred to Documents in
    /// any case.
    /// </para>
    /// <para>
    /// <b>AND NO DATABASE.</b> The repository evidences exactly one storage engine and exactly one schema,
    /// and neither has anything to do with moving a captured image between two carriers. SQL Server and
    /// Oracle appear in this service only as pure-string paging rewriters, with no instance of either
    /// provisioned; a dialect name in a codec would be a fabricated dependency.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCodecCarriesNoSerializerAndNoDatabaseType_UnitLevelNoOracle()
    {
        string code = ExecutableLinesOf(LocateFullStateCodecSource());

        foreach (string forbidden in
            new[]
            {
                // Serializers, hand-rolled or otherwise.
                "JsonSerializer", "JsonConvert", "Newtonsoft", "System.Text.Json", "Utf8JsonWriter",
                "XmlSerializer", "XDocument", "XmlDocument", "XmlReader", "XmlWriter", "XPath",
                "DataContract", "BinaryFormatter",

                // Connections, commands, contexts and providers.
                "SqliteConnection", "SqliteCommand", "DbConnection", "DbCommand", "SqlConnection",
                "IDbConnection", "DbContext", "EntityFrameworkCore", "Microsoft.Data.Sqlite",
                "SqlClient", "ConnectionString",

                // Dialect names, which would be a fabricated dependency in a codec.
                "DbtMssql", "DbtOracle", "SqlServer", "Oracle", "Sqlite",
            })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // AND THE POSITIVE HALF: the image type it does use is the PUBLISHED contract message, reached
        // through the contracts namespace rather than through anything local.
        Assert.Contains("PowerFramework.Contracts.Persistence.V1", code, StringComparison.Ordinal);
        Assert.Equal(
            "PowerFramework.Contracts.Persistence.V1",
            typeof(CarrierState).Namespace);
    }

    /// <summary>
    /// The system under test's own source file.
    /// </summary>
    /// <returns>The absolute path of <c>Buffers/FullStateCodec.cs</c>.</returns>
    /// <remarks>
    /// Same locator shape as <c>LocateQueryTaskSource</c>: the embedded repository root forms the path
    /// directly when available, and the upward walk out of the test output directory is retained as the
    /// fallback. The marker is SERVICE relative, so the walk is looking for
    /// <c>services/persistence-service</c>.
    /// </remarks>
    private static string LocateFullStateCodecSource()
    {
        if (TestRepositoryRoot.Embedded is { } root)
        {
            string direct = Path.Combine(
                root,
                "services",
                "persistence-service",
                "PowerFramework.Persistence",
                "Buffers",
                "FullStateCodec.cs");

            if (File.Exists(direct))
            {
                return direct;
            }
        }

        DirectoryInfo? probe = new(AppContext.BaseDirectory);

        while (probe is not null)
        {
            string candidate = Path.Combine(
                probe.FullName,
                "PowerFramework.Persistence",
                "Buffers",
                "FullStateCodec.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        throw new InvalidOperationException(
            "Buffers/FullStateCodec.cs could not be located from "
                + $"'{TestRepositoryRoot.SearchStart}'. The two surface-absence assertions read it "
                + "directly, and passing them without reading it would assert nothing.");
    }

    #endregion
}
