// ==================================================================================================
//  EventGateTests - THE PARITY SUITE FOR THE COMPOSABLE EVENT-DISPATCH BITMASK
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  services/dataservices-service/PowerFramework.DataServices/Domain/EventGate.cs
//                   with Domain/ValidationSession.cs as the mask's STORAGE and
//                   Domain/DataWindowEventChain.cs as the four places the mask is READ.
//
//  BEHAVIOURAL ORACLE  ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru (616 lines), READ
//                   ONLY and never an edit target (constraint C-C). Every assertion below carries the
//                   ws_objects/** locator it was derived from, because nothing else in the repository
//                   can adjudicate a behavioural question: logfile.md stops at framework 3.0.7.2062
//                   (2022-04-14) while the commit history runs years later, and the two PowerBuilder
//                   build definitions ws_objects/pfw.pbl.src/project.srj and p_pfw.srj contradict
//                   each other and both name pfw.utility.imgcodec.pbl, which exists nowhere in the
//                   repository, so neither would build as written.
//
//                   The oracle text this suite pins, quoted once so the transcription can be diffed:
//
//                       //事件ID（支持组合）                                                  [:L40]
//                       constant long EID_ROWFOCUSCHANGE   = 1  //RowFocusChanging/Changed     [:L41]
//                       constant long EID_ITEMFOCUSCHANGE  = 2  //ItemFocusChanged             [:L42]
//                       constant long EID_ITEMCHANGE       = 4  //ItemChanged，禁用后将不会触发列
//                                                               //表达式计算！                [:L43]
//
//                       public function boolean of_iseventdisabled (readonly long evt)         [:L109]
//                       public function long    of_disableevent    (readonly long evt)         [:L110]
//                       public function integer of_enableevent     (readonly long evt)         [:L111]
//
//  RULES POSITION   review_rules returns "No user rules provided.", verified for this file. NO USER
//                   RULE GOVERNS IT, and none is invented. The enterprise baseline of AAP 0.7.2
//                   applies in their place - nullable enabled with warnings as errors, plain xunit
//                   assertions with no mocking or fluent-assertion package, and no secret of any kind
//                   anywhere in the file. The constraints that DO govern it are AAP 0.7.3's:
//
//                     C-B / G2  Preserve behaviour exactly. The three constant values, the mask
//                               combinability, the return-type asymmetry and the item-change to
//                               column-expression coupling are all LEGACY behaviour and are asserted
//                               AS-IS. A test that asserted the tidied version of any of them would
//                               invert this constraint - so each such assertion says out loud that
//                               normalising it is the violation, not the finding.
//                     C-K       Document every decision. Every assertion carries its legacy locator.
//                     C-H       80% line coverage per in-scope service. This file is the dedicated
//                               coverage of Domain/EventGate.cs: all three constants, all three
//                               operations, both arms of both guards, and every mutation path.
//
//                   THE TWO RETURN CODES THE OPERATIONS ANSWER WITH come from the second oracle this
//                   suite reads, ws_objects/pfw.shared.pbl.src/retcode.sru:
//
//                       Constant Long OK                 =  0                    [retcode.sru:L39]
//                       Constant Long E_INVALID_ARGUMENT = -3                    [retcode.sru:L46]
//
//                   Both are asserted through PowerFramework.Shared.Kernel.RetCode rather than as
//                   literals, so the algebra's own port is what is being trusted and any drift in it
//                   surfaces here as well as in its own suite.
//
//  NAMING CONSTRAINT, AND IT CUTS THE OTHER WAY IN THIS FILE. The repository root .editorconfig
//  carries CA1707 and IDE1006 suppressions keyed to three exact paths - Domain/EventGate.cs,
//  Domain/DataWindowEventChain.cs and Domain/ItemChangeProtocol.cs - and THIS FILE IS NOT ONE OF
//  THEM. So the SCREAMING_SNAKE constants are REFERENCED freely below, as EventGate.EID_ITEMCHANGE
//  and friends, and NOT ONE SCREAMING_SNAKE IDENTIFIER IS DECLARED here. Nothing in this file needs a
//  suppression, and none is present.
//
//  WHY THIS FILE DECLARES ITS OWN DOUBLES INSTEAD OF REUSING THE SIBLING SUITE'S (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  DataWindowEventChainTests.cs already carries a concrete chain double. It cannot serve here, for
//  two independent reasons, and both are structural rather than stylistic:
//
//    1. It is `sealed`, and its constructor demands the CONCRETE sibling factory type, so neither a
//       different column-expression double nor an override of OnColumnExpTrace can be introduced
//       through it.
//    2. Proving the coupling requires observing that NO TRACE PAYLOAD is produced. The chain's
//       OnColumnExpTrace [se_cst_dw.sru:L32] is inert by default and reports nothing to the observer,
//       so the only way to observe a trace at all is to override it - which point 1 forbids.
//
//  The doubles below therefore compose ONLY the dependencies this file is built against:
//  FakeDataWindowHost for the forty-eight member host contract, and the chain, session and gate from
//  Domain. That also makes the suite immune to a change in a sibling test file.
//
//  NO PERFORMANCE CLAIM IS MADE OR MEASURED ANYWHERE (AAP 0.8.5). The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment,
//  so none may be asserted. The only quantitative requirement in scope is the coverage gate.
// ==================================================================================================

using System.Reflection;

using Google.Protobuf.Reflection;

using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.Shared.Kernel;

using Xunit;

// PowerFramework.Contracts.DataServices.V1 ALSO declares a type spelled EventGate - the C-03 wire
// message carrying `int64 mask = 1`. The two are different artifacts with the same name, which is
// exactly the flat-namespace collision AAP 0.4.5.1 predicts, so both are aliased explicitly and
// neither is ever reached by an unqualified name.
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using EventGate = PowerFramework.DataServices.Domain.EventGate;
using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using WireEventGate = PowerFramework.Contracts.DataServices.V1.EventGate;

namespace PowerFramework.DataServices.Tests;

// ==================================================================================================
//  THE DOUBLES
// ==================================================================================================

/// <summary>
/// The shared, deliberately inert behaviour of the four attached-service doubles this suite does not
/// measure - the port of <c>Create</c> plus <c>Event OnInit(this)</c>
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L570-L580</c>).
/// </summary>
/// <remarks>
/// INERT ON PURPOSE. The gate never consults an attached service, so a double that did anything here
/// would add behaviour the assertions then measured instead of the gate's. <c>Enabled</c> is settable
/// because the chain reads <c>#Enabled</c> before forwarding to a service [<c>:L313</c>], and both arms
/// of that read have to be drivable.
/// </remarks>
internal abstract class GateInertService : IDataWindowAttachedService
{
    /// <inheritdoc/>
    public bool Enabled { get; set; } = true;

    /// <summary>The host this service was attached to, or <see langword="null"/> before attachment.</summary>
    internal DataWindowServiceHost? AttachedTo { get; private set; }

    /// <inheritdoc/>
    public void OnInit(DataWindowServiceHost dw)
    {
        ArgumentNullException.ThrowIfNull(dw);
        AttachedTo = dw;
    }
}

/// <summary>The context-menu double. Adds nothing; the interface declares nothing.</summary>
internal sealed class GateContextMenuService : GateInertService, IDataWindowContextMenuService;

/// <summary>The row-select double.</summary>
internal sealed class GateRowSelectService : GateInertService, IDataWindowRowSelectService
{
    /// <summary>Every <c>OnFiltered</c> forward. Must stay at zero for every gated-out dispatch.</summary>
    internal int FilteredCalls { get; private set; }

    /// <inheritdoc/>
    public void OnFiltered() => FilteredCalls++;
}

/// <summary>The column-sort double. Adds nothing; the interface declares nothing.</summary>
internal sealed class GateColumnSortService : GateInertService, IDataWindowColumnSortService;

/// <summary>The drop-down-search double.</summary>
internal sealed class GateDropDownSearchService : GateInertService, IDataWindowDropDownSearchService
{
    private readonly List<string> _editChanged = [];

    /// <summary>Every <c>OnEditChanged</c> forward [<c>se_cst_dw.sru:L169-L171</c>], in order.</summary>
    internal IReadOnlyList<string> EditChangedCalls => _editChanged;

    /// <inheritdoc/>
    public void OnEditChanged(long row, IDataWindowObject dwo, string data)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        _editChanged.Add(data);
    }
}

/// <summary>
/// The column-expression double - the participant the <c>EID_ITEMCHANGE</c> coupling is measured on.
/// </summary>
/// <remarks>
/// <para>
/// IT DOES TWO THINGS, AND BOTH ARE REQUIRED TO MAKE THE COUPLING OBSERVABLE. It records every
/// <c>OnItemChanged</c> forward, which is step one of <c>ondoitemchanged</c>
/// [<c>se_cst_dw.sru:L313-L315</c>], AND it raises the real <c>oncolumnexptrace</c> event
/// [<c>:L32</c>] on the chain it was attached to, which is what the engine itself does once per
/// evaluation after flattening its recursion stack
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L753-L755</c>].
/// </para>
/// <para>
/// Raising the trace through the PRODUCTION member rather than merely recording an intention is the
/// point: it means the "no trace payload" assertion is measured on the same path a real evaluation
/// would take, so a change that let expressions recalculate outside the gate would be caught by the
/// trace assertion even if the <c>OnItemChanged</c> count were somehow preserved.
/// </para>
/// </remarks>
internal sealed class GateColumnExpressionService
    : GateInertService, IDataWindowColumnExpressionService
{
    private readonly List<(long Row, string Column)> _itemChanged = [];

    /// <summary>
    /// Every item-changed forward [<c>se_cst_dw.sru:L313-L315</c>], in order. EMPTY is the assertion
    /// that the <c>EID_ITEMCHANGE</c> gate suppressed column-expression calculation.
    /// </summary>
    internal IReadOnlyList<(long Row, string Column)> ItemChangedCalls => _itemChanged;

    /// <summary>
    /// The trace text this double emits per evaluation. Fixed rather than computed, because the
    /// engine's own rendering is characterized elsewhere and this suite only needs the payload to be
    /// distinguishable from absence.
    /// </summary>
    internal const string TraceStack = "expsvc>calc";

    /// <summary>The expression text the emitted trace carries.</summary>
    internal const string TraceExpression = "1+1";

    /// <summary>The rendered value the emitted trace carries.</summary>
    internal const string TraceValue = "2";

    /// <inheritdoc/>
    public void OnItemChanged(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        _itemChanged.Add((row, dwo.Name));

        // The engine raises oncolumnexptrace once per evaluation [se_cst_dw.sru:L32,
        // n_cst_dwsvc_columnexp.sru:L753-L758]. Reaching it through the chain means the trace travels
        // the production path, so its ABSENCE is evidence about the production path too.
        if (AttachedTo is GateEventChain chain)
        {
            chain.OnColumnExpTrace(row, dwo, TraceStack, TraceExpression, TraceValue);
        }
    }
}

/// <summary>
/// The factory double. Creates each attached service once and remembers it, so an assertion reaches
/// the instance the chain is actually holding rather than a look-alike.
/// </summary>
/// <remarks>
/// The chain's constructor is fail-fast on a null service (AAP 0.1.4), so every member answers a real
/// instance. Nothing here refuses, because refusal is the sibling suite's subject and not this one's.
/// </remarks>
internal sealed class GateServiceFactory : IDataWindowAttachedServiceFactory
{
    /// <summary>The context-menu instance handed to the chain.</summary>
    internal GateContextMenuService ContextMenu { get; } = new();

    /// <summary>The row-select instance handed to the chain.</summary>
    internal GateRowSelectService RowSelect { get; } = new();

    /// <summary>The column-sort instance handed to the chain.</summary>
    internal GateColumnSortService ColumnSort { get; } = new();

    /// <summary>The drop-down-search instance handed to the chain.</summary>
    internal GateDropDownSearchService DropDownSearch { get; } = new();

    /// <summary>The column-expression instance handed to the chain - the coupling's subject.</summary>
    internal GateColumnExpressionService ColumnExp { get; } = new();

    /// <inheritdoc/>
    public IDataWindowContextMenuService CreateContextMenu() => ContextMenu;

    /// <inheritdoc/>
    public IDataWindowRowSelectService CreateRowSelect() => RowSelect;

    /// <inheritdoc/>
    public IDataWindowColumnSortService CreateColumnSort() => ColumnSort;

    /// <inheritdoc/>
    public IDataWindowDropDownSearchService CreateDropDownSearch() => DropDownSearch;

    /// <inheritdoc/>
    public IDataWindowColumnExpressionService CreateColumnExp() => ColumnExp;
}

/// <summary>
/// Records every <see cref="DataWindowEventOutcome"/> the chain pushes, in dispatch order.
/// </summary>
/// <remarks>
/// A LIST AND NOT A LAST-OUTCOME SLOT, because <c>se_cst_dw.sru:L207</c> raises an event from inside
/// another event and a single slot would be overwritten by the inner dispatch before the outer one
/// could be read. For the gated-out cases the list is the direct evidence of what did NOT happen: a
/// gated dispatch reports exactly one outcome carrying <c>GatedOut</c>, and no outcome at all for any
/// downstream event.
/// </remarks>
internal sealed class GateEventObserver : IDataWindowEventObserver
{
    private readonly List<DataWindowEventOutcome> _outcomes = [];

    /// <summary>Every outcome, in dispatch order.</summary>
    internal IReadOnlyList<DataWindowEventOutcome> Outcomes => _outcomes;

    /// <summary>The event identifiers, in dispatch order.</summary>
    internal IReadOnlyList<EventId> EventIds => [.. _outcomes.Select(outcome => outcome.EventId)];

    /// <summary>The single outcome for one event identifier.</summary>
    /// <param name="eventId">The event identifier.</param>
    /// <returns>The outcome.</returns>
    internal DataWindowEventOutcome Single(EventId eventId) =>
        _outcomes.Single(outcome => outcome.EventId == eventId);

    /// <summary>Whether any outcome was recorded for one event identifier.</summary>
    /// <param name="eventId">The event identifier.</param>
    /// <returns><see langword="true"/> when at least one was recorded.</returns>
    internal bool Saw(EventId eventId) => _outcomes.Any(outcome => outcome.EventId == eventId);

    /// <summary>Forgets every recorded outcome.</summary>
    internal void Clear() => _outcomes.Clear();

    /// <inheritdoc/>
    public void OnEventDispatched(DataWindowEventOutcome outcome) => _outcomes.Add(outcome);
}

/// <summary>
/// A broker subscriber that counts its dispatches - the second half of every "reaches nothing"
/// assertion, alongside the host's call log.
/// </summary>
/// <remarks>
/// BOTH HALVES ARE NEEDED. A gated-out handler must reach neither the semantic event NOR the broker
/// topic [<c>se_cst_dw.sru:L124-L127</c>, <c>:L130-L133</c>, <c>:L176-L179</c>], and those are two
/// different participants: the semantic event lands in the fake host's call log, and the topic lands
/// here. Asserting only one would leave the other free to fire unnoticed.
/// </remarks>
internal sealed class GateTopicSubscriber
{
    /// <summary>How many times the broker dispatched to this subscriber.</summary>
    internal int Count { get; private set; }

    /// <summary>The value every dispatch answers with - <c>0</c> continue, <c>1</c> prevent.</summary>
    internal long Answer { get; set; } = RetCode.OK;

    /// <summary>The broker handler. Three slots, matching the widest payload the gated topics carry.</summary>
    /// <param name="slot1">Slot one - the injected source.</param>
    /// <param name="slot2">Slot two.</param>
    /// <param name="slot3">Slot three.</param>
    /// <returns><see cref="Answer"/>.</returns>
    public long OnTopic(object? slot1, object? slot2, object? slot3)
    {
        _ = slot1;
        _ = slot2;
        _ = slot3;
        Count++;
        return Answer;
    }
}

/// <summary>
/// A concrete <see cref="DataWindowEventChain"/> for this suite: it supplies the abstract host contract
/// by forwarding every member to a composed <see cref="FakeDataWindowHost"/>, records the
/// <c>oncolumnexptrace</c> raises the column-expression double emits, and adds nothing else.
/// </summary>
/// <remarks>
/// <para>
/// EVERY HOST MEMBER BELOW IS A PURE FORWARD, DELIBERATELY. The subject under test is the gate, so the
/// double must add no decision of its own - any decision taken here would be a decision the assertions
/// then measured instead of the gate's. The eleven semantic events are forwarded too, because the chain
/// raises them ON ITSELF: <c>Event RowFocusChanged(currentRow)</c> at <c>se_cst_dw.sru:L125</c> is a
/// call on the control, so the override has to reach the fake host for its call log to record it at all
/// - and that call log is precisely how "the semantic event was NOT reached" is measured.
/// </para>
/// <para>
/// <c>OnDoItemChange</c> and <c>OnDoItemChanged</c> ARE POINTEDLY NOT FORWARDED. The chain overrides
/// both itself - they are two of the nine semantic events, at <c>:L256-L293</c> and
/// <c>:L295-L320</c> - and overriding them here would replace the very path the
/// <c>EID_ITEMCHANGE</c> coupling runs through. Their inner semantic events, <c>ItemChanged</c> and
/// <c>OnItemChanged</c>, are what is observed instead.
/// </para>
/// <para>
/// The three gate members declared on <see cref="DataWindowServiceHost"/> are implemented by
/// DELEGATING TO THE SESSION, which is the whole architectural point of this suite's Phase 5: the gate
/// owns the vocabulary and the operations, and the session owns the value.
/// </para>
/// <para>
/// The host is built INSIDE the constructor so it can be handed the chain's own broker, giving one
/// registry for both halves of the double. Note the ordering that makes this safe: the base
/// constructor runs first and calls <c>OnInit(this)</c> on all five attached services
/// [<c>:L576-L580</c>], so a service must not touch a forwarded member during attachment - and none
/// does, because <see cref="GateInertService.OnInit"/> only stores the reference.
/// </para>
/// </remarks>
internal sealed class GateEventChain : DataWindowEventChain
{
    private readonly List<(long Row, string Column, string Stack, string Expression, string Value)>
        _traces = [];

    /// <summary>Builds the chain over a fresh fake host wired to the chain's own broker.</summary>
    /// <param name="session">The session that owns the disabled-event mask [<c>:L89</c>].</param>
    /// <param name="services">The attached-service factory.</param>
    /// <param name="observer">The dispatch recorder.</param>
    internal GateEventChain(
        ValidationSession session,
        GateServiceFactory services,
        IDataWindowEventObserver? observer = null)
        : base(session, services, observer)
    {
        Services = services;
        Host = new FakeDataWindowHost(Eventful);
    }

    /// <summary>The composed host every forwarded member reaches, and whose call log is the evidence.</summary>
    internal FakeDataWindowHost Host { get; }

    /// <summary>The factory, so an assertion reaches the five instances the chain is holding.</summary>
    internal GateServiceFactory Services { get; }

    /// <summary>
    /// Every <c>oncolumnexptrace</c> raise [<c>se_cst_dw.sru:L32</c>], in order. EMPTY is the
    /// assertion that the <c>EID_ITEMCHANGE</c> gate suppressed column-expression evaluation.
    /// </summary>
    internal IReadOnlyList<(long Row, string Column, string Stack, string Expression, string Value)>
        Traces => _traces;

    /// <summary>
    /// Records the trace, then runs the base so the oracle's empty body still executes and nothing
    /// about the default is bypassed.
    /// </summary>
    /// <param name="row">The one-based row being calculated.</param>
    /// <param name="dwo">The column being calculated.</param>
    /// <param name="stack">The flattened call stack.</param>
    /// <param name="expr">The expression as evaluated.</param>
    /// <param name="value">The rendered result.</param>
    public override void OnColumnExpTrace(
        long row,
        IDataWindowObject dwo,
        string stack,
        string expr,
        string value)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        _traces.Add((row, dwo.Name, stack, expr, value));

        base.OnColumnExpTrace(row, dwo, stack, expr, value);
    }

    // ---------------------------------------------------------------------------------------------
    //  THE GATE TRIO DECLARED ON THE HOST - DELEGATED TO THE SESSION, WHICH OWNS THE MASK
    //  se_cst_dw.sru:L109-L111 over the field at :L89
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public override bool IsEventDisabled(uint evt) => Session.IsEventDisabled(evt);

    /// <inheritdoc/>
    public override long DisableEvent(uint evt) => Session.DisableEvent(evt);

    /// <inheritdoc/>
    public override int EnableEvent(uint evt) => Session.EnableEvent(evt);

    // ---------------------------------------------------------------------------------------------
    //  THE REST OF THE HOST CONTRACT - forty-five members plus the object model, all pure forwards
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

    /// <inheritdoc/>
    /// <remarks>
    /// Through the fake host's public bridge, because the operation is <see langword="protected"/> on
    /// that host and a COMPOSING double cannot reach a protected member of an instance it merely holds.
    /// </remarks>
    protected override int FilterCore() => Host.InvokeFilterCore();

    /// <inheritdoc/>
    /// <remarks>Through the public bridge, for the reason given on <see cref="FilterCore"/>.</remarks>
    protected override int DeleteRowCore(long row) => Host.InvokeDeleteRowCore(row);

    // ---------------------------------------------------------------------------------------------
    //  THE SEMANTIC EVENTS THE CHAIN RAISES ON ITSELF - forwarded so the call log records them
    //  Each is the evidence that a gated-out handler reached NOTHING.
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
    /// Records step three of <c>ondoitemchanged</c> [<c>se_cst_dw.sru:L319</c>] into the host's call
    /// log, then runs the base so the oracle's empty body still executes.
    /// </summary>
    /// <param name="row">The one-based row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    public override void OnItemChanged(long row, IDataWindowObject dwo)
    {
        Host.CallLog.Record("Event OnItemChanged", row, dwo);

        base.OnItemChanged(row, dwo);
    }
}

// ==================================================================================================
//  THE SUITE
// ==================================================================================================

/// <summary>
/// The parity suite for <see cref="EventGate"/> - the composable event-dispatch bitmask ported from
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>: the three <c>EID_*</c> constants at
/// <c>:L41-L43</c>, the three operations at <c>:L109-L111</c>, the four guard sites at <c>:L124</c>,
/// <c>:L130</c>, <c>:L176</c> and <c>:L187</c>, and the item-change to column-expression coupling the
/// oracle records at <c>:L43</c>.
/// </summary>
public sealed class EventGateTests
{
    private const string ColumnName = "name";

    private const string ColumnType = "char(50)";

    private const long Row = 1L;

    /// <summary>
    /// The value already in the buffer. Passing it back as the edit text makes the oracle's equality
    /// test at <c>se_cst_dw.sru:L198</c> hold, which is what steers the ungated item-change into the
    /// <c>case else</c> arm at <c>:L226-L250</c> - the arm that reaches
    /// <c>Event OnDoItemChanged(row,dwo)</c> at <c>:L247</c> and therefore the column-expression
    /// service. Any other value would take a different arm and the coupling would not be exercised.
    /// </summary>
    private const string OriginalValue = "original";

    /// <summary>
    /// A bit no <c>EID_*</c> constant names. <c>8</c> is the next power of two after
    /// <see cref="EventGate.EID_ITEMCHANGE"/> and the oracle declares nothing for it
    /// [<c>se_cst_dw.sru:L41-L43</c> declares exactly three], so it is the "unknown bit" every
    /// no-guard case below is driven with.
    /// </summary>
    private const uint UnknownBit = 8u;

    // ==============================================================================================
    //  PHASE 1 - THE THREE CONSTANT VALUES, TRANSCRIBED FROM THE ORACLE
    // ==============================================================================================

    /// <summary>
    /// The oracle's own declarations at <c>se_cst_dw.sru:L41-L43</c>, transcribed: the identifier
    /// spelling, the <c>constant long</c> value, and the ported constant.
    /// </summary>
    /// <returns>The matrix.</returns>
    /// <remarks>
    /// THE EXPECTED SIDE IS THE READ-ONLY ORACLE AND THE ACTUAL SIDE IS THE PORT, which is what makes
    /// this a parity assertion rather than a tautology. The value column is typed
    /// <see langword="long"/> because that is what the oracle declares - <c>constant long</c> - and the
    /// port's <see langword="uint"/> is compared against it after the one widening the ported width
    /// requires.
    /// </remarks>
    public static TheoryData<string, long, uint> GateBits() => new()
    {
        // :L41  constant long EID_ROWFOCUSCHANGE  = 1  //RowFocusChanging/RowFocusChanged
        { nameof(EventGate.EID_ROWFOCUSCHANGE), 1L, EventGate.EID_ROWFOCUSCHANGE },

        // :L42  constant long EID_ITEMFOCUSCHANGE = 2  //ItemFocusChanged
        { nameof(EventGate.EID_ITEMFOCUSCHANGE), 2L, EventGate.EID_ITEMFOCUSCHANGE },

        // :L43  constant long EID_ITEMCHANGE      = 4  //ItemChanged，禁用后将不会触发列表达式计算！
        { nameof(EventGate.EID_ITEMCHANGE), 4L, EventGate.EID_ITEMCHANGE },
    };

    [Theory]
    [MemberData(nameof(GateBits))]
    public void EveryGateBitCarriesItsByteExactLegacyValueUnderItsLegacySpelling(
        string identifier,
        long oracleValue,
        uint ported)
    {
        // se_cst_dw.sru:L41-L43. The VALUE first.
        Assert.Equal(oracleValue, (long)ported);

        // AND THE SPELLING, which is a separate obligation (AAP 0.4.5.3): the SCREAMING_SNAKE
        // identifiers travel in serialized payloads, log records and characterization recordings, so a
        // rename would silently invalidate every stored comparison. Resolving the member BY ITS ORACLE
        // NAME is what fails the build if one is ever renamed to C# convention.
        FieldInfo? field = typeof(EventGate).GetField(
            identifier,
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.True(field.IsLiteral);
        Assert.Equal(typeof(uint), field.FieldType);
        Assert.Equal(ported, Assert.IsType<uint>(field.GetValue(obj: null)));
    }

    [Theory]
    [MemberData(nameof(GateBits))]
    public void EveryGateBitIsASingleDistinctPowerOfTwoSoThatTheBitsCompose(
        string identifier,
        long oracleValue,
        uint ported)
    {
        _ = identifier;
        _ = oracleValue;

        // :L40  //事件ID（支持组合）  - "event ID (combination supported)". The oracle states the bits
        // compose, and `n & (n - 1) == 0` is what makes the composition safe: each value carries EXACTLY
        // one bit, so the legacy `+` idiom cannot carry into a neighbouring bit and is therefore
        // identical to a bit-or. A value of, say, 3 would still "work" for a single test yet would make
        // EID_ROWFOCUSCHANGE and EID_ITEMFOCUSCHANGE indistinguishable once combined.
        Assert.NotEqual(0u, ported);
        Assert.Equal(0u, ported & (ported - 1u));
    }

    [Fact]
    public void TheOracleDeclaresExactlyThreeBitsAndThePortDeclaresNoFourth()
    {
        // :L41-L43 declares THREE and nothing else - a grep for EID_ over the oracle returns these three
        // declarations plus the four reads at :L124, :L130, :L176 and :L187. No zero-valued constant is
        // declared either: zero is not a nameless gap, it is the value both mutators REJECT [:L506,
        // :L530]. Inventing a fourth bit, or a named zero, would be vocabulary the oracle does not have.
        FieldInfo[] declared =
            typeof(EventGate).GetFields(BindingFlags.Static | BindingFlags.NonPublic);

        Assert.Equal(3, declared.Length);
        Assert.All(declared, field => Assert.True(field.IsLiteral));
        Assert.All(declared, field => Assert.Equal(typeof(uint), field.FieldType));

        Assert.Equal(
            [
                nameof(EventGate.EID_ITEMCHANGE),
                nameof(EventGate.EID_ITEMFOCUSCHANGE),
                nameof(EventGate.EID_ROWFOCUSCHANGE)
            ],
            declared.Select(field => field.Name).Order(StringComparer.Ordinal));
    }

    // ==============================================================================================
    //  PHASE 1 - THE CROSS-ARTIFACT INVARIANT AGAINST CONTRACT C-03
    //
    //  THIS TEST IS THE ONLY THING HOLDING THE AGREEMENT, AND THAT IS A MEASURED FACT.
    //  PowerFramework.Contracts.csproj declares ZERO ProjectReference by design - it is a boundary
    //  definition and not a shared-code back door (constraint C-A) - so NO COMPILER ANYWHERE checks
    //  that Domain/EventGate.cs and shared/PowerFramework.Contracts/Proto/dataservices.v1.proto agree.
    //  If the proto gains, loses, renumbers or renames a gate bit, this is what fails.
    //
    //  EVERY COMPARISON BELOW READS THE GENERATED ARTIFACT rather than a retyped literal: the wire
    //  numbers come from the protobuf DESCRIPTOR, which is deserialized from the compiled .proto's own
    //  bytes, and the wire spellings come from the descriptor's value names. A test that compared two
    //  hand-typed constants would prove nothing about either file.
    // ==============================================================================================

    /// <summary>
    /// The gate bit enum as the generated contract declares it -
    /// <c>message EventGate { enum Bit { ... } }</c>.
    /// </summary>
    /// <returns>The single nested enum descriptor.</returns>
    private static EnumDescriptor WireBitDescriptor() =>
        Assert.Single(WireEventGate.Descriptor.EnumTypes);

    [Fact]
    public void EveryWireGateBitHasADomainConstantWithTheSameSpellingAndTheSameNumber()
    {
        // Walking the DESCRIPTOR, so the proto file itself is the source of both the name and the
        // number on the expected side.
        foreach (EnumValueDescriptor wire in WireBitDescriptor().Values)
        {
            FieldInfo? domain = typeof(EventGate).GetField(
                wire.Name,
                BindingFlags.Static | BindingFlags.NonPublic);

            if (wire.Number == 0)
            {
                // EID_UNSPECIFIED is proto3's MANDATORY zero member and the proto records it as "the
                // protocol zero and never a legacy event". It must have NO domain counterpart, because
                // the oracle declares no zero-valued constant and both mutators reject a zero argument
                // [:L506, :L530]. A domain constant appearing here would mean the port had invented a
                // legacy event out of a protocol artifact.
                Assert.Null(domain);
                continue;
            }

            Assert.NotNull(domain);
            Assert.Equal(wire.Number, (int)Assert.IsType<uint>(domain.GetValue(obj: null)));
        }
    }

    [Fact]
    public void EveryDomainGateConstantHasAWireBitWithTheSameSpellingAndTheSameNumber()
    {
        // THE REVERSE DIRECTION, and it is a different failure. The forward walk above catches a bit
        // that exists on the wire and not in the domain; this one catches a bit added to the domain and
        // never published, which would make a gate operation succeed locally and be unrepresentable in
        // GetEventGateResponse.disabled_bits. The domain side is the three constants of
        // se_cst_dw.sru:L41-L43 and nothing else.
        IList<EnumValueDescriptor> wire = WireBitDescriptor().Values;

        foreach (FieldInfo domain in
            typeof(EventGate).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            EnumValueDescriptor? match =
                wire.SingleOrDefault(value => string.Equals(
                    value.Name,
                    domain.Name,
                    StringComparison.Ordinal));

            Assert.NotNull(match);
            Assert.Equal((int)Assert.IsType<uint>(domain.GetValue(obj: null)), match.Number);
        }
    }

    [Fact]
    public void TheWireEnumDeclaresTheThreeLegacyBitsPlusTheProtocolZeroAndNothingElse()
    {
        // Four members: the proto3 zero plus the three of :L41-L43. A fifth would be a capability the
        // oracle does not have; a third would mean one was dropped.
        IList<EnumValueDescriptor> wire = WireBitDescriptor().Values;

        // THE EXPECTED SET IS BUILT, NOT RETYPED. Every non-zero number comes from the domain constants,
        // which the theory above has already pinned to se_cst_dw.sru:L41-L43, and the only literal is the
        // 0 that proto3 REQUIRES of every enum's first member - a protocol rule rather than a legacy
        // value, which is why it is the one number with no oracle to cite.
        int[] expected =
            [
                0,
                .. typeof(EventGate)
                    .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                    .Select(field => (int)(uint)field.GetValue(obj: null)!)
            ];

        Assert.Equal(4, wire.Count);
        Assert.Equal(expected.Length, wire.Count);
        Assert.Equal(
            expected.Order(),
            wire.Select(value => value.Number).Order());

        // The generated CLR enum must agree with its own descriptor - it is generated from the same
        // proto, but from a different code path, so the agreement is worth pinning once.
        Assert.Equal(
            wire.Select(value => value.Number).Order(),
            Enum.GetValues<WireEventGate.Types.Bit>().Select(bit => (int)bit).Order());
    }

    [Theory]
    [MemberData(nameof(GateBits))]
    public void EveryDomainGateConstantEqualsItsGeneratedWireEnumMember(
        string identifier,
        long oracleValue,
        uint ported)
    {
        _ = oracleValue;

        // Resolving the GENERATED CLR enum member by the proto spelling, through the OriginalName
        // attribute the protobuf generator emits. That attribute is the generator's own record of the
        // .proto identifier, so this binds the domain constant to the published name and not merely to
        // the PascalCase name the generator happened to derive from it. The `identifier` on the expected
        // side is the oracle's own spelling, transcribed from se_cst_dw.sru:L41-L43, so this one
        // assertion chains all three artifacts together: oracle name -> proto name -> domain value.
        WireEventGate.Types.Bit wire = Enum.GetValues<WireEventGate.Types.Bit>()
            .Single(bit => string.Equals(
                typeof(WireEventGate.Types.Bit)
                    .GetField(bit.ToString())!
                    .GetCustomAttribute<OriginalNameAttribute>()!
                    .Name,
                identifier,
                StringComparison.Ordinal));

        Assert.Equal((int)ported, (int)wire);
    }

    // ==============================================================================================
    //  PHASE 1 - THE MASK WIDTH, AND WHY IT IS uint
    // ==============================================================================================

    [Fact]
    public void TheMaskWidthIsTheThirtyTwoBitUnsignedWidthTheSharedBitPrimitivesDeclare()
    {
        // PowerBuilder and C# spell different widths with the same names, and this is the assertion that
        // keeps the port on the right side of that. PowerBuilder `ulong` is 32-BIT, so it maps to C#
        // `uint`; PowerBuilder `uint` is 16-bit and maps to C# `ushort`. The oracle's own primitive is
        // declared over the 32-bit unsigned type in every position:
        //
        //     global function boolean bittest(readonly ulong num, readonly ulong bits)
        //                                                 [ws_objects/pfw.common.pbl.src/bittest.srf:L7]
        //
        // so Shared.Kernel's Bits exposes BitTest(uint, uint) - and the gate's own members must match
        // that width exactly, because a mask typed wider or narrower would not bind to it.
        MethodInfo test = SharedBitMethod(nameof(Bits.BitTest));
        MethodInfo or = SharedBitMethod(nameof(Bits.BitOr));
        MethodInfo clear = SharedBitMethod(nameof(Bits.BitClear));

        Assert.Equal(typeof(bool), test.ReturnType);
        Assert.Equal(typeof(uint), or.ReturnType);
        Assert.Equal(typeof(uint), clear.ReturnType);

        Assert.All(
            new[] { test, or, clear },
            method => Assert.All(
                method.GetParameters(),
                parameter => Assert.Equal(typeof(uint), parameter.ParameterType)));

        // AND THERE IS EXACTLY ONE OF EACH. Bits documents that a `long` operand raises CS1503 BY
        // DESIGN and that adding a `long` overload is not permitted, because such an overload is the
        // silent-truncation door that file exists to keep shut. If one ever appears, widening the mask
        // becomes possible without a single compiler diagnostic - so the absence is pinned here.
        Assert.Single(SharedBitOverloads(nameof(Bits.BitTest)));
        Assert.Single(SharedBitOverloads(nameof(Bits.BitOr)));
        Assert.Single(SharedBitOverloads(nameof(Bits.BitClear)));
    }

    [Fact]
    public void EveryGateMemberIsDeclaredAtTheMaskWidthWithTheMutatorsTakingItByReference()
    {
        // The predicate READS the mask and the two mutators ASSIGN it, which is why only the mutators
        // take it by reference: the oracle assigns its own instance field in place at :L508 and :L532,
        // and passing by reference is what reproduces that - including the property that matters most
        // for parity, that the REJECTION path leaves the caller's mask provably untouched because the
        // reference is simply never assigned.
        ParameterInfo[] predicate = GateMethod(nameof(EventGate.IsEventDisabled)).GetParameters();
        ParameterInfo[] disable = GateMethod(nameof(EventGate.DisableEvent)).GetParameters();
        ParameterInfo[] enable = GateMethod(nameof(EventGate.EnableEvent)).GetParameters();

        Assert.Equal(typeof(uint), predicate[0].ParameterType);
        Assert.False(predicate[0].ParameterType.IsByRef);
        Assert.Equal(typeof(uint), predicate[1].ParameterType);

        Assert.Equal(typeof(uint).MakeByRefType(), disable[0].ParameterType);
        Assert.False(disable[0].IsIn);
        Assert.Equal(typeof(uint), disable[1].ParameterType);

        Assert.Equal(typeof(uint).MakeByRefType(), enable[0].ParameterType);
        Assert.False(enable[0].IsIn);
        Assert.Equal(typeof(uint), enable[1].ParameterType);
    }

    [Fact]
    public void TheWidestLegalCombinationRoundTripsThroughEveryOperation()
    {
        // THE WIDEST LEGAL COMBINATION is all three bits at once - the oracle permits it explicitly
        // [:L40 "combination supported"] and it is the value with the most set bits any legal mask can
        // hold, because :L41-L43 declares exactly three.
        const uint all =
            EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE;

        Assert.Equal(7u, all);

        uint mask = 0u;

        // :L508  one call, all three bits, because `+` over disjoint powers of two IS a bit-or.
        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, all));
        Assert.Equal(all, mask);

        Assert.True(EventGate.IsEventDisabled(mask, EventGate.EID_ROWFOCUSCHANGE));
        Assert.True(EventGate.IsEventDisabled(mask, EventGate.EID_ITEMFOCUSCHANGE));
        Assert.True(EventGate.IsEventDisabled(mask, EventGate.EID_ITEMCHANGE));
        Assert.True(EventGate.IsEventDisabled(mask, all));

        // The mask carries no bit the vocabulary does not name.
        Assert.False(EventGate.IsEventDisabled(mask, UnknownBit));

        // :L532  and back to nothing disabled, exactly.
        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, all));
        Assert.Equal(0u, mask);
    }

    [Fact]
    public void TheWidestLegalCombinationSurvivesTheSessionAndItsWireWidening()
    {
        // The mask's own field is uint [Domain/ValidationSession.cs], and the snapshot widens it to the
        // int64 the wire carries on message EventGate - `int64 mask = 1`. Both halves are pinned here so
        // a change to either width is caught: the widening is legal and lossless in this direction, and
        // it is the ONLY place the two widths meet.
        const uint all =
            EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE;

        ValidationSession session = NewSession(all);

        Assert.Equal(all, session.DisabledEvent);
        Assert.Equal(7L, session.CaptureState().DisabledEventMask);
        Assert.Equal(typeof(long), typeof(ValidationSessionSnapshot)
            .GetProperty(nameof(ValidationSessionSnapshot.DisabledEventMask))!
            .PropertyType);
    }

    // ==============================================================================================
    //  PHASE 2 - THE THREE OPERATIONS AND THE PRESERVED RETURN-TYPE ASYMMETRY
    // ==============================================================================================

    /// <summary>
    /// The oracle's three prototypes at <c>se_cst_dw.sru:L109-L111</c>, paired with the ported member
    /// and the CLR type its PowerScript return type maps to.
    /// </summary>
    /// <returns>The matrix.</returns>
    /// <remarks>
    /// <para>
    /// THE ASYMMETRY IS THE SUBJECT, NOT A BLEMISH IN THE TABLE. The oracle declares <c>long</c> for
    /// the disable path and <c>integer</c> for the enable path although their bodies are mirror images -
    /// PowerBuilder <c>integer</c> is 16-bit and <c>long</c> is 32-bit, so enable is declared NARROWER
    /// than disable for no reason the source gives. It is almost certainly an oversight in the original,
    /// and constraint C-B forbids repairing it: normalising the two to one type would be a behavioural
    /// change to a published signature, so it is CARRIED as <see langword="long"/> against
    /// <see langword="int"/> and asserted here so that a future tidy-up FAILS THE BUILD rather than
    /// passing unnoticed.
    /// </para>
    /// <para>
    /// It is observationally benign, and saying so is part of preserving it honestly: the only two
    /// values either member returns are <see cref="RetCode.OK"/> and
    /// <see cref="RetCode.E_INVALID_ARGUMENT"/>, and both fit either width without truncation. The
    /// fidelity being preserved is the CONTRACT's, not a value's.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, Type> OperationSignatures() => new()
    {
        // :L109  public function boolean of_iseventdisabled (readonly long evt)
        { "of_iseventdisabled", nameof(EventGate.IsEventDisabled), typeof(bool) },

        // :L110  public function long    of_disableevent    (readonly long evt)
        { "of_disableevent", nameof(EventGate.DisableEvent), typeof(long) },

        // :L111  public function integer of_enableevent     (readonly long evt)
        { "of_enableevent", nameof(EventGate.EnableEvent), typeof(int) },
    };

    [Theory]
    [MemberData(nameof(OperationSignatures))]
    public void EveryOperationReproducesItsLegacyReturnType(
        string oraclePrototype,
        string member,
        Type expectedReturnType)
    {
        _ = oraclePrototype;

        // se_cst_dw.sru:L109-L111. PowerScript `boolean` -> bool, `long` -> long, `integer` -> int,
        // following the plan's PowerBuilder-to-C# type mapping.
        Assert.Equal(expectedReturnType, GateMethod(member).ReturnType);
    }

    [Fact]
    public void TheTwoMutatorsKeepTheLegacysDifferentReturnTypesRatherThanBeingHarmonised()
    {
        // :L110 declares `long` and :L111 declares `integer` FOR MIRROR-IMAGE BODIES. This is the
        // assertion that makes the asymmetry load bearing: it is not enough that each member happens to
        // return the right type today, the two must remain DIFFERENT from each other. Harmonising them -
        // the obvious tidy-up - is a constraint C-B violation, and this line is what catches it.
        Assert.NotEqual(
            GateMethod(nameof(EventGate.DisableEvent)).ReturnType,
            GateMethod(nameof(EventGate.EnableEvent)).ReturnType);

        // And the predicate is a third, narrower shape again: it cannot report an error AT ALL, because
        // its declared return type is `boolean` [:L109]. That is why it has no guard - a guard there
        // would have had to invent a meaning for false rather than merely add validation.
        Assert.Equal(typeof(bool), GateMethod(nameof(EventGate.IsEventDisabled)).ReturnType);
    }

    [Fact]
    public void TheSessionCarriesTheSameThreeReturnTypesAsTheGateItDelegatesTo()
    {
        // The asymmetry has to survive EVERY hop, not just the gate, or it would be silently normalised
        // one layer out. Domain/ValidationSession.cs owns the mask and forwards each operation to the
        // gate, so its three members must carry the same three types [:L109-L111].
        Assert.Equal(typeof(bool), SessionGateMethod(nameof(ValidationSession.IsEventDisabled)).ReturnType);
        Assert.Equal(typeof(long), SessionGateMethod(nameof(ValidationSession.DisableEvent)).ReturnType);
        Assert.Equal(typeof(int), SessionGateMethod(nameof(ValidationSession.EnableEvent)).ReturnType);
    }

    [Theory]
    [MemberData(nameof(OperationSignatures))]
    public void TheChainExposesEachOperationOverAReadonlyArgumentAsWellAsTheHostContractOne(
        string oraclePrototype,
        string member,
        Type expectedReturnType)
    {
        _ = oraclePrototype;

        // TWO OVERLOADS PER OPERATION, AND BOTH ARE DELIBERATE. The oracle's argument is declared
        // `readonly long evt` [:L109-L111], and AAP 0.4.5.2 maps a PowerScript `readonly` parameter to a
        // C# `in` parameter - so the chain's `in uint` overload is the faithful port of the prototype,
        // while the plain `uint` member satisfies the abstract host contract it inherits. Neither is
        // redundant, and both must carry the legacy return type.
        MethodInfo[] overloads =
            [.. typeof(DataWindowEventChain)
                .GetMethods()
                .Where(candidate => string.Equals(candidate.Name, member, StringComparison.Ordinal))];

        Assert.Equal(2, overloads.Length);
        Assert.All(overloads, overload => Assert.Equal(expectedReturnType, overload.ReturnType));

        // Exactly one of the two is the `readonly` port.
        MethodInfo readonlyPort =
            Assert.Single(overloads, overload => overload.GetParameters()[0].IsIn);

        Assert.Equal(typeof(uint).MakeByRefType(), readonlyPort.GetParameters()[0].ParameterType);
    }

    // ----------------------------------------------------------------------------------------------
    //  PHASE 2 - THE PREDICATE HAS NO ARGUMENT GUARD, AND THAT IS PRESERVED
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Degenerate and unknown-bit queries the oracle permits: mask, event, and the answer
    /// <c>BitTest(num, bits)</c> gives.
    /// </summary>
    /// <returns>The matrix.</returns>
    /// <remarks>
    /// EVERY ROW IS A LEGAL CALL WITH A DEFINED ANSWER. The oracle's predicate body is one line -
    /// <c>return BitTest(_nDisabledEvent,evt)</c> [<c>se_cst_dw.sru:L486</c>] - with no validation of
    /// any kind ahead of it, while BOTH mutators guard against a zero argument [<c>:L506</c>,
    /// <c>:L530</c>]. That asymmetry is reproduced: nothing here may throw and nothing here may return
    /// an error code, because the declared return type is <c>boolean</c> and has no room for one.
    /// </remarks>
    public static TheoryData<uint, uint, bool> UnguardedQueries() => new()
    {
        // A zero mask asks about an empty set: nothing is disabled, whatever is asked.
        { 0u, 0u, false },
        { 0u, EventGate.EID_ROWFOCUSCHANGE, false },
        { 0u, EventGate.EID_ITEMFOCUSCHANGE, false },
        { 0u, EventGate.EID_ITEMCHANGE, false },
        { 0u, UnknownBit, false },

        // A ZERO EVENT is the case the mutators reject and the predicate accepts. BitTest is
        // `(num & bits) != 0`, and `anything & 0` is 0, so the answer is false for EVERY mask - never an
        // exception and never an error code.
        { EventGate.EID_ROWFOCUSCHANGE, 0u, false },
        { EventGate.EID_ITEMCHANGE, 0u, false },
        { 7u, 0u, false },
        { uint.MaxValue, 0u, false },

        // AN UNKNOWN BIT is equally legal. The oracle names three bits but its predicate tests whatever
        // it is handed, so a bit outside the vocabulary answers truthfully about the mask rather than
        // being rejected as unrecognised.
        { UnknownBit, UnknownBit, true },
        { 7u, UnknownBit, false },
        { uint.MaxValue, UnknownBit, true },

        // The top bit, which only an unsigned mask can hold at all - the direct consequence of the
        // 32-bit UNSIGNED width [ws_objects/pfw.common.pbl.src/bittest.srf].
        { 0x8000_0000u, 0x8000_0000u, true },
    };

    [Theory]
    [MemberData(nameof(UnguardedQueries))]
    public void ThePredicateAnswersEveryDegenerateQueryWithoutGuardingIt(
        uint mask,
        uint evt,
        bool expected)
    {
        // :L486 - the whole legacy body, and no guard precedes it.
        Assert.Equal(expected, EventGate.IsEventDisabled(mask, evt));
    }

    [Fact]
    public void ThePredicateAnswersACompositeArgumentAsAnAnyQuestionAndNotAnAllQuestion()
    {
        // THE ONE GENUINE TRAP IN THIS MEMBER. BitTest is `(num & bits) != 0`, so a multi-bit argument
        // asks "is ANY of these disabled", not "are ALL of these disabled". Contract C-03 sidesteps the
        // ambiguity by carrying per-bit results as an explicit list on GetEventGateResponse instead of
        // letting a consumer test a multi-bit value itself. This member cannot be changed to answer the
        // "all" question, because the oracle answers the "any" one [:L486].
        const uint composite = EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMCHANGE;

        Assert.True(EventGate.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE, composite));
        Assert.True(EventGate.IsEventDisabled(EventGate.EID_ITEMCHANGE, composite));
        Assert.True(EventGate.IsEventDisabled(composite, composite));

        // Only a mask sharing NO bit with the argument answers false.
        Assert.False(EventGate.IsEventDisabled(EventGate.EID_ITEMFOCUSCHANGE, composite));
    }

    [Fact]
    public void ThePredicateIsSideEffectFreeSoRepeatedQueriesCannotDriftFromEachOther()
    {
        // The oracle's predicate READS its field and writes nothing [:L486], which is what allows
        // Domain/DataWindowEventChain.cs to read the mask a second time purely for its dispatch report
        // without changing the decision the item-change protocol then takes. If the predicate ever
        // acquired a side effect, that second read would silently make the two disagree.
        uint mask = EventGate.EID_ITEMCHANGE;

        for (int probe = 0; probe < 3; probe++)
        {
            Assert.True(EventGate.IsEventDisabled(mask, EventGate.EID_ITEMCHANGE));
            Assert.False(EventGate.IsEventDisabled(mask, EventGate.EID_ROWFOCUSCHANGE));
        }

        Assert.Equal(EventGate.EID_ITEMCHANGE, mask);
    }

    // ----------------------------------------------------------------------------------------------
    //  PHASE 2 - THE TWO MUTATORS, THEIR GUARD, AND THEIR RETURN CODES
    // ----------------------------------------------------------------------------------------------

    /// <summary>Every bit, disabled from an empty mask and then enabled back to it.</summary>
    /// <returns>The matrix.</returns>
    public static TheoryData<uint> EveryBitAndEveryCombination() => new()
    {
        EventGate.EID_ROWFOCUSCHANGE,
        EventGate.EID_ITEMFOCUSCHANGE,
        EventGate.EID_ITEMCHANGE,
        EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE,
        EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMCHANGE,
        EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE,
        EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE,
        UnknownBit,
    };

    [Theory]
    [MemberData(nameof(EveryBitAndEveryCombination))]
    public void DisableThenEnableReturnsTheLegacyCodesAndRoundTripsTheMaskExactly(uint evt)
    {
        uint mask = 0u;

        // :L506-L510  guard, then `_nDisabledEvent = BitOR(_nDisabledEvent,evt)`, then RetCode.OK -
        // which is 0 [ws_objects/pfw.shared.pbl.src/retcode.sru:L39].
        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, evt));
        Assert.Equal(evt, mask);
        Assert.True(EventGate.IsEventDisabled(mask, evt));

        // :L530-L534  guard, then `_nDisabledEvent = BitClear(_nDisabledEvent,evt)`, then RetCode.OK -
        // narrowed to int by a COMPILE-TIME-CHECKED constant conversion, because RetCode's members are
        // `const long` and this member returns `int`.
        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, evt));
        Assert.Equal(0u, mask);
        Assert.False(EventGate.IsEventDisabled(mask, evt));
    }

    [Theory]
    [MemberData(nameof(EveryBitAndEveryCombination))]
    public void BothMutatorsAreIdempotent(uint evt)
    {
        uint mask = 0u;

        // A bit already set is set again to the same value, so the second call changes nothing and still
        // reports success - `BitOR` is idempotent [:L508].
        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, evt));
        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, evt));
        Assert.Equal(evt, mask);

        // And clearing a bit that is already clear is equally a no-op WITH THE SAME RETURN CODE - the
        // oracle has no "was not set" answer to give, because `BitClear` reports nothing [:L532].
        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, evt));
        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, evt));
        Assert.Equal(0u, mask);
    }

    /// <summary>
    /// Masks the zero-argument guard is driven against, so the "left untouched" half is checked from a
    /// clear mask, a single-bit mask and the widest legal one alike.
    /// </summary>
    /// <returns>The matrix.</returns>
    public static TheoryData<uint> MasksTheZeroGuardMustNotTouch() => new()
    {
        0u,
        EventGate.EID_ROWFOCUSCHANGE,
        EventGate.EID_ITEMFOCUSCHANGE,
        EventGate.EID_ITEMCHANGE,
        EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE,
    };

    [Theory]
    [MemberData(nameof(MasksTheZeroGuardMustNotTouch))]
    public void BothMutatorsRejectAZeroArgumentAndLeaveTheMaskUntouched(uint mask)
    {
        uint underDisable = mask;
        uint underEnable = mask;

        // :L506  if evt = 0 then return RetCode.E_INVALID_ARGUMENT - which is -3
        // [ws_objects/pfw.shared.pbl.src/retcode.sru:L46], NOT a bespoke code invented for the gate.
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, EventGate.DisableEvent(ref underDisable, 0u));

        // :L530  the same guard, on the same value, in the mirror-image body.
        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, EventGate.EnableEvent(ref underEnable, 0u));

        // AND THE MASK IS PROVABLY UNTOUCHED, which is the half a return-code-only assertion would
        // miss. The oracle returns BEFORE reaching its own assignment [:L506 precedes :L508, :L530
        // precedes :L532], so the reference is never assigned on the rejection path.
        Assert.Equal(mask, underDisable);
        Assert.Equal(mask, underEnable);
    }

    [Fact]
    public void TheZeroGuardIsTheOnlyRejectionEitherMutatorHas()
    {
        // The oracle validates NOTHING ELSE - not the vocabulary, not the width, not the number of bits.
        // An unknown bit, an all-bits value, and every combination are all accepted with RetCode.OK
        // [:L506-L510, :L530-L534]. Adding a "known bits only" check would be a behavioural change that
        // rejected calls the legacy accepted.
        uint mask = 0u;

        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, UnknownBit));
        Assert.Equal(UnknownBit, mask);

        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, uint.MaxValue));
        Assert.Equal(uint.MaxValue, mask);

        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, uint.MaxValue));
        Assert.Equal(0u, mask);
    }

    // ----------------------------------------------------------------------------------------------
    //  PHASE 2 - COMPOSABILITY
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Disable two bits, enable one of them, and the mask that must remain - the composability the
    /// oracle claims at <c>se_cst_dw.sru:L40</c>, checked over every ordered pair.
    /// </summary>
    /// <returns>The matrix.</returns>
    public static TheoryData<uint, uint, uint> ComposedPairs() => new()
    {
        { EventGate.EID_ROWFOCUSCHANGE, EventGate.EID_ITEMFOCUSCHANGE, EventGate.EID_ITEMFOCUSCHANGE },
        { EventGate.EID_ITEMFOCUSCHANGE, EventGate.EID_ROWFOCUSCHANGE, EventGate.EID_ROWFOCUSCHANGE },
        { EventGate.EID_ROWFOCUSCHANGE, EventGate.EID_ITEMCHANGE, EventGate.EID_ITEMCHANGE },
        { EventGate.EID_ITEMCHANGE, EventGate.EID_ROWFOCUSCHANGE, EventGate.EID_ROWFOCUSCHANGE },
        { EventGate.EID_ITEMFOCUSCHANGE, EventGate.EID_ITEMCHANGE, EventGate.EID_ITEMCHANGE },
        { EventGate.EID_ITEMCHANGE, EventGate.EID_ITEMFOCUSCHANGE, EventGate.EID_ITEMFOCUSCHANGE },
    };

    [Theory]
    [MemberData(nameof(ComposedPairs))]
    public void DisablingTwoBitsThenEnablingOneLeavesExactlyTheOtherDisabled(
        uint enabledAgain,
        uint stillDisabled,
        uint expectedMask)
    {
        uint mask = 0u;

        // :L40 "combination supported". Both orders of the same pair are covered by the matrix, so a
        // mutator that clobbered the mask instead of composing into it fails on at least one row.
        Assert.Equal(RetCode.OK, EventGate.DisableEvent(ref mask, enabledAgain | stillDisabled));
        Assert.Equal(enabledAgain | stillDisabled, mask);

        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, enabledAgain));

        Assert.Equal(expectedMask, mask);
        Assert.False(EventGate.IsEventDisabled(mask, enabledAgain));
        Assert.True(EventGate.IsEventDisabled(mask, stillDisabled));

        // The third bit was never involved and must be untouched throughout.
        uint untouched =
            (EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE)
            & ~(enabledAgain | stillDisabled);

        Assert.False(EventGate.IsEventDisabled(mask, untouched));
    }

    [Theory]
    [MemberData(nameof(EveryBitAndEveryCombination))]
    public void EnablingABitThatWasNeverDisabledIsANoOpWithTheSameReturnCode(uint evt)
    {
        // The oracle cannot tell the caller that nothing was cleared: `BitClear` on an already-clear bit
        // is a no-op and :L534 returns RetCode.OK unconditionally. So an "enable" of something never
        // disabled must be indistinguishable from one that cleared a bit - same code, same resulting
        // mask - and adding a distinct answer would be new behaviour.
        uint mask = 0u;

        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mask, evt));
        Assert.Equal(0u, mask);

        // And it must not disturb an unrelated bit that IS disabled.
        uint mixed = EventGate.EID_ITEMFOCUSCHANGE;
        uint expected = mixed & ~evt;

        Assert.Equal((int)RetCode.OK, EventGate.EnableEvent(ref mixed, evt));
        Assert.Equal(expected, mixed);
    }

    // ==============================================================================================
    //  PHASE 3 - THE FOUR LEGACY GUARD SITES
    //
    //  The list is EXHAUSTIVE: a grep for EID_ over the oracle returns the three declarations of
    //  :L41-L43 and these four reads, and nothing else.
    //
    //      :L124  ondwnrowchange        if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE)  then return 0
    //      :L130  ondwnrowchanging      if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE)  then return 0
    //      :L176  ondwnitemchangefocus  if BitTest(_nDisabledEvent,EID_ITEMFOCUSCHANGE) then return 0
    //      :L187  ondwnitemchange       if BitTest(_nDisabledEvent,EID_ITEMCHANGE)      then return 0
    //
    //  ALL FOUR SHORT-CIRCUIT WITH `return 0`, AND THAT ZERO IS NOT AN ERROR CODE. Zero is the continue
    //  value of the prevent convention the chain uses throughout, so a gated event reports "continue,
    //  nothing prevented" and never a failure.
    // ==============================================================================================

    /// <summary>
    /// The four guard sites: the event, the bit that gates it, the broker topic it must not reach, and
    /// the semantic event it must not raise.
    /// </summary>
    /// <returns>The matrix.</returns>
    public static TheoryData<EventId, uint, string, string> GuardSites() => new()
    {
        // :L124-L127  gated, then `Event RowFocusChanged`, then EVT_ROWFOCUSCHANGED.
        {
            EventId.Ondwnrowchange,
            EventGate.EID_ROWFOCUSCHANGE,
            DataWindowEventChain.EVT_ROWFOCUSCHANGED,
            "Event RowFocusChanged"
        },

        // :L130-L133  gated on THE SAME BIT, then `Event RowFocusChanging`, then EVT_ROWFOCUSCHANGING.
        {
            EventId.Ondwnrowchanging,
            EventGate.EID_ROWFOCUSCHANGE,
            DataWindowEventChain.EVT_ROWFOCUSCHANGING,
            "Event RowFocusChanging"
        },

        // :L176-L179  gated on its OWN bit, then `Event ItemFocusChanged`, then EVT_ITEMFOCUSCHANGED.
        {
            EventId.Ondwnitemchangefocus,
            EventGate.EID_ITEMFOCUSCHANGE,
            DataWindowEventChain.EVT_ITEMFOCUSCHANGED,
            "Event ItemFocusChanged"
        },

        // :L187  gated at the TOP of the whole item-change micro-protocol [:L182-L253]. The semantic
        // event named here is the inner `Event ItemChanged` the oracle reaches through
        // `Event OnDoItemChange` [:L194, :L292], and the topic is the one `ondoitemchanged` probes and
        // triggers [:L316-L318].
        {
            EventId.Ondwnitemchange,
            EventGate.EID_ITEMCHANGE,
            DataWindowEventChain.EVT_ITEMCHANGED,
            "Event ItemChanged"
        },
    };

    [Theory]
    [MemberData(nameof(GuardSites))]
    public void EveryGuardSiteShortCircuitsWithZeroAndReachesNothingDownstream(
        EventId eventId,
        uint bit,
        string topic,
        string semanticEvent)
    {
        GateFixture fixture = NewFixture(bit);

        // Reads are recorded too, so an ORACLE SNAPSHOT would show up. The gated item-change must not
        // even take one: :L189-L190 reads the original value and the item status, and both sit AFTER the
        // guard at :L187.
        fixture.Host.RecordsReads = true;
        GateTopicSubscriber subscriber = Subscribe(fixture, topic);
        IDataWindowObject dwo = fixture.Dwo;
        fixture.Host.CallLog.Clear();

        // ALL FOUR RETURN 0 - continue, nothing prevented.
        Assert.Equal(RetCode.OK, Raise(fixture, eventId, dwo));

        // The semantic event was not raised. Asserting the WHOLE set is empty is stronger than asserting
        // one name absent: a gated handler must reach no semantic event at all, not merely not that one.
        Assert.Empty(SemanticEvents(fixture));
        Assert.DoesNotContain(semanticEvent, SemanticEvents(fixture));

        // Nor was the broker topic. These are two different participants, so both halves are needed.
        Assert.Equal(0, subscriber.Count);

        // EXACTLY ONE DISPATCH WAS REPORTED - the gated one - and no downstream event followed it.
        Assert.Equal([eventId], fixture.Observer.EventIds);

        DataWindowDispatchReport dispatch = fixture.Observer.Single(eventId).Dispatch;

        Assert.True(dispatch.GatedOut);
        Assert.False(dispatch.SemanticHandlerRan);
        Assert.False(dispatch.BrokerTriggerRan);
        Assert.False(dispatch.ColumnExpressionHandlerRan);
        Assert.Equal(RetCode.OK, fixture.Observer.Single(eventId).ReturnValue);

        // The mask itself is unchanged by being read - reading a gate is not a mutation [:L486].
        Assert.Equal(bit, fixture.Session.DisabledEvent);
    }

    [Theory]
    [MemberData(nameof(GuardSites))]
    public void WithTheBitClearEveryGuardSiteProceedsToItsDownstreamCalls(
        EventId eventId,
        uint bit,
        string topic,
        string semanticEvent)
    {
        // THE MIRROR CASE, and it is what stops "gated out" from being indistinguishable from "the fake
        // never wired anything up". Nothing is disabled here, so every downstream call MUST be observed.
        GateFixture fixture = NewFixture();
        GateTopicSubscriber subscriber = Subscribe(fixture, topic);

        // Steers the item-change into `case else` [:L226-L250], the only arm that reaches
        // `Event OnDoItemChanged` at :L247. The other three events ignore this seam.
        fixture.Host.ItemChangedHandler = (_, _, _) => (long)ItemChangeResult.Default;

        _ = Raise(fixture, eventId, fixture.Dwo);

        Assert.False(fixture.Session.IsEventDisabled(bit));
        Assert.Contains(semanticEvent, SemanticEvents(fixture));
        Assert.Equal(1, subscriber.Count);

        DataWindowDispatchReport dispatch = fixture.Observer.Single(eventId).Dispatch;

        Assert.False(dispatch.GatedOut);
        Assert.True(dispatch.SemanticHandlerRan);
    }

    /// <summary>
    /// Which of the four guarded events each bit gates when it is the ONLY bit set - twelve rows, the
    /// full cross product.
    /// </summary>
    /// <returns>The matrix.</returns>
    /// <remarks>
    /// TWO PROPERTIES AT ONCE, and neither is visible from a single-event test. First, one bit gates
    /// BOTH row-focus handlers - <see cref="EventGate.EID_ROWFOCUSCHANGE"/> is read at
    /// <c>se_cst_dw.sru:L124</c> AND at <c>:L130</c>, so it silences the changing and changed halves of
    /// the pair together. Second, the other two bits are INDEPENDENT of it and of each other, so a
    /// mask-clobbering or wrongly-numbered bit shows up as a false gate somewhere in the matrix.
    /// </remarks>
    public static TheoryData<uint, EventId, bool> GateIndependenceMatrix() => new()
    {
        // EID_ROWFOCUSCHANGE gates :L124 and :L130 and nothing else.
        { EventGate.EID_ROWFOCUSCHANGE, EventId.Ondwnrowchange, true },
        { EventGate.EID_ROWFOCUSCHANGE, EventId.Ondwnrowchanging, true },
        { EventGate.EID_ROWFOCUSCHANGE, EventId.Ondwnitemchangefocus, false },
        { EventGate.EID_ROWFOCUSCHANGE, EventId.Ondwnitemchange, false },

        // EID_ITEMFOCUSCHANGE gates :L176 alone.
        { EventGate.EID_ITEMFOCUSCHANGE, EventId.Ondwnrowchange, false },
        { EventGate.EID_ITEMFOCUSCHANGE, EventId.Ondwnrowchanging, false },
        { EventGate.EID_ITEMFOCUSCHANGE, EventId.Ondwnitemchangefocus, true },
        { EventGate.EID_ITEMFOCUSCHANGE, EventId.Ondwnitemchange, false },

        // EID_ITEMCHANGE gates :L187 alone.
        { EventGate.EID_ITEMCHANGE, EventId.Ondwnrowchange, false },
        { EventGate.EID_ITEMCHANGE, EventId.Ondwnrowchanging, false },
        { EventGate.EID_ITEMCHANGE, EventId.Ondwnitemchangefocus, false },
        { EventGate.EID_ITEMCHANGE, EventId.Ondwnitemchange, true },
    };

    [Theory]
    [MemberData(nameof(GateIndependenceMatrix))]
    public void EachBitGatesExactlyTheEventsTheOracleReadsItAt(
        uint bit,
        EventId eventId,
        bool expectedGatedOut)
    {
        // The four reads are at se_cst_dw.sru:L124, :L130, :L176 and :L187, and each names ONE bit. Every
        // row of the matrix is therefore either one of those four pairings, in which case the guard fires,
        // or a pairing the oracle never makes, in which case it must not.
        GateFixture fixture = NewFixture(bit);

        _ = Raise(fixture, eventId, fixture.Dwo);

        Assert.Equal(expectedGatedOut, fixture.Observer.Single(eventId).Dispatch.GatedOut);
    }

    [Fact]
    public void TheWidestLegalCombinationGatesAllFourSitesAtOnce()
    {
        // All three bits set is a legal mask [:L40], and it must silence every guarded event
        // simultaneously - the property a per-bit test cannot show.
        GateFixture fixture = NewFixture(
            EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE);

        GateTopicSubscriber changed = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGED);
        GateTopicSubscriber changing = Subscribe(fixture, DataWindowEventChain.EVT_ROWFOCUSCHANGING);
        GateTopicSubscriber itemFocus = Subscribe(fixture, DataWindowEventChain.EVT_ITEMFOCUSCHANGED);
        GateTopicSubscriber itemChanged = Subscribe(fixture, DataWindowEventChain.EVT_ITEMCHANGED);
        IDataWindowObject dwo = fixture.Dwo;
        fixture.Host.CallLog.Clear();

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(Row));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChanging(Row, Row + 1L));
        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnItemChangeFocus(Row, dwo));
        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, dwo, OriginalValue));

        Assert.Empty(SemanticEvents(fixture));
        Assert.Equal(0, changed.Count);
        Assert.Equal(0, changing.Count);
        Assert.Equal(0, itemFocus.Count);
        Assert.Equal(0, itemChanged.Count);
        Assert.All(fixture.Observer.Outcomes, outcome => Assert.True(outcome.Dispatch.GatedOut));
    }

    [Fact]
    public void AnUngatedEventIsUnaffectedByEveryBitBecauseTheOracleGuardsOnlyFour()
    {
        // NINE OF THE THIRTEEN RAW EVENTS CARRY NO GUARD AT ALL. `ondwnrbuttonup` [:L120-L121] is one of
        // them, and it must fire with EVERY bit set - the gate is not a global mute, and extending it to
        // an unguarded event would be new behaviour.
        GateFixture fixture = NewFixture(
            EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE | EventGate.EID_ITEMCHANGE);

        GateTopicSubscriber subscriber = Subscribe(fixture, DataWindowEventChain.EVT_RBUTTONUP);

        Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRButtonUp(10L, 20L, Row, fixture.Dwo));

        Assert.Equal(1, subscriber.Count);
        Assert.False(fixture.Observer.Single(EventId.Ondwnrbuttonup).Dispatch.GatedOut);
        Assert.True(fixture.Observer.Single(EventId.Ondwnrbuttonup).Dispatch.BrokerTriggerRan);
    }

    [Fact]
    public void TheGatedItemChangeSkipsTheWholeMicroProtocolAndLeavesTheSessionUntouched()
    {
        // :L187 SHORT-CIRCUITS THE MOST INTRICATE ROUTINE IN THE IN-SCOPE SET, and every step after it
        // must be provably absent:
        //   :L189      the original-value snapshot
        //   :L190      the item-status snapshot
        //   :L192-L196 the save-set-restore of the re-entrancy flag AND the stash of the result
        //   :L194      Event OnDoItemChange, whose own body raises Event ItemChanged [:L292]
        //   :L207      the NESTED Event OnDwnChanging
        //   :L219-L220 the value and status restore of the 1/2 arms
        //   :L231-L244 the type-directed coercion of the `case else` arm
        //   :L247      Event OnDoItemChanged
        GateFixture fixture = NewFixture(EventGate.EID_ITEMCHANGE);

        fixture.Host.RecordsReads = true;
        fixture.Host.ItemChangedHandler = (_, _, _) =>
            throw new InvalidOperationException("The gated item-change must never raise ItemChanged.");
        fixture.Host.DoItemChangeHandler = (_, _, _) =>
            throw new InvalidOperationException("The gated item-change must never raise OnDoItemChange.");

        IDataWindowObject dwo = fixture.Dwo;
        fixture.Host.CallLog.Clear();

        // The gate's own answer is the ZERO of the item-change alphabet, which reaches `case else` when
        // it comes from a handler but means "continue, nothing prevented" when it comes from the guard.
        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, dwo, OriginalValue));

        // No snapshot: neither read happened.
        Assert.DoesNotContain("GetItemStatus", fixture.Host.CallLog.Members);

        // No restore and no coercion: nothing was written to the buffer either.
        Assert.DoesNotContain("SetItemStatus", fixture.Host.CallLog.Members);
        Assert.DoesNotContain("SetItem", fixture.Host.CallLog.Members);

        // No semantic event of any kind, which covers OnDoItemChange, the nested EditChanged and
        // OnDoItemChanged together - and the two throwing seams above make it impossible to pass this
        // assertion by having merely failed to record.
        Assert.Empty(SemanticEvents(fixture));

        // No downstream dispatch: `ondoitemchange`, `ondwnchanging` and `ondoitemchanged` never ran.
        Assert.Equal([EventId.Ondwnitemchange], fixture.Observer.EventIds);

        // AND THE CROSS-EVENT STATE IS UNTOUCHED. The stash is what `ondwnitemvalidationerror` consumes
        // at :L331-L332, so a gated item-change that wrote to it would corrupt the NEXT event's
        // behaviour rather than its own.
        ValidationSessionSnapshot state = fixture.Session.CaptureState();

        Assert.Equal(0L, state.RawItemChangeRetCode);
        Assert.Equal(ItemChangeResult.Default, state.ItemChangeRetCode);
        Assert.False(state.InItemChange);
        Assert.False(state.InItemValidationError);
    }

    // ==============================================================================================
    //  PHASE 4 - THE ITEM-CHANGE TO COLUMN-EXPRESSION COUPLING
    //
    //  THE ORACLE STATES IT AT THE DECLARATION ITSELF [se_cst_dw.sru:L43]:
    //
    //      constant long EID_ITEMCHANGE = 4  //ItemChanged，禁用后将不会触发列表达式计算！
    //                                        "ItemChanged; once disabled, column-expression
    //                                         calculation will NOT be triggered!"
    //
    //  AND THE MECHANISM IS STRUCTURAL, NOT INCIDENTAL. Column-expression recalculation is reached ONLY
    //  through `ondoitemchanged`, whose first act is
    //
    //      if ColumnExp.#Enabled then
    //          ColumnExp.Event OnItemChanged(row,dwo)                                  [:L313-L315]
    //      end if
    //
    //  and `ondoitemchanged` is itself reached only from the `case else` arm of the item-change routine
    //  [:L247] - so the guard at :L187 cuts the ENTIRE path. A consumer that disables item change merely
    //  to quieten a notification also stops every bound column expression from recalculating, gets stale
    //  computed values, and receives NO ERROR ANYWHERE to tell it that happened.
    //
    //  THAT IS LEGACY BEHAVIOUR AND IT IS PRESERVED (constraint C-B). "Fixing" it by evaluating
    //  expressions independently of the gate - a plausible-looking improvement, since the staleness is
    //  silent - would be a C-B violation, and the two assertions below are what would fail.
    // ==============================================================================================

    [Fact]
    public void DisablingItemChangeAlsoSuppressesColumnExpressionEvaluationAndItsTrace()
    {
        GateFixture fixture = NewFixture(EventGate.EID_ITEMCHANGE);

        // THE EXPRESSION SERVICE IS ENABLED, which is what makes this a statement about the GATE. If it
        // were disabled the absence of evaluation would prove nothing at all [:L313].
        Assert.True(fixture.Services.ColumnExp.Enabled);

        fixture.Host.ItemChangedHandler = (_, _, _) => (long)ItemChangeResult.Default;

        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

        // NO COLUMN-EXPRESSION EVALUATION: step one of :L313-L315 never ran.
        Assert.Empty(fixture.Services.ColumnExp.ItemChangedCalls);

        // AND NO TRACE PAYLOAD: `oncolumnexptrace` [:L32] was never raised either, so a diagnostics
        // consumer sees no evidence that anything was skipped - which is precisely the silence the
        // oracle's warning at :L43 is about.
        Assert.Empty(fixture.Chain.Traces);

        // The carrying event never ran at all, which is the structural half of the explanation.
        Assert.False(fixture.Observer.Saw(EventId.Ondoitemchanged));
        Assert.True(fixture.Observer.Single(EventId.Ondwnitemchange).Dispatch.GatedOut);
    }

    [Fact]
    public void ClearingItemChangeRestoresColumnExpressionEvaluationAndItsTrace()
    {
        // THE SAME SESSION, THE BIT FLIPPED. Driving both directions on one session is what shows the
        // coupling follows the BIT rather than some accident of how the fixture was constructed.
        GateFixture fixture = NewFixture(EventGate.EID_ITEMCHANGE);

        fixture.Host.ItemChangedHandler = (_, _, _) => (long)ItemChangeResult.Default;

        Assert.Equal(
            (long)ItemChangeResult.Default,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));
        Assert.Empty(fixture.Services.ColumnExp.ItemChangedCalls);
        Assert.Empty(fixture.Chain.Traces);

        // :L532  clear the bit.
        Assert.Equal((int)RetCode.OK, fixture.Chain.EnableEvent(EventGate.EID_ITEMCHANGE));
        Assert.False(fixture.Session.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        fixture.Observer.Clear();

        // :L250  the `case else` arm forcibly rewrites its result to 2 so the DataWindow will not
        // re-apply the current edit text over the buffer - the arm that reached :L247 on the way.
        Assert.Equal(
            (long)ItemChangeResult.RestoreAndRejectText,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

        // NOW BOTH ARE OBSERVED. Step one of :L313-L315 ran, for the row and column that changed.
        (long row, string column) = Assert.Single(fixture.Services.ColumnExp.ItemChangedCalls);

        Assert.Equal(Row, row);
        Assert.Equal(ColumnName, column);

        // And the trace the engine raises per evaluation [:L32, n_cst_dwsvc_columnexp.sru:L753-L755]
        // travelled the production member.
        (long tracedRow, string tracedColumn, string stack, string expression, string value) =
            Assert.Single(fixture.Chain.Traces);

        Assert.Equal(Row, tracedRow);
        Assert.Equal(ColumnName, tracedColumn);
        Assert.Equal(GateColumnExpressionService.TraceStack, stack);
        Assert.Equal(GateColumnExpressionService.TraceExpression, expression);
        Assert.Equal(GateColumnExpressionService.TraceValue, value);

        // The carrying event ran, and reported the forward.
        Assert.True(fixture.Observer.Single(EventId.Ondoitemchanged).Dispatch.ColumnExpressionHandlerRan);
    }

    [Fact]
    public void TheGateAndTheEnabledFlagAreTwoIndependentGuardsAndBothArePreserved()
    {
        // :L313 IS A SECOND, SEPARATE GUARD. With the gate OPEN and the service DISABLED the item-change
        // path runs in full - `ondoitemchanged` fires and reports itself - yet no evaluation happens.
        // Collapsing the two into one condition would lose a distinction the oracle draws, and it would
        // also make a disabled service indistinguishable from a gated event in the dispatch report.
        GateFixture fixture = NewFixture();

        fixture.Services.ColumnExp.Enabled = false;
        fixture.Host.ItemChangedHandler = (_, _, _) => (long)ItemChangeResult.Default;

        Assert.Equal(
            (long)ItemChangeResult.RestoreAndRejectText,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

        // The path ran ...
        Assert.True(fixture.Observer.Saw(EventId.Ondoitemchanged));
        Assert.False(fixture.Observer.Single(EventId.Ondwnitemchange).Dispatch.GatedOut);

        // ... and the evaluation did not.
        Assert.Empty(fixture.Services.ColumnExp.ItemChangedCalls);
        Assert.Empty(fixture.Chain.Traces);
        Assert.False(
            fixture.Observer.Single(EventId.Ondoitemchanged).Dispatch.ColumnExpressionHandlerRan);
    }

    [Fact]
    public void TheOtherTwoBitsDoNotSuppressColumnExpressionEvaluation()
    {
        // THE COUPLING IS EID_ITEMCHANGE'S ALONE. The oracle attaches the warning to :L43 and to no
        // other constant, and the mechanism confirms why: neither :L124 nor :L176 lies on the path to
        // :L247. A port that suppressed evaluation from the row-focus or item-focus bit would be
        // inventing a coupling.
        GateFixture fixture = NewFixture(
            EventGate.EID_ROWFOCUSCHANGE | EventGate.EID_ITEMFOCUSCHANGE);

        fixture.Host.ItemChangedHandler = (_, _, _) => (long)ItemChangeResult.Default;

        Assert.Equal(
            (long)ItemChangeResult.RestoreAndRejectText,
            fixture.Chain.OnDwnItemChange(Row, fixture.Dwo, OriginalValue));

        Assert.Single(fixture.Services.ColumnExp.ItemChangedCalls);
        Assert.Single(fixture.Chain.Traces);
    }

    // ==============================================================================================
    //  PHASE 5 - WHERE THE MASK LIVES
    //
    //  The gate owns the VOCABULARY and the OPERATIONS. It does not own the VALUE. In the oracle the
    //  value is a private instance field,
    //
    //      //禁用的事件
    //      long _nDisabledEvent                                            [se_cst_dw.sru:L88-L89]
    //
    //  one of the FOUR cross-event fields at :L89-L96 that the event chain reads and writes BETWEEN
    //  events, and in this port that field belongs to Domain/ValidationSession.cs - because a stateless
    //  request boundary has nowhere else to put it (AAP 0.6.1.3).
    // ==============================================================================================

    [Fact]
    public void TheGateHoldsNoMaskAndNoMutableStateOfAnyKind()
    {
        // A mask stored on a static type would be shared by every session in the process, which is a
        // behavioural change of the worst kind: two unrelated DataWindows would silently gate each
        // other's events. So the type must carry NO instance field and NO mutable static field - only
        // the three compile-time literals of :L41-L43.
        Assert.Empty(typeof(EventGate).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

        Assert.All(
            typeof(EventGate).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
            field => Assert.True(field.IsLiteral));

        // Every operation therefore RECEIVES the mask rather than reading one, which is what makes these
        // tests order independent.
        Assert.All(
            new[]
            {
                GateMethod(nameof(EventGate.IsEventDisabled)),
                GateMethod(nameof(EventGate.DisableEvent)),
                GateMethod(nameof(EventGate.EnableEvent))
            },
            method =>
            {
                Assert.True(method.IsStatic);
                Assert.Equal(2, method.GetParameters().Length);
            });
    }

    [Fact]
    public void TheMaskAndTheOtherThreeCrossEventFieldsLiveOnTheValidationSession()
    {
        // THE FOUR FIELDS OF :L89-L96, pinned as a set. The port drops the Hungarian prefix the oracle
        // uses - `_nDisabledEvent` [:L89] becomes `_disabledEvent`, `_bDoItemChange` [:L92] becomes
        // `_doItemChange`, `_bDwnItemValidationError` [:L94] becomes `_inItemValidationError` and
        // `_nItemChangeRetCode` [:L96] becomes `_itemChangeRetCode` - because a field name is NOT one of
        // the identifiers AAP 0.4.5.3 preserves verbatim: it is private, so it never appears in a
        // serialized payload, a log record or a characterization recording.
        Assert.Equal(typeof(uint), SessionField("_disabledEvent").FieldType);
        Assert.Equal(typeof(bool), SessionField("_doItemChange").FieldType);
        Assert.Equal(typeof(bool), SessionField("_inItemValidationError").FieldType);
        Assert.Equal(typeof(long), SessionField("_itemChangeRetCode").FieldType);

        // AND THE MASK IS PRIVATE, exactly as :L87-L89 declares it. Its read-only projection is the
        // public surface, so nothing outside the session can assign it except through the two mutators.
        Assert.True(SessionField("_disabledEvent").IsPrivate);
        Assert.Equal(typeof(uint), typeof(ValidationSession)
            .GetProperty(nameof(ValidationSession.DisabledEvent))!
            .PropertyType);
        Assert.Null(typeof(ValidationSession)
            .GetProperty(nameof(ValidationSession.DisabledEvent))!
            .SetMethod);
    }

    [Theory]
    [MemberData(nameof(EveryBitAndEveryCombination))]
    public void TheSessionRoutesEveryOperationThroughTheGateAndStoresTheResult(uint evt)
    {
        ValidationSession session = NewSession();

        Assert.Equal(0u, session.DisabledEvent);
        Assert.False(session.IsEventDisabled(evt));

        // :L508 through the session's own field.
        Assert.Equal(RetCode.OK, session.DisableEvent(evt));
        Assert.Equal(evt, session.DisabledEvent);
        Assert.True(session.IsEventDisabled(evt));

        // The gate, given the same mask, must answer identically - that agreement is the whole
        // separation of concerns: the session stores, the gate decides.
        Assert.Equal(
            EventGate.IsEventDisabled(session.DisabledEvent, evt),
            session.IsEventDisabled(evt));

        // :L532 through the session's own field.
        Assert.Equal((int)RetCode.OK, session.EnableEvent(evt));
        Assert.Equal(0u, session.DisabledEvent);
    }

    [Fact]
    public void TheSessionsZeroGuardLeavesItsStoredMaskUntouchedJustAsTheGateDoes()
    {
        // :L506 and :L530 reached through the session, so the "left untouched" property is checked on
        // the STORED field and not merely on a local passed by reference.
        ValidationSession session = NewSession(EventGate.EID_ITEMCHANGE);

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, session.DisableEvent(0u));
        Assert.Equal(EventGate.EID_ITEMCHANGE, session.DisabledEvent);

        Assert.Equal((int)RetCode.E_INVALID_ARGUMENT, session.EnableEvent(0u));
        Assert.Equal(EventGate.EID_ITEMCHANGE, session.DisabledEvent);
    }

    [Fact]
    public void TheMaskIsPerSessionSoDisablingInOneCannotAffectAnother()
    {
        // THE REASON THE SESSION EXISTS (AAP 0.6.1.3). In the oracle the mask is a PRIVATE INSTANCE
        // FIELD on the control - `long _nDisabledEvent` [se_cst_dw.sru:L88-L89] - so two DataWindows on
        // one window cannot gate each other. Across a stateless boundary the equivalent is per
        // CORRELATED SESSION, and if the mask had been stored anywhere shared this is the test that
        // would fail.
        ValidationSession first = new("gate-session-1", "dw-1", 0u, new SessionLifetimeOptions());
        ValidationSession second = new("gate-session-2", "dw-2", 0u, new SessionLifetimeOptions());

        Assert.Equal(RetCode.OK, first.DisableEvent(EventGate.EID_ITEMCHANGE));

        Assert.True(first.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.False(second.IsEventDisabled(EventGate.EID_ITEMCHANGE));
        Assert.Equal(0u, second.DisabledEvent);

        // And the other direction, on a different bit, so neither session is special.
        Assert.Equal(RetCode.OK, second.DisableEvent(EventGate.EID_ROWFOCUSCHANGE));

        Assert.False(first.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
        Assert.Equal(EventGate.EID_ITEMCHANGE, first.DisabledEvent);
        Assert.Equal(EventGate.EID_ROWFOCUSCHANGE, second.DisabledEvent);
    }

    [Fact]
    public void TwoChainsOverTwoSessionsGateIndependentlyOfEachOther()
    {
        // The same isolation observed END TO END rather than on the session alone: one chain's gated-out
        // row-focus event [se_cst_dw.sru:L124] must not silence the other chain's, because each reads
        // its OWN instance field [:L89].
        GateFixture gated = NewFixture(EventGate.EID_ROWFOCUSCHANGE);
        GateFixture open = NewFixture();

        Assert.Equal(RetCode.OK, gated.Chain.OnDwnRowChange(Row));
        Assert.Equal(RetCode.OK, open.Chain.OnDwnRowChange(Row));

        Assert.Empty(SemanticEvents(gated));
        Assert.Contains("Event RowFocusChanged", SemanticEvents(open));

        Assert.True(gated.Observer.Single(EventId.Ondwnrowchange).Dispatch.GatedOut);
        Assert.False(open.Observer.Single(EventId.Ondwnrowchange).Dispatch.GatedOut);
    }

    [Fact]
    public void TheMaskSurvivesAcrossEveryCallWithinOneSession()
    {
        // The mask is CROSS-EVENT state [:L89-L96]: it is set by one call and read by a later one, which
        // is the entire reason it cannot live on a request. Here it is set once and then honoured by
        // three separate dispatches, and it is still there afterwards.
        GateFixture fixture = NewFixture();

        Assert.Equal(RetCode.OK, fixture.Chain.DisableEvent(EventGate.EID_ROWFOCUSCHANGE));

        for (int dispatch = 0; dispatch < 3; dispatch++)
        {
            Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChange(Row));
            Assert.Equal(RetCode.OK, fixture.Chain.OnDwnRowChanging(Row, Row + 1L));
        }

        Assert.Empty(SemanticEvents(fixture));
        Assert.All(fixture.Observer.Outcomes, outcome => Assert.True(outcome.Dispatch.GatedOut));
        Assert.Equal(EventGate.EID_ROWFOCUSCHANGE, fixture.Session.DisabledEvent);

        // An unguarded event in between changes nothing about it.
        _ = fixture.Chain.OnDwnRButtonUp(1L, 2L, Row, fixture.Dwo);

        Assert.Equal(EventGate.EID_ROWFOCUSCHANGE, fixture.Session.DisabledEvent);
        Assert.True(fixture.Chain.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE));
    }

    [Fact]
    public void TheMaskIsDiscardedWithTheSessionAndReportedOneLastTimeOnTheWayOut()
    {
        // CLOSING IS NET-NEW: in the oracle the four fields of :L89-L96 simply die with the control, so
        // there is no `of_close` to port and a server-held session needs a defined end. Two properties are
        // asserted, and they pull in opposite directions on purpose.
        ValidationSessionRegistry registry = NewRegistry();

        ValidationSessionOpenResult opened = registry.Open("dw-1", EventGate.EID_ITEMCHANGE);

        Assert.Equal(RetCode.OK, opened.ReturnCode);
        ValidationSession session = Assert.IsType<ValidationSession>(opened.Session);
        Assert.True(session.IsEventDisabled(EventGate.EID_ITEMCHANGE));

        ValidationSessionCloseResult closed = registry.Close(session.SessionId);

        Assert.Equal(RetCode.OK, closed.ReturnCode);
        Assert.True(closed.WasOpen);

        // FIRST: the mask is REPORTED at the moment of closure rather than zeroed. Closing releases the
        // session, and the final state exists precisely so a caller can see an outstanding gate rather
        // than having the evidence destroyed.
        Assert.Equal((long)EventGate.EID_ITEMCHANGE, closed.FinalState.DisabledEventMask);

        // SECOND: the mask is nonetheless gone, because the registry has dropped its only reference. A
        // later correlation identifier cannot resolve it, so nothing inherits the gate.
        Assert.False(registry.TryGet(session.SessionId, out ValidationSession? resolved));
        Assert.Null(resolved);
        Assert.Equal(RetCode.E_INVALID_HANDLE, registry.Resolve(session.SessionId).ReturnCode);
        Assert.False(session.IsOpen);

        // A NEW session over the SAME DataWindow starts with nothing disabled - the gate is not sticky.
        ValidationSessionOpenResult reopened = registry.Open("dw-1");

        Assert.Equal(RetCode.OK, reopened.ReturnCode);
        Assert.Equal(0u, Assert.IsType<ValidationSession>(reopened.Session).DisabledEvent);
    }

    [Theory]
    [MemberData(nameof(EveryBitAndEveryCombination))]
    public void ASessionOpenedWithASeededMaskCarriesExactlyThatMask(uint mask)
    {
        // The mask is the ONE legacy field a caller may seed, because `of_disableevent` [:L110] can be
        // called before any event fires. The other three start at the values PowerScript gives a fresh
        // instance: false, false and zero.
        ValidationSessionRegistry registry = NewRegistry();

        ValidationSessionOpenResult opened = registry.OpenWithId("seeded", "dw-1", mask);

        Assert.Equal(RetCode.OK, opened.ReturnCode);

        ValidationSession session = Assert.IsType<ValidationSession>(opened.Session);
        ValidationSessionSnapshot state = session.CaptureState();

        Assert.Equal(mask, session.DisabledEvent);
        Assert.Equal(mask, (uint)state.DisabledEventMask);
        Assert.False(state.InItemChange);
        Assert.False(state.InItemValidationError);
        Assert.Equal(0L, state.RawItemChangeRetCode);
    }

    // ==============================================================================================
    //  FIXTURE AND HELPERS
    // ==============================================================================================

    /// <summary>One assembled chain plus everything an assertion needs to observe it.</summary>
    /// <param name="Chain">The chain under test.</param>
    /// <param name="Observer">The dispatch recorder.</param>
    /// <param name="Session">The session holding the mask [<c>se_cst_dw.sru:L89</c>].</param>
    private sealed record GateFixture(
        GateEventChain Chain,
        GateEventObserver Observer,
        ValidationSession Session)
    {
        /// <summary>The composed fake host, whose call log is the "reached nothing" evidence.</summary>
        internal FakeDataWindowHost Host => Chain.Host;

        /// <summary>The five attached-service doubles the chain is holding.</summary>
        internal GateServiceFactory Services => Chain.Services;

        /// <summary>The one column every event in this suite is addressed to.</summary>
        internal IDataWindowObject Dwo => Host.DwObject(ColumnName);
    }

    /// <summary>Builds a session with the given initial mask and a deterministic lifetime.</summary>
    /// <param name="disabledMask">The initial disabled-event mask [<c>se_cst_dw.sru:L89</c>].</param>
    /// <returns>The session.</returns>
    private static ValidationSession NewSession(uint disabledMask = 0u) =>
        new("gate-session", "dw-gate", disabledMask, new SessionLifetimeOptions());

    /// <summary>
    /// Builds a registry with an explicit lifetime, so nothing in this suite depends on a configured
    /// default.
    /// </summary>
    /// <returns>The registry.</returns>
    private static ValidationSessionRegistry NewRegistry()
    {
        DataServicesOptions options = new();
        options.Sessions.ValidationSession.IdleTimeout = TimeSpan.FromMinutes(5);
        options.Sessions.ValidationSession.MaxConcurrentSessions = 8;

        return new ValidationSessionRegistry(options, i18n: null, timeProvider: null);
    }

    /// <summary>
    /// Builds a chain over a one-column, one-row fixture with the cursor on row one, and clears the
    /// host's call log so that an emptiness assertion measures only what the dispatch under test did.
    /// </summary>
    /// <param name="disabledMask">The initial disabled-event mask.</param>
    /// <returns>The fixture.</returns>
    private static GateFixture NewFixture(uint disabledMask = 0u)
    {
        GateEventObserver observer = new();
        GateServiceFactory services = new();
        ValidationSession session = NewSession(disabledMask);
        GateEventChain chain = new(session, services, observer);

        chain.Host.AddColumn(ColumnName, ColumnType);
        chain.Host.AddRow(OriginalValue);
        chain.Host.CurrentRow = Row;
        chain.Host.CallLog.Clear();

        return new GateFixture(chain, observer, session);
    }

    /// <summary>Subscribes a counting subscriber to one broker topic.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="topic">The topic, one of the chain's <c>EVT_*</c> constants.</param>
    /// <returns>The subscriber.</returns>
    private static GateTopicSubscriber Subscribe(GateFixture fixture, string topic)
    {
        GateTopicSubscriber subscriber = new();

        Assert.Equal(
            RetCode.OK,
            fixture.Chain.On(topic, subscriber, nameof(GateTopicSubscriber.OnTopic)));

        return subscriber;
    }

    /// <summary>The semantic events the fake host recorded, in order.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <returns>The recorded semantic-event members.</returns>
    private static IReadOnlyList<string> SemanticEvents(GateFixture fixture) =>
        [.. fixture.Host.CallLog.Members.Where(member =>
            member.StartsWith("Event ", StringComparison.Ordinal))];

    /// <summary>
    /// Raises one of the four guarded raw events with the arguments its own signature declares.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="eventId">Which guarded event to raise.</param>
    /// <param name="dwo">The column the two item events are addressed to.</param>
    /// <returns>The value the handler returned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="eventId"/> is not one of the four guarded events.
    /// </exception>
    /// <remarks>
    /// FAIL LOUDLY ON AN UNKNOWN IDENTIFIER rather than returning a benign zero: a silent default here
    /// would let a mis-typed matrix row pass as a gated-out dispatch, which is the exact outcome these
    /// tests exist to detect.
    /// </remarks>
    private static long Raise(GateFixture fixture, EventId eventId, IDataWindowObject dwo) => eventId
        switch
        {
            // :L124  ondwnrowchange(currentRow)
            EventId.Ondwnrowchange => fixture.Chain.OnDwnRowChange(Row),

            // :L130  ondwnrowchanging(currentRow, newRow)
            EventId.Ondwnrowchanging => fixture.Chain.OnDwnRowChanging(Row, Row + 1L),

            // :L176  ondwnitemchangefocus(row, dwo)
            EventId.Ondwnitemchangefocus => fixture.Chain.OnDwnItemChangeFocus(Row, dwo),

            // :L187  ondwnitemchange(row, dwo, data)
            EventId.Ondwnitemchange => fixture.Chain.OnDwnItemChange(Row, dwo, OriginalValue),

            _ => throw new ArgumentOutOfRangeException(
                nameof(eventId),
                eventId,
                "Not one of the four guarded events of se_cst_dw.sru:L124, :L130, :L176 and :L187."),
        };

    /// <summary>
    /// Resolves one of the gate's operations by name. The members are <see langword="internal"/> by
    /// design - the service's published surface is contract C-03 - so the lookup is non-public.
    /// </summary>
    /// <param name="member">The member name.</param>
    /// <returns>The method.</returns>
    private static MethodInfo GateMethod(string member)
    {
        MethodInfo? method = typeof(EventGate).GetMethod(
            member,
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        return method;
    }

    /// <summary>Resolves one of the session's three gate operations by name.</summary>
    /// <param name="member">The member name.</param>
    /// <returns>The method.</returns>
    private static MethodInfo SessionGateMethod(string member)
    {
        MethodInfo? method = typeof(ValidationSession).GetMethod(
            member,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            [typeof(uint)],
            modifiers: null);

        Assert.NotNull(method);

        return method;
    }

    /// <summary>Resolves one of the session's private cross-event state fields by name.</summary>
    /// <param name="field">The field name.</param>
    /// <returns>The field.</returns>
    private static FieldInfo SessionField(string field)
    {
        FieldInfo? resolved = typeof(ValidationSession).GetField(
            field,
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(resolved);

        return resolved;
    }

    /// <summary>Resolves one of the shared bit primitives by name.</summary>
    /// <param name="member">The member name.</param>
    /// <returns>The method.</returns>
    private static MethodInfo SharedBitMethod(string member)
    {
        MethodInfo? method = typeof(Bits).GetMethod(member, BindingFlags.Static | BindingFlags.Public);

        Assert.NotNull(method);

        return method;
    }

    /// <summary>Every overload of one shared bit primitive.</summary>
    /// <param name="member">The member name.</param>
    /// <returns>The overloads.</returns>
    private static IReadOnlyList<MethodInfo> SharedBitOverloads(string member) =>
        [.. typeof(Bits)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(candidate => string.Equals(candidate.Name, member, StringComparison.Ordinal))];
}
