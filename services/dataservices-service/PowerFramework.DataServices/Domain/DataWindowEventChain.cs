// ==================================================================================================
//  DataWindowEventChain - THE 22-EVENT RAW/SEMANTIC DELEGATION CHAIN
//  ------------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru  (616 lines)
//                   :L11-L32    all 22 events - 9 SEMANTIC against 13 RAW `pbm_dwn*`
//                   :L47-L76    the twelve EVT_* broker topics
//                   :L80-L84    the five attached services
//                   :L115-L401  every raw handler body and the two semantic bodies
//                   :L403-L414  the `filter` override
//                   :L416-L446  the `deleterow` override
//                   :L448-L467  the seven of_on / of_off pass-throughs
//                   :L469-L535  of_iseventdisabled / of_disableevent / of_enableevent
//                   :L537-L558  _of_postaccepttext, the deferred accept body
//                   :L560-L589  create / destroy / onpreconstructor / ondestructor
//                   :L591-L615  the NESTED broker subclass `eventful`
//
//  ORACLE STATUS  se_cst_dw.sru is READ ONLY and is the behavioural oracle for parity testing, never
//                 an edit target (constraint C-C). It is also the ONLY specification: logfile.md
//                 stops at framework 3.0.7.2062 while the commit history runs years later, and the
//                 two PowerBuilder build definitions contradict each other and both name
//                 pfw.utility.imgcodec.pbl, which exists nowhere in the repository, so neither would
//                 build as written. Nothing else in the tree can adjudicate a behavioural question,
//                 which is why every claim below carries the ws_objects/** locator it came from.
//
//                 MEASURED ON THE SOURCE, NOT INFERRED. `grep -cE 'native |external function'`
//                 returns 0, so this is a pure logic port with no native-substitution problem
//                 anywhere in it. `grep -c '\^'` returns 0, which is the machine-checkable half of
//                 the negative constraint recorded under THE `.^persistent` NON-APPEND below.
//
//  WHY THIS FILE CARRIES THE MOST RISK IN THE REFACTOR
//  ------------------------------------------------------------------------------------------------
//  AAP 0.6.1 names EVENT-ORDERING PRESERVATION the single highest risk in the whole decomposition
//  (goal G5) and cites precisely the divergence ported here: se_cst_dw's raw `pbm_dwn*` chain against
//  its semantic chain. Ordering in this file is not a quality attribute - IT IS THE BEHAVIOUR. Two
//  events read state the previous event wrote, one event fires a nested event from inside itself, one
//  handler's result is a function of its predecessor's return value, and two of the twelve topics
//  encode their dispatch position in the spelling of their own name.
//
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  ------------------------------------------------------------------------------------------------
//  OWNS
//      * All 22 events. Seven of the nine semantic ones are DECLARED here; the other two
//        (OnDoItemChange, OnDoItemChanged) are declared on DataWindowServiceHost because attached
//        services raise them, and are OVERRIDDEN here with their se_cst_dw bodies. All 13 raw
//        `pbm_dwn*` handlers are here in full.
//      * The twelve EVT_* topic constants, their SubscriptionTopic projections, and the dispatch
//        order that follows from the spelling of two of them.
//      * The five attached services: their creation order, their DIVERGENT initialisation order, and
//        their teardown.
//      * The nested broker subclass and its argument-injection hook.
//      * The `filter` and `deleterow` overrides.
//      * The per-capability-area ordering discipline of AAP 0.6.1.4 and the sequencing token that
//        expresses it.
//
//  DOES NOT OWN, AND MUST NOT ACQUIRE
//      * The EID_* bit vocabulary and the three mask operations   -> Domain/EventGate.cs
//      * The {0,1,2,3} item-change alphabet and its micro-protocol -> Domain/ItemChangeProtocol.cs
//      * The four cross-event mutable state fields [:L89-L96], the validation-error handler
//        [:L322-L385] and the deferred-accept body [:L537-L558] -> Domain/ValidationSession.cs
//      * The DataWindow member surface and the eleven ancestry semantic events
//                                                              -> Domain/DataWindowServiceHost.cs
//      * Anything under Expressions/, Services/, Grpc/ or Endpoints/. THEY CONSUME THIS FILE. The five
//        attached services are reached exclusively through the abstractions declared below, so the
//        dependency direction is one-way and this file names no type in any of those folders.
//      * Any storage provider, connection, SQL text or EF Core type (constraint C-E).
//      * Any key, password, token or credential literal (constraint C-F). A sequencing token is an
//        ORDERING DEVICE AND NOT A CREDENTIAL; this file opens no boundary and authenticates nothing,
//        which the stock JwtBearer handler does at Grpc/ and Endpoints/ (constraint C-G).
//
//  This chain stays INSIDE PowerFramework.DataServices and is never promoted to shared/ (constraint
//  C-A). The only permitted cross-service coupling is PowerFramework.Contracts, which is a boundary
//  definition and not a back door for shared behaviour - and the only two types consumed from it here
//  are the published EventId and OrderingDiscipline enums, aliased below.
//
//  THE COUNTS ARE COUNTED FROM THE SOURCE, NOT INFERRED
//  ------------------------------------------------------------------------------------------------
//      22  events                 :L11-L32   9 semantic + 13 raw, verified by line count
//      12  broker topics          :L47-L76   verified by `grep -c 'constant string EVT_'`
//       7  of_on / of_off         :L448-L467 one of_on and SIX of_off arities
//       3  IsValid(this) guards   :L138 :L147 :L152   - the folder brief named two; there are THREE
//       4  event-gate guard sites :L124 :L130 :L176 :L187
//       4  OnPrepare exclusions   :L599-L603 - checks 3 and 4 are REDUNDANT and are PRESERVED
//       2  broker-only raw events :L120 ondwnrbuttonup AND :L395 ondwnlbuttonup
//       2  divergent lifecycle orders  :L570-L574 creation against :L576-L580 initialisation
//
//  DECISIONS (constraint C-K: every technology-specific and boundary-specific decision recorded)
//  ------------------------------------------------------------------------------------------------
//  DECISION 1 - THE FIVE ATTACHED SERVICES ARE REACHED THROUGH ABSTRACTIONS DECLARED HERE.
//      The legacy declares them as concrete types [:L80-L84] and creates them itself [:L570-L574].
//      Three of the five live in folders that CONSUME this file - Services/ and Expressions/ - so a
//      concrete reference would invert the dependency direction. The resolution is the five
//      interfaces below plus IDataWindowAttachedServiceFactory: the chain still creates all five, in
//      the legacy's own order, and still initialises them in the legacy's own DIFFERENT order, but
//      the types it creates arrive from the factory. Each interface carries ONLY the members the
//      chain actually consumes, which is the same consumption criterion AAP 0.4.2.5 applies to the
//      host contract - measured at three hooks in total: OnFiltered [:L409], OnEditChanged [:L170]
//      and OnItemChanged [:L314].
//
//  DECISION 2 - THE OBSERVABLE OUTCOME OF A DISPATCH IS PUSHED TO AN OBSERVER, NOT STORED.
//      Contract C-03 requires more from a dispatch than its numeric return: OrderedDispatchReport
//      must say whether the gate short-circuited, whether the liveness guard stopped the broker edge,
//      whether the topic had a subscriber, and which of the three conditional steps of
//      `ondoitemchanged` ran; EventResult must carry the semantic edge and the broker edge SEPARATELY
//      so a consumer can tell which one stopped the dispatch. A `LastOutcome` property would be
//      wrong twice over: `ondwnitemchange` fires a NESTED event from inside itself [:L207], so the
//      inner dispatch would overwrite the outer one's record, and mutable per-instance state is not
//      something a test can drive deterministically. An observer sees every dispatch, nested ones
//      included, in the order they happened. The public event members keep returning the legacy
//      numeric verbatim, so no call site changes shape.
//
//  DECISION 3 - CONTROL FLOW TESTS THE RETURN-CODE ALPHABET; THE REPORT CARRIES THE TRI-VALUED VETO.
//      The oracle spells every prevent test `= 1` [:L115, :L116, :L125, :L131, :L132, :L136, :L139,
//      :L145, :L148, :L164, :L167, :L177, :L178, :L395]. That 1 is RetCode.PREVENT
//      [ws_objects/pfw.shared.pbl.src/retcode.sru:L42] - the RETURN-CODE alphabet. The wire contract
//      reports the same edge as Veto.Result, which is TRI-VALUED. Both are honoured without
//      conflating them: the branch is `== RetCode.PREVENT`, and the report is produced by the
//      explicit ToVetoResult mapping below, which preserves 2 as PreventDeep instead of downgrading
//      it. FIVE NUMERIC ALPHABETS SHARE THE NUMERALS 1 AND 2 in this area - VetoResult, the
//      exception hook's 1=prevent/2=continue, RetCode.PREVENT, the {0,1,2,3} item-change alphabet,
//      and SetEnabled's veto-to-RetCode.FAILED mapping - so no conversion between any two of them is
//      ever implicit here.
//
//  DECISION 4 - THE 12 TOPIC CONSTANTS KEEP THEIR LEGACY SCREAMING_SNAKE SPELLING AND VALUES.
//      AAP 0.4.5.3: those identifiers and values appear in serialized payloads, in log records and in
//      characterization recordings, so a rename silently invalidates every stored comparison. The
//      values matter even more than the identifiers, because TWO OF THEM ENCODE DISPATCH ORDER IN
//      THEIR OWN SPELLING - see THE FUSED TOPIC STRING below. The repository-root .editorconfig
//      carries a BAND 3 section scoped to this exact path that lowers CA1707 and IDE1006 to none.
//
//  DECISION 5 - THE DROPPED SUPER-CALLS ARE A DOCUMENTED CAPABILITY GAP, NOT AN OMISSION.
//      `:L570` opens with `call super::onpreconstructor` and `:L583` with `call super::ondestructor`.
//      In the legacy those run se_cst_datawindow's THEME REGISTRATION and UNREGISTRATION -
//      `IsPrevented(Event OnThemeRegistering())` then `ThemeManager().of_RegisterControl(this)`
//      [ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L82-L87] and
//      `ThemeManager().of_UnregisterControl(this)` [:L89-L90]. ThemeManager belongs to the DEFERRED
//      DesignSystem service, which constraint C-D forbids implementing even partially and even to
//      stub out, so both super-calls have NO ANALOGUE AND ARE DROPPED. The gap belongs to the
//      RESERVED `/v1/design/**` Gateway extension point (AAP 0.4.4), which exists precisely so this
//      is legible from the gateway's contract rather than invisible. AAP 0.8.1 states the governing
//      judgement: where the choice is between a partial implementation and a documented gap, THE
//      DOCUMENTED GAP WINS. Nothing observable to a headless service is lost, because registering a
//      control with a theme manager has no effect a gRPC or REST response can carry.
//
//  DECISION 6 - `Post` BECOMES A QUEUED CONTINUATION DRAINED BY THE HOST, NOT A MESSAGE PUMP.
//      `:L389` is `Post _of_PostAcceptText()`, which hands the call to the WIN32 MESSAGE QUEUE. AAP
//      0.6.5 marks that pump a deliberate non-port: a headless Linux container has no message pump.
//      The queue already exists on Domain/ValidationSession.cs, so this file introduces NO SECOND
//      MECHANISM - it calls TryQueueDeferredAccept and exposes DrainDeferredAccept, and the guard
//      `if Not _bDoItemChange` [:L388] lives inside the former where the flag it reads lives.
//
//  DECISION 7 - NO PERFORMANCE CLAIM IS MADE ANYWHERE IN THIS FILE.
//      AAP 0.8.5: the repository publishes no SLA, no latency budget, no throughput target and no
//      availability commitment. Nothing below is described as fast, low-latency or optimised, and no
//      shape - not the chain, not the streaming, not the sequencing tokens - is justified on
//      performance grounds. Every ordering choice here is justified by SEMANTICS and by a locator.
//
//  THE FUSED TOPIC STRING - WHY THIS EVENT MODEL DOES NOT SURVIVE NAIVE SERIALIZATION
//  ------------------------------------------------------------------------------------------------
//  Two of the twelve topics carry a leading digit IN THE VALUE: EVT_ITEMCHANGED is "0-itemchanged"
//  [:L54] and EVT_EDITCHANGED is "1-editchanged" [:L57]. The broker keeps its subscription registry
//  in ASCENDING ORDINAL ORDER OF THE SUBSCRIPTION NAME, so because ASCII digits sort before letters
//  those two occupy a deterministic position relative to each other and to every unprefixed topic.
//  THE DISPATCH ORDER IS A FUNCTION OF THE STRING'S SPELLING. A third encoding - a `.` namespace
//  suffix carrying subscription lifetime - is fused into the same string elsewhere in the framework.
//
//  So: DISPATCH AND SORT BY SubscriptionTopic.LegacyName, NEVER BY LogicalName. LegacyName is the
//  residual name exactly as the broker stores it, "0-itemchanged" included; LogicalName has the
//  ordering prefix REMOVED and is a presentation projection. Sorting by LogicalName would silently
//  reorder item-changed against edit-changed, and every message would still be well formed.
//  SubscriptionTopic guarantees a byte-exact ToLegacyString round-trip for both prefixed topics, so
//  the projections below are parsed by it rather than hand-split.
//
//  THE `.^persistent` NON-APPEND - A NEGATIVE CONSTRAINT WITH LOCATORS (constraint C-K)
//  ------------------------------------------------------------------------------------------------
//  NEVER append a lifetime or namespace suffix to a topic in this file, and never replicate the
//  threading layer's `if Pos(name,".") > 0` conditional. The seven of_on / of_off overloads
//  [:L448-L467] delegate STRAIGHT THROUGH to the broker with no suffix and no dot test at all.
//  `.^persistent` occurs at exactly SIX sites repository-wide and every one of them is in
//  PERSISTENCE's threading layer, not here:
//
//      ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544       and :L596
//      ws_objects/pfw.thread.pbl.src/n_cst_threading_task.sru:L385  and :L391
//      ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L552          and :L561
//
//  A grep for any `^` character in se_cst_dw.sru returns NOTHING AT ALL. The locators are recorded so
//  that a future contributor copying the threading pattern into this file is stopped by the evidence
//  rather than by taste, and the parity tests assert the absence over the whole topic set.
//
//  CONSTRAINTS THAT GOVERN THIS FILE
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided." Per AAP 0.7.1 that is a FINDING AND NOT
//  LATITUDE: the enterprise-standard baseline of AAP 0.7.2 applies in their place and the binding
//  constraints are AAP 0.7.3's non-rule set. The ones that bite here are C-A (no shared behaviour
//  crosses a service boundary), C-B (no new features and no behaviour improvements - every oddity
//  below is preserved and annotated), C-C (the legacy tree is a read-only oracle and every claim
//  carries its locator), C-D (no deferred service, even partially, even stubbed - DECISION 5),
//  C-E (no storage), C-F (nothing hardcoded), C-G (no boundary opened here), C-H (80% line coverage
//  per service, which is why every path below is drivable from a test with no live DataWindow),
//  C-I (independent build - only the six ProjectReferences the csproj already declares) and
//  C-K (every decision documented). AAP 0.4.5.4's translation hazards apply throughout: ONE-BASED
//  indices for `row`, `RowCount()`, the argument slot and the consumed count; a triggered event is a
//  method call returning a numeric code while a POSTED call is an explicitly queued continuation;
//  and NULL IS NEVER COLLAPSED TO ZERO.
// ==================================================================================================

using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

using EventId = PowerFramework.Contracts.DataServices.V1.EventId;
using OrderingDiscipline = PowerFramework.Contracts.DataServices.V1.OrderingDiscipline;

namespace PowerFramework.DataServices.Domain;

/// <summary>
/// The five services <c>se_cst_dw</c> attaches to itself
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L80-L84</c>), named so that the two
/// DIVERGENT lifecycle orders can be expressed as data and asserted by a test.
/// </summary>
/// <remarks>
/// The declaration order of this enum is the CREATION order [<c>:L570-L574</c>], which is also the
/// teardown order [<c>:L584-L588</c>]. It is deliberately NOT the initialisation order
/// [<c>:L576-L580</c>], which swaps positions three and four. See
/// <see cref="DataWindowEventChain.ServiceCreationOrder"/> and
/// <see cref="DataWindowEventChain.ServiceInitializationOrder"/>.
/// </remarks>
public enum DataWindowAttachedServiceKind
{
    /// <summary>
    /// <c>n_cst_dwsvc_contextmenu</c> [<c>se_cst_dw.sru:L80</c>]. Created first [<c>:L570</c>] and
    /// initialised first [<c>:L576</c>] - the one position the two orders agree on at the front.
    /// </summary>
    ContextMenu = 0,

    /// <summary>
    /// <c>n_cst_dwsvc_rowselect</c> [<c>se_cst_dw.sru:L81</c>]. Created and initialised second
    /// [<c>:L571</c>, <c>:L577</c>].
    /// </summary>
    RowSelect = 1,

    /// <summary>
    /// <c>n_cst_dwsvc_columnsort</c> [<c>se_cst_dw.sru:L82</c>]. Created THIRD [<c>:L572</c>] but
    /// initialised FOURTH [<c>:L579</c>].
    /// </summary>
    ColumnSort = 2,

    /// <summary>
    /// <c>n_cst_dwsvc_dropdownsearch</c> [<c>se_cst_dw.sru:L83</c>]. Created FOURTH [<c>:L573</c>]
    /// but initialised THIRD [<c>:L578</c>].
    /// </summary>
    DropDownSearch = 3,

    /// <summary>
    /// <c>n_cst_dwsvc_columnexp</c> [<c>se_cst_dw.sru:L84</c>]. Created last [<c>:L574</c>] and
    /// initialised last [<c>:L580</c>].
    /// </summary>
    ColumnExp = 4
}

/// <summary>
/// The chain-facing view of one attached DataWindow service: the two members
/// <c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c> consumes on all five of them.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 1 in the file header. The legacy types are concrete [<c>:L80-L84</c>], and three of the
/// five are ported into folders that CONSUME this file, so naming them concretely here would invert
/// the dependency direction. This interface plus its five derivations carry only what the chain
/// actually uses, which is the consumption criterion AAP 0.4.2.5 sets.
/// </para>
/// <para>
/// Both members already exist with these exact signatures on
/// <see cref="DataWindowServiceBase"/>, the port of <c>n_cst_dwsvc</c>'s host-facing half, so a real
/// service satisfies this contract by DECLARING it and adding nothing. That is deliberate: an
/// adapter layer between the chain and its services would be a second place for the enablement
/// protocol to drift.
/// </para>
/// <para>
/// <b><see cref="IDisposable"/> IS DELIBERATELY NOT REQUIRED.</b> The legacy teardown is an
/// unconditional <c>Destroy</c> on all five [<c>:L584-L588</c>], and a PowerBuilder object with no
/// destructor script does nothing observable when destroyed. Requiring disposal here would force
/// every implementation to add an empty method to satisfy a contract the oracle does not have, so
/// <see cref="DataWindowEventChain.Teardown"/> disposes each service ONLY IF it implements
/// <see cref="IDisposable"/> - which is exactly the set of services whose legacy counterpart has a
/// destructor body, <c>n_cst_dwsvc_columnexp</c>'s <c>Destroy _vecCalcStack</c>
/// [<c>n_cst_dwsvc_columnexp.sru:L2425</c>] being the one that does.
/// </para>
/// </remarks>
public interface IDataWindowAttachedService
{
    /// <summary>
    /// Whether the service is enabled - the port of the <c>#Enabled</c> read the chain performs
    /// before every one of the three service hooks it invokes [<c>se_cst_dw.sru:L169</c>,
    /// <c>:L313</c>, <c>:L408</c>].
    /// </summary>
    /// <remarks>
    /// Read-only from the chain's side. The legacy mutator is <c>of_setenabled</c>
    /// [<c>n_cst_dwsvc.sru:L89-L95</c>], which belongs to
    /// <see cref="DataWindowServiceBase.SetEnabled(in bool)"/> together with its vetoable
    /// <c>onenable</c> hook; the chain never sets it, so widening this member would advertise an
    /// authority the oracle does not give it.
    /// </remarks>
    bool Enabled { get; }

    /// <summary>
    /// Attaches the service to its host - the port of <c>Event OnInit(this)</c>
    /// (<c>se_cst_dw.sru:L576-L580</c>, declared <c>n_cst_dwsvc.sru:L9</c>).
    /// </summary>
    /// <param name="dw">
    /// The host. The legacy hook names the DERIVED type <c>se_cst_dw</c>, and taking the base
    /// contract instead is what stops DataServices inheriting the DesignSystem inheritance edge
    /// (AAP 0.2.1.3 Correction 3). <see cref="DataWindowEventChain"/> derives from that contract, so
    /// nothing is lost.
    /// </param>
    /// <remarks>
    /// The legacy body reads the broker straight off the host - <c>#Eventful = dw.Eventful</c>
    /// [<c>n_cst_dwsvc.sru:L86</c>] - which is why the chain creates its broker BEFORE it creates and
    /// initialises any service.
    /// </remarks>
    void OnInit(DataWindowServiceHost dw);
}

/// <summary>
/// The context-menu service, <c>n_cst_dwsvc_contextmenu</c> [<c>se_cst_dw.sru:L80</c>].
/// </summary>
/// <remarks>
/// <para>
/// CARRIES NO HOOK OF ITS OWN, AND THAT IS MEASURED RATHER THAN AN OVERSIGHT. The chain never calls
/// this service; the traffic runs the other way. The service subscribes ITSELF to two of the chain's
/// broker topics - <c>#DataWindow.of_On(#DataWindow.EVT_RBUTTONDOWN,this,"onRButtonDown")</c> and the
/// EVT_RBUTTONUP equivalent [<c>n_cst_dwsvc_contextmenu.sru:L1441-L1442</c>] - and it RAISES two of
/// the chain's semantic events back at it,
/// <see cref="DataWindowEventChain.OnInitContextMenu(long, IDataWindowObject)"/>
/// [<c>:L147</c>] and <see cref="DataWindowEventChain.OnContextMenu(long, IDataWindowObject, long)"/>
/// [<c>:L194</c>].
/// </para>
/// <para>
/// The interface exists anyway rather than the chain holding a bare
/// <see cref="IDataWindowAttachedService"/>, because the lifecycle is per-KIND: the factory creates
/// this one first and initialises it first, and a distinct type is what makes that assertable.
/// </para>
/// </remarks>
public interface IDataWindowContextMenuService : IDataWindowAttachedService
{
}

/// <summary>
/// The row-selection service, <c>n_cst_dwsvc_rowselect</c> [<c>se_cst_dw.sru:L81</c>].
/// </summary>
public interface IDataWindowRowSelectService : IDataWindowAttachedService
{
    /// <summary>
    /// Notifies the service that a filter has been applied - the port of
    /// <c>RowSelect.Event OnFiltered()</c> (<c>se_cst_dw.sru:L409</c>, declared
    /// <c>n_cst_dwsvc_rowselect.sru:L11</c>).
    /// </summary>
    /// <remarks>
    /// RETURNS NOTHING, AND THAT IS THE LEGACY DECLARATION. <c>event onfiltered ( )</c> carries no
    /// <c>type</c> clause, so there is no code to test and the raiser discards nothing. Giving it a
    /// return value would invent a veto the notification does not have. Raised by the chain ONLY when
    /// the base filter returned <c>1</c> AND the service is enabled [<c>:L407-L410</c>].
    /// </remarks>
    void OnFiltered();
}

/// <summary>
/// The column-sort service, <c>n_cst_dwsvc_columnsort</c> [<c>se_cst_dw.sru:L82</c>].
/// </summary>
/// <remarks>
/// Carries no hook of its own for the same reason
/// <see cref="IDataWindowContextMenuService"/> does not: it subscribes itself to EVT_CLICKED,
/// EVT_DOUBLECLICKED and EVT_LBUTTONUP [<c>n_cst_dwsvc_columnsort.sru:L442-L444</c>] rather than
/// being called. It is also the one service that drives the event gate from outside, through the
/// three host-facing delegations this chain exposes -
/// <c>#DataWindow.of_IsEventDisabled</c> [<c>:L409</c>], <c>of_DisableEvent</c> [<c>:L411</c>] and
/// <c>of_EnableEvent</c> [<c>:L425</c>].
/// </remarks>
public interface IDataWindowColumnSortService : IDataWindowAttachedService
{
}

/// <summary>
/// The drop-down search service, <c>n_cst_dwsvc_dropdownsearch</c> [<c>se_cst_dw.sru:L83</c>].
/// </summary>
public interface IDataWindowDropDownSearchService : IDataWindowAttachedService
{
    /// <summary>
    /// Notifies the service that the edit control's text changed - the port of
    /// <c>DropdownSearch.Event OnEditChanged(row,dwo,data)</c> (<c>se_cst_dw.sru:L170</c>, declared
    /// <c>n_cst_dwsvc_dropdownsearch.sru:L35</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being edited.</param>
    /// <param name="dwo">The column being edited.</param>
    /// <param name="data">The edit text, verbatim and UNESCAPED.</param>
    /// <remarks>
    /// <para>
    /// RETURNS NOTHING - <c>event oneditchanged ( long row, dwobject dwo, string data )</c> declares
    /// no <c>type</c>, so the raise at <c>:L170</c> is a statement and no veto is possible from here.
    /// </para>
    /// <para>
    /// THE LEGACY'S OWN SUBSCRIPTION FOR THIS HOOK IS COMMENTED OUT.
    /// <c>//#DataWindow.of_On(#DataWindow.EVT_EDITCHANGED,this,"onEditChanged")</c>
    /// [<c>n_cst_dwsvc_dropdownsearch.sru:L507</c>] sits inert immediately above the three live
    /// subscriptions at <c>:L508-L510</c>, which is exactly WHY the chain calls this hook DIRECTLY
    /// after the broker edge [<c>:L169-L171</c>] instead of the service receiving it as a subscriber.
    /// Carried as observed and not revived (constraint C-B).
    /// </para>
    /// </remarks>
    void OnEditChanged(long row, IDataWindowObject dwo, string data);
}

/// <summary>
/// The column-expression service, <c>n_cst_dwsvc_columnexp</c> [<c>se_cst_dw.sru:L84</c>].
/// </summary>
public interface IDataWindowColumnExpressionService : IDataWindowAttachedService
{
    /// <summary>
    /// Notifies the service that an item's value changed and bound expressions must recalculate -
    /// the port of <c>ColumnExp.Event OnItemChanged(row,dwo)</c> (<c>se_cst_dw.sru:L314</c>,
    /// declared <c>n_cst_dwsvc_columnexp.sru:L86</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    /// <remarks>
    /// <para>
    /// RETURNS NOTHING - <c>event onitemchanged ( long row, dwobject dwo )</c> declares no
    /// <c>type</c>.
    /// </para>
    /// <para>
    /// THIS IS STEP ONE OF THE THREE-STEP ORDER AT <c>:L313-L319</c> AND THE MECHANISM BEHIND THE
    /// EVENT-GATE COUPLING. Because it is invoked from inside <c>ondoitemchanged</c>, and because the
    /// <c>EID_ITEMCHANGE</c> guard at <c>:L187</c> short-circuits the only path that reaches
    /// <c>ondoitemchanged</c>, disabling item change ALSO stops every bound column expression from
    /// recalculating - silently, with no error anywhere. The oracle warns of it at <c>:L43</c> and it
    /// is preserved (constraint C-B); Domain/EventGate.cs documents it at the constant.
    /// </para>
    /// </remarks>
    void OnItemChanged(long row, IDataWindowObject dwo);
}

/// <summary>
/// Creates the five attached services, so the chain can reproduce <c>onpreconstructor</c>'s
/// <c>Create</c> sequence [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L570-L574</c>]
/// without naming a type in a folder that consumes it.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 1 in the file header. The legacy is five literal <c>Create</c> statements against
/// concrete classes; this is the same five creations behind five factory members, in the same order.
/// The chain calls each exactly once, from its constructor, and never re-creates a service - the
/// oracle has no path that does.
/// </para>
/// <para>
/// A factory member must not return <see langword="null"/>. The legacy <c>Create</c> either yields an
/// object or the PowerBuilder runtime fails outright, and the chain reproduces that fail-fast posture
/// rather than degrading: AAP 0.1.4 records that a structural fault terminates rather than continues,
/// and a chain holding a null service would fail later, at a dispatch, with nothing to point at.
/// </para>
/// </remarks>
public interface IDataWindowAttachedServiceFactory
{
    /// <summary>Creates the context-menu service. Called FIRST [<c>se_cst_dw.sru:L570</c>].</summary>
    /// <returns>The service. Never <see langword="null"/>.</returns>
    IDataWindowContextMenuService CreateContextMenu();

    /// <summary>Creates the row-selection service. Called second [<c>:L571</c>].</summary>
    /// <returns>The service. Never <see langword="null"/>.</returns>
    IDataWindowRowSelectService CreateRowSelect();

    /// <summary>
    /// Creates the column-sort service. Called THIRD [<c>:L572</c>] - and initialised FOURTH.
    /// </summary>
    /// <returns>The service. Never <see langword="null"/>.</returns>
    IDataWindowColumnSortService CreateColumnSort();

    /// <summary>
    /// Creates the drop-down search service. Called FOURTH [<c>:L573</c>] - and initialised THIRD.
    /// </summary>
    /// <returns>The service. Never <see langword="null"/>.</returns>
    IDataWindowDropDownSearchService CreateDropDownSearch();

    /// <summary>Creates the column-expression service. Called last [<c>:L574</c>].</summary>
    /// <returns>The service. Never <see langword="null"/>.</returns>
    IDataWindowColumnExpressionService CreateColumnExp();
}


/// <summary>
/// Which steps of a conditionally ordered dispatch actually ran - the domain half of contract C-03's
/// <c>OrderedDispatchReport</c> message.
/// </summary>
/// <remarks>
/// <para>
/// WITHOUT THIS A CONSUMER CANNOT DISTINGUISH "the step was conditionally skipped" FROM "the step
/// failed silently", and those are different facts about the same absence. The oracle has four
/// distinct reasons a step can be absent, and all four are separately representable here: the event
/// gate short-circuited [<c>se_cst_dw.sru:L124</c>, <c>:L130</c>, <c>:L176</c>, <c>:L187</c>], the
/// liveness guard found the control destroyed [<c>:L138</c>, <c>:L147</c>], the topic had no
/// subscriber [<c>:L166</c>], or a service was disabled [<c>:L169</c>, <c>:L313</c>, <c>:L408</c>].
/// </para>
/// <para>
/// Named <c>DataWindowDispatchReport</c> rather than <c>OrderedDispatchReport</c> deliberately: the
/// generated contract type of that name lives in <c>PowerFramework.Contracts.DataServices.V1</c>, and
/// a domain type sharing its spelling would become ambiguous the moment a file imported that
/// namespace wholesale. The field correspondence is one-for-one and is stated on each member.
/// </para>
/// </remarks>
internal sealed record DataWindowDispatchReport
{
    /// <summary>
    /// The neutral report: nothing ran and nothing was skipped for a reason worth recording. Used for
    /// the events that have no conditional step at all.
    /// </summary>
    internal static DataWindowDispatchReport None { get; } = new();

    /// <summary>
    /// Step one of <c>ondoitemchanged</c> [<c>se_cst_dw.sru:L313-L315</c>]: the column-expression
    /// service's changed handler. <see langword="false"/> when that service is disabled. Contract
    /// field <c>column_expression_handler_ran</c>.
    /// </summary>
    internal bool ColumnExpressionHandlerRan { get; init; }

    /// <summary>
    /// The broker edge ran. <see langword="false"/> when the topic had no subscriber
    /// [<c>:L166</c>, <c>:L316</c>] or when the liveness guard stopped it [<c>:L138</c>,
    /// <c>:L147</c>]. Contract field <c>broker_trigger_ran</c>.
    /// </summary>
    /// <remarks>
    /// <b>Reported <see langword="true"/> whenever the trigger was actually issued, including when a
    /// subscriber vetoed.</b> Pairing it with <see cref="DataWindowEventOutcome.BrokerVeto"/> is what
    /// distinguishes "ran and allowed" from "did not run" - the contract makes the same pairing for
    /// the same reason.
    /// </remarks>
    internal bool BrokerTriggerRan { get; init; }

    /// <summary>
    /// The semantic handler ran. <see langword="false"/> for the two raw events that have no semantic
    /// counterpart at all - <c>ondwnrbuttonup</c> [<c>:L120</c>] and <c>ondwnlbuttonup</c>
    /// [<c>:L395</c>] - and for any event the gate short-circuited. Contract field
    /// <c>semantic_handler_ran</c>.
    /// </summary>
    internal bool SemanticHandlerRan { get; init; }

    /// <summary>
    /// The handler short-circuited on the event gate before any step ran - the early-out at
    /// <c>:L124</c>, <c>:L130</c>, <c>:L176</c> or <c>:L187</c>. Contract field <c>gated_out</c>.
    /// </summary>
    /// <remarks>
    /// A GATED EVENT REPORTS CONTINUE, NOT PREVENTION, because all four early-outs
    /// <c>return 0</c>. This flag is the only thing that keeps the two distinguishable, since the
    /// numeric they yield is identical to an allowed dispatch's.
    /// </remarks>
    internal bool GatedOut { get; init; }

    /// <summary>
    /// The legacy's <c>IsValid(this)</c> guard found the host destroyed by the preceding handler, so
    /// the broker edge was skipped - <c>:L138</c> and <c>:L147</c>. Contract field
    /// <c>target_became_invalid</c>.
    /// </summary>
    /// <remarks>
    /// NOT DEFENSIVE NOISE. A click or double-click handler is permitted to destroy the control it was
    /// raised on, and the oracle re-tests liveness before touching it again. There is a THIRD guard at
    /// <c>:L152</c> covering the focus-free row switch; it sets
    /// <see cref="DataWindowEventOutcome.RowSwitchAttempted"/> to <see langword="false"/> rather than
    /// this flag, because that guard also carries a <c>row &gt; 0</c> condition and conflating the two
    /// would make an ordinary header click look like a destroyed control.
    /// </remarks>
    internal bool TargetBecameInvalid { get; init; }
}

/// <summary>
/// The complete observable outcome of one event dispatch - the domain half of contract C-03's
/// <c>EventResult</c> message.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 2 in the file header. The public event members return the legacy numeric verbatim, which
/// is all the oracle's own call sites need; this record is what the boundary needs, and it is pushed
/// to an <see cref="IDataWindowEventObserver"/> rather than stored on the chain.
/// </para>
/// <para>
/// THE TWO VETO EDGES ARE SEPARATE FIELDS AND NOT ONE COMBINED VERDICT. A raw event consults its
/// semantic handler and then the broker, and either edge can stop the dispatch
/// [<c>se_cst_dw.sru:L115-L117</c> for both, <c>:L120-L121</c> for the broker alone]. One verdict
/// would say that the dispatch stopped without saying WHICH edge stopped it, and since the two edges
/// have different subscribers they have different remedies.
/// </para>
/// </remarks>
internal sealed record DataWindowEventOutcome
{
    /// <summary>The event this outcome belongs to, numbered in source declaration order.</summary>
    internal required EventId EventId { get; init; }

    /// <summary>
    /// The monotonic sequencing token issued for this dispatch, starting at <c>1</c>.
    /// </summary>
    /// <remarks>
    /// FOR DETECTION, AND - ONLY UNDER <see cref="OrderingDiscipline.Sequenced"/> - FOR REORDERING.
    /// Under <see cref="OrderingDiscipline.Synchronous"/> an out-of-order arrival is a HARD ERROR and
    /// never a reorder opportunity; see <see cref="DataWindowEventSequencer"/>.
    /// </remarks>
    internal required long Sequence { get; init; }

    /// <summary>
    /// The ordering discipline governing this occurrence, assigned per capability area by
    /// AAP 0.6.1.4 and resolved by <see cref="DataWindowEventOrdering"/>.
    /// </summary>
    /// <remarks>
    /// Carried ON the outcome rather than looked up from <see cref="EventId"/>, because
    /// <c>ondwnkillfocus</c> genuinely belongs to BOTH groups and only the emitter knows which role a
    /// given occurrence is playing.
    /// </remarks>
    internal required OrderingDiscipline Discipline { get; init; }

    /// <summary>
    /// The semantic handler's edge, tri-valued. <see cref="VetoResult.Continue"/> when the handler
    /// allowed the dispatch, when the gate short-circuited, or when the event HAS no semantic handler.
    /// </summary>
    internal VetoResult SemanticVeto { get; init; }

    /// <summary>
    /// The broker's edge, tri-valued. <see cref="VetoResult.Continue"/> when no subscriber vetoed OR
    /// when the edge did not run; pair it with
    /// <see cref="DataWindowDispatchReport.BrokerTriggerRan"/> to tell those apart.
    /// </summary>
    internal VetoResult BrokerVeto { get; init; }

    /// <summary>
    /// The topic the broker edge used, DECOMPOSED into sequence, logical name and lifetime.
    /// <see langword="null"/> when the event has no broker edge.
    /// </summary>
    /// <remarks>
    /// Always one of <see cref="DataWindowEventChain.Topics"/>, so
    /// <see cref="SubscriptionTopic.ToLegacyString"/> re-emits the legacy spelling byte for byte -
    /// <c>"0-itemchanged"</c> and <c>"1-editchanged"</c> included.
    /// </remarks>
    internal SubscriptionTopic? Topic { get; init; }

    /// <summary>Which conditional steps ran. Never <see langword="null"/>.</summary>
    internal required DataWindowDispatchReport Dispatch { get; init; }

    /// <summary>
    /// THE RAW NUMERIC THE LEGACY EVENT RETURNED, VERBATIM. <c>0</c> for a void event.
    /// </summary>
    /// <remarks>
    /// Carried IN ADDITION TO the structured veto fields rather than instead of them, and for a
    /// different consumer: the structured fields are how code should REASON, this field is what a
    /// characterization recording COMPARES.
    /// </remarks>
    internal long ReturnValue { get; init; }

    /// <summary>
    /// The four-value item-change alphabet, for the three events that speak it -
    /// <c>ondwnitemchange</c> [<c>:L182-L253</c>], <c>ondoitemchange</c> [<c>:L256-L293</c>] and
    /// <c>ondwnitemvalidationerror</c> [<c>:L322-L385</c>]. <see langword="null"/> for every other
    /// event, because there the numeral means something else entirely.
    /// </summary>
    /// <remarks>
    /// Reports the value the event YIELDS, after the oracle's own rewrites - <c>3</c> becomes
    /// <c>1</c> at <c>:L225</c> and the default arm becomes <c>2</c> at <c>:L250</c>. NEVER mapped
    /// onto <see cref="RetCode"/> or <see cref="VetoResult"/>; see DECISION 3.
    /// </remarks>
    internal ItemChangeResult? ItemChangeResult { get; init; }

    /// <summary>
    /// The <c>any</c> return of <c>oncolumnexpinvokemethod</c> [<c>:L14</c>], for that event only.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is a real value here and is NOT a "no result" marker: a macro may
    /// legitimately return null, and AAP 0.4.5.4 forbids collapsing null to zero. Read
    /// <see cref="EventId"/> to tell whether this field applies at all.
    /// </remarks>
    internal object? AnyResult { get; init; }

    /// <summary>
    /// The <c>ref string filter</c> out-parameter of <c>onddsgetfilter</c> [<c>:L13</c>], for that
    /// event only.
    /// </summary>
    /// <remarks>
    /// EXPLICIT PRESENCE IS LOAD-BEARING. <see langword="null"/> means the handler did not touch the
    /// parameter, which is a refusal to filter; the empty string means the handler assigned an empty
    /// filter, which means match nothing. A <c>ref</c> parameter makes that distinction naturally and
    /// a plain string field would silently merge two different instructions.
    /// </remarks>
    internal string? ProducedFilter { get; init; }

    /// <summary>
    /// Whether the focus-free row switch at <c>:L152-L159</c> moved the cursor.
    /// <see langword="null"/> when it was not attempted at all - which is every event other than
    /// <c>ondwnlbuttonclk</c>, and for that event whenever the third liveness-and-row guard declined
    /// [<c>:L152</c>], the DataWindow was not processing [<c>:L153</c>], or the cursor was already on
    /// the requested row [<c>:L154</c>].
    /// </summary>
    /// <remarks>
    /// <c>SetRow</c> IS FALLIBLE AND ITS RETURN CODE IS NOT TRUSTED: the oracle calls it and then
    /// RE-READS <c>GetRow()</c>, returning <c>1</c> when the row still differs [<c>:L155-L156</c>].
    /// <see langword="true"/> here means the re-read agreed; <see langword="false"/> means it did not
    /// and the handler returned <c>1</c>.
    /// </remarks>
    internal bool? RowSwitchAttempted { get; init; }

    /// <summary>
    /// The structured error a dispatch produced where the legacy would have shown a dialog.
    /// <see langword="null"/> when none was produced.
    /// </summary>
    /// <remarks>
    /// <c>se_cst_dw.sru</c> carries EXACTLY ONE live dialog, the validation-error handler's
    /// <c>MessageBox(I18N(...),sErrMsg,StopSign!)</c> at <c>:L357</c>; the byte-length dialog at
    /// <c>:L286</c> is commented out and stays that way (constraint C-B). ONLY THE DELIVERY CHANNEL
    /// CHANGES: Domain/ValidationSession.cs preserves the text, the localization category, the
    /// substitution arguments and the severity, and this field carries its result outward.
    /// </remarks>
    internal ValidationStructuredError? Error { get; init; }

    /// <summary>
    /// The session state AFTER this dispatch, for the events in the synchronous group.
    /// <see langword="null"/> for the sequenced events, which mutate none of it.
    /// </summary>
    /// <remarks>
    /// Carried ON the outcome rather than left to a separate read, because the stash and the two
    /// re-entrancy guards are mutated BY these events: a consumer that re-read them afterwards could
    /// not observe the INTERMEDIATE value the next event in the chain is about to consume
    /// [<c>se_cst_dw.sru:L195</c> writes it, <c>:L331-L332</c> reads and clears it].
    /// </remarks>
    internal ValidationSessionSnapshot? State { get; init; }

    /// <summary>
    /// Whether the deferred accept-text continuation was queued by <c>ondwnkillfocus</c>
    /// [<c>:L388-L390</c>]. <see langword="null"/> for every other event.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> MEANS THE ITEM-CHANGE FLAG WAS SET, which is a meaningful observation
    /// rather than a missing one - the continuation is deliberately not queued while an item change
    /// is in flight.
    /// </remarks>
    internal bool? DeferredAcceptQueued { get; init; }
}

/// <summary>
/// Receives the outcome of every dispatch the chain performs, in the order the dispatches happened.
/// </summary>
/// <remarks>
/// <para>
/// DECISION 2 in the file header. This is how contract C-03's <c>EventResult</c> gets everything the
/// legacy numeric cannot carry, without the chain holding mutable per-dispatch state that a nested
/// event would overwrite [<c>se_cst_dw.sru:L207</c>].
/// </para>
/// <para>
/// NESTING IS VISIBLE AND THAT IS THE POINT. When <c>ondwnitemchange</c> raises
/// <c>ondwnchanging</c> from inside itself, an observer sees the inner outcome first and the outer
/// one second, which is the true sequence. A single stored slot would have shown only one of them.
/// </para>
/// <para>
/// An implementation must not throw. The chain calls it from inside a ported handler, so an exception
/// escaping here would abort a dispatch the oracle completes - a behaviour change introduced by
/// observation, which is the one thing an observer must never do.
/// </para>
/// </remarks>
internal interface IDataWindowEventObserver
{
    /// <summary>Called once per dispatch, immediately before the handler returns its numeric.</summary>
    /// <param name="outcome">The outcome. Never <see langword="null"/>.</param>
    void OnEventDispatched(DataWindowEventOutcome outcome);
}



/// <summary>
/// The per-capability-area ordering assignment of AAP 0.6.1.4, expressed as a lookup so that a
/// consumer's ordering check is driven by data rather than by a table it must keep in step by hand.
/// </summary>
/// <remarks>
/// <para>
/// AAP 0.6.1.4 requires choosing, PER CAPABILITY AREA, between pattern (a) - a monotonic sequencing
/// token a consumer may use to detect AND REORDER out-of-order delivery - and pattern (b) - a strictly
/// synchronous chain in which NO reordering is permitted and an out-of-order arrival fails the
/// session. The assignment is fixed, and it is reproduced here verbatim with the evidence for each
/// area:
/// </para>
/// <list type="table">
///   <listheader><term>Area</term><description>Pattern, and why</description></listheader>
///   <item>
///     <term>Item-change and validation</term>
///     <description>
///     <b>(b) SYNCHRONOUS.</b> Reordering is not undesirable, it is SEMANTICALLY IMPOSSIBLE. The
///     validation-error handler reads AND CLEARS the code the preceding item-change event stashed
///     [<c>se_cst_dw.sru:L331-L332</c>] and PRE-SETS its own result from it [<c>:L338-L340</c>], so
///     its behaviour is a function of its predecessor's return value. Item-change also fires a NESTED
///     event from inside itself [<c>:L207</c>], and kill-focus queues its deferred accept only while
///     the item-change flag is clear [<c>:L388-L390</c>].
///     </description>
///   </item>
///   <item>
///     <term>Focus, mouse and row-focus notification</term>
///     <description>
///     <b>(a) SEQUENCED.</b> Notifications whose only return contract is the prevent convention. They
///     carry no cross-event state, so a monotonic token is sufficient for a consumer to detect and
///     reorder.
///     </description>
///   </item>
///   <item>
///     <term>Context menu</term>
///     <description>
///     <b>(b) SYNCHRONOUS.</b> Initialisation must COMPLETE before the menu identifier passed to the
///     second event can be meaningful [<c>n_cst_dwsvc_contextmenu.sru:L147</c> then <c>:L194</c>].
///     </description>
///   </item>
///   <item>
///     <term>Drop-down search</term>
///     <description>
///     <b>(b) SYNCHRONOUS.</b> The first event produces its result through a <c>ref string</c>
///     OUT-PARAMETER [<c>se_cst_dw.sru:L13</c>], which has no asynchronous representation at all - the
///     caller blocks on the produced filter because the filter is the only thing the call exists to
///     obtain.
///     </description>
///   </item>
///   <item>
///     <term>Macro invocation</term>
///     <description>
///     <b>(b) SYNCHRONOUS.</b> The calculation cannot proceed without the returned value
///     [<c>se_cst_dw.sru:L14</c>].
///     </description>
///   </item>
///   <item>
///     <term>Expression trace</term>
///     <description><b>(a) SEQUENCED.</b> Pure diagnostics, fire-and-forget [<c>:L32</c>].</description>
///   </item>
/// </list>
/// <para>
/// <b><c>ondwnkillfocus</c> BELONGS TO BOTH GROUPS AND THAT IS NOT A CONTRADICTION.</b> As the tail of
/// the item-change chain it is synchronous, because the deferred accept must not be queued while an
/// item change is in flight; as pure focus notification it is sequenced. Only the emitter knows which
/// role a given occurrence is playing, which is why
/// <see cref="DisciplineOf(EventId, bool)"/> takes a flag for it and why the resolved discipline
/// travels ON the outcome rather than being looked up from the event identity downstream. The chain
/// itself always supplies the tail role, for the reason recorded on
/// <see cref="DataWindowEventChain.OnDwnKillFocus"/>: its body touches the cross-event state on every
/// occurrence, and of the two possible misreports only the sequenced one can authorise a harmful
/// reorder. The flagless answer is for a caller classifying a bare event identifier with no session in
/// hand.
/// </para>
/// <para>
/// <b>TWO OF THE TWENTY-TWO EVENTS ARE NOT NAMED IN THE TABLE ABOVE AND ARE STILL CLASSIFIED.</b>
/// The area list enumerates nine events under focus, mouse and row-focus notification and omits
/// <c>ondwnitemchangefocus</c> [<c>se_cst_dw.sru:L22</c>] and <c>ondwnchanging</c> [<c>:L21</c>].
/// Both are assigned pattern (a) by the group's own stated test rather than left unclassified, since
/// the only return contract either has is the prevent convention and neither touches the cross-event
/// state at <c>:L89-L96</c>. The reasoning, and why <c>ondwnchanging</c>'s nested role at <c>:L207</c>
/// needs no dual-role flag of its own, is recorded at the switch arm that assigns it.
/// </para>
/// </remarks>
internal static class DataWindowEventOrdering
{
    /// <summary>
    /// The discipline governing <paramref name="eventId"/>.
    /// </summary>
    /// <param name="eventId">The event, numbered in source declaration order.</param>
    /// <param name="withinItemChangeChain">
    /// <see langword="true"/> when the occurrence is the tail of the item-change chain rather than a
    /// standalone focus notification. Meaningful for <see cref="EventId.Ondwnkillfocus"/> ONLY, and
    /// ignored for every other event so that a caller cannot reclassify an area by accident.
    /// </param>
    /// <returns>
    /// <see cref="OrderingDiscipline.Synchronous"/> or <see cref="OrderingDiscipline.Sequenced"/>.
    /// <see cref="OrderingDiscipline.Unspecified"/> is returned for
    /// <see cref="EventId.Unspecified"/> alone, because an unidentified event has no capability area
    /// and guessing one would assign it an ordering rule it was never measured against.
    /// </returns>
    internal static OrderingDiscipline DisciplineOf(EventId eventId, bool withinItemChangeChain = false)
    {
        return eventId switch
        {
            // ------------------------------------------------------------------------------------
            // PATTERN (b) - the item-change and validation chain. :L182-L253, :L256-L293, :L295-L320,
            // :L322-L385. `onitemchanged` [:L25] is in the group because it is step three of
            // `ondoitemchanged`'s strict order [:L319] and therefore cannot float away from it.
            // ------------------------------------------------------------------------------------
            EventId.Ondwnitemchange => OrderingDiscipline.Synchronous,
            EventId.Ondoitemchange => OrderingDiscipline.Synchronous,
            EventId.Onitemchanged => OrderingDiscipline.Synchronous,
            EventId.Ondoitemchanged => OrderingDiscipline.Synchronous,
            EventId.Ondwnitemvalidationerror => OrderingDiscipline.Synchronous,

            // PATTERN (b) - the context-menu pair. :L11 then :L12.
            EventId.Oninitcontextmenu => OrderingDiscipline.Synchronous,
            EventId.Oncontextmenu => OrderingDiscipline.Synchronous,

            // PATTERN (b) - the drop-down search pair. The `ref string` at :L13 is the reason.
            EventId.Onddsgetfilter => OrderingDiscipline.Synchronous,
            EventId.Onddsfiltered => OrderingDiscipline.Synchronous,

            // PATTERN (b) - macro invocation. :L14.
            EventId.Oncolumnexpinvokemethod => OrderingDiscipline.Synchronous,

            // ------------------------------------------------------------------------------------
            // THE DUAL-MEMBERSHIP EVENT. :L387-L393. Synchronous as the tail of the item-change
            // chain, sequenced as pure focus notification.
            // ------------------------------------------------------------------------------------
            EventId.Ondwnkillfocus => withinItemChangeChain
                ? OrderingDiscipline.Synchronous
                : OrderingDiscipline.Sequenced,

            // ------------------------------------------------------------------------------------
            // PATTERN (a) - focus, mouse and row-focus notification, plus the diagnostic trace.
            // :L15-L20, :L17, :L18, :L30, :L31, :L32.
            // ------------------------------------------------------------------------------------
            EventId.Ondwnsetfocus => OrderingDiscipline.Sequenced,
            EventId.Ondwnrowchange => OrderingDiscipline.Sequenced,
            EventId.Ondwnrowchanging => OrderingDiscipline.Sequenced,
            EventId.Ondwnlbuttonclk => OrderingDiscipline.Sequenced,
            EventId.Ondwnlbuttondblclk => OrderingDiscipline.Sequenced,
            EventId.Ondwnlbuttonup => OrderingDiscipline.Sequenced,
            EventId.Ondwnrbuttondown => OrderingDiscipline.Sequenced,
            EventId.Ondwnrbuttonup => OrderingDiscipline.Sequenced,
            EventId.Ondwnitemchangefocus => OrderingDiscipline.Sequenced,
            EventId.Oncolumnexptrace => OrderingDiscipline.Sequenced,

            // TWO EVENTS THE AAP'S TABLE DOES NOT NAME, ASSIGNED BY ITS OWN CRITERIA RATHER THAN LEFT
            // UNCLASSIFIED. AAP 0.6.1.4 enumerates nine events under "focus, mouse and row-focus
            // notification" and lists neither `ondwnitemchangefocus` [se_cst_dw.sru:L22] - handled
            // above - nor `ondwnchanging` [:L21], yet both satisfy that group's stated test exactly:
            // the only return contract either has is the prevent convention [:L166-L172, :L177-L179],
            // and neither reads nor writes any of the four cross-event fields at :L89-L96. Leaving
            // either at Unspecified would be strictly worse than assigning it, because Unspecified is
            // what an UNIDENTIFIED event answers - an unclassified real event would then be
            // indistinguishable on the wire from a corrupt one.
            //
            // `ondwnchanging` HAS A SECOND, NESTED ROLE, AND IT NEEDS NO FLAG FOR IT. The item-change
            // protocol fires it from inside itself [:L207], which is the nested emission AAP 0.6.1.4
            // cites as one reason the item-change area is synchronous. That occurrence needs no
            // reclassification: it is dispatched from WITHIN an already-synchronous item-change
            // dispatch, so the guarantee it runs under is the enclosing one, and its token advances the
            // same monotonic counter either way. This is deliberately NOT modelled with the dual-role
            // flag `ondwnkillfocus` needs - kill-focus is reached from the runtime in BOTH roles and
            // only the emitter can tell them apart, whereas edit-changed's nested role is always
            // enclosed by the role that already governs it.
            EventId.Ondwnchanging => OrderingDiscipline.Sequenced,

            // EventId.Unspecified alone. NOT defaulted to a real discipline: an event with no measured
            // capability area must not inherit one silently.
            _ => OrderingDiscipline.Unspecified
        };
    }
}

/// <summary>
/// Raised when a dispatch arrives out of order inside a group governed by
/// <see cref="OrderingDiscipline.Synchronous"/>, or when a dispatch carries no token at all.
/// </summary>
/// <remarks>
/// <para>
/// A HARD ERROR, NEVER A REORDER OPPORTUNITY, and that is mechanical rather than stylistic. Inside the
/// item-change group one event's behaviour is a FUNCTION OF THE PREVIOUS EVENT'S RETURN VALUE: the
/// validation-error handler reads and clears the code stashed by the preceding item-change event
/// [<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L331-L332</c>] and pre-sets its own
/// result from it [<c>:L338-L340</c>]. A consumer that buffered and re-sorted that group would read a
/// stash written by the wrong predecessor, or by none, and would do so SILENTLY because every
/// individual message would still be well formed.
/// </para>
/// <para>
/// Under <see cref="OrderingDiscipline.Sequenced"/> this is NOT raised for an arrival that runs AHEAD of
/// the expected token: a gap is legitimate there, because both directions draw from one counter and the
/// numbers the server consumed are numbers the client never sends. It IS raised there for an arrival AT OR
/// BEHIND the ordering mark, which is a reversal or a duplicate - the events after that position have
/// already been dispatched, so there is nowhere left to put it. And it is raised on every discipline for a
/// missing token.
/// </para>
/// <para>
/// Derives from <see cref="InvalidOperationException"/> because the fault is a protocol-state
/// violation by the caller rather than a bad argument value - the same choice
/// Expressions/MacroInvoker.cs makes for the same class of fault on contract C-04's inverted stream.
/// </para>
/// </remarks>
public sealed class DataWindowEventSequenceException : InvalidOperationException
{
    /// <summary>Creates the exception with the default message.</summary>
    public DataWindowEventSequenceException()
        : base("A DataWindow event arrived out of order inside a synchronous ordering group.")
    {
    }

    /// <summary>Creates the exception with a caller-supplied message.</summary>
    /// <param name="message">The message.</param>
    public DataWindowEventSequenceException(string? message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a caller-supplied message and inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public DataWindowEventSequenceException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception carrying the full ordering detail.</summary>
    /// <param name="message">The message.</param>
    /// <param name="eventId">The event that arrived out of order.</param>
    /// <param name="discipline">The discipline the group is governed by.</param>
    /// <param name="expectedSequence">The token the group was expecting next.</param>
    /// <param name="actualSequence">The token that actually arrived.</param>
    public DataWindowEventSequenceException(
        string? message,
        EventId eventId,
        OrderingDiscipline discipline,
        long expectedSequence,
        long actualSequence)
        : base(message)
    {
        EventId = eventId;
        Discipline = discipline;
        ExpectedSequence = expectedSequence;
        ActualSequence = actualSequence;
    }

    /// <summary>The event that arrived out of order.</summary>
    public EventId EventId { get; }

    /// <summary>The ordering discipline the group is governed by.</summary>
    public OrderingDiscipline Discipline { get; }

    /// <summary>The token the group was expecting next. <c>0</c> when no token was supplied.</summary>
    public long ExpectedSequence { get; }

    /// <summary>The token that actually arrived. <c>0</c> means none was supplied.</summary>
    public long ActualSequence { get; }
}

/// <summary>
/// Issues the monotonic sequencing token every dispatch carries, and enforces the ordering rule its
/// discipline imposes.
/// </summary>
/// <remarks>
/// <para>
/// Tokens start at <c>1</c> and increase by one, so <c>0</c> is available as "not supplied" and is
/// treated as a fault on every discipline - which is what contract C-03's <c>SequencingToken</c>
/// documents.
/// </para>
/// <para>
/// THE TWO DISCIPLINES HAVE OPPOSITE RULES FOR THE SAME EVIDENCE.
/// <see cref="OrderingDiscipline.Synchronous"/> demands the exact successor and raises
/// <see cref="DataWindowEventSequenceException"/> otherwise; the group runs inside ONE validation
/// session on ONE stream and reordering it is semantically impossible.
/// <see cref="OrderingDiscipline.Sequenced"/> requires only that the token be STRICTLY ABOVE the mark: a
/// gap is legitimate there because the counter is shared with the outbound direction, but a reversal or a
/// duplicate is refused.
/// </para>
/// <para>
/// WHICH MEANS THE TOKEN IS ACTED ON RATHER THAN MERELY RECORDED, and that is the correction of a real
/// defect. The sequenced arm used to accept EVERY positive token - a reversal and a duplicate included -
/// so the ordering information was measurable and unused: production dispatched in arrival order and the
/// only reordering anywhere in the estate was a sort inside a test. <see cref="Accept"/>'s remarks record
/// why the answer is a defined error rather than a reorder buffer, and the measurement behind it.
/// </para>
/// <para>
/// Guarded by a lock, and the reason is CORRECTNESS OF THE TOKEN rather than any claim about
/// concurrency behaviour: the broker holds unsynchronised per-instance dispatch state, so a chain is
/// single-threaded per session by contract, and the lock is what makes a violation of that contract
/// surface as a wrong token instead of a torn counter. No performance property is asserted (AAP 0.8.5).
/// </para>
/// </remarks>
internal sealed class DataWindowEventSequencer
{
    /// <summary>
    /// The value a caller passes when it has no token. Treated as a fault on every discipline, which
    /// is why tokens start at one rather than zero.
    /// </summary>
    internal const long NoToken = 0L;

    private readonly Lock _gate = new();

    private long _issued;
    private long _accepted;

    /// <summary>The most recently issued token, or <see cref="NoToken"/> before the first.</summary>
    internal long LastIssued
    {
        get
        {
            lock (_gate)
            {
                return _issued;
            }
        }
    }

    /// <summary>The highest token accepted so far, or <see cref="NoToken"/> before the first.</summary>
    internal long LastAccepted
    {
        get
        {
            lock (_gate)
            {
                return _accepted;
            }
        }
    }

    /// <summary>
    /// The lowest token this conversation will still admit: the immediate successor of the last accepted
    /// one, in either direction.
    /// </summary>
    /// <remarks>
    /// ONE MEMBER FOR BOTH DISCIPLINES, BECAUSE THE TWO RULES DIFFER IN STRICTNESS RATHER THAN IN WHERE
    /// THEY MEASURE FROM. The synchronous discipline requires exactly this token; the sequenced discipline
    /// requires this token OR HIGHER. It is also the value the ordering diagnostic reports as expected, so
    /// a client is told the floor rather than having to derive it.
    /// </remarks>
    internal long NextExpected
    {
        get
        {
            lock (_gate)
            {
                return _accepted + 1L;
            }
        }
    }

    /// <summary>
    /// Issues the next token and records it as accepted, which is the emitter's path: a token the
    /// chain issues itself is by construction in order.
    /// </summary>
    /// <returns>The token, starting at <c>1</c> and strictly increasing.</returns>
    internal long Issue()
    {
        lock (_gate)
        {
            _issued++;
            _accepted = _issued;

            return _issued;
        }
    }

    /// <summary>
    /// Validates a token that arrived from a peer, applying the rule its discipline imposes, and
    /// advances the ordering mark past it.
    /// </summary>
    /// <param name="sequence">The arriving token. <see cref="NoToken"/> is always a fault.</param>
    /// <param name="discipline">The discipline governing the arriving event's capability area.</param>
    /// <param name="eventId">The arriving event, for the error detail.</param>
    /// <exception cref="DataWindowEventSequenceException">
    /// <paramref name="sequence"/> is not positive; or the discipline is
    /// <see cref="OrderingDiscipline.Synchronous"/> and the token is not the exact successor of the last
    /// accepted one; or the discipline is <see cref="OrderingDiscipline.Sequenced"/> and the token is at
    /// or behind the mark, which is a reversal or a duplicate. NEVER buffered and re-sorted; the type's
    /// remarks record the measurement that rules reordering out on this contract.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE SEQUENCED ARM USED TO ACCEPT ANY POSITIVE TOKEN, AND THAT WAS THE DEFECT. It advanced the mark
    /// when the token was above it and accepted the message anyway when it was below, on the reasoning
    /// that reorder authority belongs to the consumer. On this boundary the server IS the consumer, so
    /// "the consumer may reorder" resolved to nobody acting on the token at all: a reversal and a
    /// duplicate were both dispatched silently, in arrival order, and the only reordering anywhere in the
    /// estate was a sort inside a test.
    /// </para>
    /// <para>
    /// THE RULE IS NOW STRICTLY INCREASING PAST THE MARK, AND THE TWO HALVES OF THAT ARE BOTH DELIBERATE.
    /// A token ABOVE the mark is accepted even when it is not the immediate successor - a GAP IS
    /// LEGITIMATE here, because both directions draw from one counter and a client's token is one past
    /// the highest it has SEEN, so the numbers the server consumed are numbers the client never uses. A
    /// token AT OR BELOW the mark is refused: it is a reversal or a duplicate, and dispatching it would
    /// deliver a notification the ordering says came earlier, or run one event twice.
    /// </para>
    /// <para>
    /// WHICH IS A NARROWER CONTRACT RATHER THAN A REORDERING ONE, AND THE REASON IS MEASURED. AAP 0.6.1.4
    /// says a pattern-(a) token is sufficient for detection AND reordering, so a hold-and-release buffer
    /// was built here first and then removed, because reordering CANNOT BE MADE SOUND on this contract:
    /// one dispatch of token 1 leaves the next expected token at 4 - the chain's outcome report takes 2
    /// and the result write takes 3 - so a message held awaiting token 2 waits for a number no client will
    /// ever send. Every hold would strand. Worse, since a client must read a response to learn its next
    /// token, it cannot pipeline, and a gRPC stream delivers one sender's messages in order: an
    /// out-of-order pattern-(a) arrival is therefore not a transport artifact at all, it is a client
    /// defect, and the right answer to a client defect is a defined error rather than a buffer. That is
    /// AAP 0.1.5's rule - narrow with a defined error, never widen with a guess.
    /// </para>
    /// <para>
    /// SOUND REORDERING WOULD REQUIRE CONTIGUOUS INBOUND TOKENS, which means giving each direction its own
    /// sequence space. That is a change to the published meaning of the token ("strictly increasing within
    /// one stream") and is deliberately not made here.
    /// </para>
    /// </remarks>
    internal void Accept(long sequence, OrderingDiscipline discipline, EventId eventId)
    {
        lock (_gate)
        {
            if (sequence <= NoToken)
            {
                throw new DataWindowEventSequenceException(
                    "A DataWindow event carried no sequencing token; every message on the event chain "
                        + "must carry one.",
                    eventId,
                    discipline,
                    _accepted + 1L,
                    sequence);
            }

            long expected = _accepted + 1L;

            if (discipline == OrderingDiscipline.Synchronous)
            {
                if (sequence != expected)
                {
                    throw new DataWindowEventSequenceException(
                        "A DataWindow event arrived out of order inside a synchronous ordering group, "
                            + "where reordering is semantically impossible because one event's result is "
                            + "a function of its predecessor's.",
                        eventId,
                        discipline,
                        expected,
                        sequence);
                }

                Advance(sequence);

                return;
            }

            // OrderingDiscipline.Sequenced, and OrderingDiscipline.Unspecified with it. A gap is allowed
            // because the counter is shared with the outbound direction; a reversal is not.
            if (sequence < expected)
            {
                throw new DataWindowEventSequenceException(
                    "A DataWindow event arrived at or behind the ordering mark inside a sequenced "
                        + "ordering group, so it is a reversal or a duplicate rather than a late "
                        + "arrival. The mark counts messages already dispatched, and the events after "
                        + "that position have run, so there is nowhere left to place this one.",
                    eventId,
                    discipline,
                    expected,
                    sequence);
            }

            Advance(sequence);
        }
    }

    /// <summary>
    /// Advances the mark past an accepted token, keeping the issue counter from reusing it.
    /// </summary>
    /// <param name="sequence">The accepted token. Always the expected successor when this is reached.</param>
    /// <remarks>
    /// The issue counter is pushed forward too so a token the server issues next cannot collide with one
    /// the client has already used. Both discipline arms need exactly this, which is why it is one
    /// method: two copies would be two chances for the counters to drift apart.
    /// </remarks>
    private void Advance(long sequence)
    {
        _accepted = sequence;

        if (_issued < sequence)
        {
            _issued = sequence;
        }
    }
}



/// <summary>
/// The port of <c>se_cst_dw</c>: the DataWindow service host's 22-event raw and semantic delegation
/// chain, its twelve broker topics, its five attached services, its nested broker subclass, and its
/// <c>filter</c> and <c>deleterow</c> overrides
/// (<c>ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru</c>, 616 lines).
/// </summary>
/// <remarks>
/// <para>
/// ABSTRACT BECAUSE THE ORACLE'S OWN TYPE IS A DATAWINDOW AND THIS ONE IS NOT. <c>se_cst_dw</c>
/// derives from <c>se_cst_datawindow</c> [<c>:L4</c>, <c>:L10</c>], a DEFERRED DesignSystem library,
/// and AAP 0.2.1.3 Correction 3 resolves that structural edge by binding to
/// <see cref="DataWindowServiceHost"/> instead. That contract leaves the DataWindow member surface
/// abstract, so an adapter or a test double supplies it and this type supplies everything
/// <c>se_cst_dw</c> itself adds - which is exactly the split
/// Domain/DataWindowServiceHost.cs's DECISION 5 and DECISION 6 prescribe.
/// </para>
/// <para>
/// INTERNAL BECAUSE ITS COLLABORATORS ARE. The four cross-event state fields live on
/// <see cref="ValidationSession"/> and the item-change alphabet is
/// <see cref="Domain.ItemChangeResult"/>, both <see langword="internal"/>; a public type could not
/// name either without CS0051. Internal is also correct on its own terms - constraint C-A keeps this
/// chain inside PowerFramework.DataServices, and the csproj's <c>InternalsVisibleTo</c> already gives
/// the sibling test project the access the 80% coverage gate needs (constraint C-H).
/// </para>
/// <para>
/// IMPLEMENTS <see cref="IItemChangeEventSink"/> WITH NO ADDITIONAL MEMBERS. That interface's three
/// members are <see cref="OnDoItemChange(long, IDataWindowObject, string)"/>,
/// <see cref="OnDwnChanging(long, IDataWindowObject, string?)"/> and
/// <see cref="OnDoItemChanged(long, IDataWindowObject)"/>, all three of which the chain declares or
/// overrides for its own reasons. The coincidence is not luck: Domain/ItemChangeProtocol.cs measured
/// the sink from the three events <c>ondwnitemchange</c> raises [<c>:L194</c>, <c>:L207</c>,
/// <c>:L247</c>], and those are events of <c>se_cst_dw</c>.
/// </para>
/// <para>
/// SINGLE-THREADED PER SESSION, BY CONTRACT RATHER THAN BY LOCK. The broker holds unsynchronised
/// per-instance dispatch state, and contract C-03 runs the whole synchronous group inside one
/// validation session on one bidirectional stream. Nothing in this type is static and mutable, so two
/// chains never interfere; a single chain driven from two threads violates the contract that
/// <see cref="EventBroker"/> already imposes, and no lock here could rescue it. This is a correctness
/// statement and not a performance one (AAP 0.8.5).
/// </para>
/// </remarks>
internal abstract class DataWindowEventChain : DataWindowServiceHost, IItemChangeEventSink
{
    // ==============================================================================================
    //  THE TWELVE BROKER TOPICS                                            se_cst_dw.sru:L44-L76
    //  --------------------------------------------------------------------------------------------
    //  DECISION 4 in the file header: identifiers AND values are preserved verbatim, and each carries
    //  the legacy's own comment recording the subscriber signature it expects. Those comments are the
    //  only documentation of the subscriber contract that exists anywhere, so they are carried across
    //  rather than paraphrased.
    //
    //  ONE WARNING ABOUT THOSE COMMENTS, BECAUSE TAKING THEM AT FACE VALUE PRODUCES A BUG. Eight of
    //  them read `0:continue,1:prevent` [:L46, :L51, :L56, :L59, :L62, :L65, :L68, :L71] - eight
    //  sites, all agreeing, all suggesting a BOOLEAN convention. They are not wrong about what a
    //  handler on those topics is expected to return, but they are not a statement about what the
    //  BROKER can carry, which is tri-valued: PREVENT_ONCE = 1 and PREVENT_DEEP = 2 are declared at
    //  ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L111-L112. See DECISION 3.
    // ==============================================================================================

    /// <summary>
    /// <c>RowFocusChanging(se_cst_dw source, long currentRow, long newRow)</c> - <c>0:continue,
    /// 1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L45-L47</c>.
    /// </summary>
    /// <remarks>
    /// Triggered by <see cref="OnDwnRowChanging(long, long)"/> [<c>:L132</c>], and its result IS
    /// tested - the deliberate asymmetry against <see cref="EVT_ROWFOCUSCHANGED"/>.
    /// </remarks>
    public const string EVT_ROWFOCUSCHANGING = "rowfocuschanging";

    /// <summary>
    /// <c>RowFocusChanged(se_cst_dw source, long currentRow)</c>. Legacy declaration
    /// <c>se_cst_dw.sru:L48-L49</c>.
    /// </summary>
    /// <remarks>
    /// Triggered by <see cref="OnDwnRowChange(long)"/> [<c>:L126</c>], and its result is NOT tested.
    /// The oracle carries no prevent comment on this one, which is consistent: a change that has
    /// already happened cannot be vetoed.
    /// </remarks>
    public const string EVT_ROWFOCUSCHANGED = "rowfocuschanged";

    /// <summary>
    /// <c>long ItemFocusChanged(se_cst_dw source, long row, dwobject dwo)</c> - <c>0:continue,
    /// 1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L50-L52</c>.
    /// </summary>
    public const string EVT_ITEMFOCUSCHANGED = "itemfocuschanged";

    /// <summary>
    /// <c>ItemChanged(se_cst_dw source, long row, dwobject dwo)</c>. Legacy declaration
    /// <c>se_cst_dw.sru:L53-L54</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE LEADING <c>0-</c> IS PART OF THE VALUE AND IT IS FUNCTIONAL, NOT DECORATIVE.</b> The
    /// broker keeps its subscription registry in ascending ORDINAL order of the subscription name, and
    /// ASCII digits sort before letters, so this topic dispatches ahead of
    /// <see cref="EVT_EDITCHANGED"/> and ahead of every unprefixed topic. The digits are a hand-rolled
    /// sequence number spelled INTO the name.
    /// </para>
    /// <para>
    /// The <c>-</c> is NOT a prepend request: the broker's leading-symbol scan exits at position one
    /// for a digit, so the whole string becomes the subscription name verbatim. Decompose it for the
    /// wire through <see cref="SubscriptionTopic"/> - never by hand - and dispatch and sort by
    /// <see cref="SubscriptionTopic.LegacyName"/>, never by
    /// <see cref="SubscriptionTopic.LogicalName"/>.
    /// </para>
    /// <para>
    /// The one legacy subscription to it raises its priority explicitly:
    /// <c>#DataWindow.of_On("!" + #DataWindow.EVT_ITEMCHANGED, this, "onItemChanged")</c>
    /// [<c>n_cst_dwsvc_columnexp.sru:L2429</c>], so the ordering prefix and the priority symbol are
    /// two independent mechanisms applied to one topic.
    /// </para>
    /// </remarks>
    public const string EVT_ITEMCHANGED = "0-itemchanged";

    /// <summary>
    /// <c>long EditChanged(se_cst_dw source, long row, dwobject dwo, string data)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L55-L57</c>.
    /// </summary>
    /// <remarks>
    /// The second of the two ordering-prefixed topics; see <see cref="EVT_ITEMCHANGED"/> for the
    /// mechanism. <c>"1-editchanged"</c> sorts after <c>"0-itemchanged"</c> and before every
    /// unprefixed topic. It is also the only topic the chain probes with
    /// <see cref="EventBroker.IsSubscribed(string)"/> before triggering [<c>:L166</c>].
    /// </remarks>
    public const string EVT_EDITCHANGED = "1-editchanged";

    /// <summary>
    /// <c>long Clicked(se_cst_dw source, long xpos, long ypos, long row, dwobject dwo)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L58-L60</c>.
    /// </summary>
    public const string EVT_CLICKED = "clicked";

    /// <summary>
    /// <c>long DoubleClicked(se_cst_dw source, long xpos, long ypos, long row, dwobject dwo)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L61-L63</c>.
    /// </summary>
    public const string EVT_DOUBLECLICKED = "doubleclicked";

    /// <summary>
    /// <c>long LButtonUp(se_cst_dw source, long xpos, long ypos, long row, dwobject dwo)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L64-L66</c>.
    /// </summary>
    /// <remarks>
    /// ONE OF THE TWO TOPICS WHOSE RAW EVENT HAS NO SEMANTIC COUNTERPART. The comment names a
    /// <c>LButtonUp</c> signature for SUBSCRIBERS, but <c>ondwnlbuttonup</c> [<c>:L395-L397</c>]
    /// consults only the broker - there is no <c>Event LButtonUp</c> anywhere on the ancestry, and
    /// inventing one would fabricate a hook the oracle does not have (constraint C-B).
    /// </remarks>
    public const string EVT_LBUTTONUP = "lbuttonup";

    /// <summary>
    /// <c>long RButtonDown(se_cst_dw source, long xpos, long ypos, long row, dwobject dwo)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L67-L69</c>.
    /// </summary>
    public const string EVT_RBUTTONDOWN = "rbuttondown";

    /// <summary>
    /// <c>long RButtonUp(se_cst_dw source, long xpos, long ypos, long row, dwobject dwo)</c> -
    /// <c>0:continue,1:prevent</c>. Legacy declaration <c>se_cst_dw.sru:L70-L72</c>.
    /// </summary>
    /// <remarks>
    /// The other broker-only topic; see <see cref="EVT_LBUTTONUP"/>. <c>ondwnrbuttonup</c>
    /// [<c>:L120-L121</c>] consults only the broker.
    /// </remarks>
    public const string EVT_RBUTTONUP = "rbuttonup";

    /// <summary>
    /// <c>GetFocus(se_cst_dw source)</c>. Legacy declaration <c>se_cst_dw.sru:L73-L74</c>.
    /// </summary>
    /// <remarks>
    /// Triggered by <see cref="OnDwnSetFocus"/> BEFORE the semantic event, and its result is NOT
    /// tested [<c>:L399-L400</c>]. Note the crossed naming, which is the oracle's and is preserved:
    /// the <c>setfocus</c> raw event triggers the <c>getfocus</c> topic.
    /// </remarks>
    public const string EVT_GETFOCUS = "getfocus";

    /// <summary>
    /// <c>LoseFocus(se_cst_dw source)</c>. Legacy declaration <c>se_cst_dw.sru:L75-L76</c>.
    /// </summary>
    /// <remarks>
    /// Triggered by <see cref="OnDwnKillFocus"/> after the deferred accept is queued and before the
    /// semantic event, and its result is NOT tested [<c>:L387-L392</c>].
    /// </remarks>
    public const string EVT_LOSEFOCUS = "losefocus";

    /// <summary>
    /// The twelve topics as parsed <see cref="SubscriptionTopic"/> values, in the oracle's declaration
    /// order [<c>se_cst_dw.sru:L47</c> through <c>:L76</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parsed once through <see cref="SubscriptionTopic.ParseSubscription(string?, out SubscriptionTopic?)"/>
    /// rather than constructed field by field, which is what guarantees the byte-exact
    /// <see cref="SubscriptionTopic.ToLegacyString"/> round-trip for the two prefixed topics: a parsed
    /// topic re-emits its own raw bytes.
    /// </para>
    /// <para>
    /// NOT ONE OF THE TWELVE CARRIES A NAMESPACE SUFFIX, and the parity tests assert that over the
    /// whole set. See THE `.^persistent` NON-APPEND in the file header for the six locators that prove
    /// the suffix belongs to Persistence's threading layer and not here.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<SubscriptionTopic> Topics { get; } = BuildTopics();

    /// <summary>
    /// The order the five attached services are CREATED in [<c>se_cst_dw.sru:L570-L574</c>], which is
    /// also the order they are torn down in [<c>:L584-L588</c>].
    /// </summary>
    /// <remarks>
    /// DELIBERATELY DIFFERENT FROM <see cref="ServiceInitializationOrder"/> at positions three and
    /// four. The divergence is intentional-as-observed and must NOT be harmonised (constraint C-B);
    /// the parity tests assert that the two sequences DIFFER, so a tidying "consistency fix" fails the
    /// build rather than passing silently.
    /// </remarks>
    internal static IReadOnlyList<DataWindowAttachedServiceKind> ServiceCreationOrder { get; } =
    [
        DataWindowAttachedServiceKind.ContextMenu,     // :L570  Create n_cst_dwsvc_contextmenu
        DataWindowAttachedServiceKind.RowSelect,       // :L571  Create n_cst_dwsvc_rowselect
        DataWindowAttachedServiceKind.ColumnSort,      // :L572  Create n_cst_dwsvc_columnsort     <- 3rd
        DataWindowAttachedServiceKind.DropDownSearch,  // :L573  Create n_cst_dwsvc_dropdownsearch <- 4th
        DataWindowAttachedServiceKind.ColumnExp        // :L574  Create n_cst_dwsvc_columnexp
    ];

    /// <summary>
    /// The order the five attached services are INITIALISED in [<c>se_cst_dw.sru:L576-L580</c>].
    /// </summary>
    /// <remarks>
    /// POSITIONS THREE AND FOUR ARE SWAPPED relative to <see cref="ServiceCreationOrder"/>: the
    /// drop-down search service is initialised before the column-sort service even though it was
    /// created after it. This is the easiest thing in the whole file to "fix" by accident, so it is
    /// stated twice - here and at the constructor that performs it - and pinned by a test.
    /// </remarks>
    internal static IReadOnlyList<DataWindowAttachedServiceKind> ServiceInitializationOrder { get; } =
    [
        DataWindowAttachedServiceKind.ContextMenu,     // :L576  ContextMenu.Event OnInit(this)
        DataWindowAttachedServiceKind.RowSelect,       // :L577  RowSelect.Event OnInit(this)
        DataWindowAttachedServiceKind.DropDownSearch,  // :L578  DropDownSearch.Event OnInit(this) <- 3rd
        DataWindowAttachedServiceKind.ColumnSort,      // :L579  ColumnSort.Event OnInit(this)     <- 4th
        DataWindowAttachedServiceKind.ColumnExp        // :L580  ColumnExp.Event OnInit(this)
    ];

    /// <summary>
    /// The class name the broker's argument-injection hook tests ancestry against - the translation of
    /// the oracle's <c>IsAncestor(target,"n_cst_dwsvc")</c> [<c>se_cst_dw.sru:L603</c>].
    /// </summary>
    /// <remarks>
    /// <see cref="Ancestry.IsAncestor(object?, string)"/> compares the UNQUALIFIED
    /// <see cref="Type.Name"/> while walking base types, so the argument must be the .NET class name
    /// and not the PowerScript one; <see cref="DataWindowServiceBase"/> is the port of
    /// <c>n_cst_dwsvc</c>. Spelled with <c>nameof</c> so a rename of that type cannot leave this test
    /// silently matching nothing. The walk is SELF-INCLUSIVE - a type is its own ancestor - which is
    /// the legacy semantics and is what makes the base type itself an excluded target.
    /// </remarks>
    internal const string AttachedServiceAncestorClassName = nameof(DataWindowServiceBase);

    /// <summary>
    /// The value <c>filter</c> and <c>deleterow</c> return on success. <b>ONE, NOT ZERO.</b>
    /// </summary>
    /// <remarks>
    /// <c>:L407</c> is <c>if rtCode = 1 then</c> and <c>:L432</c> is
    /// <c>if rtCode &lt;&gt; 1 then return rtCode</c>. Deliberately NOT mapped onto
    /// <see cref="RetCode"/>, whose <see cref="RetCode.OK"/> is <c>0</c>: the two numbering schemes
    /// are incompatible and conflating them inverts every success test in both overrides.
    /// </remarks>
    internal const int DataWindowSuccess = 1;

    /// <summary>
    /// The value <c>deleterow</c> returns for an out-of-range request [<c>se_cst_dw.sru:L421</c>].
    /// </summary>
    internal const int DeleteRowOutOfRange = -1;

    /// <summary>
    /// The value <c>Describe("DataWindow.Processing")</c> must equal for the focus-free row switch to
    /// be attempted [<c>se_cst_dw.sru:L153</c>].
    /// </summary>
    /// <remarks>
    /// Compared as a STRING, exactly as the oracle compares it. <c>Describe</c> returns text, and
    /// parsing it to a number here would introduce a conversion the oracle does not perform and a
    /// failure mode it does not have.
    /// </remarks>
    internal const string DataWindowProcessingActive = "1";

    private readonly ValidationSession _session;
    private readonly IDataWindowEventObserver? _observer;
    private readonly DataWindowEventSequencer _sequencer;
    private readonly DataWindowEventBroker _eventful;

    // The substitute for PowerBuilder's `IsValid(this)` being false after `Destroy`. Set by Teardown,
    // read by the three liveness guards. Not volatile and not locked: a chain is single-threaded per
    // session by the same contract EventBroker already imposes, and a lock here would imply a
    // concurrency guarantee this type does not make.
    private bool _tornDown;

    /// <summary>
    /// Reproduces <c>onpreconstructor</c> [<c>se_cst_dw.sru:L570-L581</c>]: it creates the broker,
    /// creates the five attached services in the oracle's CREATION order, and initialises them in the
    /// oracle's DIFFERENT initialisation order.
    /// </summary>
    /// <param name="session">
    /// The per-session state carrying the four cross-event fields the oracle declares privately at
    /// <c>:L89-L96</c>: the disabled-event mask, the two re-entrancy flags, and the item-change return
    /// code the validation-error event consumes.
    /// </param>
    /// <param name="services">
    /// The factory the five <c>Create</c> statements at <c>:L570-L574</c> become. See DECISION 1.
    /// </param>
    /// <param name="observer">
    /// Receives the outcome of every dispatch. Optional: the oracle has no observer, so a chain
    /// without one behaves identically and simply reports nothing. See DECISION 2.
    /// </param>
    /// <param name="sequencer">
    /// Issues the monotonic sequencing token. Optional; a fresh one is created when omitted, which is
    /// correct for a chain that owns its own stream.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="session"/> or <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A factory member returned <see langword="null"/>. FAIL-FAST IS THE PRESERVED POSTURE
    /// (AAP 0.1.4): the legacy <c>Create</c> either yields an object or the runtime fails outright, and
    /// a chain that accepted a null service would fail later, at a dispatch, with nothing to point at.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE BROKER IS CREATED FIRST, AND THAT ORDER IS FORCED. <c>on se_cst_dw.create</c> instantiates
    /// it at <c>:L562</c>, and every service's <c>OnInit</c> reads it straight off the host -
    /// <c>#Eventful = dw.Eventful</c> [<c>n_cst_dwsvc.sru:L86</c>]. A service initialised before the
    /// broker existed would capture nothing.
    /// </para>
    /// <para>
    /// <b>THE DROPPED SUPER-CALL.</b> <c>:L570</c> opens with <c>call super::onpreconstructor</c>,
    /// which in the legacy runs se_cst_datawindow's theme registration -
    /// <c>IsPrevented(Event OnThemeRegistering())</c> then
    /// <c>ThemeManager().of_RegisterControl(this)</c>
    /// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L82-L87</c>]. ThemeManager
    /// belongs to the deferred DesignSystem service, so under constraint C-D that call has NO ANALOGUE
    /// AND IS DROPPED. It is a DOCUMENTED CAPABILITY GAP belonging to the reserved
    /// <c>/v1/design/**</c> Gateway extension point (AAP 0.4.4) - not an omission, and emphatically
    /// not something to stub. See DECISION 5.
    /// </para>
    /// <para>
    /// <c>this</c> ESCAPES THIS CONSTRUCTOR, DELIBERATELY AND AS THE ORACLE DOES. Both the broker and
    /// the five services receive the host while it is still being constructed, exactly as
    /// <c>onpreconstructor</c> passes <c>this</c> at <c>:L576-L580</c>. It is safe here because the
    /// only member either reaches during construction is <see cref="Eventful"/>, which is a sealed
    /// override over a field assigned before the escape; a derived type therefore cannot interpose an
    /// uninitialised broker.
    /// </para>
    /// </remarks>
    protected DataWindowEventChain(
        ValidationSession session,
        IDataWindowAttachedServiceFactory services,
        IDataWindowEventObserver? observer = null,
        DataWindowEventSequencer? sequencer = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(services);

        _session = session;
        _observer = observer;
        _sequencer = sequencer ?? new DataWindowEventSequencer();

        // :L562  this.eventful = create eventful
        // Before any service, for the reason in the remarks above.
        _eventful = new DataWindowEventBroker(this);

        // ------------------------------------------------------------------------------------------
        // :L570-L574  THE CREATION ORDER. ContextMenu, RowSelect, ColumnSort, DropDownSearch,
        //             ColumnExp. Position three is ColumnSort and position four is DropDownSearch.
        // ------------------------------------------------------------------------------------------
        ContextMenu = Require(services.CreateContextMenu(), nameof(services.CreateContextMenu));
        RowSelect = Require(services.CreateRowSelect(), nameof(services.CreateRowSelect));
        ColumnSort = Require(services.CreateColumnSort(), nameof(services.CreateColumnSort));
        DropDownSearch = Require(services.CreateDropDownSearch(), nameof(services.CreateDropDownSearch));
        ColumnExp = Require(services.CreateColumnExp(), nameof(services.CreateColumnExp));

        // ------------------------------------------------------------------------------------------
        // :L576-L580  THE INITIALISATION ORDER - AND IT IS NOT THE CREATION ORDER.
        //
        //     :L576  ContextMenu.Event OnInit(this)
        //     :L577  RowSelect.Event OnInit(this)
        //     :L578  DropDownSearch.Event OnInit(this)     <- THIRD, though created FOURTH
        //     :L579  ColumnSort.Event OnInit(this)         <- FOURTH, though created THIRD
        //     :L580  ColumnExp.Event OnInit(this)
        //
        // POSITIONS THREE AND FOUR ARE SWAPPED. This is preserved exactly as written and must NOT be
        // harmonised with the creation order (constraint C-B): the two sequences are separately
        // observable, a subscriber registered during OnInit lands in the broker's registry in this
        // order, and a test asserts that the two sequences DIFFER so that a tidying "consistency fix"
        // fails the build instead of passing silently.
        // ------------------------------------------------------------------------------------------
        ContextMenu.OnInit(this);
        RowSelect.OnInit(this);
        DropDownSearch.OnInit(this);
        ColumnSort.OnInit(this);
        ColumnExp.OnInit(this);
    }

    /// <summary>
    /// The nested broker instance - the port of <c>eventful eventful</c> [<c>se_cst_dw.sru:L33</c>],
    /// created at <c>:L562</c> and destroyed at <c>:L567</c>.
    /// </summary>
    /// <remarks>
    /// SEALED, because Domain/DataWindowServiceHost.cs's DECISION 6 assigns the declaration to the
    /// host and the SUPPLY to this chain, and because the constructor passes <c>this</c> to the five
    /// services which immediately read this member. A derived type able to substitute the broker could
    /// hand them one this chain never triggers.
    /// </remarks>
    public sealed override EventBroker Eventful => _eventful;

    /// <summary>
    /// The context-menu service [<c>se_cst_dw.sru:L80</c>]. <c>privatewrite</c> in the oracle, so the
    /// setter is absent rather than private.
    /// </summary>
    internal IDataWindowContextMenuService ContextMenu { get; }

    /// <summary>The row-selection service [<c>se_cst_dw.sru:L81</c>].</summary>
    internal IDataWindowRowSelectService RowSelect { get; }

    /// <summary>The column-sort service [<c>se_cst_dw.sru:L82</c>].</summary>
    internal IDataWindowColumnSortService ColumnSort { get; }

    /// <summary>The drop-down search service [<c>se_cst_dw.sru:L83</c>].</summary>
    internal IDataWindowDropDownSearchService DropDownSearch { get; }

    /// <summary>The column-expression service [<c>se_cst_dw.sru:L84</c>].</summary>
    internal IDataWindowColumnExpressionService ColumnExp { get; }

    /// <summary>
    /// The per-session state holding the four cross-event fields the oracle declares at
    /// <c>se_cst_dw.sru:L89-L96</c>.
    /// </summary>
    /// <remarks>
    /// Exposed so the boundary can project <c>ValidationSessionState</c> onto contract C-03's
    /// <c>EventResult.state</c> without a second route into the session registry. The chain never
    /// replaces it: the oracle's fields are instance fields of one control and have no lifetime of
    /// their own.
    /// </remarks>
    internal ValidationSession Session => _session;

    /// <summary>The sequencer issuing this chain's monotonic tokens.</summary>
    internal DataWindowEventSequencer Sequencer => _sequencer;

    private static T Require<T>(T service, string factoryMember)
        where T : class
    {
        // The legacy `Create` cannot yield nothing, so a null here is a structural fault in the
        // wiring rather than a runtime condition the oracle can reach. Fail fast rather than degrade
        // (AAP 0.1.4); softening this into a warning-and-continue would be a behavioural change
        // dressed as robustness.
        return service ?? throw new InvalidOperationException(
            $"IDataWindowAttachedServiceFactory.{factoryMember} returned null; the legacy `Create` at "
                + "se_cst_dw.sru:L570-L574 cannot yield nothing, so a null service is a wiring fault.");
    }

    /// <summary>
    /// Parses the twelve declared topic names into topics, in declaration order.
    /// </summary>
    /// <returns>The twelve topics.</returns>
    /// <remarks>
    /// THE RETURN TYPE NAMES THE IMMUTABILITY RATHER THAN LEAVING IT TO A COMMENT.
    /// <see cref="Topics"/> publishes <see cref="IReadOnlyList{T}"/> because that is the contract its
    /// consumers are written against, but this producer genuinely yields a
    /// <see cref="System.Collections.ObjectModel.ReadOnlyCollection{T}"/> and saying so makes the
    /// guarantee checkable at the signature. Stated for the avoidance of doubt: the reason is
    /// expressiveness, NOT performance (AAP 0.8.5 forbids justifying any design choice here on
    /// performance grounds).
    /// </remarks>
    private static System.Collections.ObjectModel.ReadOnlyCollection<SubscriptionTopic> BuildTopics()
    {
        // Declaration order, :L47 through :L76. The order of this array is itself observable: it is
        // what the parity test walks to assert that no topic carries a namespace suffix, and what a
        // dispatch-order assertion sorts to prove that "0-itemchanged" precedes "1-editchanged".
        string[] names =
        [
            EVT_ROWFOCUSCHANGING,   // :L47
            EVT_ROWFOCUSCHANGED,    // :L49
            EVT_ITEMFOCUSCHANGED,   // :L52
            EVT_ITEMCHANGED,        // :L54  "0-itemchanged"
            EVT_EDITCHANGED,        // :L57  "1-editchanged"
            EVT_CLICKED,            // :L60
            EVT_DOUBLECLICKED,      // :L63
            EVT_LBUTTONUP,          // :L66
            EVT_RBUTTONDOWN,        // :L69
            EVT_RBUTTONUP,          // :L72
            EVT_GETFOCUS,           // :L74
            EVT_LOSEFOCUS           // :L76
        ];

        List<SubscriptionTopic> topics = new(names.Length);

        foreach (string name in names)
        {
            // Parsed rather than constructed, so ToLegacyString re-emits the raw bytes and the
            // sequence/logical-name decomposition is the parser's rather than this file's. A failure
            // is impossible for these twelve literals and is still not swallowed: a silently missing
            // topic would detach every subscriber to it.
            long rtCode = SubscriptionTopic.ParseSubscription(name, out SubscriptionTopic? topic);

            if (rtCode != RetCode.OK || topic is null)
            {
                throw new InvalidOperationException(
                    $"The broker topic '{name}' declared at se_cst_dw.sru:L47-L76 did not parse as a "
                        + $"subscription topic (RetCode {rtCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}).");
            }

            topics.Add(topic);
        }

        // Wrapped rather than returned bare, because Topics is a STATIC shared collection: handing out
        // the List itself behind an interface would let any consumer cast it back and mutate the topic
        // set for the whole process. This is an immutability guarantee, not a performance choice
        // (AAP 0.8.5).
        return topics.AsReadOnly();
    }

    /// <summary>
    /// The topic value for one of the twelve declared names.
    /// </summary>
    /// <param name="name">One of the <c>EVT_*</c> constants.</param>
    /// <returns>
    /// The parsed topic, or <see langword="null"/> when <paramref name="name"/> is not one of the
    /// twelve. Never invents a topic, because a topic that is not in the oracle's twelve has no
    /// subscriber contract to describe.
    /// </returns>
    internal static SubscriptionTopic? TopicOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (SubscriptionTopic topic in Topics)
        {
            // Ordinal and case-SENSITIVE, because the broker's own name comparison is
            // [n_cst_eventful.sru registry scan] and a case-insensitive match here would find a topic
            // the broker would not dispatch.
            if (string.Equals(topic.LegacyName, name, StringComparison.Ordinal))
            {
                return topic;
            }
        }

        return null;
    }

    // ==============================================================================================
    //  THE TWO EXPLICIT ALPHABET CONVERSIONS                                          DECISION 3
    //  --------------------------------------------------------------------------------------------
    //  FIVE NUMERIC ALPHABETS SHARE THE NUMERALS 1 AND 2 in this area, so neither conversion below is
    //  ever performed implicitly and neither is ever performed in the reverse direction:
    //
    //      1. VetoResult                      0 continue, 1 prevent-once, 2 prevent-deep
    //      2. the return-code algebra          RetCode.OK 0, RetCode.PREVENT 1  <- what a handler
    //                                          returns and what every `= 1` site tests
    //      3. the exception hook's own set     1 prevent, 2 CONTINUE  <- 2 means the OPPOSITE of (1)
    //      4. the item-change alphabet         {0,1,2,3}, with 3 rewritten to 1 and `case else` to 2
    //      5. SetEnabled's veto mapping        a veto becomes RetCode.FAILED
    //
    //  The chain's CONTROL FLOW only ever uses (2), because that is what the oracle writes. The
    //  REPORT only ever uses (1), because that is what contract C-03 carries. ToVetoResult is the one
    //  bridge between them and it preserves 2 as PreventDeep instead of downgrading it.
    // ==============================================================================================

    /// <summary>
    /// Reads the numeric a broker dispatch produced out of the <see langword="object"/> the broker
    /// returns - the port of PowerScript's implicit <c>any</c>-to-<c>long</c> comparison at every
    /// <c>Eventful.of_Trigger(...) = 1</c> site.
    /// </summary>
    /// <param name="value">
    /// The dispatch result: the last invoked handler's value, or the topic's default return value when
    /// no handler ran. The nested broker sets that default to <c>0</c> [<c>se_cst_dw.sru:L596</c>].
    /// </param>
    /// <returns>
    /// The numeric, or <see langword="null"/> when the value is <see langword="null"/> or is not a
    /// number at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// NULL IS RETURNED AS NULL AND IS NEVER COLLAPSED TO ZERO (AAP 0.4.5.4). A null dispatch result
    /// is not a prevention and is not a continue-with-code-zero; it is the absence of a code, and the
    /// only thing the oracle does with it is fail the <c>= 1</c> test - which a null also does here.
    /// The distinction is preserved because the report carries the numeric verbatim for
    /// characterization comparison.
    /// </para>
    /// <para>
    /// A NON-NUMERIC RESULT ANSWERS NULL RATHER THAN THROWING. PowerScript would fault on comparing an
    /// <c>any</c> holding a string with <c>1</c>; a fault here would abort a dispatch the oracle can
    /// only reach by a subscriber returning the wrong type, and the observable outcome of that fault -
    /// the dispatch not being prevented - is what null already produces. Widening the contract with a
    /// guess is forbidden; narrowing it to "not a prevention" is the defined behaviour
    /// (AAP 0.1.5).
    /// </para>
    /// </remarks>
    internal static long? ToLegacyNumber(object? value)
    {
        return value switch
        {
            null => null,
            long l => l,
            int i => i,
            short s => s,
            sbyte sb => sb,
            byte b => b,
            ushort us => us,
            uint ui => ui,
            ulong ul when ul <= long.MaxValue => (long)ul,

            // PowerBuilder's `dec`/`decimal(n)` and `real`/`double` all reach an `any`. Only an exact
            // integral value can equal the integral prevent code, so a fractional one answers null for
            // the same reason a string does: it cannot be the code being tested for.
            decimal d when d == decimal.Truncate(d) && d >= long.MinValue && d <= long.MaxValue =>
                (long)d,
            double db when double.IsInteger(db) && db >= long.MinValue && db <= long.MaxValue =>
                (long)db,
            float f when float.IsInteger(f) && f >= long.MinValue && f <= long.MaxValue => (long)f,

            _ => null
        };
    }

    /// <summary>
    /// Projects a handler's numeric onto the tri-valued veto contract C-03 carries.
    /// </summary>
    /// <param name="value">The numeric a semantic handler or a broker dispatch produced.</param>
    /// <returns>
    /// <see cref="VetoResult.PreventOnce"/> for <c>1</c>, <see cref="VetoResult.PreventDeep"/> for
    /// <c>2</c>, and <see cref="VetoResult.Continue"/> for every other value INCLUDING
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>2 IS PRESERVED AS <see cref="VetoResult.PreventDeep"/> AND IS NEVER DOWNGRADED.</b> That is
    /// the whole reason the veto is tri-valued: flattening it would convert every deep prevention into
    /// a shallow one, silently, and only for nested dispatches - which
    /// <c>ondwnitemchange</c> genuinely produces [<c>se_cst_dw.sru:L207</c>].
    /// </para>
    /// <para>
    /// THIS MAPPING IS FOR THE REPORT ONLY. The oracle's own branch is <c>= 1</c>, so a handler
    /// returning <c>2</c> does NOT stop a raw event's dispatch, and no call site below tests this
    /// projection. Using it to decide control flow would widen the prevent condition from one value to
    /// two, which is a behaviour change (constraint C-B).
    /// </para>
    /// </remarks>
    internal static VetoResult ToVetoResult(long? value)
    {
        return value switch
        {
            (long)VetoResult.PreventOnce => VetoResult.PreventOnce,
            (long)VetoResult.PreventDeep => VetoResult.PreventDeep,
            _ => VetoResult.Continue
        };
    }

    // ==============================================================================================
    //  THE NINE SEMANTIC EVENTS                                              se_cst_dw.sru:L11-L32
    //  --------------------------------------------------------------------------------------------
    //  FIVE ARE DECLARED HERE. The other four - `ondoitemchange` [:L24], `ondoitemchanged` [:L26],
    //  `oninitcontextmenu` [:L11] and `oncontextmenu` [:L12] - are declared on DataWindowServiceHost
    //  because an ATTACHED SERVICE raises them on its host rather than the chain raising them on
    //  itself, and they are OVERRIDDEN below with the bodies se_cst_dw gives them.
    //
    //  THE CONTEXT-MENU PAIR MOVED, AND THE MOVE WAS FORCED BY THE ORACLE RATHER THAN CHOSEN. Both were
    //  originally declared here as `virtual`, while the doc-comment on each already recorded that they
    //  are raised BY THE CONTEXT-MENU SERVICE ON ITS HOST and cited
    //  n_cst_dwsvc_contextmenu.sru:L147 and :L194 for it. A service holds its host as a
    //  DataWindowServiceHost, so those two raise sites could not compile against a member declared only
    //  here. They are now declared on the host and OVERRIDDEN here, so the chain's surface is unchanged
    //  for every existing consumer - including the nine-event reflection table in
    //  DataWindowEventChainTests - and there is still exactly ONE definition of each legacy event.
    //
    //  VIRTUAL WITH A NO-OP DEFAULT, NOT ABSTRACT, AND THE ORACLE PROVES THE VIRTUAL IS USED. Five
    //  objects derive from se_cst_dw - the w_test_dwsvc_* windows in ws_objects/pfw.tests.pbl.src -
    //  and they extend these events with `call super::<event>`, for instance
    //  w_test_dwsvc_dropdownsearch.srw:L59 `event onddsgetfilter;call super::onddsgetfilter;`. A
    //  PowerBuilder event with no script attached yields its type's initial value and performs
    //  nothing, which is exactly what each default body below does.
    //
    //  THEY ARE METHODS RATHER THAN C# `event` MEMBERS for the reason DataWindowServiceHost states for
    //  its own eleven: a PowerBuilder event raised with the `Event` keyword RETURNS A VALUE to its
    //  single raiser, and a multicast void `event` cannot express that. The genuinely multicast half
    //  of the legacy model is the broker.
    // ==============================================================================================

    /// <summary>
    /// Raised so the application can populate the context menu - the port of
    /// <c>event type long oninitcontextmenu ( long row, dwobject dwo )</c>
    /// (<c>se_cst_dw.sru:L11</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row the menu was requested over, or <c>0</c> when none.</param>
    /// <param name="dwo">The object the menu was requested over.</param>
    /// <returns><c>1</c> to prevent the menu; any other value to continue.</returns>
    /// <remarks>
    /// Raised by the context-menu service, not by this chain:
    /// <c>if #DataWindow.Event OnInitContextMenu(row,dwo) = 1 then return</c>
    /// [<c>n_cst_dwsvc_contextmenu.sru:L147</c>]. ORDERING: SYNCHRONOUS - initialisation must COMPLETE
    /// before the menu identifier passed to <see cref="OnContextMenu(long, IDataWindowObject, long)"/>
    /// can mean anything.
    /// </remarks>
    public override long OnInitContextMenu(long row, IDataWindowObject dwo)
    {
        return 0L;
    }

    /// <summary>
    /// Raised when a context-menu item has been chosen - the port of
    /// <c>event type long oncontextmenu ( long row, dwobject dwo, long mid )</c>
    /// (<c>se_cst_dw.sru:L12</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row the menu was shown over.</param>
    /// <param name="dwo">The object the menu was shown over.</param>
    /// <param name="mid">
    /// The chosen menu item's identifier. Meaningful only because
    /// <see cref="OnInitContextMenu(long, IDataWindowObject)"/> has already completed - the raise site
    /// passes the identifier the menu returned [<c>n_cst_dwsvc_contextmenu.sru:L194</c>].
    /// </param>
    /// <returns><c>1</c> to prevent the service's own handling; any other value to continue.</returns>
    /// <remarks>ORDERING: SYNCHRONOUS, the second half of the context-menu pair.</remarks>
    public override long OnContextMenu(long row, IDataWindowObject dwo, long mid)
    {
        return 0L;
    }

    /// <summary>
    /// Raised so the application can supply the drop-down search filter - the port of
    /// <c>event onddsgetfilter ( long row, dwobject dwo, string data, ref string filter )</c>
    /// (<c>se_cst_dw.sru:L13</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being searched from.</param>
    /// <param name="dwo">The column being searched.</param>
    /// <param name="data">The text typed so far, verbatim and UNESCAPED.</param>
    /// <param name="filter">
    /// THE RESULT. This event has NO RETURN TYPE; its result is produced by MUTATING THIS REFERENCE.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>THE <c>ref</c> OUT-PARAMETER IS WHY DROP-DOWN SEARCH IS ASSIGNED PATTERN (b).</b> A
    /// <c>ref</c> out-parameter has NO ASYNCHRONOUS REPRESENTATION: the caller blocks on the produced
    /// filter because the filter is the only thing the call exists to obtain. Modelled as a
    /// <c>ref</c> parameter per AAP 0.4.5.2 and NOT converted into a return value - a return value
    /// would destroy the distinction between "the handler assigned an empty filter", which means match
    /// nothing, and "the handler did not touch the parameter", which means it declined to filter.
    /// </para>
    /// <para>
    /// NO RAISE SITE EXISTS INSIDE THE IN-SCOPE LIBRARY, and that was measured rather than assumed: a
    /// repository-wide search finds this event's declaration at <c>:L13</c> and exactly one
    /// SUBSCRIBER, the override at <c>ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw:L59</c>.
    /// The raise belongs to the presentational half of the drop-down search service, which is deferred
    /// (AAP 0.2.1.3 Correction 4). The event is declared here regardless, because dropping it would
    /// make the 22-event surface incomplete and would leave the one existing subscriber with nothing
    /// to attach to.
    /// </para>
    /// </remarks>
    public virtual void OnDdsGetFilter(long row, IDataWindowObject dwo, string data, ref string filter)
    {
        // A PowerBuilder event with no script attached performs nothing and leaves its `ref` parameter
        // exactly as the caller passed it. Assigning anything here - even the empty string - would
        // convert "declined to filter" into "match nothing" for every unhandled occurrence.
    }

    /// <summary>
    /// Raised so the APPLICATION can evaluate a column-expression macro - the port of
    /// <c>event type any oncolumnexpinvokemethod ( long row, dwobject dwo, string name, string args[] )</c>
    /// (<c>se_cst_dw.sru:L14</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being calculated.</param>
    /// <param name="dwo">The column being calculated.</param>
    /// <param name="name">
    /// The macro function name. The reserved dispatch sentinel is <c>"Invoke"</c>; an empty name models
    /// a bare variable reference.
    /// </param>
    /// <param name="args">
    /// The arguments, AS STRINGS, which the handler coerces itself. ONE-BASED IN THE LEGACY: the
    /// authoritative specification's worked handler is
    /// <c>return Round(Double(args[1]), Long(args[2]))</c>
    /// [<c>docs/n_cst_dwsvc_columnexp.md</c>], which shows both facts in one line. A .NET array is
    /// zero-based, so a consumer bridging to legacy-shaped code shifts by one - stated explicitly
    /// because AAP 0.4.5.4 names one-based translation the most dangerous mechanical hazard in this
    /// refactor.
    /// </param>
    /// <returns>
    /// The macro's value, as the PowerScript <c>any</c> that AAP 0.4.5.2 maps to
    /// <see langword="object"/>. <see langword="null"/> is a legitimate result and is not a
    /// "no result" marker.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE INVERTED HALF OF CONTRACT C-04's <c>InvokeMethodChannel</c>.</b> The legacy
    /// expects the APPLICATION to implement the macro switch, and it is in the same process; across a
    /// service boundary DataServices holds the expression engine while its client holds the
    /// application, so DataServices must CALL BACK INTO ITS CLIENT. That is exactly what a
    /// <see langword="virtual"/> member with a no-op default expresses: the chain declares the
    /// question, an override or a stream adapter answers it. Raised by the expression engine at
    /// <c>n_cst_dwsvc_columnexp.sru:L2263</c> and <c>:L2287</c>.
    /// </para>
    /// <para>
    /// ORDERING: SYNCHRONOUS. The calculation cannot proceed without the returned value, so there is
    /// no version of this event that can be fired and forgotten.
    /// </para>
    /// </remarks>
    public virtual object? OnColumnExpInvokeMethod(
        long row,
        IDataWindowObject dwo,
        string name,
        string[] args)
    {
        // An unhandled PowerBuilder event of type `any` yields null, NOT zero and NOT the empty
        // string. Substituting either would make an unhandled macro indistinguishable from one that
        // legitimately evaluated to that value.
        return null;
    }

    /// <summary>
    /// Raised after an item's value has genuinely changed - the port of
    /// <c>event onitemchanged ( long row, dwobject dwo )</c> (<c>se_cst_dw.sru:L25</c>), raised as
    /// STEP THREE of <c>ondoitemchanged</c> [<c>:L319</c>].
    /// </summary>
    /// <param name="row">The ONE-BASED row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    /// <remarks>
    /// <para>
    /// NOT TO BE CONFUSED WITH <see cref="DataWindowServiceHost.ItemChanged(long, IDataWindowObject, string)"/>,
    /// which is the ANCESTRY event raised at <c>:L292</c> and takes the edit text and returns the
    /// four-value alphabet. These are two different events that differ by one word of spelling, and
    /// the oracle raises them from two different places for two different purposes: <c>ItemChanged</c>
    /// asks whether a change may proceed, <c>OnItemChanged</c> reports that one did.
    /// </para>
    /// <para>
    /// RETURNS NOTHING, which is the legacy declaration rather than a simplification: <c>:L25</c>
    /// carries no <c>type</c> clause, so the raise at <c>:L319</c> discards nothing and no veto is
    /// possible from here.
    /// </para>
    /// </remarks>
    public virtual void OnItemChanged(long row, IDataWindowObject dwo)
    {
    }

    /// <summary>
    /// Raised after the drop-down search filter has been applied - the port of
    /// <c>event onddsfiltered ( long row, dwobject dwo, long rowcount, long filteredcount )</c>
    /// (<c>se_cst_dw.sru:L28</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row the search was started from.</param>
    /// <param name="dwo">The column being searched.</param>
    /// <param name="rowCount">Rows REMAINING after the filter.</param>
    /// <param name="filteredCount">Rows REMOVED by the filter.</param>
    /// <remarks>
    /// ORDERING: SYNCHRONOUS, the second half of the drop-down search pair. BOTH COUNTS ARE HEADLESS
    /// DATA and both ship (constraint C-D): they are the search result's SHAPE, not its appearance.
    /// Like <see cref="OnDdsGetFilter"/> this event has no raise site inside the in-scope library,
    /// because the raise belongs to the deferred presentational half.
    /// </remarks>
    public virtual void OnDdsFiltered(
        long row,
        IDataWindowObject dwo,
        long rowCount,
        long filteredCount)
    {
    }

    /// <summary>
    /// Raised for every column-expression evaluation, for diagnostics - the port of
    /// <c>event oncolumnexptrace ( long row, dwobject dwo, string stack, string expr, string value )</c>
    /// (<c>se_cst_dw.sru:L32</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being calculated.</param>
    /// <param name="dwo">The column being calculated.</param>
    /// <param name="stack">
    /// THE CALL STACK, <c>&gt;</c>-DELIMITED. The engine flattens the recursion stack its vector builds
    /// into one delimited string before raising this [<c>n_cst_dwsvc_columnexp.sru:L753-L755</c>], and
    /// the flattened form is what the legacy event carries, so it is what travels.
    /// </param>
    /// <param name="expr">The expression as evaluated.</param>
    /// <param name="value">
    /// The resulting value, RENDERED AS THE LEGACY RENDERS IT. One formatting defect is observable and
    /// preserved (constraint C-B): the literal text <c>(null)</c> is reported when the value is empty
    /// and either the expression treats empty as null or the column is not a string column
    /// [<c>n_cst_dwsvc_columnexp.sru:L758</c>], so three distinct states render identically. Consumers
    /// must not parse this to recover null-ness.
    /// </param>
    /// <remarks>ORDERING: SEQUENCED. Pure diagnostics, fire-and-forget.</remarks>
    public virtual void OnColumnExpTrace(
        long row,
        IDataWindowObject dwo,
        string stack,
        string expr,
        string value)
    {
    }

    /// <summary>
    /// Asks whether one item's value may change - the port of the <c>ondoitemchange</c> BODY
    /// (<c>se_cst_dw.sru:L256-L293</c>), overriding the declaration
    /// <see cref="DataWindowServiceHost.OnDoItemChange(long, IDataWindowObject, string?)"/>.
    /// </summary>
    /// <param name="row">The ONE-BASED row whose item is changing.</param>
    /// <param name="dwo">The column the change applies to.</param>
    /// <param name="data">
    /// The proposed value as text. NOT YET WRITTEN TO THE BUFFER when this runs - the oracle's own
    /// comment says so [<c>:L259</c>]. NULL IS LEGAL AND IS NOT A STRUCTURAL FAULT: an attached
    /// service reaches this event with a null after <c>SetNull(sVal)</c>
    /// [<c>n_cst_dwsvc_contextmenu.sru:L1050-L1051</c>], which is how a pasted empty cell asks a
    /// <c>NilIsNull</c> column to store a null. It is forwarded unchanged rather than coerced.
    /// </param>
    /// <returns>
    /// The four-value item-change alphabet, propagated verbatim from the ancestry event this delegates
    /// to. NEVER a <see cref="RetCode"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> is <see langword="null"/>. A structural guard on a parameter the
    /// signature already declares non-nullable; it cannot fire for any input the oracle can produce.
    /// <paramref name="data"/> is DELIBERATELY NOT GUARDED - see its parameter note.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE BODY IS EFFECTIVELY ONE LINE, <c>:L292</c>. Everything above it in the oracle is a comment
    /// block, three locals and a DORMANT validation path: a commented-out byte-length check with its
    /// own dialog [<c>:L280-L290</c>] and a DEAD assignment <c>sColName = dwo.Name</c> [<c>:L278</c>]
    /// whose local is never read. Both are recorded on Domain/ItemChangeProtocol.cs, which owns the
    /// surrounding protocol, and NEITHER IS DUPLICATED HERE. Reviving the check would reject edit text
    /// the oracle accepts and would steer the outer dispatch into a different arm (constraint C-B).
    /// </para>
    /// <para>
    /// The four-value alphabet is reported typed as well as raw, so a consumer can reason on it without
    /// re-deriving the classification; <see cref="ItemChangeProtocol.Classify(long)"/> owns that
    /// mapping, including the fact that every value outside <c>{1,2,3}</c> is
    /// <see cref="ItemChangeResult.Default"/>.
    /// </para>
    /// </remarks>
    public override long OnDoItemChange(long row, IDataWindowObject dwo, string? data)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        // NO GUARD ON `data`. A null is a legal, oracle-reachable value here - see the parameter note -
        // and it is forwarded to the semantic event unchanged.
        DataWindowEventOutcome outcome =
            NewOutcome(EventId.Ondoitemchange, withinItemChangeChain: true);

        // :L292  return Event ItemChanged(row,dwo,data)
        long rtCode = ItemChanged(row, dwo, data);

        Report(outcome with
        {
            ReturnValue = rtCode,
            ItemChangeResult = ItemChangeProtocol.Classify(rtCode),
            Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
        });

        return rtCode;
    }

    /// <summary>
    /// Reports that an item's value has changed and been written - the port of the
    /// <c>ondoitemchanged</c> BODY (<c>se_cst_dw.sru:L295-L320</c>), overriding the declaration
    /// <see cref="DataWindowServiceHost.OnDoItemChanged(long, IDataWindowObject)"/>.
    /// </summary>
    /// <param name="row">The ONE-BASED row whose item changed.</param>
    /// <param name="dwo">The column that changed.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>ITS INTERNAL ORDER IS CONTRACT AND ALL THREE STEPS ARE CONDITIONAL DIFFERENTLY.</b>
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <c>:L313-L315</c> the column-expression service's changed handler, IF THAT SERVICE IS
    ///   ENABLED. This step is the mechanism behind the event-gate coupling: because it is reached
    ///   only through this path, and the <c>EID_ITEMCHANGE</c> guard short-circuits that path,
    ///   disabling item change also stops every bound expression recalculating.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L316-L318</c> the broker trigger, IF THE TOPIC HAS A SUBSCRIBER. The trigger's result is
    ///   NOT tested - <c>:L317</c> is a bare statement, unlike the trigger sites in the raw handlers -
    ///   so a subscriber cannot veto from here. The veto is still REPORTED, because the report exists
    ///   to describe what happened and a subscriber returning <c>1</c> here is worth seeing even
    ///   though the oracle ignores it.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L319</c> the semantic event, UNCONDITIONALLY.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Both conditions are PART OF THE SEQUENCE rather than incidental, which is why the report
    /// carries all three flags: a consumer observing only two of the three must be able to tell that
    /// the third was CONDITIONALLY SKIPPED rather than skipped in error.
    /// </para>
    /// </remarks>
    public override void OnDoItemChanged(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome =
            NewOutcome(EventId.Ondoitemchanged, withinItemChangeChain: true);

        bool columnExpressionRan = false;
        bool brokerRan = false;
        long? brokerResult = null;

        // ------------------------------------------------------------------------------------------
        // STEP 1  :L313-L315   if ColumnExp.#Enabled then ColumnExp.Event OnItemChanged(row,dwo)
        // ------------------------------------------------------------------------------------------
        if (ColumnExp.Enabled)
        {
            ColumnExp.OnItemChanged(row, dwo);
            columnExpressionRan = true;
        }

        // ------------------------------------------------------------------------------------------
        // STEP 2  :L316-L318   if Eventful.of_IsSubscribed(EVT_ITEMCHANGED) then
        //                          Eventful.of_Trigger(EVT_ITEMCHANGED,row,dwo)
        //
        // THE SUBSCRIBER PROBE IS PART OF THE CONTRACT AND NOT AN OPTIMISATION (AAP 0.8.5 forbids
        // describing it as one). It is observable: without a subscriber the broker's triggering and
        // triggered hooks do not fire and the topic's default return value is not resolved.
        // ------------------------------------------------------------------------------------------
        if (_eventful.IsSubscribed(EVT_ITEMCHANGED))
        {
            brokerResult = ToLegacyNumber(_eventful.Trigger(EVT_ITEMCHANGED, row, dwo));
            brokerRan = true;
        }

        // ------------------------------------------------------------------------------------------
        // STEP 3  :L319   Event OnItemChanged(row,dwo)   - UNCONDITIONAL
        // ------------------------------------------------------------------------------------------
        OnItemChanged(row, dwo);

        Report(outcome with
        {
            // A void event. The report's numeric is 0 because there is nothing to return, not because
            // the dispatch succeeded with code zero.
            ReturnValue = 0L,
            BrokerVeto = ToVetoResult(brokerResult),
            Topic = TopicOf(EVT_ITEMCHANGED),
            Dispatch = new DataWindowDispatchReport
            {
                ColumnExpressionHandlerRan = columnExpressionRan,
                BrokerTriggerRan = brokerRan,
                SemanticHandlerRan = true
            }
        });
    }

    // ==============================================================================================
    //  THE TWO REPORTING PRIMITIVES - protected, and deliberately so
    //  --------------------------------------------------------------------------------------------
    //  THE NINE SEMANTIC EVENTS ARE OUTBOUND CALLS, AND THEIR OUTCOMES BELONG TO WHOEVER ANSWERS THEM.
    //  The thirteen raw events are dispatch ENTRY POINTS - this chain is the dispatcher, so it reports
    //  them itself. Seven of the nine semantic events are the opposite: they are questions this chain
    //  asks the application, their default bodies are the empty PowerBuilder events se_cst_dw declares
    //  at :L11-L14, :L25, :L28 and :L32, and an unhandled one performs nothing. A default that reported
    //  would be reporting a dispatch that did not happen, and a default that reported BEFORE an
    //  override computed the real answer would report the wrong result - PowerBuilder's descendants
    //  extend these with `call super::<event>` [w_test_dwsvc_dropdownsearch.srw:L59], so base-first is
    //  exactly the shape a faithful override takes.
    //
    //  So these two are protected rather than private: an override that PRODUCES a result - the C-03
    //  and C-04 stream adapters in Grpc/, which marshal these seven onto the wire - issues its own
    //  token and reports its own outcome, including the two payload fields only it can fill in,
    //  DataWindowEventOutcome.ProducedFilter for onddsgetfilter's `ref string` result and
    //  DataWindowEventOutcome.AnyResult for oncolumnexpinvokemethod's `any` result. Without this the
    //  two fields would be unreachable by anyone, which is the same as not existing.
    //
    //  ONE CAVEAT, RECORDED RATHER THAN GUARDED AGAINST. An override of a RAW event that also calls
    //  base would report twice, because the base already reports. That is provably not a shape the
    //  oracle takes: all five descendants of se_cst_dw - the w_test_dwsvc_* windows - override semantic
    //  events only, and not one overrides a raw `ondwn*` event.
    // ==============================================================================================

    /// <summary>
    /// Opens an outcome for one dispatch: issues its token and resolves its ordering discipline.
    /// </summary>
    /// <param name="eventId">The event being dispatched, numbered in source declaration order.</param>
    /// <param name="withinItemChangeChain">
    /// <see langword="true"/> when this occurrence is the tail of the item-change chain rather than a
    /// standalone notification. Meaningful for <see cref="EventId.Ondwnkillfocus"/> only.
    /// </param>
    /// <returns>
    /// A fresh outcome carrying the token and the discipline, with an empty dispatch report the caller
    /// completes with <c>with</c> before handing it to <see cref="Report(DataWindowEventOutcome)"/>.
    /// </returns>
    protected DataWindowEventOutcome NewOutcome(EventId eventId, bool withinItemChangeChain = false)
    {
        // The token is issued for EVERY dispatch, observer or not, so that Sequencer.LastIssued is a
        // faithful count of dispatches rather than a count of observed ones. A gated-out dispatch is
        // still a dispatch that happened.
        return new DataWindowEventOutcome
        {
            EventId = eventId,
            Sequence = _sequencer.Issue(),
            Discipline = DataWindowEventOrdering.DisciplineOf(eventId, withinItemChangeChain),
            Dispatch = DataWindowDispatchReport.None
        };
    }

    /// <summary>
    /// Pushes one completed outcome to the observer, if there is one.
    /// </summary>
    /// <param name="outcome">The completed outcome.</param>
    /// <remarks>
    /// PUSHED, NOT STORED, and that is forced rather than chosen: <c>se_cst_dw.sru:L207</c> fires an
    /// event from inside another event, so a last-outcome slot would be overwritten by the inner
    /// dispatch before the outer one could be read. Outcomes therefore arrive in COMPLETION order,
    /// which for a nested run puts the inner event's outcome ahead of its enclosing one even though the
    /// enclosing one holds the lower token.
    /// </remarks>
    protected void Report(DataWindowEventOutcome outcome)
    {
        // No null-conditional-with-side-effects subtlety: the observer is optional because the oracle
        // has none, and a chain without one must behave identically.
        _observer?.OnEventDispatched(outcome);
    }

    // ==============================================================================================
    //  LIVENESS - THE PORT OF `IsValid(this)`                          se_cst_dw.sru:L138 :L147 :L152
    //  --------------------------------------------------------------------------------------------
    //  THREE GUARD SITES, NOT TWO. The folder brief named two; the source has three, and the third
    //  carries an extra condition, so they are not interchangeable:
    //
    //      :L138  if IsValid(this) then                    - before the double-click broker edge
    //      :L147  if IsValid(this) then                    - before the click broker edge
    //      :L152  if IsValid(this) and row > 0 then        - before the focus-free row switch
    //
    //  THEY ARE NOT DEFENSIVE NOISE. A click or double-click handler is permitted to destroy the
    //  control it was raised on - the oracle's own validation-error handler notes at :L368 that a
    //  dialog's posted message may have deleted a row - and the oracle re-tests liveness before
    //  touching the control again.
    //
    //  WHY Predicates.IsValidObject IS *NOT* WHAT THIS CALLS. That function is the port of
    //  ws_objects/pfw.common.pbl.src/isvalidobject.srf, a DIFFERENT function; and its own
    //  documentation records that in .NET the exact equivalent of "created and not yet destroyed"
    //  reduces to "not null", which for `this` is unconditionally true. Routing the guard through it
    //  would make all three sites inert - the guard would never fire, and the skip it exists to
    //  produce would become unreachable and untestable. The concept the oracle is testing is
    //  DESTRUCTION, and this chain's destruction is Teardown, so the flag Teardown sets is the
    //  faithful substitute.
    // ==============================================================================================

    /// <summary>
    /// Whether the chain has not yet been torn down - the port of <c>IsValid(this)</c> at
    /// <c>se_cst_dw.sru:L138</c>, <c>:L147</c> and <c>:L152</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> from the moment <see cref="Teardown"/> begins. A handler that tears the
    /// chain down mid-dispatch therefore causes the remaining steps of that dispatch to be skipped,
    /// which is exactly what the oracle's guards produce when a handler destroys the control.
    /// </remarks>
    internal bool IsAlive => !_tornDown;

    // ==============================================================================================
    //  THE THIRTEEN RAW pbm_dwn* HANDLERS, IN DECLARATION ORDER          se_cst_dw.sru:L15-L31
    //  --------------------------------------------------------------------------------------------
    //  THE PATTERN IS raw -> semantic -> broker, AND IT IS NOT UNIFORM. The variations ARE the
    //  behaviour, so each body below is ported line by line rather than through a shared template:
    //
    //      * TWO have NO semantic event at all and consult only the broker: ondwnrbuttonup [:L120]
    //        and ondwnlbuttonup [:L395]. Do not invent an RButtonUp or an LButtonUp.
    //      * ondwnrowchange does NOT test its broker result [:L126] while ondwnrowchanging DOES
    //        [:L132]. A change that has already happened cannot be vetoed; one that is about to
    //        happen can.
    //      * ondwnsetfocus and ondwnkillfocus trigger the broker BEFORE the semantic event and do not
    //        test the result [:L391, :L399].
    //      * ondwnchanging probes for a subscriber before triggering [:L166] and then calls a service
    //        hook directly [:L169-L171].
    //      * ondwnlbuttondblclk and ondwnlbuttonclk guard the broker edge on liveness [:L138, :L147].
    //      * Four are gated on the event bitmask [:L124, :L130, :L176, :L187], and all four
    //        early-outs `return 0` - a gated event reports CONTINUE, never a prevention.
    // ==============================================================================================

    /// <summary>
    /// <c>pbm_dwnrbuttondown</c> - the port of <c>ondwnrbuttondown</c>
    /// (<c>se_cst_dw.sru:L115-L118</c>).
    /// </summary>
    /// <param name="xpos">
    /// The pointer x position in the DataWindow's OWN LOGICAL UNITS, exactly as the runtime supplies
    /// it. NEVER converted: no DPI conversion, no font metric and no window rectangle appears anywhere
    /// in this file (constraint C-D). These are INPUT COORDINATES consumed as data, not geometry this
    /// file computes.
    /// </param>
    /// <param name="ypos">The pointer y position, in the same units.</param>
    /// <param name="row">The ONE-BASED row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> when either edge prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    public virtual long OnDwnRButtonDown(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnrbuttondown);
        SubscriptionTopic? topic = TopicOf(EVT_RBUTTONDOWN);

        // :L115  if Event RButtonDown(xpos,ypos,row,dwo) = 1 then return 1
        long semantic = RButtonDown(xpos, ypos, row, dwo);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        // :L116  if Eventful.of_Trigger(EVT_RBUTTONDOWN,xpos,ypos,row,dwo) = 1 then return 1
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_RBUTTONDOWN, xpos, ypos, row, dwo));

        // :L117  return 0
        long rtCode = broker == RetCode.PREVENT ? RetCode.PREVENT : RetCode.OK;

        Report(outcome with
        {
            ReturnValue = rtCode,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnrbuttonup</c> - the port of <c>ondwnrbuttonup</c> (<c>se_cst_dw.sru:L120-L122</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position, in the DataWindow's own logical units.</param>
    /// <param name="ypos">The pointer y position, in the same units.</param>
    /// <param name="row">The ONE-BASED row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> when the broker prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>BROKER EDGE ONLY - THE FIRST OF THE TWO SUCH HANDLERS.</b> There is no
    /// <c>Event RButtonUp</c> anywhere on the ancestry, so no semantic handler is consulted and the
    /// report's <see cref="DataWindowDispatchReport.SemanticHandlerRan"/> is
    /// <see langword="false"/> by construction rather than by circumstance. Inventing a semantic
    /// counterpart would fabricate a hook the oracle does not have (constraint C-B) and would give
    /// this chain a twenty-third event that nothing raises. The other such handler is
    /// <see cref="OnDwnLButtonUp"/>.
    /// </remarks>
    public virtual long OnDwnRButtonUp(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnrbuttonup);

        // :L120  if Eventful.of_Trigger(EVT_RBUTTONUP,xpos,ypos,row,dwo) = 1 then return 1
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_RBUTTONUP, xpos, ypos, row, dwo));

        // :L121  return 0
        long rtCode = broker == RetCode.PREVENT ? RetCode.PREVENT : RetCode.OK;

        Report(outcome with
        {
            ReturnValue = rtCode,
            BrokerVeto = ToVetoResult(broker),
            Topic = TopicOf(EVT_RBUTTONUP),
            Dispatch = new DataWindowDispatchReport { BrokerTriggerRan = true }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnrowchange</c> - the port of <c>ondwnrowchange</c> (<c>se_cst_dw.sru:L124-L128</c>).
    /// </summary>
    /// <param name="currentRow">The new current row, ONE-BASED. <c>0</c> when the DataWindow is empty.</param>
    /// <returns><c>1</c> when the semantic handler prevented; otherwise <c>0</c>.</returns>
    /// <remarks>
    /// <para>
    /// GATED ON <c>EID_ROWFOCUSCHANGE</c> [<c>:L124</c>]. The early-out returns <c>0</c>, so a gated
    /// event reports CONTINUE and not a prevention; the report's
    /// <see cref="DataWindowDispatchReport.GatedOut"/> flag is the only thing that keeps the two
    /// distinguishable.
    /// </para>
    /// <para>
    /// <b>THE BROKER RESULT IS NOT TESTED</b> [<c>:L126</c> is a bare statement], which is a
    /// DELIBERATE ASYMMETRY against <see cref="OnDwnRowChanging(long, long)"/> whose result IS tested.
    /// The veto is still reported, because a subscriber returning <c>1</c> here is worth seeing even
    /// though the oracle ignores it - but acting on it would ADD a prevention path the oracle does not
    /// have.
    /// </para>
    /// <para>
    /// ALSO RAISED DIRECTLY BY <see cref="DeleteRow(long)"/> [<c>:L435</c>, <c>:L437</c>], which is why
    /// this member is reachable without a live runtime at all.
    /// </para>
    /// </remarks>
    public virtual long OnDwnRowChange(long currentRow)
    {
        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnrowchange);

        // :L124  if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE) then return 0
        if (_session.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE))
        {
            Report(outcome with
            {
                ReturnValue = RetCode.OK,
                Dispatch = new DataWindowDispatchReport { GatedOut = true }
            });

            return RetCode.OK;
        }

        // :L125  if Event RowFocusChanged(currentRow) = 1 then return 1
        long semantic = RowFocusChanged(currentRow);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = TopicOf(EVT_ROWFOCUSCHANGED),
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        // :L126  Eventful.of_Trigger(EVT_ROWFOCUSCHANGED,currentRow)   - RESULT DISCARDED
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_ROWFOCUSCHANGED, currentRow));

        // :L127  return 0   - unconditionally, whatever the broker said
        Report(outcome with
        {
            ReturnValue = RetCode.OK,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = TopicOf(EVT_ROWFOCUSCHANGED),
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return RetCode.OK;
    }

    /// <summary>
    /// <c>pbm_dwnrowchanging</c> - the port of <c>ondwnrowchanging</c>
    /// (<c>se_cst_dw.sru:L130-L134</c>).
    /// </summary>
    /// <param name="currentRow">The row focus is leaving, ONE-BASED.</param>
    /// <param name="newRow">The row focus is moving to, ONE-BASED.</param>
    /// <returns><c>1</c> when either edge prevented; otherwise <c>0</c>.</returns>
    /// <remarks>
    /// GATED ON <c>EID_ROWFOCUSCHANGE</c> [<c>:L130</c>] - the SAME bit as
    /// <see cref="OnDwnRowChange(long)"/>, so disabling it silences both halves of the pair. Unlike
    /// that handler, THIS ONE TESTS THE BROKER RESULT [<c>:L132</c>]: a row change that has not
    /// happened yet can be vetoed.
    /// </remarks>
    public virtual long OnDwnRowChanging(long currentRow, long newRow)
    {
        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnrowchanging);
        SubscriptionTopic? topic = TopicOf(EVT_ROWFOCUSCHANGING);

        // :L130  if BitTest(_nDisabledEvent,EID_ROWFOCUSCHANGE) then return 0
        if (_session.IsEventDisabled(EventGate.EID_ROWFOCUSCHANGE))
        {
            Report(outcome with
            {
                ReturnValue = RetCode.OK,
                Dispatch = new DataWindowDispatchReport { GatedOut = true }
            });

            return RetCode.OK;
        }

        // :L131  if Event RowFocusChanging(currentrow,newrow) = 1 then return 1
        long semantic = RowFocusChanging(currentRow, newRow);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        // :L132  if Eventful.of_Trigger(EVT_ROWFOCUSCHANGING,currentRow,newrow) = 1 then return 1
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_ROWFOCUSCHANGING, currentRow, newRow));

        // :L133  return 0
        long rtCode = broker == RetCode.PREVENT ? RetCode.PREVENT : RetCode.OK;

        Report(outcome with
        {
            ReturnValue = rtCode,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnlbuttondblclk</c> - the port of <c>ondwnlbuttondblclk</c>
    /// (<c>se_cst_dw.sru:L136-L143</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position, in the DataWindow's own logical units.</param>
    /// <param name="ypos">The pointer y position, in the same units.</param>
    /// <param name="row">The ONE-BASED row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> when either edge prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// LIVENESS GUARD ONE OF THREE, at <c>:L138</c>. The semantic handler runs first and MAY DESTROY
    /// THE CONTROL; the broker edge is skipped when it did, and the report says so through
    /// <see cref="DataWindowDispatchReport.TargetBecameInvalid"/> rather than leaving the skip
    /// indistinguishable from an absent subscriber.
    /// </remarks>
    public virtual long OnDwnLButtonDblClk(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnlbuttondblclk);
        SubscriptionTopic? topic = TopicOf(EVT_DOUBLECLICKED);

        // :L136  if Event DoubleClicked(xpos,ypos,row,dwo) = 1 then return 1
        long semantic = DoubleClicked(xpos, ypos, row, dwo);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        long? broker = null;
        bool brokerRan = false;

        // :L138-L140  if IsValid(this) then
        //                 if Eventful.of_Trigger(EVT_DOUBLECLICKED,xpos,ypos,row,dwo) = 1 then return 1
        //             end if
        if (IsAlive)
        {
            broker = ToLegacyNumber(_eventful.Trigger(EVT_DOUBLECLICKED, xpos, ypos, row, dwo));
            brokerRan = true;

            if (broker == RetCode.PREVENT)
            {
                Report(outcome with
                {
                    ReturnValue = RetCode.PREVENT,
                    SemanticVeto = ToVetoResult(semantic),
                    BrokerVeto = ToVetoResult(broker),
                    Topic = topic,
                    Dispatch = new DataWindowDispatchReport
                    {
                        SemanticHandlerRan = true,
                        BrokerTriggerRan = true
                    }
                });

                return RetCode.PREVENT;
            }
        }

        // :L142  return 0
        Report(outcome with
        {
            ReturnValue = RetCode.OK,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = brokerRan,
                TargetBecameInvalid = !brokerRan
            }
        });

        return RetCode.OK;
    }

    /// <summary>
    /// <c>pbm_dwnlbuttonclk</c> - the port of <c>ondwnlbuttonclk</c>
    /// (<c>se_cst_dw.sru:L145-L162</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position, in the DataWindow's own logical units.</param>
    /// <param name="ypos">The pointer y position, in the same units.</param>
    /// <param name="row">The ONE-BASED row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns>
    /// <c>1</c> when either edge prevented, OR when the focus-free row switch was attempted and the
    /// cursor did not move; otherwise <c>0</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THREE STEPS, AND THE THIRD IS THE ONE NOTHING ELSE IN THE CHAIN HAS. The oracle labels it
    /// <c>//无焦点切换行</c> - "switch rows without focus" [<c>:L151</c>]. It runs only when the
    /// SECOND liveness guard AND <c>row &gt; 0</c> hold [<c>:L152</c>], only while the DataWindow
    /// reports itself processing [<c>:L153</c>], and only when the cursor is not already there
    /// [<c>:L154</c>].
    /// </para>
    /// <para>
    /// <b><c>SetRow</c> IS FALLIBLE AND ITS RETURN CODE IS NOT TRUSTED.</b> The oracle calls it and
    /// then RE-READS <c>GetRow()</c>, returning <c>1</c> when the row still differs
    /// [<c>:L155-L156</c>]. Reproducing the re-read rather than testing <c>SetRow</c>'s own code is
    /// the whole point: a successful return is not evidence that the cursor moved.
    /// </para>
    /// <para>
    /// <c>Describe("DataWindow.Processing")</c> is compared AS A STRING against <c>"1"</c>, exactly as
    /// the oracle compares it. Parsing it to a number would add a conversion the oracle does not
    /// perform and a failure mode it does not have.
    /// </para>
    /// </remarks>
    public virtual long OnDwnLButtonClk(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnlbuttonclk);
        SubscriptionTopic? topic = TopicOf(EVT_CLICKED);

        // :L145  if Event Clicked(xpos,ypos,row,dwo) = 1 then return 1
        long semantic = Clicked(xpos, ypos, row, dwo);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        long? broker = null;
        bool brokerRan = false;

        // :L147-L149  LIVENESS GUARD TWO OF THREE
        if (IsAlive)
        {
            broker = ToLegacyNumber(_eventful.Trigger(EVT_CLICKED, xpos, ypos, row, dwo));
            brokerRan = true;

            if (broker == RetCode.PREVENT)
            {
                Report(outcome with
                {
                    ReturnValue = RetCode.PREVENT,
                    SemanticVeto = ToVetoResult(semantic),
                    BrokerVeto = ToVetoResult(broker),
                    Topic = topic,
                    Dispatch = new DataWindowDispatchReport
                    {
                        SemanticHandlerRan = true,
                        BrokerTriggerRan = true
                    }
                });

                return RetCode.PREVENT;
            }
        }

        bool? rowSwitchMoved = null;

        // ------------------------------------------------------------------------------------------
        // :L151-L159  //无焦点切换行   "switch rows without focus"
        //
        //     :L152  if IsValid(this) and row > 0 then          LIVENESS GUARD THREE OF THREE
        //     :L153      if Describe("DataWindow.Processing") = "1" then
        //     :L154          if GetRow() <> row then
        //     :L155              SetRow(row)
        //     :L156              if GetRow() <> row then return 1
        // ------------------------------------------------------------------------------------------
        if (IsAlive && row > 0L)
        {
            if (string.Equals(Describe("DataWindow.Processing"), DataWindowProcessingActive, StringComparison.Ordinal))
            {
                if (GetRow() != row)
                {
                    // The return code is DELIBERATELY DISCARDED, exactly as :L155 discards it. The
                    // re-read on the next line is the oracle's own success test.
                    _ = SetRow(row);

                    rowSwitchMoved = GetRow() == row;

                    if (rowSwitchMoved != true)
                    {
                        Report(outcome with
                        {
                            ReturnValue = RetCode.PREVENT,
                            SemanticVeto = ToVetoResult(semantic),
                            BrokerVeto = ToVetoResult(broker),
                            Topic = topic,
                            RowSwitchAttempted = false,
                            Dispatch = new DataWindowDispatchReport
                            {
                                SemanticHandlerRan = true,
                                BrokerTriggerRan = brokerRan,
                                TargetBecameInvalid = !brokerRan
                            }
                        });

                        return RetCode.PREVENT;
                    }
                }
            }
        }

        // :L161  return 0
        Report(outcome with
        {
            ReturnValue = RetCode.OK,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            RowSwitchAttempted = rowSwitchMoved,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = brokerRan,
                TargetBecameInvalid = !brokerRan
            }
        });

        return RetCode.OK;
    }

    /// <summary>
    /// <c>pbm_dwnchanging</c> - the port of <c>ondwnchanging</c> (<c>se_cst_dw.sru:L164-L174</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row being edited.</param>
    /// <param name="dwo">The column being edited.</param>
    /// <param name="data">
    /// The edit text. NULLABLE, because the NESTED raise at <c>:L207</c> passes
    /// <c>String(dwo.Primary[row])</c> and a null buffer value renders as null - the oracle reassigns
    /// its own parameter there. Never collapsed to the empty string (AAP 0.4.5.4).
    /// </param>
    /// <returns><c>1</c> when either edge prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THREE STEPS IN STRICT ORDER [<c>:L164-L173</c>]: the semantic <c>EditChanged</c>; then the
    /// broker trigger BUT ONLY IF THE TOPIC HAS A SUBSCRIBER [<c>:L166</c>]; then, IF DROP-DOWN
    /// SEARCH IS ENABLED, that service's <c>OnEditChanged</c> [<c>:L169-L171</c>].
    /// </para>
    /// <para>
    /// <b>THE SUBSCRIBER PROBE IS THE ONLY ONE OF ITS KIND AMONG THE RAW HANDLERS</b> and it is
    /// observable, not an optimisation (AAP 0.8.5 forbids calling it one): without a subscriber the
    /// broker's triggering and triggered hooks do not fire and the topic's default return value is
    /// never resolved. Every other raw handler triggers unconditionally.
    /// </para>
    /// <para>
    /// The service hook is reached through <see cref="IDataWindowDropDownSearchService"/> and returns
    /// nothing, so it cannot veto - <c>:L170</c> is a bare statement over an event declared with no
    /// <c>type</c> clause.
    /// </para>
    /// </remarks>
    public virtual long OnDwnChanging(long row, IDataWindowObject dwo, string? data)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnchanging);
        SubscriptionTopic? topic = TopicOf(EVT_EDITCHANGED);

        // :L164  if Event EditChanged(row,dwo,data) = 1 then return 1
        //
        // `data!` rather than `data ?? ""`. The ancestry event declares the parameter non-nullable, so
        // the null-forgiving operator is what lets the RUNTIME VALUE pass through untransformed;
        // substituting the empty string would convert "no value" into "empty value", which
        // AAP 0.4.5.4 forbids.
        long semantic = EditChanged(row, dwo, data!);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        long? broker = null;
        bool brokerRan = false;

        // :L166-L168  if Eventful.of_IsSubscribed(EVT_EDITCHANGED) then
        //                 if Eventful.of_Trigger(EVT_EDITCHANGED,row,dwo,data) = 1 then return 1
        //             end if
        if (_eventful.IsSubscribed(EVT_EDITCHANGED))
        {
            broker = ToLegacyNumber(_eventful.Trigger(EVT_EDITCHANGED, row, dwo, data));
            brokerRan = true;

            if (broker == RetCode.PREVENT)
            {
                Report(outcome with
                {
                    ReturnValue = RetCode.PREVENT,
                    SemanticVeto = ToVetoResult(semantic),
                    BrokerVeto = ToVetoResult(broker),
                    Topic = topic,
                    Dispatch = new DataWindowDispatchReport
                    {
                        SemanticHandlerRan = true,
                        BrokerTriggerRan = true
                    }
                });

                return RetCode.PREVENT;
            }
        }

        // :L169-L171  if DropdownSearch.#Enabled then
        //                 DropdownSearch.Event OnEditChanged(row,dwo,data)
        //             end if
        //
        // The service's parameter is non-nullable for the same reason EditChanged's is, and the same
        // null-forgiving pass-through applies.
        if (DropDownSearch.Enabled)
        {
            DropDownSearch.OnEditChanged(row, dwo, data!);
        }

        // :L173  return 0
        Report(outcome with
        {
            ReturnValue = RetCode.OK,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = brokerRan
            }
        });

        return RetCode.OK;
    }

    /// <summary>
    /// <c>pbm_dwnitemchangefocus</c> - the port of <c>ondwnitemchangefocus</c>
    /// (<c>se_cst_dw.sru:L176-L180</c>).
    /// </summary>
    /// <param name="row">The ONE-BASED row now holding focus.</param>
    /// <param name="dwo">The column now holding focus.</param>
    /// <returns><c>1</c> when either edge prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// GATED ON <c>EID_ITEMFOCUSCHANGE</c> [<c>:L176</c>] - the second of the three gate bits, and the
    /// only handler that reads it. Both edges are tested.
    /// </remarks>
    public virtual long OnDwnItemChangeFocus(long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnitemchangefocus);
        SubscriptionTopic? topic = TopicOf(EVT_ITEMFOCUSCHANGED);

        // :L176  if BitTest(_nDisabledEvent,EID_ITEMFOCUSCHANGE) then return 0
        if (_session.IsEventDisabled(EventGate.EID_ITEMFOCUSCHANGE))
        {
            Report(outcome with
            {
                ReturnValue = RetCode.OK,
                Dispatch = new DataWindowDispatchReport { GatedOut = true }
            });

            return RetCode.OK;
        }

        // :L177  if Event ItemFocusChanged(row,dwo) = 1 then return 1
        long semantic = ItemFocusChanged(row, dwo);

        if (semantic == RetCode.PREVENT)
        {
            Report(outcome with
            {
                ReturnValue = RetCode.PREVENT,
                SemanticVeto = ToVetoResult(semantic),
                Topic = topic,
                Dispatch = new DataWindowDispatchReport { SemanticHandlerRan = true }
            });

            return RetCode.PREVENT;
        }

        // :L178  if Eventful.of_Trigger(EVT_ITEMFOCUSCHANGED,row,dwo) = 1 then return 1
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_ITEMFOCUSCHANGED, row, dwo));

        // :L179  return 0
        long rtCode = broker == RetCode.PREVENT ? RetCode.PREVENT : RetCode.OK;

        Report(outcome with
        {
            ReturnValue = rtCode,
            SemanticVeto = ToVetoResult(semantic),
            BrokerVeto = ToVetoResult(broker),
            Topic = topic,
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnitemchange</c> - the port of <c>ondwnitemchange</c>
    /// (<c>se_cst_dw.sru:L182-L254</c>), the most intricate event in the in-scope set.
    /// </summary>
    /// <param name="row">The ONE-BASED row whose item changed.</param>
    /// <param name="dwo">The column the change is addressed to.</param>
    /// <param name="data">The edit text the DataWindow is about to apply.</param>
    /// <returns>The four-value item-change alphabet, after the oracle's own rewrites.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> or <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>DELEGATED IN FULL TO <see cref="ItemChangeProtocol"/>, WHICH OWNS IT.</b> That file holds
    /// the whole <c>{0,1,2,3}</c> micro-protocol - the gate early-out, the value and status snapshots,
    /// the four-step re-entrancy dance, the equality test with its explicit null-and-null arm, the
    /// nested raise, the empty <c>case 1</c> that FALLS THROUGH to <c>case 2</c>, the <c>case 3</c>
    /// that rewrites its result to <c>1</c>, and the default arm that coerces by column-type prefix
    /// and then FORCIBLY RETURNS <c>2</c>. Reimplementing any of it here would create a second copy of
    /// the most subtle behaviour in the service.
    /// </para>
    /// <para>
    /// This chain supplies the three collaborators the protocol needs and nothing else: itself as the
    /// host, the session as the state, and itself as the event sink - which it can be because
    /// <see cref="IItemChangeEventSink"/>'s three members are all events of <c>se_cst_dw</c>.
    /// </para>
    /// <para>
    /// ORDERING: SYNCHRONOUS. It fires a NESTED event from inside itself [<c>:L207</c>] and it writes
    /// the stash the validation-error event later consumes [<c>:L195</c>], so nothing in its group may
    /// be reordered. The observer sees the nested dispatch FIRST and this one second, which is the
    /// true sequence.
    /// </para>
    /// </remarks>
    public virtual long OnDwnItemChange(long row, IDataWindowObject dwo, string data)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(data);

        DataWindowEventOutcome outcome =
            NewOutcome(EventId.Ondwnitemchange, withinItemChangeChain: true);

        // Read FOR THE REPORT ONLY. The gate DECISION belongs to ItemChangeProtocol, which performs it
        // at the top of the call below [:L187]; duplicating the decision here would create two places
        // for it to disagree. Reading the mask is side-effect free and nothing between here and the
        // call can change it.
        bool gatedOut = _session.IsEventDisabled(EventGate.EID_ITEMCHANGE);

        ItemChangeResult result =
            ItemChangeProtocol.OnDwnItemChange(this, _session, this, row, dwo, data);

        Report(outcome with
        {
            ReturnValue = (long)result,
            ItemChangeResult = result,
            State = _session.CaptureState(),
            Dispatch = new DataWindowDispatchReport
            {
                GatedOut = gatedOut,
                SemanticHandlerRan = !gatedOut
            }
        });

        return (long)result;
    }

    /// <summary>
    /// <c>pbm_dwnitemvalidationerror</c> - the port of <c>ondwnitemvalidationerror</c>
    /// (<c>se_cst_dw.sru:L322-L385</c>), the stash's CONSUMER.
    /// </summary>
    /// <param name="row">The ONE-BASED row whose entry was rejected.</param>
    /// <param name="dwo">The column whose entry was rejected.</param>
    /// <param name="data">The rejected text.</param>
    /// <returns>The four-value alphabet: <c>1</c>, <c>3</c>, or the handler's own code.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="dwo"/> or <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>DELEGATED IN FULL TO <see cref="ValidationSession"/>, WHICH OWNS IT</b> together with the
    /// four cross-event fields it reads and writes. That file holds the re-entrancy early-out that
    /// returns <c>1</c> WITHOUT clearing the guard [<c>:L327</c>], the consume-and-clear of the stash
    /// [<c>:L331-L332</c>], the pre-set when the stashed code was <c>1</c> or <c>3</c>
    /// [<c>:L338-L340</c>], the null-to-zero coercion of the handler's result [<c>:L344</c>], the
    /// validation-message read that STRIPS THE OUTER TWO CHARACTERS [<c>:L350-L353</c>], the localized
    /// fallback [<c>:L354-L356</c>], the ONE live dialog in the whole object [<c>:L357</c>] as a
    /// structured error, the empty-data path that clears the guard and RETURNS 3 [<c>:L360-L363</c>],
    /// and the restore that is suppressed when the row no longer exists [<c>:L369</c>].
    /// </para>
    /// <para>
    /// ORDERING: SYNCHRONOUS, and this is the event that makes reordering IMPOSSIBLE rather than
    /// merely undesirable - its result is a function of its predecessor's return value.
    /// </para>
    /// </remarks>
    public virtual long OnDwnItemValidationError(long row, IDataWindowObject dwo, string data)
    {
        ArgumentNullException.ThrowIfNull(dwo);
        ArgumentNullException.ThrowIfNull(data);

        DataWindowEventOutcome outcome =
            NewOutcome(EventId.Ondwnitemvalidationerror, withinItemChangeChain: true);

        ValidationErrorOutcome result = _session.OnDwnItemValidationError(this, row, dwo, data);

        Report(outcome with
        {
            ReturnValue = result.RawResult,
            ItemChangeResult = result.Result,
            Error = result.Error,
            State = result.State,
            Dispatch = new DataWindowDispatchReport
            {
                // The re-entrant early-out at :L327 runs no handler at all, which is a different fact
                // from a handler that ran and produced 1.
                SemanticHandlerRan = !result.ReEntered && result.ItemErrorRaised
            }
        });

        return result.RawResult;
    }

    /// <summary>
    /// <c>pbm_dwnkillfocus</c> - the port of <c>ondwnkillfocus</c> (<c>se_cst_dw.sru:L387-L393</c>).
    /// </summary>
    /// <returns>The semantic <c>LoseFocus</c> handler's code, VERBATIM.</returns>
    /// <remarks>
    /// <para>
    /// THREE STEPS, IN THIS ORDER, AND THE FIRST IS CONDITIONAL:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <c>:L388-L390</c> QUEUE THE DEFERRED ACCEPT, but ONLY WHILE THE ITEM-CHANGE FLAG IS CLEAR.
    ///   The oracle writes <c>Post _of_PostAcceptText()</c>, which hands the call to the Win32 message
    ///   queue; a headless container has no message pump, so it becomes an explicitly queued
    ///   continuation on the session (DECISION 6). The flag test lives inside
    ///   <see cref="ValidationSession.TryQueueDeferredAccept"/> next to the flag it reads, so this
    ///   handler does not duplicate it.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L391</c> trigger <see cref="EVT_LOSEFOCUS"/> WITH NO ARGUMENTS, and DO NOT TEST the
    ///   result - the veto is reported but cannot stop anything, exactly as at <c>:L126</c>.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L392</c> return the semantic handler's code VERBATIM. This is one of only two raw
    ///   handlers that propagate a semantic code instead of normalising it to <c>0</c> or <c>1</c>;
    ///   <see cref="OnDwnSetFocus"/> is the other.
    ///   </description></item>
    /// </list>
    /// <para>
    /// ORDERING: THIS EVENT BELONGS TO BOTH GROUPS AND THAT IS NOT A CONTRADICTION. As the tail of the
    /// item-change chain it is SYNCHRONOUS, because the deferred accept must not be queued while an
    /// item change is in flight; as pure focus notification it is SEQUENCED.
    /// </para>
    /// <para>
    /// <b>THIS OVERLOAD ALWAYS REPORTS THE SYNCHRONOUS ROLE, ON EVERY OCCURRENCE, AND THAT IS THE ONLY
    /// CHOICE THAT IS NEVER WRONG.</b> The queueing step at <c>:L388-L390</c> reads the session's
    /// item-change flag and may enqueue a continuation that will RE-ENTER the item-change protocol, so
    /// every occurrence touches the cross-event state at <c>:L89-L96</c> - the one that queues because
    /// it wrote to it, and the one that arrives mid-change because it read a set flag and declined. No
    /// occurrence is therefore safely reorderable. Reporting <see cref="OrderingDiscipline.Sequenced"/>
    /// for an occurrence that was in fact the tail would authorise a consumer to reorder a message whose
    /// predecessor's result it depends on; reporting Synchronous for one that was pure notification
    /// merely forbids a reorder that was never needed. The asymmetry of those two mistakes is the whole
    /// argument.
    /// </para>
    /// <para>
    /// The sequenced reading still exists, and it is what
    /// <see cref="DataWindowEventOrdering.DisciplineOf(EventId, bool)"/> answers WITHOUT the flag - for
    /// a caller classifying a bare event identifier with no session in hand, such as a boundary
    /// validating an inbound wire message it did not emit.
    /// </para>
    /// </remarks>
    public virtual long OnDwnKillFocus()
    {
        DataWindowEventOutcome outcome =
            NewOutcome(EventId.Ondwnkillfocus, withinItemChangeChain: true);

        // :L387  //*应用修改        "apply the modification"
        // :L388  if Not _bDoItemChange then
        // :L389      Post _of_PostAcceptText()
        // :L390  end if
        bool queued = _session.TryQueueDeferredAccept();

        // :L391  Eventful.of_Trigger(EVT_LOSEFOCUS)   - NO ARGUMENTS, RESULT DISCARDED
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_LOSEFOCUS));

        // :L392  return Event LoseFocus()
        long rtCode = LoseFocus();

        Report(outcome with
        {
            ReturnValue = rtCode,
            SemanticVeto = ToVetoResult(rtCode),
            BrokerVeto = ToVetoResult(broker),
            Topic = TopicOf(EVT_LOSEFOCUS),
            DeferredAcceptQueued = queued,
            State = _session.CaptureState(),
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnlbuttonup</c> - the port of <c>ondwnlbuttonup</c> (<c>se_cst_dw.sru:L395-L397</c>).
    /// </summary>
    /// <param name="xpos">The pointer x position, in the DataWindow's own logical units.</param>
    /// <param name="ypos">The pointer y position, in the same units.</param>
    /// <param name="row">The ONE-BASED row under the pointer, or <c>0</c> when none.</param>
    /// <param name="dwo">The object under the pointer.</param>
    /// <returns><c>1</c> when the broker prevented; otherwise <c>0</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dwo"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <b>BROKER EDGE ONLY - THE SECOND OF THE TWO SUCH HANDLERS, ALONGSIDE
    /// <see cref="OnDwnRButtonUp"/>.</b> The folder brief flagged only the right-button one;
    /// verification of the source found both. There is no <c>Event LButtonUp</c> on the ancestry, and
    /// the <see cref="EVT_LBUTTONUP"/> comment's mention of an <c>LButtonUp</c> signature describes
    /// what a SUBSCRIBER receives, not a semantic event that exists.
    /// </remarks>
    public virtual long OnDwnLButtonUp(long xpos, long ypos, long row, IDataWindowObject dwo)
    {
        ArgumentNullException.ThrowIfNull(dwo);

        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnlbuttonup);

        // :L395  if Eventful.of_Trigger(EVT_LBUTTONUP,xpos,ypos,row,dwo) = 1 then return 1
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_LBUTTONUP, xpos, ypos, row, dwo));

        // :L396  return 0
        long rtCode = broker == RetCode.PREVENT ? RetCode.PREVENT : RetCode.OK;

        Report(outcome with
        {
            ReturnValue = rtCode,
            BrokerVeto = ToVetoResult(broker),
            Topic = TopicOf(EVT_LBUTTONUP),
            Dispatch = new DataWindowDispatchReport { BrokerTriggerRan = true }
        });

        return rtCode;
    }

    /// <summary>
    /// <c>pbm_dwnsetfocus</c> - the port of <c>ondwnsetfocus</c> (<c>se_cst_dw.sru:L399-L401</c>).
    /// </summary>
    /// <returns>The semantic <c>GetFocus</c> handler's code, VERBATIM.</returns>
    /// <remarks>
    /// <para>
    /// THE BROKER COMES FIRST AND ITS RESULT IS NOT TESTED [<c>:L399</c>], then the semantic handler's
    /// code is returned verbatim [<c>:L400</c>]. Both facts are unusual among the thirteen and both
    /// are preserved.
    /// </para>
    /// <para>
    /// THE CROSSED NAMING IS THE ORACLE'S: the <c>setfocus</c> raw event triggers the
    /// <see cref="EVT_GETFOCUS"/> topic and raises the <c>GetFocus</c> semantic event. Not to be
    /// confused with <see cref="DataWindowServiceHost.GetFocusedObject"/>, which is the PowerScript
    /// SYSTEM function <c>GetFocus()</c> used at <c>:L553</c> and renamed there precisely because C#
    /// cannot hold two members differing only in return type.
    /// </para>
    /// </remarks>
    public virtual long OnDwnSetFocus()
    {
        DataWindowEventOutcome outcome = NewOutcome(EventId.Ondwnsetfocus);

        // :L399  Eventful.of_Trigger(EVT_GETFOCUS)   - NO ARGUMENTS, RESULT DISCARDED
        long? broker = ToLegacyNumber(_eventful.Trigger(EVT_GETFOCUS));

        // :L400  return Event GetFocus()
        long rtCode = GetFocus();

        Report(outcome with
        {
            ReturnValue = rtCode,
            SemanticVeto = ToVetoResult(rtCode),
            BrokerVeto = ToVetoResult(broker),
            Topic = TopicOf(EVT_GETFOCUS),
            Dispatch = new DataWindowDispatchReport
            {
                SemanticHandlerRan = true,
                BrokerTriggerRan = true
            }
        });

        return rtCode;
    }

    // ==============================================================================================
    //  THE TWO DATAWINDOW OVERRIDES                                        se_cst_dw.sru:L403-L446
    //  --------------------------------------------------------------------------------------------
    //  BOTH RETURN AN INTEGER WHERE SUCCESS IS 1, NOT 0, and neither is mapped onto RetCode whose OK
    //  is 0 - conflating the two inverts every success test in both bodies. Both reach the built-in
    //  behaviour through `base.`, which is what DataWindowServiceHost's template-method seam over
    //  FilterCore and DeleteRowCore exists for (its DECISION 5).
    // ==============================================================================================

    /// <summary>
    /// Applies the current filter - the port of the <c>filter</c> override
    /// (<c>se_cst_dw.sru:L403-L414</c>).
    /// </summary>
    /// <returns>
    /// The base implementation's code, PROPAGATED VERBATIM. <c>1</c> INDICATES SUCCESS.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The whole body is: call the base [<c>:L406</c>], and ONLY IF IT RETURNED <c>1</c> [<c>:L407</c>]
    /// notify the row-selection service when that service is enabled [<c>:L408-L410</c>]; then return
    /// the base's code unchanged [<c>:L413</c>]. The notification's own return value does not exist -
    /// the event is declared with no <c>type</c> clause - so nothing about it can alter the result.
    /// </para>
    /// <para>
    /// The service is reached through <see cref="IDataWindowRowSelectService"/> and not through a
    /// reference to Services/, which consumes this file.
    /// </para>
    /// </remarks>
    public override int Filter()
    {
        // :L406  rtCode = super::Filter()
        int rtCode = base.Filter();

        // :L407  if rtCode = 1 then
        if (rtCode == DataWindowSuccess)
        {
            // :L408-L410  if RowSelect.#Enabled then RowSelect.Event OnFiltered()
            if (RowSelect.Enabled)
            {
                RowSelect.OnFiltered();
            }
        }

        // :L413  return rtCode
        return rtCode;
    }

    /// <summary>
    /// Deletes one row - the port of the <c>deleterow</c> override (<c>se_cst_dw.sru:L416-L446</c>).
    /// </summary>
    /// <param name="row">
    /// The ONE-BASED row to delete. <b><c>0</c> IS LEGAL AND MEANS "THE CURRENT ROW"</b>
    /// [<c>:L424-L426</c>] - it is not an out-of-range value and must not be rejected.
    /// </param>
    /// <returns>
    /// <see cref="DeleteRowOutOfRange"/> for an out-of-range request; otherwise the base
    /// implementation's code, PROPAGATED VERBATIM, where <c>1</c> INDICATES SUCCESS.
    /// </returns>
    /// <remarks>
    /// <para>
    /// SEVEN STEPS, IN THIS EXACT ORDER, EVERY ONE OF THEM CONTRACT:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///   <c>:L421</c> <c>if r &lt; 0 or r &gt; RowCount() then return -1</c>. Note the asymmetry:
    ///   negative is rejected, ZERO IS NOT.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L423-L426</c> resolve: <c>nRow = r</c>, and when that is <c>0</c>, <c>nRow = GetRow()</c>.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L428-L429</c> SNAPSHOT TWO BOOLEANS BEFORE THE DELETE - whether the row is the current one
    ///   and whether it is the last one. Both readings become wrong the instant the row goes, which is
    ///   why they are taken first.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L431-L432</c> call the base with the RESOLVED row, and bail on anything but <c>1</c>.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L434-L438</c> notify: when the DataWindow is now EMPTY raise the row-change event with row
    ///   <c>0</c>; ELSE IF the deleted row was the current one AND <c>GetRow()</c> RE-READ AFTER THE
    ///   DELETE still equals it, raise it with that row. The re-read is not redundant - the runtime may
    ///   have moved the cursor.
    ///   </description></item>
    ///   <item><description>
    ///   <c>:L441-L443</c> when the LAST row went and rows remain, <c>SetRedraw(true)</c>. The oracle's
    ///   comment is <c>//使DETAIL区的颜色刷新..</c> - "refresh the DETAIL band's colour".
    ///   </description></item>
    ///   <item><description><c>:L445</c> return the base's code.</description></item>
    /// </list>
    /// <para>
    /// <b>THE <c>SetRedraw</c> CALL IS A DELIBERATE BOUNDARY JUDGEMENT AND IT DOES NOT BREACH
    /// CONSTRAINT C-D.</b> The comment mentions colour, which makes it look presentational, but the
    /// call itself takes NO GEOMETRY, NO DPI VALUE, NO FONT METRIC AND NO WINDOW HANDLE - it is a
    /// single boolean on the host contract, which is why
    /// <see cref="DataWindowServiceHost.SetRedraw(bool)"/> exists there at all. Dropping it would
    /// remove an observable call the oracle makes; implementing the colour refresh would be
    /// DesignSystem work. Making the call and letting the host decide what it means is the only option
    /// that is neither.
    /// </para>
    /// </remarks>
    public override int DeleteRow(long row)
    {
        // :L421  if r < 0 or r > RowCount() then return -1
        if (row < 0L || row > RowCount())
        {
            return DeleteRowOutOfRange;
        }

        // :L423-L426  nRow = r; if nRow = 0 then nRow = GetRow()
        long resolvedRow = row;

        if (resolvedRow == 0L)
        {
            resolvedRow = GetRow();
        }

        // :L428-L429  SNAPSHOTS TAKEN BEFORE THE DELETE
        bool isCurrentRow = resolvedRow == GetRow();
        bool isLastRow = resolvedRow == RowCount();

        // :L431  rtCode = super::DeleteRow(nRow)   - the RESOLVED row, not the argument
        int rtCode = base.DeleteRow(resolvedRow);

        // :L432  if rtCode <> 1 then return rtCode
        if (rtCode != DataWindowSuccess)
        {
            return rtCode;
        }

        // :L434-L438  if RowCount() = 0 then Event OnDwnRowChange(0)
        //             elseif bIsCurrentRow and nRow = GetRow() then Event OnDwnRowChange(nRow)
        if (RowCount() == 0L)
        {
            // Row 0 is the oracle's "there is no current row" notification, not a sentinel error.
            _ = OnDwnRowChange(0L);
        }
        else if (isCurrentRow && resolvedRow == GetRow())
        {
            _ = OnDwnRowChange(resolvedRow);
        }

        // :L440-L443  //使DETAIL区的颜色刷新..    "refresh the DETAIL band's colour"
        //             if bIsLastRow and RowCount() > 0 then SetRedraw(true)
        if (isLastRow && RowCount() > 0L)
        {
            _ = SetRedraw(true);
        }

        // :L445  return rtCode
        return rtCode;
    }

    // ==============================================================================================
    //  THE SEVEN SUBSCRIPTION PASS-THROUGHS                                se_cst_dw.sru:L448-L467
    //  --------------------------------------------------------------------------------------------
    //  PURE PASS-THROUGHS. Every one of the seven is a single-line delegation in the oracle, with NO
    //  suffix, NO transformation and NO validation of its own - the broker owns all three. They map
    //  onto EventBroker.Subscribe and EventBroker.Unsubscribe, and NOT onto Disable: the oracle calls
    //  of_Off, and Disable is a different operation on the same registry.
    //
    //  THE NEGATIVE CONSTRAINT LIVES HERE. These seven are exactly where a contributor would be
    //  tempted to append `.^persistent` after reading Persistence's threading layer. Do not. See THE
    //  `.^persistent` NON-APPEND in the file header for the six locators that prove the suffix is not
    //  this chain's, and note that se_cst_dw.sru contains no `^` character at all.
    //
    //  `readonly` PARAMETERS BECOME `in` (AAP 0.4.5.2) and `powerobject` becomes `object?` - nullable,
    //  because a PowerBuilder object reference can be null and EventBroker's own overloads accept it.
    //
    //  ONE CALL-SITE HAZARD, RECORDED RATHER THAN DESIGNED AWAY. Off(name, obj) and Off(obj, evtName)
    //  are unambiguous in PowerScript, where `string` and `powerobject` are unrelated types, but in C#
    //  a string IS an object - so a caller passing TWO STRINGS is ambiguous (CS0121) and must type the
    //  target as `object` to choose. EventBroker's seven Unsubscribe overloads carry the identical
    //  shape for the identical reason, so this is a precedented consequence of the type mapping rather
    //  than a defect introduced here; collapsing an arity to avoid it would drop a legacy overload.
    // ==============================================================================================

    /// <summary>
    /// Subscribes a handler to a topic - the port of <c>of_on(name, obj, evtname)</c>
    /// (<c>se_cst_dw.sru:L448</c>).
    /// </summary>
    /// <param name="name">
    /// The topic, normally one of the twelve <c>EVT_*</c> constants, optionally prefixed with the
    /// broker's own ordering, capture or priority symbols - the one legacy subscription that uses one
    /// is <c>"!" + EVT_ITEMCHANGED</c> [<c>n_cst_dwsvc_columnexp.sru:L2429</c>]. CASE-SENSITIVE.
    /// </param>
    /// <param name="obj">The object owning the handler.</param>
    /// <param name="evtName">The handler member's name. Case-INSENSITIVE in the broker.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long On(in string name, in object? obj, in string evtName)
    {
        // :L448  return Eventful.of_On(name,obj,evtName)
        return _eventful.Subscribe(name, obj, evtName);
    }

    /// <summary>
    /// Unsubscribes one handler of one object from one topic - the port of
    /// <c>of_off(name, obj, evtname)</c> (<c>se_cst_dw.sru:L451</c>).
    /// </summary>
    /// <param name="name">The topic.</param>
    /// <param name="obj">The subscribing object.</param>
    /// <param name="evtName">The handler member's name.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long Off(in string name, in object? obj, in string evtName)
    {
        // :L451  return Eventful.of_Off(name,obj,evtName)
        return _eventful.Unsubscribe(name, obj, evtName);
    }

    /// <summary>
    /// Unsubscribes every handler of one object from one topic - the port of
    /// <c>of_off(name, obj)</c> (<c>se_cst_dw.sru:L454</c>).
    /// </summary>
    /// <param name="name">The topic.</param>
    /// <param name="obj">The subscribing object.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long Off(in string name, in object? obj)
    {
        // :L454  return Eventful.of_Off(name,obj)
        return _eventful.Unsubscribe(name, obj);
    }

    /// <summary>
    /// Unsubscribes every subscriber of one topic - the port of <c>of_off(name)</c>
    /// (<c>se_cst_dw.sru:L457</c>).
    /// </summary>
    /// <param name="name">The topic.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long Off(in string name)
    {
        // :L457  return Eventful.of_Off(name)
        return _eventful.Unsubscribe(name);
    }

    /// <summary>
    /// Unsubscribes EVERYTHING - the port of <c>of_off()</c> (<c>se_cst_dw.sru:L460</c>).
    /// </summary>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    /// <remarks>
    /// This is the overload <see cref="Teardown"/> calls FIRST, before destroying any service
    /// [<c>:L583</c>]. Unsubscribing before destroying is what prevents a dispatch reaching a
    /// destroyed service.
    /// </remarks>
    public long Off()
    {
        // :L460  return Eventful.of_Off()
        return _eventful.Unsubscribe();
    }

    /// <summary>
    /// Unsubscribes one object from every topic - the port of <c>of_off(obj)</c>
    /// (<c>se_cst_dw.sru:L463</c>).
    /// </summary>
    /// <param name="obj">The subscribing object.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long Off(in object? obj)
    {
        // :L463  return Eventful.of_Off(obj)
        return _eventful.Unsubscribe(obj);
    }

    /// <summary>
    /// Unsubscribes one handler of one object from every topic - the port of
    /// <c>of_off(obj, evtname)</c> (<c>se_cst_dw.sru:L466</c>).
    /// </summary>
    /// <param name="obj">The subscribing object.</param>
    /// <param name="evtName">The handler member's name.</param>
    /// <returns>The broker's return code, propagated verbatim.</returns>
    public long Off(in object? obj, in string evtName)
    {
        // :L466  return Eventful.of_Off(obj,evtName)
        return _eventful.Unsubscribe(obj, evtName);
    }

    // ==============================================================================================
    //  THE THREE HOST-FACING EVENT-GATE DELEGATIONS                        se_cst_dw.sru:L469-L535
    //  --------------------------------------------------------------------------------------------
    //  THESE ARE se_cst_dw's OWN PUBLIC FUNCTIONS AND THEY ARE NOT OPTIONAL. The MECHANISM belongs to
    //  Domain/EventGate.cs and the VALUE to Domain/ValidationSession.cs; what lives here is the
    //  host-facing route the oracle's own consumers take, and without it the column-sort service has
    //  no way to reach the gate through its host:
    //
    //      n_cst_dwsvc_columnsort.sru:L409  if Not #DataWindow.of_IsEventDisabled(#DataWindow.EID_ROWFOCUSCHANGE) then
    //      n_cst_dwsvc_columnsort.sru:L411      #DataWindow.of_DisableEvent(#DataWindow.EID_ROWFOCUSCHANGE)
    //      n_cst_dwsvc_columnsort.sru:L425      #DataWindow.of_EnableEvent(#DataWindow.EID_ROWFOCUSCHANGE)
    //
    //  THE EID_* CONSTANTS ARE DELIBERATELY NOT REDECLARED HERE. EventGate owns that vocabulary, and
    //  a second copy of a constant catalogue is exactly what the repository-root .editorconfig's
    //  BAND 3 discipline exists to avoid - a file that merely CONSUMES a preserved constant needs no
    //  entry of its own. Consumers write EventGate.EID_ROWFOCUSCHANGE.
    // ==============================================================================================

    /// <summary>
    /// Whether the given event bits are disabled - the port of <c>of_iseventdisabled</c>
    /// (<c>se_cst_dw.sru:L469-L487</c>, body at <c>:L486</c>).
    /// </summary>
    /// <param name="evt">
    /// One or a combination of <c>EventGate.EID_*</c>. The oracle's own comment says combinations are
    /// supported [<c>:L40</c>].
    /// </param>
    /// <returns><see langword="true"/> when any tested bit is set in the session's mask.</returns>
    public bool IsEventDisabled(in uint evt)
    {
        // :L486  return BitTest(_nDisabledEvent,evt)
        return _session.IsEventDisabled(evt);
    }

    /// <summary>
    /// Disables the given event bits - the port of <c>of_disableevent</c>
    /// (<c>se_cst_dw.sru:L489-L511</c>).
    /// </summary>
    /// <param name="evt">One or a combination of <c>EventGate.EID_*</c>.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// <paramref name="evt"/> is zero [<c>:L506</c>] - in which case the mask is left provably
    /// untouched.
    /// </returns>
    /// <remarks>
    /// DISABLING <c>EID_ITEMCHANGE</c> ALSO STOPS EVERY BOUND COLUMN EXPRESSION FROM RECALCULATING,
    /// silently and with no error anywhere. The oracle warns of it at <c>:L43</c>; the mechanism is the
    /// guard at <c>:L187</c> short-circuiting the only path that reaches
    /// <see cref="OnDoItemChanged(long, IDataWindowObject)"/>'s first step. Preserved (constraint C-B).
    /// </remarks>
    public long DisableEvent(in uint evt)
    {
        // :L506-L510  the zero guard, the in-place BitOR, and RetCode.OK
        return _session.DisableEvent(evt);
    }

    /// <summary>
    /// Re-enables the given event bits - the port of <c>of_enableevent</c>
    /// (<c>se_cst_dw.sru:L513-L535</c>).
    /// </summary>
    /// <param name="evt">One or a combination of <c>EventGate.EID_*</c>.</param>
    /// <returns>
    /// <see cref="RetCode.OK"/>, or <see cref="RetCode.E_INVALID_ARGUMENT"/> when
    /// <paramref name="evt"/> is zero [<c>:L530</c>].
    /// </returns>
    /// <remarks>
    /// RETURNS <see langword="int"/> WHERE ITS SIBLING RETURNS <see langword="long"/>, because the
    /// oracle declares it <c>public function integer of_enableevent</c> [<c>:L513</c>] while
    /// <c>of_disableevent</c> is declared <c>long</c> [<c>:L489</c>] - and its own comment block still
    /// says <c>Returns: long</c> [<c>:L521</c>]. The inconsistency is the oracle's and is preserved
    /// rather than tidied (constraint C-B); both values are the same return-code algebra.
    /// </remarks>
    public int EnableEvent(in uint evt)
    {
        // :L530-L534  the zero guard, the in-place BitClear, and RetCode.OK
        return _session.EnableEvent(evt);
    }

    /// <summary>
    /// Runs the queued deferred accept-text continuation, if one is pending - the drain half of
    /// <c>Post _of_PostAcceptText()</c> (<c>se_cst_dw.sru:L389</c>, body at <c>:L537-L558</c>).
    /// </summary>
    /// <returns>
    /// What the continuation observed, or <see langword="null"/> when nothing was queued or the session
    /// is closed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DECISION 6 in the file header. The oracle relies on the WIN32 MESSAGE PUMP turning; a headless
    /// Linux container has none, so the pump becomes an explicit call the host makes at a point of its
    /// own choosing. That is the same treatment <see cref="EventBroker.DrainPostedContinuations"/> gives
    /// the broker's own posted dispatches, and this chain deliberately introduces NO SECOND MECHANISM -
    /// the queue is the session's, so the continuation and the four cross-event fields it reads stay in
    /// one place.
    /// </para>
    /// <para>
    /// The continuation's body is <c>:L553-L557</c>: <c>if GetFocus() &lt;&gt; this then</c> - an
    /// IDENTITY COMPARISON against the host rather than a focus test - <c>if AcceptText() = -1 then
    /// SetFocus()</c>. Domain/ValidationSession.cs owns it.
    /// </para>
    /// </remarks>
    internal DeferredAcceptOutcome? DrainDeferredAccept()
    {
        return _session.DrainDeferredAccept(this);
    }

    /// <summary>
    /// Tears the chain down - the port of <c>ondestructor</c> (<c>se_cst_dw.sru:L583-L589</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE ORDER IS THE POINT.</b> <c>Eventful.of_Off()</c> runs FIRST [<c>:L583</c>], and only then
    /// are the five services destroyed [<c>:L584-L588</c>] in the CREATION order - ContextMenu,
    /// RowSelect, ColumnSort, DropDownSearch, ColumnExp - not the initialisation order. Unsubscribing
    /// before destroying is what prevents a dispatch reaching a service that no longer exists; all five
    /// subscribe themselves during initialisation, so the reverse order would leave a live subscription
    /// pointing at a destroyed object.
    /// </para>
    /// <para>
    /// <b>THE DROPPED SUPER-CALL.</b> <c>:L583</c> opens with <c>call super::ondestructor</c>, which in
    /// the legacy runs se_cst_datawindow's <c>ThemeManager().of_UnregisterControl(this)</c>
    /// [<c>ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L89-L90</c>]. ThemeManager
    /// belongs to the deferred DesignSystem service, so under constraint C-D that call has NO ANALOGUE
    /// AND IS DROPPED - a DOCUMENTED CAPABILITY GAP belonging to the reserved <c>/v1/design/**</c>
    /// Gateway extension point (AAP 0.4.4), and emphatically not something to stub. It is the exact
    /// counterpart of the drop at the constructor, and both are recorded so a reader cannot mistake
    /// either for an omission. See DECISION 5.
    /// </para>
    /// <para>
    /// <b>NOT NAMED <c>Dispose</c>, DELIBERATELY.</b> PowerBuilder's <c>Destroy</c> is an explicit
    /// lifecycle call with nothing to do with unmanaged resources; naming this <c>Dispose</c> would
    /// invite <c>using</c> blocks and finalizer semantics the oracle has no analogue for, and would make
    /// the chain's own teardown indistinguishable from resource cleanup a derived adapter adds for
    /// itself. A service that DOES hold a resource is still disposed here, through the
    /// <see cref="IDisposable"/> test below - which is why
    /// <see cref="IDataWindowAttachedService"/> does not require the interface.
    /// </para>
    /// <para>
    /// IDEMPOTENT. A second call does nothing. PowerBuilder would fault on a second <c>Destroy</c> of
    /// the same reference, but that fault is unreachable from a faithful port - nothing in the oracle
    /// destroys a control twice - so idempotence removes a failure mode without removing a behaviour.
    /// </para>
    /// </remarks>
    public void Teardown()
    {
        if (_tornDown)
        {
            return;
        }

        // Set BEFORE anything else, so the three liveness guards see a host whose destruction has
        // begun. A dispatch already in flight then stops touching it, which is exactly what
        // `IsValid(this)` produces for a caller whose handler destroyed the control.
        _tornDown = true;

        // :L583  Eventful.of_Off()   - FIRST, before any destroy
        _ = Off();

        // :L584-L588  Destroy in CREATION order
        DestroyService(ContextMenu);     // :L584  Destroy ContextMenu
        DestroyService(RowSelect);       // :L585  Destroy RowSelect
        DestroyService(ColumnSort);      // :L586  Destroy ColumnSort
        DestroyService(DropDownSearch);  // :L587  Destroy DropDownSearch
        DestroyService(ColumnExp);       // :L588  Destroy ColumnExp
    }

    private static void DestroyService(IDataWindowAttachedService service)
    {
        // PowerBuilder's `Destroy` runs the object's destructor script and then invalidates the
        // reference. A service whose legacy counterpart has no destructor body has nothing to run, and
        // the managed equivalent of "run the destructor" for the one that does -
        // n_cst_dwsvc_columnexp's `Destroy _vecCalcStack` [n_cst_dwsvc_columnexp.sru:L2425] - is
        // IDisposable. Testing for the interface rather than requiring it keeps the requirement off
        // every service that does not need it.
        if (service is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // ==============================================================================================
    //  THE NESTED BROKER SUBCLASS                                          se_cst_dw.sru:L591-L615
    //  --------------------------------------------------------------------------------------------
    //  :L591 declares `type eventful from n_cst_eventful within se_cst_dw` - a NESTED SUBCLASS of the
    //  broker, held as an instance member [:L33] and created and destroyed with the host [:L562,
    //  :L567]. Nested here for the same reason: the subclass reads three members of its enclosing
    //  instance, which is what PowerBuilder's implicit `parent` gives it and what the constructor
    //  argument gives it here.
    //
    //  THE SUBCLASSING IS NOT DECORATIVE. EventBroker's four hooks - OnPrepare, OnTriggering,
    //  OnTriggered and OnException - fire ONLY WHEN THE RUNTIME TYPE IS DERIVED, so the whole
    //  argument-injection behaviour below depends on this type existing. Only OnPrepare is overridden,
    //  because that is the only one the oracle overrides.
    // ==============================================================================================

    /// <summary>
    /// The chain's own event broker - the port of the nested
    /// <c>type eventful from n_cst_eventful within se_cst_dw</c> (<c>se_cst_dw.sru:L591-L615</c>).
    /// </summary>
    /// <remarks>
    /// Sealed. The oracle has no further derivation of it, and the enclosing chain hands
    /// <see cref="EventBroker"/>-typed access out through
    /// <see cref="DataWindowEventChain.Eventful"/>, so a subclass could only weaken the injection
    /// contract the five attached services depend on.
    /// </remarks>
    internal sealed class DataWindowEventBroker : EventBroker
    {
        private readonly DataWindowEventChain _parent;

        /// <summary>
        /// Reproduces the nested type's <c>constructor</c> event (<c>se_cst_dw.sru:L594-L597</c>).
        /// </summary>
        /// <param name="parent">
        /// The enclosing chain - PowerBuilder's implicit <c>parent</c>, which
        /// <c>onprepare</c> reads at <c>:L600</c> and injects at <c>:L604</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="parent"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The body is two statements: <c>call super::constructor</c> [<c>:L594</c>], which the base
        /// constructor is, and <c>of_SetDefaultReturnValue(0)</c> [<c>:L596</c>], which sets the value
        /// every dispatch resolves to when no handler produced one.
        /// </para>
        /// <para>
        /// THAT ZERO IS LOAD-BEARING FOR THE WHOLE CHAIN. Every <c>= 1</c> prevent test above reads a
        /// dispatch result, and a subscriber-less topic must therefore resolve to something that is
        /// NOT <c>1</c>. Zero is the continue value of the same convention.
        /// </para>
        /// <para>
        /// A DORMANT ALTERNATIVE IS CARRIED INERT (constraint C-B). The oracle's own lines
        /// <c>:L594-L595</c> are:
        /// </para>
        /// <code>
        /// //long nvl
        /// //SetNull(nvl)
        /// </code>
        /// <para>
        /// which would have declared a null long and set the default return value to NULL instead of
        /// <c>0</c>. It is commented out in the oracle, so THE LEGACY DOES NOT DO IT, and reviving it
        /// would change every subscriber-less dispatch result from a number into a null - which the
        /// numeric coercion above reports as "not a prevention" either way, but which a
        /// characterization recording would compare differently. Recorded, never revived.
        /// </para>
        /// </remarks>
        internal DataWindowEventBroker(DataWindowEventChain parent)
        {
            ArgumentNullException.ThrowIfNull(parent);

            _parent = parent;

            // :L594  //long nvl
            // :L595  //SetNull(nvl)
            //        The dormant null-default alternative. Inert in the oracle and inert here.
            // :L596  of_SetDefaultReturnValue(0)
            //        The oracle discards the return code; so does this.
            _ = SetDefaultReturnValue(0L);
        }

        /// <summary>
        /// Injects the parent DataWindow as the leading argument of every APPLICATION handler - the
        /// port of the nested type's <c>onprepare</c> event (<c>se_cst_dw.sru:L599-L607</c>).
        /// </summary>
        /// <param name="name">The topic being dispatched.</param>
        /// <param name="target">The subscription's callback target.</param>
        /// <param name="arguments">
        /// The argument buffer, addressed ONE-BASED. <see cref="EventArgumentContext"/> is the
        /// invoker-free substitute for the legacy <c>n_scriptinvoker</c> parameter: AAP 0.2.1.4
        /// establishes that <c>n_scriptinvoker</c> is not a real dependency but a VARIADIC-CALL ESCAPE
        /// HATCH for a language that cannot forward an arbitrary argument list, and C# forwards
        /// natively, so the workaround has no analogue to port. Only its two real capabilities survive
        /// - leading-argument injection and consumed-count reporting - and both are on this type.
        /// </param>
        /// <returns>
        /// <see cref="RetCode.OK"/> ON EVERY PATH. The oracle returns <c>0</c> from all six of its
        /// exits, so this hook never skips a subscriber - which matters because
        /// <see cref="RetCode.PREVENT"/> here would mean "do not invoke this handler at all".
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> or <paramref name="arguments"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// <para>
        /// SIX STEPS IN THIS EXACT ORDER, AND THE ORDER IS OBSERVABLE:
        /// </para>
        /// <list type="number">
        ///   <item><description>
        ///   <c>:L599</c> <c>call super::onprepare</c>, then <c>if argCount &lt; 1 then return 0</c> -
        ///   a handler that declares no parameters has no slot to inject into.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L600</c> <c>if target = parent then return 0</c> - the host does not need to be told
        ///   which host raised the event.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L601</c> <c>if target = ColumnExp then return 0</c>.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L602</c> <c>if target = DropDownSearch then return 0</c>.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L603</c> <c>if IsAncestor(target,"n_cst_dwsvc") then return 0</c> - every attached
        ///   service is excluded, because their handlers are declared without a leading source
        ///   parameter. All five subscribe THEMSELVES to this broker
        ///   [<c>n_cst_dwsvc_contextmenu.sru:L1441-L1442</c>,
        ///   <c>n_cst_dwsvc_rowselect.sru:L274-L276</c>,
        ///   <c>n_cst_dwsvc_columnsort.sru:L442-L444</c>,
        ///   <c>n_cst_dwsvc_dropdownsearch.sru:L508-L510</c>,
        ///   <c>n_cst_dwsvc_columnexp.sru:L2429</c>], so this arm is heavily exercised rather than
        ///   theoretical.
        ///   </description></item>
        ///   <item><description>
        ///   <c>:L604-L606</c> <c>invoker.SetArg(1,parent)</c> then <c>argPassed = 1</c> then
        ///   <c>return 0</c>.
        ///   </description></item>
        /// </list>
        /// <para>
        /// <b>CHECKS THREE AND FOUR ARE REDUNDANT WITH CHECK FIVE, AND THE REDUNDANCY IS PRESERVED
        /// ON PURPOSE.</b> Both named services are <c>n_cst_dwsvc</c> descendants, so <c>:L603</c>
        /// would already exclude them - but it is reached SECOND, and the two operations are not the
        /// same operation: <c>:L601</c> and <c>:L602</c> are IDENTITY comparisons against two
        /// particular instances, while <c>:L603</c> is a name-based ancestry WALK. They could diverge
        /// - a service supplied through
        /// <see cref="IDataWindowColumnExpressionService"/> that did not derive from
        /// <see cref="DataWindowServiceBase"/> would be caught by the identity check and missed by the
        /// walk - so removing either would be a behaviour change (constraint C-B). This is
        /// intentionally preserved redundancy, not dead code.
        /// </para>
        /// <para>
        /// THE ANCESTRY WALK IS SELF-INCLUSIVE, which is the legacy semantics
        /// <see cref="Ancestry.IsAncestor(object?, string)"/> reproduces: a type is its own ancestor,
        /// so <see cref="DataWindowServiceBase"/> itself is excluded, and an empty class name answers
        /// <see langword="false"/> rather than throwing.
        /// </para>
        /// <para>
        /// ONE-BASED SLOT NUMBERING IS PRESERVED, NOT "CORRECTED". <c>SetArg(1, parent)</c> becomes
        /// <see cref="EventArgumentContext.SetArgument(int, object?)"/> with the literal <c>1</c>, and
        /// the consumed count is likewise <c>1</c>. AAP 0.4.5.4 names one-based-to-zero-based
        /// translation the single most dangerous mechanical hazard in this refactor, and
        /// <see cref="EventArgumentContext"/> performs the conversion once, internally, so no call
        /// site has to.
        /// </para>
        /// </remarks>
        protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(arguments);

            // :L599  call super::onprepare
            //
            // The oracle DISCARDS the ancestor's value - PowerBuilder's `call super::event` runs the
            // ancestor script and the caller keeps its own return - so it is discarded here too. It is
            // still called, because dropping the call would be a different program: a future base
            // implementation with a body would silently stop running.
            _ = base.OnPrepare(name, target, arguments);

            // :L599  if argCount < 1 then return 0
            if (arguments.DeclaredArgumentCount < 1)
            {
                return RetCode.OK;
            }

            // :L600  if target = parent then return 0
            if (ReferenceEquals(target, _parent))
            {
                return RetCode.OK;
            }

            // :L601  if target = ColumnExp then return 0          REDUNDANT WITH :L603, PRESERVED
            if (ReferenceEquals(target, _parent.ColumnExp))
            {
                return RetCode.OK;
            }

            // :L602  if target = DropDownSearch then return 0     REDUNDANT WITH :L603, PRESERVED
            if (ReferenceEquals(target, _parent.DropDownSearch))
            {
                return RetCode.OK;
            }

            // :L603  if IsAncestor(target,"n_cst_dwsvc") then return 0
            if (Ancestry.IsAncestor(target, AttachedServiceAncestorClassName))
            {
                return RetCode.OK;
            }

            // :L604  invoker.SetArg(1,parent)     ONE-BASED SLOT
            //
            // The oracle discards this return code as well. A rejection is impossible here because the
            // declared count was checked at :L599, and swallowing it silently would be worse than
            // discarding it deliberately - so the discard is explicit.
            _ = arguments.SetArgument(1, _parent);

            // :L605  argPassed = 1
            arguments.ConsumedArgumentCount = 1;

            // :L606  return 0
            return RetCode.OK;
        }
    }
}
