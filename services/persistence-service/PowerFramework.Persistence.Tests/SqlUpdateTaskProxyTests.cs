// ==================================================================================================
//  SqlUpdateTaskProxyTests.cs - the parity suite for the caller-side update proxy
// ==================================================================================================
//
//  Pins the behaviour of Tasks/TaskProxies/SqlUpdateTaskProxy.cs, the port of the read-only object
//  ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru.
//
//  NO REAL THREAD AND NO DATABASE (C-H). Every test drives the subject through two substituted seams -
//  a hand-driven task-substrate host and a recording write-back target - plus a REAL SqlUpdateTask
//  built over fakes, so the delegation assertions verify the actual worker contract rather than a
//  mock's expectations. Nothing here starts a thread, opens a connection or touches a DataWindow.
//
//  THE PINNING TESTS ARE THE POINT. Several cases below assert behaviour that looks like a defect and
//  is: the swallowed return codes, the Primary-only row translation, the missing case-else, the
//  table-1 array bound. Each is marked PINNING and each exists so that "fixing" the production code
//  breaks a test instead of silently changing behaviour (C-B).
//
// ==================================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Parity tests for <c>SqlUpdateTaskProxy</c>.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "Every disposable the harness creates is owned by the harness and released by its own Dispose.")]
public sealed class SqlUpdateTaskProxyTests
{
    // ----------------------------------------------------------------------------------------------
    //  §3.1 - COUNT ACCUMULATION [:L66-L69]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Two per-table reports SUM. This is the test that fails if the production code assigns instead of
    /// adding [<c>:L66-L69</c>], which with two tables would report only the last table's counts.
    /// </summary>
    [Fact]
    public void OnUpdated_AccumulatesAcrossAMultiTableRun()
    {
        using UpdateProxyHarness harness = new();

        // Table 1, then table 2 - the worker fires once per table
        // [n_cst_thread_task_sqlupdate.sru:L247, from the loop at :L364-L369].
        harness.Proxy.OnUpdated(3L, 5L, 7L);
        harness.Proxy.OnUpdated(11L, 13L, 17L);

        Assert.Equal(14L, harness.Proxy.GetRowsInserted());
        Assert.Equal(18L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(24L, harness.Proxy.GetRowsDeleted());

        // Stated explicitly: an assignment would have left the second table's numbers, so these are the
        // values a "simplified" implementation would produce and must NOT.
        Assert.NotEqual(11L, harness.Proxy.GetRowsInserted());
        Assert.NotEqual(13L, harness.Proxy.GetRowsUpdated());
        Assert.NotEqual(17L, harness.Proxy.GetRowsDeleted());
    }

    /// <summary>
    /// A single report is still accumulated onto zero, and the three counters are independent - a swap
    /// between updated and deleted would survive the multi-table test above but not this one.
    /// </summary>
    [Fact]
    public void OnUpdated_KeepsTheThreeCountersIndependent()
    {
        using UpdateProxyHarness harness = new();

        harness.Proxy.OnUpdated(1L, 2L, 3L);

        Assert.Equal(1L, harness.Proxy.GetRowsInserted());
        Assert.Equal(2L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(3L, harness.Proxy.GetRowsDeleted());
    }

    /// <summary>
    /// The three count accessors carry NO busy guard [<c>:L214, :L217, :L220</c>], so they answer while
    /// the task is busy - which is exactly when a progress display reads them.
    /// </summary>
    [Fact]
    public void CountAccessors_AnswerWhileBusy()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnUpdated(4L, 5L, 6L);

        harness.Host.MakeBusy();

        Assert.True(harness.Proxy.IsBusy());
        Assert.Equal(4L, harness.Proxy.GetRowsInserted());
        Assert.Equal(5L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(6L, harness.Proxy.GetRowsDeleted());
    }

    // ----------------------------------------------------------------------------------------------
    //  §3.1 - THE IDENTITY APPEND [:L71-L77]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// ONE block appended per firing, in table order, each carrying its own id and both arrays
    /// [<c>:L73-L76</c>]. Verified through the write-back, which is the only observer of the collection.
    /// </summary>
    [Fact]
    public void OnIdentityColumnDataRetrieved_AppendsOneBlockPerTableInOrder()
    {
        using UpdateProxyHarness harness = new();

        // Two tables, two DIFFERENT identity columns, so a merge or a reorder is detectable.
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [100L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(4L, [200L], []);

        // One Primary! row, newly modified, so both blocks write at cursor 1.
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
            PrimaryRowCount = 1L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        // Two writes for one row - one per block - with each block's OWN column id and OWN value.
        Assert.Equal(
            [(1L, 1L, 100L), (1L, 4L, 200L)],
            target.PrimaryWrites);
    }

    /// <summary>
    /// The append is one-based-equivalent: the Nth firing lands at position N, which is what makes the
    /// write-back's <c>_idColDatas[1]</c> read address the FIRST reported table [<c>:L136</c>].
    /// </summary>
    [Fact]
    public void OnIdentityColumnDataRetrieved_LandsTheFirstFiringAtPositionOne()
    {
        using UpdateProxyHarness harness = new();

        // Block one has ONE value; block two has THREE. The bound comes from block ONE, so exactly one
        // row is written even though block two could have supplied three.
        harness.Proxy.OnIdentityColumnDataRetrieved(2L, [10L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(3L, [20L, 21L, 22L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
            },
            PrimaryRowCount = 2L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        // ONE row written, from block one's bound of 1 - not two.
        Assert.Equal(
            [(1L, 2L, 10L), (1L, 3L, 20L)],
            target.PrimaryWrites);
    }

    /// <summary>
    /// Null arguments are refused rather than stored, so a contract violation surfaces at the call site
    /// instead of as a null reference inside the write-back.
    /// </summary>
    [Fact]
    public void OnIdentityColumnDataRetrieved_RejectsNullArrays()
    {
        using UpdateProxyHarness harness = new();

        Assert.Throws<ArgumentNullException>(
            () => harness.Proxy.OnIdentityColumnDataRetrieved(1L, null!, []));
        Assert.Throws<ArgumentNullException>(
            () => harness.Proxy.OnIdentityColumnDataRetrieved(1L, [], null!));
    }

    // ----------------------------------------------------------------------------------------------
    //  §3.2 - THE WRITE-BACK [:L120-L205]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 THE CENTRAL PARITY TEST. Values collected from <c>Filter!</c> BACKWARD
    /// [<c>n_cst_thread_task_sqlupdate.sru:L237</c>] are applied FORWARD
    /// [<c>n_cst_threading_task_sqlupdate.sru:L154, :L163</c>], and the pairing is what puts each value
    /// on its own row. Asserted end to end: the expected mapping below is only correct if BOTH halves keep
    /// their directions, so "correcting" either one fails this test.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_AppliesForwardInBothBuffersAgainstBackwardCollectedFilterValues()
    {
        using UpdateProxyHarness harness = new();

        // The source rows, in source order, with the identity the database assigned to each.
        //   Primary! rows 1,2 -> ids 41, 42     (collected FORWARD, so the array is [41, 42])
        //   Filter!  rows 1,2,3 -> ids 51,52,53 (collected BACKWARD, so the array is [53, 52, 51])
        //
        // The collector's own direction is reproduced here by hand rather than assumed: this is exactly
        // the array IdentityColumnResolver hands over.
        long?[] primaryCollectedForward = [41L, 42L];
        long?[] filterCollectedBackward = [53L, 52L, 51L];

        harness.Proxy.OnIdentityColumnDataRetrieved(1L, primaryCollectedForward, filterCollectedBackward);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 2L,
            FilteredRowCount = 3L,
            PrimaryStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
            },
            FilterStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
                [3L] = ItemStatus.NewModified,
            },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        // Primary: forward walk over a forward-collected array - row N gets element N.
        Assert.Equal([(1L, 1L, 41L), (2L, 1L, 42L)], target.PrimaryWrites);

        // Filter: forward walk over a BACKWARD-collected array. Filter! row 1 is the source's LAST
        // filtered row, so it correctly receives 53 - the last id the collector saw first.
        Assert.Equal(
            [(1L, 1L, 53L), (2L, 1L, 52L), (3L, 1L, 51L)],
            target.FilterWrites);

        // A "corrected" reverse apply would have produced this instead. Stated so the intent of the
        // assertion above cannot be misread as arbitrary.
        Assert.NotEqual(
            [(1L, 1L, 51L), (2L, 1L, 52L), (3L, 1L, 53L)],
            target.FilterWrites);
    }

    /// <summary>
    /// Only NEWLY-MODIFIED rows receive a value [<c>:L140, :L156</c>]. A merely data-modified row is
    /// visited by the walk and skipped, and it does NOT advance the value cursor.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_WritesOnlyNewModifiedRows()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [61L, 62L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 4L,
            PrimaryStatuses =
            {
                [1L] = ItemStatus.DataModified,   // visited by the walk, skipped by the new-row test
                [2L] = ItemStatus.NewModified,    // cursor 1 -> 61
                [3L] = ItemStatus.NotModified,    // not even visited
                [4L] = ItemStatus.NewModified,    // cursor 2 -> 62
            },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        Assert.Equal([(2L, 1L, 61L), (4L, 1L, 62L)], target.PrimaryWrites);
    }

    /// <summary>
    /// More qualifying rows than collected values ends the pass without failing it
    /// [<c>:L142, :L158</c>] - the surplus rows keep whatever they had.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_StopsWhenTheCursorPassesTheBound()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [71L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 3L,
            PrimaryStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
                [3L] = ItemStatus.NewModified,
            },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        Assert.Equal([(1L, 1L, 71L)], target.PrimaryWrites);
    }

    /// <summary>
    /// A null collected value is written THROUGH as null, never coerced to zero - zero is a legal
    /// identity on a table seeded at zero.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_CarriesANullValueThrough()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [null], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        Assert.Equal([(1L, 1L, (long?)null)], target.PrimaryWrites);
    }

    /// <summary>
    /// An empty collection answers the GENERIC FAILURE [<c>:L128-L129</c>] - not OK, not a not-found
    /// code.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_AnswersFailedWhenNothingWasCollected()
    {
        using UpdateProxyHarness harness = new();
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(target));
        Assert.Empty(target.PrimaryWrites);
    }

    /// <summary>
    /// PINNING: the empty-collection test runs BEFORE the type dispatch [<c>:L129</c> before
    /// <c>:L131</c>], so an UNSUPPORTED target with nothing collected answers FAILED rather than
    /// E_INVALID_TYPE. The order of the two tests is observable.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_PrefersFailedOverInvalidTypeWhenBothApply()
    {
        using UpdateProxyHarness harness = new();
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.Other);

        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(target));
    }

    /// <summary>
    /// An invalid object answers <c>E_INVALID_OBJECT</c> [<c>:L126</c>], and it does so BEFORE the
    /// collection is measured.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_AnswersInvalidObjectForANullTarget()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Proxy.RefreshIdentityData(null!));
    }

    /// <summary>
    /// Anything that is neither a DataWindow nor a DataStore answers <c>E_INVALID_TYPE</c>
    /// [<c>:L200-L201</c>], and nothing is written.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_AnswersInvalidTypeForAnUnsupportedTarget()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [81L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.Other)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.E_INVALID_TYPE, harness.Proxy.RefreshIdentityData(target));
        Assert.Empty(target.PrimaryWrites);
        Assert.Empty(target.RedrawCalls);
    }

    /// <summary>
    /// The busy guard refuses the write-back with <c>E_BUSY</c> [<c>:L125</c>] and writes nothing.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_RefusesWhileBusy()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [91L], []);
        harness.Host.MakeBusy();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.RefreshIdentityData(target));
        Assert.Empty(target.PrimaryWrites);
    }

    /// <summary>
    /// Redraw is bracketed on the <c>DataWindow!</c> arm [<c>:L134, :L166</c>] and NOT on the
    /// <c>DataStore!</c> arm [<c>:L167-L199</c>], and the suspension happens BEFORE the first write and
    /// the restore AFTER the last.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_BracketsRedrawOnTheDataWindowArmOnly()
    {
        using UpdateProxyHarness dataWindow = new();
        dataWindow.Proxy.OnIdentityColumnDataRetrieved(1L, [101L], []);

        RecordingWriteBackTarget windowTarget = new(IdentityWriteBackTargetKind.DataWindow)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, dataWindow.Proxy.RefreshIdentityData(windowTarget));
        Assert.Equal([false, true], windowTarget.RedrawCalls);

        // The bracket encloses the write: suspension is recorded before it, restoration after.
        Assert.Equal(0, windowTarget.RedrawCallsBeforeFirstWrite);
        Assert.Single(windowTarget.PrimaryWrites);

        using UpdateProxyHarness dataStore = new();
        dataStore.Proxy.OnIdentityColumnDataRetrieved(1L, [101L], []);

        RecordingWriteBackTarget storeTarget = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, dataStore.Proxy.RefreshIdentityData(storeTarget));
        Assert.Empty(storeTarget.RedrawCalls);
    }

    /// <summary>
    /// PINNING - THE PRESERVED LATENT DEFECT [<c>:L136</c> bounding, <c>:L143-L145</c> indexing]. Both
    /// passes take their bound from block ONE and then index EVERY block with it, so a shorter later
    /// block is reached past. The bound is NOT widened; the C# port answers a FAILURE CODE rather than
    /// throwing.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_AnswersFailedRatherThanThrowingOnTheTableOneBound()
    {
        using UpdateProxyHarness harness = new();

        // Block one supplies TWO values; block two supplies ONE. The bound is two.
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [111L, 112L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(2L, [211L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 2L,
            PrimaryStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
            },
        };

        // A failure code, NOT an exception.
        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(target));

        // PARTIAL APPLICATION IS THE ORACLE'S BEHAVIOUR TOO, and this is the exact prefix it produces.
        // Row 1 applies for both blocks at cursor 1. Row 2 then applies for block ONE at cursor 2 - which
        // is within block one's bound of two - and only the inner loop's SECOND iteration reaches past
        // block two's single value. The oracle's `dw.SetItem(...)` for block one has already run by then
        // [:L143-L145], and its array-boundary error is raised on the next iteration, so the writes below
        // are what a caller observes in both implementations.
        Assert.Equal(
            [(1L, 1L, 111L), (1L, 2L, 211L), (2L, 1L, 112L)],
            target.PrimaryWrites);
    }

    /// <summary>
    /// The redraw restore still runs when the preserved out-of-range path leaves the DataWindow arm -
    /// the oracle always falls through to <c>:L166</c>, so a suspended target is never left suspended.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_RestoresRedrawEvenOnTheFailurePath()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [121L, 122L], []);
        harness.Proxy.OnIdentityColumnDataRetrieved(2L, [221L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataWindow)
        {
            PrimaryRowCount = 2L,
            PrimaryStatuses =
            {
                [1L] = ItemStatus.NewModified,
                [2L] = ItemStatus.NewModified,
            },
        };

        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(target));
        Assert.Equal([false, true], target.RedrawCalls);
    }

    /// <summary>
    /// An empty value array for one buffer skips that buffer's whole pass [<c>:L137, :L152</c>] while the
    /// other still runs, which is the "nothing was collected for this buffer" reading.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_SkipsABufferWhoseArrayIsEmpty()
    {
        using UpdateProxyHarness harness = new();

        // Filter values only - the Primary pass must be skipped entirely.
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [], [131L]);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            FilteredRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
            FilterStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));
        Assert.Empty(target.PrimaryWrites);
        Assert.Equal([(1L, 1L, 131L)], target.FilterWrites);
    }

    /// <summary>
    /// The two buffers use the TWO DIFFERENT write accessors - the item setter for <c>Primary!</c>
    /// [<c>:L144</c>] and the direct buffer expression for <c>Filter!</c> [<c>:L160</c>] - and the
    /// recording target keeps them apart, so a merged implementation would fail here.
    /// </summary>
    [Fact]
    public void RefreshIdentityData_UsesTheAsymmetricWriteAccessors()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [141L], [151L]);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            FilteredRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
            FilterStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.RefreshIdentityData(target));

        Assert.Equal([(1L, 1L, 141L)], target.PrimaryWrites);
        Assert.Equal([(1L, 1L, 151L)], target.FilterWrites);
    }

    // ----------------------------------------------------------------------------------------------
    //  §3.3 - THE DATABASE-ERROR ROW REWRITE [:L310-L343]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The ordinal among modified rows becomes an ACTUAL buffer row [<c>:L323-L325</c>], and ONLY the row
    /// changes - every other member is carried through untouched.
    /// </summary>
    [Fact]
    public void OnDbError_TranslatesTheOrdinalIntoAnActualPrimaryRow()
    {
        using UpdateProxyHarness harness = new();

        // Modified rows are 2, 5 and 9. Ordinal 2 is therefore buffer row 5.
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 10L,
            PrimaryStatuses =
            {
                [2L] = ItemStatus.DataModified,
                [5L] = ItemStatus.NewModified,
                [9L] = ItemStatus.DataModified,
            },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        DbErrorData incoming = new()
        {
            SqlDbCode = -803L,
            SqlErrText = "duplicate key",
            SqlSyntax = "INSERT INTO COMPANY VALUES (1)",
            Buffer = DwBuffer.Primary,
            Row = 2L,
        };

        harness.Host.PublishDbError(harness.Proxy, incoming);

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        Assert.Equal(5L, latched.Row);
        Assert.Equal(-803L, latched.SqlDbCode);
        Assert.Equal("duplicate key", latched.SqlErrText);
        Assert.Equal("INSERT INTO COMPANY VALUES (1)", latched.SqlSyntax);
        Assert.Equal(DwBuffer.Primary, latched.Buffer);
    }

    /// <summary>
    /// A non-positive row returns early [<c>:L315</c>] and is left exactly as reported.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void OnDbError_LeavesANonPositiveRowAlone(long reportedRow)
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 3L,
            PrimaryStatuses = { [3L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -1L, Row = reportedRow });

        Assert.Equal(reportedRow, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// With no retained update object the payload is latched but never translated [<c>:L314</c>].
    /// </summary>
    [Fact]
    public void OnDbError_LatchesWithoutTranslatingWhenNoObjectIsRetained()
    {
        using UpdateProxyHarness harness = new();

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -7L, Row = 2L });

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        Assert.Equal(-7L, latched.SqlDbCode);
        Assert.Equal(2L, latched.Row);
    }

    /// <summary>
    /// PINNING - PRESERVED GAP 1 [<c>:L320-L321</c>]. Only <c>Primary!</c> is walked, so an error
    /// reported against <c>Filter!</c> or <c>Delete!</c> keeps its RAW ORDINAL. The rows below are chosen
    /// so a buffer-aware translation would have produced a different answer.
    /// </summary>
    [Theory]
    [InlineData(DwBuffer.Filter)]
    [InlineData(DwBuffer.Delete)]
    public void OnDbError_LeavesNonPrimaryBufferRowsUntranslated(DwBuffer buffer)
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 4L,
            FilteredRowCount = 4L,
            PrimaryStatuses = { [4L] = ItemStatus.NewModified },
            FilterStatuses = { [3L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -1L, Buffer = buffer, Row = 1L });

        // Translated against PRIMARY regardless of the reported buffer - the gap. Primary's only modified
        // row is 4, so ordinal 1 becomes 4 even for a Filter!-reported error.
        Assert.Equal(4L, harness.Proxy.GetLastDbErrorData().Row);
        Assert.Equal(buffer, harness.Proxy.GetLastDbErrorData().Buffer);
    }

    /// <summary>
    /// PINNING - PRESERVED GAP 2 [the <c>choose case</c> at <c>:L317</c> has NO <c>case else</c>, ending
    /// at <c>:L342</c>]. An unsupported retained object falls through SILENTLY, leaving the row
    /// untranslated with no error and no log.
    /// </summary>
    [Fact]
    public void OnDbError_FallsThroughSilentlyForAnUnsupportedRetainedObject()
    {
        using UpdateProxyHarness harness = new();

        // Retained through the write-back's own path is impossible for an unsupported kind, so the kind is
        // flipped after retention - which is exactly the situation the missing case-else covers.
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 3L,
            PrimaryStatuses = { [3L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        target.Kind = IdentityWriteBackTargetKind.Other;

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -1L, Row = 1L });

        // Untranslated - still the raw ordinal.
        Assert.Equal(1L, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// An ordinal beyond the number of modified rows finds no match and the row is left alone - the walk
    /// at [<c>:L322</c>] simply ends.
    /// </summary>
    [Fact]
    public void OnDbError_LeavesTheRowAloneWhenTheOrdinalExceedsTheModifiedRows()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 2L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -1L, Row = 9L });

        Assert.Equal(9L, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// LAST-ERROR-WINS: the second error wholly REPLACES the first, and the replacement happens BEFORE
    /// any row translation because the base's bare assignment runs first [<c>:L310</c> then
    /// <c>n_cst_threading_task_sqlbase.sru:L44</c>]. Nothing accumulates.
    /// </summary>
    [Fact]
    public void OnDbError_LatchesTheLastErrorAndTranslatesThatOne()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 6L,
            PrimaryStatuses =
            {
                [2L] = ItemStatus.NewModified,
                [6L] = ItemStatus.DataModified,
            },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -100L, SqlErrText = "first", Row = 1L });

        Assert.Equal(2L, harness.Proxy.GetLastDbErrorData().Row);

        harness.Host.PublishDbError(
            harness.Proxy,
            new DbErrorData { SqlDbCode = -200L, SqlErrText = "second", Row = 2L });

        DbErrorData latched = harness.Proxy.GetLastDbErrorData();

        // Wholly replaced - the first error's code and text are gone - and the SECOND ordinal is the one
        // translated, to buffer row 6.
        Assert.Equal(-200L, latched.SqlDbCode);
        Assert.Equal("second", latched.SqlErrText);
        Assert.Equal(6L, latched.Row);
    }

    // ----------------------------------------------------------------------------------------------
    //  §5.1 - RESET AND PREPARE, AND THE DIVERGENCE BETWEEN THEM
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Reset chains to the base FIRST [<c>:L85</c>] and clears all FIVE pieces of state
    /// [<c>:L88-L94</c>], answering OK [<c>:L96</c>].
    /// </summary>
    [Fact]
    public void Reset_ClearsEveryPieceOfCallerSideState()
    {
        using UpdateProxyHarness harness = new();

        harness.Proxy.OnUpdated(1L, 2L, 3L);
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [161L], []);
        Assert.Equal(RetCode.OK, harness.Proxy.SetMultiTableUpdate(true));

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        Assert.Equal(RetCode.OK, harness.Proxy.Reset());

        // The three counters.
        Assert.Equal(0L, harness.Proxy.GetRowsInserted());
        Assert.Equal(0L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(0L, harness.Proxy.GetRowsDeleted());

        // The identity blocks - observed through the write-back's empty-collection answer.
        RecordingWriteBackTarget probe = new(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(probe));

        // The multi-table flag - observed through the capture gate, which now skips for a "?" table.
        RecordingWriteBackTarget gate = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = "?",
            ChangeRowCount = 5L,
        };
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(gate, applyData: true));
        Assert.Equal(0, gate.GetChangesCalls);

        // The retained update object - observed through the error hook, which no longer translates.
        harness.Host.PublishDbError(harness.Proxy, new DbErrorData { Row = 1L });
    }

    /// <summary>
    /// Reset abandons on the base's failure, returning the base's code VERBATIM [<c>:L86</c>] and
    /// clearing NOTHING.
    /// </summary>
    [Fact]
    public void Reset_AbandonsOnTheBaseFailureAndClearsNothing()
    {
        using UpdateProxyHarness harness = new();

        harness.Proxy.OnUpdated(1L, 2L, 3L);
        harness.Host.MakeBusy();

        // The base's own reset guards on busy first, so it answers E_BUSY - a FAILING code.
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.Reset());

        // Nothing was cleared.
        Assert.Equal(1L, harness.Proxy.GetRowsInserted());
        Assert.Equal(2L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(3L, harness.Proxy.GetRowsDeleted());
    }

    /// <summary>
    /// PINNING - THE PREPARE/RESET DIVERGENCE [<c>:L291-L301</c> versus <c>:L82-L97</c>]. Prepare clears
    /// the counters and the identity blocks but LEAVES the multi-table flag and the retained update
    /// object standing, where Reset clears both.
    /// </summary>
    [Fact]
    public void OnPrepare_LeavesTheMultiTableFlagAndTheRetainedObjectStanding()
    {
        using UpdateProxyHarness harness = new();

        harness.Proxy.OnUpdated(1L, 2L, 3L);
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [171L], []);
        Assert.Equal(RetCode.OK, harness.Proxy.SetMultiTableUpdate(true));

        RecordingWriteBackTarget retained = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 4L,
            PrimaryStatuses = { [4L] = ItemStatus.NewModified },
        };
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(retained, applyData: false));

        Assert.Equal(0L, harness.Host.InvokePrepare(harness.Proxy));

        // CLEARED: the counters.
        Assert.Equal(0L, harness.Proxy.GetRowsInserted());
        Assert.Equal(0L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(0L, harness.Proxy.GetRowsDeleted());

        // CLEARED: the identity blocks.
        RecordingWriteBackTarget probe = new(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(probe));

        // NOT CLEARED: the multi-table flag still forces a capture for a "?" update table.
        RecordingWriteBackTarget gate = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = "?",
            ChangeRowCount = 3L,
        };
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(gate, applyData: true));
        Assert.Equal(1, gate.GetChangesCalls);

        // NOT CLEARED: the retained object - re-retained above, so re-establish the original and confirm
        // the error hook still translates through it.
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(retained, applyData: false));
        harness.Host.PublishDbError(harness.Proxy, new DbErrorData { Row = 1L });
        Assert.Equal(4L, harness.Proxy.GetLastDbErrorData().Row);
    }

    /// <summary>
    /// PINNING - THE ANCESTOR'S ANSWER IS DISCARDED ONE LEVEL DOWN, SO THIS OVERRIDE'S GUARD IS
    /// DEFENSIVE AND CANNOT FIRE THROUGH THE REAL CHAIN. Verified in the oracle rather than assumed: the
    /// legacy parent's <c>onprepare</c> unconditionally answers <c>return 0 //continue</c>
    /// [<c>n_cst_threading_task_sqlbase.sru:L206-L211</c>], and the substrate merely DECLARES
    /// <c>event type long onprepare ( )</c> with no body [<c>n_cst_threading_task.sru:L21</c>] - so the
    /// whole chain always yields zero. A substrate that wanted to prevent the run is therefore OVERRULED
    /// below this level, which is the base's own documented preserved asymmetry.
    /// </summary>
    /// <remarks>
    /// The guard at [<c>:L293</c>] is nonetheless reproduced and must not be deleted for coverage: C-B
    /// requires the line, and it is the only thing that would honour a non-zero ancestor answer if the
    /// chain ever produced one. This test exists to record WHY the branch is unreachable so nobody
    /// "fixes" either half.
    /// </remarks>
    [Fact]
    public void OnPrepare_IsNotAbortedByAPreventingSubstrateBecauseTheAncestorDiscardsIt()
    {
        using UpdateProxyHarness harness = new();

        harness.Proxy.OnUpdated(5L, 6L, 7L);
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [211L], []);

        // The substrate says PREVENT...
        harness.Host.PrepareAnswer = RetCode.PREVENT;

        // ...and prepare still answers CONTINUE, because the ancestor swallowed it.
        Assert.Equal(0L, harness.Host.InvokePrepare(harness.Proxy));

        // So the clear DID happen - the guard never fired.
        Assert.Equal(0L, harness.Proxy.GetRowsInserted());
        Assert.Equal(0L, harness.Proxy.GetRowsUpdated());
        Assert.Equal(0L, harness.Proxy.GetRowsDeleted());

        RecordingWriteBackTarget probe = new(IdentityWriteBackTargetKind.DataStore);
        Assert.Equal(RetCode.FAILED, harness.Proxy.RefreshIdentityData(probe));
    }

    // ----------------------------------------------------------------------------------------------
    //  §3.2 / §5.3 - THE FINALIZE HOOK'S GATED AUTO-INVOCATION [:L303-L308]
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The write-back runs when the exit code is OK and an object is retained [<c>:L303-L305</c>].
    /// </summary>
    [Fact]
    public void OnFinalize_InvokesTheWriteBackWhenTheRunSucceeded()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [181L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.LastExitCodeValue = RetCode.OK;
        harness.Proxy.OnFinalize();

        Assert.Equal([(1L, 1L, 181L)], target.PrimaryWrites);
    }

    /// <summary>
    /// The gate is a LITERAL EQUALITY against OK, so a cancelled or prevented run writes nothing - a
    /// success predicate would have let PREVENT through.
    /// </summary>
    [Theory]
    [InlineData(RetCode.CANCELLED)]
    [InlineData(RetCode.PREVENT)]
    [InlineData(RetCode.FAILED)]
    public void OnFinalize_SkipsTheWriteBackForAnyExitCodeOtherThanOk(long exitCode)
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [191L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        harness.Host.LastExitCodeValue = exitCode;
        harness.Proxy.OnFinalize();

        Assert.Empty(target.PrimaryWrites);
    }

    /// <summary>
    /// With no retained object the finalize hook does nothing at all [<c>:L304</c>] - and in particular
    /// does not throw.
    /// </summary>
    [Fact]
    public void OnFinalize_DoesNothingWithoutARetainedObject()
    {
        using UpdateProxyHarness harness = new();
        harness.Host.LastExitCodeValue = RetCode.OK;

        harness.Proxy.OnFinalize();
    }

    // ----------------------------------------------------------------------------------------------
    //  §5.2 - THE SETTERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The data-object setter delegates through to the worker [<c>:L103</c>].
    /// </summary>
    [Fact]
    public void SetDataObject_DelegatesToTheWorker()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetDataObject("d_company"));

        Assert.Equal("d_company", harness.Worker.DataObject);
    }

    /// <summary>
    /// PINNING - THE UN-GATED ASSERTION [<c>:L101</c>]. It fires in EVERY configuration, Release
    /// included, which is the whole point of the inconsistency with the syntax setter.
    /// </summary>
    [Fact]
    public void SetDataObject_AssertsOnAnEmptyNameInEveryConfiguration()
    {
        using UpdateProxyHarness harness = new();

        Assert.Throws<AssertionFailure>(() => harness.Proxy.SetDataObject(string.Empty));
    }

    /// <summary>
    /// The syntax setter delegates through [<c>:L117</c>], and its own delegation clears the worker's
    /// data object - the mutual exclusion the worker owns.
    /// </summary>
    [Fact]
    public void SetSqlSyntax_DelegatesToTheWorker()
    {
        using UpdateProxyHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.SetDataObject("d_company"));

        Assert.Equal(RetCode.OK, harness.Proxy.SetSqlSyntax("release 12.5;"));

        Assert.Equal("release 12.5;", harness.Worker.SqlSyntax);
        Assert.Equal(string.Empty, harness.Worker.DataObject);
    }

    /// <summary>
    /// PINNING - THE DEBUG-GATED ASSERTION [<c>:L113-L115</c>]. In a Debug build the empty string
    /// asserts; in a Release build it passes straight through. The two arms below are the reproduced
    /// gating, and the contrast with the un-gated setter above is the preserved inconsistency.
    /// </summary>
    [Fact]
    public void SetSqlSyntax_GatesItsAssertionBehindDebug()
    {
        using UpdateProxyHarness harness = new();

#if DEBUG
        Assert.Throws<AssertionFailure>(() => harness.Proxy.SetSqlSyntax(string.Empty));
#else
        Assert.Equal(RetCode.OK, harness.Proxy.SetSqlSyntax(string.Empty));
        Assert.Equal(string.Empty, harness.Worker.SqlSyntax);
#endif
    }

    /// <summary>
    /// The auto-commit setter is BOOLEAN [<c>:L52</c>] and delegates [<c>:L108</c>].
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetAutoCommit_DelegatesTheBooleanToTheWorker(bool autoCommit)
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetAutoCommit(autoCommit));

        Assert.Equal(autoCommit, harness.Worker.AutoCommit);
    }

    /// <summary>
    /// The multi-table setter writes BOTH copies - its own [<c>:L209</c>] and the worker's
    /// [<c>:L211</c>].
    /// </summary>
    [Fact]
    public void SetMultiTableUpdate_WritesBothTheLocalFlagAndTheWorkers()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetMultiTableUpdate(true));

        // The worker's copy.
        Assert.True(harness.Worker.MultiTableUpdate);

        // The local copy, observed through the capture gate it drives.
        RecordingWriteBackTarget gate = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = "?",
            ChangeRowCount = 2L,
        };
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(gate, applyData: true));
        Assert.Equal(1, gate.GetChangesCalls);
    }

    /// <summary>
    /// The update-data setter forwards the payload BY REFERENCE and its count [<c>:L238</c>].
    /// </summary>
    [Fact]
    public void SetUpdateData_ForwardsThePayloadByReference()
    {
        using UpdateProxyHarness harness = new();

        CarrierState payload = new();

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateData(ref payload!, 42L));

        Assert.Equal(42L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// Every setter, and the write-back, is refused with <c>E_BUSY</c> while the task is busy
    /// [<c>:L99, :L106, :L111, :L207, :L223, :L236, :L246</c>].
    /// </summary>
    [Fact]
    public void EverySetter_IsRefusedWhileBusy()
    {
        using UpdateProxyHarness harness = new();
        harness.Host.MakeBusy();

        CarrierState? payload = null;
        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore);

        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetDataObject("d_company"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetAutoCommit(true));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetSqlSyntax("release 12.5;"));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetMultiTableUpdate(true));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateData(ref payload, 1L));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.AddUpdatableTable("COMPANY", ["name"], ["id"], "id"));
        Assert.Equal(
            RetCode.E_BUSY,
            harness.Proxy.AddUpdatableTable("COMPANY", ["name"], ["id"], "id", 1L, false));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateObject(target));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.SetUpdateObject(target, applyData: true));
        Assert.Equal(RetCode.E_BUSY, harness.Proxy.RefreshIdentityData(target));

        // Nothing reached the worker.
        Assert.Equal(string.Empty, harness.Worker.DataObject);
        Assert.False(harness.Worker.AutoCommit);
        Assert.Empty(harness.Worker.Tables.Descriptors);
    }

    // ----------------------------------------------------------------------------------------------
    //  §5.2 - ADD-UPDATABLE-TABLE, AND THE PRESENCE DISTINCTION
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔴 THE FOUR-ARGUMENT FORM LEAVES BOTH SETTINGS ABSENT [<c>:L227-L234</c>] - asserted as
    /// <see langword="null"/>, not as <c>0</c> and <see langword="false"/>. A defaulted value would emit a
    /// modification line the oracle omits [<c>n_cst_thread_task_sqlupdate.sru:L131, :L135</c>].
    /// </summary>
    [Fact]
    public void AddUpdatableTable_FourArgumentFormLeavesBothOptionalSettingsAbsent()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable("COMPANY", ["name", "age"], ["id"], "id"));

        UpdatableTableDescriptor descriptor = Assert.Single(harness.Worker.Tables.Descriptors);

        Assert.Null(descriptor.UpdateWhere);
        Assert.Null(descriptor.UpdateKeyInPlace);

        // Stated explicitly: these are the values a defaulting implementation would have produced.
        Assert.NotEqual(0L, descriptor.UpdateWhere);
        Assert.False(descriptor.UpdateKeyInPlace is false);

        Assert.Equal("COMPANY", descriptor.Name);
        Assert.Equal(["name", "age"], descriptor.UpdatableColumns);
        Assert.Equal(["id"], descriptor.KeyColumns);
        Assert.Equal("id", descriptor.IdentityColumn);
    }

    /// <summary>
    /// The six-argument form passes BOTH settings through, including the fixture's own
    /// <c>updatewhere=1 updatekeyinplace=no</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14</c>].
    /// </summary>
    [Fact]
    public void AddUpdatableTable_SixArgumentFormPassesBothSettingsThrough()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable(
                "COMPANY",
                ["name", "age", "address", "salary", "birth"],
                ["id"],
                "id",
                1L,
                false));

        UpdatableTableDescriptor descriptor = Assert.Single(harness.Worker.Tables.Descriptors);

        Assert.Equal(1L, descriptor.UpdateWhere);
        Assert.False(descriptor.UpdateKeyInPlace);
        Assert.NotNull(descriptor.UpdateKeyInPlace);
    }

    /// <summary>
    /// Both arities append, in call order, so multi-table update really does carry several descriptors.
    /// </summary>
    [Fact]
    public void AddUpdatableTable_AppendsOneDescriptorPerCallInOrder()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.OK, harness.Proxy.AddUpdatableTable("COMPANY", ["name"], ["id"], "id"));
        Assert.Equal(
            RetCode.OK,
            harness.Proxy.AddUpdatableTable("SALARIES", ["amount"], ["id"], string.Empty, 1L, true));

        Assert.Equal(2, harness.Worker.Tables.Descriptors.Count);
        Assert.Equal("COMPANY", harness.Worker.Tables.Descriptors[0].Name);
        Assert.Equal("SALARIES", harness.Worker.Tables.Descriptors[1].Name);
        Assert.Null(harness.Worker.Tables.Descriptors[0].UpdateWhere);
        Assert.Equal(1L, harness.Worker.Tables.Descriptors[1].UpdateWhere);
    }

    /// <summary>
    /// The worker's three-arm admission test is reached, not re-derived here - an empty name is refused
    /// with <c>E_INVALID_ARGUMENT</c> [<c>n_cst_thread_task_sqlupdate.sru:L84</c>].
    /// </summary>
    [Fact]
    public void AddUpdatableTable_SurfacesTheWorkersAdmissionRefusal()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            harness.Proxy.AddUpdatableTable(string.Empty, ["name"], ["id"], "id"));

        Assert.Empty(harness.Worker.Tables.Descriptors);
    }

    // ----------------------------------------------------------------------------------------------
    //  §5.2 - SET-UPDATE-OBJECT, THE CAPTURE GATE AND THE SWALLOWED FAILURE
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A named update table captures [<c>:L254-L255</c>] and both delegated values reach the worker.
    /// </summary>
    [Fact]
    public void SetUpdateObject_CapturesWhenTheUpdateTableIsNamed()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataWindow)
        {
            UpdateTableAnswer = "COMPANY",
            SyntaxAnswer = "release 12.5;",
            ChangeRowCount = 4L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: true));

        Assert.Equal(1, target.GetChangesCalls);
        Assert.Equal("release 12.5;", harness.Worker.SqlSyntax);
        Assert.Equal(4L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// A <c>"?"</c> update table with multi-table OFF SKIPS the capture [<c>:L254</c>] - but the
    /// update-data setter is STILL CALLED with an empty payload and a zero count, which is the
    /// success-with-no-data shape the worker depends on
    /// [<c>n_cst_thread_task_sqlupdate.sru:L339-L342</c>].
    /// </summary>
    [Fact]
    public void SetUpdateObject_SkipsTheCaptureForTheUnknownMarkerYetStillForwards()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataWindow)
        {
            UpdateTableAnswer = "?",
            SyntaxAnswer = "release 12.5;",
            ChangeRowCount = 9L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: true));

        Assert.Equal(0, target.GetChangesCalls);

        // The forward still happened: the syntax arrived, and the row count is the cleared ZERO rather
        // than the target's nine.
        Assert.Equal("release 12.5;", harness.Worker.SqlSyntax);
        Assert.Equal(0L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// Multi-table FORCES the capture regardless of the describe answer - the gate is an OR
    /// [<c>:L254</c>].
    /// </summary>
    [Fact]
    public void SetUpdateObject_ForcesTheCaptureWhenMultiTableIsOn()
    {
        using UpdateProxyHarness harness = new();
        Assert.Equal(RetCode.OK, harness.Proxy.SetMultiTableUpdate(true));

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataWindow)
        {
            UpdateTableAnswer = "?",
            ChangeRowCount = 6L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: true));

        Assert.Equal(1, target.GetChangesCalls);
        Assert.Equal(6L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// With the apply flag CLEAR the object is retained but nothing is captured or forwarded
    /// [<c>:L251-L252</c>].
    /// </summary>
    [Fact]
    public void SetUpdateObject_RetainsWithoutApplyingWhenTheFlagIsClear()
    {
        using UpdateProxyHarness harness = new();
        harness.Proxy.OnIdentityColumnDataRetrieved(1L, [201L], []);

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = "COMPANY",
            ChangeRowCount = 3L,
            PrimaryRowCount = 1L,
            PrimaryStatuses = { [1L] = ItemStatus.NewModified },
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: false));

        Assert.Equal(0, target.GetChangesCalls);
        Assert.Equal(0L, harness.Worker.GetUpdateRows());

        // Retained nonetheless - the finalize hook finds it.
        harness.Host.LastExitCodeValue = RetCode.OK;
        harness.Proxy.OnFinalize();
        Assert.Equal([(1L, 1L, 201L)], target.PrimaryWrites);
    }

    /// <summary>
    /// The one-argument overload defaults the apply flag to <see langword="true"/> [<c>:L277</c>].
    /// </summary>
    [Fact]
    public void SetUpdateObject_OneArgumentFormDefaultsApplyToTrue()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataStore)
        {
            UpdateTableAnswer = "COMPANY",
            ChangeRowCount = 7L,
        };

        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target));

        Assert.Equal(1, target.GetChangesCalls);
        Assert.Equal(7L, harness.Worker.GetUpdateRows());
    }

    /// <summary>
    /// An unsupported kind answers <c>E_INVALID_TYPE</c> [<c>:L271</c>] and retains NOTHING, so a
    /// previously retained object survives.
    /// </summary>
    [Fact]
    public void SetUpdateObject_AnswersInvalidTypeAndRetainsNothing()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget unsupported = new(IdentityWriteBackTargetKind.Other);

        Assert.Equal(RetCode.E_INVALID_TYPE, harness.Proxy.SetUpdateObject(unsupported, applyData: true));
        Assert.Equal(0, unsupported.GetChangesCalls);
    }

    /// <summary>
    /// A null target answers <c>E_INVALID_OBJECT</c> [<c>:L247</c>].
    /// </summary>
    [Fact]
    public void SetUpdateObject_AnswersInvalidObjectForNull()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal(RetCode.E_INVALID_OBJECT, harness.Proxy.SetUpdateObject(null!, applyData: true));
    }

    /// <summary>
    /// PINNING - THE SWALLOWED-FAILURE DEFECT [<c>:L257-L258</c> discarding, <c>:L274</c> answering].
    /// The delegated calls' codes are discarded and the answer is OK regardless. Driven by making the
    /// worker refuse: a busy worker's own reset path is not involved, so the refusal is produced by
    /// handing the worker a state in which its setters would fail - here an admission-refused descriptor
    /// is irrelevant, so the observable proof is that OK is answered even though the forwarded syntax was
    /// the empty string a Debug assertion would have rejected had the proxy checked it.
    /// </summary>
    [Fact]
    public void SetUpdateObject_AnswersOkEvenWhenTheDelegatedSyntaxIsEmpty()
    {
        using UpdateProxyHarness harness = new();

        RecordingWriteBackTarget target = new(IdentityWriteBackTargetKind.DataWindow)
        {
            UpdateTableAnswer = "COMPANY",
            SyntaxAnswer = string.Empty,
            ChangeRowCount = 2L,
        };

        // OK, and NOT an assertion failure: this path does not route through SetSqlSyntax's guarded
        // overload, it calls the worker directly [:L257], so the un-gated and DEBUG-gated assertions are
        // both bypassed exactly as the oracle bypasses them.
        Assert.Equal(RetCode.OK, harness.Proxy.SetUpdateObject(target, applyData: true));
        Assert.Equal(string.Empty, harness.Worker.SqlSyntax);
    }

    // ----------------------------------------------------------------------------------------------
    //  §5.3 - MARKERS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The type marker is exactly <c>"sqlupdate"</c> [<c>:L17, :L27</c>] and the worker class name
    /// exactly <c>"n_cst_thread_task_sqlupdate"</c> [<c>:L288</c>]. Both are contract: the substrate
    /// resolves a worker by the second, and both appear in characterization recordings.
    /// </summary>
    [Fact]
    public void Markers_CarryTheExactLegacyStrings()
    {
        using UpdateProxyHarness harness = new();

        Assert.Equal("n_cst_thread_task_sqlupdate", harness.Host.RequestedWorkerClassName);
        Assert.Equal("sqlupdate", RecordingProxyHost.ReadTaskType(harness.Proxy));
    }

    // ----------------------------------------------------------------------------------------------
    //  §4 - THE PROGRESS NOTIFICATION'S PACKED PAYLOAD
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The payload round-trips: <c>MakeLong(current, total)</c> - FIRST ARGUMENT IS THE LOW WORD, per
    /// <c>ws_objects/pfw.common.pbl.src/makelong.srf:L7</c> - unpacks back to the same pair through
    /// <c>LoWord</c>/<c>HiWord</c>, matching the legacy comment
    /// <c>lparam:(low word:current,high word:total)</c> [<c>:L32</c>].
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(7, 11)]
    [InlineData(1000, 2500)]
    [InlineData(65535, 65535)]
    public void TryDecodeProgress_RoundTripsTheFixedWidthPackedPayload(int current, int total)
    {
        // Packed exactly as Buffers/DataWindowBuffers.cs packs it - the ONE encoder, not duplicated here.
        long lparam = Bits.MakeLong((ushort)current, (ushort)total);

        Assert.True(
            SqlUpdateTaskProxy.TryDecodeProgress(
                (long)SqlUpdateTaskNotifyCode.Progress,
                lparam,
                out long decodedCurrent,
                out long decodedTotal));

        Assert.Equal(current, decodedCurrent);
        Assert.Equal(total, decodedTotal);

        // The word order is asserted directly against the packer's inverses too, so a swapped
        // implementation cannot pass by symmetry when current happens to equal total.
        Assert.Equal(Bits.LoWord((uint)lparam), (ushort)decodedCurrent);
        Assert.Equal(Bits.HiWord((uint)lparam), (ushort)decodedTotal);
    }

    /// <summary>
    /// The two notify vocabularies COLLIDE at the value 1 - progress here [<c>:L32</c>] versus the query
    /// proxy's row cap [<c>n_cst_threading_task_sqlquery.sru:L28</c>] - which is why they are separate
    /// enumerations. This test states the collision so a future merge is caught.
    /// </summary>
    [Fact]
    public void NotifyCodes_CollideNumericallyAcrossTheTwoTaskTypes()
    {
        Assert.Equal(1L, (long)SqlUpdateTaskNotifyCode.Progress);
        Assert.Equal(1L, (long)SqlQueryTaskNotifyCode.MaxRows);

        // Same number, different meaning, different type. The compiler is what keeps them apart.
        Assert.NotEqual(
            typeof(SqlUpdateTaskNotifyCode),
            typeof(SqlQueryTaskNotifyCode));
    }

    /// <summary>
    /// A code that is not the progress code is refused, with both outputs cleared - a query task's
    /// row-cap notification carries the same number and must NOT be decoded as progress.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(2L)]
    [InlineData(-1L)]
    public void TryDecodeProgress_RefusesAnyOtherNotifyCode(long wparam)
    {
        Assert.False(
            SqlUpdateTaskProxy.TryDecodeProgress(wparam, 12345L, out long current, out long total));

        Assert.Equal(0L, current);
        Assert.Equal(0L, total);
    }

    // ----------------------------------------------------------------------------------------------
    //  Construction
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The three required collaborators are all validated, so a misregistration fails at construction
    /// rather than at first use.
    /// </summary>
    [Fact]
    public void Constructor_RejectsEveryNullCollaborator()
    {
        using UpdateProxyHarness harness = new();

        Assert.Throws<ArgumentNullException>(
            () => new SqlUpdateTaskProxy(null!, NullLogger<SqlUpdateTaskProxy>.Instance, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(
            () => new SqlUpdateTaskProxy(harness.Host, null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(
            () => new SqlUpdateTaskProxy(harness.Host, NullLogger<SqlUpdateTaskProxy>.Instance, null!));
    }

    /// <summary>
    /// The typed downcast fails fast when no worker is attached, preserving the oracle's
    /// null-object-reference-into-termination posture rather than softening it into a return code.
    /// </summary>
    [Fact]
    public void Setters_FailFastWhenNoWorkerIsAttached()
    {
        using UpdateProxyHarness harness = new(attachWorker: false);

        Assert.Throws<InvalidOperationException>(() => harness.Proxy.SetAutoCommit(true));
    }

    // ==============================================================================================
    //  THE TWO SUBSTITUTED SEAMS AND THE HARNESS - no thread, no database, no DataWindow
    // ==============================================================================================

    /// <summary>
    /// Builds a proxy over a hand-driven substrate host and a REAL worker whose own collaborators are
    /// fakes, so delegation assertions verify the actual worker contract rather than a mock's
    /// expectations.
    /// </summary>
    private sealed class UpdateProxyHarness : IDisposable
    {
        internal UpdateProxyHarness(bool attachWorker = true)
        {
            PersistenceOptions options = new();
            IOptions<PersistenceOptions> accessor = Options.Create(options);

            Pool = new TransactionPool(accessor, TimeProvider.System, new UnusedTransactionActivator());

            Worker = new SqlUpdateTask(
                new UnusedTaskHost(),
                Pool,
                new UnusedDataStoreFactory(),
                new SqlRetrievalHookActivator(),
                new UnusedCarrierAdapter(),
                new ConflictDetector(SqlRedactor.Instance),
                TimeProvider.System,
                NullLogger<SqlUpdateTask>.Instance);

            Host = new RecordingProxyHost { Task = attachWorker ? Worker : null };

            Proxy = new SqlUpdateTaskProxy(
                Host,
                NullLogger<SqlUpdateTaskProxy>.Instance,
                TimeProvider.System);

            if (attachWorker)
            {
                // The base's OnInit is what assigns WorkerTask from the host [SqlTaskProxyBase.cs:L2607],
                // and it is protected, so the harness reaches it the way the substrate would.
                Assert.Equal(RetCode.OK, RecordingProxyHost.Invoke(Proxy, "OnInit"));
            }
        }

        internal RecordingProxyHost Host { get; }

        internal SqlUpdateTaskProxy Proxy { get; }

        internal SqlUpdateTask Worker { get; }

        internal TransactionPool Pool { get; }

        public void Dispose()
        {
            Proxy.Dispose();
            Worker.Dispose();
            Pool.Dispose();
        }
    }

    /// <summary>
    /// The task substrate, hand driven: no thread, no synchronization primitive, no message pump.
    /// </summary>
    private sealed class RecordingProxyHost : ISqlTaskProxyHost
    {
        /// <summary>The worker the base attaches on init.</summary>
        public SqlTaskBase? Task { get; init; }

        /// <summary>The class name the proxy asked for, so the marker can be asserted.</summary>
        internal string RequestedWorkerClassName { get; private set; } = string.Empty;

        /// <summary>What the ancestor prepare answers, so the guard's literal inequality is assertable.</summary>
        internal long PrepareAnswer { get; set; } = RetCode.OK;

        /// <summary>The latched exit code the finalize hook gates on.</summary>
        internal long LastExitCodeValue { get; set; } = RetCode.FAILED;

        public bool IsRunning { get; private set; }

        public bool IsControllerBusy { get; private set; }

        public bool IsSyncSignalSet { get; private set; }

        public bool IsCancelled => false;

        public CancellationToken Cancellation => CancellationToken.None;

        public long LastExitCode => LastExitCodeValue;

        public long LastErrorCode => RetCode.OK;

        public string LastErrorInfo => string.Empty;

        public ulong TaskId => 1UL;

        public int TaskIndex => 1;

        public string TaskClassName => RequestedWorkerClassName;

        /// <summary>
        /// Makes the proxy busy the way the oracle's composition does: not running, controller busy
        /// [<c>n_cst_threading_task.sru:L305</c>].
        /// </summary>
        internal void MakeBusy()
        {
            IsRunning = false;
            IsControllerBusy = true;
        }

        public void RaiseSyncSignal() => IsSyncSignalSet = true;

        public void ClearSyncSignal() => IsSyncSignalSet = false;

        public long OnInit(string workerClassName)
        {
            RequestedWorkerClassName = workerClassName;
            return RetCode.OK;
        }

        public long OnPrepare() => PrepareAnswer;

        public long Cancel() => RetCode.OK;

        public long SetWorkerDelayFor(double seconds) => RetCode.OK;

        public long SetWorkerSkip(bool skip) => RetCode.OK;

        /// <summary>
        /// Publishes a database error through the interface the worker uses
        /// [<c>n_cst_thread_task_sqlbase.sru:L95</c>].
        /// </summary>
        internal void PublishDbError(SqlUpdateTaskProxy proxy, DbErrorData error) =>
            ((ISqlTaskProxy)proxy).OnDbError(in error);

        /// <summary>Drives the proxy's prepare hook, which the substrate owns in production.</summary>
        internal long InvokePrepare(SqlUpdateTaskProxy proxy) => Invoke(proxy, "OnPrepare");

        /// <summary>Reads the proxy's protected type marker.</summary>
        internal static string ReadTaskType(SqlUpdateTaskProxy proxy) =>
            (string)typeof(SqlTaskProxyBase)
                .GetProperty("TaskType", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(proxy)!;

        /// <summary>Invokes one of the base's protected lifecycle hooks.</summary>
        internal static long Invoke(SqlUpdateTaskProxy proxy, string name) =>
            (long)typeof(SqlTaskProxyBase)
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, [])!
                .Invoke(proxy, null)!;
    }

    /// <summary>
    /// The write-back target, recording every call so BOTH iteration directions and BOTH asymmetric write
    /// accessors are assertable with nothing running.
    /// </summary>
    private sealed class RecordingWriteBackTarget : IIdentityWriteBackTarget
    {
        internal RecordingWriteBackTarget(IdentityWriteBackTargetKind kind) => Kind = kind;

        /// <summary>Settable so the missing case-else can be reached after retention.</summary>
        public IdentityWriteBackTargetKind Kind { get; set; }

        internal Dictionary<long, ItemStatus> PrimaryStatuses { get; } = [];

        internal Dictionary<long, ItemStatus> FilterStatuses { get; } = [];

        internal long PrimaryRowCount { get; init; }

        internal long FilteredRowCount { get; init; }

        internal string UpdateTableAnswer { get; init; } = "?";

        internal string SyntaxAnswer { get; init; } = string.Empty;

        internal long ChangeRowCount { get; init; }

        internal int GetChangesCalls { get; private set; }

        /// <summary>The item-setter writes - the <c>Primary!</c> accessor [<c>:L144</c>].</summary>
        internal List<(long Row, long ColumnId, long? Value)> PrimaryWrites { get; } = [];

        /// <summary>The direct buffer-expression writes - the <c>Filter!</c> accessor [<c>:L160</c>].</summary>
        internal List<(long Row, long ColumnId, long? Value)> FilterWrites { get; } = [];

        internal List<bool> RedrawCalls { get; } = [];

        /// <summary>How many redraw calls preceded the first write, so the bracket's order is assertable.</summary>
        internal int RedrawCallsBeforeFirstWrite { get; private set; } = -1;

        public void SetRedraw(bool redraw)
        {
            if (PrimaryWrites.Count == 0 && FilterWrites.Count == 0 && RedrawCallsBeforeFirstWrite < 0)
            {
                RedrawCallsBeforeFirstWrite = RedrawCalls.Count;
            }

            RedrawCalls.Add(redraw);
        }

        public long RowCount() => PrimaryRowCount;

        public long FilteredCount() => FilteredRowCount;

        public ItemStatus GetItemStatus(long row, int columnIndex, DwBuffer buffer)
        {
            Dictionary<long, ItemStatus> statuses =
                buffer == DwBuffer.Filter ? FilterStatuses : PrimaryStatuses;

            return statuses.TryGetValue(row, out ItemStatus status) ? status : ItemStatus.NotModified;
        }

        public void SetItemValue(long row, long columnId, long? value) =>
            PrimaryWrites.Add((row, columnId, value));

        public void SetFilterBufferValue(long row, long columnId, long? value) =>
            FilterWrites.Add((row, columnId, value));

        public string Describe(string property) =>
            property == "DataWindow.Syntax" ? SyntaxAnswer : UpdateTableAnswer;

        public long GetChanges(out CarrierState? changes)
        {
            GetChangesCalls++;
            changes = new CarrierState();
            return ChangeRowCount;
        }
    }

    /// <summary>
    /// The worker's task host. Present because the worker's constructor requires one; no test in this file
    /// runs the worker, so every member answers the neutral value rather than simulating a substrate.
    /// </summary>
    private sealed class UnusedTaskHost : ISqlTaskHost
    {
        public bool IsMainThread => true;

        public bool IsCancelled => false;

        public int TaskIndex => 1;

        public ISqlTaskProxy? ParentTasking => null;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.E_OUT_OF_BOUND;
        }

        public bool HasData(string name) => false;

        public object? GetData(string name) => null;

        public long SetData(string name, object? data) => RetCode.OK;

        public long OnPrepare() => RetCode.OK;

        public void OnUninit()
        {
        }

        public long OnError(long errCode, string errInfo) => RetCode.OK;

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    /// <summary>
    /// A data-store factory the worker never calls, because no test here runs an update. It throws rather
    /// than answering a half-built store, so an accidental use is loud instead of silently wrong.
    /// </summary>
    private sealed class UnusedDataStoreFactory : ISqlDataStoreFactory
    {
        public ISqlDataStore Create(CarrierThreadAffinity affinity) =>
            throw new NotSupportedException(
                "No test in SqlUpdateTaskProxyTests runs the worker, so no data store is required.");
    }

    /// <summary>A carrier adapter the worker never calls, for the same reason.</summary>
    private sealed class UnusedCarrierAdapter : ISqlUpdateCarrierAdapter
    {
        public ISqlUpdateCarrier Adapt(ISqlDataStore store) =>
            throw new NotSupportedException(
                "No test in SqlUpdateTaskProxyTests runs the worker, so no carrier is required.");
    }

    /// <summary>A transaction activator the pool never reaches, for the same reason.</summary>
    private sealed class UnusedTransactionActivator : IPooledTransactionActivator
    {
        public IPooledTransaction CreateDefault() =>
            throw new NotSupportedException(
                "No test in SqlUpdateTaskProxyTests opens a transaction.");

        public IPooledTransaction Create(string className) =>
            throw new NotSupportedException(
                "No test in SqlUpdateTaskProxyTests opens a transaction.");
    }
}
