// ==============================================================================================
//  DataWindowBuffersTests - the buffer model, the carrier pair and the five preserved defects
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST
//      services/persistence-service/PowerFramework.Persistence/Buffers/DataWindowBuffers.cs
//
//  BEHAVIOURAL ORACLE (all READ ONLY per constraint C-C)
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru       the _ds carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru    the _ds_mt carrier
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L553-L557 the selection
//      ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L84-L110 the handover and
//                                                                               the codec selector
//      ws_objects/pfw.tests.pbl.src/dw_sqlite.srd                               the golden master
//
//  WHAT THIS SUITE IS FOR
//  --------------------------------------------------------------------------------------------
//  Five behaviours in the system under test look like defects and are not, so a well-meaning future
//  edit would "fix" each of them and silently break parity. Every one is pinned below, by name:
//
//      DEFECT 1  a delete preview against Primary!/Filter! does NOT advance the progress counter
//      DEFECT 2  the deleted-row contribution walks the Delete! buffer from its count down to 1
//      DEFECT 3  a row-cap handler answering 1 LIFTS the cap and lets the retrieve continue
//      DEFECT 4  the progress payload packs two 16-BIT words, so anything above 65535 truncates
//      DEFECT 5  the N-char rewriter returns its ARGUMENT ITSELF once it meets an N-prefixed literal
//
//  NO DATABASE, NO CONTAINER, NO REAL CLOCK (C-H). Every case runs against in-memory buffers and a
//  hand-written deterministic TimeProvider. The clock matters more than it looks: the throttle
//  threshold decides HOW MANY progress notifications an update emits, and a real clock would make
//  that count vary run to run, which is precisely what the Golden-Master technique forbids.
//
//  C-F SELF-AUDIT: every value in this file is synthetic. No credential, key, token, password,
//  connection string or certificate appears, and no test asserts on a statement's literal values.
// ==============================================================================================

using PowerFramework.Contracts.Common.V1;
using PowerFramework.Persistence.Buffers;

using Xunit;

// The same collision the system under test resolves the same way: the published contracts declare a
// MESSAGE named RetCode while PowerFramework.Shared.Kernel declares the STATIC CLASS of return-code
// constants, also named RetCode. Importing both namespaces would make every unqualified mention
// CS0104 ambiguous, so the kernel types this suite needs arrive by alias instead.
//
// RetCode IS DELIBERATELY NOT ALIASED HERE. GlobalUsings.cs owns this assembly's alias for it -
// `global using RetCode = PowerFramework.Shared.Kernel.RetCode;` - and binds exactly the same target,
// so the bare name already means the kernel class in this file. Restating it file-locally is not a
// harmless duplicate the way a repeated namespace import is: a file-local ALIAS that repeats a global
// ALIAS of the same name is error CS1537, so the one declaration has to live in one place. Bits is
// aliased below because it has no global alias, only the namespace import that would leave it
// unambiguous but unqualified.
using Bits = PowerFramework.Shared.Kernel.Bits;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// A deterministic monotonic clock. Advancing it is the only way time passes in this suite.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TimestampFrequency"/> is one thousand, so ONE TICK IS ONE MILLISECOND and the throttle
/// threshold reads directly as a tick count. That keeps every case below expressible in the same
/// units the legacy <c>CPU()</c> counter uses, which counts whole milliseconds.
/// </para>
/// <para>
/// Hand-written rather than taken from a testing package: the service adds no package reference for a
/// fake clock, and <see cref="TimeProvider"/> is designed to be subclassed for exactly this.
/// </para>
/// </remarks>
internal sealed class DeterministicClock : TimeProvider
{
    private long _timestamp;

    /// <inheritdoc/>
    public override long TimestampFrequency => 1_000L;

    /// <inheritdoc/>
    public override long GetTimestamp() => _timestamp;

    /// <summary>Moves the clock forward by a whole number of milliseconds.</summary>
    /// <param name="milliseconds">How far to advance.</param>
    internal void AdvanceMilliseconds(long milliseconds) => _timestamp += milliseconds;
}

/// <summary>
/// A recording stand-in for the parent SQL task, reproducing only the five members a carrier consumes.
/// </summary>
internal sealed class RecordingParentTask : ICarrierParentTask
{
    /// <inheritdoc/>
    public bool IsMainThread { get; set; } = true;

    /// <inheritdoc/>
    public bool IsNCharBinding { get; set; }

    /// <inheritdoc/>
    public bool IsCancelled { get; set; }

    /// <summary>The value <see cref="OnNotify"/> answers. One means stop - except on the row cap.</summary>
    internal long NotifyAnswer { get; set; }

    /// <summary>The value <see cref="OnDbError"/> answers.</summary>
    internal long DbErrorAnswer { get; set; }

    /// <summary>How many notifications were raised.</summary>
    internal int NotifyCount { get; private set; }

    /// <summary>Every notification payload, in order.</summary>
    internal List<long> Payloads { get; } = [];

    /// <summary>The notify code of the most recent notification.</summary>
    internal long LastNotifyCode { get; private set; }

    /// <summary>The string argument of the most recent notification.</summary>
    internal string LastText { get; private set; } = "sentinel-never-observed";

    /// <summary>The five scalars of the most recent database-error forward.</summary>
    internal (long Code, string Text, string Syntax, DwBuffer Buffer, long Row)? LastDbError
    {
        get;
        private set;
    }

    /// <inheritdoc/>
    public long OnDbError(
        long sqlDbCode,
        string sqlErrText,
        string sqlSyntax,
        DwBuffer buffer,
        long row)
    {
        LastDbError = (sqlDbCode, sqlErrText, sqlSyntax, buffer, row);

        return DbErrorAnswer;
    }

    /// <inheritdoc/>
    public long OnNotify(long notifyCode, long payload, string text)
    {
        NotifyCount++;
        LastNotifyCode = notifyCode;
        LastText = text;
        Payloads.Add(payload);

        return NotifyAnswer;
    }
}

/// <summary>
/// Pins the three-buffer model, the two carrier types, the affinity factory, the ownership handover
/// and all five preserved defects.
/// </summary>
public sealed class DataWindowBuffersTests
{
    private const long StatementCount = 5L;

    #region Helpers

    private static (WorkerDataWindowCarrier Carrier, DeterministicClock Clock,
        RecordingParentTask Task) NewWorker()
    {
        DeterministicClock clock = new();
        RecordingParentTask task = new() { IsMainThread = false };
        WorkerDataWindowCarrier carrier = new(clock);
        carrier.OnInit(task);

        return (carrier, clock, task);
    }

    private static (DataWindowCarrier Carrier, RecordingParentTask Task) NewMainThread()
    {
        RecordingParentTask task = new();
        DataWindowCarrier carrier = new(new DeterministicClock());
        carrier.OnInit(task);

        return (carrier, task);
    }

    private static void SeedModifiedRows(DataWindowBufferStore store, DwBuffer buffer, long count)
    {
        for (long index = 0L; index < count; index++)
        {
            store.AppendRow(buffer, ItemStatus.DataModified);
        }
    }

    private static int LowWord(long payload) => Bits.LoWord((uint)payload);

    private static int HighWord(long payload) => Bits.HiWord((uint)payload);

    /// <summary>
    /// Maps a statement-kind NAME onto the internal discriminator.
    /// </summary>
    /// <remarks>
    /// Theory data carries the name rather than the value because <c>SqlPreviewType</c> is internal to
    /// the service assembly: naming it in the signature of a public test method would be inconsistent
    /// accessibility, and widening the type merely to be testable would be the wrong trade.
    /// </remarks>
    private static SqlPreviewType StatementKind(string name) => name switch
    {
        "Select" => SqlPreviewType.Select,
        "Insert" => SqlPreviewType.Insert,
        "Update" => SqlPreviewType.Update,
        "Delete" => SqlPreviewType.Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown statement kind."),
    };

    private static CarrierThreadAffinity AffinityOf(bool isMainThread) =>
        isMainThread ? CarrierThreadAffinity.MainThread : CarrierThreadAffinity.WorkerThread;

    #endregion

    #region The three-buffer model and its counts

    /// <summary>
    /// The four counts report their own buffers and nothing else. <c>ModifiedCount</c> spans the
    /// primary AND filter buffers, which is derived from the update walk visiting both
    /// [<c>n_cst_threading_task_sqlupdate.sru:L172, L188</c>] while the deleted contribution is added
    /// separately [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L55-L60</c>].
    /// </summary>
    [Fact]
    public void CountsReportTheirOwnBuffers()
    {
        DataWindowBufferStore store = new();

        SeedModifiedRows(store, DwBuffer.Primary, 3L);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        SeedModifiedRows(store, DwBuffer.Filter, 2L);
        store.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(4L, store.RowCount());
        Assert.Equal(2L, store.FilteredCount());
        Assert.Equal(1L, store.DeletedCount());

        // Three modified in Primary plus two in Filter. The unmodified primary row and the deleted
        // row are both excluded.
        Assert.Equal(5L, store.ModifiedCount());
    }

    /// <summary>
    /// <c>AppendRow</c> answers the new row's ONE-BASED number, which is the post-add count and never
    /// the zero-based index a list would report (R9).
    /// </summary>
    [Fact]
    public void AppendRowAnswersOneBasedRowNumbers()
    {
        DataWindowBufferStore store = new();

        Assert.Equal(1L, store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified));
        Assert.Equal(2L, store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified));
        Assert.Equal(1L, store.AppendRow(DwBuffer.Filter, ItemStatus.NotModified));
    }

    /// <summary>
    /// A row number outside 1 through the buffer's count is a structural fault and fails fast rather
    /// than answering a null status, because every measured legacy call site bounds itself by the
    /// buffer's own count.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(3L)]
    public void RowAccessRejectsRowNumbersOutsideTheOneBasedRange(long row)
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.RowAt(row, DwBuffer.Primary);
            });
    }

    /// <summary>
    /// A buffer value outside the three declared members can only arise from an out-of-range cast, so
    /// it fails fast rather than answering an empty buffer.
    /// </summary>
    [Fact]
    public void BufferResolutionRejectsAnUndeclaredBuffer()
    {
        DataWindowBufferStore store = new();
        const DwBuffer undeclared = (DwBuffer)9;

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.AppendRow(undeclared, ItemStatus.NotModified);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetNextModified(0L, undeclared);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.RowsDiscard(1L, 1L, undeclared);
            });
    }

    #endregion

    #region Item status - the column-zero row-status convention

    /// <summary>
    /// Column index zero addresses THE ROW and any positive index addresses THAT COLUMN, which is the
    /// distinction two adjacent legacy lines rely on
    /// [<c>n_cst_thread_task_sqlupdate.sru:L160</c> versus <c>:L162</c>].
    /// </summary>
    [Fact]
    public void ItemStatusDistinguishesTheRowFromItsColumns()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.SetItemStatus(1L, 0, DwBuffer.Primary, ItemStatus.NewModified));
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.SetItemStatus(1L, 3, DwBuffer.Primary, ItemStatus.DataModified));

        Assert.Equal(ItemStatus.NewModified, store.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.DataModified, store.GetItemStatus(1L, 3, DwBuffer.Primary));

        // An untouched column reads NotModified, which is the status a freshly retrieved column
        // carries and the answer that makes the legacy's key-column comparison behave.
        Assert.Equal(ItemStatus.NotModified, store.GetItemStatus(1L, 4, DwBuffer.Primary));
    }

    /// <summary>
    /// The modified-row walk visits every modified row in ascending order and terminates on the
    /// no-more sentinel, reproducing the legacy <c>do while (nRow &gt; 0)</c> idiom.
    /// </summary>
    [Fact]
    public void ModifiedRowWalkVisitsModifiedRowsInAscendingOrder()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.AppendRow(DwBuffer.Primary, ItemStatus.DataModified);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.AppendRow(DwBuffer.Primary, ItemStatus.NewModified);

        Assert.Equal(new[] { 2L, 4L }, store.EnumerateModifiedRows(DwBuffer.Primary));

        long first = store.GetNextModified(0L, DwBuffer.Primary);
        long second = store.GetNextModified(first, DwBuffer.Primary);
        long none = store.GetNextModified(second, DwBuffer.Primary);

        Assert.Equal(2L, first);
        Assert.Equal(4L, second);
        Assert.Equal(0L, none);
    }

    #endregion

    #region Column values and the original values the concurrency check needs

    /// <summary>
    /// The first write per column captures the original; later writes do not overwrite it, so the
    /// concurrency check never compares against an intermediate value the database never saw.
    /// </summary>
    [Fact]
    public void OriginalValueIsCapturedOnceAndSurvivesLaterWrites()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 2, DwBuffer.Primary, "retrieved");
        store.ResetUpdate();

        Assert.Equal("retrieved", store.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("retrieved", store.GetItemOriginalValue(1L, 2, DwBuffer.Primary));

        store.SetItemValue(1L, 2, DwBuffer.Primary, "edited once");
        store.SetItemValue(1L, 2, DwBuffer.Primary, "edited twice");

        Assert.Equal("edited twice", store.GetItemValue(1L, 2, DwBuffer.Primary));
        Assert.Equal("retrieved", store.GetItemOriginalValue(1L, 2, DwBuffer.Primary));
    }

    /// <summary>
    /// Writing a value does NOT change any item status. The legacy relies on PowerBuilder's implicit
    /// flip and exploits it with a self-assignment
    /// [<c>n_cst_thread_task_sqlupdate.sru:L155-L167</c>]; the port models the two explicitly, which
    /// is what makes that workaround expressible.
    /// </summary>
    [Fact]
    public void WritingAValueDoesNotChangeAnyItemStatus()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 1, DwBuffer.Primary, 42);

        Assert.Equal(ItemStatus.NotModified, store.GetItemStatus(1L, 0, DwBuffer.Primary));
        Assert.Equal(ItemStatus.NotModified, store.GetItemStatus(1L, 1, DwBuffer.Primary));
        Assert.Equal(0L, store.ModifiedCount());
    }

    /// <summary>
    /// A row reports its assigned columns in ASCENDING order however they were written. Deterministic
    /// order matters because a codec that serialized in enumeration order would otherwise produce a
    /// payload whose byte layout varied between runs, which would break the characterization
    /// comparison the parity model rests on.
    /// </summary>
    [Fact]
    public void ARowReportsItsAssignedColumnsInAscendingOrder()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 6, DwBuffer.Primary, "birth");
        store.SetItemValue(1L, 1, DwBuffer.Primary, "id");
        store.SetItemValue(1L, 4, DwBuffer.Primary, "address");

        Assert.Equal(new[] { 1, 4, 6 }, store.RowAt(1L, DwBuffer.Primary).AssignedColumnNumbers);
    }

    /// <summary>
    /// A value is never addressed by the column-zero sentinel, so passing it is a caller fault rather
    /// than an alternative meaning (R9 item 4).
    /// </summary>
    [Fact]
    public void ValueAccessRejectsTheRowStatusSentinel()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetItemValue(1L, 0, DwBuffer.Primary);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.GetItemOriginalValue(1L, 0, DwBuffer.Primary);
            });
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = store.SetItemValue(1L, 0, DwBuffer.Primary, "x");
            });
    }

    // ==========================================================================================
    //  BLOB VALUES ARE ISOLATED ON EVERY CROSSING, AND THE REASON IS THE CONCURRENCY CHECK
    //  ----------------------------------------------------------------------------------------
    //  `byte[]` is the ONE value type in the published domain that is mutable, and the carrier's whole
    //  purpose is to hold a CURRENT value and its ORIGINAL side by side so that `updatewhere=1`
    //  [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14] can put the original into the generated where
    //  clause. Share one array between the two and the two stop being two: mutating the caller's array
    //  in place moves the original ALONG WITH the current, the comparison compares a value against
    //  itself, and the optimistic-concurrency check silently passes for every row - which is a lost
    //  update that no row-count assertion would notice.
    //
    //  Three crossings therefore copy: INGRESS (the write), the ORIGINAL SNAPSHOT taken from it, and
    //  EGRESS (both reads). Every other value in the domain is immutable, so it is handed over as-is.
    // ==========================================================================================

    /// <summary>
    /// A caller that mutates the array it wrote cannot reach the value the carrier holds.
    /// </summary>
    [Fact]
    public void MutatingTheArrayThatWasWrittenDoesNotReachTheStoredValue()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        byte[] written = [1, 2, 3];

        store.SetItemValue(1L, 3, DwBuffer.Primary, written);

        written[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
    }

    /// <summary>
    /// A caller that mutates the array it READ cannot reach the value the carrier holds either.
    /// </summary>
    [Fact]
    public void MutatingTheArrayThatWasReadDoesNotReachTheStoredValue()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 3, DwBuffer.Primary, new byte[] { 1, 2, 3 });

        byte[] read = Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary));

        read[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));

        // Two reads never hand back the same instance, which is what makes the guarantee hold for a
        // caller that keeps one of them.
        Assert.NotSame(read, store.GetItemValue(1L, 3, DwBuffer.Primary));
    }

    /// <summary>
    /// THE CASE THE ISOLATION EXISTS FOR: an in-place mutation must not move the ORIGINAL along with the
    /// current, because the two are what the concurrency check compares.
    /// </summary>
    [Fact]
    public void AnInPlaceMutationCannotMoveTheOriginalAlongWithTheCurrent()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        byte[] retrieved = [1, 2, 3];

        store.SetItemValue(1L, 3, DwBuffer.Primary, retrieved);
        store.ResetUpdate();

        // The edit, performed the way a careless caller would perform it - in place on the array it
        // still holds a reference to, with no second SetItemValue at all.
        retrieved[0] = 99;

        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));

        // And the honest edit still separates the two, which is the property the check depends on.
        store.SetItemValue(1L, 3, DwBuffer.Primary, new byte[] { 4, 5, 6 });

        Assert.Equal(
            new byte[] { 4, 5, 6 },
            Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));
        Assert.NotSame(
            store.GetItemValue(1L, 3, DwBuffer.Primary),
            store.GetItemOriginalValue(1L, 3, DwBuffer.Primary));
    }

    /// <summary>
    /// An EMPTY blob survives as an empty blob rather than as a null or a one-element array, so the
    /// round trip has no length-dependent hole.
    /// </summary>
    /// <remarks>
    /// INSTANCE IDENTITY IS DELIBERATELY NOT ASSERTED HERE, unlike in the non-empty cases above. A
    /// zero-length array has no element to mutate, so it carries no aliasing hazard at all, and the
    /// runtime hands out a single interned instance for every zero-length array of a given element type -
    /// which means the copy taken on ingress legitimately IS the caller's instance. Asserting otherwise
    /// would be asserting a runtime implementation detail that buys no safety.
    /// </remarks>
    [Fact]
    public void AnEmptyBlobSurvivesAsAnEmptyBlob()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        store.SetItemValue(1L, 3, DwBuffer.Primary, new byte[] { });
        store.ResetUpdate();

        Assert.Empty(Assert.IsType<byte[]>(store.GetItemValue(1L, 3, DwBuffer.Primary)));
        Assert.Empty(Assert.IsType<byte[]>(store.GetItemOriginalValue(1L, 3, DwBuffer.Primary)));
    }

    /// <summary>
    /// Immutable values are handed over AS THEY ARE, because copying them would buy nothing and the
    /// isolation is deliberately narrow.
    /// </summary>
    [Fact]
    public void AnImmutableValueIsHandedOverWithoutBeingCopied()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        string written = "Contoso";

        store.SetItemValue(1L, 2, DwBuffer.Primary, written);

        Assert.Same(written, store.GetItemValue(1L, 2, DwBuffer.Primary));
    }

    #endregion

    #region RowsMove, RowsDiscard and the two resets

    /// <summary>
    /// Reproduces the legacy's own most important movement:
    /// <c>RowsMove(1, FilteredCount(), Filter!, Data, RowCount() + 1, Primary!)</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L109</c>], which folds the whole filter buffer onto the END
    /// of the primary buffer of the SAME carrier.
    /// </summary>
    [Fact]
    public void RowsMoveFoldsTheFilterBufferOntoTheEndOfPrimary()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);
        store.SetItemValue(1L, 1, DwBuffer.Primary, "primary-1");
        store.AppendRow(DwBuffer.Filter, ItemStatus.DataModified);
        store.SetItemValue(1L, 1, DwBuffer.Filter, "filtered-1");
        store.AppendRow(DwBuffer.Filter, ItemStatus.DataModified);
        store.SetItemValue(2L, 1, DwBuffer.Filter, "filtered-2");

        long answer = store.RowsMove(
            1L,
            store.FilteredCount(),
            DwBuffer.Filter,
            store,
            store.RowCount() + 1L,
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, answer);
        Assert.Equal(3L, store.RowCount());
        Assert.Equal(0L, store.FilteredCount());
        Assert.Equal("primary-1", store.GetItemValue(1L, 1, DwBuffer.Primary));
        Assert.Equal("filtered-1", store.GetItemValue(2L, 1, DwBuffer.Primary));
        Assert.Equal("filtered-2", store.GetItemValue(3L, 1, DwBuffer.Primary));
    }

    /// <summary>
    /// C-B. An empty range answers failure and moves nothing, which is what
    /// <c>RowsMove(1, FilteredCount(), ...)</c> does when nothing is filtered. The legacy ignores the
    /// answer, so no silent no-op success is invented.
    /// </summary>
    [Fact]
    public void RowsMoveAnswersFailureForAnEmptyRangeAndMovesNothing()
    {
        DataWindowBufferStore store = new();
        store.AppendRow(DwBuffer.Primary, ItemStatus.NotModified);

        long answer = store.RowsMove(
            1L,
            store.FilteredCount(),
            DwBuffer.Filter,
            store,
            store.RowCount() + 1L,
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, answer);
        Assert.Equal(1L, store.RowCount());
        Assert.Equal(0L, store.FilteredCount());
    }

    /// <summary>
    /// A chunk moves into a separate carrier, as at
    /// <c>n_cst_thread_task_sqlquery.sru:L160</c>, and a range past the end is refused.
    /// </summary>
    [Fact]
    public void RowsMoveTransfersBetweenCarriersAndRefusesAnUnaddressableRange()
    {
        DataWindowBufferStore source = new();
        DataWindowBufferStore temporary = new();
        SeedModifiedRows(source, DwBuffer.Primary, 3L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            source.RowsMove(1L, 2L, DwBuffer.Primary, temporary, 1L, DwBuffer.Primary));
        Assert.Equal(1L, source.RowCount());
        Assert.Equal(2L, temporary.RowCount());

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            source.RowsMove(1L, 9L, DwBuffer.Primary, temporary, 1L, DwBuffer.Primary));
        Assert.Equal(1L, source.RowCount());
        Assert.Equal(2L, temporary.RowCount());

        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = source.RowsMove(1L, 1L, DwBuffer.Primary, null!, 1L, DwBuffer.Primary);
            });
    }

    /// <summary>
    /// C-B. <c>RowsDiscard</c> is the workaround for the documented prohibition on using
    /// <c>Reset</c> to clear data between chunks [<c>n_cst_thread_task_sqlquery.sru:L176-L182</c>], so
    /// it removes rows from ONE buffer and leaves the others alone - unlike <see cref="Reset"/>.
    /// </summary>
    [Fact]
    public void RowsDiscardRemovesFromOneBufferOnly()
    {
        DataWindowBufferStore store = new();
        SeedModifiedRows(store, DwBuffer.Primary, 3L);
        SeedModifiedRows(store, DwBuffer.Filter, 2L);

        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            store.RowsDiscard(1L, 3L, DwBuffer.Primary));
        Assert.Equal(0L, store.RowCount());
        Assert.Equal(2L, store.FilteredCount());

        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            store.RowsDiscard(1L, 5L, DwBuffer.Filter));
        Assert.Equal(2L, store.FilteredCount());
    }

    /// <summary>
    /// The two resets are DIFFERENT OPERATIONS and neither substitutes for the other:
    /// <c>Reset</c> removes every row from all three buffers, while <c>ResetUpdate</c> keeps every
    /// row, clears the update flags, re-baselines the original values and empties only the delete
    /// buffer.
    /// </summary>
    [Fact]
    public void ResetAndResetUpdateAreNotInterchangeable()
    {
        DataWindowBufferStore resetTarget = new();
        SeedModifiedRows(resetTarget, DwBuffer.Primary, 2L);
        SeedModifiedRows(resetTarget, DwBuffer.Filter, 1L);
        resetTarget.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, resetTarget.Reset());
        Assert.Equal(0L, resetTarget.RowCount());
        Assert.Equal(0L, resetTarget.FilteredCount());
        Assert.Equal(0L, resetTarget.DeletedCount());

        DataWindowBufferStore resetUpdateTarget = new();
        SeedModifiedRows(resetUpdateTarget, DwBuffer.Primary, 2L);
        resetUpdateTarget.SetItemValue(1L, 1, DwBuffer.Primary, "retrieved");
        resetUpdateTarget.SetItemStatus(1L, 5, DwBuffer.Primary, ItemStatus.DataModified);
        SeedModifiedRows(resetUpdateTarget, DwBuffer.Filter, 1L);
        resetUpdateTarget.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, resetUpdateTarget.ResetUpdate());

        // Rows survive; only the flags and the delete buffer are cleared.
        Assert.Equal(2L, resetUpdateTarget.RowCount());
        Assert.Equal(1L, resetUpdateTarget.FilteredCount());
        Assert.Equal(0L, resetUpdateTarget.DeletedCount());
        Assert.Equal(0L, resetUpdateTarget.ModifiedCount());
        Assert.Equal(
            ItemStatus.NotModified,
            resetUpdateTarget.GetItemStatus(1L, 5, DwBuffer.Primary));

        // And the originals are re-baselined onto the current values, which is what makes freshly
        // delivered rows read as unmodified.
        Assert.Equal("retrieved", resetUpdateTarget.GetItemOriginalValue(1L, 1, DwBuffer.Primary));
    }

    #endregion

    #region The codec discriminator

    /// <summary>
    /// Only processing kinds 4 and 5 - crosstab and composite, named by the legacy's own comment at
    /// <c>n_cst_thread_task_sqlquery.sru:L94</c> - select the full-state path. Every other value,
    /// including PowerBuilder's <c>"!"</c> and <c>"?"</c> describe markers and the golden master's own
    /// <c>processing=1</c>, selects the changeset path.
    /// </summary>
    [Theory]
    [InlineData("4", true)]
    [InlineData("5", true)]
    [InlineData("1", false)]
    [InlineData("0", false)]
    [InlineData("3", false)]
    [InlineData("6", false)]
    [InlineData("!", false)]
    [InlineData("?", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not a number", false)]
    public void OnlyCrosstabAndCompositeSelectFullStateTransfer(
        string? describeResult,
        bool expectsFullState)
    {
        DataWindowBufferStore store = new()
        {
            Processing = DataWindowProcessing.FromDescribe(describeResult),
        };

        Assert.Equal(expectsFullState, store.RequiresFullStateTransfer);
        Assert.Equal(expectsFullState, store.Processing.SelectsFullStateTransfer);
    }

    /// <summary>
    /// The two named kinds keep the numeric values the legacy dispatches on, and an unassigned carrier
    /// takes the changeset path.
    /// </summary>
    [Fact]
    public void ProcessingKindsKeepTheirLegacyNumericValues()
    {
        Assert.Equal(4L, DataWindowProcessing.Crosstab.Value);
        Assert.Equal(5L, DataWindowProcessing.Composite.Value);
        Assert.Equal(0L, DataWindowProcessing.Unassigned.Value);
        Assert.False(DataWindowProcessing.Unassigned.SelectsFullStateTransfer);
        Assert.False(new DataWindowBufferStore().RequiresFullStateTransfer);
    }

    #endregion

    #region The main-thread carrier - the six members and the three events

    /// <summary>
    /// <c>SetMaxRows</c> answers the FRAMEWORK success value, which is zero, and not the DataStore
    /// success value, which is one [<c>n_cst_thread_task_sqlbase_ds.sru:L69-L71</c>]. C-B: no
    /// validation is added, so a negative cap is stored and simply behaves as no limit.
    /// </summary>
    [Fact]
    public void SetMaxRowsAnswersTheFrameworkSuccessCodeAndValidatesNothing()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        Assert.Equal(RetCode.OK, carrier.SetMaxRows(10L));
        Assert.Equal(RetCode.OK, carrier.SetMaxRows(0L));
        Assert.Equal(RetCode.OK, carrier.SetMaxRows(-7L));
        Assert.NotEqual(DataWindowBufferStore.DataStoreSuccess, RetCode.OK);
    }

    /// <summary>
    /// The update-completion event captures the three counts, and they are reported by the three
    /// getters [<c>n_cst_thread_task_sqlbase_ds.sru:L162-L166, L73-L80</c>].
    /// </summary>
    [Fact]
    public void UpdateEndCapturesTheThreeCounts()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        carrier.OnUpdateEnd(7L, 11L, 13L);

        Assert.Equal(7L, carrier.GetInsertedCount());
        Assert.Equal(11L, carrier.GetUpdatedCount());
        Assert.Equal(13L, carrier.GetDeletedCount());
    }

    /// <summary>
    /// The update's deleted-row count and the delete BUFFER's row count are different facts that
    /// happen to have near-identical legacy names. Both are asserted together here precisely so that
    /// a future reader who conflates them sees this test fail.
    /// </summary>
    [Fact]
    public void UpdateDeletedCountIsNotTheDeleteBufferCount()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);

        Assert.Equal(2L, carrier.DeletedCount());
        Assert.Equal(0L, carrier.GetDeletedCount());

        carrier.OnUpdateEnd(0L, 0L, 2L);
        carrier.ResetUpdate();

        Assert.Equal(0L, carrier.DeletedCount());
        Assert.Equal(2L, carrier.GetDeletedCount());
    }

    /// <summary>
    /// <c>ClearState</c> zeroes ALL FIVE pieces of carrier state
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L82-L87</c>]. The row cap is not directly readable, so it
    /// is observed the only way a caller can: a retrieve row past the old cap no longer notifies.
    /// </summary>
    [Fact]
    public void ClearStateZeroesAllFiveStateFields()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();

        carrier.SetMaxRows(2L);
        carrier.OnUpdateEnd(3L, 4L, 5L);
        task.NotifyAnswer = 0L;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(3L));
        Assert.True(carrier.IsRowsExceeded());
        Assert.Equal(1, task.NotifyCount);

        carrier.ClearState();

        Assert.False(carrier.IsRowsExceeded());
        Assert.Equal(0L, carrier.GetInsertedCount());
        Assert.Equal(0L, carrier.GetUpdatedCount());
        Assert.Equal(0L, carrier.GetDeletedCount());

        // The cap is gone: a row far past the old cap neither notifies nor stops the retrieve.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(99L));
        Assert.Equal(1, task.NotifyCount);
    }

    /// <summary>
    /// The database-error event forwards all five raw scalars to the parent task and PROPAGATES the
    /// hook's answer [<c>n_cst_thread_task_sqlbase_ds.sru:L159</c>]. The carrier assembles nothing.
    /// </summary>
    [Fact]
    public void DbErrorForwardsFiveScalarsAndPropagatesTheAnswer()
    {
        (DataWindowCarrier carrier, RecordingParentTask task) = NewMainThread();
        task.DbErrorAnswer = DataWindowBufferStore.EventStop;

        long answer = carrier.OnDbError(-1234L, "constraint violated", "SELECT 1", DwBuffer.Filter, 9L);

        Assert.Equal(DataWindowBufferStore.EventStop, answer);
        Assert.Equal((-1234L, "constraint violated", "SELECT 1", DwBuffer.Filter, 9L), task.LastDbError);
    }

    /// <summary>
    /// An uninitialized carrier fails fast rather than inventing a benign default: the legacy
    /// dereferences its parent reference unconditionally, and softening a structural fault into
    /// warn-and-continue would be a behavioural change dressed as robustness.
    /// </summary>
    [Fact]
    public void AnUninitializedCarrierFailsFast()
    {
        DataWindowCarrier carrier = new(new DeterministicClock());

        Assert.Null(carrier.ParentTask);
        Assert.Null(carrier.ParentThreadAffinity);
        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = carrier.OnDbError(0L, "text", "syntax", DwBuffer.Primary, 1L);
            });
        Assert.Throws<ArgumentNullException>(() => carrier.OnInit(null!));
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = new DataWindowCarrier(null!);
            });
    }

    /// <summary>
    /// Initialization captures the parent task and derives the parent THREAD's affinity from it,
    /// which is a different fact from the carrier TYPE's own affinity
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L62-L63</c>].
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InitializationDerivesTheParentThreadAffinity(bool isMainThread)
    {
        RecordingParentTask task = new() { IsMainThread = isMainThread };
        DataWindowCarrier carrier = new(new DeterministicClock());

        carrier.OnInit(task);

        Assert.Same(task, carrier.ParentTask);
        Assert.Equal(AffinityOf(isMainThread), carrier.ParentThreadAffinity);

        // The TYPE's affinity is unaffected by which thread initialized it.
        Assert.Equal(CarrierThreadAffinity.MainThread, carrier.Affinity);
    }

    #endregion

    #region DEFECT 5 - the N-char rewriter

    /// <summary>
    /// The rewriter prefixes every string literal with <c>N</c>, and the eight cases below are the
    /// whole of its grammar [<c>n_cst_thread_task_sqlbase_ds.sru:L89-L147</c>].
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("SELECT 1", "SELECT 1")]
    [InlineData("SELECT 'a'", "SELECT N'a'")]
    [InlineData("'abc'", "N'abc'")]
    [InlineData("SELECT 'a','b'", "SELECT N'a',N'b'")]
    [InlineData("SELECT 'a''b'", "SELECT N'a''b'")]
    [InlineData("SELECT ''", "SELECT N''")]
    [InlineData("SELECT 'a", "SELECT N'a")]
    // A literal that OPENS with an escaped quote. This is the one path on which the escape branch
    // itself has to open the pending segment, because the opening quote reset it one position earlier
    // [n_cst_thread_task_sqlbase_ds.sru:L118].
    [InlineData("SELECT '''a'", "SELECT N'''a'")]
    [InlineData(
        "UPDATE COMPANY SET address='x' WHERE name='y'",
        "UPDATE COMPANY SET address=N'x' WHERE name=N'y'")]
    public void NCharRewriterPrefixesEveryLiteral(string statement, string expected)
    {
        (DataWindowCarrier carrier, RecordingParentTask task) = NewMainThread();
        task.IsNCharBinding = true;

        long answer = carrier.OnSqlPreview(SqlPreviewType.Update, statement, DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Equal(expected, carrier.SqlPreviewStatement);
    }

    /// <summary>
    /// C-B DEFECT 5. Once the scan meets a literal ALREADY prefixed with <c>N</c> it answers the
    /// argument ITSELF [<c>n_cst_thread_task_sqlbase_ds.sru:L125-L127</c>]. Two things this pins that
    /// an "idempotent rewrite" would break: the answer is the SAME REFERENCE, and the test is per
    /// STATEMENT - the second case below has an unprefixed literal AFTER the prefixed one and is still
    /// returned wholly unrewritten, discarding everything the scan had already accumulated.
    /// </summary>
    [Theory]
    [InlineData("SELECT N'a'")]
    [InlineData("SELECT N'a','b'")]
    [InlineData("SELECT 'a',N'b'")]
    public void NCharRewriterAnswersItsArgumentItselfWhenAlreadyPrefixed(string statement)
    {
        (DataWindowCarrier carrier, RecordingParentTask task) = NewMainThread();
        task.IsNCharBinding = true;

        carrier.OnSqlPreview(SqlPreviewType.Update, statement, DwBuffer.Primary);

        Assert.Same(statement, carrier.SqlPreviewStatement);
    }

    /// <summary>
    /// Both conditions must hold before anything is rewritten: the statement kind must be an update or
    /// an insert, AND the connection must use N-char binding
    /// [<c>n_cst_thread_task_sqlbase_ds.sru:L168-L169</c>]. The answer is the continue value either
    /// way, unconditionally [<c>:L173</c>].
    /// </summary>
    [Theory]
    [InlineData("Update", true, true)]
    [InlineData("Insert", true, true)]
    [InlineData("Select", true, false)]
    [InlineData("Delete", true, false)]
    [InlineData("Update", false, false)]
    [InlineData("Insert", false, false)]
    [InlineData("Select", false, false)]
    [InlineData("Delete", false, false)]
    public void NCharRewriteRequiresBothTheStatementKindAndTheBindingFlag(
        string sqlTypeName,
        bool isNCharBinding,
        bool expectsRewrite)
    {
        (DataWindowCarrier carrier, RecordingParentTask task) = NewMainThread();
        task.IsNCharBinding = isNCharBinding;

        long answer = carrier.OnSqlPreview(
            StatementKind(sqlTypeName),
            "SELECT 'a'",
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);

        if (expectsRewrite)
        {
            Assert.Equal("SELECT N'a'", carrier.SqlPreviewStatement);
        }
        else
        {
            Assert.Null(carrier.SqlPreviewStatement);
        }
    }

    /// <summary>
    /// The preview setter refuses a null statement, and the preview event refuses one too.
    /// </summary>
    [Fact]
    public void SqlPreviewRefusesANullStatement()
    {
        (DataWindowCarrier carrier, _) = NewMainThread();

        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = carrier.SetSqlPreview(null!);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = carrier.OnSqlPreview(SqlPreviewType.Update, null!, DwBuffer.Primary);
            });
    }

    #endregion

    #region The worker carrier - cancellation on all four overrides

    /// <summary>
    /// All four worker overrides answer the stop value the moment the parent task reports
    /// cancellation [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L27, L30, L49, L63</c>].
    /// </summary>
    [Fact]
    public void EveryWorkerOverrideStopsOnCancellation()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        task.IsCancelled = true;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveStart());
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(1L));
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnUpdateStart());
        Assert.Equal(
            DataWindowBufferStore.EventStop,
            carrier.OnSqlPreview(SqlPreviewType.Update, "SELECT 1", DwBuffer.Primary));
        Assert.Equal(0, task.NotifyCount);
    }

    /// <summary>
    /// Uncancelled, the three overrides whose legacy scripts fall off their end answer zero - which is
    /// the PowerScript default for a long event and NOT the ancestor's value. The contrast is the SQL
    /// preview, which writes <c>return AncestorReturnValue</c> explicitly at <c>:L44</c>.
    /// </summary>
    [Fact]
    public void UncancelledOverridesAnswerTheContinueValue()
    {
        (WorkerDataWindowCarrier carrier, _, _) = NewWorker();

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveStart());
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnUpdateStart());
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1L));
    }

    /// <summary>
    /// The worker carrier reports the worker affinity, from its header annotation
    /// <c>[运行在子线程]</c> [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L2</c>].
    /// </summary>
    [Fact]
    public void TheWorkerCarrierReportsTheWorkerAffinity()
    {
        (WorkerDataWindowCarrier carrier, _, _) = NewWorker();

        Assert.Equal(CarrierThreadAffinity.WorkerThread, carrier.Affinity);
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = new WorkerDataWindowCarrier(null!);
            });
    }

    #endregion

    #region DEFECT 2 - the deleted-row contribution to the progress total

    /// <summary>
    /// C-B DEFECT 2 and R9. The update-start walk seeds the total from the modified count and then adds
    /// the DELETE-COUNTABLE rows, walking the delete buffer from its count down to 1
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L52-L60</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The total is observed the only way a caller can - through the HIGH word of the first progress
    /// payload - and it pins three things at once: the two-status case list, which excludes a
    /// new-then-deleted row because it never reached the database and generates no statement; and BOTH
    /// bounds of the walk, since a walk starting one row short would answer 2 and one running to zero
    /// would fail on an unaddressable row.
    /// </para>
    /// <para>
    /// The DIRECTION itself cannot be observed from a count, which is exactly why the descent is
    /// annotated as deliberate at its site and verified against its locator by inspection rather than
    /// asserted here. Reversing it is the canonical way to produce wrong data that still passes a
    /// count assertion, so pretending a count test covers it would be worse than saying so.
    /// </para>
    /// </remarks>
    [Fact]
    public void UpdateStartAddsOnlyDeleteCountableRowsToTheProgressTotal()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();

        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NotModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.NewModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.DataModified);
        carrier.AppendRow(DwBuffer.Delete, ItemStatus.New);

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnUpdateStart());

        // One modified primary row, plus NotModified! and DataModified! from the delete buffer.
        // NewModified! and New! are absent from the legacy case list and are correctly excluded.
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);

        Assert.Equal(1, task.NotifyCount);
        Assert.Equal(3, HighWord(task.Payloads[0]));
        Assert.Equal(1, LowWord(task.Payloads[0]));
    }

    #endregion

    #region DEFECT 1 - the uncounted delete-then-insert arm

    /// <summary>
    /// C-B DEFECT 1. A DELETE preview against the <c>Primary!</c> or <c>Filter!</c> buffer answers the
    /// ancestor's value WITHOUT advancing the progress counter
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L32-L35</c>], because it is the delete half of the
    /// delete-then-insert pair the golden master's <c>updatekeyinplace=no</c> produces
    /// [<c>dw_sqlite.srd:L14</c>].
    /// </summary>
    /// <remarks>
    /// The arm is BUFFER-SENSITIVE, which is what makes it identifiable at all: a delete preview
    /// against the <c>Delete!</c> buffer is a genuine user deletion and IS counted. Both halves are
    /// asserted here, so an implementation that dropped the buffer test would fail.
    /// </remarks>
    [Theory]
    [InlineData(DwBuffer.Primary)]
    [InlineData(DwBuffer.Filter)]
    public void ADeletePreviewAgainstPrimaryOrFilterIsNotCounted(DwBuffer buffer)
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Delete,
            "DELETE FROM COMPANY WHERE id=1",
            buffer);

        // The ancestor's value, which is the continue value, and no notification at all: the counter
        // is still zero, so not even the first-statement condition fires.
        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Equal(0, task.NotifyCount);

        // The very next preview is therefore statement number ONE, not two.
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);

        Assert.Equal(1, task.NotifyCount);
        Assert.Equal(1, LowWord(task.Payloads[0]));
    }

    /// <summary>
    /// The contrast case: a delete preview against the <c>Delete!</c> buffer is a real deletion and IS
    /// counted, so it becomes statement number one.
    /// </summary>
    [Fact]
    public void ADeletePreviewAgainstTheDeleteBufferIsCounted()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Delete,
            "DELETE FROM COMPANY WHERE id=1",
            DwBuffer.Delete);

        Assert.Equal(DataWindowBufferStore.EventContinue, answer);
        Assert.Equal(1, task.NotifyCount);
        Assert.Equal((long)SqlUpdateTaskNotifyCode.Progress, task.LastNotifyCode);
        Assert.Equal(1, LowWord(task.Payloads[0]));
    }

    /// <summary>
    /// A SELECT preview is never progress, so the whole accounting block is skipped
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L31</c>].
    /// </summary>
    [Fact]
    public void ASelectPreviewIsNeverProgress()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            carrier.OnSqlPreview(SqlPreviewType.Select, "SELECT * FROM COMPANY", DwBuffer.Primary));
        Assert.Equal(0, task.NotifyCount);
    }

    #endregion

    #region The throttle - three conditions, and the exact notification count

    /// <summary>
    /// With the clock frozen, only the first-statement and reached-the-total conditions can fire, so an
    /// update of five statements emits EXACTLY TWO notifications
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L37</c>].
    /// </summary>
    [Fact]
    public void WithAFrozenClockOnlyTheFirstAndLastStatementsNotify()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();

        for (long statement = 1L; statement <= StatementCount; statement++)
        {
            carrier.OnSqlPreview(
                SqlPreviewType.Update,
                "UPDATE COMPANY SET age=1",
                DwBuffer.Primary);
        }

        Assert.Equal(2, task.NotifyCount);
        Assert.Equal(new[] { 1, 5 }, task.Payloads.Select(LowWord));
        Assert.All(task.Payloads, payload => Assert.Equal(5, HighWord(payload)));
    }

    /// <summary>
    /// The throttle boundary is STRICTLY greater than one hundred milliseconds, so a gap of exactly one
    /// hundred does not notify and a gap of one hundred and one does. The stamp is taken on each
    /// notification, so the intervals are measured from the previous notification and not from the
    /// start of the update.
    /// </summary>
    [Fact]
    public void TheThrottleBoundaryIsStrictlyGreaterThanOneHundredMilliseconds()
    {
        (WorkerDataWindowCarrier carrier, DeterministicClock clock, RecordingParentTask task) =
            NewWorker();

        // A total of one hundred keeps the reached-the-total condition out of the way entirely.
        SeedModifiedRows(carrier, DwBuffer.Primary, 100L);
        carrier.OnUpdateStart();

        // Statement 1 notifies on the first-statement condition and re-stamps the clock.
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        Assert.Equal(1, task.NotifyCount);

        // Exactly one hundred milliseconds later: NOT greater than one hundred, so no notification -
        // and, crucially, no re-stamp either.
        clock.AdvanceMilliseconds(100L);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        Assert.Equal(1, task.NotifyCount);

        // One millisecond more, so one hundred and one from the last notification: it fires.
        clock.AdvanceMilliseconds(1L);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        Assert.Equal(2, task.NotifyCount);

        // And the interval restarts from that notification rather than from the update's start.
        clock.AdvanceMilliseconds(100L);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        Assert.Equal(2, task.NotifyCount);

        clock.AdvanceMilliseconds(1L);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        Assert.Equal(3, task.NotifyCount);
    }

    /// <summary>
    /// A progress handler answering one stops the update, which is the ORDINARY meaning of that answer
    /// - unlike the row-cap notification [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L39-L41</c>].
    /// </summary>
    [Fact]
    public void AProgressHandlerAnsweringOneStopsTheUpdate()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, StatementCount);
        carrier.OnUpdateStart();
        task.NotifyAnswer = DataWindowBufferStore.EventStop;

        long answer = carrier.OnSqlPreview(
            SqlPreviewType.Update,
            "UPDATE COMPANY SET age=1",
            DwBuffer.Primary);

        Assert.Equal(DataWindowBufferStore.EventStop, answer);
        Assert.Equal(1, task.NotifyCount);
    }

    /// <summary>
    /// The notification carries the empty string, as both carrier call sites do.
    /// </summary>
    [Fact]
    public void TheProgressNotificationCarriesTheEmptyStringArgument()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.OnUpdateStart();

        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);

        Assert.Equal(string.Empty, task.LastText);
    }

    #endregion

    #region DEFECT 4 - the sixteen-bit progress payload

    /// <summary>
    /// The payload packs the current statement in the LOW word and the total in the HIGH word, which is
    /// the order the legacy's own comment states
    /// [<c>n_cst_threading_task_sqlupdate.sru:L32</c>].
    /// </summary>
    [Fact]
    public void TheProgressPayloadPacksCurrentLowAndTotalHigh()
    {
        (WorkerDataWindowCarrier carrier, DeterministicClock clock, RecordingParentTask task) =
            NewWorker();
        SeedModifiedRows(carrier, DwBuffer.Primary, 7L);
        carrier.OnUpdateStart();

        // Statement 1 notifies; 2 does not; 3 is forced by advancing past the throttle.
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);
        clock.AdvanceMilliseconds(101L);
        carrier.OnSqlPreview(SqlPreviewType.Update, "UPDATE COMPANY SET age=1", DwBuffer.Primary);

        Assert.Equal(2, task.NotifyCount);
        Assert.Equal(Bits.MakeLong(1, 7), (uint)task.Payloads[0]);
        Assert.Equal(Bits.MakeLong(3, 7), (uint)task.Payloads[1]);
        Assert.Equal(3, LowWord(task.Payloads[1]));
        Assert.Equal(7, HighWord(task.Payloads[1]));
    }

    /// <summary>
    /// C-B DEFECT 4. The two halves are SIXTEEN-BIT words, so a statement count above 65535 wraps. It
    /// is reachable - the chunk size defaults to ten thousand and a result may hold far more rows than
    /// 65535 - and it is observable in the notification stream, so it is preserved rather than widened.
    /// </summary>
    [Fact]
    public void TheProgressPayloadTruncatesAboveSixteenBits()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();

        // A total of one keeps the reached-the-total condition true from statement one onward, so every
        // preview notifies and the last payload is the one at statement 65536.
        SeedModifiedRows(carrier, DwBuffer.Primary, 1L);
        carrier.OnUpdateStart();

        const int wrapPoint = 65536;
        for (int statement = 1; statement <= wrapPoint; statement++)
        {
            carrier.OnSqlPreview(
                SqlPreviewType.Update,
                "UPDATE COMPANY SET age=1",
                DwBuffer.Primary);
        }

        Assert.Equal(wrapPoint, task.NotifyCount);

        // Statement 65535 still fits; statement 65536 wraps to zero rather than widening the field.
        Assert.Equal(65535, LowWord(task.Payloads[wrapPoint - 2]));
        Assert.Equal(0, LowWord(task.Payloads[wrapPoint - 1]));
        Assert.Equal(1, HighWord(task.Payloads[wrapPoint - 1]));

        // Pinned against the packing primitive directly, so that a change to either side shows up.
        Assert.Equal(Bits.MakeLong(0, 1), (uint)task.Payloads[wrapPoint - 1]);
    }

    #endregion

    #region DEFECT 3 - row-cap lifting versus row-cap enforcement

    /// <summary>
    /// C-B DEFECT 3. A row-cap handler answering ONE LIFTS the cap and the retrieve CONTINUES
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L65-L68</c>] - the opposite of what that answer means
    /// everywhere else in this codebase. The rows-exceeded flag stays clear on this arm, so a caller
    /// cannot tell the cap was ever reached.
    /// </summary>
    [Fact]
    public void ARowCapHandlerAnsweringOneLiftsTheCapAndContinues()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        carrier.SetMaxRows(2L);
        task.NotifyAnswer = DataWindowBufferStore.EventStop;

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1L));
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(2L));
        Assert.Equal(0, task.NotifyCount);

        // Row three passes the cap: the handler is asked, answers one, and the retrieve CONTINUES.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(3L));
        Assert.Equal(1, task.NotifyCount);
        Assert.False(carrier.IsRowsExceeded());

        // The cap was cleared, so the remainder of the retrieve is never asked again.
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(4L));
        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(4_000L));
        Assert.Equal(1, task.NotifyCount);
        Assert.False(carrier.IsRowsExceeded());
    }

    /// <summary>
    /// The enforcement arm: a handler answering anything else raises the rows-exceeded flag and stops
    /// the retrieve [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L69-L70</c>].
    /// </summary>
    [Fact]
    public void ARowCapHandlerAnsweringAnythingElseEnforcesTheCap()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        carrier.SetMaxRows(2L);
        task.NotifyAnswer = DataWindowBufferStore.EventContinue;

        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(3L));
        Assert.True(carrier.IsRowsExceeded());
        Assert.Equal(1, task.NotifyCount);

        // The cap is still in force, so a further row asks again.
        Assert.Equal(DataWindowBufferStore.EventStop, carrier.OnRetrieveRow(4L));
        Assert.Equal(2, task.NotifyCount);
    }

    /// <summary>
    /// The row-cap payload is the ROW NUMBER passed straight through, with no word packing and
    /// therefore no truncation - unlike the progress payload. The notify code belongs to the QUERY
    /// task's set, whose value one means the row cap while the UPDATE task's value one means progress.
    /// </summary>
    [Fact]
    public void TheRowCapPayloadIsTheRowNumberUnpacked()
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        carrier.SetMaxRows(1L);
        task.NotifyAnswer = DataWindowBufferStore.EventContinue;

        carrier.OnRetrieveRow(70_000L);

        Assert.Equal(70_000L, task.Payloads[0]);
        Assert.Equal((long)SqlQueryTaskNotifyCode.MaxRows, task.LastNotifyCode);
        Assert.Equal(string.Empty, task.LastText);
    }

    /// <summary>
    /// A cap of zero or below means NO LIMIT, so nothing is ever notified
    /// [<c>n_cst_thread_task_sqlbase_ds_mt.sru:L64</c>].
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ACapOfZeroOrBelowMeansNoLimit(long cap)
    {
        (WorkerDataWindowCarrier carrier, _, RecordingParentTask task) = NewWorker();
        carrier.SetMaxRows(cap);

        Assert.Equal(DataWindowBufferStore.EventContinue, carrier.OnRetrieveRow(1_000_000L));
        Assert.Equal(0, task.NotifyCount);
        Assert.False(carrier.IsRowsExceeded());
    }

    #endregion

    #region The two per-task notify codes

    /// <summary>
    /// The two sets are separate enumerations that SHARE the value one, and every numeric value is the
    /// legacy's [<c>n_cst_threading_task_sqlquery.sru:L28-L33</c>,
    /// <c>n_cst_threading_task_sqlupdate.sru:L32</c>]. Merging them would make that overlap
    /// unrepresentable, and renumbering to disambiguate would invalidate every stored recording.
    /// </summary>
    [Fact]
    public void TheTwoNotifyCodeSetsKeepTheirLegacyValuesIncludingTheSharedOne()
    {
        Assert.Equal(1L, (long)SqlQueryTaskNotifyCode.MaxRows);
        Assert.Equal(2L, (long)SqlQueryTaskNotifyCode.DataReceived);
        Assert.Equal(3L, (long)SqlQueryTaskNotifyCode.PageReceived);
        Assert.Equal(4L, (long)SqlQueryTaskNotifyCode.DataChunk);
        Assert.Equal(5L, (long)SqlQueryTaskNotifyCode.ChildReceived);
        Assert.Equal(6L, (long)SqlQueryTaskNotifyCode.ChildQuery);

        Assert.Equal(1L, (long)SqlUpdateTaskNotifyCode.Progress);

        // The collision is real and is the reason the two sets are distinct types.
        Assert.Equal(
            (long)SqlQueryTaskNotifyCode.MaxRows,
            (long)SqlUpdateTaskNotifyCode.Progress);
    }

    #endregion

    #region The affinity factory

    /// <summary>
    /// The factory reproduces the selection at
    /// <c>n_cst_thread_task_sqlbase.sru:L553-L557</c>, answering the EXACT carrier type for each
    /// affinity. Exact rather than assignable matters: the worker type derives from the main-thread
    /// type, so an "is a" assertion would pass for a collapsed implementation.
    /// </summary>
    [Fact]
    public void TheFactoryAnswersTheExactCarrierTypeForEachAffinity()
    {
        DeterministicClock clock = new();

        DataWindowCarrier mainThread =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, clock);
        DataWindowCarrier worker =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.WorkerThread, clock);

        Assert.IsType<DataWindowCarrier>(mainThread);
        Assert.IsType<WorkerDataWindowCarrier>(worker);
        Assert.Equal(CarrierThreadAffinity.MainThread, mainThread.Affinity);
        Assert.Equal(CarrierThreadAffinity.WorkerThread, worker.Affinity);
    }

    /// <summary>
    /// The boolean-shaped overload reads the way the legacy branch does, asking
    /// <c>#ParentThread.of_IsMainThread()</c>.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFactoryKeysOnTheMainThreadPredicate(bool isMainThread)
    {
        DataWindowCarrier carrier =
            DataWindowCarrierFactory.CreateForThread(isMainThread, new DeterministicClock());

        Assert.Equal(AffinityOf(isMainThread), carrier.Affinity);
    }

    /// <summary>
    /// There are exactly two carrier types, so an undeclared affinity and a missing clock both fail
    /// fast.
    /// </summary>
    [Fact]
    public void TheFactoryRefusesAnUndeclaredAffinityAndAMissingClock()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
            {
                _ = DataWindowCarrierFactory.Create(
                    (CarrierThreadAffinity)7,
                    new DeterministicClock());
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, null!);
            });
        Assert.Throws<ArgumentNullException>(
            () =>
            {
                _ = DataWindowCarrierFactory.CreateForThread(false, null!);
            });
    }

    #endregion

    #region The main-thread handover that runs no codec at all

    /// <summary>
    /// The handover is available only on the main thread, with no receiver AND with caching off - the
    /// nested conjunction at <c>n_cst_thread_task_sqlquery.sru:L84-L85</c>. All eight combinations are
    /// pinned, because any one of the three failing sends the result down a codec path instead.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    public void TheHandoverRequiresAllThreeConditions(
        bool isMainThread,
        bool hasReceiver,
        bool cacheEnabled,
        bool expected)
    {
        Assert.Equal(
            expected,
            DataWindowCarrierOwnership.CanMoveWithoutSerialization(
                AffinityOf(isMainThread),
                hasReceiver,
                cacheEnabled));
    }

    /// <summary>
    /// The receiver adopts the carrier itself and the sender's reference is then dropped, reproducing
    /// <c>tasking.Event OnDataMove(data)</c> followed by <c>SetNull(data)</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L87-L89</c>]. The answer is the framework success code.
    /// </summary>
    [Fact]
    public void TheHandoverTransfersOwnershipAndDropsTheSendersReference()
    {
        DataWindowCarrier? sender =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, new DeterministicClock());
        DataWindowCarrier? received = null;

        long answer = DataWindowCarrierOwnership.Move(ref sender, carrier => received = carrier);

        Assert.Equal(RetCode.OK, answer);
        Assert.NotNull(received);
        Assert.Null(sender);
    }

    /// <summary>
    /// A carrier may be handed over once. A second handover means two parties believe they own the same
    /// result, which is exactly the fault the dropped reference exists to prevent, so it fails fast.
    /// </summary>
    [Fact]
    public void AHandoverCannotHappenTwice()
    {
        DataWindowCarrier? sender =
            DataWindowCarrierFactory.Create(CarrierThreadAffinity.MainThread, new DeterministicClock());

        DataWindowCarrierOwnership.Move(ref sender, _ => { });

        // A ref argument cannot be captured by a lambda, so each failing call is made through a local
        // function that closes over nothing.
        void MoveAgain() => DataWindowCarrierOwnership.Move(ref sender, _ => { });
        void MoveWithNoReceiver() => DataWindowCarrierOwnership.Move(ref sender, null!);

        Assert.Throws<InvalidOperationException>(MoveAgain);
        Assert.Throws<ArgumentNullException>(MoveWithNoReceiver);
    }

    #endregion
}
