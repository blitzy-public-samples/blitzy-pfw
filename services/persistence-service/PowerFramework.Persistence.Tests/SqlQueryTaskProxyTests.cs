// ==============================================================================================
//  SqlQueryTaskProxyTests.cs - THE CALLER-SIDE QUERY PROXY PARITY SUITES
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     services/persistence-service/PowerFramework.Persistence/Tasks/TaskProxies/
//                     SqlQueryTaskProxy.cs
//  ORACLE         ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru  (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru   (READ ONLY)
//                 ws_objects/pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru (READ ONLY)
//                 ws_objects/pfw.common.pbl.src/makelong.srf                           (READ ONLY)
//                 docs/PB多线程绕坑提示.md                                              (READ ONLY)
//
//  WHAT THESE SUITES ARE FOR. The proxy reproduces TEN legacy behaviours that a maintainer would
//  otherwise "fix", and a comment at the point of reproduction cannot stop that on its own. Each is
//  PINNED here by a test whose NAME says the behaviour is deliberate (C-B):
//
//    D1   the `>1` chunk normalisation runs in OPPOSITE directions - full state coerces to SUCCESS
//         [:L248] and a changeset coerces to FAILURE [:L250]              <- the big one
//    D2   the child handler carries the FAILURE form only [:L141], with no full-state counterpart
//    D3   the child's filter and sort re-apply only ABOVE length one [:L145-L150], because a length
//         of exactly one is PowerBuilder's "!" / "?" sentinel and not a one-character expression
//    D4   Reset() does NOT clear the record count or the redraw flag [:L282-L299] while the prepare
//         hook DOES clear both [:L503-L513]                              <- the easiest to "tidy"
//    D5   the ownership transfer is DESTROY-THEN-ADOPT [:L268-L269], in that order
//    D6   the data-move handler is the ONE payload handler with no cancellation check [:L268]
//    D7   the no-receiver arm has NO empty-payload guard at all [:L231-L243] while both receiver
//         arms do [:L207-L210, :L226-L229]
//    D8   the array parameter form calls the WORKER's reset [:L369], so the caller-side
//         has-parameters flag desynchronises on an empty array
//    D9   two members are PUBLIC despite the `_of_` private-convention prefix [:L442, :L457]
//    D10  three assertions, ALL of them DEBUG-gated [:L276, :L304, :L313], and none un-gated -
//         in deliberate contrast with the sibling update proxy's one-gated-one-un-gated split
//
//  plus the three MakeLong notification sites with their LOW-word-first packing [:L254, :L265,
//  :L270], the three-way receiver dispatch in which only the DataWindow arm suspends redraw and
//  recalculates groups, the busy guard on every setter and on none of the three count getters, the
//  one-based select-index default of 1 and the two audited one-based loops (R9), the carrier
//  hand-out that leaves a fresh carrier behind [:L416-L422], and idempotent deterministic disposal.
//
//  NO DATABASE, NO DataWindow RUNTIME, NO NETWORK AND NO REAL WORKER THREAD IS INVOLVED ANYWHERE IN
//  THIS FILE (C-H). Every collaborator is a hand-written double nested at the bottom of the class,
//  and the clock is a fixed TimeProvider that nothing advances - which is the point, because the
//  subject reads no clock at all. The only reflection here reaches the base class's three protected
//  members (OnInit, OnPrepare, Notifications) that the framework raises rather than a caller.
//
//  RULES POSITION. No user rules were provided for this project: the rules document contains
//  exactly one line saying so, and re-reading it returns the same. Nothing is invented or
//  back-filled in their place. The binding constraints are the enterprise-standard baseline plus
//  the named non-rule constraints, and C-B and C-H are the two that shape this file.
// ==============================================================================================

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Tasks.TaskProxies;
using PowerFramework.Persistence.Transactions;

namespace PowerFramework.Persistence.Tests;

public sealed class SqlQueryTaskProxyTests
{
    private static CarrierState NewState() => new();

    // ---------------------------------------------------------------------------------------------
    // 1. The >1 normalisation in BOTH directions, plus the child handler's failure-only form.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OnDataChunk_ChangesetArm_CoercesAboveOneToFailure_PreservedOppositeDirection()
    {
        Harness f = new();
        f.PayloadCodec.ApplyAnswer = 2L; // the runtime's partial-apply value

        CarrierState? payload = NewState();
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: false);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    [Theory]
    [InlineData(2L, DataWindowBufferStore.DataStoreSuccess)]
    [InlineData(7L, DataWindowBufferStore.DataStoreSuccess)]
    [InlineData(1L, DataWindowBufferStore.DataStoreSuccess)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, DataWindowBufferStore.DataStoreFailure)]
    public void OnDataChunk_FullStateArm_CoercesAboveOneToSuccess_PreservedOppositeDirection(long raw, long expected)
    {
        // The full-state arm of OnDataChunk IS this call, so asserting it asserts the arm. The managed
        // full-state apply answers only 1 or -1, so the above-one band is unreachable end to end -
        // which is exactly why FullStateCodec publishes the rule as a member.
        Assert.Equal(expected, FullStateCodec.NormalizeResult(raw));
    }

    [Theory]
    [InlineData(2L, DataWindowBufferStore.DataStoreFailure)]
    [InlineData(7L, DataWindowBufferStore.DataStoreFailure)]
    [InlineData(1L, DataWindowBufferStore.DataStoreSuccess)]
    [InlineData(0L, 0L)]
    [InlineData(-1L, DataWindowBufferStore.DataStoreFailure)]
    public void NormalizeChangesetChunkResult_IsTheOppositeDirection(long raw, long expected) =>
        Assert.Equal(expected, SqlQueryTaskProxy.NormalizeChangesetChunkResult(raw));

    [Fact]
    public void TheTwoNormalisations_TakeTheSameInputToOppositeSigns()
    {
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, FullStateCodec.NormalizeResult(2L));
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            SqlQueryTaskProxy.NormalizeChangesetChunkResult(2L));
    }

    [Fact]
    public void OnChildDataReceived_UsesTheFailureFormOnly_PreservedThirdDataPoint()
    {
        Harness f = new();
        FakeChildSurface child = new(f.HeldStore.Carrier);
        f.ChildResolver.Child = child;
        f.PayloadCodec.ApplyAnswer = 2L;

        CarrierState? payload = NewState();
        long result = f.Proxy.OnChildDataReceived("colname", ref payload);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    // ---------------------------------------------------------------------------------------------
    // 2. MakeLong packing for all three notification sites, round-tripped through LoWord/HiWord.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ChunkNotification_PacksCountLow_AndCurrentHigh()
    {
        Harness f = new();
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        CarrierState? payload = NewState();
        _ = f.Proxy.OnDataChunk(ref payload, count: 5L, current: 3L, fullState: false);

        (long wparam, long lparam, string text) = Assert.Single(seen);
        Assert.Equal((long)SqlQueryTaskNotifyCode.DataChunk, wparam);
        Assert.Equal(string.Empty, text);
        Assert.Equal(5, Bits.LoWord((uint)lparam));
        Assert.Equal(3, Bits.HiWord((uint)lparam));
    }

    [Fact]
    public void PageNotification_PacksPageCountLow_AndRecordCountHigh()
    {
        Harness f = new();
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        f.Proxy.OnPageReceived(pageCount: 11L, recordCount: 220L);

        (long wparam, long lparam, string text) = Assert.Single(seen);
        Assert.Equal((long)SqlQueryTaskNotifyCode.PageReceived, wparam);
        Assert.Equal(string.Empty, text);
        Assert.Equal(11, Bits.LoWord((uint)lparam));
        Assert.Equal(220, Bits.HiWord((uint)lparam));
        Assert.Equal(11L, f.Proxy.GetPageCount());
        Assert.Equal(220L, f.Proxy.GetRecordCount());
    }

    [Fact]
    public void PageNotification_WrapsTheNotCountedSentinel_ExactlyAsPowerBuilderDoes()
    {
        Harness f = new();
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        f.Proxy.OnPageReceived(pageCount: -1L, recordCount: -1L);

        (long _, long lparam, string _) = Assert.Single(seen);
        Assert.Equal(ushort.MaxValue, Bits.LoWord((uint)lparam));
        Assert.Equal(ushort.MaxValue, Bits.HiWord((uint)lparam));

        // The untruncated values remain readable, so nothing is lost.
        Assert.Equal(-1L, f.Proxy.GetPageCount());
        Assert.Equal(-1L, f.Proxy.GetRecordCount());
    }

    [Fact]
    public void DataMoveNotification_ReusesTheChunkCode_AndPacksOneOfOne()
    {
        Harness f = new();
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        f.Proxy.OnDataMove(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        (long wparam, long lparam, string text) = Assert.Single(seen);
        Assert.Equal((long)SqlQueryTaskNotifyCode.DataChunk, wparam);
        Assert.NotEqual((long)SqlQueryTaskNotifyCode.MaxRows, wparam);
        Assert.Equal(string.Empty, text);
        Assert.Equal(1, Bits.LoWord((uint)lparam));
        Assert.Equal(1, Bits.HiWord((uint)lparam));
    }

    [Fact]
    public void DataReceivedNotification_CarriesTheRowCountRaw_WithNoPacking()
    {
        Harness f = new();
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        f.Proxy.OnDataReceived(123_456L);

        (long wparam, long lparam, string _) = Assert.Single(seen);
        Assert.Equal((long)SqlQueryTaskNotifyCode.DataReceived, wparam);
        Assert.Equal(123_456L, lparam);
        Assert.Equal(123_456L, f.Proxy.GetRowCount());
    }

    [Fact]
    public void ChildNotification_CarriesTheNameInTheStringArgument_AndZeroNumeric()
    {
        Harness f = new();
        f.ChildResolver.Child = new FakeChildSurface(f.HeldStore.Carrier);
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        CarrierState? payload = NewState();
        _ = f.Proxy.OnChildDataReceived("dept_id", ref payload);

        (long wparam, long lparam, string text) = Assert.Single(seen);
        Assert.Equal((long)SqlQueryTaskNotifyCode.ChildReceived, wparam);
        Assert.Equal(0L, lparam);
        Assert.Equal("dept_id", text);
    }

    // ---------------------------------------------------------------------------------------------
    // 3. Ownership transfer.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OnDataMove_DestroysThePreviousCarrier_BeforeAdoptingTheIncomingOne()
    {
        Harness f = new();
        RecordingStore original = f.HeldStore;

        DataWindowCarrier incoming = DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance);

        f.Proxy.OnDataMove(incoming);

        Assert.Equal(1, original.ClearStateCalls);
        Assert.Equal(1, original.DisposeCalls);
        Assert.Same(incoming, Assert.Single(f.Adopter.Adopted));
        Assert.Same(f.Adopter.Produced, f.Proxy.Data);
        Assert.NotSame(original, f.Proxy.Data);
    }

    [Fact]
    public void OnDataMove_ProceedsEvenWhenCancellationIsSignalled_PreservedMissingCheck()
    {
        Harness f = new();
        RecordingStore original = f.HeldStore;
        f.Host.Cancelled = true;

        DataWindowCarrier incoming = DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance);

        f.Proxy.OnDataMove(incoming);

        Assert.Equal(1, original.DisposeCalls);
        Assert.Same(f.Adopter.Produced, f.Proxy.Data);
    }

    [Fact]
    public void EveryOtherPayloadHandler_DoesShortCircuitOnCancellation()
    {
        Harness f = new();
        f.ChildResolver.Child = new FakeChildSurface(f.HeldStore.Carrier);
        f.Host.Cancelled = true;

        CarrierState? chunk = NewState();
        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            f.Proxy.OnDataChunk(ref chunk, 1L, 1L, false));
        Assert.NotNull(chunk); // not even cleared - the early-out precedes the clear

        CarrierState? child = NewState();
        Assert.Equal(
            DataWindowBufferStore.EventContinue,
            f.Proxy.OnChildDataReceived("c", ref child));

        f.Proxy.OnCreateData("syntax");
        Assert.Empty(f.Runtime.CreatedSyntaxes);

        f.Proxy.OnDataReceived(99L);
        Assert.Equal(0L, f.Proxy.GetRowCount());

        f.Proxy.OnPageReceived(9L, 9L);
        Assert.Equal(0L, f.Proxy.GetPageCount());

        Assert.Equal(0, f.PayloadCodec.ApplyCalls);
    }

    // ---------------------------------------------------------------------------------------------
    // 4. The carrier hand-out.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void MoveData_HandsTheHeldCarrierOut_AndLeavesAFreshOneBehind()
    {
        Harness f = new();
        RecordingStore original = f.HeldStore;

        ISqlDataStore outgoing = f.Proxy.MoveData();

        Assert.Same(original, outgoing);
        Assert.NotNull(f.Proxy.Data);
        Assert.NotSame(original, f.Proxy.Data);

        // NOT a destroy: the caller owns what it received.
        Assert.Equal(0, original.ClearStateCalls);
        Assert.Equal(0, original.DisposeCalls);
    }

    [Fact]
    public void MoveData_HasNoBusyGuard()
    {
        Harness f = new();
        RecordingStore original = f.HeldStore;
        f.Host.Running = true;
        f.Host.SyncSignalSet = false; // busy

        Assert.True(f.Proxy.IsBusy());
        Assert.Same(original, f.Proxy.MoveData());
    }

    // ---------------------------------------------------------------------------------------------
    // 5. The reset/prepare divergence - the pinning tests.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Reset_DoesNotClearTheRecordCountNorTheRedrawFlag_PreservedLegacyDefect()
    {
        Harness f = new();
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver receiver = new(QueryReceiverKind.DataWindow, receiverStore);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(receiver));

        // Raise the redraw debt through the real first-chunk path, and latch a record count.
        f.Proxy.OnPageReceived(pageCount: 4L, recordCount: 44L);
        CarrierState? payload = NewState();
        _ = f.Proxy.OnDataChunk(ref payload, count: 2L, current: 1L, fullState: false);
        Assert.Equal([false], receiver.RedrawCalls);

        Assert.Equal(RetCode.OK, f.Proxy.Reset());

        // The neighbours ARE cleared...
        Assert.Equal(0L, f.Proxy.GetPageCount());
        Assert.Equal(0L, f.Proxy.GetRowCount());
        // ...and the record count is NOT.
        Assert.Equal(44L, f.Proxy.GetRecordCount());

        // The redraw flag survived too: OnFinalize still finds a debt to forget. The receiver was
        // cleared by the reset, so the flag is lowered without a restore - which is the legacy's own
        // behaviour and is asserted by the ABSENCE of a second redraw call.
        f.Proxy.OnFinalize();
        Assert.Equal([false], receiver.RedrawCalls);

        // A second finalize now finds no debt, proving the first one consumed it.
        f.Proxy.OnFinalize();
        Assert.Equal([false], receiver.RedrawCalls);
    }

    [Fact]
    public void OnPrepare_DoesClearTheRecordCountAndTheRedrawFlag_TheDivergence()
    {
        Harness f = new();
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver receiver = new(QueryReceiverKind.DataWindow, receiverStore);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(receiver));

        f.Proxy.OnPageReceived(pageCount: 4L, recordCount: 44L);
        f.Proxy.OnDataReceived(9L);
        CarrierState? payload = NewState();
        _ = f.Proxy.OnDataChunk(ref payload, count: 2L, current: 1L, fullState: false);

        Assert.Equal(0L, Harness.InvokeOnPrepare(f.Proxy));

        Assert.Equal(0L, f.Proxy.GetRecordCount());
        Assert.Equal(0L, f.Proxy.GetPageCount());
        Assert.Equal(0L, f.Proxy.GetRowCount());
        Assert.Equal(string.Empty, f.Proxy.Data.DataObject);

        // The redraw flag was cleared, so the finalize hook finds no debt and issues no restore even
        // though the DataWindow receiver is still installed.
        f.Proxy.OnFinalize();
        Assert.Equal([false], receiver.RedrawCalls);
    }

    [Fact]
    public void OnPrepare_AncestorVetoArmIsPreserved_ButTheBaseSwallowsTheHostAnswer()
    {
        // VERIFIED IN THE ORACLE: n_cst_threading_task_sqlbase.sru:L206-L211 returns the bare literal
        // 0 UNCONDITIONALLY, discarding its own super's answer, so AncestorReturnValue at
        // n_cst_threading_task_sqlquery.sru:L503 can never be non-zero through the shipped ancestor.
        // The guard is preserved-but-unreachable legacy code. This test pins that fact: a non-zero host
        // answer does NOT abort, and the four clears still happen.
        Harness f = new();
        f.Proxy.OnPageReceived(pageCount: 4L, recordCount: 44L);
        f.Host.PrepareAnswer = RetCode.E_BUSY;

        Assert.Equal(0L, Harness.InvokeOnPrepare(f.Proxy));

        Assert.Equal(0L, f.Proxy.GetRecordCount());
        Assert.Equal(0L, f.Proxy.GetPageCount());
    }

    [Fact]
    public void Reset_CallsTheBaseFirst_AndAbandonsOnItsFailure()
    {
        Harness f = new();
        Assert.Equal(RetCode.OK, f.Proxy.SetPageCounting(false));
        Assert.Equal(RetCode.OK, f.Proxy.SetPaged(true));
        Assert.Equal(RetCode.OK, f.Proxy.SetPageSize(50L));
        f.Proxy.OnDataReceived(77L);

        // Busy: the BASE's guard refuses, so nothing of this type's state may move.
        f.Host.Running = true;
        f.Host.SyncSignalSet = false;

        Assert.Equal(RetCode.E_BUSY, f.Proxy.Reset());

        Assert.False(f.Proxy.PageCounting);
        Assert.True(f.Proxy.Paged);
        Assert.Equal(50L, f.Proxy.PageSize);
        Assert.Equal(77L, f.Proxy.GetRowCount());
        Assert.Equal("initial", f.Proxy.Data.DataObject);
    }

    [Fact]
    public void Reset_ClearsTheCarrierDefinitionAndTheReceiver_AndAnswersOk()
    {
        Harness f = new();
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataStore, receiverStore)));
        Assert.True(f.Proxy.HasReceiver);

        Assert.Equal(RetCode.OK, f.Proxy.Reset());

        Assert.False(f.Proxy.HasReceiver);
        Assert.Equal(string.Empty, f.Proxy.Data.DataObject);
    }

    [Fact]
    public void PageCounting_DefaultsToTrueOnConstruction_AndAgainAfterReset()
    {
        Harness f = new();

        Assert.True(f.Proxy.PageCounting);
        Assert.False(f.Proxy.Paged);
        Assert.Equal(0L, f.Proxy.PageSize);
        Assert.Equal(0L, f.Proxy.PageIndex);

        Assert.Equal(RetCode.OK, f.Proxy.SetPageCounting(false));
        Assert.False(f.Proxy.PageCounting);

        Assert.Equal(RetCode.OK, f.Proxy.Reset());
        Assert.True(f.Proxy.PageCounting);
    }

    // ---------------------------------------------------------------------------------------------
    // 6. The > 1 sentinel screen on the child's filter and sort.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("?", false)]
    [InlineData("!", false)]
    [InlineData("", false)]
    [InlineData("ab", true)]
    [InlineData("age > 30", true)]
    public void ChildFilterAndSort_ReapplyOnlyAboveLengthOne_TheSentinelScreen(string described, bool reapplied)
    {
        Harness f = new();
        FakeChildSurface child = new(f.HeldStore.Carrier);
        child.SetDescribe(SqlQueryTaskProxy.ChildFilterProperty, described);
        child.SetDescribe(SqlQueryTaskProxy.ChildSortProperty, described);
        f.ChildResolver.Child = child;

        CarrierState? payload = NewState();
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            f.Proxy.OnChildDataReceived("c", ref payload));

        Assert.Equal(reapplied ? 1 : 0, child.FilterCalls);
        Assert.Equal(reapplied ? 1 : 0, child.SortCalls);
    }

    [Fact]
    public void OnChildDataReceived_ClearsTheBuffer_AndResetsUnconditionally()
    {
        Harness f = new();
        FakeChildSurface child = new(f.HeldStore.Carrier);
        f.ChildResolver.Child = child;

        // EMPTY payload: still reset, still success, and the apply is never called.
        CarrierState? payload = null;
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            f.Proxy.OnChildDataReceived("c", ref payload));
        Assert.Equal(0, f.PayloadCodec.ApplyCalls);

        // NON-empty payload: applied, then CLEARED through the ref.
        payload = NewState();
        Assert.Equal(
            DataWindowBufferStore.DataStoreSuccess,
            f.Proxy.OnChildDataReceived("c", ref payload));
        Assert.Null(payload);
        Assert.Equal(1, f.PayloadCodec.ApplyCalls);
    }

    [Fact]
    public void OnChildDataReceived_ChildResolutionFailure_IsAFailureAndPublishesNothing()
    {
        Harness f = new();
        f.ChildResolver.Resolves = false;
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        CarrierState? payload = NewState();
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            f.Proxy.OnChildDataReceived("c", ref payload));

        Assert.Null(payload); // cleared even on the failure path, at :L139
        Assert.Empty(seen);
    }

    [Fact]
    public void OnChildDataReceived_TargetsTheReceiverStore_WhenOneIsInstalled()
    {
        Harness f = new();
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataWindow, receiverStore)));
        f.ChildResolver.Child = new FakeChildSurface(receiverStore.Carrier);

        CarrierState? payload = NewState();
        _ = f.Proxy.OnChildDataReceived("c", ref payload);

        Assert.Same(receiverStore, Assert.Single(f.ChildResolver.Targets));
    }

    // ---------------------------------------------------------------------------------------------
    // 7. The three chunk arms - the empty-payload asymmetry and the presentational restriction.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void OnDataChunk_NoReceiverArm_AppliesAnEmptyPayloadUnconditionally_PreservedAsymmetry()
    {
        Harness f = new();
        f.PayloadCodec.ApplyAnswer = DataWindowBufferStore.DataStoreFailure;

        CarrierState? payload = null;
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: false);

        // The apply RAN on an empty payload and its failure stands - no empty-payload short circuit.
        Assert.Equal(1, f.PayloadCodec.ApplyCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnDataChunk_BothReceiverArms_ShortCircuitAnEmptyPayloadWithSuccess(bool isDataWindow)
    {
        QueryReceiverKind kind = isDataWindow
            ? QueryReceiverKind.DataWindow
            : QueryReceiverKind.DataStore;
        Harness f = new();
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(new RecordingReceiver(kind, receiverStore)));
        f.PayloadCodec.ApplyAnswer = DataWindowBufferStore.DataStoreFailure;

        CarrierState? payload = null;
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: false);

        Assert.Equal(0, f.PayloadCodec.ApplyCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, result);
    }

    [Fact]
    public void OnDataChunk_OnlyTheDataWindowArm_SuspendsRedrawAndRecalculatesGroups()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver dw = new(QueryReceiverKind.DataWindow, store);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(dw));

        CarrierState? first = NewState();
        _ = f.Proxy.OnDataChunk(ref first, count: 2L, current: 1L, fullState: false);
        Assert.Equal([false], dw.RedrawCalls);
        Assert.Equal(0, dw.GroupCalcCalls);

        CarrierState? last = NewState();
        _ = f.Proxy.OnDataChunk(ref last, count: 2L, current: 2L, fullState: false);
        Assert.Equal([false, true], dw.RedrawCalls);
        Assert.Equal(1, dw.GroupCalcCalls);

        // The debt is paid, so the finalize hook adds nothing.
        f.Proxy.OnFinalize();
        Assert.Equal([false, true], dw.RedrawCalls);
    }

    [Fact]
    public void OnDataChunk_TheDataStoreArm_TouchesNeitherRedrawNorGrouping()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver ds = new(QueryReceiverKind.DataStore, store);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(ds));

        CarrierState? first = NewState();
        _ = f.Proxy.OnDataChunk(ref first, count: 2L, current: 1L, fullState: false);
        CarrierState? last = NewState();
        _ = f.Proxy.OnDataChunk(ref last, count: 2L, current: 2L, fullState: false);

        Assert.Empty(ds.RedrawCalls);
        Assert.Equal(0, ds.GroupCalcCalls);
    }

    [Fact]
    public void OnFinalize_RestoresRedraw_OnlyOnTheDataWindowBranch()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver dw = new(QueryReceiverKind.DataWindow, store);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(dw));

        // An INTERRUPTED stream: the first chunk arrives and the last never does.
        CarrierState? first = NewState();
        _ = f.Proxy.OnDataChunk(ref first, count: 9L, current: 1L, fullState: false);
        Assert.Equal([false], dw.RedrawCalls);

        f.Proxy.OnFinalize();
        Assert.Equal([false, true], dw.RedrawCalls);

        // Idempotent: the debt is consumed.
        f.Proxy.OnFinalize();
        Assert.Equal([false, true], dw.RedrawCalls);
    }

    [Fact]
    public void OnDataChunk_ClearsTheBufferOnEveryArm()
    {
        Harness f = new();
        CarrierState? payload = NewState();
        _ = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: false);
        Assert.Null(payload);
    }

    [Fact]
    public void OnDataChunk_PublishesNothing_WhenTheApplyFails()
    {
        Harness f = new();
        f.PayloadCodec.ApplyAnswer = DataWindowBufferStore.DataStoreFailure;
        List<(long Wparam, long Lparam, string Text)> seen = f.CaptureNotifications();

        CarrierState? payload = NewState();
        Assert.Equal(
            DataWindowBufferStore.DataStoreFailure,
            f.Proxy.OnDataChunk(ref payload, 1L, 1L, false));

        Assert.Empty(seen);
    }

    // ---------------------------------------------------------------------------------------------
    // 8. The parameter forms - including the preserved desync.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SetParams_WithAnEmptyArray_ClearsTheWorkerParamsButLeavesHasParamsUnchanged_PreservedDesync()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.AddParam("p", 1L));
        Assert.True(f.Proxy.HasParams());

        Assert.Equal(RetCode.OK, f.Proxy.SetParams());

        // THE DESYNC: the worker's list is empty and the caller-side flag still says otherwise.
        Assert.False(f.Worker!.HasParams());
        Assert.True(f.Proxy.HasParams());
    }

    [Fact]
    public void ResetParams_TheInheritedFormDoesClearTheFlag_WhichIsWhatMakesTheDesyncADefect()
    {
        Harness f = new();
        Assert.Equal(RetCode.OK, f.Proxy.AddParam("p", 1L));
        Assert.True(f.Proxy.HasParams());

        Assert.Equal(RetCode.OK, f.Proxy.ResetParams());
        Assert.False(f.Proxy.HasParams());
    }

    [Fact]
    public void SetParams_WithANonEmptyArray_InstallsEveryElementPositionally()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetParams(1L, "two", 3.0m));

        Assert.True(f.Proxy.HasParams());
        Assert.True(f.Worker!.HasParams());
    }

    [Fact]
    public void SetParam_WrapsExactlyOneElement()
    {
        Harness f = new();
        Assert.Equal(RetCode.OK, f.Proxy.SetParam(42L));
        Assert.True(f.Worker!.HasParams());
    }

    [Fact]
    public void SetParams_BailsOnTheFirstNonOkElement()
    {
        Harness f = new();

        // A two-dimensional value is rejected by the inherited AddParam with E_INVALID_ARGUMENT.
        long[,] rejected = new long[2, 2];

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, f.Proxy.SetParams(1L, rejected, 3L));
    }

    // ---------------------------------------------------------------------------------------------
    // 9. Clause setters, paged unique index, receiver setter.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SetWhereClause_TwoArgumentForm_DelegatesWithSelectIndexOne()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetWhereClause(Enums.SQL_MS_REPLACE, "age > 30"));

        SqlClause stored = Assert.Single(f.Worker!.Clauses.WhereClauses);
        Assert.Equal(1, stored.SelectIndex);
        Assert.Equal(Enums.SQL_MS_REPLACE, stored.ModifyStyle);
        Assert.Equal("age > 30", stored.Clause);
    }

    [Fact]
    public void SetOrderByClause_TwoArgumentForm_DelegatesWithSelectIndexOne()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetOrderByClause(Enums.SQL_MS_APPEND, "age A"));

        SqlClause stored = Assert.Single(f.Worker!.Clauses.OrderByClauses);
        Assert.Equal(1, stored.SelectIndex);
        Assert.Equal(Enums.SQL_MS_APPEND, stored.ModifyStyle);
    }

    [Fact]
    public void SetWhereAndOrderByClause_ThreeArgumentForms_CarryTheSuppliedIndexUnconverted()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetWhereClause(3, Enums.SQL_MS_PREPEND, "x = 1"));

        SqlClause stored = Assert.Single(f.Worker!.Clauses.WhereClauses);
        Assert.Equal(3, stored.SelectIndex);
    }

    [Theory]
    [InlineData("COMPANY.age")]
    [InlineData("a.b")]
    public void SetPagedUniqueIndexColumns_QualifiedColumns_AreForwarded(string column)
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetPagedUniqueIndexColumns([column]));
        Assert.Equal([column], f.Worker!.PagedUniqueIndexColumns);
    }

    [Theory]
    [InlineData("age")]
    [InlineData("")]
    public void SetPagedUniqueIndexColumns_UnqualifiedColumns_AreRejected(string column)
    {
        Harness f = new();

        Assert.Equal(
            RetCode.E_INVALID_ARGUMENT,
            f.Proxy.SetPagedUniqueIndexColumns(["COMPANY.id", column]));

        // Nothing was forwarded - the bail happens before the delegation.
        Assert.Empty(f.Worker!.PagedUniqueIndexColumns);
    }

    [Fact]
    public void SetReceiver_RejectsAnInvalidObjectAndAWrongType_Differently()
    {
        Harness f = new();

        Assert.Equal(RetCode.E_INVALID_OBJECT, f.Proxy.SetReceiver(null));
        Assert.Equal(RetCode.E_INVALID_TYPE, f.Proxy.SetReceiver(new object()));
        Assert.Equal(RetCode.E_INVALID_TYPE, f.Proxy.SetReceiver("a string is not a receiver"));
        Assert.False(f.Proxy.HasReceiver);

        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataStore, store)));
        Assert.True(f.Proxy.HasReceiver);
    }

    // ---------------------------------------------------------------------------------------------
    // 10. The busy guard - on every setter, on none of the three getters.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheBusyGuard_FiresOnEverySetter()
    {
        Harness f = new();
        f.Host.Running = true;
        f.Host.SyncSignalSet = false;
        Assert.True(f.Proxy.IsBusy());

        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        List<(string Name, long Result)> answers =
        [
            ("SetSql", f.Proxy.SetSql("select 1")),
            ("SetSqlSyntax", f.Proxy.SetSqlSyntax("syntax")),
            ("SetDataObject", f.Proxy.SetDataObject("dw_sqlite")),
            ("SetPaged", f.Proxy.SetPaged(true)),
            ("SetPageSize", f.Proxy.SetPageSize(10L)),
            ("SetPageIndex", f.Proxy.SetPageIndex(2L)),
            ("SetPageCounting", f.Proxy.SetPageCounting(false)),
            ("SetChunkSize", f.Proxy.SetChunkSize(5000L)),
            ("SetMaxRows", f.Proxy.SetMaxRows(10L)),
            ("SetPageNative", f.Proxy.SetPageNative(true)),
            ("SetCache", f.Proxy.SetCache(true)),
            ("SetFilter", f.Proxy.SetFilter("age > 1")),
            ("SetSort", f.Proxy.SetSort("age A")),
            ("SetHookClass", f.Proxy.SetHookClass("hook")),
            ("SetWhereClause2", f.Proxy.SetWhereClause(Enums.SQL_MS_REPLACE, "x")),
            ("SetWhereClause3", f.Proxy.SetWhereClause(1, Enums.SQL_MS_REPLACE, "x")),
            ("SetOrderByClause2", f.Proxy.SetOrderByClause(Enums.SQL_MS_REPLACE, "x")),
            ("SetOrderByClause3", f.Proxy.SetOrderByClause(1, Enums.SQL_MS_REPLACE, "x")),
            ("SetPagedUniqueIndexColumns", f.Proxy.SetPagedUniqueIndexColumns(["a.b"])),
            ("SetReceiver", f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataStore, store))),
            ("SetParam", f.Proxy.SetParam(1L)),
            ("SetParams", f.Proxy.SetParams(1L, 2L)),
        ];

        foreach ((string name, long result) in answers)
        {
            Assert.Equal(RetCode.E_BUSY, result);
        }

        Assert.Equal(22, answers.Count);

        // The local properties were NOT written either, because the guard precedes the write.
        Assert.False(f.Proxy.Paged);
        Assert.Equal(0L, f.Proxy.PageSize);
        Assert.Equal(0L, f.Proxy.PageIndex);
        Assert.True(f.Proxy.PageCounting);
        Assert.False(f.Proxy.HasReceiver);
    }

    [Fact]
    public void TheThreeCountGetters_HaveNoBusyGuard()
    {
        Harness f = new();
        f.Proxy.OnDataReceived(5L);
        f.Proxy.OnPageReceived(2L, 20L);

        f.Host.Running = true;
        f.Host.SyncSignalSet = false;
        Assert.True(f.Proxy.IsBusy());

        Assert.Equal(5L, f.Proxy.GetRowCount());
        Assert.Equal(2L, f.Proxy.GetPageCount());
        Assert.Equal(20L, f.Proxy.GetRecordCount());
    }

    [Fact]
    public void TheLocalWriteAndTheForward_BothHappen()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.Proxy.SetPaged(true));
        Assert.Equal(RetCode.OK, f.Proxy.SetPageSize(25L));
        Assert.Equal(RetCode.OK, f.Proxy.SetPageIndex(3L));
        Assert.Equal(RetCode.OK, f.Proxy.SetPageCounting(false));

        Assert.True(f.Proxy.Paged);
        Assert.Equal(25L, f.Proxy.PageSize);
        Assert.Equal(3L, f.Proxy.PageIndex);
        Assert.False(f.Proxy.PageCounting);

        Assert.True(f.Worker!.Paged);
        Assert.Equal(25L, f.Worker.PageSize);
        Assert.Equal(3L, f.Worker.PageIndex);
        Assert.False(f.Worker.PageCounting);
    }

    [Fact]
    public void DelegateOnlySetters_Forward_AndSurfaceTheWorkerAnswer()
    {
        Harness f = new();

        // The worker's own inclusive floor of 1000 is NOT duplicated here - it is surfaced.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, f.Proxy.SetChunkSize(1000L));
        Assert.Equal(RetCode.OK, f.Proxy.SetChunkSize(1001L));
        Assert.Equal(1001L, f.Worker!.ChunkSize);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, f.Proxy.SetMaxRows(-1L));
        Assert.Equal(RetCode.OK, f.Proxy.SetMaxRows(500L));
        Assert.Equal(500L, f.Worker.MaxRows);

        Assert.Equal(RetCode.OK, f.Proxy.SetCache(true));
        Assert.True(f.Worker.Cache);

        Assert.Equal(RetCode.OK, f.Proxy.SetPageNative(true));
        Assert.True(f.Worker.PageNative);

        Assert.Equal(RetCode.OK, f.Proxy.SetSql("select * from COMPANY"));
        Assert.Equal("select * from COMPANY", f.Worker.Sql);

        Assert.Equal(RetCode.OK, f.Proxy.SetSqlSyntax("release 12.5;"));
        Assert.Equal("release 12.5;", f.Worker.SqlSyntax);

        Assert.Equal(RetCode.OK, f.Proxy.SetDataObject("dw_sqlite"));
        Assert.Equal("dw_sqlite", f.Worker.DataObject);

        Assert.Equal(RetCode.OK, f.Proxy.SetFilter("age > 30"));
        Assert.Equal("age > 30", f.Worker.NewFilter);

        Assert.Equal(RetCode.OK, f.Proxy.SetSort("age A"));
        Assert.Equal("age A", f.Worker.NewSort);

        Assert.Equal(RetCode.OK, f.Proxy.SetHookClass("n_hook"));
        Assert.Equal("n_hook", f.Worker.HookClass);
    }

    // ---------------------------------------------------------------------------------------------
    // 11. Identity, the two public `_of_` members, create-data and the error channel.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheWorkerClassNameAndTheTaskType_AreTheLegacyStrings()
    {
        Harness f = new();

        Assert.Equal(RetCode.OK, f.InitResult);
        Assert.Equal("n_cst_thread_task_sqlquery", f.Host.InitClassName);
    }

    [Fact]
    public void NeedsCreatedObject_ComparesTheDescribedType_AgainstTheLegacyLiteral()
    {
        Harness f = new();

        // A fresh carrier describes as the failure sentinel, which is not "datawindow".
        Assert.True(f.Proxy.NeedsCreatedObject);

        f.HeldStore.SetDescribe(SqlQueryTaskProxy.TypeProperty, SqlQueryTaskProxy.BuiltObjectTypeValue);
        Assert.False(f.Proxy.NeedsCreatedObject);

        // With a receiver installed the RECEIVER is interrogated, not the held carrier.
        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataWindow, receiverStore)));
        Assert.True(f.Proxy.NeedsCreatedObject);

        receiverStore.SetDescribe(
            SqlQueryTaskProxy.TypeProperty,
            SqlQueryTaskProxy.BuiltObjectTypeValue);
        Assert.False(f.Proxy.NeedsCreatedObject);
    }

    [Fact]
    public void OnCreateData_BuildsTheReceiverWhenInstalled_AndTheCarrierOtherwise()
    {
        Harness f = new();

        f.Proxy.OnCreateData("syntax-A");
        Assert.Equal("syntax-A", f.HeldStore.CreatedFromSyntax);

        RecordingStore receiverStore = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataStore, receiverStore)));

        f.Proxy.OnCreateData("syntax-B");
        Assert.Equal("syntax-B", receiverStore.CreatedFromSyntax);
        Assert.Equal("syntax-A", f.HeldStore.CreatedFromSyntax);
    }

    [Fact]
    public void ReportError_GoesToTheFrameworkErrorChannel_AndNotToTheDbErrorLatch()
    {
        Harness f = new();
        List<(long Wparam, string Text)> errors = f.CaptureErrors();
        List<(long Wparam, long Lparam, string Text)> notifications = f.CaptureNotifications();

        f.Proxy.ReportError(RetCode.E_INTERNAL_ERROR, "GetChanges Failed");

        (long wparam, string text) = Assert.Single(errors);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, wparam);
        Assert.Equal("GetChanges Failed", text);
        Assert.Empty(notifications);

        // The database-error latch is untouched.
        Assert.Equal(DbErrorData.Empty, f.Proxy.GetLastDbErrorData());
    }

    // ---------------------------------------------------------------------------------------------
    // 12. The sink adaptation - nothing returns a buffer, and the arms route correctly.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SendChunkAsync_RoutesToTheChangesetArm()
    {
        Harness f = new();
        f.PayloadCodec.ApplyAnswer = 2L;

        IQueryResultSink sink = f.Proxy;
        long result = await sink.SendChunkAsync(
            new ChangesetChunk(NewState(), chunkCount: 1L, chunkIndex: 1L),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    [Fact]
    public async Task SendChildChunkAsync_RoutesToTheChildHandler()
    {
        Harness f = new();
        f.ChildResolver.Child = new FakeChildSurface(f.HeldStore.Carrier);

        IQueryResultSink sink = f.Proxy;
        long result = await sink.SendChildChunkAsync(
            new ChangesetChildPayload("dept_id", NewState()),
            TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, result);
        Assert.Equal("dept_id", Assert.Single(f.ChildResolver.Names));
    }

    [Fact]
    public async Task CreateDataAsync_RoutesToTheCreateHandler_AndAnswersContinue()
    {
        Harness f = new();

        IQueryResultSink sink = f.Proxy;
        long result = await sink.CreateDataAsync("syntax", TestContext.Current.CancellationToken);

        Assert.Equal(DataWindowBufferStore.EventContinue, result);
        Assert.Equal("syntax", Assert.Single(f.Runtime.CreatedSyntaxes));
    }

    [Fact]
    public void SendFullStateChunk_ForwardsTheChunkFlag()
    {
        Harness f = new();
        IQueryResultSink sink = f.Proxy;

        // FullState = true takes the full-state arm, which rejects a non-canonical image with -1.
        long fullStateResult = sink.SendFullStateChunk(new QueryDataChunk
        {
            State = NewState(),
            ChunkCount = 1L,
            ChunkIndex = 1L,
            FullState = true,
        });
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, fullStateResult);
        Assert.Equal(0, f.PayloadCodec.ApplyCalls);

        // FullState = false takes the changeset arm, which reaches the substituted payload codec.
        long changesetResult = sink.SendFullStateChunk(new QueryDataChunk
        {
            State = NewState(),
            ChunkCount = 1L,
            ChunkIndex = 1L,
            FullState = false,
        });
        Assert.Equal(DataWindowBufferStore.DataStoreSuccess, changesetResult);
        Assert.Equal(1, f.PayloadCodec.ApplyCalls);
    }

    [Fact]
    public void TheSinkStateQuestions_AreTheTwoPublicUnderscorePrefixedMembers()
    {
        Harness f = new();
        IQueryResultSink sink = f.Proxy;

        Assert.False(sink.HasReceiver);
        Assert.True(sink.NeedsCreatedObject);

        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataWindow, store)));
        Assert.True(sink.HasReceiver);
    }

    // ---------------------------------------------------------------------------------------------
    // 13. Disposal.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Dispose_DestroysTheHeldCarrierExactlyOnce_AndIsIdempotent()
    {
        Harness f = new();
        RecordingStore held = f.HeldStore;

        f.Proxy.Dispose();
        f.Proxy.Dispose();
        f.Proxy.Dispose();

        Assert.Equal(1, held.ClearStateCalls);
        Assert.Equal(1, held.DisposeCalls);
    }

    [Fact]
    public void Dispose_DestroysTheAdoptedCarrier_AfterAnOwnershipTransfer()
    {
        Harness f = new();
        RecordingStore original = f.HeldStore;

        f.Proxy.OnDataMove(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));

        RecordingStore adopted = (RecordingStore)f.Proxy.Data;
        Assert.NotSame(original, adopted);

        f.Proxy.Dispose();

        // The original was destroyed by the transfer, exactly once, and not again by the dispose.
        Assert.Equal(1, original.DisposeCalls);
        Assert.Equal(1, adopted.DisposeCalls);
    }

    [Fact]
    public void Dispose_DoesNotDestroyACarrier_ThatWasHandedOut()
    {
        Harness f = new();
        ISqlDataStore handedOut = f.Proxy.MoveData();
        RecordingStore replacement = (RecordingStore)f.Proxy.Data;

        f.Proxy.Dispose();

        Assert.Equal(0, ((RecordingStore)handedOut).DisposeCalls);
        Assert.Equal(1, replacement.DisposeCalls);
    }

    [Fact]
    public void Dispose_ReleasesTheReceiver_WithoutDestroyingIt()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        Assert.Equal(
            RetCode.OK,
            f.Proxy.SetReceiver(new RecordingReceiver(QueryReceiverKind.DataWindow, store)));

        f.Proxy.Dispose();

        Assert.False(f.Proxy.HasReceiver);
        Assert.Equal(0, store.DisposeCalls);
    }

    // ---------------------------------------------------------------------------------------------
    // 14. Guard clauses.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheHandlers_RejectStructurallyInvalidArguments()
    {
        Harness f = new();

        CarrierState? payload = NewState();
        CarrierState? captured = payload;
        _ = Assert.Throws<ArgumentNullException>(
            () => f.Proxy.OnChildDataReceived(null!, ref captured));
        _ = Assert.Throws<ArgumentException>(() => f.Proxy.OnChildDataReceived("   ", ref captured));
        _ = Assert.Throws<ArgumentNullException>(() => f.Proxy.OnCreateData(null!));
        _ = Assert.Throws<ArgumentNullException>(() => f.Proxy.OnDataMove(null!));
        _ = Assert.Throws<ArgumentNullException>(() => f.Proxy.ReportError(0L, null!));
        _ = Assert.Throws<ArgumentNullException>(() => f.Proxy.SendFullStateChunk(null!));
    }

    [Fact]
    public void AProxyWithNoAttachedWorker_FailsFastOnADelegatingSetter()
    {
        Harness f = new(attachWorker: false);

        _ = Assert.Throws<InvalidOperationException>(() => f.Proxy.SetSql("select 1"));
    }

    [Fact]
    public void AFullStatePayload_ReachesTheFullStateApply_OnTheDataWindowReceiverArm()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver dw = new(QueryReceiverKind.DataWindow, store);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(dw));

        CarrierState? payload = NewState();
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: true);

        // The changeset codec is bypassed entirely, and no presentational call is made on the
        // full-state arm even on the DataWindow receiver.
        Assert.Equal(0, f.PayloadCodec.ApplyCalls);
        Assert.Empty(dw.RedrawCalls);
        Assert.Equal(0, dw.GroupCalcCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
        Assert.Null(payload);
    }

    [Fact]
    public void AFullStatePayload_ReachesTheFullStateApply_OnTheDataStoreReceiverArm()
    {
        Harness f = new();
        RecordingStore store = new(DataWindowCarrierFactory.Create(
            CarrierThreadAffinity.MainThread,
            FixedClock.Instance));
        RecordingReceiver ds = new(QueryReceiverKind.DataStore, store);
        Assert.Equal(RetCode.OK, f.Proxy.SetReceiver(ds));

        CarrierState? payload = NewState();
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: true);

        Assert.Equal(0, f.PayloadCodec.ApplyCalls);
        Assert.Empty(ds.RedrawCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    [Fact]
    public void AFullStatePayload_ReachesTheFullStateApply_OnTheHeldCarrierArm()
    {
        Harness f = new();

        CarrierState? payload = NewState();
        long result = f.Proxy.OnDataChunk(ref payload, count: 1L, current: 1L, fullState: true);

        Assert.Equal(0, f.PayloadCodec.ApplyCalls);
        Assert.Equal(DataWindowBufferStore.DataStoreFailure, result);
    }

    [Fact]
    public void ASeamThatAnswersNoCarrier_FailsFastRatherThanDegrading()
    {
        Harness f = new();
        f.Adopter.ReturnsNull = true;

        _ = Assert.Throws<InvalidOperationException>(() => f.Proxy.OnDataMove(
            DataWindowCarrierFactory.Create(
                CarrierThreadAffinity.MainThread,
                FixedClock.Instance)));
    }

    [Fact]
    public void TheHeldCarrier_ComesFromTheAffinityKeyedFactory()
    {
        Harness f = new();

        RecordingStore created = Assert.Single(f.StoreFactory.Created);
        Assert.Same(created, f.Proxy.Data);
        Assert.Equal(CarrierThreadAffinity.MainThread, created.Carrier.Affinity);

        _ = f.Proxy.MoveData();
        Assert.Equal(2, f.StoreFactory.Created.Count);
        Assert.Equal(CarrierThreadAffinity.MainThread, f.StoreFactory.Created[1].Carrier.Affinity);
    }

    // =============================================================================================
    //  THE SUBSTITUTED SEAMS. Every collaborator the proxy takes is a hand-written double nested
    //  here, exactly as the sibling SqlQueryTaskTests does it: no mocking package, no database, no
    //  DataWindow runtime, no network and no real worker thread anywhere in this file (C-H).
    // =============================================================================================

    private sealed class FakeProxyHost : ISqlTaskProxyHost
    {
        private readonly CancellationTokenSource _cts = new();

        internal bool Running { get; set; }

        internal bool ControllerBusy { get; set; }

        internal bool SyncSignalSet { get; set; }

        internal bool Cancelled { get; set; }

        internal SqlTaskBase? WorkerTask { get; set; }

        internal long PrepareAnswer { get; set; }

        internal string? InitClassName { get; private set; }

        internal int SyncRaised { get; private set; }

        internal int SyncCleared { get; private set; }

        public bool IsRunning => Running;

        public bool IsControllerBusy => ControllerBusy;

        public bool IsSyncSignalSet => SyncSignalSet;

        public void RaiseSyncSignal() => SyncRaised++;

        public void ClearSyncSignal() => SyncCleared++;

        public bool IsCancelled => Cancelled;

        public CancellationToken Cancellation => _cts.Token;

        public long LastExitCode => 0L;

        public long LastErrorCode => 0L;

        public string LastErrorInfo => string.Empty;

        public ulong TaskId => 7UL;

        public int TaskIndex => 1;

        public string TaskClassName => "n_cst_thread_task_sqlquery";

        public SqlTaskBase? Task => WorkerTask;

        public long OnInit(string workerClassName)
        {
            InitClassName = workerClassName;
            return RetCode.OK;
        }

        public long OnPrepare() => PrepareAnswer;

        public long Cancel()
        {
            Cancelled = true;
            return RetCode.OK;
        }

        public long SetWorkerDelayFor(double seconds) => RetCode.OK;

        public long SetWorkerSkip(bool skip) => RetCode.OK;
    }

    private sealed class FakeTaskHost : ISqlTaskHost
    {
        private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

        internal ISqlTaskProxy? Tasking { get; set; }

        public bool IsMainThread { get; set; } = true;

        public bool IsCancelled { get; set; }

        public int TaskIndex => 1;

        public long GetTask(int index, out SqlTaskBase? task)
        {
            task = null;
            return RetCode.OK;
        }

        public bool HasData(string name) => _data.ContainsKey(name);

        public object? GetData(string name) => _data.TryGetValue(name, out object? value) ? value : null;

        public long SetData(string name, object? data)
        {
            _data[name] = data;
            return RetCode.OK;
        }

        public ISqlTaskProxy? ParentTasking => Tasking;

        public long OnPrepare() => 0L;

        public void OnUninit()
        {
        }

        public long OnError(long errCode, string errInfo) => RetCode.OK;

        public long OnNotify(long notifyCode, long payload, string text) => RetCode.OK;
    }

    private sealed class FakePooledTransaction : IPooledTransaction
    {
        public long SqlCode => 0L;

        public long SqlDbCode => 0L;

        public long SqlNRows => 0L;

        public string SqlErrText => string.Empty;

        public string SqlReturnData => string.Empty;

        public bool AutoCommit { get; set; }

        public void StampSqlState(in SqlState state)
        {
        }

        public long Connect() => RetCode.OK;

        public long Disconnect() => RetCode.OK;

        public long Rollback() => RetCode.OK;

        public long Commit(bool autoRollback) => RetCode.OK;

        public long Commit() => RetCode.OK;

        public long AutoCommitCheckpoint() => RetCode.OK;

        public long Exec(string? sqlCommand) => RetCode.OK;

        public bool IsConnected() => true;

        // The probe-reporting overload. This double never consults a connection, so it never
        // probes - the flag is false on every path, matching the cache/refusal arms of the oracle.
        public bool IsConnected(out bool probed)
        {
            probed = false;
            return true;
        }

        public bool IsBroken() => false;

        public long SetBroken() => RetCode.OK;

        public void ClearState()
        {
        }

        public DatabaseType GetDbType() => DatabaseType.DbtMssql;

        public long ApplyTransactionData(in TransactionData descriptor) => RetCode.OK;

        public bool IsSqlFailed() => false;

        public bool IsSqlSucceeded() => true;

        public DbErrorData CaptureError() => DbErrorData.Empty;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTransactionActivator : IPooledTransactionActivator
    {
        public IPooledTransaction CreateDefault() => new FakePooledTransaction();

        public IPooledTransaction Create(string className) => new FakePooledTransaction();
    }

    private sealed class FakeQueryTransactionSurface : IQueryTransactionSurface
    {
        public GridSyntaxOutcome GridSyntaxFromSql(IPooledTransaction transaction, string sql) =>
            new(string.Empty, string.Empty);

        public ValueTask<CountQueryOutcome> Query(
            IPooledTransaction transaction,
            string sql,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CountQueryOutcome(RetCode.OK, null, string.Empty));

        public long RaiseBeforeRetrieve(IPooledTransaction transaction, DataWindowCarrier data) =>
            RetCode.OK;

        public void RaiseAfterRetrieve(
            IPooledTransaction transaction,
            DataWindowCarrier data,
            long rowCount)
        {
        }
    }

    /// <summary>An <see cref="ISqlDataStore"/> that also counts its deterministic destruction.</summary>
    private sealed class RecordingStore : ISqlDataStore, IDisposable
    {
        private readonly Dictionary<string, string> _described = new(StringComparer.Ordinal);

        internal RecordingStore(DataWindowCarrier carrier) => Carrier = carrier;

        internal int ClearStateCalls { get; private set; }

        internal int DisposeCalls { get; private set; }

        internal string? CreatedFromSyntax { get; set; }

        public DataWindowCarrier Carrier { get; }

        public string DataObject { get; set; } = "initial";

        internal void SetDescribe(string property, string value) => _described[property] = value;

        public string GetSqlSelect() => string.Empty;

        public string Modify(string modificationScript) => string.Empty;

        public string Describe(string property) =>
            _described.TryGetValue(property, out string? value) ? value : "!";

        public long SetFilter(string? filter) => DataWindowBufferStore.DataStoreSuccess;

        public long SetSort(string? sort) => DataWindowBufferStore.DataStoreSuccess;

        public void ClearState()
        {
            ClearStateCalls++;
            Carrier.ClearState();
        }

        public void OnInit(ICarrierParentTask parentTask) => Carrier.OnInit(parentTask);

        public long Retrieve(IReadOnlyList<object?> parameters) => DataWindowBufferStore.DataStoreSuccess;

        public void Dispose() => DisposeCalls++;
    }

    private sealed class SingleStoreFactory : ISqlDataStoreFactory
    {
        private readonly TimeProvider _clock;

        internal SingleStoreFactory(TimeProvider clock) => _clock = clock;

        internal List<RecordingStore> Created { get; } = [];

        public ISqlDataStore Create(CarrierThreadAffinity affinity)
        {
            RecordingStore store = new(DataWindowCarrierFactory.Create(affinity, _clock));
            Created.Add(store);
            return store;
        }
    }

    private sealed class FakeQueryRuntime : IQueryDataWindowRuntime
    {
        internal List<string> CreatedSyntaxes { get; } = [];

        public CarrierCreateOutcome CreateFromSyntax(ISqlDataStore data, string syntax)
        {
            CreatedSyntaxes.Add(syntax);

            if (data is RecordingStore store)
            {
                store.CreatedFromSyntax = syntax;
            }

            return new CarrierCreateOutcome(DataWindowBufferStore.DataStoreSuccess, string.Empty);
        }

        public bool TryGetChild(ISqlDataStore data, string columnName, out DataWindowBufferStore? child)
        {
            child = null;
            return false;
        }

        public long AttachTransaction(ISqlDataStore data, IPooledTransaction transaction) =>
            DataWindowBufferStore.DataStoreSuccess;
    }

    private sealed class FakeChildSurface : IQueryChildSurface
    {
        private readonly Dictionary<string, string> _described = new(StringComparer.Ordinal);

        internal FakeChildSurface(DataWindowBufferStore store) => Store = store;

        internal int FilterCalls { get; private set; }

        internal int SortCalls { get; private set; }

        public DataWindowBufferStore Store { get; }

        internal void SetDescribe(string property, string value) => _described[property] = value;

        public string Describe(string property) =>
            _described.TryGetValue(property, out string? value) ? value : string.Empty;

        public long Filter()
        {
            FilterCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }

        public long Sort()
        {
            SortCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    private sealed class FakeChildResolver : IQueryChildResolver
    {
        internal FakeChildSurface? Child { get; set; }

        internal bool Resolves { get; set; } = true;

        internal List<ISqlDataStore> Targets { get; } = [];

        internal List<string> Names { get; } = [];

        public bool TryGetChild(ISqlDataStore target, string columnName, out IQueryChildSurface? child)
        {
            Targets.Add(target);
            Names.Add(columnName);

            if (!Resolves)
            {
                child = null;
                return false;
            }

            child = Child;
            return Child is not null;
        }
    }

    private sealed class FakeCarrierAdopter : IQueryCarrierAdopter
    {
        private readonly TimeProvider _clock;

        internal FakeCarrierAdopter(TimeProvider clock) => _clock = clock;

        internal List<DataWindowCarrier> Adopted { get; } = [];

        internal RecordingStore? Produced { get; private set; }

        internal bool ReturnsNull { get; set; }

        public ISqlDataStore Adopt(DataWindowCarrier carrier)
        {
            Adopted.Add(carrier);

            if (ReturnsNull)
            {
                return null!;
            }

            Produced = new RecordingStore(carrier);
            return Produced;
        }
    }

    private sealed class ProgrammablePayloadCodec : IChangesetPayloadCodec
    {
        internal long ApplyAnswer { get; set; } = DataWindowBufferStore.DataStoreSuccess;

        internal int ApplyCalls { get; private set; }

        internal List<DataWindowBufferStore> Targets { get; } = [];

        public long TryEncode(DataWindowBufferStore source, out CarrierState? state)
        {
            state = new CarrierState();
            return DataWindowBufferStore.DataStoreSuccess;
        }

        public long TryApply(DataWindowBufferStore target, CarrierState? state)
        {
            ApplyCalls++;
            Targets.Add(target);
            return ApplyAnswer;
        }
    }

    private sealed class RecordingReceiver : IQueryReceiver
    {
        internal RecordingReceiver(QueryReceiverKind kind, ISqlDataStore store)
        {
            Kind = kind;
            Store = store;
        }

        internal List<bool> RedrawCalls { get; } = [];

        internal int GroupCalcCalls { get; private set; }

        public QueryReceiverKind Kind { get; }

        public ISqlDataStore Store { get; }

        public long SetRedraw(bool enable)
        {
            RedrawCalls.Add(enable);
            return DataWindowBufferStore.DataStoreSuccess;
        }

        public long GroupCalc()
        {
            GroupCalcCalls++;
            return DataWindowBufferStore.DataStoreSuccess;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        internal static readonly FixedClock Instance = new();

        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }


    /// <summary>Builds a fully substituted proxy with an attached real worker.</summary>
    private sealed class Harness
    {
        internal Harness(bool attachWorker = true)
        {
            Options = new PersistenceOptions();
            IOptions<PersistenceOptions> accessor = Microsoft.Extensions.Options.Options.Create(Options);

            Host = new FakeProxyHost();
            StoreFactory = new SingleStoreFactory(FixedClock.Instance);
            Runtime = new FakeQueryRuntime();
            ChildResolver = new FakeChildResolver();
            Adopter = new FakeCarrierAdopter(FixedClock.Instance);
            PayloadCodec = new ProgrammablePayloadCodec();

            if (attachWorker)
            {
                FakeTaskHost taskHost = new();
                TransactionPool pool = new(
                    accessor,
                    FixedClock.Instance,
                    new FakeTransactionActivator());

                Worker = new SqlQueryTask(
                    taskHost,
                    pool,
                    new SingleStoreFactory(FixedClock.Instance),
                    new SqlRetrievalHookActivator(),
                    FixedClock.Instance,
                    NullLogger<SqlQueryTask>.Instance,
                    new FakeQueryTransactionSurface(),
                    Runtime,
                    [new SqlServerPagingRewriter(), new OraclePagingRewriter()],
                    new ChangesetCodec(FixedClock.Instance),
                    SqlRedactor.Instance,
                    accessor);

                Host.WorkerTask = Worker;
            }

            Proxy = new SqlQueryTaskProxy(
                Host,
                NullLogger<SqlQueryTaskProxy>.Instance,
                FixedClock.Instance,
                StoreFactory,
                Runtime,
                ChildResolver,
                Adopter,
                PayloadCodec,
                accessor);

            if (attachWorker)
            {
                InitResult = InvokeOnInit(Proxy);
            }
        }

        internal PersistenceOptions Options { get; }

        internal FakeProxyHost Host { get; }

        internal SingleStoreFactory StoreFactory { get; }

        internal FakeQueryRuntime Runtime { get; }

        internal FakeChildResolver ChildResolver { get; }

        internal FakeCarrierAdopter Adopter { get; }

        internal ProgrammablePayloadCodec PayloadCodec { get; }

        internal SqlQueryTask? Worker { get; }

        internal SqlQueryTaskProxy Proxy { get; }

        internal long InitResult { get; }

        internal RecordingStore HeldStore => (RecordingStore)Proxy.Data;

        internal static long InvokeOnInit(SqlQueryTaskProxy proxy) =>
            (long)typeof(SqlTaskProxyBase)
                .GetMethod(
                    "OnInit",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(proxy, null)!;

        internal TaskNotificationDispatcher Notifications => DispatcherOf(Proxy);

        internal List<(long Wparam, long Lparam, string Text)> CaptureNotifications()
        {
            List<(long, long, string)> seen = [];
            _ = Notifications.On(
                TaskEventName.Notify,
                (source, wparam, lparam, text) =>
                {
                    seen.Add((wparam, lparam, text));
                    return null;
                });
            return seen;
        }

        internal List<(long Wparam, string Text)> CaptureErrors()
        {
            List<(long, string)> seen = [];
            _ = Notifications.On(
                TaskEventName.Error,
                (source, wparam, lparam, text) =>
                {
                    seen.Add((wparam, text));
                    return null;
                });
            return seen;
        }

        internal static TaskNotificationDispatcher DispatcherOf(SqlQueryTaskProxy proxy) =>
            (TaskNotificationDispatcher)typeof(SqlTaskProxyBase)
                .GetProperty(
                    "Notifications",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(proxy)!;

        internal static long InvokeOnPrepare(SqlQueryTaskProxy proxy) =>
            (long)typeof(SqlTaskProxyBase)
                .GetMethod(
                    "OnPrepare",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(proxy, null)!;
    }
}
