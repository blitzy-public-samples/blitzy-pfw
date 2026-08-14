// ==================================================================================================
//  DataWindowEventChainTests - the event-ordering conformance suite for Domain/DataWindowEventChain.cs
//  ------------------------------------------------------------------------------------------------
//  BEHAVIOURAL ORACLE   ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                         :L11-L32    the twenty-two events - nine semantic, thirteen raw pbm_dwn*
//                         :L41-L43    EID_ROWFOCUSCHANGE / EID_ITEMFOCUSCHANGE / EID_ITEMCHANGE
//                         :L47-L76    the twelve topics, two of them ORDERING-PREFIXED
//                         :L115-L400  every raw event body, one at a time
//                         :L403-L414  the Filter override            success is 1, not 0
//                         :L416-L446  the DeleteRow override         seven ordered steps
//                         :L448-L467  the seven of_on / of_off pass-throughs
//                         :L562-L589  creation, initialisation and teardown - THREE DIFFERENT ORDERS
//                         :L591-L615  the nested broker subclass and its onprepare
//                       ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L82-L90
//                         the dropped super-calls - theme register / unregister, constraint C-D
//                       ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544,L596
//                         `.^persistent` - PERSISTENCE's suffix, asserted ABSENT from this chain
//
//  AAP 0.6.1 names event-ordering preservation THE SINGLE HIGHEST RISK in the decomposition, and
//  AAP 0.8.1 requires conformance tests PER WORKFLOW rather than merely per event. This file is that
//  suite. The legacy tree is READ ONLY and is never an edit target (constraint C-C); every assertion
//  below states what the oracle ACTUALLY does, including where that is an oddity a tidy-minded reader
//  would "fix" - an assertion that encoded the tidied behaviour would be asserting a regression.
//
//  SCOPE, AND THE ONE LINE THIS FILE MUST NOT CROSS
//  ------------------------------------------------------------------------------------------------
//  This is FAMILY 1 of the four mandated parity-matrix families: EVENT ORDER. It owns what order the
//  chain does things in, which participants run, and what each event returns.
//
//  It does NOT own, and must not acquire, the other half of AAP 0.6.1's subject matter:
//      * the PER-CAPABILITY-AREA ORDERING-PATTERN ASSIGNMENT of AAP 0.6.1.4 - which areas get
//        pattern (a) a sequencing token and which get pattern (b) a strictly synchronous chain, i.e.
//        every assertion about `DataWindowEventOrdering.DisciplineOf`; and
//      * the OUT-OF-ORDER RULE - that an out-of-order arrival is a hard error under (b) and tolerable
//        under (a), i.e. every assertion about `DataWindowEventSequencer.Accept` and
//        `DataWindowEventSequenceException`.
//  Both live in EventOrderingPatternTests.cs. THE SPLIT IS DELIBERATE AND IS NOT DUPLICATED HERE: two
//  files asserting one fact is two files to change when the oracle is re-read, and the second one
//  silently becomes the stale one. Likewise the queued continuation `ondwnkillfocus` hands to the
//  session is ValidationSessionTests.cs's subject - this file asserts only WHERE in the kill-focus
//  sequence the queueing happens, never what the continuation then does.
//
//  SHAPE. Table-driven parity matrices as theories with member data (AAP 0.6.7), plain xunit
//  assertions, driven through one composed test double so every path is reachable with no live
//  DataWindow, no window handle and no message pump anywhere (constraints C-D and C-H).
//
//  ORDERED, NEVER SET-WISE. Every ordering assertion compares an ORDERED LIST. A set comparison would
//  pass on a reordered chain, which is exactly the regression AAP 0.6.1 calls the highest risk.
//
//  RULES. review_rules returns exactly "No user rules provided.", so NO USER RULE GOVERNS THIS FILE.
//  Per AAP 0.7.1 that is a finding and not latitude: the enterprise baseline of AAP 0.7.2 applies in
//  its place, and the binding constraints are AAP 0.7.3's non-rule set - C-B (every asymmetry below is
//  legacy behaviour, asserted rather than harmonised), C-C (the oracle is read-only and every claim
//  carries its locator), C-H (this file is the primary coverage of the chain) and C-K (every assertion
//  cites the line it came from). Per AAP 0.4.5.3 the SCREAMING_SNAKE topic identifiers are REFERENCED
//  here and none is DECLARED here, because no test file appears in the repository-root .editorconfig
//  suppression bands and this project builds under TreatWarningsAsErrors.
//
//  WHY THE DOUBLE COMPOSES RATHER THAN INHERITS. Domain/DataWindowServiceHost.cs's DECISION 5 makes
//  the chain a DataWindowServiceHost in its own right, so a double for the chain cannot also inherit
//  FakeDataWindowHost - C# has no multiple inheritance. FakeEventChain therefore holds one and
//  forwards the whole host contract to it, which keeps a single implementation of the buffer
//  bookkeeping and a single call log. The two protected *Core operations are reached through the two
//  public bridges added to FakeDataWindowHost for exactly this purpose.
// ==================================================================================================
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using Xunit;

// ALIASED RATHER THAN IMPORTED WHOLESALE. PowerFramework.Contracts.Common.V1 and
// PowerFramework.Contracts.DataServices.V1 each publish a type whose name also exists in
// PowerFramework.DataServices.Domain - the wire half and the in-process half of a matched pair - so an
// unaliased import is CS0104. Naming the three types this file needs keeps every use unambiguous
// without importing either namespace.
//
// OrderingDiscipline is DELIBERATELY NOT ALIASED HERE. It is the vocabulary of the ordering-pattern
// assignment, which EventOrderingPatternTests.cs owns - see THE ONE LINE THIS FILE MUST NOT CROSS
// above. Its absence from this using block is the machine-checkable half of that boundary.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;

namespace PowerFramework.DataServices.Tests;

// ==================================================================================================
//  THE DOUBLES
// ==================================================================================================

/// <summary>
/// One shared, ordered log of every lifecycle and callback moment across all five attached services.
/// </summary>
/// <remarks>
/// A SINGLE log rather than one per service, because the properties under test are ORDERS ACROSS the
/// five - creation order, initialisation order and teardown order - and three separate logs could not
/// express the relation between them. Entries are <c>"&lt;moment&gt;:&lt;kind&gt;"</c> so a failure
/// message reads as the sequence the oracle's line numbers describe.
/// </remarks>
internal sealed class FakeChainServiceLog
{
    private readonly List<string> _entries = [];

    internal IReadOnlyList<string> Entries => _entries;

    internal void Record(string moment, DataWindowAttachedServiceKind kind) =>
        _entries.Add(moment + ":" + kind);

    /// <summary>
    /// Appends a free-form entry, so a broker subscriber and the chain's own semantic event can be
    /// interleaved into the SAME sequence as the five services.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <remarks>
    /// This is what makes <c>ondoitemchanged</c>'s three-step order assertable as one list: step one is
    /// the column-expression service, step two is the broker and step three is the semantic event, and
    /// they are three different participants [<c>se_cst_dw.sru:L313-L319</c>].
    /// </remarks>
    internal void Record(string entry) => _entries.Add(entry);

    internal void Clear() => _entries.Clear();

    /// <summary>
    /// The service kinds recorded under one moment, in the order they were recorded.
    /// </summary>
    /// <param name="moment">The moment, for example <c>"Create"</c>.</param>
    /// <returns>The kinds, in recorded order.</returns>
    internal IReadOnlyList<DataWindowAttachedServiceKind> Sequence(string moment)
    {
        string prefix = moment + ":";
        List<DataWindowAttachedServiceKind> kinds = [];

        foreach (string entry in _entries)
        {
            if (entry.StartsWith(prefix, StringComparison.Ordinal))
            {
                kinds.Add(Enum.Parse<DataWindowAttachedServiceKind>(entry[prefix.Length..]));
            }
        }

        return kinds;
    }

    /// <summary>The zero-based position of one entry, or <c>-1</c>.</summary>
    /// <param name="entry">The entry, for example <c>"Dispose:ContextMenu"</c>.</param>
    /// <returns>The position, or <c>-1</c> when absent.</returns>
    internal int IndexOf(string entry)
    {
        for (int index = 0; index < _entries.Count; index++)
        {
            if (string.Equals(_entries[index], entry, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

/// <summary>
/// The shared behaviour of all five attached-service doubles: it records its own construction, its
/// initialisation and its disposal into the shared log.
/// </summary>
/// <remarks>
/// <para>
/// Construction is recorded IN THE CONSTRUCTOR, because construction is the moment the oracle's
/// <c>Create</c> statements at <c>se_cst_dw.sru:L570-L574</c> occur - recording it in the factory
/// instead would measure the factory's call order rather than the chain's.
/// </para>
/// <para>
/// <see cref="IDisposable"/> IS IMPLEMENTED HERE THOUGH THE INTERFACE DOES NOT REQUIRE IT. The chain's
/// teardown disposes only a service that happens to be disposable, so implementing it is what makes
/// the teardown ORDER observable at all - and the interface deliberately does not demand it, so that a
/// production service with nothing to release is not forced into the pattern.
/// </para>
/// </remarks>
internal abstract class FakeAttachedService : IDataWindowAttachedService, IDisposable
{
    protected FakeAttachedService(FakeChainServiceLog log, DataWindowAttachedServiceKind kind)
    {
        Log = log;
        Kind = kind;
        Log.Record("Create", kind);
    }

    internal FakeChainServiceLog Log { get; }

    internal DataWindowAttachedServiceKind Kind { get; }

    /// <summary>The host handed to <see cref="OnInit"/>, or <see langword="null"/>.</summary>
    internal DataWindowServiceHost? InitializedWith { get; private set; }

    /// <summary>How many times <see cref="Dispose"/> ran.</summary>
    internal int DisposeCount { get; private set; }

    /// <inheritdoc/>
    /// <remarks>Settable here so a test can drive both arms of every <c>#Enabled</c> gate.</remarks>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc/>
    public void OnInit(DataWindowServiceHost dw)
    {
        InitializedWith = dw;
        Log.Record("OnInit", Kind);
    }

    /// <summary>Runs at the START of <see cref="Dispose"/>, before the entry is recorded.</summary>
    /// <remarks>
    /// This is what makes "unsubscribe BEFORE destroy" [<c>:L583</c> then <c>:L584-L588</c>] assertable:
    /// a hook on the FIRST destroy can observe whether the broker's registry has already been cleared.
    /// </remarks>
    internal Action? OnDisposing { get; set; }

    /// <summary>The arguments the last broker probe received, or <see langword="null"/>.</summary>
    internal object?[]? LastThreeSlots { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        OnDisposing?.Invoke();
        DisposeCount++;
        Log.Record("Dispose", Kind);
    }

    /// <summary>
    /// A three-parameter broker handler, so an attached service can BE a subscriber - which is what the
    /// nested broker's identity and ancestry exclusions exist to special-case.
    /// </summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    public long OnBrokerProbe(object? slot1, object? slot2, object? slot3)
    {
        LastThreeSlots = [slot1, slot2, slot3];
        Log.Record("Probe", Kind);
        return RetCode.OK;
    }
}

/// <summary>The context-menu double [<c>se_cst_dw.sru:L80</c>].</summary>
internal sealed class FakeContextMenuService : FakeAttachedService, IDataWindowContextMenuService
{
    internal FakeContextMenuService(FakeChainServiceLog log)
        : base(log, DataWindowAttachedServiceKind.ContextMenu)
    {
    }
}

/// <summary>The row-selection double [<c>se_cst_dw.sru:L81</c>].</summary>
internal sealed class FakeRowSelectService : FakeAttachedService, IDataWindowRowSelectService
{
    internal FakeRowSelectService(FakeChainServiceLog log)
        : base(log, DataWindowAttachedServiceKind.RowSelect)
    {
    }

    /// <summary>How many times the filtered notification arrived [<c>se_cst_dw.sru:L409</c>].</summary>
    internal int FilteredCount { get; private set; }

    /// <inheritdoc/>
    public void OnFiltered()
    {
        FilteredCount++;
        Log.Record("OnFiltered", Kind);
    }
}

/// <summary>The column-sort double [<c>se_cst_dw.sru:L82</c>].</summary>
internal sealed class FakeColumnSortService : FakeAttachedService, IDataWindowColumnSortService
{
    internal FakeColumnSortService(FakeChainServiceLog log)
        : base(log, DataWindowAttachedServiceKind.ColumnSort)
    {
    }
}

/// <summary>The drop-down search double [<c>se_cst_dw.sru:L83</c>].</summary>
internal sealed class FakeDropDownSearchService
    : FakeAttachedService, IDataWindowDropDownSearchService
{
    private readonly List<(long Row, IDataWindowObject Dwo, string Data)> _editChanged = [];

    internal FakeDropDownSearchService(FakeChainServiceLog log)
        : base(log, DataWindowAttachedServiceKind.DropDownSearch)
    {
    }

    /// <summary>
    /// Every edit-changed forward, with its arguments [<c>se_cst_dw.sru:L169-L171</c>].
    /// </summary>
    internal IReadOnlyList<(long Row, IDataWindowObject Dwo, string Data)> EditChangedCalls =>
        _editChanged;

    /// <inheritdoc/>
    public void OnEditChanged(long row, IDataWindowObject dwo, string data)
    {
        _editChanged.Add((row, dwo, data));
        Log.Record("OnEditChanged", Kind);
    }
}

/// <summary>The column-expression double [<c>se_cst_dw.sru:L84</c>].</summary>
internal sealed class FakeColumnExpressionService
    : FakeAttachedService, IDataWindowColumnExpressionService
{
    private readonly List<(long Row, IDataWindowObject Dwo)> _itemChanged = [];

    internal FakeColumnExpressionService(FakeChainServiceLog log)
        : base(log, DataWindowAttachedServiceKind.ColumnExp)
    {
    }

    /// <summary>
    /// Every item-changed forward [<c>se_cst_dw.sru:L313-L315</c>] - the FIRST of the three steps
    /// <c>ondoitemchanged</c> performs in strict order.
    /// </summary>
    internal IReadOnlyList<(long Row, IDataWindowObject Dwo)> ItemChangedCalls => _itemChanged;

    /// <inheritdoc/>
    public void OnItemChanged(long row, IDataWindowObject dwo)
    {
        _itemChanged.Add((row, dwo));
        Log.Record("OnItemChanged", Kind);
    }
}

/// <summary>
/// The factory double. It creates each service on demand and remembers it, so a test can reach the
/// instance the chain is actually holding rather than a look-alike.
/// </summary>
/// <remarks>
/// <see cref="ReturnNullFor"/> exists to drive the chain's fail-fast wiring check: the oracle's
/// <c>Create</c> cannot yield nothing, so a null is a structural fault and the chain throws rather
/// than degrading (AAP 0.1.4).
/// </remarks>
internal sealed class FakeAttachedServiceFactory : IDataWindowAttachedServiceFactory
{
    // DECLARED AS THE INTERFACES THE FACTORY RETURNS, NOT AS THE CONCRETE DOUBLES. The doubles are
    // disposable so that teardown ORDER is observable, but THE CHAIN OWNS THEIR LIFETIME - it destroys
    // them at :L584-L588 and a second disposal would corrupt the DisposeCount assertions. Holding them
    // behind their non-disposable interfaces states that ownership in the field types, and keeps this
    // factory correctly NOT disposable.
    private IDataWindowContextMenuService? _contextMenu;
    private IDataWindowRowSelectService? _rowSelect;
    private IDataWindowColumnSortService? _columnSort;
    private IDataWindowDropDownSearchService? _dropDownSearch;
    private IDataWindowColumnExpressionService? _columnExp;

    internal FakeAttachedServiceFactory(FakeChainServiceLog? log = null) => Log = log ?? new();

    internal FakeChainServiceLog Log { get; }

    /// <summary>The one kind whose <c>Create</c> answers null, or <see langword="null"/> for none.</summary>
    internal DataWindowAttachedServiceKind? ReturnNullFor { get; set; }

    internal FakeContextMenuService ContextMenu => Created<FakeContextMenuService>(_contextMenu);

    internal FakeRowSelectService RowSelect => Created<FakeRowSelectService>(_rowSelect);

    internal FakeColumnSortService ColumnSort => Created<FakeColumnSortService>(_columnSort);

    internal FakeDropDownSearchService DropDownSearch => Created<FakeDropDownSearchService>(_dropDownSearch);

    internal FakeColumnExpressionService ColumnExp => Created<FakeColumnExpressionService>(_columnExp);

    /// <inheritdoc/>
    public IDataWindowContextMenuService CreateContextMenu()
    {
        if (ReturnNullFor == DataWindowAttachedServiceKind.ContextMenu)
        {
            return null!;
        }

        _contextMenu = new FakeContextMenuService(Log);
        return _contextMenu;
    }

    /// <inheritdoc/>
    public IDataWindowRowSelectService CreateRowSelect()
    {
        if (ReturnNullFor == DataWindowAttachedServiceKind.RowSelect)
        {
            return null!;
        }

        _rowSelect = new FakeRowSelectService(Log);
        return _rowSelect;
    }

    /// <inheritdoc/>
    public IDataWindowColumnSortService CreateColumnSort()
    {
        if (ReturnNullFor == DataWindowAttachedServiceKind.ColumnSort)
        {
            return null!;
        }

        _columnSort = new FakeColumnSortService(Log);
        return _columnSort;
    }

    /// <inheritdoc/>
    public IDataWindowDropDownSearchService CreateDropDownSearch()
    {
        if (ReturnNullFor == DataWindowAttachedServiceKind.DropDownSearch)
        {
            return null!;
        }

        _dropDownSearch = new FakeDropDownSearchService(Log);
        return _dropDownSearch;
    }

    /// <inheritdoc/>
    public IDataWindowColumnExpressionService CreateColumnExp()
    {
        if (ReturnNullFor == DataWindowAttachedServiceKind.ColumnExp)
        {
            return null!;
        }

        _columnExp = new FakeColumnExpressionService(Log);
        return _columnExp;
    }

    private static T Created<T>(object? instance)
        where T : class =>
        instance as T ?? throw new InvalidOperationException(
            "The chain has not created this service yet; read it after constructing the chain.");
}

/// <summary>
/// Records every <see cref="DataWindowEventOutcome"/> the chain pushes, in dispatch order.
/// </summary>
/// <remarks>
/// The chain PUSHES its outcome to an observer rather than storing a last-outcome slot, because
/// <c>se_cst_dw.sru:L207</c> fires an event from inside another event and a single slot would be
/// overwritten by the inner dispatch before the outer one could be read. This recorder is therefore
/// the only complete view of a nested run, which is exactly what the ordering assertions need.
/// </remarks>
internal sealed class RecordingEventObserver : IDataWindowEventObserver
{
    private readonly List<DataWindowEventOutcome> _outcomes = [];

    internal IReadOnlyList<DataWindowEventOutcome> Outcomes => _outcomes;

    internal DataWindowEventOutcome Last =>
        _outcomes.Count > 0
            ? _outcomes[^1]
            : throw new InvalidOperationException("No event was dispatched.");

    internal IReadOnlyList<EventId> EventIds => [.. _outcomes.Select(outcome => outcome.EventId)];

    internal DataWindowEventOutcome Single(EventId eventId) =>
        _outcomes.Single(outcome => outcome.EventId == eventId);

    internal void Clear() => _outcomes.Clear();

    /// <inheritdoc/>
    public void OnEventDispatched(DataWindowEventOutcome outcome) => _outcomes.Add(outcome);
}

/// <summary>
/// A concrete <see cref="DataWindowEventChain"/> for testing: it supplies the abstract host contract by
/// forwarding every member to a composed <see cref="FakeDataWindowHost"/>, and adds nothing else.
/// </summary>
/// <remarks>
/// <para>
/// EVERY MEMBER BELOW IS A PURE FORWARD, DELIBERATELY. The subject under test is the chain's ordering
/// and delegation, so the double must add no behaviour of its own - any decision taken here would be a
/// decision the assertions then measure instead of the chain's. The eleven semantic events are
/// forwarded too, because the chain invokes them on ITSELF: <c>Event RButtonDown(...)</c> at
/// <c>se_cst_dw.sru:L115</c> is a call on the control, so the override has to reach the fake host for
/// its handler seam and its call log to be reachable at all.
/// </para>
/// <para>
/// <c>OnDoItemChange</c> and <c>OnDoItemChanged</c> are POINTEDLY NOT forwarded. The chain overrides
/// both itself - they are two of the nine semantic events, at <c>:L256-L293</c> and <c>:L295-L320</c> -
/// and overriding them here would replace the very behaviour under test. Their inner semantic events,
/// <c>ItemChanged</c> and <c>OnItemChanged</c>, are what the fake host observes instead.
/// </para>
/// <para>
/// The host is built INSIDE the constructor so it can be handed the chain's own broker: one registry
/// then serves both halves of the double, and a subscription made through the chain is visible to
/// anything that reads the host's broker. Column and row setup happens after construction, which is
/// safe because the chain's constructor reads neither.
/// </para>
/// </remarks>
internal sealed class FakeEventChain : DataWindowEventChain
{
    /// <summary>Builds the double over a session and a service factory.</summary>
    /// <param name="session">The validation session the chain holds for its whole life.</param>
    /// <param name="services">The factory the chain creates its five attached services from.</param>
    /// <param name="observer">The dispatch observer, or <see langword="null"/> for none.</param>
    /// <remarks>
    /// THE BASE'S FOURTH PARAMETER - ITS SEQUENCER - IS DELIBERATELY NOT SURFACED. Supplying one is how
    /// a test would drive the sequencer's ACCEPT rule, and that rule is EventOrderingPatternTests.cs's
    /// subject (see THE ONE LINE THIS FILE MUST NOT CROSS in the file header). Leaving the base to build
    /// its own keeps this double unable to reach into the other file's territory even by accident, while
    /// still issuing the entry tokens whose ORDER this file does read.
    /// </remarks>
    internal FakeEventChain(
        ValidationSession session,
        FakeAttachedServiceFactory services,
        IDataWindowEventObserver? observer = null)
        : base(session, services, observer)
    {
        Services = services;
        Host = new FakeDataWindowHost(Eventful);
    }

    /// <summary>The composed host every forwarded member reaches.</summary>
    internal FakeDataWindowHost Host { get; }

    /// <summary>The factory, so a test can reach the five instances the chain is holding.</summary>
    internal FakeAttachedServiceFactory Services { get; }

    // ---------------------------------------------------------------------------------------------
    //  THE HOST CONTRACT - thirty-one abstract members plus the object model, all forwarded
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override object? ObjectModel => Host.ObjectModel;

    /// <inheritdoc/>
    public override IDataWindowObject GetObjectAttribute(string dwoName) =>
        Host.GetObjectAttribute(dwoName);

    /// <inheritdoc/>
    public override string Describe(string property) => Host.Describe(property);

    /// <inheritdoc/>
    public override long RowCount() => Host.RowCount();

    /// <inheritdoc/>
    public override long GetRow() => Host.GetRow();

    /// <inheritdoc/>
    public override int SetRow(long row) => Host.SetRow(row);

    /// <inheritdoc/>
    public override string GetColumnName() => Host.GetColumnName();

    /// <inheritdoc/>
    public override int AcceptText() => Host.AcceptText();

    /// <inheritdoc/>
    public override int SetRedraw(bool enable) => Host.SetRedraw(enable);

    /// <inheritdoc/>
    public override int SelectRow(long row, bool select) => Host.SelectRow(row, select);

    /// <inheritdoc/>
    public override bool IsSelected(long row) => Host.IsSelected(row);

    /// <inheritdoc/>
    public override long GetSelectedRow(long startRow) => Host.GetSelectedRow(startRow);

    /// <inheritdoc/>
    public override object? GetFocusedObject() => Host.GetFocusedObject();

    /// <inheritdoc/>
    public override int SetFocus() => Host.SetFocus();

    /// <inheritdoc/>
    public override ItemStatus GetItemStatus(long row, long columnId, DwBuffer buffer) =>
        Host.GetItemStatus(row, columnId, buffer);

    /// <inheritdoc/>
    public override int SetItemStatus(long row, long columnId, DwBuffer buffer, ItemStatus status) =>
        Host.SetItemStatus(row, columnId, buffer, status);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, string? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, decimal? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, long? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateTime? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, DateOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, TimeOnly? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override int SetItem(long row, long columnId, object? value) =>
        Host.SetItem(row, columnId, value);

    /// <inheritdoc/>
    public override string? GetItemString(long row, string column) =>
        Host.GetItemString(row, column);

    /// <inheritdoc/>
    public override decimal? GetItemDecimal(long row, string column) =>
        Host.GetItemDecimal(row, column);

    /// <inheritdoc/>
    public override double? GetItemNumber(long row, string column) =>
        Host.GetItemNumber(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, string? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, decimal? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, long? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override DateTime? GetItemDateTime(long row, string column) =>
        Host.GetItemDateTime(row, column);

    /// <inheritdoc/>
    public override DateOnly? GetItemDate(long row, string column) => Host.GetItemDate(row, column);

    /// <inheritdoc/>
    public override TimeOnly? GetItemTime(long row, string column) => Host.GetItemTime(row, column);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateTime? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, DateOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override int SetItem(long row, string column, TimeOnly? value) =>
        Host.SetItem(row, column, value);

    /// <inheritdoc/>
    public override long Find(string expression, long start, long end) =>
        Host.Find(expression, start, end);

    /// <inheritdoc/>
    public override long InsertRow(long row) => Host.InsertRow(row);

    /// <inheritdoc/>
    public override string GetValue(string column, long index) => Host.GetValue(column, index);

    /// <inheritdoc/>
    public override int GetChild(string column, ref IDataWindowChild? child) =>
        Host.GetChild(column, ref child);

    // ---------------------------------------------------------------------------------------------
    //  THE HOST MEMBERS WHOSE BACKING IS NOT THE FAKE HOST
    //  -------------------------------------------------------------------------------------------
    //  Every forward above reaches Host, because Host is where the value lives. The three GATE
    //  members are the exception, and the exception is the whole point of them: the DISABLED-EVENT
    //  MASK IS SESSION STATE, one of the four cross-event fields at se_cst_dw.sru:L89-L96, so the
    //  chain reads it through _session on every gated event [Domain/DataWindowEventChain.cs, the
    //  EID_ROWFOCUSCHANGE, EID_ITEMFOCUSCHANGE and EID_ITEMCHANGE guards]. Routing these three to
    //  Host instead would give the double TWO masks that disagree - a disable taken through the
    //  host-shaped member would be invisible to the gating the chain actually performs - which is
    //  exactly the divergence TheGateDelegationsRouteToTheSessionAndAreObservableInDispatch
    //  measures when it asserts the chain's answer and the SESSION's answer together.
    //
    //  THE CHAIN ALREADY DECLARES THE SAME THREE OPERATIONS TAKING `in uint`, delegating to the same
    //  session. Those do not satisfy these abstract members, because a ref-kind difference is not an
    //  override, so both spellings exist and BOTH MUST ANSWER FROM ONE PLACE. They do.
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt) => Session.IsEventDisabled(evt);

    /// <inheritdoc/>
    public override long DisableEvent(uint evt) => Session.DisableEvent(evt);

    /// <inheritdoc/>
    public override int EnableEvent(uint evt) => Session.EnableEvent(evt);

    /// <inheritdoc/>
    public override long GetRowIDFromRow(long row) => Host.GetRowIDFromRow(row);

    /// <inheritdoc/>
    public override long GetRowFromRowID(long rowId) => Host.GetRowFromRowID(rowId);

    /// <inheritdoc/>
    public override int SetSort(string sort) => Host.SetSort(sort);

    /// <inheritdoc/>
    public override int Sort() => Host.Sort();

    /// <inheritdoc/>
    public override int GroupCalc() => Host.GroupCalc();

    /// <inheritdoc/>
    /// <remarks>
    /// Through the public bridge, because the operation is protected on the fake host and a composing
    /// double cannot reach a protected member of an instance it merely holds.
    /// </remarks>
    protected override int FilterCore() => Host.InvokeFilterCore();

    /// <inheritdoc/>
    /// <remarks>Through the public bridge, for the reason given on <see cref="FilterCore"/>.</remarks>
    protected override int DeleteRowCore(long row) => Host.InvokeDeleteRowCore(row);

    // ---------------------------------------------------------------------------------------------
    //  THE ELEVEN SEMANTIC EVENTS THE RAW CHAIN DELEGATES TO
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override long RButtonDown(long xpos, long ypos, long row, IDataWindowObject dwo) =>
        Host.RButtonDown(xpos, ypos, row, dwo);

    /// <inheritdoc/>
    public override long RowFocusChanged(long currentRow) => Host.RowFocusChanged(currentRow);

    /// <inheritdoc/>
    public override long RowFocusChanging(long currentRow, long newRow) =>
        Host.RowFocusChanging(currentRow, newRow);

    /// <inheritdoc/>
    public override long DoubleClicked(long xpos, long ypos, long row, IDataWindowObject dwo) =>
        Host.DoubleClicked(xpos, ypos, row, dwo);

    /// <inheritdoc/>
    public override long Clicked(long xpos, long ypos, long row, IDataWindowObject dwo) =>
        Host.Clicked(xpos, ypos, row, dwo);

    /// <inheritdoc/>
    public override long EditChanged(long row, IDataWindowObject dwo, string data) =>
        Host.EditChanged(row, dwo, data);

    /// <inheritdoc/>
    public override long ItemFocusChanged(long row, IDataWindowObject dwo) =>
        Host.ItemFocusChanged(row, dwo);

    /// <inheritdoc/>
    public override long ItemChanged(long row, IDataWindowObject dwo, string? data) =>
        Host.ItemChanged(row, dwo, data);

    /// <inheritdoc/>
    public override long? ItemError(long row, IDataWindowObject dwo, string data) =>
        Host.ItemError(row, dwo, data);

    /// <inheritdoc/>
    public override long LoseFocus() => Host.LoseFocus();

    /// <inheritdoc/>
    public override long GetFocus() => Host.GetFocus();

    /// <summary>
    /// Records STEP THREE of <c>ondoitemchanged</c> into the shared service log, then runs the base.
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    /// <remarks>
    /// THE ONE PLACE THIS DOUBLE ADDS AN OBSERVATION RATHER THAN A FORWARD, and it is the minimum that
    /// makes the strict order at <c>se_cst_dw.sru:L313-L319</c> assertable: steps one, two and three are
    /// three DIFFERENT participants - the column-expression service, the broker and this semantic event
    /// - so their relative order is only visible if all three write into one sequence. It calls base,
    /// so the oracle's empty body still runs and nothing about the default is bypassed.
    /// </remarks>
    public override void OnItemChanged(long row, IDataWindowObject dwo)
    {
        Services.Log.Record("Semantic:OnItemChanged");
        base.OnItemChanged(row, dwo);
    }

    /// <summary>The arguments the chain's own broker probe received, or <see langword="null"/>.</summary>
    internal object?[]? ProbeSlots { get; private set; }

    /// <summary>
    /// When set, the drop-down search filter this double produces - and reports.
    /// </summary>
    /// <remarks>
    /// THIS IS THE DOCUMENTED EXTENSION POINT, EXERCISED. Seven of the nine semantic events are outbound
    /// questions whose default bodies report nothing, because an unhandled event performed no dispatch.
    /// An override that PRODUCES a result issues its own token and reports its own outcome through the
    /// two protected primitives - which is the only way
    /// <see cref="DataWindowEventOutcome.ProducedFilter"/> and
    /// <see cref="DataWindowEventOutcome.AnyResult"/> are ever populated. The C-03 and C-04 stream
    /// adapters in Grpc/ take exactly this shape.
    /// </remarks>
    internal Func<string>? ProducedFilterFactory { get; set; }

    /// <summary>When set, the macro result this double produces - and reports.</summary>
    internal Func<object?>? MacroResultFactory { get; set; }

    /// <inheritdoc/>
    public override void OnDdsGetFilter(long row, IDataWindowObject dwo, string data, ref string filter)
    {
        if (ProducedFilterFactory is null)
        {
            base.OnDdsGetFilter(row, dwo, data, ref filter);
            return;
        }

        filter = ProducedFilterFactory();

        Report(NewOutcome(EventId.Onddsgetfilter) with
        {
            ProducedFilter = filter,
            Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
        });
    }

    /// <inheritdoc/>
    public override object? OnColumnExpInvokeMethod(
        long row,
        IDataWindowObject dwo,
        string name,
        string[] args)
    {
        if (MacroResultFactory is null)
        {
            return base.OnColumnExpInvokeMethod(row, dwo, name, args);
        }

        object? result = MacroResultFactory();

        Report(NewOutcome(EventId.Oncolumnexpinvokemethod) with
        {
            AnyResult = result,
            Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
        });

        return result;
    }

    /// <summary>
    /// A three-parameter broker handler ON THE CHAIN ITSELF, so the nested broker's
    /// <c>target = parent</c> exclusion [<c>se_cst_dw.sru:L600</c>] can be driven: the host does not
    /// need to be told which host raised the event.
    /// </summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    public long OnBrokerProbe(object? slot1, object? slot2, object? slot3)
    {
        ProbeSlots = [slot1, slot2, slot3];
        Services.Log.Record("Probe:Parent");
        return RetCode.OK;
    }
}

/// <summary>
/// A broker subscriber that records its dispatches and reports every argument it received.
/// </summary>
/// <remarks>
/// The handler arities are what make the nested broker's argument injection observable. The broker
/// resolves a handler by member name and passes <c>Min(declared - consumed, payload)</c> arguments from
/// the slot after the last one the prepare hook consumed, so a three-parameter handler receiving the
/// chain in slot one is direct evidence that <c>invoker.SetArg(1,parent)</c> and <c>argPassed = 1</c>
/// both happened [<c>se_cst_dw.sru:L604-L605</c>].
/// </remarks>
internal sealed class RecordingSubscriber
{
    private readonly List<string> _dispatches = [];

    internal RecordingSubscriber(string label) => Label = label;

    internal string Label { get; }

    internal IReadOnlyList<string> Dispatches => _dispatches;

    internal int Count => _dispatches.Count;

    /// <summary>The arguments the last three-slot dispatch received, or <see langword="null"/>.</summary>
    internal object?[]? LastThreeSlots { get; private set; }

    /// <summary>A veto to answer with, applied to every arity.</summary>
    internal long Answer { get; set; } = RetCode.OK;

    /// <summary>Runs after the dispatch is recorded and before <see cref="Answer"/> is returned.</summary>
    internal Action? InHandler { get; set; }

    /// <summary>A handler declaring no parameters at all - nothing for the hook to inject into.</summary>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnNoArguments()
    {
        _dispatches.Add(Label + ":0");
        InHandler?.Invoke();
        return Answer;
    }

    /// <summary>A handler declaring one parameter - exactly the injected slot and nothing more.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnOneArgument(object? slot1)
    {
        _dispatches.Add(Label + ":1");
        LastThreeSlots = [slot1, null, null];
        InHandler?.Invoke();
        return Answer;
    }

    /// <summary>A handler declaring three parameters.</summary>
    /// <param name="slot1">Slot one - the injected source, when the hook injects.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnThreeArguments(object? slot1, object? slot2, object? slot3)
    {
        _dispatches.Add(Label + ":3");
        LastThreeSlots = [slot1, slot2, slot3];
        InHandler?.Invoke();
        return Answer;
    }
}

/// <summary>
/// A subscriber that is also a <see cref="DataWindowServiceBase"/> descendant, so the nested broker's
/// ancestry exclusion [<c>se_cst_dw.sru:L603</c>] can be driven WITHOUT also matching either of the two
/// identity exclusions at <c>:L601</c> and <c>:L602</c>.
/// </summary>
/// <remarks>
/// This is what proves those two identity checks are separately reachable rather than dead code. The
/// five attached-service doubles above do NOT derive from <see cref="DataWindowServiceBase"/>, so a
/// dispatch to one of them is excluded by identity alone, and a dispatch to this type is excluded by
/// ancestry alone. Preserving both is constraint C-B: the identity comparison and the ancestry walk are
/// different operations, and the oracle performs both.
/// </remarks>
internal sealed class RecordingServiceSubscriber : DataWindowServiceBase
{
    private readonly List<string> _dispatches = [];

    internal IReadOnlyList<string> Dispatches => _dispatches;

    internal object?[]? LastThreeSlots { get; private set; }

    /// <summary>A three-parameter handler, matching <see cref="RecordingSubscriber"/>'s shape.</summary>
    /// <param name="slot1">Slot one.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="RetCode.OK"/>.</returns>
    public long OnThreeArguments(object? slot1, object? slot2, object? slot3)
    {
        _dispatches.Add("service:3");
        LastThreeSlots = [slot1, slot2, slot3];
        return RetCode.OK;
    }
}


// ==================================================================================================
//  THE SUITE
// ==================================================================================================

/// <summary>
/// The event-ordering conformance suite for
/// <see cref="DataWindowEventChain"/> - the port of
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>.
/// </summary>
public sealed class DataWindowEventChainTests
{
    private const string ColumnName = "name";
    private const string ColumnType = "char(50)";
    private const long Row = 1L;

    /// <summary>
    /// The twenty-two events, paired with the C# member each one becomes.
    /// </summary>
    /// <remarks>
    /// <b>THIS TABLE IS THE ONLY GUARD ON THE CORRESPONDENCE, AND THAT IS A MEASURED FACT.</b>
    /// <c>PowerFramework.Contracts.csproj</c> declares ZERO <c>ProjectReference</c> by design - it is a
    /// boundary definition and not a shared-code back door (constraint C-A) - so no compiler anywhere
    /// checks that the chain's members and contract C-03's <c>EventChain</c> event set agree. If the
    /// proto gains, loses or renames an event, this table is what fails.
    /// </remarks>
    private static readonly (EventId Id, string Member)[] EventMemberTable =
    [
            // The nine semantic events, :L11-L14, :L24-L26, :L28, :L32.
        (EventId.Oninitcontextmenu, nameof(DataWindowEventChain.OnInitContextMenu)),
        (EventId.Oncontextmenu, nameof(DataWindowEventChain.OnContextMenu)),
        (EventId.Onddsgetfilter, nameof(DataWindowEventChain.OnDdsGetFilter)),
        (EventId.Oncolumnexpinvokemethod, nameof(DataWindowEventChain.OnColumnExpInvokeMethod)),
        (EventId.Ondoitemchange, nameof(DataWindowEventChain.OnDoItemChange)),
        (EventId.Onitemchanged, nameof(DataWindowEventChain.OnItemChanged)),
        (EventId.Ondoitemchanged, nameof(DataWindowEventChain.OnDoItemChanged)),
        (EventId.Onddsfiltered, nameof(DataWindowEventChain.OnDdsFiltered)),
        (EventId.Oncolumnexptrace, nameof(DataWindowEventChain.OnColumnExpTrace)),
            // The thirteen raw pbm_dwn* events, in declaration order, :L15-L23, :L27, :L29-L31.
        (EventId.Ondwnrbuttondown, nameof(DataWindowEventChain.OnDwnRButtonDown)),
        (EventId.Ondwnrbuttonup, nameof(DataWindowEventChain.OnDwnRButtonUp)),
        (EventId.Ondwnrowchange, nameof(DataWindowEventChain.OnDwnRowChange)),
        (EventId.Ondwnrowchanging, nameof(DataWindowEventChain.OnDwnRowChanging)),
        (EventId.Ondwnlbuttondblclk, nameof(DataWindowEventChain.OnDwnLButtonDblClk)),
        (EventId.Ondwnlbuttonclk, nameof(DataWindowEventChain.OnDwnLButtonClk)),
        (EventId.Ondwnchanging, nameof(DataWindowEventChain.OnDwnChanging)),
        (EventId.Ondwnitemchangefocus, nameof(DataWindowEventChain.OnDwnItemChangeFocus)),
        (EventId.Ondwnitemchange, nameof(DataWindowEventChain.OnDwnItemChange)),
        (EventId.Ondwnitemvalidationerror, nameof(DataWindowEventChain.OnDwnItemValidationError)),
        (EventId.Ondwnkillfocus, nameof(DataWindowEventChain.OnDwnKillFocus)),
        (EventId.Ondwnlbuttonup, nameof(DataWindowEventChain.OnDwnLButtonUp)),
        (EventId.Ondwnsetfocus, nameof(DataWindowEventChain.OnDwnSetFocus))
    ];

    /// <summary>
    /// The THIRTEEN raw <c>pbm_dwn*</c> events in the oracle's own DECLARATION ORDER, with the locator
    /// of each declaration.
    /// </summary>
    private static readonly string[] RawEventOrderTable =
    [
        nameof(DataWindowEventChain.OnDwnRButtonDown),        // :L15  pbm_dwnrbuttondown
        nameof(DataWindowEventChain.OnDwnRButtonUp),          // :L16  pbm_dwnrbuttonup
        nameof(DataWindowEventChain.OnDwnRowChange),          // :L17  pbm_dwnrowchange
        nameof(DataWindowEventChain.OnDwnRowChanging),        // :L18  pbm_dwnrowchanging
        nameof(DataWindowEventChain.OnDwnLButtonDblClk),      // :L19  pbm_dwnlbuttondblclk
        nameof(DataWindowEventChain.OnDwnLButtonClk),         // :L20  pbm_dwnlbuttonclk
        nameof(DataWindowEventChain.OnDwnChanging),           // :L21  pbm_dwnchanging
        nameof(DataWindowEventChain.OnDwnItemChangeFocus),    // :L22  pbm_dwnitemchangefocus
        nameof(DataWindowEventChain.OnDwnItemChange),         // :L23  pbm_dwnitemchange
        nameof(DataWindowEventChain.OnDwnItemValidationError), // :L27 pbm_dwnitemvalidationerror
        nameof(DataWindowEventChain.OnDwnKillFocus),          // :L29  pbm_dwnkillfocus
        nameof(DataWindowEventChain.OnDwnLButtonUp),          // :L30  pbm_dwnlbuttonup
        nameof(DataWindowEventChain.OnDwnSetFocus)            // :L31  pbm_dwnsetfocus
    ];

    /// <summary>The twelve topics, paired with their BYTE-EXACT legacy values [<c>:L47-L76</c>].</summary>
    private static readonly (string Constant, string Value)[] TopicValueTable =
    [
        (nameof(DataWindowEventChain.EVT_ROWFOCUSCHANGING), "rowfocuschanging"),
        (nameof(DataWindowEventChain.EVT_ROWFOCUSCHANGED), "rowfocuschanged"),
        (nameof(DataWindowEventChain.EVT_ITEMFOCUSCHANGED), "itemfocuschanged"),
        (nameof(DataWindowEventChain.EVT_ITEMCHANGED), "0-itemchanged"),
        (nameof(DataWindowEventChain.EVT_EDITCHANGED), "1-editchanged"),
        (nameof(DataWindowEventChain.EVT_CLICKED), "clicked"),
        (nameof(DataWindowEventChain.EVT_DOUBLECLICKED), "doubleclicked"),
        (nameof(DataWindowEventChain.EVT_LBUTTONUP), "lbuttonup"),
        (nameof(DataWindowEventChain.EVT_RBUTTONDOWN), "rbuttondown"),
        (nameof(DataWindowEventChain.EVT_RBUTTONUP), "rbuttonup"),
        (nameof(DataWindowEventChain.EVT_GETFOCUS), "getfocus"),
        (nameof(DataWindowEventChain.EVT_LOSEFOCUS), "losefocus")
    ];

    /// <summary>
    /// The NINE semantic events, each paired with the return type and the exact parameter list its
    /// legacy declaration prescribes [<c>:L11-L14</c>, <c>:L24-L26</c>, <c>:L28</c>, <c>:L32</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SIGNATURES ARE PART OF THE ORDERING CONTRACT, WHICH IS WHY THEY ARE PINNED HERE RATHER THAN
    /// LEFT TO THE COMPILER. Two of the nine cannot be expressed asynchronously at all, and each one's
    /// signature is the evidence:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   <c>onddsgetfilter</c> [<c>:L13</c>] produces its result through a <b><c>ref string</c>
    ///   out-parameter</b> and returns nothing. The parameter itself projects onto a wire field with
    ///   explicit presence; what has no fire-and-forget form is the SEQUENCING, because the caller blocks
    ///   on the produced filter - so the drop-down search pair is irreducibly request/response.
    ///   </description></item>
    ///   <item><description>
    ///   <c>oncolumnexpinvokemethod</c> [<c>:L14</c>] returns <c>any</c> over a <c>string args[]</c>.
    ///   AAP 0.4.5.2 maps <c>any</c> to <see cref="object"/>, and the string array is what makes
    ///   contract C-04's INVERTED macro channel expressible: the legacy expects the APPLICATION to
    ///   implement the macro switch, so across a boundary DataServices calls back into its client and
    ///   the calculation cannot proceed until it answers.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The remaining seven are declarations with no script, so their signature is the whole of their
    /// contract. A widened parameter or a changed return type would be a new feature (constraint C-B).
    /// </para>
    /// </remarks>
    private static readonly (string Member, Type ReturnType, string Parameters)[]
        SemanticEventSignatureTable =
    [
        // :L11  event type long oninitcontextmenu ( long row, dwobject dwo )
        (nameof(DataWindowEventChain.OnInitContextMenu), typeof(long), "Int64,IDataWindowObject"),

        // :L12  event type long oncontextmenu ( long row, dwobject dwo, long mid )
        (nameof(DataWindowEventChain.OnContextMenu), typeof(long), "Int64,IDataWindowObject,Int64"),

        // :L13  event onddsgetfilter ( long row, dwobject dwo, string data, ref string filter )
        (nameof(DataWindowEventChain.OnDdsGetFilter),
            typeof(void),
            "Int64,IDataWindowObject,String,String&"),

        // :L14  event type any oncolumnexpinvokemethod ( long row, dwobject dwo, string name, string args[] )
        (nameof(DataWindowEventChain.OnColumnExpInvokeMethod),
            typeof(object),
            "Int64,IDataWindowObject,String,String[]"),

        // :L24  event type long ondoitemchange ( long row, dwobject dwo, string data )
        (nameof(DataWindowEventChain.OnDoItemChange),
            typeof(long),
            "Int64,IDataWindowObject,String"),

        // :L25  event onitemchanged ( long row, dwobject dwo )
        (nameof(DataWindowEventChain.OnItemChanged), typeof(void), "Int64,IDataWindowObject"),

        // :L26  event ondoitemchanged ( long row, dwobject dwo )
        (nameof(DataWindowEventChain.OnDoItemChanged), typeof(void), "Int64,IDataWindowObject"),

        // :L28  event onddsfiltered ( long row, dwobject dwo, long rowcount, long filteredcount )
        (nameof(DataWindowEventChain.OnDdsFiltered),
            typeof(void),
            "Int64,IDataWindowObject,Int64,Int64"),

        // :L32  event oncolumnexptrace ( long row, dwobject dwo, string stack, string expr, string value )
        (nameof(DataWindowEventChain.OnColumnExpTrace),
            typeof(void),
            "Int64,IDataWindowObject,String,String,String")
    ];

    /// <summary>The event-to-member table as theory data.</summary>
    public static TheoryData<EventId, string> EventMembers
    {
        get
        {
            TheoryData<EventId, string> data = new();

            foreach ((EventId id, string member) in EventMemberTable)
            {
                data.Add(id, member);
            }

            return data;
        }
    }

    /// <summary>The topic-value table as theory data.</summary>
    public static TheoryData<string, string> TopicValues
    {
        get
        {
            TheoryData<string, string> data = new();

            foreach ((string constant, string value) in TopicValueTable)
            {
                data.Add(constant, value);
            }

            return data;
        }
    }

    /// <summary>The nine semantic signatures as theory data.</summary>
    public static TheoryData<string, Type, string> SemanticEventSignatures
    {
        get
        {
            TheoryData<string, Type, string> data = new();

            foreach ((string member, Type returnType, string parameters) in SemanticEventSignatureTable)
            {
                data.Add(member, returnType, parameters);
            }

            return data;
        }
    }

    /// <summary>
    /// The THIRTEEN raw <c>pbm_dwn*</c> events as theory data, each paired with its ONE-BASED position
    /// in the oracle's declaration order [<c>:L15-L23</c>, <c>:L27</c>, <c>:L29-L31</c>].
    /// </summary>
    /// <remarks>
    /// DECLARATION ORDER IS DATA HERE BECAUSE IT IS NOT ALPHABETICAL, NOT GROUPED AND NOT DERIVABLE.
    /// The oracle interleaves the item-change family through the mouse and focus families - item
    /// validation error sits at <c>:L27</c>, between <c>ondwnitemchange</c> at <c>:L23</c> and
    /// <c>ondwnkillfocus</c> at <c>:L29</c>, with two SEMANTIC declarations in between at
    /// <c>:L24-L26</c> - so the only way to state the order is to state it.
    /// </remarks>
    public static TheoryData<int, string> RawEventDeclarationOrder
    {
        get
        {
            TheoryData<int, string> data = new();

            for (int position = 0; position < RawEventOrderTable.Length; position++)
            {
                data.Add(position + 1, RawEventOrderTable[position]);
            }

            return data;
        }
    }

    /// <summary>
    /// The two row-focus raw events, paired with what a BROKER veto does to each - which is the
    /// deliberate asymmetry at <c>:L124-L127</c> against <c>:L130-L133</c>.
    /// </summary>
    /// <remarks>
    /// Columns are: whether the event is <c>ondwnrowchanging</c> rather than <c>ondwnrowchange</c>; the
    /// result the event must return when the broker answers <c>1</c>; and the locator the row is drawn
    /// from, carried as data so a failure message names the line rather than the theory.
    /// </remarks>
    public static TheoryData<bool, long, string> RowFocusBrokerVetoMatrix =>
        new()
        {
            // ondwnrowchange TRIGGERS the broker and DISCARDS its result - the focus has already moved,
            // so there is nothing left to veto.
            { false, RetCode.OK, "se_cst_dw.sru:L126" },

            // ondwnrowchanging TESTS the result, because the move can still be stopped.
            { true, RetCode.PREVENT, "se_cst_dw.sru:L132" }
        };

    /// <summary>
    /// The four combinations of (<c>DataWindow.Processing</c> is <c>"1"</c>) by (<c>SetRow</c>
    /// actually moves the row) that <c>:L152-L159</c> discriminates between.
    /// </summary>
    /// <remarks>
    /// Columns are: the <c>Describe("DataWindow.Processing")</c> answer; whether the host's
    /// <c>SetRow</c> genuinely moves the cursor; the event's expected return; the expected current row
    /// afterwards; and whether the outcome should report a row-switch attempt at all - which is
    /// <see langword="null"/> when the guarded block was never entered, so "not attempted" and
    /// "attempted and failed" stay distinguishable.
    /// </remarks>
    public static TheoryData<string, bool, long, long, bool?> ClickRowSwitchMatrix =>
        new()
        {
            // Processing, SetRow moves, expected result, expected current row, switch attempted.
            { "1", true, RetCode.OK, 2L, true },
            { "1", false, RetCode.PREVENT, 1L, false },
            { "0", true, RetCode.OK, 1L, null },
            { "0", false, RetCode.OK, 1L, null }
        };

    /// <summary>
    /// The four combinations of (the column-expression service is <c>#Enabled</c>) by (the
    /// <c>0-itemchanged</c> topic has a subscriber) that <c>:L313-L319</c> discriminates between.
    /// </summary>
    /// <remarks>
    /// Steps one and two are independently gated and step three is gated on NOTHING, so the expected
    /// sequence is a different ORDERED LIST in each of the four rows and the semantic event is the last
    /// entry in all four.
    /// </remarks>
    public static TheoryData<bool, bool> DoItemChangedGateMatrix =>
        new()
        {
            { true, true },
            { true, false },
            { false, true },
            { false, false }
        };


    // ==============================================================================================
    //  FIXTURE
    // ==============================================================================================

    /// <summary>One assembled chain plus everything a test needs to observe it.</summary>
    private sealed record ChainFixture(
        FakeEventChain Chain,
        RecordingEventObserver Observer,
        FakeAttachedServiceFactory Services,
        ValidationSession Session)
    {
        internal FakeDataWindowHost Host => Chain.Host;

        internal FakeChainServiceLog Log => Services.Log;

        internal IDataWindowObject Dwo => Host.DwObject(ColumnName);
    }

    /// <summary>
    /// A session seeded with nothing but a correlation id, a handle and the one legacy field a caller
    /// may seed - the disabled-event bitmask [<c>:L89-L90</c>].
    /// </summary>
    /// <param name="disabledMask">The initial disabled-event bitmask.</param>
    /// <returns>The session.</returns>
    /// <remarks>
    /// THE LIFETIME ARGUMENT IS LEFT AT ITS DEFAULT DELIBERATELY. Passing nothing is behaviourally
    /// identical to passing a fresh option object - the session itself substitutes one - and it keeps
    /// this file's import surface to exactly the dependencies it was given, with no reach into the
    /// configuration namespace for a value that has no bearing on event ordering.
    /// </remarks>
    private static ValidationSession NewSession(uint disabledMask = 0u) =>
        new("chain-session", "dw-1", disabledMask);

    /// <summary>
    /// Builds a chain over a one-column, one-row fixture with the cursor on row 1.
    /// </summary>
    /// <param name="disabledMask">The initial disabled-event bitmask [<c>:L89-L90</c>].</param>
    /// <returns>The fixture.</returns>
    /// <remarks>
    /// NO SEQUENCER IS EVER SUPPLIED FROM HERE. The chain builds its own, and how that sequencer
    /// ACCEPTS a token is EventOrderingPatternTests.cs's subject - see THE ONE LINE THIS FILE MUST NOT
    /// CROSS in the file header. What this file reads from a dispatch is the ORDER the outcomes arrived
    /// in and the entry order their tokens record, both of which the chain produces on its own.
    /// </remarks>
    private static ChainFixture NewFixture(uint disabledMask = 0u)
    {
        RecordingEventObserver observer = new();
        FakeAttachedServiceFactory services = new();
        ValidationSession session = NewSession(disabledMask);
        FakeEventChain chain = new(session, services, observer);

        chain.Host.AddColumn(ColumnName, ColumnType);
        chain.Host.AddRow("original");
        chain.Host.CurrentRow = Row;

        return new ChainFixture(chain, observer, services, session);
    }

    // ==============================================================================================
    //  BRIEF PHASE 1 - THE TWENTY-TWO-EVENT SURFACE, ENUMERATED
    //  --------------------------------------------------------------------------------------------
    //  Nine SEMANTIC with their legacy signatures, thirteen RAW in declaration order, the total of
    //  twenty-two, and the one-for-one correspondence with contract C-03's own event set.
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(EventMembers))]
    public void EveryContractEventHasAChainMember(EventId eventId, string member)
    {
        Assert.NotEqual(EventId.Unspecified, eventId);
        Assert.NotNull(typeof(DataWindowEventChain).GetMethod(member));
    }

    [Theory]
    [MemberData(nameof(SemanticEventSignatures))]
    public void EverySemanticEventCarriesItsLegacySignature(
        string member,
        Type returnType,
        string parameters)
    {
        // :L11-L14, :L24-L26, :L28, :L32. The nine semantic declarations, signature by signature.
        // Parameter types are compared as an ORDERED, comma-joined list, because a semantic event's
        // parameter ORDER is as much a part of the contract as its parameter set - `oncontextmenu`
        // [:L12] takes (row, dwo, mid) and nothing else would carry the same meaning.
        System.Reflection.MethodInfo? found = typeof(DataWindowEventChain).GetMethod(member);

        Assert.NotNull(found);
        Assert.Equal(returnType, found.ReturnType);
        Assert.Equal(
            parameters,
            string.Join(',', found.GetParameters().Select(parameter => parameter.ParameterType.Name)));
    }

    [Fact]
    public void ExactlyNineSemanticEventsArePinnedWithTheirSignatures()
    {
        // The count is the assertion: a tenth row here, or a missing one, would mean the 13-versus-9
        // divergence AAP 0.6.1.1 cites had moved without anybody noticing.
        Assert.Equal(9, SemanticEventSignatureTable.Length);

        // And the nine are exactly the nine the event-to-member table classifies as non-raw, so the two
        // tables cannot drift apart.
        Assert.Equal(
            [.. EventMemberTable
                .Select(entry => entry.Member)
                .Where(name => !name.StartsWith("OnDwn", StringComparison.Ordinal))],
            SemanticEventSignatureTable.Select(entry => entry.Member));
    }

    [Theory]
    [MemberData(nameof(RawEventDeclarationOrder))]
    public void EveryRawEventExistsAtItsDeclaredPosition(int position, string member)
    {
        // :L15-L23, :L27, :L29-L31. ONE-BASED position, matching the oracle's own indexing convention
        // (AAP 0.4.5.4), so a row reads as "the Nth raw event the oracle declares".
        System.Reflection.MethodInfo? found = typeof(DataWindowEventChain).GetMethod(member);

        Assert.NotNull(found);
        Assert.StartsWith("OnDwn", member, StringComparison.Ordinal);
        Assert.Equal(member, RawEventOrderTable[position - 1]);
    }

    [Fact]
    public void TheThirteenRawEventsAreDeclaredInTheOraclesOwnOrder()
    {
        // THE WHOLE ORDERED LIST IN ONE ASSERTION, not a set. A set comparison would pass on a
        // reordered chain [:L15-L31], and the interleaving is the part a reader would "tidy": the
        // item-change family is split across :L23 and :L27 by two SEMANTIC declarations at :L24-L26.
        Assert.Equal(
            [
                nameof(DataWindowEventChain.OnDwnRButtonDown),
                nameof(DataWindowEventChain.OnDwnRButtonUp),
                nameof(DataWindowEventChain.OnDwnRowChange),
                nameof(DataWindowEventChain.OnDwnRowChanging),
                nameof(DataWindowEventChain.OnDwnLButtonDblClk),
                nameof(DataWindowEventChain.OnDwnLButtonClk),
                nameof(DataWindowEventChain.OnDwnChanging),
                nameof(DataWindowEventChain.OnDwnItemChangeFocus),
                nameof(DataWindowEventChain.OnDwnItemChange),
                nameof(DataWindowEventChain.OnDwnItemValidationError),
                nameof(DataWindowEventChain.OnDwnKillFocus),
                nameof(DataWindowEventChain.OnDwnLButtonUp),
                nameof(DataWindowEventChain.OnDwnSetFocus)
            ],
            RawEventOrderTable);

        Assert.Equal(13, RawEventOrderTable.Length);

        // And that order is the order the event-to-member table lists them in, so the contract's own
        // enumeration and the oracle's declaration order agree.
        Assert.Equal(
            RawEventOrderTable,
            EventMemberTable
                .Select(entry => entry.Member)
                .Where(name => name.StartsWith("OnDwn", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheChainDeclaresExactlyTwentyTwoEvents_NineSemanticAndThirteenRaw()
    {
        // :L11-L32. The split is 9 + 13 and the totals are counted rather than asserted from a comment.
        string[] semantic =
        [
            nameof(DataWindowEventChain.OnInitContextMenu),
            nameof(DataWindowEventChain.OnContextMenu),
            nameof(DataWindowEventChain.OnDdsGetFilter),
            nameof(DataWindowEventChain.OnColumnExpInvokeMethod),
            nameof(DataWindowEventChain.OnDoItemChange),
            nameof(DataWindowEventChain.OnItemChanged),
            nameof(DataWindowEventChain.OnDoItemChanged),
            nameof(DataWindowEventChain.OnDdsFiltered),
            nameof(DataWindowEventChain.OnColumnExpTrace)
        ];

        // Every raw one is spelled OnDwn*, which is the oracle's own pbm_dwn* prefix.
        string[] raw = [.. EventMemberTable
            .Select(entry => entry.Member)
            .Where(name => name.StartsWith("OnDwn", StringComparison.Ordinal))];

        Assert.Equal(9, semantic.Length);
        Assert.Equal(13, raw.Length);
        Assert.Equal(22, semantic.Length + raw.Length);
        Assert.Equal(22, EventMemberTable.Length);

        // And the contract enumerates exactly the same twenty-two, Unspecified aside.
        Assert.Equal(22, Enum.GetValues<EventId>().Count(id => id != EventId.Unspecified));
    }

    [Fact]
    public void OnDdsGetFilterProducesItsResultThroughARefStringAndReturnsNothing()
    {
        // :L13. The `ref` out-parameter is the reason the drop-down search pair is irreducibly
        // request/response: the caller BLOCKS on the produced filter, so the answer has no fire-and-forget
        // form even though the parameter itself projects onto a wire field. (Which ordering PATTERN that fact earns the area is
        // EventOrderingPatternTests.cs's call; this file asserts the fact itself.)
        System.Reflection.MethodInfo member =
            typeof(DataWindowEventChain).GetMethod(nameof(DataWindowEventChain.OnDdsGetFilter))!;

        Assert.Equal(typeof(void), member.ReturnType);

        System.Reflection.ParameterInfo produced = member.GetParameters()[^1];
        Assert.True(produced.ParameterType.IsByRef);
        Assert.Equal(typeof(string), produced.ParameterType.GetElementType());
    }

    [Fact]
    public void OnDdsGetFilterDefaultLeavesTheFilterExactlyAsPassed()
    {
        // An unhandled PowerBuilder event performs nothing and leaves its `ref` parameter untouched.
        // Assigning even the empty string would turn "declined to filter" into "match nothing".
        ChainFixture fixture = NewFixture();
        string filter = "untouched";

        fixture.Chain.OnDdsGetFilter(Row, fixture.Dwo, "typed", ref filter);

        Assert.Equal("untouched", filter);
    }

    [Fact]
    public void OnColumnExpInvokeMethodIsTheInvertedMacroChannel()
    {
        // :L14 returns `any` over a `string args[]`. AAP 0.4.5.2 maps `any` to object, and the string
        // array is what makes C-04's InvokeMethodChannel expressible: the legacy expects the
        // APPLICATION to implement the macro switch, so across a boundary DataServices calls back into
        // its client. A virtual member with a null default is that question.
        System.Reflection.MethodInfo member = typeof(DataWindowEventChain)
            .GetMethod(nameof(DataWindowEventChain.OnColumnExpInvokeMethod))!;

        Assert.Equal(typeof(object), member.ReturnType);
        Assert.Contains(member.GetParameters(), parameter => parameter.ParameterType == typeof(string[]));

        ChainFixture fixture = NewFixture();

        // null is a LEGITIMATE result and not a "no result" marker, so the default answers null rather
        // than zero or the empty string.
        Assert.Null(fixture.Chain.OnColumnExpInvokeMethod(Row, fixture.Dwo, "Invoke", ["1", "2"]));
    }

    [Fact]
    public void TheSevenDeclaredSemanticEventsAreInertByDefault()
    {
        // :L11-L14, :L25, :L28, :L32 are DECLARATIONS with no script. An unhandled PowerBuilder event
        // yields its type's initial value and performs nothing - so none of them may touch the host,
        // and none may report a dispatch that did not happen.
        ChainFixture fixture = NewFixture();
        fixture.Host.RecordsReads = true;
        fixture.Host.CallLog.Clear();
        string filter = string.Empty;

        Assert.Equal(0L, fixture.Chain.OnInitContextMenu(Row, fixture.Dwo));
        Assert.Equal(0L, fixture.Chain.OnContextMenu(Row, fixture.Dwo, 42L));
        fixture.Chain.OnDdsGetFilter(Row, fixture.Dwo, "typed", ref filter);
        Assert.Null(fixture.Chain.OnColumnExpInvokeMethod(Row, fixture.Dwo, string.Empty, []));
        fixture.Chain.OnItemChanged(Row, fixture.Dwo);
        fixture.Chain.OnDdsFiltered(Row, fixture.Dwo, 3L, 7L);
        fixture.Chain.OnColumnExpTrace(Row, fixture.Dwo, "a>b", "1+1", "2");

        Assert.Empty(fixture.Host.CallLog.Records);
        Assert.Empty(fixture.Observer.Outcomes);
    }

    // ==============================================================================================
    //  BRIEF PHASE 3 - THE TWELVE TOPICS AND THE THREE-ENCODING TOPIC STRING
    //  --------------------------------------------------------------------------------------------
    //  The byte-exact values, the two ordering prefixes, the sort key, the decomposed triple's round
    //  trip, the `.^persistent` lifetime and the tri-valued veto that follows it.
    // ==============================================================================================

    [Theory]
    [MemberData(nameof(TopicValues))]
    public void EveryTopicConstantCarriesItsByteExactLegacyValue(string constant, string value)
    {
        string? actual = (string?)typeof(DataWindowEventChain)
            .GetField(constant)!
            .GetRawConstantValue();

        Assert.Equal(value, actual);
    }

    [Fact]
    public void TwelveTopicsAreDeclaredAndParsed()
    {
        // :L47-L76 declares exactly twelve.
        Assert.Equal(12, TopicValueTable.Length);
        Assert.Equal(12, DataWindowEventChain.Topics.Count);

        // Declaration order is observable: it is what the no-suffix sweep below walks.
        Assert.Equal(
            [.. TopicValueTable.Select(entry => entry.Value)],
            DataWindowEventChain.Topics.Select(topic => topic.LegacyName));
    }

    [Fact]
    public void TheTwoOrderingPrefixesAreCarriedInTheValueNotStrippedFromIt()
    {
        // The prefix is baked into the VALUE at :L54 and :L57, and the broker's dispatch order derives
        // from the lexical sort of the subscription name - so the prefix is functional, not decorative.
        Assert.Equal("0-itemchanged", DataWindowEventChain.EVT_ITEMCHANGED);
        Assert.Equal("1-editchanged", DataWindowEventChain.EVT_EDITCHANGED);

        SubscriptionTopic itemChanged = DataWindowEventChain.TopicOf(DataWindowEventChain.EVT_ITEMCHANGED)!;
        SubscriptionTopic editChanged = DataWindowEventChain.TopicOf(DataWindowEventChain.EVT_EDITCHANGED)!;

        // SubscriptionTopic decomposes the fused string into its three encodings, and the residual
        // dispatch key KEEPS the prefix.
        Assert.Equal(0, itemChanged.Sequence);
        Assert.Equal(1, editChanged.Sequence);
        Assert.Equal("itemchanged", itemChanged.LogicalName);
        Assert.Equal("editchanged", editChanged.LogicalName);
        Assert.Equal("0-itemchanged", itemChanged.LegacyName);
        Assert.Equal("1-editchanged", editChanged.LegacyName);
    }

    [Fact]
    public void DispatchOrderIsKeyedOnLegacyName_AndLogicalNameInvertsIt()
    {
        // THE ORDERING TEST WITH TEETH. Sorting by LegacyName puts item-changed first, which is what
        // the "0-"/"1-" prefixes exist to achieve. Sorting by LogicalName INVERTS the pair, because
        // "editchanged" < "itemchanged" - so a refactor that "simplified" the sort key would silently
        // reorder these two, and this assertion is what stops it.
        string[] pair = [DataWindowEventChain.EVT_ITEMCHANGED, DataWindowEventChain.EVT_EDITCHANGED];

        SubscriptionTopic[] topics = [.. pair.Select(name => DataWindowEventChain.TopicOf(name)!)];

        string[] byLegacyName =
        [
            .. topics
                .OrderBy(topic => topic.LegacyName, StringComparer.Ordinal)
                .Select(topic => topic.LegacyName)
        ];

        string[] byLogicalName =
        [
            .. topics
                .OrderBy(topic => topic.LogicalName, StringComparer.Ordinal)
                .Select(topic => topic.LegacyName)
        ];

        Assert.Equal(["0-itemchanged", "1-editchanged"], byLegacyName);
        Assert.Equal(["1-editchanged", "0-itemchanged"], byLogicalName);
        Assert.NotEqual(byLegacyName, byLogicalName);

        // The broker's own comparer agrees with the legacy key, which is the property that matters at
        // dispatch time.
        Assert.Equal(
            ["0-itemchanged", "1-editchanged"],
            topics.Order(SubscriptionTopic.DispatchOrderComparer).Select(topic => topic.LegacyName));

        // The pairwise comparison agrees too, in BOTH directions, so the ordering is a total order and
        // not an artefact of the sort's stability.
        Assert.True(SubscriptionTopic.CompareDispatchOrder(topics[0], topics[1]) < 0);
        Assert.True(SubscriptionTopic.CompareDispatchOrder(topics[1], topics[0]) > 0);
    }

    [Fact]
    public void OverTheWholeTwelveALogicalNameSortDivergesFromTheDispatchOrder()
    {
        // THE SAME GUARD, WIDENED FROM THE PAIR TO ALL TWELVE, because a refactor does not change the
        // sort key of two topics - it changes it for the whole registry. Sorting the full set by
        // LogicalName produces a DIFFERENT sequence from sorting it by LegacyName, and the difference is
        // exactly the "0-"/"1-" pair moving [:L54, :L57]: with the prefixes stripped, "editchanged" and
        // "itemchanged" fall into alphabetical position among the other ten instead of leading them.
        string[] byLegacyName =
        [
            .. DataWindowEventChain.Topics
                .OrderBy(topic => topic.LegacyName, StringComparer.Ordinal)
                .Select(topic => topic.LegacyName)
        ];

        string[] byLogicalName =
        [
            .. DataWindowEventChain.Topics
                .OrderBy(topic => topic.LogicalName, StringComparer.Ordinal)
                .Select(topic => topic.LegacyName)
        ];

        Assert.Equal(12, byLegacyName.Length);
        Assert.NotEqual(byLegacyName, byLogicalName);

        // THE TWO PREFIXED TOPICS LEAD THE DISPATCH ORDER, because ASCII digits sort before letters -
        // which is the entire reason their authors spelled a digit into the name.
        Assert.Equal(["0-itemchanged", "1-editchanged"], byLegacyName[..2]);

        // Under a logical-name sort neither leads, so a "simplified" sort key would silently demote both.
        Assert.DoesNotContain(byLogicalName[0], new[] { "0-itemchanged", "1-editchanged" });

        // And the dispatch comparer reproduces the legacy-name sequence over the whole set.
        Assert.Equal(
            byLegacyName,
            DataWindowEventChain.Topics
                .Order(SubscriptionTopic.DispatchOrderComparer)
                .Select(topic => topic.LegacyName));
    }

    [Fact]
    public void BothPrefixedTopicsRoundTripThroughToLegacyStringByteExactly()
    {
        foreach (string name in
            new[] { DataWindowEventChain.EVT_ITEMCHANGED, DataWindowEventChain.EVT_EDITCHANGED })
        {
            SubscriptionTopic topic = DataWindowEventChain.TopicOf(name)!;
            Assert.Equal(name, topic.ToLegacyString());
        }
    }

    [Fact]
    public void EveryTopicRoundTripsAndCarriesNoNamespaceSuffix()
    {
        // THE NEGATIVE CONSTRAINT, MACHINE-CHECKED. `.^persistent` occurs at exactly six sites
        // repository-wide, ALL of them in ws_objects/pfw.thread.pbl.src - n_cst_threading.sru:L544 and
        // :L596, n_cst_threading_task.sru:L385 and :L391, n_cst_thread.sru:L552 and :L561 - which is
        // PERSISTENCE's threading layer, not DataServices. se_cst_dw.sru contains no `^` character at
        // all, and its seven of_on/of_off overloads at :L448-L467 delegate straight through with no
        // suffix and no "does the name contain a dot" test. Asserted over the WHOLE topic set so a
        // contributor copying the threading pattern is stopped by a failing build.
        foreach (SubscriptionTopic topic in DataWindowEventChain.Topics)
        {
            Assert.Equal(topic.LegacyName, topic.ToLegacyString());
            Assert.False(topic.HasNamespace);
            Assert.False(topic.HasNamespaceCriterion);
            Assert.Null(topic.NamespaceCriterion);
            Assert.DoesNotContain('^', topic.LegacyName);
            Assert.DoesNotContain('.', topic.LegacyName);

            // The lifetime component of the triple is therefore TRANSIENT on all twelve. Not "absent":
            // the field is always carried, and its value here is the one an unnamespaced subscription
            // projects onto.
            Assert.Equal(SubscriptionLifetime.Transient, topic.Lifetime);
        }
    }

    [Fact]
    public void ThePersistentLifetimeIsRepresentableInTheTripleEvenThoughThisChainNeverEmitsIt()
    {
        // BOTH HALVES OF THE SAME CONSTRAINT, IN ONE TEST, BECAUSE ONE WITHOUT THE OTHER IS MISLEADING.
        //
        // REPRESENTABLE: the triple's third field can carry the threading layer's lifetime, so the
        // decomposition of AAP 0.6.1.2 is not lossy. `.^persistent` at n_cst_threading.sru:L544 and
        // :L596 is a FILTER - "every name WHERE namespace is NOT persistent" - and both the namespace it
        // names and the lifetime it projects onto round-trip.
        SubscriptionTopic persistent = new()
        {
            LegacyName = "rowfocuschanged",
            NamespaceCriterion = SubscriptionNamespaces.Persistent
        };

        Assert.Equal(SubscriptionLifetime.Persistent, persistent.Lifetime);
        Assert.True(persistent.HasNamespace);
        Assert.Equal("rowfocuschanged." + SubscriptionNamespaces.Persistent, persistent.ToLegacyString());

        // NOT EMITTED HERE: the persistent namespace belongs to PERSISTENCE's threading layer. Not one
        // of this chain's twelve topics names it, and none of the seven of_on/of_off overloads appends
        // it [:L448-L467 delegate straight through].
        Assert.DoesNotContain(
            SubscriptionNamespaces.Persistent,
            DataWindowEventChain.Topics.Select(topic => topic.Namespace));
        Assert.All(
            DataWindowEventChain.Topics,
            topic => Assert.Equal(SubscriptionNamespaces.None, topic.Namespace));
    }

    [Theory]
    [InlineData(0, "itemchanged", "0-itemchanged")]
    [InlineData(1, "editchanged", "1-editchanged")]
    public void TheDecomposedTripleReconstitutesTheFusedLegacyStringAndParsesBackToTheSameFields(
        int sequence,
        string logicalName,
        string fused)
    {
        // THE ROUND TRIP AAP 0.6.1.2 REQUIRES, IN BOTH DIRECTIONS.
        //
        // The wire contract carries `sequence`, `name` and `lifetime` as THREE SEPARATE FIELDS; the fused
        // legacy spelling is reconstituted only at the compatibility edge. Transmit the fused string
        // opaquely and the ordering becomes invisible; parse it at the far end and the contract has an
        // undocumented grammar. So both directions must be exact.
        //
        // FORWARD - from the triple to the fused string. `sequence` and `logicalName` are spelled back
        // into the name with the `-` of TopicSymbols.Prepend between them, which IS the legacy encoding
        // at :L54 and :L57, and `lifetime` contributes no suffix because this chain's lifetime is
        // transient.
        SubscriptionTopic built = new() { LegacyName = $"{sequence}{TopicSymbols.Prepend}{logicalName}" };

        Assert.Equal(fused, built.ToLegacyString());
        Assert.Equal(sequence, built.Sequence);
        Assert.Equal(logicalName, built.LogicalName);
        Assert.Equal(SubscriptionLifetime.Transient, built.Lifetime);

        // BACKWARD - from the fused string to the same three fields.
        Assert.Equal(RetCode.OK, SubscriptionTopic.ParseSubscription(fused, out SubscriptionTopic? parsed));
        Assert.NotNull(parsed);
        Assert.Equal(sequence, parsed.Sequence);
        Assert.Equal(logicalName, parsed.LogicalName);
        Assert.Equal(SubscriptionLifetime.Transient, parsed.Lifetime);

        // AND THE TWO AGREE, field for field and byte for byte - which is what makes the fused form a
        // projection of the triple rather than a second, parallel source of truth.
        Assert.Equal(built.Sequence, parsed.Sequence);
        Assert.Equal(built.LogicalName, parsed.LogicalName);
        Assert.Equal(built.Lifetime, parsed.Lifetime);
        Assert.Equal(built.ToLegacyString(), parsed.ToLegacyString());

        // The chain's own constant is that same fused string, so the round trip is over the REAL topic
        // and not a look-alike built for the test.
        Assert.Equal(fused, DataWindowEventChain.TopicOf(fused)!.ToLegacyString());
    }

    [Fact]
    public void AnUnprefixedTopicCarriesNoSequenceAndKeepsItsWholeNameAsTheLogicalOne()
    {
        // THE OTHER TEN, WHICH ARE THE CONTROL CASE FOR THE TRIPLE. A null sequence is not "sequence
        // zero": zero is a real position that "0-itemchanged" occupies [:L54], so collapsing absent onto
        // zero would give ten topics a dispatch position they were never given.
        foreach (SubscriptionTopic topic in DataWindowEventChain.Topics.Where(
            candidate => candidate.Sequence is null))
        {
            Assert.Equal(topic.LegacyName, topic.LogicalName);
            Assert.Equal(topic.LegacyName, topic.ToLegacyString());
        }

        Assert.Equal(10, DataWindowEventChain.Topics.Count(topic => topic.Sequence is null));
        Assert.Equal(2, DataWindowEventChain.Topics.Count(topic => topic.Sequence is not null));
    }

    [Fact]
    public void TopicOfAnsweredNullForAnUnparsableNameRatherThanGuessing()
    {
        Assert.Null(DataWindowEventChain.TopicOf(null));
        Assert.NotNull(DataWindowEventChain.TopicOf(DataWindowEventChain.EVT_CLICKED));
    }

    // ==============================================================================================
    //  BRIEF PHASE 2 - EVERY RAW EVENT'S DELEGATION SHAPE, ONE AT A TIME
    //  --------------------------------------------------------------------------------------------
    //  THE PATTERN IS raw -> semantic -> broker, AND IT IS NOT UNIFORM. The variations ARE the
    //  behaviour, so each is asserted separately rather than through one shared helper that would
    //  smooth them over.
    // ==============================================================================================

    /// <summary>Subscribes a recording subscriber to one topic through the chain's own pass-through.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="topic">The topic.</param>
    /// <returns>The subscriber.</returns>
    private static RecordingSubscriber Subscribe(ChainFixture fixture, string topic)
    {
        RecordingSubscriber subscriber = new(topic);

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(topic, subscriber, nameof(RecordingSubscriber.OnThreeArguments)));

        return subscriber;
    }

    /// <summary>The semantic events the fake host recorded, in order.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The recorded semantic-event members.</returns>
    private static IReadOnlyList<string> SemanticEvents(ChainFixture fixture) =>
        [.. fixture.Host.CallLog.Members.Where(member =>
            member.StartsWith("Event ", StringComparison.Ordinal))];

    // ---------------------------------------------------------------------------------------------
    //  :L115-L117  ondwnrbuttondown
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RButtonDown_RaisesTheSemanticEventThenTheBroker()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONDOWN);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonDown(10L, 20L, Row, fixture.Dwo));

        Assert.Equal(["Event RButtonDown"], SemanticEvents(fixture));
        Assert.Equal(1, subscriber.Count);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnrbuttondown);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);
        Assert.True(outcome.Dispatch.BrokerTriggerRan);
        Assert.Equal("rbuttondown", outcome.Topic!.LegacyName);
    }

    [Fact]
    public void RButtonDown_ASemanticVetoShortCircuitsBeforeTheBroker()
    {
        // :L115  if Event RButtonDown(...) = 1 then return 1
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONDOWN);
        fixture.Host.RButtonDownHandler = (_, _, _, _) => RetCode.PREVENT;

        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnRButtonDown(10L, 20L, Row, fixture.Dwo));

        Assert.Equal(0, subscriber.Count);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnrbuttondown);
        Assert.Equal(VetoResult.PreventOnce, outcome.SemanticVeto);
        Assert.False(outcome.Dispatch.BrokerTriggerRan);
    }

    [Fact]
    public void RButtonDown_ABrokerVetoIsHonoured()
    {
        // :L116  if Eventful.of_Trigger(EVT_RBUTTONDOWN,...) = 1 then return 1
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONDOWN);
        subscriber.Answer = RetCode.PREVENT;

        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnRButtonDown(10L, 20L, Row, fixture.Dwo));
        Assert.Equal(
            VetoResult.PreventOnce,
            fixture.Observer.Single(EventId.Ondwnrbuttondown).BrokerVeto);
    }

    // ---------------------------------------------------------------------------------------------
    //  :L120-L121  ondwnrbuttonup  and  :L395-L396  ondwnlbuttonup - THE TWO BROKER-ONLY RAW EVENTS
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RButtonUpAndLButtonUp_AreBrokerOnlyAndRaiseNoSemanticEvent()
    {
        // TWO SUCH CASES, NOT ONE. The folder brief flagged only ondwnrbuttonup; verification found
        // ondwnlbuttonup too [:L395-L396]. There is no semantic RButtonUp and no semantic LButtonUp
        // anywhere in the oracle, so inventing either would be a new feature (constraint C-B).
        ChainFixture fixture = NewFixture();
        RecordingSubscriber up = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);
        RecordingSubscriber left = Subscribe(fixture, DataWindowEventChain.EVT_LBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonUp(10L, 20L, Row, fixture.Dwo));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonUp(10L, 20L, Row, fixture.Dwo));

        Assert.Empty(SemanticEvents(fixture));
        Assert.Equal(1, up.Count);
        Assert.Equal(1, left.Count);

        foreach (EventId eventId in new[] { EventId.Ondwnrbuttonup, EventId.Ondwnlbuttonup })
        {
            DataWindowEventOutcome outcome = fixture.Observer.Single(eventId);
            Assert.False(outcome.Dispatch.SemanticHandlerRan);
            Assert.True(outcome.Dispatch.BrokerTriggerRan);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RButtonUpAndLButtonUp_HonourABrokerVeto(bool right)
    {
        ChainFixture fixture = NewFixture();
        string topic = right ? DataWindowEventChain.EVT_RBUTTONUP : DataWindowEventChain.EVT_LBUTTONUP;
        RecordingSubscriber subscriber = Subscribe(fixture, topic);
        subscriber.Answer = RetCode.PREVENT;

        long result = right
            ? fixture.Chain.OnDwnRButtonUp(10L, 20L, Row, fixture.Dwo)
            : fixture.Chain.OnDwnLButtonUp(10L, 20L, Row, fixture.Dwo);

        Assert.Equal(RetCode.PREVENT, result);
    }

    // ---------------------------------------------------------------------------------------------
    //  :L124-L133  ondwnrowchange and ondwnrowchanging - THE DELIBERATE ASYMMETRY
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(RowFocusBrokerVetoMatrix))]
    public void RowFocusEvents_TheBrokerResultIsHonouredByRowChangingAndDiscardedByRowChange(
        bool changing,
        long expected,
        string locator)
    {
        // THE TWO LOCATORS, NAMED, BECAUSE THE ASYMMETRY IS THE WHOLE POINT:
        //
        //   :L124-L127  event ondwnrowchange;   if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE) then return 0
        //                                       if Event RowFocusChanged(currentRow) = 1 then return 1
        //                                       Eventful.of_Trigger(EVT_ROWFOCUSCHANGED,currentRow)   <- BARE
        //                                       return 0
        //   :L130-L133  event ondwnrowchanging; if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE) then return 0
        //                                       if Event RowFocusChanging(...) = 1 then return 1
        //                                       if Eventful.of_Trigger(EVT_ROWFOCUSCHANGING,...) = 1 then return 1
        //                                       return 0
        //
        // :L126 is a BARE STATEMENT and :L132 is an `if`. A port that "normalised" the pair would
        // silently GAIN a veto on rowchange or LOSE one on rowchanging, and a test that drove only one
        // of the two would not notice either way - so both are driven from one theory.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(
            fixture,
            changing
                ? DataWindowEventChain.EVT_ROWFOCUSCHANGING
                : DataWindowEventChain.EVT_ROWFOCUSCHANGED);
        subscriber.Answer = RetCode.PREVENT;

        long actual = changing
            ? fixture.Chain.OnDwnRowChanging(Row, 2L)
            : fixture.Chain.OnDwnRowChange(Row);

        Assert.Equal(expected, actual);

        // The broker RAN on both, so the divergence really is about the RESULT and not about reaching
        // the broker edge at all.
        Assert.Equal(1, subscriber.Count);
        Assert.True(fixture.Observer.Last.Dispatch.BrokerTriggerRan);

        // And the veto is REPORTED on both, so a consumer can still see that a subscriber objected even
        // where the objection changed nothing.
        Assert.Equal(VetoResult.PreventOnce, fixture.Observer.Last.BrokerVeto);
        Assert.Contains("se_cst_dw.sru:L1", locator, StringComparison.Ordinal);
    }

    [Fact]
    public void RowChange_IgnoresTheBrokerResultWhileRowChanging_HonoursIt()
    {
        // THE SAME ASYMMETRY, SIDE BY SIDE ON ONE CHAIN [:L124-L133], which the theory above cannot
        // show: there, each row builds its own fixture, so nothing proves the two events behave
        // differently under IDENTICAL conditions. Here one chain, one pair of subscribers, both
        // answering PREVENT.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber changed = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGED);
        RecordingSubscriber changing = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGING);
        changed.Answer = RetCode.PREVENT;
        changing.Answer = RetCode.PREVENT;

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(Row));
        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnRowChanging(Row, 2L));

        Assert.Equal(1, changed.Count);
        Assert.Equal(1, changing.Count);

        // The veto WAS observed on both, and reported on both - it simply does not alter the result of
        // `rowchange`. Reporting it is how a consumer can still see that a subscriber objected.
        Assert.Equal(
            VetoResult.PreventOnce,
            fixture.Observer.Single(EventId.Ondwnrowchange).BrokerVeto);
        Assert.Equal(
            VetoResult.PreventOnce,
            fixture.Observer.Single(EventId.Ondwnrowchanging).BrokerVeto);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RowFocusEvents_HonourTheRowFocusChangeGate(bool changing)
    {
        // :L124 and :L130 both guard on EID_ROWFOCUSCHANGE - ONE bit covering BOTH events.
        ChainFixture fixture = NewFixture(EventGate.EID_ROWFOCUSCHANGE);
        RecordingSubscriber subscriber = Subscribe(
            fixture,
            changing
                ? DataWindowEventChain.EVT_ROWFOCUSCHANGING
                : DataWindowEventChain.EVT_ROWFOCUSCHANGED);

        long result = changing
            ? fixture.Chain.OnDwnRowChanging(Row, 2L)
            : fixture.Chain.OnDwnRowChange(Row);

        Assert.Equal(RetCode.OK, result);
        Assert.Empty(SemanticEvents(fixture));
        Assert.Equal(0, subscriber.Count);
        Assert.True(fixture.Observer.Last.Dispatch.GatedOut);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RowFocusEvents_ASemanticVetoShortCircuitsBeforeTheBroker(bool changing)
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(
            fixture,
            changing
                ? DataWindowEventChain.EVT_ROWFOCUSCHANGING
                : DataWindowEventChain.EVT_ROWFOCUSCHANGED);
        fixture.Host.RowFocusChangedHandler = _ => RetCode.PREVENT;
        fixture.Host.RowFocusChangingHandler = (_, _) => RetCode.PREVENT;

        long result = changing
            ? fixture.Chain.OnDwnRowChanging(Row, 2L)
            : fixture.Chain.OnDwnRowChange(Row);

        Assert.Equal(RetCode.PREVENT, result);
        Assert.Equal(0, subscriber.Count);
        Assert.Equal(VetoResult.PreventOnce, fixture.Observer.Last.SemanticVeto);
    }

    // ---------------------------------------------------------------------------------------------
    //  :L136-L161  ondwnlbuttondblclk and ondwnlbuttonclk - AND THE THREE LIVENESS GUARDS
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DoubleClick_RaisesTheSemanticEventThenTheBroker()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_DOUBLECLICKED);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonDblClk(1L, 2L, Row, fixture.Dwo));

        Assert.Equal(["Event DoubleClicked"], SemanticEvents(fixture));
        Assert.Equal(1, subscriber.Count);
        Assert.False(fixture.Observer.Last.Dispatch.TargetBecameInvalid);
    }

    [Fact]
    public void DoubleClick_TheLivenessGuardAtL138SkipsTheBrokerWhenTheHandlerDestroyedTheControl()
    {
        // GUARD ONE OF THREE [:L138]. A handler is permitted to destroy the control mid-dispatch -
        // `IsValid(this)` is what the oracle checks before touching it again, and skipping the broker
        // edge is the observable consequence.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_DOUBLECLICKED);
        fixture.Host.DoubleClickedHandler = (_, _, _, _) =>
        {
            fixture.Chain.Teardown();
            return RetCode.OK;
        };

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonDblClk(1L, 2L, Row, fixture.Dwo));

        Assert.Equal(0, subscriber.Count);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnlbuttondblclk);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);
        Assert.False(outcome.Dispatch.BrokerTriggerRan);
        Assert.True(outcome.Dispatch.TargetBecameInvalid);
    }

    [Fact]
    public void Click_TheLivenessGuardAtL147SkipsTheBrokerWhenTheHandlerDestroyedTheControl()
    {
        // GUARD TWO OF THREE [:L147].
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        fixture.Host.ClickedHandler = (_, _, _, _) =>
        {
            fixture.Chain.Teardown();
            return RetCode.OK;
        };

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonClk(1L, 2L, Row, fixture.Dwo));

        Assert.Equal(0, subscriber.Count);
        Assert.True(fixture.Observer.Single(EventId.Ondwnlbuttonclk).Dispatch.TargetBecameInvalid);
    }

    [Fact]
    public void Click_TheLivenessGuardAtL152SkipsTheRowSwitchWhenTheBrokerDestroyedTheControl()
    {
        // GUARD THREE OF THREE [:L152], AND IT IS NOT THE SAME AS GUARD TWO - it carries the extra
        // `row > 0` condition and it protects a DIFFERENT block. Destroying the control from the BROKER
        // handler leaves guard two already passed, so only the row switch is skipped. That is why the
        // three guards are not interchangeable and why all three are preserved.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        subscriber.InHandler = () => fixture.Chain.Teardown();
        fixture.Host.RecordsReads = true;
        fixture.Host.CallLog.Clear();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonClk(1L, 2L, 2L, fixture.Dwo));

        Assert.Equal(1, subscriber.Count);
        Assert.DoesNotContain("Describe", fixture.Host.CallLog.Members);
        Assert.DoesNotContain("SetRow", fixture.Host.CallLog.Members);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnlbuttonclk);
        Assert.True(outcome.Dispatch.BrokerTriggerRan);
        Assert.Null(outcome.RowSwitchAttempted);
    }

    [Theory]
    [MemberData(nameof(ClickRowSwitchMatrix))]
    public void Click_TheRowSwitchWithoutFocusAcrossAllFourCombinations(
        string processing,
        bool setRowMoves,
        long expectedResult,
        long expectedCurrentRow,
        bool? expectedSwitchAttempted)
    {
        // ALL FOUR COMBINATIONS OF (Processing == "1") BY (SetRow ACTUALLY MOVES), because the block is
        // two nested conditions over two independent facts and only the full cross-product pins it:
        //
        //   :L152  if IsValid(this) and row > 0 then
        //   :L153      if Describe("DataWindow.Processing") = "1" then     <- STRING compare, not boolean
        //   :L154          if GetRow() <> row then
        //   :L155              SetRow(row)
        //   :L156              if GetRow() <> row then return 1            <- SetRow IS FALLIBLE
        //
        // The RE-READ at :L156 is the load-bearing line: trusting SetRow's own return code instead would
        // let a failed move look successful, and a row-count assertion would never catch it.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;
        fixture.Host.Processing = processing;
        fixture.Host.SetRowMovesCurrentRow = setRowMoves;
        fixture.Host.RecordsReads = true;

        Assert.Equal(expectedResult, fixture.Chain.OnDwnLButtonClk(1L, 2L, 2L, fixture.Dwo));

        Assert.Equal(expectedCurrentRow, fixture.Host.CurrentRow);

        // null means the guarded block was never entered, so "not attempted" stays distinguishable from
        // "attempted and failed" - which are the row 3 / row 4 and the row 2 cases respectively.
        Assert.Equal(
            expectedSwitchAttempted,
            fixture.Observer.Single(EventId.Ondwnlbuttonclk).RowSwitchAttempted);

        // SetRow is reached only when the processing test passed, so its presence in the call log is the
        // independent witness that :L153 gated the block rather than :L154.
        if (expectedSwitchAttempted is null)
        {
            Assert.DoesNotContain("SetRow", fixture.Host.CallLog.Members);
        }
        else
        {
            Assert.Contains("SetRow", fixture.Host.CallLog.Members);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData("1 ")]
    [InlineData("01")]
    public void Click_ComparesTheProcessingAnswerAsAStringAgainstExactlyOne(string processing)
    {
        // :L153 compares Describe("DataWindow.Processing") AS A STRING against "1". Anything else - a
        // "0", a "no", an empty answer, a trailing space or a leading zero - fails the test, so the row
        // is left alone. A port that coerced the answer to a boolean or an integer would treat "01" and
        // "1 " as true and switch a row the oracle leaves put.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;
        fixture.Host.Processing = processing;

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonClk(1L, 2L, 2L, fixture.Dwo));

        Assert.Equal(1L, fixture.Host.CurrentRow);
        Assert.Null(fixture.Observer.Single(EventId.Ondwnlbuttonclk).RowSwitchAttempted);
    }

    [Fact]
    public void Click_LeavesTheRowAloneForRowZero()
    {
        // :L152 guards on `row > 0` as well as liveness. A click on a header or a summary band carries
        // row 0, and there is no row to switch to.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonClk(1L, 2L, 0L, fixture.Dwo));

        Assert.Equal(1L, fixture.Host.CurrentRow);
        Assert.Null(fixture.Observer.Single(EventId.Ondwnlbuttonclk).RowSwitchAttempted);
    }

    // ---------------------------------------------------------------------------------------------
    //  :L164-L173  ondwnchanging - THE SUBSCRIBER PROBE AND THE DROP-DOWN SEARCH FORWARD
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EditChanged_WithNoSubscriberTheBrokerIsNotTriggeredAtAll()
    {
        // :L166  if Eventful.of_IsSubscribed(EVT_EDITCHANGED) then ...
        //
        // THE PROBE IS PART OF THE CONTRACT, NOT AN OPTIMISATION (AAP 0.8.5 forbids calling it one). It
        // is observable: with no subscriber the broker's hooks do not fire and the topic's default
        // return value is never resolved.
        ChainFixture fixture = NewFixture();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed"));

        Assert.Equal(["Event EditChanged"], SemanticEvents(fixture));
        Assert.False(fixture.Observer.Single(EventId.Ondwnchanging).Dispatch.BrokerTriggerRan);
    }

    [Fact]
    public void EditChanged_WithASubscriberTheBrokerIsTriggeredAndCanVeto()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_EDITCHANGED);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed"));
        Assert.Equal(1, subscriber.Count);
        Assert.True(fixture.Observer.Single(EventId.Ondwnchanging).Dispatch.BrokerTriggerRan);

        subscriber.Answer = RetCode.PREVENT;
        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed"));
    }

    [Fact]
    public void EditChanged_ForwardsToDropDownSearchOnlyWhileItIsEnabled()
    {
        // :L169-L171  if DropdownSearch.#Enabled then DropdownSearch.Event OnEditChanged(row,dwo,data)
        ChainFixture fixture = NewFixture();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed"));
        Assert.Single(fixture.Services.DropDownSearch.EditChangedCalls);
        Assert.Equal("typed", fixture.Services.DropDownSearch.EditChangedCalls[0].Data);
        Assert.Equal(Row, fixture.Services.DropDownSearch.EditChangedCalls[0].Row);

        fixture.Services.DropDownSearch.Enabled = false;
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed again"));
        Assert.Single(fixture.Services.DropDownSearch.EditChangedCalls);
    }

    [Fact]
    public void EditChanged_ASemanticVetoStopsBothTheBrokerAndTheDropDownSearchForward()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_EDITCHANGED);
        fixture.Host.EditChangedHandler = (_, _, _) => RetCode.PREVENT;

        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "typed"));

        Assert.Equal(0, subscriber.Count);
        Assert.Empty(fixture.Services.DropDownSearch.EditChangedCalls);
    }

    // ---------------------------------------------------------------------------------------------
    //  :L176-L179  ondwnitemchangefocus
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ItemChangeFocus_HonoursItsOwnGateThenDelegatesThenTriggers()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ITEMFOCUSCHANGED);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnItemChangeFocus(Row, fixture.Dwo));
        Assert.Equal(["Event ItemFocusChanged"], SemanticEvents(fixture));
        Assert.Equal(1, subscriber.Count);

        // The broker result IS tested here [:L178].
        subscriber.Answer = RetCode.PREVENT;
        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnItemChangeFocus(Row, fixture.Dwo));
    }

    [Fact]
    public void ItemChangeFocus_TheGateIsItsOwnBitAndNotTheRowFocusOne()
    {
        // :L176 guards on EID_ITEMFOCUSCHANGE, a DIFFERENT bit from :L124's EID_ROWFOCUSCHANGE.
        ChainFixture fixture = NewFixture(EventGate.EID_ITEMFOCUSCHANGE);
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ITEMFOCUSCHANGED);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnItemChangeFocus(Row, fixture.Dwo));
        Assert.Empty(SemanticEvents(fixture));
        Assert.Equal(0, subscriber.Count);
        Assert.True(fixture.Observer.Last.Dispatch.GatedOut);

        // Row focus is unaffected by the item-focus bit.
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(Row));
        Assert.Equal(["Event RowFocusChanged"], SemanticEvents(fixture));
    }

    // ---------------------------------------------------------------------------------------------
    //  :L399-L400  ondwnsetfocus  and  :L387-L393  ondwnkillfocus
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void SetFocus_TriggersTheBrokerThenReturnsTheSemanticResult()
    {
        // :L399-L400  the broker FIRST, then `return Event GetFocus()`. The order matters: the semantic
        // event's value is the result, and the broker's is not.
        ChainFixture fixture = NewFixture();
        List<string> order = [];
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_GETFOCUS);
        subscriber.Answer = RetCode.PREVENT;
        subscriber.InHandler = () => order.Add("broker");
        fixture.Host.GetFocusHandler = () =>
        {
            order.Add("semantic");
            return 7L;
        };

        Assert.Equal(7L, fixture.Chain.OnDwnSetFocus());

        // THE ORDERED LIST, AND IT IS THE REVERSE OF EVERY OTHER RAW EVENT IN THIS FILE. Nine of the
        // thirteen ask the semantic event FIRST and let it veto; `ondwnsetfocus` triggers the broker
        // first and then RETURNS the semantic result [:L399-L400]. Asserting only that both ran would
        // pass on the far more common shape.
        Assert.Equal(["broker", "semantic"], order);

        Assert.Equal(1, subscriber.Count);
        Assert.Equal(["Event GetFocus"], SemanticEvents(fixture));

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnsetfocus);
        Assert.Equal(7L, outcome.ReturnValue);

        // The broker's PREVENT is reported but does NOT become the result - :L399 discards it.
        Assert.Equal(VetoResult.PreventOnce, outcome.BrokerVeto);
    }

    [Fact]
    public void KillFocus_QueuesTheDeferredAcceptThenTriggersThenReturnsTheSemanticResult()
    {
        // :L387-L393  THREE STEPS IN ONE FIXED ORDER, and this test asserts the ORDER ONLY:
        //
        //   :L388-L390  if Not _bDoItemChange then Post _of_PostAcceptText()   <- 1, the queueing
        //   :L391       Eventful.of_Trigger(EVT_LOSEFOCUS)                     <- 2, result DISCARDED
        //   :L392       return Event LoseFocus()                               <- 3, THIS is the result
        //
        // WHAT THE CONTINUATION THEN DOES [:L553-L557] IS NOT ASSERTED HERE. That body reads the four
        // cross-event fields, so it belongs to ValidationSessionTests.cs; this file's business is where
        // in the kill-focus sequence the queueing happens, not what is queued.
        ChainFixture fixture = NewFixture();
        List<string> order = [];
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_LOSEFOCUS);

        // Observed NON-DESTRUCTIVELY at each later step, so reading the order does not drain the queue.
        subscriber.InHandler = () => order.Add(
            fixture.Session.DeferredAcceptPending ? "broker(after queueing)" : "broker(before queueing)");
        fixture.Host.LoseFocusHandler = () =>
        {
            order.Add(fixture.Session.DeferredAcceptPending ? "semantic(after queueing)" : "semantic(before queueing)");
            return 3L;
        };

        Assert.Equal(3L, fixture.Chain.OnDwnKillFocus());

        // THE ORDERED LIST. The broker saw the accept already queued and the semantic event ran after
        // the broker - which fixes all three positions relative to one another.
        Assert.Equal(["broker(after queueing)", "semantic(after queueing)"], order);

        Assert.Equal(1, subscriber.Count);
        Assert.Equal(["Event LoseFocus"], SemanticEvents(fixture));

        // :L391's result is DISCARDED and :L392's is returned, so a vetoing subscriber cannot change the
        // answer - the asymmetry `ondwnsetfocus` shares [:L399-L400].
        subscriber.Answer = RetCode.PREVENT;
        Assert.Equal(3L, fixture.Chain.OnDwnKillFocus());

        DataWindowEventOutcome outcome = fixture.Observer.Outcomes[0];
        Assert.Equal(EventId.Ondwnkillfocus, outcome.EventId);
        Assert.True(outcome.DeferredAcceptQueued);
        Assert.Equal(3L, outcome.ReturnValue);

        // The continuation is drained by the HOST through the session's own queue - not by a second
        // mechanism, and not by a message pump.
        Assert.NotNull(fixture.Chain.DrainDeferredAccept());
    }

    [Fact]
    public void KillFocus_DoesNotQueueTheDeferredAcceptWhileAnItemChangeIsInFlight()
    {
        // :L388  if Not _bDoItemChange then Post ...
        ChainFixture fixture = NewFixture();

        // Hooked on ItemChanged rather than on the host's OnDoItemChange seam, because the CHAIN
        // overrides `ondoitemchange` [:L256-L293] and forwards to the ancestry `ItemChanged` event at
        // :L292 - so the host's own OnDoItemChange is never reached through the chain at all.
        fixture.Host.ItemChangedHandler = (_, _, _) =>
        {
            // Inside the item-change handler the flag is set [:L192-L196], so the kill-focus that
            // arrives here must NOT queue an accept - accepting text mid-change would re-enter.
            Assert.Equal(3L, fixture.Chain.OnDwnKillFocus());
            return 0L;
        };
        fixture.Host.LoseFocusHandler = () => 3L;

        _ = fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original");

        Assert.False(fixture.Observer.Single(EventId.Ondwnkillfocus).DeferredAcceptQueued);
        Assert.Null(fixture.Chain.DrainDeferredAccept());
    }

    // ---------------------------------------------------------------------------------------------
    //  :L182-L253  ondwnitemchange   and   :L322-L385  ondwnitemvalidationerror - BOTH DELEGATED
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ItemChange_IsDelegatedToTheItemChangeProtocolAndReportsItsAlphabet()
    {
        // The chain does NOT reimplement the {0,1,2,3} protocol - Domain/ItemChangeProtocol.cs owns it.
        // With the value unchanged the default arm coerces and then FORCIBLY returns 2 [:L243-L250].
        ChainFixture fixture = NewFixture();

        long result = fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original");

        Assert.Equal((long)ItemChangeResult.RestoreAndRejectText, result);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnitemchange);
        Assert.Equal(ItemChangeResult.RestoreAndRejectText, outcome.ItemChangeResult);
        Assert.NotNull(outcome.State);
    }

    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(2L, 2L)]
    [InlineData(3L, 1L)]
    public void ItemChange_CarriesTheFourValueAlphabetIncludingThreeCollapsingIntoOne(
        long handlerResult,
        long expected)
    {
        // CASE 3 REWRITES ITSELF TO 1 [:L239-L241] - the value is kept, focus does not move, and the
        // result becomes 1. That collapse is legacy behaviour and is asserted rather than corrected.
        //
        // The theory carries LONGS rather than the ItemChangeResult enum because the enum is internal
        // while a theory method is public - and coercing the enum into a public signature just to name
        // the values would put the wire alphabet where the raw alphabet belongs. The mapping back to the
        // enum is asserted inside, where accessibility is not a constraint.
        ChainFixture fixture = NewFixture();
        fixture.Host.ItemChangedHandler = (_, _, _) => handlerResult;

        long actual = fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original");

        Assert.Equal(expected, actual);
        Assert.Equal(
            ItemChangeProtocol.Classify(expected),
            fixture.Observer.Single(EventId.Ondwnitemchange).ItemChangeResult);
    }

    [Fact]
    public void ItemChange_TheGateSuppressesTheWholeChainIncludingColumnExpression()
    {
        // THE EID_ITEMCHANGE COUPLING [:L42, :L110]: disabling item-change also suppresses
        // column-expression evaluation, because the expression service is reached only from inside
        // `ondoitemchanged`, which the gated-out item-change never gets to.
        ChainFixture fixture = NewFixture(EventGate.EID_ITEMCHANGE);

        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original"));

        Assert.Empty(SemanticEvents(fixture));
        Assert.Empty(fixture.Services.ColumnExp.ItemChangedCalls);
        Assert.True(fixture.Observer.Single(EventId.Ondwnitemchange).Dispatch.GatedOut);
    }

    [Fact]
    public void ItemValidationError_IsDelegatedToTheValidationSession()
    {
        // The chain does not reimplement :L322-L385 either; Domain/ValidationSession.cs owns it,
        // because four of its steps read and write the cross-event state at :L89-L96.
        ChainFixture fixture = NewFixture();

        long result = fixture.Chain.OnDwnItemValidationError(Row, fixture.Dwo, string.Empty);

        // EMPTY DATA CLEARS THE GUARD AND RETURNS 3 [:L376-L380], which is neither of the two values a
        // reader would predict from the non-empty path.
        Assert.Equal((long)ItemChangeResult.KeepValueNoFocusMove, result);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnitemvalidationerror);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, outcome.ItemChangeResult);
        Assert.NotNull(outcome.State);
    }

    [Fact]
    public void ItemValidationError_ConsumesTheCodeTheItemChangeEventStashed()
    {
        // THE REASON THE ITEM-CHANGE GROUP CANNOT BE REORDERED AT ALL. The validation-error handler READS AND CLEARS
        // the code its predecessor stashed [:L331-L332] and PRE-SETS its own result from it
        // [:L338-L340], so its behaviour is a FUNCTION of the previous event's return value. Reordering
        // is not undesirable here, it is impossible.
        ChainFixture fixture = NewFixture();
        fixture.Host.ItemChangedHandler = (_, _, _) => 1L;

        Assert.Equal(
            (long)ItemChangeResult.TriggerValidationError,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original"));
        Assert.Equal(1L, fixture.Session.CaptureState().RawItemChangeRetCode);

        Assert.Equal(
            (long)ItemChangeResult.TriggerValidationError,
            fixture.Chain.OnDwnItemValidationError(Row, fixture.Dwo, "rejected"));

        // CLEARED, so a second validation error cannot read a stale predecessor's code.
        Assert.Equal(0L, fixture.Session.CaptureState().RawItemChangeRetCode);
    }

    // ---------------------------------------------------------------------------------------------
    //  BRIEF PHASE 4 - :L256-L293 ondoitemchange AND :L295-L320 ondoitemchanged
    //  -------------------------------------------------------------------------------------------
    //  THE STRICT THREE-STEP SEQUENCE AT :L313-L319, ACROSS ALL FOUR GATE COMBINATIONS.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DoItemChange_IsEffectivelyOneLineAndForwardsToTheAncestryItemChangedEvent()
    {
        // :L292  return Event ItemChanged(row,dwo,data). The dormant commented byte-length check at
        // :L280-L290 and the dead assignment at :L278 belong to ItemChangeProtocol.cs and are not
        // duplicated here.
        ChainFixture fixture = NewFixture();
        fixture.Host.ItemChangedHandler = (row, _, data) =>
        {
            Assert.Equal(Row, row);
            Assert.Equal("typed", data);
            return 3L;
        };

        Assert.Equal(3L, fixture.Chain.OnDoItemChange(Row, fixture.Dwo, "typed"));

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondoitemchange);
        Assert.Equal(3L, outcome.ReturnValue);
        Assert.Equal(ItemChangeResult.KeepValueNoFocusMove, outcome.ItemChangeResult);
    }

    [Fact]
    public void DoItemChanged_RunsColumnExpressionThenBrokerThenSemanticEvent_InThatExactOrder()
    {
        // :L313-L319  THE STRICT THREE-STEP ORDER, AND IT IS CONTRACT:
        //   1  :L313-L315  if ColumnExp.#Enabled then ColumnExp.Event OnItemChanged(row,dwo)
        //   2  :L316-L318  if Eventful.of_IsSubscribed(EVT_ITEMCHANGED) then trigger it
        //   3  :L319       Event OnItemChanged(row,dwo)     - UNCONDITIONAL
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ITEMCHANGED);
        subscriber.InHandler = () => fixture.Log.Record("Broker:0-itemchanged");
        fixture.Log.Clear();

        fixture.Chain.OnDoItemChanged(Row, fixture.Dwo);

        Assert.Equal(
            ["OnItemChanged:ColumnExp", "Broker:0-itemchanged", "Semantic:OnItemChanged"],
            fixture.Log.Entries);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondoitemchanged);
        Assert.True(outcome.Dispatch.ColumnExpressionHandlerRan);
        Assert.True(outcome.Dispatch.BrokerTriggerRan);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);
        Assert.Equal("0-itemchanged", outcome.Topic!.LegacyName);
    }

    [Theory]
    [MemberData(nameof(DoItemChangedGateMatrix))]
    public void DoItemChanged_AcrossAllFourGateCombinationsKeepsTheOrderAndNeverSkipsStepThree(
        bool columnExpressionEnabled,
        bool subscriberPresent)
    {
        // THE FULL CROSS-PRODUCT OF THE TWO INDEPENDENT GATES AT :L313-L319. Step one is gated on
        // `ColumnExp.#Enabled` [:L313], step two on `Eventful.of_IsSubscribed(EVT_ITEMCHANGED)` [:L316]
        // and step three on NOTHING [:L319] - so there are exactly four reachable sequences, the
        // semantic event is the last entry in all four, and the two present steps keep their relative
        // order in the one row where both run.
        ChainFixture fixture = NewFixture();
        fixture.Services.ColumnExp.Enabled = columnExpressionEnabled;

        if (subscriberPresent)
        {
            RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ITEMCHANGED);
            subscriber.InHandler = () => fixture.Log.Record("Broker:0-itemchanged");
        }

        fixture.Log.Clear();

        fixture.Chain.OnDoItemChanged(Row, fixture.Dwo);

        // BUILT IN THE ORACLE'S OWN ORDER, then compared as an ORDERED LIST. A set comparison would pass
        // on a chain that ran the broker before the expression service.
        List<string> expected = [];

        if (columnExpressionEnabled)
        {
            expected.Add("OnItemChanged:ColumnExp");
        }

        if (subscriberPresent)
        {
            expected.Add("Broker:0-itemchanged");
        }

        expected.Add("Semantic:OnItemChanged");

        Assert.Equal(expected, fixture.Log.Entries);

        // The report agrees with the log on which of the three steps ran, and step three ALWAYS did.
        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondoitemchanged);
        Assert.Equal(columnExpressionEnabled, outcome.Dispatch.ColumnExpressionHandlerRan);
        Assert.Equal(subscriberPresent, outcome.Dispatch.BrokerTriggerRan);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);
        Assert.Equal("Semantic:OnItemChanged", fixture.Log.Entries[^1]);

        // AND THE EXPRESSION SERVICE SAW THE ARGUMENTS, or did not, according to its own gate - so a
        // disabled service is not merely unreported, it is genuinely not called [:L313-L315].
        Assert.Equal(
            columnExpressionEnabled ? 1 : 0,
            fixture.Services.ColumnExp.ItemChangedCalls.Count);
    }

    // ---------------------------------------------------------------------------------------------
    //  BRIEF PHASE 3 (CONTINUED) - THE TRI-VALUED VETO, CARRIED END TO END AND NEVER DOWNGRADED
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void PreventDeepIsCarriedAsPreventDeepAndIsNeverDowngradedToPreventOnce()
    {
        // AAP 0.6.1.2: flattening the veto to a boolean "would silently convert a deep prevention into a
        // shallow one". The report therefore distinguishes 1 from 2 - while CONTROL FLOW still tests
        // `= 1` exactly as the oracle does at :L116, so a handler answering 2 does NOT stop the
        // dispatch. Widening the prevent condition from one value to two would be a behaviour change.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONDOWN);
        subscriber.Answer = (long)VetoResult.PreventDeep;

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonDown(1L, 2L, Row, fixture.Dwo));

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnrbuttondown);
        Assert.Equal(VetoResult.PreventDeep, outcome.BrokerVeto);
        Assert.NotEqual(VetoResult.PreventOnce, outcome.BrokerVeto);
    }

    [Theory]
    [InlineData(0L, VetoResult.Continue)]
    [InlineData(1L, VetoResult.PreventOnce)]
    [InlineData(2L, VetoResult.PreventDeep)]
    [InlineData(3L, VetoResult.Continue)]
    [InlineData(-1L, VetoResult.Continue)]
    public void TheVetoProjectionKeepsAllThreeStatesAndCollapsesNothingElse(
        long value,
        VetoResult expected) =>
        Assert.Equal(expected, DataWindowEventChain.ToVetoResult(value));

    [Fact]
    public void TheOneTheChainBranchesOnIsTheSameOneTheReturnCodeAlgebraCallsPrevented()
    {
        // TWO NUMERIC ALPHABETS SHARE THE NUMERAL 1, AND THIS PINS THEM TO THE SAME VALUE. The oracle
        // spells every prevent test `= 1` [:L115, :L116, :L125, :L131, :L132, :L136, :L139, :L145,
        // :L148, :L164, :L167, :L177, :L178, :L395], and that 1 is RetCode.PREVENT
        // [ws_objects/pfw.shared.pbl.src/retcode.sru:L42]. The veto projection reads the SAME numeral as
        // PreventOnce. If either ever moved independently, a subscriber's veto would stop being a veto.
        Assert.Equal(1L, RetCode.PREVENT);
        Assert.Equal((long)VetoResult.PreventOnce, RetCode.PREVENT);
        Assert.Equal(VetoResult.PreventOnce, DataWindowEventChain.ToVetoResult(RetCode.PREVENT));
        Assert.True(Predicates.IsPrevented(RetCode.PREVENT));

        // AND THE TRI-STATE HOLE IS PRESERVED RATHER THAN CLOSED: a prevention reads as a SUCCESS in the
        // return-code algebra [issucceeded.srf:L11-L13 tests `>= 0`], which is a documented legacy defect
        // (constraint C-B) and is exactly why this file branches on `== RetCode.PREVENT` and never on
        // `IsFailed`. Asserted here so the two alphabets cannot be quietly conflated.
        Assert.True(Predicates.IsSucceeded(RetCode.PREVENT));
        Assert.False(Predicates.IsFailed(RetCode.PREVENT));

        // PreventDeep (2) is NOT prevented in the return-code algebra at all - it is a broker state, not
        // a return code - which is the clearest statement that the two alphabets are separate.
        Assert.False(Predicates.IsPrevented((long)VetoResult.PreventDeep));
        Assert.Equal(VetoResult.PreventDeep, DataWindowEventChain.ToVetoResult(2L));
    }

    [Fact]
    public void ANullBrokerAnswerIsNeverCollapsedIntoZero()
    {
        // AAP 0.4.5.4: null must never be collapsed to zero, because that would turn "no answer" into
        // "continue" and, in the return-code algebra, "neither succeeded nor failed" into "succeeded".
        Assert.Null(DataWindowEventChain.ToLegacyNumber(null));
        Assert.Equal(VetoResult.Continue, DataWindowEventChain.ToVetoResult(null));

        // A non-numeric answer NARROWS to null with a defined outcome rather than widening to a guess.
        Assert.Null(DataWindowEventChain.ToLegacyNumber("not a number"));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1m));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1.0d));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(1.5d));
    }

    // ==============================================================================================
    //  BRIEF PHASE 5b - THE Filter AND DeleteRow OVERRIDES.  SUCCESS IS 1, NOT 0.
    // ==============================================================================================

    [Fact]
    public void Filter_NotifiesRowSelectOnlyWhenTheBaseSucceededWithOne()
    {
        // :L406-L413.  SUCCESS IS 1 [:L407] - this is a DataWindow integer, NOT the return-code algebra
        // where 0 means success. Mapping it onto RetCode would invert the condition.
        ChainFixture fixture = NewFixture();

        Assert.Equal(1, fixture.Chain.Filter());
        Assert.Equal(1, fixture.Services.RowSelect.FilteredCount);
        Assert.Contains("FilterCore", fixture.Host.CallLog.Members);

        // AND THE NOTIFICATION CARRIES NO COUNTS. :L409 is `RowSelect.Event OnFiltered()` with an EMPTY
        // argument list - the row and filtered counts belong to a DIFFERENT event, `onddsfiltered(row,
        // dwo, rowcount, filteredcount)` at :L28, which the drop-down search service raises. Adding
        // count parameters here because they "obviously belong" would be a new feature (constraint C-B),
        // so their absence is asserted rather than assumed.
        Assert.Empty(
            typeof(IDataWindowRowSelectService)
                .GetMethod(nameof(IDataWindowRowSelectService.OnFiltered))!
                .GetParameters());
        Assert.Equal(
            4,
            typeof(DataWindowEventChain)
                .GetMethod(nameof(DataWindowEventChain.OnDdsFiltered))!
                .GetParameters()
                .Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void Filter_DoesNotNotifyRowSelectForAnyOtherBaseResult(int baseResult)
    {
        // Zero is INCLUDED deliberately: it is the value a reader trained on RetCode would expect to
        // mean success, and here it must not fire the notification.
        ChainFixture fixture = NewFixture();
        fixture.Host.FilterResult = baseResult;

        Assert.Equal(baseResult, fixture.Chain.Filter());
        Assert.Equal(0, fixture.Services.RowSelect.FilteredCount);
    }

    [Fact]
    public void Filter_DoesNotNotifyADisabledRowSelectService()
    {
        // :L408  if RowSelect.#Enabled then ...
        ChainFixture fixture = NewFixture();
        fixture.Services.RowSelect.Enabled = false;

        Assert.Equal(1, fixture.Chain.Filter());
        Assert.Equal(0, fixture.Services.RowSelect.FilteredCount);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-5L)]
    [InlineData(2L)]
    public void DeleteRow_RejectsAnOutOfRangeRowWithoutCallingTheBase(long row)
    {
        // :L421  if r < 0 or r > RowCount() then return -1.  One row exists, so 2 is out of range.
        ChainFixture fixture = NewFixture();

        Assert.Equal(-1, fixture.Chain.DeleteRow(row));
        Assert.DoesNotContain("DeleteRowCore", fixture.Host.CallLog.Members);
    }

    [Fact]
    public void DeleteRow_TreatsZeroAsTheCurrentRowRatherThanAsOutOfRange()
    {
        // :L423-L426.  ZERO IS LEGAL and means "the current row" - the guard rejects NEGATIVE, not zero.
        // The base is then called with the RESOLVED row [:L431], never with the argument.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 2L;

        Assert.Equal(1, fixture.Chain.DeleteRow(0L));

        Assert.Contains("DeleteRowCore[2]", fixture.Host.CallLog.Descriptions);
        Assert.DoesNotContain("DeleteRowCore[0]", fixture.Host.CallLog.Descriptions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2)]
    public void DeleteRow_ShortCircuitsOnAnyBaseResultOtherThanOne(int baseResult)
    {
        // :L432  if rtCode <> 1 then return rtCode - so no notification and no redraw follow.
        ChainFixture fixture = NewFixture();
        fixture.Host.DeleteRowResult = baseResult;

        Assert.Equal(baseResult, fixture.Chain.DeleteRow(Row));

        Assert.Empty(SemanticEvents(fixture));
        Assert.Null(fixture.Host.RedrawEnabled);
        Assert.DoesNotContain(EventId.Ondwnrowchange, fixture.Observer.EventIds);
    }

    [Fact]
    public void DeleteRow_NotifiesWithRowZeroWhenTheDataWindowBecomesEmpty()
    {
        // :L434-L435  if RowCount() = 0 then Event OnDwnRowChange(0).  Row 0 is the oracle's "there is
        // no current row" notification and NOT a sentinel error.
        ChainFixture fixture = NewFixture();

        Assert.Equal(1, fixture.Chain.DeleteRow(Row));

        Assert.Contains("Event RowFocusChanged[0]", fixture.Host.CallLog.Descriptions);
        Assert.Contains(EventId.Ondwnrowchange, fixture.Observer.EventIds);

        // AND THE REDRAW IS NOT ISSUED, because :L441 also requires RowCount() > 0. The last row went
        // and nothing remains to repaint.
        Assert.Null(fixture.Host.RedrawEnabled);
    }

    [Fact]
    public void DeleteRow_NotifiesWithTheDeletedRowWhenItWasTheCurrentOneAndRowsRemain()
    {
        // :L436-L437  elseif bIsCurrentRow and nRow = GetRow() then Event OnDwnRowChange(nRow). The
        // GetRow() RE-READ AFTER the delete is not redundant - the runtime may have moved the cursor.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = Row;

        Assert.Equal(1, fixture.Chain.DeleteRow(Row));

        Assert.Contains("Event RowFocusChanged[1]", fixture.Host.CallLog.Descriptions);

        // Row 1 was not the LAST row, so no redraw - which is what keeps the two branches separable.
        Assert.Null(fixture.Host.RedrawEnabled);
    }

    [Fact]
    public void DeleteRow_DoesNotNotifyWhenTheDeletedRowWasNotTheCurrentOne()
    {
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.AddRow("third");
        fixture.Host.CurrentRow = 1L;

        Assert.Equal(1, fixture.Chain.DeleteRow(2L));

        Assert.Empty(SemanticEvents(fixture));
        Assert.DoesNotContain(EventId.Ondwnrowchange, fixture.Observer.EventIds);
    }

    [Fact]
    public void DeleteRow_RedrawsWhenTheLastRowWentAndRowsRemain()
    {
        // :L440-L443  //使DETAIL区的颜色刷新..    "refresh the DETAIL band's colour"
        //             if bIsLastRow and RowCount() > 0 then SetRedraw(true)
        //
        // A DELIBERATE BOUNDARY JUDGEMENT THAT DOES NOT BREACH CONSTRAINT C-D. The comment mentions
        // colour, but the call takes no geometry, no DPI value, no font metric and no window handle -
        // it is one boolean on the host contract. Dropping it would remove an observable call the oracle
        // makes; implementing the colour refresh would be DesignSystem work.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;

        Assert.Equal(1, fixture.Chain.DeleteRow(2L));

        Assert.True(fixture.Host.RedrawEnabled);
        Assert.Contains("SetRedraw[true]", fixture.Host.CallLog.Descriptions);
    }

    // ==============================================================================================
    //  BRIEF PHASE 5a - THE SEVEN SUBSCRIPTION PASS-THROUGHS
    // ==============================================================================================

    [Fact]
    public void ExactlySevenSubscriptionOverloadsExist_OneOnAndSixOff()
    {
        // :L102-L108 prototypes them and :L448-L467 bodies them: exactly seven - of_on/3, and of_off in
        // arities 3, 2, 1, 0, 1 and 2. SIX of_off OVERLOADS, NOT FIVE: the two one-argument forms take
        // DIFFERENT types [:L457 string against :L463 powerobject] and so do the two two-argument forms
        // [:L454 (string,obj) against :L466 (obj,evt)].
        System.Reflection.MethodInfo[] members = typeof(DataWindowEventChain).GetMethods();

        Assert.Single(members, member => member.Name == "On");
        Assert.Equal(6, members.Count(member => member.Name == "Off"));

        // THE PARAMETER SHAPES, PINNED. Counting the overloads is not enough: two forms of the same
        // arity that differed only in parameter ORDER would still count six while dispatching a caller's
        // argument to the wrong slot. `&` is the by-ref marker every shape carries, because the oracle
        // declares all seven with `readonly` parameters [:L102-L108] and AAP 0.4.5.2 maps `readonly` onto
        // `in` - so the marker's presence is itself part of the ported signature and is asserted below.
        Assert.Equal(
            ["String&,Object&,String&", "Object&,String&", "String&,Object&", "Object&", "String&", string.Empty],
            members
                .Where(member => member.Name == "Off")
                .Select(ParameterShape)
                .OrderByDescending(shape => shape.Length)
                .ThenBy(shape => shape, StringComparer.Ordinal));

        // :L448  of_on(name, obj, evtname) - one form only, and its three parameters in that order.
        Assert.Equal("String&,Object&,String&", ParameterShape(members.Single(member => member.Name == "On")));

        // :L451  of_off(name, obj, evtname) - the three-argument form, matching of_on's own order.
        Assert.Single(
            members,
            member => member.Name == "Off" && ParameterShape(member) == "String&,Object&,String&");

        // EVERY PARAMETER OF ALL SEVEN IS `in`, NOT `ref` AND NOT `out`. A `ref` would let a callee
        // rewrite the caller's topic name, which `readonly` in the oracle forbids.
        foreach (System.Reflection.MethodInfo member in
            members.Where(candidate => candidate.Name is "On" or "Off"))
        {
            Assert.All(
                member.GetParameters(),
                parameter =>
                {
                    Assert.True(parameter.ParameterType.IsByRef);
                    Assert.True(parameter.IsIn);
                    Assert.False(parameter.IsOut);
                });
        }
    }

    /// <summary>The ordered, comma-joined parameter-type names of one member.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The shape, empty for a member declaring no parameters.</returns>
    private static string ParameterShape(System.Reflection.MethodInfo member) =>
        string.Join(',', member.GetParameters().Select(parameter => parameter.ParameterType.Name));

    [Fact]
    public void On_IsAPurePassThroughToTheBroker()
    {
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = new("clicked");

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_CLICKED,
                subscriber,
                nameof(RecordingSubscriber.OnThreeArguments)));

        // Registered under the topic EXACTLY as given - no suffix, no transformation, no validation of
        // its own. The broker owns all three.
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));

        _ = fixture.Chain.OnDwnLButtonClk(1L, 2L, Row, fixture.Dwo);
        Assert.Equal(1, subscriber.Count);
    }

    [Fact]
    public void OffWithNameTargetAndHandler_RemovesExactlyThatSubscription()
    {
        // :L451  of_off(name, obj, evtname)
        ChainFixture fixture = NewFixture();
        RecordingSubscriber clicked = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        _ = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.Off(
                DataWindowEventChain.EVT_CLICKED,
                clicked,
                nameof(RecordingSubscriber.OnThreeArguments)));

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
    }

    [Fact]
    public void OffWithNameAndTarget_RemovesThatTargetsSubscriptionToThatName()
    {
        // :L454  of_off(name, obj)
        ChainFixture fixture = NewFixture();
        RecordingSubscriber clicked = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        _ = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.Off(DataWindowEventChain.EVT_CLICKED, clicked));

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
    }

    [Fact]
    public void OffWithNameOnly_RemovesEverySubscriptionToThatName()
    {
        // :L457  of_off(name)
        ChainFixture fixture = NewFixture();
        _ = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        _ = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        _ = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.Off(DataWindowEventChain.EVT_CLICKED));

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
    }

    [Fact]
    public void OffWithNothing_RemovesEverySubscription()
    {
        // :L460  of_off()  - the arity teardown uses FIRST, before destroying anything.
        ChainFixture fixture = NewFixture();
        _ = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        _ = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.Off());

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
    }

    [Fact]
    public void OffWithTargetOnly_RemovesEverySubscriptionThatTargetHolds()
    {
        // :L463  of_off(obj).  THE CALL-SITE HAZARD IS REAL AND RECORDED RATHER THAN DESIGNED AWAY: in
        // PowerScript `string` and `powerobject` are unrelated, so of_off(name,obj) and of_off(obj,evt)
        // are unambiguous; in C# a string IS an object, so a caller must type the target as `object` to
        // choose. EventBroker's own seven Unsubscribe overloads carry the identical shape for the
        // identical reason, so this is a precedented consequence of the type mapping.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber shared = new("shared");
        RecordingSubscriber other = new("other");

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_CLICKED,
                shared,
                nameof(RecordingSubscriber.OnThreeArguments)));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_RBUTTONUP,
                shared,
                nameof(RecordingSubscriber.OnThreeArguments)));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_GETFOCUS,
                other,
                nameof(RecordingSubscriber.OnThreeArguments)));

        Assert.Equal(RetCode.OK, fixture.Chain.Off((object)shared));

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_GETFOCUS));
    }

    [Fact]
    public void OffWithTargetAndHandler_RemovesEverySubscriptionThatPairHolds()
    {
        // :L466  of_off(obj, evtname)
        ChainFixture fixture = NewFixture();
        RecordingSubscriber shared = new("shared");

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_CLICKED,
                shared,
                nameof(RecordingSubscriber.OnThreeArguments)));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_RBUTTONUP,
                shared,
                nameof(RecordingSubscriber.OnNoArguments)));

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.Off((object)shared, nameof(RecordingSubscriber.OnThreeArguments)));

        Assert.False(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED));
        Assert.True(fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_RBUTTONUP));
    }

    // ==============================================================================================
    //  THE THREE GATE DELEGATIONS                                          se_cst_dw.sru:L469-L536
    // ==============================================================================================

    [Fact]
    public void TheGateDelegationsRouteToTheSessionAndAreObservableInDispatch()
    {
        ChainFixture fixture = NewFixture();

        Assert.False(fixture.Chain.IsEventDisabled(EventGate.EID_ITEMCHANGE));

        // :L489 returns `long` while :L513 returns `integer`, and :L521's own comment block still says
        // "Returns: long". THAT INCONSISTENCY IS THE ORACLE'S and is reproduced rather than harmonised.
        Assert.Equal(RetCode.OK, fixture.Chain.DisableEvent(EventGate.EID_ITEMCHANGE));
        Assert.True(fixture.Chain.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.True(fixture.Session.IsEventDisabled(EventGate.EID_ITEMCHANGE));

        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original"));

        Assert.Equal((int)RetCode.OK, fixture.Chain.EnableEvent(EventGate.EID_ITEMCHANGE));
        Assert.False(fixture.Chain.IsEventDisabled(EventGate.EID_ITEMCHANGE));

        Assert.Equal(
            (long)ItemChangeResult.RestoreAndRejectText,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original"));
    }

    [Fact]
    public void TheGateDelegationsRejectAnEmptyBitmask()
    {
        ChainFixture fixture = NewFixture();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, fixture.Chain.DisableEvent(0u));
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, fixture.Chain.EnableEvent(0u));
    }

    [Fact]
    public void TheGateBitsAreComposableAndIndependent()
    {
        // :L41-L43  EID_ROWFOCUSCHANGE = 1, EID_ITEMFOCUSCHANGE = 2, EID_ITEMCHANGE = 4 - powers of two
        // so they compose, which is what makes one disable call able to cover two events.
        ChainFixture fixture = NewFixture();

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.DisableEvent(EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMCHANGE));

        Assert.True(fixture.Chain.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
        Assert.False(fixture.Chain.IsEventDisabled(EventGate.EID_ITEMFOCUSCHANGE));
        Assert.True(fixture.Chain.IsEventDisabled(EventGate.EID_ITEMCHANGE));
    }

    // ==============================================================================================
    //  BRIEF PHASE 5c - THE TWO DIVERGENT LIFECYCLE ORDERS, AND TEARDOWN
    // ==============================================================================================

    [Fact]
    public void CreationOrderIsContextMenuRowSelectColumnSortDropDownSearchColumnExp()
    {
        // :L570-L574.  Position three is ColumnSort and position four is DropDownSearch.
        ChainFixture fixture = NewFixture();

        Assert.Equal(
            [
                DataWindowAttachedServiceKind.ContextMenu,
                DataWindowAttachedServiceKind.RowSelect,
                DataWindowAttachedServiceKind.ColumnSort,
                DataWindowAttachedServiceKind.DropDownSearch,
                DataWindowAttachedServiceKind.ColumnExp
            ],
            fixture.Log.Sequence("Create"));

        Assert.Equal(
            DataWindowEventChain.ServiceCreationOrder,
            fixture.Log.Sequence("Create"));
    }

    [Fact]
    public void InitialisationOrderSwapsPositionsThreeAndFour()
    {
        // :L576-L580.  DropDownSearch is initialised THIRD though created FOURTH, and ColumnSort is
        // initialised FOURTH though created THIRD.
        ChainFixture fixture = NewFixture();

        Assert.Equal(
            [
                DataWindowAttachedServiceKind.ContextMenu,
                DataWindowAttachedServiceKind.RowSelect,
                DataWindowAttachedServiceKind.DropDownSearch,
                DataWindowAttachedServiceKind.ColumnSort,
                DataWindowAttachedServiceKind.ColumnExp
            ],
            fixture.Log.Sequence("OnInit"));

        Assert.Equal(
            DataWindowEventChain.ServiceInitializationOrder,
            fixture.Log.Sequence("OnInit"));
    }

    [Fact]
    public void TheTwoLifecycleOrdersDiverge_AndTheDivergenceMustNotBeHarmonised()
    {
        // BOTH ORDERS, IN ONE TEST, BECAUSE THE FACT UNDER TEST IS THE RELATION BETWEEN THEM.
        //
        //   :L570-L574   ContextMenu, RowSelect, ColumnSort,      DropDownSearch, ColumnExp   <- CREATE
        //   :L576-L580   ContextMenu, RowSelect, DropDownSearch,  ColumnSort,     ColumnExp   <- OnInit
        //                                        ^^^^^^^^^^^^^^   ^^^^^^^^^^      POSITIONS 3 AND 4 SWAP
        //
        // THIS DIVERGENCE IS LEGACY BEHAVIOUR AND MUST NOT BE HARMONISED (constraint C-B). It reads like
        // a slip and it may well have been one, but the oracle is the specification and AAP 0.8.1 governs
        // the judgement: a documented oddity beats a silent correction. This test is what makes
        // "consistency-fixing" the two into one order FAIL THE BUILD rather than pass unnoticed.
        ChainFixture fixture = NewFixture();

        Assert.Equal(
            [
                DataWindowAttachedServiceKind.ContextMenu,
                DataWindowAttachedServiceKind.RowSelect,
                DataWindowAttachedServiceKind.ColumnSort,
                DataWindowAttachedServiceKind.DropDownSearch,
                DataWindowAttachedServiceKind.ColumnExp
            ],
            fixture.Log.Sequence("Create"));

        Assert.Equal(
            [
                DataWindowAttachedServiceKind.ContextMenu,
                DataWindowAttachedServiceKind.RowSelect,
                DataWindowAttachedServiceKind.DropDownSearch,
                DataWindowAttachedServiceKind.ColumnSort,
                DataWindowAttachedServiceKind.ColumnExp
            ],
            fixture.Log.Sequence("OnInit"));

        Assert.NotEqual(fixture.Log.Sequence("Create"), fixture.Log.Sequence("OnInit"));
        Assert.NotEqual(
            DataWindowEventChain.ServiceCreationOrder,
            DataWindowEventChain.ServiceInitializationOrder);

        // POSITIONS 1, 2 AND 5 AGREE; ONLY 3 AND 4 SWAP. Stated positionally so the assertion pins WHERE
        // the divergence is rather than merely that there is one - a different permutation would be a
        // different behaviour and would pass a bare NotEqual.
        Assert.Equal(fixture.Log.Sequence("Create")[0], fixture.Log.Sequence("OnInit")[0]);
        Assert.Equal(fixture.Log.Sequence("Create")[1], fixture.Log.Sequence("OnInit")[1]);
        Assert.Equal(fixture.Log.Sequence("Create")[2], fixture.Log.Sequence("OnInit")[3]);
        Assert.Equal(fixture.Log.Sequence("Create")[3], fixture.Log.Sequence("OnInit")[2]);
        Assert.Equal(fixture.Log.Sequence("Create")[4], fixture.Log.Sequence("OnInit")[4]);

        // And they are permutations of one another, so the divergence is an ORDER difference and not a
        // missing or extra service.
        Assert.Equal(
            [.. DataWindowEventChain.ServiceCreationOrder.Order()],
            DataWindowEventChain.ServiceInitializationOrder.Order());
    }

    [Fact]
    public void EveryServiceIsInitialisedWithTheChainItself()
    {
        // :L576-L580 pass `this`. The chain IS the host, which is why it derives from
        // DataWindowServiceHost rather than holding one.
        ChainFixture fixture = NewFixture();

        foreach (FakeAttachedService service in new FakeAttachedService[]
        {
            fixture.Services.ContextMenu,
            fixture.Services.RowSelect,
            fixture.Services.ColumnSort,
            fixture.Services.DropDownSearch,
            fixture.Services.ColumnExp
        })
        {
            Assert.Same(fixture.Chain, service.InitializedWith);
        }
    }

    [Fact]
    public void ConstructionAndTeardownTouchNoHostMemberAtAll()
    {
        // THE DROPPED SUPER-CALLS, OBSERVED AS AN ABSENCE. :L570 opens with
        // `call super::onpreconstructor` and :L583 with `call super::ondestructor`, which in the legacy
        // run se_cst_datawindow's THEME REGISTRATION and UNREGISTRATION
        // [se_cst_datawindow.sru:L82-L87 and :L89-L90, calling ThemeManager().of_RegisterControl and
        // of_UnregisterControl]. Under constraint C-D those have NO ANALOGUE and are DROPPED - a
        // documented capability gap belonging to the reserved /v1/design/** Gateway extension point
        // (AAP 0.4.4), and emphatically not something to stub. Nothing theming-related may therefore
        // appear, which is exactly what an empty call log asserts.
        ChainFixture fixture = NewFixture();
        fixture.Host.RecordsReads = true;

        Assert.Empty(fixture.Host.CallLog.Records);

        fixture.Chain.Teardown();

        Assert.Empty(fixture.Host.CallLog.Records);
    }

    [Fact]
    public void Teardown_UnsubscribesBeforeDestroyingAnyService()
    {
        // :L583 THEN :L584-L588. Unsubscribing first is what prevents a dispatch landing in a service
        // that has already been destroyed - so the order is not cosmetic.
        ChainFixture fixture = NewFixture();
        _ = Subscribe(fixture, DataWindowEventChain.EVT_CLICKED);
        bool registryWasClearAtFirstDestroy = false;

        fixture.Services.ContextMenu.OnDisposing = () =>
            registryWasClearAtFirstDestroy =
                !fixture.Chain.Eventful.IsSubscribed(DataWindowEventChain.EVT_CLICKED);

        fixture.Chain.Teardown();

        Assert.True(registryWasClearAtFirstDestroy);
    }

    [Fact]
    public void Teardown_DestroysInCreationOrderAndIsIdempotent()
    {
        // :L584-L588 destroy in CREATION order - which is a third distinct sequence to keep straight,
        // and the one that matches creation rather than initialisation.
        ChainFixture fixture = NewFixture();
        fixture.Log.Clear();

        fixture.Chain.Teardown();

        Assert.Equal(DataWindowEventChain.ServiceCreationOrder, fixture.Log.Sequence("Dispose"));
        Assert.False(fixture.Chain.IsAlive);

        // IDEMPOTENT. PowerBuilder would fault on a second Destroy of the same reference, but nothing in
        // the oracle destroys a control twice, so idempotence removes a failure mode without removing a
        // behaviour.
        fixture.Chain.Teardown();

        Assert.Equal(5, fixture.Log.Sequence("Dispose").Count);
        Assert.Equal(1, fixture.Services.ContextMenu.DisposeCount);
    }

    [Theory]
    [InlineData(DataWindowAttachedServiceKind.ContextMenu)]
    [InlineData(DataWindowAttachedServiceKind.RowSelect)]
    [InlineData(DataWindowAttachedServiceKind.ColumnSort)]
    [InlineData(DataWindowAttachedServiceKind.DropDownSearch)]
    [InlineData(DataWindowAttachedServiceKind.ColumnExp)]
    public void AFactoryThatYieldsNothingIsAStructuralFaultAndFailsFast(
        DataWindowAttachedServiceKind kind)
    {
        // The oracle's `Create` CANNOT yield nothing, so a null here is a wiring fault rather than a
        // runtime condition the oracle can reach. Fail fast rather than degrade (AAP 0.1.4): softening
        // this into a warning-and-continue would be a behavioural change dressed as robustness.
        FakeAttachedServiceFactory services = new() { ReturnNullFor = kind };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new FakeEventChain(NewSession(), services));

        Assert.Contains("se_cst_dw.sru:L570-L574", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheConstructorRejectsAMissingSessionOrFactory()
    {
        Assert.Throws<ArgumentNullException>(
            () => new FakeEventChain(null!, new FakeAttachedServiceFactory()));
        Assert.Throws<ArgumentNullException>(() => new FakeEventChain(NewSession(), null!));
    }

    [Fact]
    public void TheBrokerIsCreatedBeforeAnyServiceSoOnInitCanReadIt()
    {
        // :L562 creates the broker, :L570 onwards create the services, :L576 onwards initialise them -
        // and every one of the five reads #Eventful during OnInit, so the order is load-bearing.
        ChainFixture fixture = NewFixture();

        Assert.NotNull(fixture.Chain.Eventful);
        Assert.Same(fixture.Chain.Eventful, fixture.Host.Eventful);
    }

    // ==============================================================================================
    //  THE NESTED BROKER SUBCLASS AND ITS onprepare                        se_cst_dw.sru:L591-L607
    // ==============================================================================================

    [Fact]
    public void TheNestedBrokerDefaultsItsReturnValueToZeroRatherThanNull()
    {
        // :L596  of_SetDefaultReturnValue(0). The dormant alternative at :L594-L595 - `//long nvl` and
        // `//SetNull(nvl)` - would have made the default NULL, and it is commented out in the oracle, so
        // THE LEGACY DOES NOT DO IT. Carried inert, never revived (constraint C-B).
        ChainFixture fixture = NewFixture();

        object? answer = fixture.Chain.Eventful.Trigger(DataWindowEventChain.EVT_CLICKED);

        Assert.NotNull(answer);
        Assert.Equal(0L, DataWindowEventChain.ToLegacyNumber(answer));
    }

    [Fact]
    public void OnPrepare_InjectsTheParentAsArgumentOneForAnOrdinarySubscriber()
    {
        // :L604-L605  invoker.SetArg(1,parent) then argPassed = 1. ONE-BASED SLOT (AAP 0.4.5.4), so the
        // trigger's own payload starts at slot TWO.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGED);

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.Equal(1, subscriber.Count);
        Assert.NotNull(subscriber.LastThreeSlots);
        Assert.Same(fixture.Chain, subscriber.LastThreeSlots![0]);
        Assert.Equal(7L, subscriber.LastThreeSlots[1]);
    }

    [Fact]
    public void OnPrepare_DoesNothingForAHandlerThatDeclaresNoParameters()
    {
        // :L599  if argCount < 1 then return 0 - there is no slot to inject into. The handler still runs;
        // the hook returns 0 on every path, so it never skips a subscriber.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = new("no-args");

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                subscriber,
                nameof(RecordingSubscriber.OnNoArguments)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.Equal(1, subscriber.Count);
        Assert.Null(subscriber.LastThreeSlots);
    }

    [Fact]
    public void OnPrepare_DoesNotInjectWhenTheTargetIsTheHostItself()
    {
        // :L600  if target = parent then return 0 - the host does not need to be told which host raised
        // the event, so the payload occupies slot one.
        ChainFixture fixture = NewFixture();

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                fixture.Chain,
                nameof(FakeEventChain.OnBrokerProbe)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.NotNull(fixture.Chain.ProbeSlots);
        Assert.Equal(7L, fixture.Chain.ProbeSlots![0]);
        Assert.NotSame(fixture.Chain, fixture.Chain.ProbeSlots[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnPrepare_DoesNotInjectForTheColumnExpressionOrDropDownSearchServiceByIDENTITY(
        bool columnExpression)
    {
        // :L601 and :L602 are IDENTITY comparisons, and they are REDUNDANT with :L603's ancestry walk in
        // production - every attached service IS an n_cst_dwsvc descendant. THE REDUNDANCY IS PRESERVED
        // (constraint C-B): the two operations are not the same, they are checked FIRST, and removing
        // them would be a behaviour change. This test proves they are INDEPENDENTLY REACHABLE, because
        // the service doubles here deliberately do NOT derive from DataWindowServiceBase - so an
        // exclusion observed for one of them can only have come from the identity check.
        ChainFixture fixture = NewFixture();
        FakeAttachedService target = columnExpression
            ? fixture.Services.ColumnExp
            : fixture.Services.DropDownSearch;

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                target,
                nameof(FakeAttachedService.OnBrokerProbe)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.NotNull(target.LastThreeSlots);
        Assert.Equal(7L, target.LastThreeSlots![0]);

        // THE PREMISE, CHECKED INDEPENDENTLY OF THE PRODUCTION ANCESTRY WALK. Asserting it with the same
        // helper the chain uses at :L603 would make a broken walk agree with itself and mask exactly the
        // bug this test exists to catch, so the BCL's own assignability test is used instead.
        Assert.IsNotAssignableFrom<DataWindowServiceBase>(target);
    }

    [Fact]
    public void OnPrepare_DoesNotInjectForAnyOtherAttachedServiceByANCESTRY()
    {
        // :L603  if IsAncestor(target,"n_cst_dwsvc") then return 0. Driven with a target that derives
        // from DataWindowServiceBase and is NEITHER of the two identity-matched services, so the
        // exclusion can only have come from the ancestry walk. All five real services subscribe
        // themselves to this broker [n_cst_dwsvc_contextmenu.sru:L1441-L1442,
        // n_cst_dwsvc_rowselect.sru:L274-L276, n_cst_dwsvc_columnsort.sru:L442-L444,
        // n_cst_dwsvc_dropdownsearch.sru:L508-L510, n_cst_dwsvc_columnexp.sru:L2429], so this arm is
        // heavily exercised in production rather than theoretical.
        ChainFixture fixture = NewFixture();
        RecordingServiceSubscriber target = new();

        // The premise, checked with the BCL rather than with the production walk, for the reason given
        // on the identity-exclusion theory above.
        Assert.IsAssignableFrom<DataWindowServiceBase>(target);
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                target,
                nameof(RecordingServiceSubscriber.OnThreeArguments)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.Single(target.Dispatches);
        Assert.NotNull(target.LastThreeSlots);
        Assert.Equal(7L, target.LastThreeSlots![0]);
    }

    [Fact]
    public void OnPrepare_InjectsForAServiceDoubleThatMatchesNeitherExclusion()
    {
        // THE CONTROL CASE, AND IT IS WHAT PINS THE EXCLUSION SET AT EXACTLY FOUR CHECKS. The context
        // menu double is an attached service, yet it matches neither identity check and does not derive
        // from DataWindowServiceBase - so it IS injected. A hook that excluded "any attached service"
        // rather than the four things the oracle actually tests would fail here.
        ChainFixture fixture = NewFixture();
        FakeContextMenuService target = fixture.Services.ContextMenu;

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                target,
                nameof(FakeAttachedService.OnBrokerProbe)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.NotNull(target.LastThreeSlots);
        Assert.Same(fixture.Chain, target.LastThreeSlots![0]);
        Assert.Equal(7L, target.LastThreeSlots[1]);
    }

    [Fact]
    public void OnPrepare_NeverSkipsASubscriberOnAnyPath()
    {
        // :L599-L606 return 0 from ALL SIX exits. RetCode.PREVENT here would mean "do not invoke this
        // handler at all", so a single wrong exit would silently mute a subscriber.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber ordinary = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGED);
        FakeContextMenuService service = fixture.Services.ContextMenu;
        RecordingServiceSubscriber descendant = new();

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                service,
                nameof(FakeAttachedService.OnBrokerProbe)));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                descendant,
                nameof(RecordingServiceSubscriber.OnThreeArguments)));
        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(
                DataWindowEventChain.EVT_ROWFOCUSCHANGED,
                fixture.Chain,
                nameof(FakeEventChain.OnBrokerProbe)));

        _ = fixture.Chain.OnDwnRowChange(7L);

        Assert.Equal(1, ordinary.Count);
        Assert.NotNull(service.LastThreeSlots);
        Assert.Single(descendant.Dispatches);
        Assert.NotNull(fixture.Chain.ProbeSlots);
    }

    // ==============================================================================================
    //  PER-WORKFLOW CONFORMANCE
    //  --------------------------------------------------------------------------------------------
    //  AAP 0.8.1 requires event-ordering conformance tests PER WORKFLOW, not merely per event, and the
    //  distinction is not pedantic: every assertion above measures one event's own shape, and none of
    //  them would catch a chain that got each event right but emitted them in the wrong RELATIVE order.
    //  Each test below walks one complete workflow and asserts the whole observed sequence.
    //
    //  TWO WORKFLOWS ARE PARTLY INVISIBLE TO THE OBSERVER BY DESIGN, and that is stated rather than
    //  worked around. Seven of the nine semantic events are OUTBOUND questions with empty default
    //  bodies, so an unhandled one reports nothing - there was no dispatch to report. Their ordering is
    //  therefore asserted through the participants that ARE observable: the services, the broker and the
    //  raw events that bracket them.
    //
    //  WHERE THE SCENARIOS COME FROM. The sequences below are drawn from the legacy test and demo
    //  windows, which are REFERENCE ONLY under constraint C-C - read for realism, never ported, never
    //  edited:
    //      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L250-L252
    //          the real subscribe shape - of_On(name, parent, handlerName) with a CASE-SENSITIVE event
    //          name and a case-INSENSITIVE handler name - which is the shape Subscribe uses here.
    //      ws_objects/pfw.tests.pbl.src/w_test_eventful.srw:L240,L247
    //          a handler's arity need NOT match the trigger's, but the ORDER must, and surplus arguments
    //          are DISCARDED - which is why RecordingSubscriber declares three different arities.
    //      ws_objects/pfw.tests.pbl.src/w_test_dwsvc_contextmenu.srw:L97
    //          a real oncontextmenu(row, dwo, mid) handler resolving the menu text FROM mid, which is
    //          why the context-menu workflow below passes a non-zero identifier to the second event and
    //          none at all to the first.
    //      ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw
    //          54 lines and NO event handler at all, so it contributes no sequence - recorded so the
    //          absence is a checked finding rather than an oversight.
    // ==============================================================================================

    [Fact]
    public void Workflow_ItemChangeAndValidation_NestsTheInnerEventsAndCompletesThemInsideOut()
    {
        // THE HIGHEST-RISK WORKFLOW IN THE FILE:
        //   ondwnitemchange -> ondoitemchange -> ondoitemchanged -> ondwnitemvalidationerror
        //                                                       -> ondwnkillfocus
        //
        // The nested shape is visible in the ORDER OF THE REPORTS: the two inner events complete before
        // their enclosing one, so their outcomes arrive first even though the enclosing event holds the
        // LOWER token. That is the direct consequence of :L207 firing an event from inside another, and
        // it is why the outcome is pushed to an observer rather than stored in a slot.
        ChainFixture fixture = NewFixture();

        _ = fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original");
        _ = fixture.Chain.OnDwnItemValidationError(Row, fixture.Dwo, "bad");
        _ = fixture.Chain.OnDwnKillFocus();

        Assert.Equal(
            [
                EventId.Ondoitemchange,
                EventId.Ondoitemchanged,
                EventId.Ondwnitemchange,
                EventId.Ondwnitemvalidationerror,
                EventId.Ondwnkillfocus
            ],
            fixture.Observer.EventIds);

        // THE ENCLOSING EVENT HOLDS THE LOWER TOKEN, WHICH IS HOW THE NESTING IS READ BACK. Tokens are
        // issued on ENTRY and reports are made on EXIT, so ENTRY order and COMPLETION order genuinely
        // differ for this group - and that difference is the observable fingerprint of :L207 firing an
        // event from inside another one. Both orders are asserted, because either alone would be
        // consistent with a flat chain.
        Assert.Equal(1L, fixture.Observer.Single(EventId.Ondwnitemchange).Sequence);
        Assert.Equal(2L, fixture.Observer.Single(EventId.Ondoitemchange).Sequence);
        Assert.Equal(3L, fixture.Observer.Single(EventId.Ondoitemchanged).Sequence);
        Assert.Equal(4L, fixture.Observer.Single(EventId.Ondwnitemvalidationerror).Sequence);
        Assert.Equal(5L, fixture.Observer.Single(EventId.Ondwnkillfocus).Sequence);

        // ENTRY ORDER, as an ORDERED LIST, and it is NOT the completion order asserted above.
        Assert.Equal(
            [
                EventId.Ondwnitemchange,
                EventId.Ondoitemchange,
                EventId.Ondoitemchanged,
                EventId.Ondwnitemvalidationerror,
                EventId.Ondwnkillfocus
            ],
            fixture.Observer.Outcomes
                .OrderBy(outcome => outcome.Sequence)
                .Select(outcome => outcome.EventId));

        Assert.NotEqual(
            [.. fixture.Observer.EventIds],
            fixture.Observer.Outcomes
                .OrderBy(outcome => outcome.Sequence)
                .Select(outcome => outcome.EventId));
    }

    [Fact]
    public void Workflow_FocusClickAndRowSwitch_EmitsTheFiveRawEventsInOrderAndWithoutNesting()
    {
        // THE FLAT WORKFLOW - five raw events, none of which raises another:
        //   ondwnsetfocus -> ondwnlbuttonclk -> ondwnrowchanging -> ondwnrowchange -> ondwnkillfocus
        //
        // NOTHING HERE RAISES ANYTHING ELSE, which is what makes this the control case for the nested
        // item-change group above: entry order and completion order coincide. The kill-focus tail still
        // reads the session's item-change flag and may enqueue a continuation that RE-ENTERS the
        // item-change protocol [:L388-L390], so its position at the end of the sequence is load-bearing
        // even here - which is why the ordered list below includes it rather than filtering it out.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnSetFocus());
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnLButtonClk(1L, 2L, 2L, fixture.Dwo));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChanging(1L, 2L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(2L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnKillFocus());

        Assert.Equal(
            [
                EventId.Ondwnsetfocus,
                EventId.Ondwnlbuttonclk,
                EventId.Ondwnrowchanging,
                EventId.Ondwnrowchange,
                EventId.Ondwnkillfocus
            ],
            fixture.Observer.EventIds);

        Assert.Equal(
            [1L, 2L, 3L, 4L, 5L],
            fixture.Observer.Outcomes.Select(outcome => outcome.Sequence));

        // NO NESTING IN THIS WORKFLOW, so ENTRY order IS COMPLETION order - the exact opposite of the
        // item-change group above, where the two differ. That contrast is what makes each of the two
        // workflows evidence about the other: a chain that nested here, or that flattened there, would
        // fail one of the two.
        Assert.Equal(
            [.. fixture.Observer.EventIds],
            fixture.Observer.Outcomes
                .OrderBy(outcome => outcome.Sequence)
                .Select(outcome => outcome.EventId));
    }

    [Fact]
    public void Workflow_ContextMenu_BracketsTheSemanticPairBetweenTheRawButtonEvents()
    {
        // THE CONTEXT-MENU WORKFLOW:
        //   ondwnrbuttondown -> [ oninitcontextmenu -> oncontextmenu ] -> ondwnrbuttonup
        //
        // The two semantic events are raised BY THE SERVICE on its host [n_cst_dwsvc_contextmenu.sru:L147
        // then :L194], not by the chain, and initialisation must COMPLETE before the menu identifier the
        // second one carries can mean anything - which is why the pair cannot be reordered.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber down = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONDOWN);
        RecordingSubscriber up = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonDown(10L, 20L, Row, fixture.Dwo));
        Assert.Equal(0L, fixture.Chain.OnInitContextMenu(Row, fixture.Dwo));
        Assert.Equal(0L, fixture.Chain.OnContextMenu(Row, fixture.Dwo, 42L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonUp(10L, 20L, Row, fixture.Dwo));

        Assert.Equal(1, down.Count);
        Assert.Equal(1, up.Count);

        // The two raw events bracket the pair and are the only observable halves, because the two
        // semantic ones are unhandled outbound questions.
        Assert.Equal(
            [EventId.Ondwnrbuttondown, EventId.Ondwnrbuttonup],
            fixture.Observer.EventIds);
        Assert.Equal([1L, 2L], fixture.Observer.Outcomes.Select(outcome => outcome.Sequence));

        // The init event carries NO menu identifier and the context event carries one [:L11 against
        // :L12], which is the signature-level reason the pair reads init-then-show and never the reverse.
        Assert.Equal(
            2,
            typeof(DataWindowEventChain)
                .GetMethod(nameof(DataWindowEventChain.OnInitContextMenu))!
                .GetParameters()
                .Length);
        Assert.Equal(
            3,
            typeof(DataWindowEventChain)
                .GetMethod(nameof(DataWindowEventChain.OnContextMenu))!
                .GetParameters()
                .Length);
    }

    [Fact]
    public void Workflow_DropDownSearch_RunsSemanticThenBrokerThenTheServiceForward()
    {
        // THE DROP-DOWN SEARCH WORKFLOW:
        //   ondwnchanging -> [ semantic EditChanged -> broker -> DropdownSearch.OnEditChanged ]
        //                 -> onddsgetfilter -> onddsfiltered
        //
        // :L164-L171 fixes the order of the three participants inside `ondwnchanging`, and the filter
        // pair that follows cannot be reordered either: the first produces its result through a
        // `ref string` out-parameter [:L13], so the caller blocks on it.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_EDITCHANGED);
        subscriber.InHandler = () => fixture.Log.Record("Broker:1-editchanged");
        fixture.Log.Clear();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnChanging(Row, fixture.Dwo, "Sh"));

        // THE BROKER PRECEDES THE SERVICE FORWARD. Swapping them would let a subscriber's veto arrive
        // after the drop-down search had already acted on the keystroke.
        Assert.Equal(["Broker:1-editchanged", "OnEditChanged:DropDownSearch"], fixture.Log.Entries);
        Assert.Equal(["Event EditChanged"], SemanticEvents(fixture));

        string filter = string.Empty;
        fixture.Chain.OnDdsGetFilter(Row, fixture.Dwo, "Sh", ref filter);
        fixture.Chain.OnDdsFiltered(Row, fixture.Dwo, 1L, 2L);

        // Only the raw event is observable; the filter pair is the deferred presentational half's raise,
        // and both are unhandled outbound questions here.
        Assert.Equal([EventId.Ondwnchanging], fixture.Observer.EventIds);

        // The filter is produced through the `ref string` out-parameter at :L13 and the counts arrive
        // afterwards at :L28, so the get-filter call must COMPLETE before the filtered notification can
        // report anything - the ordering follows from the signatures, not from a convention.
        Assert.Equal(string.Empty, filter);
        Assert.Equal(
            typeof(void),
            typeof(DataWindowEventChain)
                .GetMethod(nameof(DataWindowEventChain.OnDdsFiltered))!
                .ReturnType);
    }

    [Fact]
    public void Workflow_DeleteTheCurrentRow_RaisesTheRawRowChangeEventFromInsideTheOverride()
    {
        // THE DELETE WORKFLOW:  DeleteRow -> ondwnrowchange (raised by the override at :L435 / :L437)
        //
        // The override raises the RAW event directly rather than the semantic one, so the whole raw
        // chain - gate, semantic delegation and broker trigger - runs from inside a delete. A consumer
        // therefore sees a row-focus notification it did not ask for, and that is the oracle's behaviour.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGED);

        Assert.Equal(1, fixture.Chain.DeleteRow(Row));

        Assert.Equal([EventId.Ondwnrowchange], fixture.Observer.EventIds);
        Assert.Equal(1, subscriber.Count);
        Assert.Equal(["Event RowFocusChanged"], SemanticEvents(fixture));

        // AND THE GATE STILL APPLIES INSIDE THE DELETE. Disabling row focus change suppresses the
        // notification without affecting the delete itself.
        ChainFixture gated = NewFixture(EventGate.EID_ROWFOCUSCHANGE);
        RecordingSubscriber silent = Subscribe(gated, DataWindowEventChain.EVT_ROWFOCUSCHANGED);

        Assert.Equal(1, gated.Chain.DeleteRow(Row));

        Assert.Equal(0, silent.Count);
        Assert.True(gated.Observer.Single(EventId.Ondwnrowchange).Dispatch.GatedOut);
    }

    // ==============================================================================================
    //  THE REMAINING BRANCHES - VETO PATHS, THE NUMERIC COERCION MATRIX, AND THE EXTENSION POINT
    // ==============================================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClickAndDoubleClick_ASemanticVetoShortCircuitsBeforeTheBroker(bool doubleClick)
    {
        // :L137 and :L146. The semantic event is asked FIRST, and its veto ends the dispatch before the
        // liveness guard and the broker edge are ever reached.
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(
            fixture,
            doubleClick ? DataWindowEventChain.EVT_DOUBLECLICKED : DataWindowEventChain.EVT_CLICKED);
        fixture.Host.DoubleClickedHandler = (_, _, _, _) => RetCode.PREVENT;
        fixture.Host.ClickedHandler = (_, _, _, _) => RetCode.PREVENT;

        long result = doubleClick
            ? fixture.Chain.OnDwnLButtonDblClk(1L, 2L, Row, fixture.Dwo)
            : fixture.Chain.OnDwnLButtonClk(1L, 2L, Row, fixture.Dwo);

        Assert.Equal(RetCode.PREVENT, result);
        Assert.Equal(0, subscriber.Count);

        DataWindowEventOutcome outcome = fixture.Observer.Last;
        Assert.Equal(VetoResult.PreventOnce, outcome.SemanticVeto);
        Assert.True(outcome.Dispatch.SemanticHandlerRan);
        Assert.False(outcome.Dispatch.BrokerTriggerRan);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClickAndDoubleClick_ABrokerVetoIsHonoured(bool doubleClick)
    {
        // :L138-L140 and :L147-L149.
        ChainFixture fixture = NewFixture();
        fixture.Host.AddRow("second");
        fixture.Host.CurrentRow = 1L;
        RecordingSubscriber subscriber = Subscribe(
            fixture,
            doubleClick ? DataWindowEventChain.EVT_DOUBLECLICKED : DataWindowEventChain.EVT_CLICKED);
        subscriber.Answer = RetCode.PREVENT;

        long result = doubleClick
            ? fixture.Chain.OnDwnLButtonDblClk(1L, 2L, 2L, fixture.Dwo)
            : fixture.Chain.OnDwnLButtonClk(1L, 2L, 2L, fixture.Dwo);

        Assert.Equal(RetCode.PREVENT, result);

        DataWindowEventOutcome outcome = fixture.Observer.Last;
        Assert.Equal(VetoResult.PreventOnce, outcome.BrokerVeto);
        Assert.True(outcome.Dispatch.BrokerTriggerRan);

        // A CLICK VETOED BY THE BROKER NEVER REACHES THE ROW SWITCH, so the current row is untouched.
        Assert.Equal(1L, fixture.Host.CurrentRow);
    }

    [Fact]
    public void ItemChangeFocus_ASemanticVetoShortCircuitsBeforeTheBroker()
    {
        // :L177  if Event ItemFocusChanged(row,dwo) = 1 then return 1
        ChainFixture fixture = NewFixture();
        RecordingSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_ITEMFOCUSCHANGED);
        fixture.Host.ItemFocusChangedHandler = (_, _) => RetCode.PREVENT;

        Assert.Equal(RetCode.PREVENT, fixture.Chain.OnDwnItemChangeFocus(Row, fixture.Dwo));

        Assert.Equal(0, subscriber.Count);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Ondwnitemchangefocus);
        Assert.Equal(VetoResult.PreventOnce, outcome.SemanticVeto);
        Assert.False(outcome.Dispatch.BrokerTriggerRan);
    }

    [Fact]
    public void EveryNumericTypeThatCanReachAnAnyIsCoercedAndEverythingElseAnswersNull()
    {
        // PowerScript's `any` is the broker's return type, and a handler may answer with any numeric
        // width or with something that is not a number at all. Only an EXACT INTEGRAL value can equal the
        // integral prevent code, so a fractional one answers null for the same reason a string does.
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1L));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber((short)1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber((sbyte)1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber((byte)1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber((ushort)1));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1u));
        Assert.Equal(1L, DataWindowEventChain.ToLegacyNumber(1ul));
        Assert.Equal(2L, DataWindowEventChain.ToLegacyNumber(2m));
        Assert.Equal(2L, DataWindowEventChain.ToLegacyNumber(2.0d));
        Assert.Equal(2L, DataWindowEventChain.ToLegacyNumber(2.0f));

        // OUT OF RANGE AND FRACTIONAL BOTH NARROW TO NULL rather than widening to a guess.
        Assert.Null(DataWindowEventChain.ToLegacyNumber(ulong.MaxValue));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(1.5m));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(1.5f));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(double.NaN));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(double.PositiveInfinity));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(new object()));
        Assert.Null(DataWindowEventChain.ToLegacyNumber(true));
    }

    [Fact]
    public void TopicOfAnswersNullForANameThatIsNotOneOfTheTwelve()
    {
        // Case-SENSITIVE and ordinal, because the broker's own name comparison is - a case-insensitive
        // match here would find a topic the broker would not dispatch.
        Assert.Null(DataWindowEventChain.TopicOf("clicked "));
        Assert.Null(DataWindowEventChain.TopicOf("CLICKED"));
        Assert.Null(DataWindowEventChain.TopicOf("itemchanged"));
        Assert.NotNull(DataWindowEventChain.TopicOf("0-itemchanged"));
    }

    [Fact]
    public void TheChainExposesTheSessionItWasBuiltWithAndNeverReplacesIt()
    {
        // The oracle's four cross-event fields are INSTANCE FIELDS OF ONE CONTROL [:L89-L96] and have no
        // lifetime of their own, so the chain holds one session for its whole life. It is exposed so the
        // boundary can project the state onto contract C-03's EventResult without a second route into the
        // session registry.
        ChainFixture fixture = NewFixture();

        Assert.Same(fixture.Session, fixture.Chain.Session);

        _ = fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, "original");

        Assert.Same(fixture.Session, fixture.Chain.Session);
    }

    [Fact]
    public void AnOverrideThatProducesAFilterReportsItThroughProducedFilter()
    {
        // THE `ref string` RESULT, CARRIED ON THE OUTCOME. A return value would destroy the distinction
        // between "the handler assigned an empty filter", which means match nothing, and "the handler did
        // not touch the parameter", which means it declined to filter - so the outcome records what was
        // PRODUCED, and null there means nothing was.
        ChainFixture fixture = NewFixture();
        fixture.Chain.ProducedFilterFactory = () => "name like 'Sh%'";
        string filter = string.Empty;

        fixture.Chain.OnDdsGetFilter(Row, fixture.Dwo, "Sh", ref filter);

        Assert.Equal("name like 'Sh%'", filter);

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Onddsgetfilter);
        Assert.Equal("name like 'Sh%'", outcome.ProducedFilter);
        Assert.Equal(1L, outcome.Sequence);
    }

    [Fact]
    public void AnOverrideThatAnswersAMacroReportsItThroughAnyResult()
    {
        // THE INVERTED MACRO CHANNEL, CARRIED ON THE OUTCOME. `any` maps to object, so null is a
        // legitimate answer rather than a "no result" marker - which is why the field is object? and why
        // an unhandled macro reports NO OUTCOME AT ALL rather than an outcome carrying null.
        ChainFixture fixture = NewFixture();
        fixture.Chain.MacroResultFactory = () => 42m;

        Assert.Equal(42m, fixture.Chain.OnColumnExpInvokeMethod(Row, fixture.Dwo, "Invoke", ["6", "7"]));

        DataWindowEventOutcome outcome = fixture.Observer.Single(EventId.Oncolumnexpinvokemethod);
        Assert.Equal(42m, outcome.AnyResult);

        // A NULL ANSWER IS STILL AN ANSWER when a handler produced it.
        fixture.Chain.MacroResultFactory = () => null;
        Assert.Null(fixture.Chain.OnColumnExpInvokeMethod(Row, fixture.Dwo, "Invoke", []));
        Assert.Equal(2, fixture.Observer.Outcomes.Count);
        Assert.Null(fixture.Observer.Last.AnyResult);
    }
}
