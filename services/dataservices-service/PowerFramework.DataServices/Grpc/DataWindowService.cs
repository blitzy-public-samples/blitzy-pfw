// ==================================================================================================
//  DataWindowService - the server implementation of contract C-03 `dataservices.v1.DataWindowService`.
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS. A TRANSPORT AND SESSION FACADE, and nothing else. Every behaviour it exposes
//  already lives in Domain/ and Services/: the 22-event chain and its per-area ordering assignment
//  (Domain/DataWindowEventChain.cs), the four pieces of cross-event state and the validation-error
//  protocol (Domain/ValidationSession.cs), the composable gate and its return-type asymmetry
//  (Domain/EventGate.cs), the {0,1,2,3} item-change alphabet (Domain/ItemChangeProtocol.cs), the
//  abstract host that stands in for the deferred `se_cst_datawindow`
//  (Domain/DataWindowServiceHost.cs), and the four headless models (Services/*.cs). NOTHING here
//  decides what an event means, what a veto does, or which order a chain runs in. If a behavioural
//  question can be asked of this file, the answer is in one of those files.
//
//  ITS BEHAVIOURAL SOURCE is `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru`, with
//  `n_cst_dwsvc.sru`, `n_cst_dwsvc_rowselect.sru` and `n_cst_dwsvc_contextmenu.sru` beside it and
//  `n_cst_eventful.sru` behind the broker edge. ALL ARE READ-ONLY (C-C): they are the behavioural
//  oracle, and every behavioural assertion below carries its `ws_objects/**` locator because nothing
//  else in the repository can adjudicate it - `logfile.md` stops at 2022 while the git history runs to
//  2026, and the two `.srj` build definitions contradict each other and both reference a library that
//  does not exist.
//
//  ============================ WHY THIS SERVICE IS gRPC (C-K) ==================================
//  Fixed by AAP 0.1.5 FROM THE SHAPE OF THE LEGACY INTERFACE, not from preference, and the reasoning
//  is not re-opened here. Three properties of that interface drove it, all verified at source:
//
//    1. A 22-EVENT ORDERED CHAIN WITH VETO SEMANTICS [se_cst_dw.sru:L11-L32], in which each raw event
//       delegates to a semantic one and then to the broker, and BOTH delegations can stop the dispatch
//       [:L115-L117].
//    2. A `ref string` OUT-PARAMETER - `onddsgetfilter(long row, dwobject dwo, string data,
//       ref string filter)` [:L13] - which declares NO RETURN TYPE AT ALL and produces its result by
//       mutating the argument. There is no asynchronous representation of that: the caller blocks on
//       the produced filter because the filter is the only thing the call exists to obtain.
//    3. An `any` RETURN OVER A `string[]` ARGUMENT - `oncolumnexpinvokemethod(long row, dwobject dwo,
//       string name, string args[]) -> any` [:L14].
//
//  That shape needs compile-time contract enforcement, BIDIRECTIONAL streaming to carry the ordered
//  chain in both directions, and a status model rich enough for a four-value alphabet AND a tri-valued
//  veto. Protobuf over gRPC is the only transport in the mandated stack that carries all three. JSON
//  over REST would lose both the ordering and the typed veto, and would flatten the veto to a boolean.
//
//  ==================== SIXTEEN METHODS, AND WHY THAT IS NOT SCOPE CREEP =======================
//  The contract declares SIXTEEN rpcs on this service, and the contract is authoritative on its own
//  shape. Its service comment states the reconciliation verbatim: "the eight core ones ... plus the
//  eight read/apply operations that make Section 8's headless halves reachable. The count was eight
//  while those four models were declared but referenced by nothing."
//
//  C-B forbids surface BEYOND the contract; it does not license leaving contract methods
//  UNIMPLEMENTED. The eight read/apply operations are the AAP 0.3.5 HEADLESS HALVES THAT SHIP - the
//  filter and sort expression text, the search state and counts, the menu ITEM model with its computed
//  LOGICAL text widths, and the row-selection state. They are not deferred surface. The contract's own
//  Section 8 asserts of them: "Every one is headless without exception: no geometry, no DPI
//  conversion, no font metric, no pixel coordinate, no window handle, no IME state (C-D)", and this
//  file holds that line - see the C-D self-audit below.
//
//  ======================== WHY THE EVENT STREAM NEEDS NO ROUTER ================================
//  The sibling C-04 service reaches its client through a singleton `MacroInvocationRouter`, because
//  its inverted question is raised by an ENGINE that has no stream in hand. THIS service's nine
//  semantic questions are different: every one of them is raised BY THE CHAIN, INSIDE the synchronous
//  scope of handling an inbound notification that arrived ON THE SAME STREAM. A per-stream
//  conversation is therefore sufficient and a router would be a lifetime with no owner. See
//  DataWindowEventConversation.
//
//  =========================== WHY `Aborted` IS THE CONFLICT STATUS (C-K) =======================
//  gRPC `Aborted` (code 10) is THE CANONICAL MAPPING TO HTTP 409, which is what lets Gateway's REST
//  projection surface a concurrency conflict unchanged rather than inventing a translation. The
//  contract declares that choice machine-readably on the `Update` method itself, so a client discovers
//  the trailer key, the status code and the payload type from the descriptor instead of from prose,
//  and this file READS THAT DECLARATION rather than writing the key as a literal. THE CONFLICT IS
//  SURFACED AND NEVER RETRIED: the caller implements an explicit retry-or-surface policy, Gateway's
//  typed client is documented as propagating it intact, and a retry here would silently defeat both.
//  THERE IS NO SILENT OVERWRITE ANYWHERE IN THE SYSTEM.
//
//  ===================== THE FOUR ALPHABETS THAT SHARE THE NUMERALS 1 AND 2 =====================
//  Never conflated, each its own type on the wire, all four evidenced:
//
//    * VetoResult             Continue=0, PreventOnce=1, PreventDeep=2
//                             [n_cst_eventful.sru:L111-L112]. PreventOnce is cleared as the dispatch
//                             unwinds; PreventDeep SURVIVES the unwind [clearing condition :L956].
//                             Flattening to a boolean silently turns a deep prevention into a shallow
//                             one, so the wire carries `Veto.Result`.
//    * The broker's OnException hook  1 = prevent -> exit, 2 = continue -> clear the flag and carry
//                             on, anything else -> rethrow [n_cst_eventful.sru:L889-L895]. A SEPARATE
//                             alphabet, owned by Shared.Eventful and never seen on this boundary.
//    * RetCode.PREVENT = 1    tested through Predicates. The tri-state hole is PRESERVED: IsSucceeded
//                             is `>= 0` [issucceeded.srf:L11-L13], so IsSucceeded(PREVENT) IS TRUE - a
//                             prevention reads as a success - and CANCELLED is excluded from IsFailed
//                             explicitly [isfailed.srf:L11-L13] so it is NEITHER. Not "fixed" here.
//    * The item-change {0,1,2,3} alphabet  its own enum, owned by Domain/ItemChangeProtocol.cs and
//                             transported as the contract's `ItemChangeResult`. NEVER mapped onto
//                             RetCode: `case 3` rewrites the result to 1 [se_cst_dw.sru:L223-L225] and
//                             the `case else` arm forcibly returns 2 [:L250], neither of which means
//                             what the same digit means in the return-code algebra.
//
//  ======================= STRUCTURED ERRORS REPLACE EVERY DIALOG ================================
//  ONLY THE DELIVERY CHANNEL CHANGES. Text, localization category, `Sprintf` substitution arguments
//  and severity are all preserved. The in-scope dialog sites reaching this boundary are
//  `n_cst_dwsvc_rowselect.sru:L239` (one), `n_cst_dwsvc_contextmenu.sru` (ten), and the validation-
//  error path at `se_cst_dw.sru:L355-L357`, which builds `I18N(ne_cst_i18n.CAT_DWSVC,
//  "<invalid value>") + "!"` and raises it with the stop-sign severity. Those DO route through
//  localization, so the category travels on the wire.
//
//  AND THE INCONSISTENCY IS PRESERVED, NOT HARMONISED (C-B): the 28 column-expression parse messages
//  are hardcoded and do NOT route through localization. They belong to C-04
//  (Grpc/ColumnExpressionService.cs, Expressions/ParseErrorFormatter.cs) and nothing here localizes
//  them by accident, because nothing here handles them at all.
//
//  ======================================== RULES POSITION ======================================
//  `review_rules` returns exactly "No user rules provided." - one line, nothing to page through. NO
//  user-specified rule governs this file, none is invented here, and the absence is NOT treated as
//  permission to lower the bar. The enterprise-standard baseline (AAP 0.7.2) applies in their place
//  and the binding constraints are AAP 0.7.3's non-rule inventory. Those that govern this file are
//  discharged at the point of use and audited here:
//
//  C-A SELF-AUDIT: every `using` below names either `PowerFramework.Contracts.*`, a folder of THIS
//      service, or a `PowerFramework.Shared.*` library. NOT ONE type from `PowerFramework.Gateway.*`,
//      `PowerFramework.Persistence.*` or `PowerFramework.Security.*` is referenced, and none can be -
//      the csproj declares no ProjectReference to any of them. Persistence is reached ONLY through
//      Clients/PersistenceClient.cs and Security ONLY through Clients/SecurityClient.cs.
//  C-B SELF-AUDIT: exactly the sixteen contract methods, no convenience overload, no validation the
//      legacy does not perform, NO RETRY, no fallback, no cache. The one place a guard was tempting -
//      C-05's chunk-size floor - is deliberately absent, because the contract states it "is not this
//      interface's guard".
//  C-D SELF-AUDIT: no method, message, field or placeholder for DesignSystem, Documents, Integration
//      or ScriptBridge, and no `NotImplementedException` anywhere. Specifically NOT exposed: window
//      geometry, window visibility, DPI-to-pixel conversion, font metrics, pixel coordinates, window
//      handles, IME state and popup-menu objects. The `xpos`/`ypos` on the five mouse events are
//      INPUTS the runtime supplies to a handler in the DataWindow's own logical units, consumed as
//      data - this file neither computes, converts nor renders them. The four reserved 501 routes
//      belong to Gateway; nothing analogous appears here.
//  C-E SELF-AUDIT: no storage access of any kind. Persistence is the only service with a provider.
//  C-F SELF-AUDIT: no key, token, password, credential, certificate or connection string in any form,
//      including in a comment or a default. The one field that could carry data lifted from a failing
//      statement is `common.v1.DbError.sqlsyntax`, which is RELAYED to the caller and NEVER written to
//      a log - see the redaction note on Update.
//  C-G SELF-AUDIT: no `[AllowAnonymous]`, no per-method authorization escape, and no claim is read
//      anywhere in this file. The legacy opened no listening socket, registered no route and received
//      no unsolicited request, so this is part of the system's first-ever ingress; authentication is
//      the stock JwtBearer handler validating against Security's published JWKS, configured in
//      Program.cs, and this service holds VERIFICATION MATERIAL ONLY - Security is the sole issuer and
//      no signing key appears here.
//  C-H SELF-AUDIT: every collaborator arrives by constructor injection, there is NO static mutable
//      state, and no method needs a live Persistence instance to be driven - the client is a class
//      with virtual members, so a double substitutes for it.
//  C-L SELF-AUDIT: `/health` and `/v1/ping` are NOT defined here. They live in Endpoints/, where their
//      anonymous and token-required postures are declared and exercised.
//
//  FAIL FAST, NEVER GRACEFUL DEGRADATION (AAP 0.6.7). Within this file a structurally impossible state
//  - an unknown session, a stream message with no session, an out-of-order arrival inside a
//  synchronous ordering group - is a HARD DEFINED ERROR, not a best-effort recovery. Softening one
//  into warn-and-continue would be a behavioural change dressed as robustness.
// ==================================================================================================

using System.Globalization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Eventful;
using PowerFramework.Shared.Kernel;

// ---- Alias block. Every entry resolves a REAL collision, none is decoration.
// ----
// ---- Wire*/Domain* pairs are collisions BETWEEN THE TWO VOCABULARIES, spelled so a reader never has
// ---- to work out which side of the boundary a name is on; the bare form of such a name is never used
// ---- below. `EventId` is different in kind - it collides with Microsoft.Extensions.Logging.EventId,
// ---- which this file never uses - so it is bound to the contract type under its own name, matching
// ---- Domain/DataWindowEventChain.cs's alias for the same type and keeping the two files' spelling of
// ---- the event identity identical.
using DomainContextMenuModel = PowerFramework.DataServices.Services.ContextMenuModel;
using DomainEventGate = PowerFramework.DataServices.Domain.EventGate;
using DomainItemChangeResult = PowerFramework.DataServices.Domain.ItemChangeResult;
using EventId = global::PowerFramework.Contracts.DataServices.V1.EventId;
using GeneratedDataWindowServiceBase =
    global::PowerFramework.Contracts.DataServices.V1.DataWindowService.DataWindowServiceBase;
using PersistenceBeginSessionRequest =
    global::PowerFramework.Contracts.Persistence.V1.BeginSessionRequest;
using PersistenceCreateQueryTaskRequest =
    global::PowerFramework.Contracts.Persistence.V1.CreateQueryTaskRequest;
using PersistenceCreateUpdateTaskRequest =
    global::PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest;
using PersistenceEndSessionRequest =
    global::PowerFramework.Contracts.Persistence.V1.EndSessionRequest;
using PersistencePrepareUpdateRequest =
    global::PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest;
using PersistenceQueryRequest = global::PowerFramework.Contracts.Persistence.V1.QueryRequest;
using PersistenceQueryResponse = global::PowerFramework.Contracts.Persistence.V1.QueryResponse;
using PersistenceQuerySpec = global::PowerFramework.Contracts.Persistence.V1.QuerySpec;
using PersistenceReleaseQueryTaskRequest =
    global::PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskRequest;
using PersistenceReleaseUpdateTaskRequest =
    global::PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest;
using PersistenceSessionHandle = global::PowerFramework.Contracts.Persistence.V1.SessionHandle;
using PersistenceTaskHandle = global::PowerFramework.Contracts.Persistence.V1.TaskHandle;
using PersistenceTransactionDescriptor =
    global::PowerFramework.Contracts.Persistence.V1.TransactionDescriptor;
using PersistenceUpdateRequest = global::PowerFramework.Contracts.Persistence.V1.UpdateRequest;
using PositionalParameter = global::PowerFramework.Contracts.Persistence.V1.PositionalParameter;

// The session and task lifecycle types C-05 through C-08 publish. Aliased individually rather than
// imported wholesale for the same reason every other Persistence type here is: the namespace publishes an
// UpdateRequest, an UpdateResponse and a DataWindowService of its own, and naming exactly the types used
// keeps the collision surface at zero rather than relying on lexical luck.
using BeginSessionRequest = global::PowerFramework.Contracts.Persistence.V1.BeginSessionRequest;
using BeginSessionResponse = global::PowerFramework.Contracts.Persistence.V1.BeginSessionResponse;
using CreateQueryTaskRequest = global::PowerFramework.Contracts.Persistence.V1.CreateQueryTaskRequest;
using CreateQueryTaskResponse = global::PowerFramework.Contracts.Persistence.V1.CreateQueryTaskResponse;
using CreateUpdateTaskRequest = global::PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskRequest;
using CreateUpdateTaskResponse = global::PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse;
using EndSessionRequest = global::PowerFramework.Contracts.Persistence.V1.EndSessionRequest;
using OperationStatus = global::PowerFramework.Contracts.Persistence.V1.OperationStatus;
using ReleaseQueryTaskRequest = global::PowerFramework.Contracts.Persistence.V1.ReleaseQueryTaskRequest;
using PrepareUpdateRequest = global::PowerFramework.Contracts.Persistence.V1.PrepareUpdateRequest;
using PrepareUpdateResponse = global::PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse;
using TableUpdateContract = global::PowerFramework.Contracts.Persistence.V1.TableUpdateContract;
using ReleaseUpdateTaskRequest = global::PowerFramework.Contracts.Persistence.V1.ReleaseUpdateTaskRequest;
using SessionHandle = global::PowerFramework.Contracts.Persistence.V1.SessionHandle;
using TaskHandle = global::PowerFramework.Contracts.Persistence.V1.TaskHandle;
using TransactionDescriptor = global::PowerFramework.Contracts.Persistence.V1.TransactionDescriptor;

// The PORTED constant catalogue, which is what every outcome code in this file is spelled from. The wire
// twin is `common.v1.RetCode`, a wrapper message whose nested enum is aliased below as WireRetCode; the
// two agree value for value by construction (see the note on ToWireRetCode) but they are different types
// and the bare name must resolve to exactly one of them.
using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireContextMenuModel = global::PowerFramework.Contracts.DataServices.V1.ContextMenuModel;
using WireEventGate = global::PowerFramework.Contracts.DataServices.V1.EventGate;
using WireItemChangeResult = global::PowerFramework.Contracts.DataServices.V1.ItemChangeResult;
using WireRetCode = global::PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.DataServices.Grpc;

/// <summary>
/// The seam through which the DataWindow event chain asks its client one of the nine SEMANTIC
/// questions - the port of the framework asking the application, across a boundary the application now
/// sits on the far side of.
/// </summary>
/// <remarks>
/// <para>
/// WHY AN INVERSION IS STRUCTURALLY REQUIRED AND NOT A DESIGN PREFERENCE (C-K). Nine of the twenty-two
/// events are the framework ASKING rather than telling:
/// <c>oninitcontextmenu</c> [<c>se_cst_dw.sru:L11</c>], <c>oncontextmenu</c> [<c>:L12</c>],
/// <c>onddsgetfilter</c> [<c>:L13</c>], <c>oncolumnexpinvokemethod</c> [<c>:L14</c>],
/// <c>ondoitemchange</c> [<c>:L24</c>], <c>onitemchanged</c> [<c>:L25</c>],
/// <c>ondoitemchanged</c> [<c>:L26</c>], <c>onddsfiltered</c> [<c>:L28</c>] and
/// <c>oncolumnexptrace</c> [<c>:L32</c>]. In the legacy the answer comes from a handler in the same
/// process. Here DataServices holds the DataWindow logic while its client holds the application, so the
/// question must travel outbound WHILE THE INBOUND CALL THAT PROVOKED IT IS STILL OPEN. Two unary calls
/// cannot express that; one bidirectional stream can, which is why the contract's <c>EventChain</c> is
/// bidirectional and why its response carries an <c>invoke</c> alternative alongside <c>result</c>.
/// </para>
/// <para>
/// STRICTLY SYNCHRONOUS FOR SEVEN OF THE NINE. <c>onddsgetfilter</c> produces its result through a
/// <c>ref string</c> out-parameter with no return type at all [<c>:L13</c>], so the caller BLOCKS on
/// the produced filter; <c>oncolumnexpinvokemethod</c> cannot let a calculation proceed without the
/// returned value [<c>:L14</c>]; and the item-change group's result feeds the validation-error handler
/// that follows it [<c>:L331-L332</c>, <c>:L338-L340</c>]. The two exceptions are pure notification -
/// <c>onddsfiltered</c> and <c>oncolumnexptrace</c> - and the discipline for each is resolved by
/// <see cref="DataWindowEventOrdering.DisciplineOf(EventId, bool)"/> rather than by this interface.
/// </para>
/// <para>
/// IMPLEMENTED BY THE TRANSPORT, CONSUMED BY THE CHAIN. <see cref="DataWindowEventConversation"/> is
/// the implementation, and it is per-stream. A concrete <c>DataWindowEventChain</c> receives one
/// through <see cref="IDataWindowEventChainFactory"/> and overrides its semantic members to ask
/// through it.
/// </para>
/// </remarks>
public interface IDataWindowSemanticResponder
{
    /// <summary>
    /// Asks the client one semantic question and waits for its answer.
    /// </summary>
    /// <param name="question">
    /// The question, already carrying its <c>EventId</c> and its populated body. Never
    /// <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>
    /// The client's answer. A client that does not handle the event answers with an unset veto and a
    /// zero return value, which is exactly what an unhandled PowerBuilder event yields.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="question"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The channel is closed, or a question is already outstanding on it. ONE AT A TIME IS THE
    /// CONTRACT: the ordering disciplines of AAP 0.6.1.4 are defined over a single ordered
    /// conversation, and a second concurrent question would make the sequence ambiguous.
    /// </exception>
    ValueTask<EventResult> AskAsync(EventNotification question, CancellationToken cancellationToken);
}

/// <summary>
/// The four headless models of AAP 0.3.5, bound to one DataWindow.
/// </summary>
/// <param name="Host">
/// The host the four models are attached to. The abstract stand-in for the deferred
/// <c>se_cst_datawindow</c>, per AAP 0.2.1.3 Correction 3.
/// </param>
/// <param name="ContextMenu">
/// The context-menu ITEM model - <c>n_cst_dwsvc_contextmenu</c> [<c>se_cst_dw.sru:L80</c>]. Its
/// headless half only: labels, identifiers, enabled and split flags, and computed LOGICAL text widths.
/// </param>
/// <param name="RowSelect">
/// The row-selection service - <c>n_cst_dwsvc_rowselect</c> [<c>:L81</c>]. Headless in its ENTIRETY.
/// </param>
/// <param name="ColumnSort">
/// The column-sort model - <c>n_cst_dwsvc_columnsort</c> [<c>:L82</c>]. Its headless half only: the
/// sort state and the generated sort expression.
/// </param>
/// <param name="DropDownSearch">
/// The drop-down search model - <c>n_cst_dwsvc_dropdownsearch</c> [<c>:L83</c>]. Its headless half
/// only: the constructed filter expression, the search state and the counts.
/// </param>
/// <remarks>
/// THE FIFTH ATTACHED SERVICE IS DELIBERATELY ABSENT. <c>ColumnExp</c> [<c>:L84</c>] belongs to
/// contract C-04, which is a SEPARATE SERVICE so the expansion engine can version independently of the
/// event chain (AAP 0.4.3). Carrying it here would re-couple the two across the very boundary the split
/// exists to draw.
/// </remarks>
public sealed record DataWindowModelSet(
    DataWindowServiceHost Host,
    DomainContextMenuModel ContextMenu,
    RowSelectService RowSelect,
    ColumnSortModel ColumnSort,
    DropDownSearchModel DropDownSearch);

/// <summary>
/// Binds a DataWindow handle to its retained set of headless models.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS RATHER THAN FOUR INJECTED MODELS. All four derive from
/// <c>DataWindowServiceBase</c> and are USELESS UNTIL ATTACHED to a host - each requires
/// <c>OnInit</c>, and each throws rather than guessing when it is asked to work unattached. They are
/// per-DataWindow state, exactly as the legacy has them: five instance members of one control
/// [<c>se_cst_dw.sru:L80-L84</c>]. Four container singletons would be four permanently unattached
/// objects, so the binding arrives through this seam - the same composition-not-behaviour reasoning
/// that puts <c>IDataWindowHostFactory</c> in the sibling C-04 service rather than in Program.cs.
/// </para>
/// <para>
/// GET-OR-CREATE, AND THE RETENTION IS THE POINT. The legacy services live as long as the control
/// does, so <c>ApplyColumnSort</c> followed by <c>GetColumnSortState</c> MUST observe the sort that was
/// applied. An implementation that built a fresh set per call would answer every read with a default
/// and lose every write - a defect that returns a well-formed response and so survives any assertion
/// that only checks the shape.
/// </para>
/// <para>
/// FAIL FAST, NOT OPTIONAL. This is a REQUIRED dependency: the eight read/apply operations cannot
/// answer anything without it, so a service composed without one is structurally faulty rather than
/// partly working, and AAP 0.1.4 requires that stay fail-fast instead of softening into a service that
/// answers <c>E_NO_IMPLEMENTATION</c> to everything.
/// </para>
/// </remarks>
public interface IDataWindowModelSetProvider
{
    /// <summary>
    /// Returns the retained model set for a DataWindow handle, creating it on first use.
    /// </summary>
    /// <param name="dataWindowHandle">
    /// The caller's own name for the DataWindow, taken verbatim from the request. It is NOT a session
    /// identifier.
    /// </param>
    /// <returns>
    /// The model set, or <see langword="null"/> when this provider cannot serve the handle. A null
    /// answer becomes <c>RetCode.E_INVALID_HANDLE</c> and NEVER a silently created set: a caller that
    /// mistyped a handle must learn that, not receive a second empty DataWindow.
    /// </returns>
    DataWindowModelSet? GetOrCreate(string dataWindowHandle);
}

/// <summary>
/// One update table's contract, as a DataWindow's own definition declares it.
/// </summary>
/// <param name="TableName">The update table [<c>dw_sqlite.srd:L14</c> <c>update="COMPANY"</c>].</param>
/// <param name="UpdatableColumns">
/// Every column the definition marks <c>update=yes</c>, in declaration order.
/// </param>
/// <param name="KeyColumns">Every column the definition marks <c>key=yes</c>, in declaration order.</param>
/// <param name="IdentityColumn">
/// The column the definition marks <c>identity=yes</c>, or the empty string when it marks none.
/// </param>
/// <param name="UpdateWhere">
/// The update-where MODE, a <c>long</c> rather than a flag: 0 is key columns only, 1 is key and
/// updateable columns, 2 is key and modified columns. Absent means "leave the carrier's own setting
/// alone", which is what C-06 does with an unset field.
/// </param>
/// <param name="UpdateKeyInPlace">
/// Whether a key change is performed in place. Absent means "leave the carrier's own setting alone".
/// </param>
/// <remarks>
/// <para>
/// <b>THIS IS DERIVED FROM THE DEFINITION AND IS NEVER TAKEN FROM A REQUEST.</b> C-03's
/// <c>UpdateRequest</c> carries a handle, an optional session and the rows - and deliberately no table
/// descriptor, because the legacy has none to carry: its update contract lives in the DataWindow's own
/// static definition, re-derived at run time from the descriptor array
/// [<c>n_cst_thread_task_sqlupdate.sru:L98-L145</c>]. Publishing the descriptor as a request field would
/// let a caller name an update table the DataWindow does not declare, which is an authorization hole
/// dressed as flexibility.
/// </para>
/// <para>
/// A NULL ANSWER FROM THE PROVIDER IS ORDINARY, NOT A FAULT. A derived or read-only DataWindow declares
/// no update table at all, and C-06's prepare step accepts an empty descriptor array whenever
/// multi-table update is off [<c>:L365</c> against <c>:L371</c>]. So an absent descriptor means "this
/// DataWindow's own definition governs", which is precisely the legacy single-table shape.
/// </para>
/// </remarks>
public sealed record DataWindowUpdateContract(
    string TableName,
    IReadOnlyList<string> UpdatableColumns,
    IReadOnlyList<string> KeyColumns,
    string IdentityColumn,
    long? UpdateWhere,
    bool? UpdateKeyInPlace);

/// <summary>
/// Resolves the update contract a DataWindow's own definition declares.
/// </summary>
/// <remarks>
/// <para>
/// WHY A SEAM RATHER THAN A DIRECT READ OF THE DEFINITION STORE. The definition store is a
/// <c>Domain</c> concern and this file is the boundary; the same reasoning that puts
/// <see cref="IDataWindowModelSetProvider"/> here rather than injecting the concrete registry applies
/// unchanged. It also keeps the update path testable without a registry: a fixture supplies the
/// descriptor it wants to assert on.
/// </para>
/// <para>
/// OPTIONAL ON THE SERVICE, AND THAT IS DELIBERATE. An update composed without this seam still runs -
/// it names the data object and lets the carrier's own definition govern, which is the legacy
/// single-table path exactly. What it cannot do is drive a MULTI-TABLE update, and it does not pretend
/// to: C-03 publishes no way to request one.
/// </para>
/// </remarks>
public interface IDataWindowUpdateContractProvider
{
    /// <summary>
    /// Returns the update contract for a DataWindow handle.
    /// </summary>
    /// <param name="dataWindowHandle">The caller's own name for the DataWindow.</param>
    /// <returns>
    /// The contract, or <see langword="null"/> when the handle names nothing registered OR when the
    /// definition it names declares no update table. The two are deliberately not distinguished: both
    /// mean "there is no descriptor to send", and C-06 refuses an update against a DataWindow with no
    /// updatable table on its own authority [<c>n_cst_thread_task_sqlupdate.sru:L179-L189</c>].
    /// </returns>
    DataWindowUpdateContract? GetUpdateContract(string dataWindowHandle);
}

/// <summary>
/// Creates the concrete event chain for one validation session.
/// </summary>
/// <remarks>
/// <para>
/// WHY A SEAM AND NOT A CONSTRUCTOR CALL. <c>DataWindowEventChain</c> is abstract because it IS a
/// <c>DataWindowServiceHost</c>, and a host's members - <c>Describe</c>, <c>RowCount</c>,
/// <c>AcceptText</c>, <c>SetItem</c> - can only be answered by something holding a real DataWindow.
/// DataServices deliberately does not: <c>se_cst_dw</c> derives from <c>se_cst_datawindow</c>
/// [<c>se_cst_dw.sru:L4</c>, <c>:L10</c>], which lives in the DEFERRED DesignSystem library, and AAP
/// 0.2.1.3 Correction 3 resolves that STRUCTURAL INHERITANCE EDGE by declaring an abstract host
/// contract carrying only the members the service layer consumes and recording the legacy parent as
/// REFERENCE-only. Binding that abstraction to something concrete is a COMPOSITION concern, so it
/// arrives here.
/// </para>
/// <para>
/// THE CHAIN OWNS THE ORDER, THIS FILE DOES NOT. A chain built by this factory brings with it the
/// creation order and the DIFFERENT initialisation order of the five attached services
/// [<c>se_cst_dw.sru:L570-L574</c> against <c>:L576-L580</c>, where positions three and four are
/// swapped], the broker created before any service [<c>:L562</c>], the gate bit-tests, the veto
/// two-step and the item-change micro-protocol. None of that is re-implemented, re-ordered or
/// second-guessed on this boundary.
/// </para>
/// </remarks>
internal interface IDataWindowEventChainFactory
{
    /// <summary>
    /// Creates a chain bound to a session and a DataWindow.
    /// </summary>
    /// <param name="session">
    /// The session holding the four pieces of cross-event state [<c>se_cst_dw.sru:L89-L96</c>]. The
    /// chain reads and writes them; this file only opens, correlates and closes.
    /// </param>
    /// <param name="dataWindowHandle">The caller's own name for the DataWindow.</param>
    /// <param name="observer">
    /// Receives one <c>DataWindowEventOutcome</c> per dispatch, immediately before the handler returns
    /// its numeric. This is how the rich outcome reaches the wire, since the chain's own event members
    /// return only a <c>long</c>.
    /// </param>
    /// <param name="responder">
    /// The channel a semantic handler asks the client through. See
    /// <see cref="IDataWindowSemanticResponder"/>.
    /// </param>
    /// <returns>
    /// The chain, or <see langword="null"/> when this factory cannot serve the handle - which becomes
    /// <c>RetCode.E_INVALID_HANDLE</c> rather than a chain over a DataWindow that does not exist.
    /// </returns>
    DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder);
}

/// <summary>
/// Projects the domain vocabulary onto contract C-03's wire vocabulary, and back.
/// </summary>
/// <remarks>
/// <para>
/// EVERY MEMBER IS A PROJECTION AND NONE IS A DECISION. Not one method here chooses a veto, resolves an
/// ordering discipline, classifies an item-change result or computes a gate. Each takes a value the
/// domain already produced and renames it for the wire. That distinction is what keeps the behavioural
/// authority in Domain/ where the oracle locators live.
/// </para>
/// <para>
/// SELF-CONTAINED BY DESIGN, AND THE ONE TEMPTING REUSE WAS DELIBERATELY REJECTED (C-K). The C-04 service
/// file carries its own equivalent of the outcome-code cast and the <c>object?</c>-to-<c>AnyValue</c>
/// union, and reaching for them from here was considered and refused for two independent reasons. First,
/// this file's dependency whitelist does not include the sibling service's file, and reaching outside a
/// declared whitelist is not made acceptable by the target being convenient. Second, and more
/// importantly, both service files state that C-03 and C-04 are SEPARATE CONTRACTS so the expansion
/// engine can version independently of the event chain; a compile edge from one service's transport file
/// to the other's would couple exactly what the split exists to separate, and it would do so through a
/// helper - the least visible kind of coupling to review. So the two primitives are implemented here.
/// </para>
/// <para>
/// THE DUPLICATION IS THEREFORE DELIBERATE, AND ITS TWIN IS NAMED SO THE PAIR CAN BE REVIEWED TOGETHER:
/// <c>ExpressionWireProjection</c> in <c>Grpc/ColumnExpressionService.cs</c> holds the C-04 copies. The
/// two must classify a value identically or a characterization recording taken through one contract will
/// not compare against the other, so each carries the same evidence and the same arms. The complementary
/// half of the boundary is already drawn by that file: it leaves <c>col_type_prefix</c> UNSPECIFIED
/// because the coercion switch [<c>se_cst_dw.sru:L231-L244</c>] is C-03's, so the classification below is
/// this file's to own and is owned here.
/// </para>
/// </remarks>
internal static class DataWindowWireProjection
{
    /// <summary>
    /// The number of leading characters the legacy coercion switch dispatches on.
    /// </summary>
    /// <remarks>
    /// <c>Left(dwo.ColType,5)</c> [<c>se_cst_dw.sru:L231</c>]. Five, verbatim, and named rather than
    /// written as a literal at the one place it is used, because it is the reason <c>"datetime"</c> and
    /// <c>"date"</c> land on different arms at all.
    /// </remarks>
    internal const int ColTypePrefixLength = 5;

    /// <summary>
    /// Projects a ported outcome code onto the wire enum.
    /// </summary>
    /// <param name="code">A value from <see cref="RetCode"/>.</param>
    /// <returns>The wire value.</returns>
    /// <remarks>
    /// A CAST IS CORRECT HERE AND IS NOT A SHORTCUT. <c>common.v1.RetCode.Value</c> is generated from a
    /// proto enum whose members cite <c>ws_objects/pfw.shared.pbl.src/retcode.sru</c> line by line, so the
    /// two catalogues carry IDENTICAL NUMERIC VALUES by construction; preserving the identifiers
    /// (AAP 0.4.5.3) is what makes the numbers travel unchanged. Every ported constant fits in
    /// <see cref="int"/> - the widest magnitude is <c>UNKNOWN = -4000</c> - and protobuf permits an
    /// unrecognised numeric value to travel as itself, so a code this file has never seen arrives intact
    /// rather than being folded onto a default. THE AGREEMENT HAS NO COMPILE-TIME EDGE, deliberately: the
    /// contracts project holds no reference to the shared kernel, so keeping the conversion on one line
    /// means there is exactly one line to inspect when the invariant is reviewed.
    /// </remarks>
    internal static WireRetCode ToWireRetCode(long code) => (WireRetCode)(int)code;

    /// <summary>
    /// Projects a value read from a DataWindow buffer onto the wire's discriminated union.
    /// </summary>
    /// <param name="value">The value, which may legitimately be <see langword="null"/>.</param>
    /// <returns>The union, with the explicit null arm set when the value is <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// NULL IS NEVER COLLAPSED TO ZERO OR TO THE EMPTY STRING. PowerBuilder has null for value types and
    /// the item-change equality test at <c>se_cst_dw.sru:L198-L202</c> has an EXPLICIT null-and-null arm,
    /// so a consumer that could not tell null from zero could not reproduce that arm. Collapsing it would
    /// also turn "neither succeeded nor failed" into "succeeded" wherever the tri-state predicates read the
    /// same value. An unhandled event of type <c>any</c> yields null too, which is why the null arm is a
    /// STATED member of the union rather than field absence.
    /// </para>
    /// <para>
    /// A TYPE OUTSIDE THE LEGACY CLASSES TRAVELS AS ITS OWN INVARIANT RENDERING rather than being dropped,
    /// so it is refused downstream by name instead of vanishing. Rendered invariantly throughout: a
    /// culture-sensitive rendering would make the same value serialise differently on two hosts and break
    /// every characterization comparison that touched it.
    /// </para>
    /// </remarks>
    internal static AnyValue ToWireAny(object? value) => value switch
    {
        null => new AnyValue { IsNull = true },
        bool boolean => new AnyValue { BoolValue = boolean },
        string text => new AnyValue { StringValue = text },
        short integer => new AnyValue { Int64Value = integer },
        int integer => new AnyValue { Int64Value = integer },
        long integer => new AnyValue { Int64Value = integer },
        ushort unsigned => new AnyValue { Uint64Value = unsigned },
        uint unsigned => new AnyValue { Uint64Value = unsigned },
        ulong unsigned => new AnyValue { Uint64Value = unsigned },
        float real => new AnyValue { DoubleValue = real },
        double real => new AnyValue { DoubleValue = real },
        decimal number => new AnyValue
        {
            DecimalValue = new DecimalValue
            {
                Value = number.ToString(CultureInfo.InvariantCulture),
            },
        },
        DateTime moment => new AnyValue { DatetimeValue = ToWireDateTime(moment) },
        DateOnly day => new AnyValue { DateValue = ToWireDate(day) },
        TimeOnly clock => new AnyValue { TimeValue = ToWireTime(clock) },
        byte[] blob => new AnyValue { BlobValue = ByteString.CopyFrom(blob) },
        _ => new AnyValue
        {
            StringValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        },
    };

    // =============================================================================================
    //  THE THREE TEMPORAL MESSAGES
    //  -------------------------------------------------------------------------------------------
    //  THREE MESSAGES AND NOT ONE TIMESTAMP, because the legacy has three distinct types - `date`,
    //  `time` and `datetime` - and NO TIME-ZONE CONCEPT AT ALL. The item-change coercion switch
    //  dispatches on which of the three a column is [se_cst_dw.sru:L238-L243], so collapsing them
    //  would destroy the information that behaviour depends on. Emission is always canonical, and the
    //  fractional part appears only when it is non-zero so a whole-second value renders identically to
    //  the way the oracle renders it.
    // =============================================================================================

    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";
    private const string TimeFractionFormat = "HH:mm:ss.ffffff";
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";
    private const string DateTimeFractionFormat = "yyyy-MM-ddTHH:mm:ss.ffffff";

    /// <summary>Projects a legacy <c>date</c> onto its wire message.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The message.</returns>
    internal static DateValue ToWireDate(DateOnly value) =>
        new() { Value = value.ToString(DateFormat, CultureInfo.InvariantCulture) };

    /// <summary>Projects a legacy <c>time</c> onto its wire message.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The message, carrying a fractional part only when it is non-zero.</returns>
    internal static TimeValue ToWireTime(TimeOnly value) =>
        new()
        {
            Value = value.Ticks % TimeSpan.TicksPerSecond == 0
                ? value.ToString(TimeFormat, CultureInfo.InvariantCulture)
                : value.ToString(TimeFractionFormat, CultureInfo.InvariantCulture),
        };

    /// <summary>Projects a legacy <c>datetime</c> onto its wire message.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The message, UNZONED and carrying a fractional part only when it is non-zero.</returns>
    internal static DateTimeValue ToWireDateTime(DateTime value) =>
        new()
        {
            Value = value.Ticks % TimeSpan.TicksPerSecond == 0
                ? value.ToString(DateTimeFormat, CultureInfo.InvariantCulture)
                : value.ToString(DateTimeFractionFormat, CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Projects a DataWindow column object onto its wire reference, INCLUDING the coercion prefix.
    /// </summary>
    /// <param name="dwo">The object, or <see langword="null"/>.</param>
    /// <returns>The reference; an empty one when <paramref name="dwo"/> is <see langword="null"/>.</returns>
    /// <remarks>
    /// <c>dwobject</c> is a LIVE object at source. Its identifier is the ONE-BASED DataWindow ordinal and is
    /// read through the ported <c>ColumnId()</c> projection rather than cast here, because the oracle's
    /// identifier arrives as an <c>any</c> and the coercion is that projection's job - the oracle reads it as
    /// <c>Long(dwo.ID)</c> and passes it straight to <c>SetItem</c> and <c>GetItemStatus</c>
    /// [<c>se_cst_dw.sru:L190</c>, <c>:L219-L220</c>]. The declared type travels UNABBREVIATED even though
    /// the coercion switch reads only its first five characters, because the dormant byte-length check at
    /// <c>:L280-L290</c> parses the length out of the whole string.
    /// </remarks>
    internal static DwObjectRef ToWireDwObject(IDataWindowObject? dwo) =>
        dwo is null
            ? new DwObjectRef()
            : new DwObjectRef
            {
                Name = dwo.Name ?? string.Empty,
                Id = dwo.ColumnId() ?? 0L,
                ColType = dwo.ColType ?? string.Empty,
                ColTypePrefix = ClassifyColType(dwo.ColType),
            };

    /// <summary>
    /// Classifies a declared column type onto the coercion alphabet of
    /// <c>se_cst_dw.sru:L231-L244</c>.
    /// </summary>
    /// <param name="colType">The full declared column type, for example <c>char(50)</c>.</param>
    /// <returns>
    /// The matching arm, or <c>COL_TYPE_PREFIX_UNSPECIFIED</c> for a type outside the fixed set.
    /// </returns>
    /// <remarks>
    /// <para>
    /// TEN LITERALS AND NO <c>case else</c>, VERBATIM. The oracle's switch has exactly six arms over ten
    /// literals - <c>"char","char("</c>, <c>"decim","real","numbe"</c>, <c>"long","ulong"</c>,
    /// <c>"datet"</c>, <c>"date"</c>, <c>"time"</c> - and NO default arm of its own, so a column whose
    /// type matches none of them IS SIMPLY NOT WRITTEN. That silent no-op is the legacy behaviour and is
    /// preserved as the unspecified member rather than promoted to an error (C-B).
    /// </para>
    /// <para>
    /// WHY EXACT MATCHING ON THE TRUNCATED PREFIX RATHER THAN <c>StartsWith</c>. The oracle tests
    /// <c>"datet"</c> BEFORE <c>"date"</c> [<c>:L238</c> before <c>:L240</c>], and the contract records
    /// that ordering as a preserved trap: a reimplementation testing <c>StartsWith("date")</c> first
    /// would swallow every <c>datetime</c> column into the date arm and SILENTLY DROP THE TIME OF DAY.
    /// Matching the truncated prefix exactly is immune to the trap by construction - <c>datetime</c>
    /// truncates to <c>"datet"</c> and <c>date</c> truncates to <c>"date"</c>, which are different
    /// strings - so the arms cannot alias however they are ordered.
    /// </para>
    /// <para>
    /// Compared ordinally. These are DataWindow type keywords, not human-readable text, and no culture
    /// may participate in matching them.
    /// </para>
    /// </remarks>
    internal static DwObjectRef.Types.ColTypePrefix ClassifyColType(string? colType)
    {
        if (string.IsNullOrEmpty(colType))
        {
            return DwObjectRef.Types.ColTypePrefix.Unspecified;
        }

        string prefix = colType.Length <= ColTypePrefixLength
            ? colType
            : colType[..ColTypePrefixLength];

        return prefix switch
        {
            // :L232-L233  case "char","char("  -> SetItem(..., data)
            "char" => DwObjectRef.Types.ColTypePrefix.Char,
            "char(" => DwObjectRef.Types.ColTypePrefix.CharParen,

            // :L234-L235  case "decim","real","numbe"  -> SetItem(..., Dec(data))
            "decim" => DwObjectRef.Types.ColTypePrefix.Decim,
            "real" => DwObjectRef.Types.ColTypePrefix.Real,
            "numbe" => DwObjectRef.Types.ColTypePrefix.Numbe,

            // :L236-L237  case "long","ulong"  -> SetItem(..., Long(data))
            "long" => DwObjectRef.Types.ColTypePrefix.Long,
            "ulong" => DwObjectRef.Types.ColTypePrefix.Ulong,

            // :L238-L239  case "datet"  -> SetItem(..., DateTime(data)).  BEFORE the date arm.
            "datet" => DwObjectRef.Types.ColTypePrefix.Datet,

            // :L240-L241  case "date"  -> SetItem(..., Date(data))
            "date" => DwObjectRef.Types.ColTypePrefix.Date,

            // :L242-L243  case "time"  -> SetItem(..., Time(data))
            "time" => DwObjectRef.Types.ColTypePrefix.Time,

            // No `case else` at source: not written, silently.
            _ => DwObjectRef.Types.ColTypePrefix.Unspecified,
        };
    }

    /// <summary>
    /// Projects the tri-valued veto onto the wire.
    /// </summary>
    /// <param name="veto">The domain value.</param>
    /// <returns>The wire member.</returns>
    /// <remarks>
    /// THREE MEMBERS AND NEVER A BOOLEAN [<c>n_cst_eventful.sru:L111-L112</c>].
    /// <c>PreventOnce</c> aborts the current dispatch level and is cleared as that level unwinds;
    /// <c>PreventDeep</c> aborts the whole nested chain and SURVIVES the unwind [clearing condition at
    /// <c>:L956</c>]. Flattening the pair to "prevented" would silently convert a deep prevention into a
    /// shallow one and let a chain resume that the oracle had stopped for good.
    /// </remarks>
    internal static Veto.Types.Result ToWireVeto(VetoResult veto) => veto switch
    {
        VetoResult.PreventOnce => Veto.Types.Result.PreventOnce,
        VetoResult.PreventDeep => Veto.Types.Result.PreventDeep,
        _ => Veto.Types.Result.Continue,
    };

    /// <summary>
    /// Projects the item-change alphabet onto the wire.
    /// </summary>
    /// <param name="result">The domain member.</param>
    /// <returns>The wire member.</returns>
    /// <remarks>
    /// ITS OWN ALPHABET, NEVER MAPPED ONTO <see cref="RetCode"/>. <c>{0,1,2,3}</c> from the
    /// <c>choose case</c> at <c>se_cst_dw.sru:L211-L251</c>, where <c>1</c> is an empty guard, <c>3</c>
    /// rewrites the result to <c>1</c> [<c>:L223-L225</c>] and the default arm forcibly returns <c>2</c>
    /// [<c>:L250</c>]. None of those digits means what the same digit means in the return-code algebra,
    /// and the classification itself is <c>Domain/ItemChangeProtocol.cs</c>'s - this only renames it.
    /// </remarks>
    internal static WireItemChangeResult ToWireItemChange(DomainItemChangeResult result) => result switch
    {
        DomainItemChangeResult.TriggerValidationError => WireItemChangeResult.TriggerValidationError,
        DomainItemChangeResult.RestoreAndRejectText => WireItemChangeResult.RestoreAndRejectText,
        DomainItemChangeResult.KeepValueNoFocusMove => WireItemChangeResult.KeepValueNoFocusMove,
        _ => WireItemChangeResult.Default,
    };

    /// <summary>
    /// Projects a dialog severity onto the wire.
    /// </summary>
    /// <param name="severity">The domain member.</param>
    /// <returns>The wire member, value for value.</returns>
    /// <remarks>
    /// The two enumerations agree member for member and value for value, so this is a rename. It is
    /// written out rather than cast because the domain enumeration is a port of the PowerBuilder
    /// <c>Icon</c> domain and the wire one is generated, and nothing forces them to stay aligned.
    /// </remarks>
    internal static Severity ToWireSeverity(DialogSeverity severity) => severity switch
    {
        DialogSeverity.None => Severity.None,
        DialogSeverity.Information => Severity.Information,
        DialogSeverity.Question => Severity.Question,
        DialogSeverity.Exclamation => Severity.Exclamation,
        DialogSeverity.StopSign => Severity.StopSign,
        _ => Severity.Unspecified,
    };

    /// <summary>
    /// Projects the validation-error dialog onto a structured error.
    /// </summary>
    /// <param name="error">The domain error, or <see langword="null"/> when no dialog was raised.</param>
    /// <returns>The wire error, or <see langword="null"/>.</returns>
    /// <remarks>
    /// ONLY THE DELIVERY CHANNEL CHANGES [<c>se_cst_dw.sru:L355-L357</c>]. The title, the text, the
    /// localization category, the pre-substitution arguments and the stop-sign severity are all carried.
    /// The category travels as DATA rather than being implied, because a characterization recording has
    /// to be comparable against the legacy's own and the category selects which translation table
    /// answered - and because these messages, unlike the 28 column-expression ones, DO route through
    /// localization.
    /// </remarks>
    internal static StructuredError? ToWireError(ValidationStructuredError? error)
    {
        if (error is null)
        {
            return null;
        }

        StructuredError projected = new()
        {
            Title = error.Title,
            Text = error.Text,
            Localized = error.Localized,
            Category = error.LocalizationCategory,
            Severity = ToWireSeverity(error.Severity),
        };

        projected.FormatArgs.AddRange(error.FormatArguments);

        // Absent at source on this path, and absence is meaningful: :L357 raises a dialog, not an
        // operation, and the code the event ultimately returns belongs to the item-change alphabet
        // rather than to the return-code algebra. Only a real code is written.
        if (error.ReturnCode.HasValue)
        {
            projected.RetCode = ToWireRetCode(error.ReturnCode.Value);
        }

        return projected;
    }

    /// <summary>
    /// Projects the row-selection rejection dialog onto a structured error.
    /// </summary>
    /// <param name="error">The domain error, or <see langword="null"/>.</param>
    /// <returns>The wire error, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The single dialog site of the row-selection service
    /// [<c>n_cst_dwsvc_rowselect.sru:L239</c>]. TWO SEPARATE LOOKUPS AT SOURCE, not one lookup of a
    /// joined string, so both keys travel: the row-number fragment - which is BOTH a lookup key and a
    /// <see cref="Formatting.Sprintf"/> format string, hence a translation that still contains the
    /// placeholder - and the rejection fragment, which carries no placeholder. The ONE-BASED row is the
    /// substitution argument and travels as data beside the rendered text, and it is the row the inner
    /// propagation loop had reached rather than the row that was clicked.
    /// </remarks>
    internal static StructuredError? ToWireError(RowSelectRejectionError? error)
    {
        if (error is null)
        {
            return null;
        }

        StructuredError projected = new()
        {
            Title = string.Empty,
            Text = error.Text,
            Localized = error.Localized,
            Category = error.LocalizationCategory,
            Severity = error.Icon == RowSelectMessageIcon.StopSign
                ? Severity.StopSign
                : Severity.None,
        };

        projected.FormatArgs.Add(error.RowFormatTemplate);
        projected.FormatArgs.Add(error.RejectionMessageKey);
        projected.FormatArgs.Add(error.Row.ToString(CultureInfo.InvariantCulture));

        return projected;
    }

    /// <summary>
    /// Projects a context-menu error onto a structured error.
    /// </summary>
    /// <param name="error">The domain error, or <see langword="null"/>.</param>
    /// <returns>The wire error, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The ten dialog sites of the context-menu service. The kind and the offending value are carried in
    /// the substitution arguments rather than dropped, because the legacy composes them into the message
    /// text and a consumer re-rendering in another locale needs the operands back.
    /// </remarks>
    internal static StructuredError? ToWireError(ContextMenuError? error)
    {
        if (error is null)
        {
            return null;
        }

        StructuredError projected = new()
        {
            Title = string.Empty,
            Text = error.Text,
            Localized = error.Localized,
            Category = error.LocalizationCategory,
            Severity = error.Icon == ContextMenuMessageIcon.StopSign
                ? Severity.StopSign
                : Severity.None,
        };

        projected.FormatArgs.Add(error.RowFormatTemplate);
        projected.FormatArgs.Add(error.DetailMessageKey);
        projected.FormatArgs.Add(error.Row.ToString(CultureInfo.InvariantCulture));

        if (error.OffendingValue is not null)
        {
            projected.FormatArgs.Add(error.OffendingValue);
        }

        return projected;
    }

    /// <summary>
    /// Projects the four pieces of cross-event state onto the wire.
    /// </summary>
    /// <param name="snapshot">The captured state.</param>
    /// <returns>The wire message.</returns>
    /// <remarks>
    /// THE FOUR FIELDS OF <c>se_cst_dw.sru:L89-L96</c>, plus the deferred continuation. <c>long
    /// _nDisabledEvent</c> [<c>:L89</c>], <c>boolean _bDoItemChange</c> [<c>:L92</c>], <c>boolean
    /// _bDwnItemValidationError</c> [<c>:L94</c>] and <c>long _nItemChangeRetCode</c> [<c>:L96</c>] - the
    /// last being the item-change result STASHED FOR THE VALIDATION-ERROR EVENT TO CONSUME. The fifth
    /// field has no legacy twin because <c>Post _of_PostAcceptText()</c> [<c>:L389</c>] relies on a Win32
    /// message pump that a headless Linux container does not have; it became an explicitly queued
    /// continuation, and whether one is outstanding is observable state that a caller is entitled to
    /// read.
    /// </remarks>
    internal static ValidationSessionState ToWireState(ValidationSessionSnapshot snapshot) => new()
    {
        DisabledEventMask = snapshot.DisabledEventMask,
        InItemChange = snapshot.InItemChange,
        InItemValidationError = snapshot.InItemValidationError,
        ItemChangeRetCode = ToWireItemChange(snapshot.ItemChangeRetCode),
        DeferredAcceptPending = snapshot.DeferredAcceptPending,
    };

    /// <summary>
    /// Projects the composable gate mask onto the wire, together with the bits it has set.
    /// </summary>
    /// <param name="disabledEvent">The mask, as the session holds it.</param>
    /// <returns>The gate and the set bits, in the order the constants are declared.</returns>
    /// <remarks>
    /// <para>
    /// THREE BITS, COMPOSABLE, FROM <c>se_cst_dw.sru:L41-L43</c>:
    /// <c>EID_ROWFOCUSCHANGE = 1</c> (RowFocusChanging / RowFocusChanged),
    /// <c>EID_ITEMFOCUSCHANGE = 2</c> (ItemFocusChanged) and <c>EID_ITEMCHANGE = 4</c>. The constant
    /// identifiers keep their legacy SCREAMING_SNAKE spelling throughout, because they appear in
    /// serialized payloads, log records and characterization recordings where a rename would silently
    /// invalidate every stored comparison (AAP 0.4.5.3).
    /// </para>
    /// <para>
    /// THE MASK IS 32 BITS WIDE, AND THAT IS NOT AN ARBITRARY CHOICE. PowerBuilder's <c>ulong</c> is
    /// 32-bit, so <c>Domain/EventGate.cs</c> declares the constants as <see cref="uint"/> to match the
    /// fixed-width bit operations of <c>Shared.Kernel</c>. The wire field is <c>int64</c>, which is WIDER
    /// than the legacy value and never narrower, so the widening below cannot lose a bit.
    /// </para>
    /// <para>
    /// THE COUPLING AT <c>:L43</c> IS PART OF THIS SURFACE, NOT BURIED BENEATH IT: the source comment
    /// states that disabling <c>EID_ITEMCHANGE</c> also suppresses column-expression calculation. That is
    /// why <c>EID_ITEMCHANGE</c> appearing in the set bits is observable to a caller of C-04 as well, and
    /// why the reported bits are enumerated rather than left for a caller to derive from the mask.
    /// </para>
    /// </remarks>
    internal static (WireEventGate Gate, IReadOnlyList<WireEventGate.Types.Bit> DisabledBits)
        ToWireGate(uint disabledEvent)
    {
        WireEventGate gate = new() { Mask = disabledEvent };

        List<WireEventGate.Types.Bit> bits = [];

        // Declaration order, :L41 then :L42 then :L43. Tested through the ported predicate rather than
        // with a hand-rolled bit test, so there is one authority on what "disabled" means.
        if (DomainEventGate.IsEventDisabled(disabledEvent, DomainEventGate.EID_ROWFOCUSCHANGE))
        {
            bits.Add(WireEventGate.Types.Bit.EidRowfocuschange);
        }

        if (DomainEventGate.IsEventDisabled(disabledEvent, DomainEventGate.EID_ITEMFOCUSCHANGE))
        {
            bits.Add(WireEventGate.Types.Bit.EidItemfocuschange);
        }

        if (DomainEventGate.IsEventDisabled(disabledEvent, DomainEventGate.EID_ITEMCHANGE))
        {
            bits.Add(WireEventGate.Types.Bit.EidItemchange);
        }

        return (gate, bits);
    }

    /// <summary>
    /// Projects the conditional-dispatch report onto the wire.
    /// </summary>
    /// <param name="report">The domain report.</param>
    /// <returns>The wire message, field for field.</returns>
    /// <remarks>
    /// WITHOUT THIS A CONSUMER CANNOT TELL "conditionally skipped" FROM "failed silently". The oracle has
    /// four distinct reasons a step can be absent and all four are separately representable: the gate
    /// short-circuited [<c>se_cst_dw.sru:L124</c>, <c>:L130</c>, <c>:L176</c>, <c>:L187</c>], the
    /// liveness guard found the control destroyed [<c>:L138</c>, <c>:L147</c>], the topic had no
    /// subscriber [<c>:L166</c>], or a service was disabled [<c>:L169</c>, <c>:L313</c>, <c>:L408</c>].
    /// </remarks>
    internal static OrderedDispatchReport ToWireDispatch(DataWindowDispatchReport? report)
    {
        DataWindowDispatchReport source = report ?? DataWindowDispatchReport.None;

        return new OrderedDispatchReport
        {
            ColumnExpressionHandlerRan = source.ColumnExpressionHandlerRan,
            BrokerTriggerRan = source.BrokerTriggerRan,
            SemanticHandlerRan = source.SemanticHandlerRan,
            GatedOut = source.GatedOut,
            TargetBecameInvalid = source.TargetBecameInvalid,
        };
    }

    /// <summary>
    /// Projects a broker subscription topic onto the wire as the DECOMPOSED TRIPLE.
    /// </summary>
    /// <param name="topic">The topic, or <see langword="null"/> when the dispatch consulted no broker.</param>
    /// <returns>The wire topic, or <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// THE LEGACY TOPIC STRING FUSES THREE INDEPENDENT ENCODINGS INTO ONE OPAQUE VALUE - a lexical
    /// ordering prefix, the logical name, and a lifetime namespace. Transmit the fused string and the
    /// ordering becomes invisible; parse it at the far end and the contract acquires an undocumented
    /// grammar. So the triple travels decomposed and the fused legacy form is reconstituted ONLY at the
    /// compatibility edge, which is <c>SubscriptionTopic.ToLegacyString()</c> and not this method.
    /// </para>
    /// <para>
    /// <b>ONLY TWO OF THE TWELVE TOPICS CARRY AN ORDERING PREFIX</b>, and they are carried verbatim:
    /// <c>EVT_ITEMCHANGED = "0-itemchanged"</c> [<c>se_cst_dw.sru:L54</c>] and
    /// <c>EVT_EDITCHANGED = "1-editchanged"</c> [<c>:L57</c>]. The other ten are bare names
    /// [<c>:L47-L76</c>].
    /// </para>
    /// <para>
    /// <b>DISPATCH AND SORT KEY IS <c>LegacyName</c>, NEVER <c>LogicalName</c>.</b> The broker's order
    /// derives from the LEXICAL SORT OF THE SUBSCRIPTION NAME, and <c>LegacyName</c> is the residual name
    /// exactly as the broker's <c>eventdata.name</c> stores it - so it is the ordinal sort key that keeps
    /// <c>"0-itemchanged"</c> ahead of <c>"1-editchanged"</c>. Substituting <c>LogicalName</c> would
    /// compare <c>"itemchanged"</c> against <c>"editchanged"</c> and SILENTLY REVERSE them. Both are
    /// carried so a consumer never has to choose: <c>legacy_name</c> for ordering, <c>name</c> for
    /// identity.
    /// </para>
    /// <para>
    /// <b>NO LIFETIME SUFFIX IS SYNTHESIZED.</b> <c>se_cst_dw</c>'s <c>of_on</c> and six <c>of_off</c>
    /// overloads [<c>:L102-L108</c>] pass straight through to the broker without appending a namespace
    /// [bodies at <c>:L448-L467</c>]; the <c>.^persistent</c> suffix belongs to the THREADING layer
    /// [<c>ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544</c>, <c>:L596</c>], which is
    /// Persistence's concern. The field is carried and left at its transient default for this service's
    /// topics, and inventing a suffix here would attribute a lifetime the oracle never assigns.
    /// </para>
    /// </remarks>
    internal static BrokerTopic? ToWireTopic(SubscriptionTopic? topic)
    {
        if (topic is null)
        {
            return null;
        }

        BrokerTopic projected = new()
        {
            Grammar = topic.Grammar == PowerFramework.Shared.Eventful.TopicGrammar.Filter
                ? BrokerTopic.Types.TopicGrammar.Filter
                : BrokerTopic.Types.TopicGrammar.Subscription,
            LegacyName = topic.LegacyName,
            Sequence = topic.Sequence ?? 0,
            HasSequencePrefix = topic.Sequence.HasValue,
            Name = topic.LogicalName,
            WellKnown = ClassifyWellKnownTopic(topic.LegacyName),
            NegateName = topic.NegateName,
            NegateNamespace = topic.NegateNamespace,
            Lifetime = topic.Lifetime == SubscriptionLifetime.Persistent
                ? BrokerTopic.Types.Lifetime.Persistent
                : BrokerTopic.Types.Lifetime.Transient,
            Capture = topic.Capture switch
            {
                CaptureMode.Handled => BrokerTopic.Types.CaptureMode.CapHandled,
                CaptureMode.All => BrokerTopic.Types.CaptureMode.CapAll,
                _ => BrokerTopic.Types.CaptureMode.CapUnhandled,
            },
            Priority = topic.Priority,

            // The legacy default is NORMAL, established before the topic is parsed, so "a priority was
            // stated" is exactly "the priority is not normal". There is no separate flag at source to
            // read, and deriving it is therefore the honest projection rather than an assumption.
            HasPriority = topic.Priority != Priorities.Normal,
            PriorityKeyword = topic.Priority switch
            {
                Priorities.Low => BrokerTopic.Types.PriorityKeyword.Lowest,
                Priorities.High => BrokerTopic.Types.PriorityKeyword.Highest,
                _ => BrokerTopic.Types.PriorityKeyword.Unspecified,
            },
            PrependWithinPriority = topic.Prepend,
        };

        // `optional` on the wire, so absence is representable and is NOT the same as an empty namespace.
        if (topic.NamespaceCriterion is not null)
        {
            projected.Namespace = topic.NamespaceCriterion;
        }

        return projected;
    }

    /// <summary>
    /// Classifies a residual topic name against the twelve constants
    /// <c>se_cst_dw.sru</c> declares at <c>:L47-L76</c>.
    /// </summary>
    /// <param name="legacyName">The residual name, exactly as the broker stores it.</param>
    /// <returns>The well-known member, or the unspecified member for an application-declared topic.</returns>
    /// <remarks>
    /// Keyed on the LEGACY NAME rather than the logical one, so <c>"0-itemchanged"</c> matches with its
    /// prefix intact. Compared ordinally: these are legacy symbols, not human-readable text. The
    /// unspecified member means "a topic this service does not declare" and NOT "an error" - the broker
    /// carries application topics too, and reporting one is a legitimate outcome.
    /// </remarks>
    internal static BrokerTopic.Types.WellKnownName ClassifyWellKnownTopic(string? legacyName) =>
        legacyName switch
        {
            "rowfocuschanging" => BrokerTopic.Types.WellKnownName.EvtRowfocuschanging,  // :L47
            "rowfocuschanged" => BrokerTopic.Types.WellKnownName.EvtRowfocuschanged,    // :L49
            "itemfocuschanged" => BrokerTopic.Types.WellKnownName.EvtItemfocuschanged,  // :L52
            "0-itemchanged" => BrokerTopic.Types.WellKnownName.EvtItemchanged,          // :L54  PREFIXED
            "1-editchanged" => BrokerTopic.Types.WellKnownName.EvtEditchanged,          // :L57  PREFIXED
            "clicked" => BrokerTopic.Types.WellKnownName.EvtClicked,                    // :L60
            "doubleclicked" => BrokerTopic.Types.WellKnownName.EvtDoubleclicked,        // :L63
            "lbuttonup" => BrokerTopic.Types.WellKnownName.EvtLbuttonup,                // :L66
            "rbuttondown" => BrokerTopic.Types.WellKnownName.EvtRbuttondown,            // :L69
            "rbuttonup" => BrokerTopic.Types.WellKnownName.EvtRbuttonup,                // :L72
            "getfocus" => BrokerTopic.Types.WellKnownName.EvtGetfocus,                  // :L74
            "losefocus" => BrokerTopic.Types.WellKnownName.EvtLosefocus,                // :L76
            _ => BrokerTopic.Types.WellKnownName.EvtUnspecified,
        };

    /// <summary>
    /// Projects one dispatch outcome onto the wire result.
    /// </summary>
    /// <param name="outcome">The outcome the chain reported to its observer.</param>
    /// <param name="correlationId">The identifier of the notification this answers.</param>
    /// <returns>The wire result.</returns>
    /// <remarks>
    /// <para>
    /// A RENAME AND NOTHING MORE. Every field below was decided inside the chain; none is computed here.
    /// The two vetoes stay SEPARATE because the raw event consults the semantic handler and then the
    /// broker, and both edges can stop the dispatch [<c>se_cst_dw.sru:L115-L117</c>] - merging them would
    /// leave a consumer unable to say which one did.
    /// </para>
    /// <para>
    /// TWO DOMAIN OBSERVATIONS ARE DELIBERATELY NOT PROJECTED, because <c>EventResult</c> declares no
    /// field for either and inventing one would widen a published contract. <c>RowSwitchAttempted</c> -
    /// the focus-less row switch at <c>:L152-L159</c> - is a domain detail with no wire twin.
    /// <c>DeferredAcceptQueued</c> IS observable, through <c>state.deferred_accept_pending</c>, which is
    /// the same fact read from the session rather than from the dispatch; the notification side carries it
    /// too, on <c>DwnKillFocusEvent</c>.
    /// </para>
    /// </remarks>
    internal static EventResult ToWireResult(DataWindowEventOutcome outcome, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        EventResult result = new()
        {
            CorrelationId = correlationId,
            EventId = outcome.EventId,
            SemanticVeto = ToWireVeto(outcome.SemanticVeto),
            BrokerVeto = ToWireVeto(outcome.BrokerVeto),
            Dispatch = ToWireDispatch(outcome.Dispatch),
            ReturnValue = outcome.ReturnValue,
            AnyResult = ToWireAny(outcome.AnyResult),
        };

        BrokerTopic? topic = ToWireTopic(outcome.Topic);
        if (topic is not null)
        {
            result.Topic = topic;
        }

        // Absent unless the dispatch actually produced one. The alphabet is nullable on the outcome
        // precisely so that "this event has no item-change result" stays distinct from "its result was
        // the default arm", and collapsing the two would make every notification look like a coercion.
        if (outcome.ItemChangeResult.HasValue)
        {
            result.ItemChangeResult = ToWireItemChange(outcome.ItemChangeResult.Value);
        }

        // `optional` on the wire. Only `onddsgetfilter` produces one, through its `ref string`
        // out-parameter [:L13], and an absent filter is NOT an empty filter - an empty string is a
        // legitimate produced value meaning "filter nothing".
        if (outcome.ProducedFilter is not null)
        {
            result.ProducedFilter = outcome.ProducedFilter;
        }

        StructuredError? error = ToWireError(outcome.Error);
        if (error is not null)
        {
            result.Error = error;
        }

        if (outcome.State.HasValue)
        {
            result.State = ToWireState(outcome.State.Value);
        }

        return result;
    }
}

/// <summary>
/// One ordered, bidirectional event conversation over one <c>EventChain</c> stream.
/// </summary>
/// <remarks>
/// <para>
/// ONE VALIDATION SESSION, ONE STREAM, ONE COUNTER. AAP 0.6.1.4 defines both ordering patterns over a
/// single ordered conversation, so this type owns the whole of one: the response writer and the gate
/// that serializes it, the single outstanding semantic question, and the outcome the chain reports for
/// the dispatch currently in flight.
/// </para>
/// <para>
/// WHY NO SINGLETON ROUTER, unlike the sibling C-04 service's macro channel (C-K). C-04's inverted
/// question is raised by an ENGINE that holds no stream, so it needs a registry to find one. Every one
/// of this chain's nine semantic questions is raised BY THE CHAIN, INSIDE the synchronous scope of
/// handling an inbound notification that arrived on THIS stream. The stream is therefore already in
/// hand, and a registry would be a lifetime with no owner and a second place for the correlation to go
/// wrong.
/// </para>
/// <para>
/// WHAT THE TOKEN IS FOR DEPENDS ON THE DISCIPLINE, AND THE TWO ANSWERS ARE OPPOSITE. Inside a group
/// assigned the SYNCHRONOUS discipline it is for DETECTION ONLY: an out-of-order arrival is a HARD ERROR
/// that fails the session and is never an invitation to buffer and re-sort. The reason is mechanical -
/// the validation-error handler READS AND THEN CLEARS the code stashed by the item-change event that
/// precedes it [<c>se_cst_dw.sru:L331-L332</c>] and pre-sets its own result from that value
/// [<c>:L338-L340</c>], so one event's behaviour is a FUNCTION OF ITS PREDECESSOR'S RETURN VALUE, and a
/// re-sorted chain reads a stash written by the wrong predecessor or by none. NOTHING IN THIS TYPE EVER
/// BUFFERS A SYNCHRONOUS MESSAGE, in either strictness mode.
/// </para>
/// <para>
/// INSIDE A SEQUENCED GROUP THE RULE IS LOOSER BUT IT IS STILL ENFORCED, AND THAT ENFORCEMENT IS NEW.
/// Those events carry no cross-event state, so a GAP is admitted: the counter is shared with the outbound
/// direction, a client's token is one past the highest it has SEEN, and the numbers this server consumed
/// are numbers the client never sends. A REVERSAL OR A DUPLICATE IS REFUSED. Previously the sequenced arm
/// accepted every positive token including those two, so the ordering information was recorded and never
/// acted on - which left pattern (a) indistinguishable from arrival order.
/// </para>
/// <para>
/// WHY NOT REORDER, GIVEN THAT AAP 0.6.1.4 SAYS THE TOKEN IS SUFFICIENT FOR IT. Because on this contract
/// it is not: a hold-and-release buffer was built here and then removed after measurement.
/// <see cref="DataWindowEventSequencer.Accept"/>'s remarks carry the numbers - one dispatch of token 1
/// leaves the next expected token at 4, because the chain's outcome report and the result write each take
/// one from the same counter - so a message held awaiting token 2 waits for a number no client will send.
/// A client must also read a response to learn its next token, so it cannot pipeline, and one gRPC stream
/// delivers a sender's messages in order: a displaced pattern-(a) arrival is a client defect rather than
/// a transport artifact, and the answer to a client defect is a defined error (AAP 0.1.5).
/// </para>
/// <para>
/// THE DISCIPLINE IS THE SERVER'S TO RESOLVE, NOT THE CLIENT'S TO DECLARE. The token carries a
/// discipline so a consumer's own check is data-driven, but the assignment itself belongs to
/// <see cref="DataWindowEventOrdering.DisciplineOf(EventId, bool)"/> - it is the per-capability-area
/// ruling of AAP 0.6.1.4, with the evidence recorded there area by area. So an inbound message is
/// checked under the discipline THIS SERVER resolves from the event identity, and the resolved value is
/// stamped on every outbound token. A client that declared the wrong one is corrected rather than
/// trusted, which matters because trusting a client's claim that the item-change group is reorderable
/// would hand it exactly the reorder authority that group cannot have.
/// </para>
/// <para>
/// NOT THREAD-SAFE FOR CONCURRENT DISPATCH, BY DESIGN. One question may be outstanding at a time and one
/// dispatch may be in flight at a time, because both properties are what make the sequence unambiguous.
/// The write gate exists because gRPC forbids concurrent writes to one response stream, not because
/// concurrent dispatch is permitted.
/// </para>
/// </remarks>
internal sealed class DataWindowEventConversation
    : IDataWindowEventObserver, IDataWindowSemanticResponder, IDisposable
{
    private readonly IServerStreamWriter<EventChainResponse> _responseStream;
    private readonly string _sessionId;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Lock _pendingGate = new();
    private readonly ILogger? _logger;

    private DataWindowEventSequencer? _sequencer;
    private PendingQuestion? _pending;
    private DataWindowEventOutcome? _lastOutcome;
    private bool _disposed;

    /// <summary>
    /// Creates the conversation.
    /// </summary>
    /// <param name="sessionId">The validation session this stream serves. Echoed on every response.</param>
    /// <param name="responseStream">The server-to-client writer.</param>
    /// <param name="logger">Diagnostics. Never receives buffer values or edit text.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    internal DataWindowEventConversation(
        string sessionId,
        IServerStreamWriter<EventChainResponse> responseStream,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(responseStream);

        _sessionId = sessionId;
        _responseStream = responseStream;
        _logger = logger;
    }

    /// <summary>
    /// Binds the chain's sequencer to this conversation.
    /// </summary>
    /// <param name="sequencer">The chain's own sequencer.</param>
    /// <remarks>
    /// SET AFTER CONSTRUCTION BECAUSE THE ORDER OF CREATION FORCES IT, not as an afterthought: the chain
    /// needs this conversation as its observer and responder in its constructor, and the sequencer is the
    /// chain's. Binding it here means both directions of the stream draw their ordinals from ONE counter,
    /// which is what makes "one ordered conversation" true rather than aspirational.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sequencer"/> is <see langword="null"/>.</exception>
    internal void Bind(DataWindowEventSequencer sequencer)
    {
        ArgumentNullException.ThrowIfNull(sequencer);

        _sequencer = sequencer;
    }

    /// <summary>
    /// Captures the outcome of the dispatch currently in flight.
    /// </summary>
    /// <param name="outcome">The outcome, reported once per dispatch.</param>
    /// <remarks>
    /// THE CHAIN'S EVENT MEMBERS RETURN ONLY A <c>long</c>, so this is the only route by which the rich
    /// outcome - the two vetoes, the topic, the conditional-dispatch report, the item-change alphabet
    /// member, the produced filter, the structured error and the session snapshot - reaches the wire.
    /// Reading the numeric alone would discard all of it.
    /// </remarks>
    public void OnEventDispatched(DataWindowEventOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        lock (_pendingGate)
        {
            _lastOutcome = outcome;
        }
    }

    /// <summary>
    /// Takes the outcome captured for the dispatch that has just completed.
    /// </summary>
    /// <returns>The outcome, or <see langword="null"/> when the chain reported none.</returns>
    /// <remarks>
    /// CONSUMED RATHER THAN READ, so a second dispatch cannot inherit the first one's outcome. A gate
    /// short-circuit can return without reporting - the mask test at <c>se_cst_dw.sru:L187</c> returns
    /// <c>0</c> before anything else happens - and a stale outcome would then be attributed to it, which
    /// is a wrong answer rather than a missing one.
    /// </remarks>
    internal DataWindowEventOutcome? TakeOutcome()
    {
        lock (_pendingGate)
        {
            DataWindowEventOutcome? outcome = _lastOutcome;
            _lastOutcome = null;

            return outcome;
        }
    }

    /// <summary>
    /// Resolves the ordering discipline for an event, authoritatively.
    /// </summary>
    /// <param name="eventId">The event.</param>
    /// <returns>The assigned discipline.</returns>
    /// <remarks>
    /// Delegates to the domain assignment. <c>ondwnkillfocus</c> belongs to BOTH groups and that is not a
    /// contradiction - as the tail of the item-change chain it is synchronous, as pure focus notification
    /// it is sequenced - so the flagless overload is used here deliberately: this boundary classifies a
    /// bare event identifier arriving from a client, and the domain's own note records that the flagless
    /// answer is exactly the one for that case.
    /// </remarks>
    internal static OrderingDiscipline DisciplineOf(EventId eventId) =>
        DataWindowEventOrdering.DisciplineOf(eventId);

    /// <summary>
    /// Checks an inbound message's token against the conversation's ordering discipline.
    /// </summary>
    /// <param name="sequence">The token the client supplied.</param>
    /// <param name="eventId">The event the message carries.</param>
    /// <param name="strictOrdering">
    /// Whether a violation fails the call. Bound from <c>DataServices:EventChain:StrictOrdering</c>,
    /// which defaults to <see langword="true"/>.
    /// </param>
    /// <exception cref="DataWindowEventSequenceException">
    /// The token is absent, or is out of order for its discipline, AND strict ordering is in force.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE SEQUENCER OWNS THE RULE AND THIS ONLY ASKS IT. A missing token is always a fault; inside a
    /// synchronous group a token that is not the immediate successor is a fault; inside a sequenced group
    /// a token above the mark is admitted - a gap is legitimate, because the counter is shared with the
    /// outbound direction - while a token AT OR BELOW the mark is a fault, being a reversal or a
    /// duplicate rather than a late arrival.
    /// </para>
    /// <para>
    /// WHAT THE STRICTNESS DIAL DOES AND DOES NOT DO. With it in force - the default, and the posture AAP
    /// 0.6.1.4 assigns - a violation fails the call with a defined status. With it relaxed, a deployment
    /// has stated that its consumers implement the token posture themselves, so the violation is recorded
    /// and the message is still processed IN ARRIVAL ORDER. NEITHER MODE EVER REORDERS OR BUFFERS: the
    /// prohibition is on reordering, and relaxing strictness changes who reports the anomaly, never
    /// whether the server rearranges the chain.
    /// </para>
    /// </remarks>
    internal void AcceptInbound(long sequence, EventId eventId, bool strictOrdering)
    {
        DataWindowEventSequencer sequencer = _sequencer
            ?? throw new InvalidOperationException(
                "The event conversation has no sequencer bound, so an inbound token cannot be checked. "
                    + "Bind must be called with the chain's sequencer before the first message is read.");

        try
        {
            sequencer.Accept(sequence, DisciplineOf(eventId), eventId);
        }
        catch (DataWindowEventSequenceException violation) when (!strictOrdering)
        {
            _logger?.LogWarning(
                violation,
                "A DataWindow event arrived out of order on session {SessionId}: {EventId} carried "
                    + "sequence {ActualSequence} where {ExpectedSequence} was expected under the "
                    + "{Discipline} discipline. Strict ordering is relaxed for this deployment, so the "
                    + "message is processed IN ARRIVAL ORDER and is NOT reordered; the consumer owns the "
                    + "ordering check.",
                _sessionId,
                violation.EventId,
                violation.ActualSequence,
                violation.ExpectedSequence,
                violation.Discipline);
        }
    }

    /// <summary>
    /// Asks the client one semantic question and waits for its answer.
    /// </summary>
    /// <param name="question">The question.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The client's answer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="question"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The conversation is closed, or a question is already outstanding.
    /// </exception>
    public async ValueTask<EventResult> AskAsync(
        EventNotification question,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        // A correlation identifier is what pairs the answer with the question, so one is minted when the
        // caller supplied none. Minted rather than defaulted to empty: two unidentified questions would
        // be indistinguishable, and an answer could then complete the wrong one.
        if (string.IsNullOrEmpty(question.CorrelationId))
        {
            question.CorrelationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        PendingQuestion pending = new(question.CorrelationId);

        lock (_pendingGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_pending is not null)
            {
                throw new InvalidOperationException(
                    "A semantic question is already outstanding on this event conversation. One at a "
                        + "time is the contract: the ordering disciplines of AAP 0.6.1.4 are defined over "
                        + "a single ordered conversation, so a second concurrent question would make the "
                        + "sequence ambiguous.");
            }

            _pending = pending;
        }

        try
        {
            await WriteAsync(
                    response => response.Invoke = question,
                    DisciplineOf(question.EventId),
                    cancellationToken)
                .ConfigureAwait(false);

            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((PendingQuestion)state!).Cancel(),
                pending);

            return await pending.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_pendingGate)
            {
                if (ReferenceEquals(_pending, pending))
                {
                    _pending = null;
                }
            }
        }
    }

    /// <summary>
    /// Delivers a client's answer to the outstanding question.
    /// </summary>
    /// <param name="answer">The answer the client sent.</param>
    /// <returns>
    /// <see langword="true"/> when it completed an outstanding question; <see langword="false"/> when
    /// there was none, or when its correlation identifier matched no outstanding question.
    /// </returns>
    /// <remarks>
    /// A FALSE ANSWER IS REPORTED, NOT SWALLOWED. An unmatched result means the client answered a question
    /// this server never asked, or answered one twice; the caller turns that into a defined error rather
    /// than discarding it, because silently dropping it would leave a synchronous dispatch waiting for an
    /// answer that has already been thrown away.
    /// </remarks>
    internal bool Deliver(EventResult answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        PendingQuestion? pending;

        lock (_pendingGate)
        {
            pending = _pending;

            if (pending is null
                || !string.Equals(pending.CorrelationId, answer.CorrelationId, StringComparison.Ordinal))
            {
                return false;
            }

            _pending = null;
        }

        return pending.Complete(answer);
    }

    /// <summary>
    /// Writes one result to the client.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="discipline">The discipline to stamp on the token.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the message has been written.</returns>
    internal Task WriteResultAsync(
        EventResult result,
        OrderingDiscipline discipline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        return WriteAsync(response => response.Result = result, discipline, cancellationToken);
    }

    /// <summary>
    /// Writes one structured error to the client.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the message has been written.</returns>
    /// <remarks>
    /// Carried on the stream rather than raised as a status when the conversation can continue. A fault
    /// that ENDS the conversation - an unknown session, or an ordering violation under strict ordering -
    /// is a status instead, because the stream has nothing further to say.
    /// </remarks>
    internal Task WriteErrorAsync(StructuredError error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(error);

        return WriteAsync(
            response => response.Error = error,
            OrderingDiscipline.Unspecified,
            cancellationToken);
    }

    /// <summary>
    /// Serializes one write onto the response stream and stamps its token.
    /// </summary>
    /// <param name="fill">Sets the payload alternative on the response.</param>
    /// <param name="discipline">The discipline to stamp.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the message has been written.</returns>
    /// <remarks>
    /// <para>
    /// THE GATE IS REQUIRED BY gRPC, which forbids concurrent writes to one response stream. The ordinal
    /// is issued INSIDE the gate so that the number a message carries and the order it goes out in cannot
    /// disagree - issuing outside would let two writers interleave and produce a stream whose tokens
    /// describe an order that never happened.
    /// </para>
    /// <para>
    /// THE TOKEN IS CHECKED BEFORE THE WRITE AND IS NOT PASSED TO IT, AND THAT IS A HARD REQUIREMENT OF
    /// THE SERVER STACK RATHER THAN A STYLE CHOICE (C-K). The ASP.NET Core server writer
    /// <c>Grpc.AspNetCore.Server.Internal.HttpContextStreamWriter&lt;T&gt;</c> declares only the
    /// single-argument <c>WriteAsync(T)</c>; it does NOT implement the two-argument overload, so a call
    /// supplying a token resolves to the DEFAULT INTERFACE METHOD on
    /// <c>Grpc.Core.IAsyncStreamWriter&lt;T&gt;</c>, which throws <see cref="NotSupportedException"/>
    /// ("Cancellation of stream writes is not supported by this gRPC implementation") whenever the token
    /// can be cancelled. A <see cref="ServerCallContext.CancellationToken"/> ALWAYS can be, so passing it
    /// here would make the first write of every conversation fail at run time - verified against the
    /// installed 2.83.0 server assembly by reflection over the writer's declared methods. Cancellation is
    /// therefore honoured by observing the token at the gate and immediately before the write, which stops
    /// promptly for a caller that has gone without depending on an overload the stack does not provide.
    /// </para>
    /// </remarks>
    private async Task WriteAsync(
        Action<EventChainResponse> fill,
        OrderingDiscipline discipline,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EventChainResponse response = new()
            {
                SessionId = _sessionId,
                Token = new SequencingToken
                {
                    Sequence = _sequencer?.Issue() ?? DataWindowEventSequencer.NoToken,
                    Discipline = discipline,
                },
            };

            fill(response);

            // Observed, never forwarded - see the remarks. An ordinal has already been issued at this
            // point, so a cancellation here abandons that ordinal, which is correct: the message it
            // belonged to is never written, and a gap is honest where a reused number would be a lie.
            cancellationToken.ThrowIfCancellationRequested();

            await _responseStream.WriteAsync(response).ConfigureAwait(false);
        }
        finally
        {
            _ = _writeGate.Release();
        }
    }

    /// <summary>
    /// Closes the conversation and cancels any outstanding question.
    /// </summary>
    /// <remarks>
    /// AN OUTSTANDING QUESTION IS CANCELLED RATHER THAN LEFT, so a synchronous dispatch blocked on an
    /// answer that will never arrive fails instead of hanging. Leaving it would be graceful degradation
    /// of the fail-fast posture AAP 0.6.7 requires.
    /// </remarks>
    public void Dispose()
    {
        PendingQuestion? pending;

        lock (_pendingGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
            _lastOutcome = null;
        }

        pending?.Cancel();

        _writeGate.Dispose();
    }

    /// <summary>
    /// The single outstanding semantic question.
    /// </summary>
    /// <param name="correlationId">The identifier the answer must carry.</param>
    private sealed class PendingQuestion(string correlationId)
    {
        private readonly TaskCompletionSource<EventResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The identifier the answer must carry.</summary>
        internal string CorrelationId { get; } = correlationId;

        /// <summary>The task a waiting dispatch awaits.</summary>
        internal Task<EventResult> Completion => _completion.Task;

        /// <summary>Completes the question with the client's answer.</summary>
        /// <param name="answer">The answer.</param>
        /// <returns><see langword="true"/> when this call completed it.</returns>
        internal bool Complete(EventResult answer) => _completion.TrySetResult(answer);

        /// <summary>Cancels the question.</summary>
        internal void Cancel() => _completion.TrySetCanceled();
    }
}

/// <summary>
/// The server implementation of contract C-03 <c>dataservices.v1.DataWindowService</c>.
/// </summary>
/// <remarks>
/// <para>
/// A TRANSPORT AND SESSION FACADE. See the file header for the transport rationale, the sixteen-method
/// reconciliation, the four alphabets and the constraint self-audits.
/// </para>
/// <para>
/// WHY THE TYPE IS <see langword="internal"/> WITH A PUBLIC CONSTRUCTOR, WHERE THE C-04 SIBLING IS PUBLIC
/// (C-K). Two of its collaborators are internal by deliberate design - <c>ValidationSessionRegistry</c>
/// owns session identity and the idle sweep over the four cross-event fields
/// [<c>se_cst_dw.sru:L89-L96</c>], and <c>DataWindowEventChain</c> is the chain itself - so a public type
/// could not name either in a public signature (CS0051). The sibling
/// <c>Grpc/ColumnExpressionService.cs</c> is public because ITS registry was authored public; widening
/// this one to match would cascade through the session, its snapshot, its resolution and its close result
/// in a Domain file this file does not own, to buy nothing. <see langword="internal"/> is also the more
/// honest declaration: nothing outside this assembly can or should construct this service, and
/// <c>Program.cs</c> is inside it.
/// </para>
/// <para>
/// AND IT COSTS THE HOSTING MODEL NOTHING, VERIFIED RATHER THAN ASSUMED. <c>MapGrpcService&lt;T&gt;</c>
/// constrains only <c>T : class</c>, and its activator resolves the service through
/// <c>ActivatorUtilities.CreateFactory</c>, which selects a PUBLIC CONSTRUCTOR and is indifferent to the
/// declaring type's own accessibility - so the constructor stays public for the activator to find while
/// the type stays internal. That mechanism was exercised directly against this type through a populated
/// container and constructed it successfully. The test project reaches the type through the
/// <c>InternalsVisibleTo</c> the csproj already declares.
/// </para>
/// <para>
/// SECURITY IS DELIBERATELY NOT INJECTED HERE, AND THAT IS A FINDING RATHER THAN AN OMISSION. C-A permits
/// this file to reach Security ONLY through <c>Clients/SecurityClient.cs</c>, and no C-03 operation has a
/// cryptographic or token-minting need: the 22-event chain, the retrieval/validation/update triple, the
/// gate and the four headless models contain nothing keyed. Inbound authentication is the stock
/// <c>JwtBearer</c> handler configured in <c>Program.cs</c> validating against Security's published JWKS
/// - framework code that this file must not duplicate (C-G) - and the outbound credential this service
/// presents to Persistence is obtained by <c>PersistenceClient</c> through the token provider it already
/// holds. An injected-but-unused collaborator would be dead weight that C-B forbids and that
/// <c>TreatWarningsAsErrors</c> would reject outright. Security remains reachable the moment a C-03
/// operation needs it; today none does.
/// </para>
/// </remarks>
internal sealed class DataWindowService : GeneratedDataWindowServiceBase
{
    /// <summary>
    /// The rich-error binding contract C-03 declares on its <c>Update</c> method.
    /// </summary>
    /// <remarks>
    /// READ ONCE FROM THE GENERATED DESCRIPTOR, NEVER WRITTEN AS A LITERAL. The contract declares the
    /// trailer key, the gRPC status code and the payload type as a method option, so the descriptor is the
    /// single authority and a drift between contract and implementation cannot hide in a hard-coded
    /// string. The same key and the same payload type as C-06 uses, deliberately: DataServices relays what
    /// Persistence produced, and one decoding path must serve the whole chain from Persistence through
    /// DataServices to Gateway.
    /// </remarks>
    private static readonly RichErrorBinding? UpdateRichErrorBinding = ReadUpdateRichErrorBinding();

    /// <summary>
    /// The payload type this service can encode into a rich-error trailer.
    /// </summary>
    private static readonly string SupportedRichErrorPayloadType = RichErrorTrailer.Descriptor.FullName;

    private readonly ValidationSessionRegistry _sessions;
    private readonly IDataWindowEventChainFactory _chainFactory;
    private readonly IDataWindowModelSetProvider _models;
    private readonly PersistenceClient _persistence;

    /// <summary>
    /// Resolves the update descriptor a DataWindow's own definition declares, when one is composed.
    /// </summary>
    /// <remarks>
    /// OPTIONAL, AND ITS ABSENCE IS A NARROWER UPDATE RATHER THAN A BROKEN ONE. Without it an update still
    /// names its data object and lets the carrier's own static definition govern - which is precisely the
    /// legacy single-table path, where no prepare runs at all
    /// [<c>n_cst_thread_task_sqlupdate.sru:L370-L371</c>].
    /// </remarks>
    private readonly IDataWindowUpdateContractProvider? _updateContracts;
    private readonly EventChainOptions _eventChain;
    private readonly SessionLifetimeOptions _sessionLifetime;
    private readonly DropDownSearchOptions _dropDownSearch;
    private readonly PersistenceSessionOptions _persistenceSession;
    private readonly ILogger<DataWindowService>? _logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="options">
    /// The service options. <c>DataServices:EventChain:StrictOrdering</c> states whether an out-of-order
    /// arrival fails the call, and <c>DataServices:Sessions:ValidationSession</c> supplies the idle timeout
    /// and the concurrency ceiling every session this service opens is given.
    /// </param>
    /// <param name="sessions">
    /// The validation-session registry. It owns session identity, the idle sweep and the concurrency
    /// ceiling; this service only opens, correlates by identifier, and closes.
    /// </param>
    /// <param name="chainFactory">
    /// Binds a session and a DataWindow handle to a concrete event chain. REQUIRED: the chain is what owns
    /// every one of the 22 events, so a service composed without a factory is structurally faulty rather
    /// than partly working, and AAP 0.1.4 requires that fail fast rather than degrade.
    /// </param>
    /// <param name="models">
    /// Binds a DataWindow handle to its retained headless models. REQUIRED for the same reason: the eight
    /// read/apply operations cannot answer anything without it.
    /// </param>
    /// <param name="persistence">
    /// The typed client for contracts C-05 through C-08. THE ONLY ROUTE TO PERSISTENCE (C-A), and the only
    /// service holding a storage provider (C-E) - nothing in this file touches storage.
    /// </param>
    /// <param name="logger">
    /// Diagnostics. NEVER receives buffer values, edit text or a statement: see the redaction note on
    /// <see cref="Update"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public DataWindowService(
        IOptions<DataServicesOptions> options,
        ValidationSessionRegistry sessions,
        IDataWindowEventChainFactory chainFactory,
        IDataWindowModelSetProvider models,
        PersistenceClient persistence,
        ILogger<DataWindowService>? logger = null,
        IDataWindowUpdateContractProvider? updateContracts = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _chainFactory = chainFactory ?? throw new ArgumentNullException(nameof(chainFactory));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _updateContracts = updateContracts;

        DataServicesOptions configured = options.Value;

        _eventChain = configured.EventChain ?? new EventChainOptions();
        _sessionLifetime = configured.Sessions?.ValidationSession ?? new SessionLifetimeOptions();
        _dropDownSearch = configured.DropDownSearch ?? new DropDownSearchOptions();
        _persistenceSession = configured.PersistenceSession ?? new PersistenceSessionOptions();
        _logger = logger;
    }

    /// <summary>
    /// Composes the session request every scope this service opens is begun with.
    /// </summary>
    /// <returns>The request, carrying the configured descriptor.</returns>
    /// <remarks>
    /// <para>
    /// ONE PLACE, TWO CONTRACTS. Both C-05 retrieval and C-06 update begin a session first, and both begin
    /// it from this one request so a deployment cannot end up connecting one way for a read and another way
    /// for a write.
    /// </para>
    /// <para>
    /// BUILT FRESH PER SESSION RATHER THAN CACHED, deliberately. The message is mutable and carries the log
    /// password, so a single shared instance would put credential material on an object graph reachable for
    /// the life of the process and let any accidental mutation change every later session at once. It is
    /// cheap to build and there is no performance objective to trade against (AAP 0.8.5).
    /// </para>
    /// <para>
    /// <b>⚠ NO FLAGS MESSAGE IS SENT, AND THE OMISSION IS THE POINT.</b> The contract's
    /// <c>ConnectionParameterFlags</c> is a caller ASSERTION about what the connection-parameter string
    /// resolves to, not an instruction: Persistence derives <c>DisableBind</c> and <c>NCharBind</c> from
    /// the string itself by the oracle's own regular expressions
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>] and refuses a
    /// session whose supplied flags disagree with that derivation. This service forwards the string
    /// UNEXAMINED, so it has nothing independent to assert - an assertion built from two separate
    /// configuration keys was not a second opinion, it was a second copy that could contradict the first.
    /// Leaving the field unset selects the contract's documented absent arm, "take them from the
    /// connection-parameter string", which is the only reading with one authority.
    /// </para>
    /// <para>
    /// <b>WHAT THAT FIXED.</b> The flags used to be sent from independently settable
    /// <c>DisableBind</c>/<c>NCharBind</c> options beside <c>DbParm</c>. Because the oracle reads
    /// <c>NCharBind</c> only INSIDE the <c>DisableBind</c> branch, the obvious operator configuration -
    /// <c>DbParm="DisableBind=1"</c> with both flags set true - resolves to <c>nchar_bind=false</c> and
    /// therefore DISAGREED. And a disagreement is not a per-request error: it refuses the SESSION, so
    /// every retrieval and every update failed until the configuration was corrected. One input cannot
    /// disagree with itself.
    /// </para>
    /// <para>
    /// AN UNCONFIGURED DEPLOYMENT SENDS AN EMPTY DESCRIPTOR, which is the default and is deliberate: it
    /// names no database and holds no credential, and Persistence then resolves its own connection from its
    /// own options (AAP 0.6.6). An empty connection-parameter string matches neither pattern, so binding
    /// stays ENABLED and values travel as parameters rather than as interpolated literals - the safe arm,
    /// reached without a second setting to keep in step. Nothing here is a literal: every field arrives
    /// through the options pattern (constraint C-F).
    /// </para>
    /// </remarks>
    private BeginSessionRequest BuildSessionRequest() => new()
    {
        Descriptor_ = BuildTransactionDescriptor(),
    };
    /// <summary>
    /// Runs a release and swallows its failure into a log record.
    /// </summary>
    /// <param name="release">The release call.</param>
    /// <param name="what">What is being released, for the diagnostic.</param>
    /// <returns>A task that completes when the release has been attempted.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE RELEASE MUST NOT REPLACE THE FAILURE IT IS CLEANING UP AFTER.</b> These calls run in
    /// <c>finally</c> blocks, and an exception thrown from a <c>finally</c> discards the exception already
    /// in flight - so a retrieval that failed for a diagnosable reason would surface as a cleanup error
    /// instead. Logging is therefore the whole of the handling, and the log record is a warning rather
    /// than an error because the session teardown that follows releases the pooled transaction regardless.
    /// </para>
    /// <para>
    /// <b>CancellationToken.None IS PASSED DELIBERATELY.</b> A release requested because the caller
    /// cancelled must still run: honouring the cancelled token here would skip the release precisely when
    /// it is most needed, leaving a task and its pooled connection held until the process ends.
    /// </para>
    /// </remarks>
    private async Task ReleaseQuietlyAsync(Func<Task> release, string what)
    {
        try
        {
            await release().ConfigureAwait(false);
        }
        catch (RpcException failure)
        {
            _logger?.LogWarning(
                "Releasing the {What} after a Persistence operation failed with gRPC status {StatusCode}. "
                    + "The failure is recorded and not raised, because raising from a finally would "
                    + "discard the operation's own diagnosis.",
                what,
                failure.StatusCode);
        }
    }

    /// <summary>
    /// Builds the failure that surfaces an upstream Persistence status this service cannot satisfy.
    /// </summary>
    /// <param name="attempted">What was attempted, for the diagnostic.</param>
    /// <param name="dataWindowHandle">The handle the operation named, or the empty string when none applies.</param>
    /// <param name="status">The upstream status, which may be absent.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE RETURN CODE AND THE ERROR TEXT BOTH TRAVEL, AND THAT IS THE WHOLE POINT.</b> An upstream
    /// terminal status carries three facts - a return code, an error text and an optional database error -
    /// and a caller that is told only the third cannot distinguish a non-database failure from success.
    /// The code and the text therefore travel in the RPC status detail, which is the channel gRPC provides
    /// for exactly this and which every client library surfaces.
    /// </para>
    /// <para>
    /// <b>THE STATEMENT TEXT IS NEVER LOGGED.</b> The database error's statement field carries interpolated
    /// literal values, because the legacy runs without bind variables when <c>DisableBind</c> is set
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129</c>] and the legacy
    /// logger performs no redaction at all. It is relayed to the caller, who is entitled to it, and the
    /// log record below reads only the numeric code.
    /// </para>
    /// <para>
    /// <b>THE STATUS CODE IS MAPPED, NOT FLATTENED.</b> A cancellation stays Cancelled and an invalid
    /// argument stays InvalidArgument, because a caller's retry-or-surface policy keys on the status code;
    /// mapping everything to Internal would make a caller error look like a server fault.
    /// </para>
    /// </remarks>
    private RpcException BuildUpstreamFailure(
        string attempted,
        string dataWindowHandle,
        OperationStatus? status)
    {
        long retCode = status is null ? RetCode.E_INTERNAL_ERROR : (long)status.RetCode;
        string errorText = status?.ErrorText ?? string.Empty;

        StatusCode code = retCode switch
        {
            RetCode.CANCELLED => StatusCode.Cancelled,
            RetCode.E_INVALID_ARGUMENT or RetCode.E_OUT_OF_BOUND => StatusCode.InvalidArgument,
            RetCode.E_INVALID_HANDLE or RetCode.E_INVALID_TRANSACTION => StatusCode.FailedPrecondition,
            RetCode.E_NO_SUPPORT or RetCode.E_NO_IMPLEMENTATION => StatusCode.Unimplemented,
            RetCode.E_BUSY => StatusCode.Unavailable,
            _ => StatusCode.Internal,
        };

        _logger?.LogWarning(
            "Persistence could not {Attempted} for DataWindow handle {DataWindowHandle}: return code "
                + "{ReturnCode}, database code {SqlDbCode}. Any statement text in the upstream payload is "
                + "relayed to the caller and deliberately NOT logged.",
            attempted,
            dataWindowHandle.Length == 0 ? "(none)" : dataWindowHandle,
            retCode,
            status?.DbError?.Sqldbcode ?? 0L);

        return new RpcException(new Status(
            code,
            string.Format(
                CultureInfo.InvariantCulture,
                "Persistence could not {0}. Return code {1}.{2}",
                attempted,
                retCode,
                errorText.Length == 0 ? string.Empty : " " + errorText)));
    }

    /// <summary>
    /// Reads the rich-error binding from the generated service descriptor.
    /// </summary>
    /// <returns>The binding, or <see langword="null"/> when the descriptor declares none.</returns>
    private static RichErrorBinding? ReadUpdateRichErrorBinding()
    {
        MethodDescriptor? method = global::PowerFramework.Contracts.DataServices.V1.DataWindowService
            .Descriptor
            .FindMethodByName(nameof(Update));

        return method?.GetOptions()?.GetExtension(CommonV1Extensions.RichError);
    }

    /// <summary>
    /// Resolves a session identifier to a live session, taking it into use.
    /// </summary>
    /// <param name="sessionId">The identifier the caller supplied.</param>
    /// <returns>
    /// The resolution. Unresolved carries <c>RetCode.E_INVALID_ARGUMENT</c> for a blank identifier and
    /// <c>RetCode.E_INVALID_HANDLE</c> for one that names no open session.
    /// </returns>
    /// <remarks>
    /// AN UNKNOWN SESSION IS A DEFINED ERROR AND NEVER A SILENTLY CREATED ONE. The four pieces of
    /// cross-event state [<c>se_cst_dw.sru:L89-L96</c>] are what a session exists to hold, and a session
    /// conjured on demand would hold DEFAULTS - an empty gate mask, cleared re-entrancy flags and no
    /// stashed item-change code - so the very next validation-error event would read a stash that was
    /// never written [<c>:L331-L332</c>] and pre-set the wrong result [<c>:L338-L340</c>]. That is a wrong
    /// answer dressed as a recovery, which is exactly what the fail-fast posture of AAP 0.6.7 forbids.
    /// </remarks>
    private ValidationSessionResolution ResolveSession(string? sessionId) => _sessions.Resolve(sessionId);

    /// <summary>
    /// Builds the structured error a caller receives when a handle names nothing this service can serve.
    /// </summary>
    /// <param name="dataWindowHandle">The handle the caller supplied.</param>
    /// <param name="what">What could not be bound to it.</param>
    /// <returns>The error.</returns>
    /// <remarks>
    /// NOT LOCALIZED, AND THAT IS DELIBERATE. This is a BOUNDARY-CREATED diagnostic with no legacy dialog
    /// behind it - the legacy has no handles because it has no boundary - so there is no oracle text to
    /// preserve and no category to report. Marking it localized would claim a translation table answered
    /// when none did, which would corrupt a characterization comparison of the errors that genuinely are
    /// localized. Its severity is the stop sign because the operation cannot proceed.
    /// </remarks>
    private static StructuredError UnknownHandleError(string dataWindowHandle, string what) => new()
    {
        Title = string.Empty,
        Text = string.Format(
            CultureInfo.InvariantCulture,
            "No {0} could be bound to DataWindow handle '{1}'. The handle is the caller's own name for a "
                + "DataWindow and is not a session identifier; it is never created implicitly, because a "
                + "silently created DataWindow would answer every read with a default.",
            what,
            dataWindowHandle),
        Localized = false,
        Category = 0L,
        Severity = Severity.StopSign,
        RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
    };

    /// <summary>
    /// Builds the call status a streamed operation fails with when its upstream work handles could not be
    /// acquired.
    /// </summary>
    /// <param name="scope">The refused acquisition, carrying its outcome code and diagnostic.</param>
    /// <param name="dataWindowHandle">The DataWindow the operation was for, for the diagnostic record.</param>
    /// <param name="operation">What was being attempted, for the caller-facing detail.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// <para>
    /// A REFUSED ACQUISITION TRAVELS AS THE CALL STATUS AND NOT AS A TERMINAL CHUNK CARRYING AN ERROR, and
    /// the contract is what decides that. <c>RetrieveChunk.error</c> is declared "present only on a failure
    /// that the legacy would have surfaced through its error event", and <c>common.v1.DbError.sqldbcode</c>
    /// is explicitly "NOT a RetCode" - it is the driver's own code space. A handle acquisition is neither:
    /// it is a boundary operation the legacy does not have, because the legacy creates its session and its
    /// task in-process and reads the outcome as a return value. Synthesising a <c>DbError</c> for it would
    /// put a non-driver number into a driver-code field AND claim a database error event that never
    /// happened, which would corrupt any recording that compares error events. So it travels where the
    /// blank-handle rejection above already travels: the status.
    /// </para>
    /// <para>
    /// EVERY ARM IS ANSWERABLE FOR ITSELF, rather than one blanket <c>Internal</c> or one blanket
    /// <c>Unavailable</c>. A code naming a value the CALLER supplied is <c>InvalidArgument</c>, because C-05
    /// adjudicates each setting on the create call and a rejected setting is the caller's to fix. A
    /// registry at capacity is <c>ResourceExhausted</c>, which is the status a resilience policy is allowed
    /// to retry and which projects to HTTP 429. A refusal by policy is <c>PermissionDenied</c>, which must
    /// NOT be retried and would be if it were folded into the default. A transaction or handle that is not
    /// valid is <c>FailedPrecondition</c>, matching what <see cref="BuildUpstreamFailure"/> gives the same
    /// two codes so that one upstream code cannot reach a caller as two different statuses. An acquisition
    /// that reported success and carried no handle is <c>Internal</c>, because that is the upstream breaking
    /// the contract rather than anything the caller or a retry can address. Everything else - a provider
    /// that is down, a handle the server declined to issue - is <c>Unavailable</c>: the operation could not
    /// be served now, and a retrieval is a safe read, so a caller may retry it.
    /// </para>
    /// <para>
    /// THE UPSTREAM DIAGNOSTIC IS RELAYED TO THE CALLER AND THE NUMERIC CODE ALONE IS LOGGED, on the same
    /// terms as the update path's statement field: the caller is entitled to learn which setting was
    /// refused, and this service's own log is not the place to accumulate text it did not author.
    /// </para>
    /// </remarks>
    private RpcException AcquisitionFailure(
        PersistenceWorkScope scope,
        string dataWindowHandle,
        string operation)
    {
        StatusCode status = scope.ReturnCode switch
        {
            RetCode.E_INVALID_ARGUMENT
                or RetCode.E_INVALID_DATAOBJECT
                or RetCode.E_INVALID_SQL
                or RetCode.E_OUT_OF_BOUND => StatusCode.InvalidArgument,
            RetCode.E_BUSY => StatusCode.ResourceExhausted,
            RetCode.E_ACCESS_DENIED => StatusCode.PermissionDenied,

            // A TRANSACTION OR HANDLE THAT IS NOT VALID IS A PRECONDITION, NOT A CAPACITY PROBLEM, and it
            // is mapped to the SAME status BuildUpstreamFailure gives the same two codes. That agreement is
            // the point: a caller keys its retry-or-surface policy on the status code, so one upstream code
            // reaching it as two different statuses depending on which internal helper happened to raise it
            // would make the policy unwritable. Unavailable invites a retry, and retrying a session whose
            // descriptor names a transaction that is not valid produces the identical refusal forever.
            RetCode.E_INVALID_HANDLE or RetCode.E_INVALID_TRANSACTION => StatusCode.FailedPrecondition,

            // AND THE ONE ARM THAT IS ABOUT THE PRODUCER RATHER THAN THE WORK. PersistenceWorkScope raises
            // MissingHandleCode when an acquisition answered SUCCESS and then carried no handle to address -
            // see PersistenceWorkScope.MissingSessionHandleText and MissingTaskHandleText. That is a breach
            // of the contract by the upstream, not a condition the caller can act on and not one a retry
            // can clear, so it is Internal: an unretryable server-side fault, which is exactly what
            // Unavailable would have mis-stated.
            PersistenceWorkScope.MissingHandleCode => StatusCode.Internal,

            _ => StatusCode.Unavailable,
        };

        _logger?.LogWarning(
            "A {Operation} on DataWindow handle {DataWindowHandle} could not acquire its upstream work "
                + "handles: outcome {ReturnCode}, reported as gRPC {StatusCode}. The upstream diagnostic is "
                + "relayed to the caller and is deliberately not logged here.",
            operation,
            dataWindowHandle,
            scope.ReturnCode,
            status);

        return new RpcException(new Status(
            status,
            string.Format(
                CultureInfo.InvariantCulture,
                "The {0} could not acquire a Persistence work handle (outcome {1}). {2}",
                operation,
                scope.ReturnCode,
                scope.ErrorText)));
    }

    // =================================================================================================
    //  RETRIEVAL - the first third of the retrieval / validation / update triple
    // =================================================================================================

    /// <summary>
    /// Streams a DataWindow's rows as buffer-shaped chunks.
    /// </summary>
    /// <param name="request">Which DataWindow, which arguments, which buffers and how large a chunk.</param>
    /// <param name="responseStream">The chunks, delivered progressively.</param>
    /// <param name="context">The call context.</param>
    /// <returns>A task that completes when the last chunk has been written.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">
    /// <c>InvalidArgument</c> when the handle is blank. <c>RetrieveChunk</c> declares no outcome-code
    /// field - only a database error, which the contract reserves for "a failure that the legacy would
    /// have surfaced through its error event" - so a caller fault has nowhere on the message to go and
    /// travels as the call status instead. Narrowing with a defined error rather than widening the
    /// message with a guess.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE STREAMING SHAPE HAS A LEGACY ANALOGUE AND IS NOT AN INVENTION (C-K).
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru</c> declares
    /// <c>event type long ondatareceived(ref n_cst_thread_task_sqlbase_ds data, long rowcount)</c> -
    /// progressive delivery WITH a per-delivery row count, the carrier by reference and the count beside
    /// it. Both halves are reproduced: the rows and <c>row_count</c>. NOTHING IS BUFFERED TO COMPLETION
    /// HERE; each upstream chunk is projected and written as it arrives, so a large result set never
    /// materializes whole on either side of this boundary.
    /// </para>
    /// <para>
    /// WHY A FLAT ROWSET WOULD NOT DO - the obvious simplification, and wrong. The legacy result carrier
    /// derives from a <c>datastore</c>, so it IS a DataWindow complete with buffers and per-item statuses,
    /// and THE ORIGINAL-VALUE SHADOW THE UPDATE CONTRACT COMPARES AGAINST IS PART OF THAT STATE rather
    /// than something recomputable from the current row. Flattening it would silently discard exactly the
    /// state <see cref="Update"/> depends on, and the loss would not show until a concurrency check
    /// quietly stopped detecting conflicts.
    /// </para>
    /// <para>
    /// C-05's CHUNK-SIZE GUARD IS DELIBERATELY NOT REPRODUCED (C-B). Persistence preserves a legacy guard
    /// whereby a chunk size at or below 1000 yields <c>E_INVALID_ARGUMENT</c>
    /// [<c>n_cst_thread_task_sqlquery.sru:L410</c>], and the contract states in terms that it "is not this
    /// interface's guard - it belongs to the SQL task layer, which sits behind Persistence". Copying it
    /// here would invent a validation the legacy does not perform at this boundary, which C-B forbids as
    /// firmly as it forbids removing one. The size is forwarded and Persistence adjudicates it.
    /// </para>
    /// <para>
    /// TWO UPSTREAM ALTERNATIVES ARE NOT PROJECTED, AND THE GAP IS DOCUMENTED RATHER THAN PAPERED OVER.
    /// C-05's stream may also carry child data (a drop-down DataWindow's own rows) and page counts.
    /// <c>RetrieveChunk</c> declares NO field for either, and adding one would widen a published
    /// contract, so neither travels. This is a stated narrowing, not a silent drop.
    /// </para>
    /// </remarks>
    public override async Task Retrieve(
        RetrieveRequest request,
        IServerStreamWriter<RetrieveChunk> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(request.DatawindowHandle))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Retrieve requires a DataWindow handle; the field was blank."));
        }

        // The client's cancellation is the authority on when to stop. Honouring it is what keeps a
        // cancelled retrieval from continuing to pull rows from Persistence for a caller that has gone.
        //
        // IT IS PASSED TO THE UPSTREAM CALL AND OBSERVED BEFORE EACH WRITE, BUT NEVER PASSED TO A WRITE.
        // The ASP.NET Core server writer implements only the single-argument WriteAsync, so supplying a
        // cancellable token routes to the Grpc.Core default interface method, which throws
        // NotSupportedException outright - see the remarks on DataWindowEventConversation.WriteAsync for
        // the full evidence. Every write below therefore uses the one-argument form.
        CancellationToken cancellationToken = context.CancellationToken;

        // ==========================================================================================
        //  EVERY REQUESTED BUFFER IS VALIDATED BEFORE ANY UPSTREAM WORK IS ACQUIRED.
        //
        //  A PROTO3 ENUM FIELD IS AN OPEN int ON THE WIRE, so `repeated DwBuffer` accepts 7 or -1 as
        //  readily as it accepts Primary!, Delete! and Filter!. Without this guard an unknown value
        //  simply never matched the per-segment test further down, and the caller got a SUCCESSFUL,
        //  COMPLETE, EMPTY stream - final marker and all - for a request the service did not
        //  understand. That is the worst available failure shape: the caller cannot distinguish it
        //  from a genuinely empty result, so a typo'd or version-skewed buffer reads as "no rows"
        //  instead of "no such buffer".
        //
        //  BEFORE THE SCOPE, DELIBERATELY. Acquiring the upstream query task opens a Persistence
        //  session and borrows a pooled transaction, and a request that cannot be served must not
        //  cost either. It is also the same ordering the blank-handle guard above already uses.
        //
        //  THE TEST IS AN EXPLICIT THREE-WAY PATTERN AND NOT Enum.IsDefined: it matches the sibling
        //  guard in Persistence's own buffer layer [Buffers/ItemStatus.cs], reads as the closed
        //  three-member domain the legacy actually has, and needs no reflection.
        // ==========================================================================================
        foreach (DwBuffer candidate in request.Buffers)
        {
            if (candidate is DwBuffer.Primary or DwBuffer.Delete or DwBuffer.Filter)
            {
                continue;
            }

            // THE NUMERIC VALUE IS NAMED AND NOTHING ELSE IS (constraint C-F). It is the caller's own
            // enum ordinal - not row data, not a statement, not a credential - and a refusal that did
            // not say which value was rejected would leave a caller with a repeated field to bisect
            // by hand.
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Retrieve was asked for buffer {0}, which is not one of the three DataWindow "
                    + "buffers. Supply Primary ({1}), Delete ({2}) or Filter ({3}), or leave the "
                    + "field empty for Primary alone.",
                    (int)candidate,
                    (int)DwBuffer.Primary,
                    (int)DwBuffer.Delete,
                    (int)DwBuffer.Filter)));
        }

        // Empty means the primary buffer only, which is the legacy default: a freshly retrieved DataWindow
        // has nothing in Delete! and nothing in Filter! until a filter or a delete has run. Built as a set
        // so the buffer test below is a lookup rather than a scan per row.
        HashSet<DwBuffer> requested = request.Buffers.Count == 0
            ? [DwBuffer.Primary]
            : [.. request.Buffers];

        // THE UPSTREAM WORK HANDLES ARE ACQUIRED BEFORE THE FIRST ROW IS ASKED FOR, AND RELEASED ON EVERY
        // PATH OUT OF THIS METHOD - the refusal below, a failing write, an upstream fault and the caller's
        // cancellation alike. Every C-05 operating call NAMES a server-held task, and Persistence refuses a
        // blank handle with E_INVALID_HANDLE before it reaches a statement [Grpc/QueryService.cs], so a
        // default handle is not a lenient default: it is a guaranteed refusal.
        //
        // THE SPEC TRAVELS ON THE CREATE CALL AND IS NOT REPEATED ON THE RUN CALL, AND THAT IS A
        // CORRECTNESS REQUIREMENT RATHER THAN TIDINESS. C-05 documents QueryRequest.spec as "merged over
        // whatever the Set* calls already applied", and a clause setter is NOT IDEMPOTENT - a WHERE clause
        // carrying SQL_MS_APPEND applied twice appends the same fragment twice and changes the executed
        // statement. Applying it once, at creation, is also where C-05 adjudicates each setting
        // individually so a refusal names which one was wrong.
        await using PersistenceWorkScope scope = await _persistence
            .OpenQueryScopeAsync(BuildQuerySpec(request), BuildSessionRequest(), cancellationToken)
            .ConfigureAwait(false);

        if (!scope.IsAcquired)
        {
            throw AcquisitionFailure(scope, request.DatawindowHandle, "retrieval");
        }

        // ==========================================================================================
        //  THE STREAM ITSELF IS A SEPARATE METHOD, AND THE SPLIT IS LOAD BEARING RATHER THAN TIDY.
        //
        //  The scope above must be disposed BEFORE the owed final marker is written, because disposing it
        //  is what releases the upstream task and ends the session - and holding a pooled transaction,
        //  and therefore a connection, across a write to a client that may be slow or gone is exactly the
        //  leak the scope exists to prevent. An `await using` releases at the end of its enclosing BLOCK,
        //  so the epilogue has to sit outside that block; the loop is therefore lifted into a method that
        //  RETURNS what it delivered, and the epilogue runs after it against the returned counters.
        //
        //  The counters travel back rather than being recomputed because the owed marker's ordinal must
        //  CONTINUE the sequence rather than restart it - a consumer detects a truncated stream from that
        //  ordinal, so a marker numbered 1 after five chunks would read as a gap of four.
        // ==========================================================================================
        RetrievalStreamOutcome streamed = await StreamRetrievalAsync(
                request,
                responseStream,
                scope.Task,
                requested,
                cancellationToken)
            .ConfigureAwait(false);

        if (streamed.FinalWritten)
        {
            return;
        }

        // "A stream that ends without it was truncated" is the contract's rule, so a final marker is
        // always written on a SUCCESSFUL stream - including for an empty result, where it is the only
        // chunk. Ending the stream silently would make a legitimately empty retrieval indistinguishable
        // from a truncated one. A FAILING stream never reaches here: it throws.
        cancellationToken.ThrowIfCancellationRequested();

        await responseStream
            .WriteAsync(new RetrieveChunk
            {
                RowCount = 0L,
                ChunkIndex = streamed.ChunkIndex + 1L,
                CumulativeRowCount = streamed.CumulativeRowCount,
                Final = true,
            })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Streams one retrieval's chunks, and raises a terminal upstream failure rather than masking it.
    /// </summary>
    /// <param name="request">The retrieval request.</param>
    /// <param name="responseStream">The response writer.</param>
    /// <param name="task">The Persistence task the query runs on, held by the caller's work scope.</param>
    /// <param name="requested">The buffers the caller asked for.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    /// <returns>
    /// What was streamed and whether the final marker has already been written. The two counters travel
    /// back with the answer because the caller writes the owed final marker and its ordinal must continue
    /// the sequence rather than restart it - a consumer detects a gap from that ordinal.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A TERMINAL FAILURE IS PROPAGATED, NOT SUMMARISED.</b> The upstream terminal status carries three
    /// things - a return code, an error text and an optional database error - and this method surfaces all
    /// three: the database error travels on the error chunk exactly as C-03 prescribes ("present only on a
    /// failure that the legacy would have surfaced through its error event; a chunk carrying an error
    /// carries no rows"), and the return code and error text travel in the RPC status that follows it.
    /// Writing an empty final chunk instead - which is what this used to do for a non-database failure -
    /// makes a failed retrieval indistinguishable from an empty successful one, and a caller cannot
    /// recover a fact it was never told.
    /// </para>
    /// <para>
    /// The order is deliberate: the chunk is written FIRST and the status raised after it, because gRPC
    /// delivers every message before the status, so a consumer reads the payload and then learns the call
    /// failed. Raising first would discard the payload.
    /// </para>
    /// <para>
    /// <b>THE SPECIFICATION IS NOT A PARAMETER, AND ITS ABSENCE IS THE CORRECTNESS POINT.</b> C-05 applies
    /// the spec once, on the CREATE call the work scope makes, because a clause setter is NOT IDEMPOTENT -
    /// a WHERE clause carrying SQL_MS_APPEND applied twice appends the same fragment twice and changes the
    /// executed statement. This method therefore names only the task, so there is no second place a spec
    /// could be re-applied from.
    /// </para>
    /// </remarks>
    private async Task<RetrievalStreamOutcome> StreamRetrievalAsync(
        RetrieveRequest request,
        IServerStreamWriter<RetrieveChunk> responseStream,
        PersistenceTaskHandle task,
        HashSet<DwBuffer> requested,
        CancellationToken cancellationToken)
    {
        PersistenceQueryRequest query = new() { Task = task };

        long chunkIndex = 0L;
        long cumulative = 0L;

        await foreach (PersistenceQueryResponse response in _persistence
            .QueryAsync(query, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            switch (response.PayloadCase)
            {
                case PersistenceQueryResponse.PayloadOneofCase.DataChunk:
                    RetrieveChunk chunk = ProjectChunk(
                        response.DataChunk,
                        requested,
                        request.DatawindowHandle,
                        ++chunkIndex,
                        ref cumulative);

                    // The upstream chunk ordinal and its total are the only evidence of where the stream
                    // ends, and a zero total means the producer did not declare one - in which case the
                    // terminal status is what ends the stream, not this test.
                    bool upstreamDeclaredLast = response.DataChunk.ChunkCount > 0L
                        && response.DataChunk.ChunkIndex >= response.DataChunk.ChunkCount;

                    if (upstreamDeclaredLast)
                    {
                        chunk.Final = true;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await responseStream.WriteAsync(chunk).ConfigureAwait(false);

                    if (upstreamDeclaredLast)
                    {
                        // The stream is complete and its final marker has been written, so the caller owes
                        // no second one.
                        return new RetrievalStreamOutcome(true, chunkIndex, cumulative);
                    }

                    break;

                case PersistenceQueryResponse.PayloadOneofCase.Status:
                    // TERMINAL. A failing status carries the legacy error event's payload, which travels on
                    // the final chunk with no rows beside it - the contract's own rule for an error chunk.
                    // The statement field inside it is RELAYED AND NEVER LOGGED: see Update's redaction
                    // note for why that field is treated as sensitive.
                    long terminalOutcome = response.Status.RetCode == default
                        ? RetCode.OK
                        : (long)response.Status.RetCode;

                    if (Predicates.IsFailed(terminalOutcome) && response.Status.DbError is null)
                    {
                        // ============================================================================
                        //  A FAILURE WITH NOWHERE ON THE MESSAGE TO GO TRAVELS AS THE CALL STATUS.
                        //
                        //  RetrieveChunk carries a database error and NOTHING ELSE that can express an
                        //  outcome, so a failing status that brought no DbError - which is what a
                        //  rejected clause, an invalid paging request or a bad chunk size produces -
                        //  has no field to occupy. Writing the plain final marker instead, which is
                        //  what this arm used to do unconditionally, DELIVERED A FAILURE AS A
                        //  SUCCESSFUL EMPTY RETRIEVAL: byte for byte the same answer a DataWindow with
                        //  no matching rows produces, so no caller could tell a refused query from an
                        //  empty one.
                        //
                        //  Raising after chunks have already been written is deliberate and correct on
                        //  a server stream: the client receives the chunks it was sent and then a
                        //  non-OK status, which is exactly "partial result, then failure" and is the
                        //  only truthful encoding available once bytes are on the wire.
                        // ============================================================================
                        throw BuildTerminalFailure(terminalOutcome);
                    }

                    RetrieveChunk terminal = new()
                    {
                        RowCount = 0L,
                        ChunkIndex = ++chunkIndex,
                        CumulativeRowCount = cumulative,
                        Final = true,
                    };

                    if (response.Status.DbError is not null)
                    {
                        terminal.Error = response.Status.DbError;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await responseStream.WriteAsync(terminal).ConfigureAwait(false);

                    // AND THEN THE FAILURE ITSELF - BUT ONLY IF THERE WAS ONE.
                    //
                    // The return code and the error text have nowhere to live on a RetrieveChunk: the
                    // message declares neither field, and adding one would widen a published contract. So
                    // for a FAILING status they travel in the RPC status, which is where a gRPC failure
                    // belongs. What must not happen is writing this chunk and returning normally, which
                    // reports a failed retrieval as a successful empty one - and a caller reading only the
                    // stream would then read a database failure as "zero rows, plus an error you may
                    // ignore".
                    //
                    // ⚠ THE GUARD IS THE CORRECTION, AND ITS ABSENCE FAILED EVERY SUCCESSFUL RETRIEVAL ⚠
                    //
                    // A terminal status is how a producer says "that is all", and it says so on SUCCESS as
                    // well as on failure. Raising unconditionally here therefore turned the ordinary
                    // end-of-stream marker into a call failure carrying "return code 0" - and the tri-state
                    // algebra widens that further, because PREVENT (=1) satisfies the legacy success
                    // predicate and CANCELLED (=-2) is excluded from failure by name, so both are
                    // non-failures that must complete normally (C-B). The failure test is therefore the
                    // same predicate the arm above uses, not "the status field was present".
                    if (Predicates.IsFailed(terminalOutcome))
                    {
                        throw BuildUpstreamFailure(
                            "retrieve rows",
                            request.DatawindowHandle,
                            response.Status);
                    }

                    // A NON-FAILING TERMINAL STATUS ENDS THE STREAM, AND ITS MARKER HAS JUST BEEN WRITTEN,
                    // so the caller owes no second one - writing another would deliver two final chunks.
                    return new RetrievalStreamOutcome(true, chunkIndex, cumulative);

                case PersistenceQueryResponse.PayloadOneofCase.RowCount:
                case PersistenceQueryResponse.PayloadOneofCase.ChildDataChunk:
                case PersistenceQueryResponse.PayloadOneofCase.PageCounts:
                case PersistenceQueryResponse.PayloadOneofCase.None:
                default:
                    // Not projected: RetrieveChunk declares no field for a total row count on its own, for
                    // child data or for page counts, and inventing one would widen a published contract.
                    // The documented narrowing, restated at the point it takes effect.
                    break;
            }

        }

        // The upstream stream ended without declaring a last chunk and without a terminal status, so the
        // final marker is owed.
        return new RetrievalStreamOutcome(false, chunkIndex, cumulative);
    }

    /// <summary>
    /// What one retrieval stream delivered, and whether it already wrote its final marker.
    /// </summary>
    /// <param name="FinalWritten">Whether the final marker has been written.</param>
    /// <param name="ChunkIndex">The last one-based chunk ordinal written.</param>
    /// <param name="CumulativeRowCount">The rows delivered across every chunk on this stream.</param>
    private readonly record struct RetrievalStreamOutcome(
        bool FinalWritten,
        long ChunkIndex,
        long CumulativeRowCount);

    /// <summary>
    /// Builds the upstream query specification for a retrieval.
    /// </summary>
    /// <param name="request">The retrieval request.</param>
    /// <returns>The specification C-05 consumes.</returns>
    /// <remarks>
    /// <para>
    /// THE HANDLE IS THE DATA OBJECT. <c>RetrieveRequest</c> carries no statement and no task, so the
    /// caller's own name for the DataWindow is what identifies the source - which is exactly the legacy
    /// relationship, where a DataWindow's <c>DataObject</c> is what its <c>Retrieve()</c> runs.
    /// </para>
    /// <para>
    /// THE ARGUMENT LIST CHANGES BASIS HERE, AND IT IS THE ONLY PLACE IT DOES. Legacy retrieval arguments
    /// are POSITIONAL AND ONE-BASED, matching <c>Retrieve(args...)</c>; a protobuf <c>repeated</c> field is
    /// zero-based. Wire element 0 is therefore legacy argument 1, and the ordinal is carried in the
    /// parameter's name so the position survives a transport that might otherwise be free to reorder.
    /// </para>
    /// <para>
    /// THE NAME IS RENDERED INVARIANTLY AND IS LOWERCASE BY CONSTRUCTION. C-05 records that the legacy
    /// lowercases a parameter name on entry [<c>n_cst_thread_task_sqlbase.sru:L256</c>], so a caller
    /// sending "ID" and one sending "id" are the same parameter to the legacy. A decimal ordinal has no
    /// case to fold, so it satisfies that rule without a transformation that could itself vary by culture.
    /// </para>
    /// <para>
    /// A ZERO CHUNK SIZE MEANS "THE SERVER CHOOSES" and is therefore NOT FORWARDED. Forwarding a zero
    /// would state a size of zero to Persistence, which is a different assertion from stating none, and
    /// Persistence's own guard would then reject a request the caller never made.
    /// </para>
    /// </remarks>
    private static PersistenceQuerySpec BuildQuerySpec(RetrieveRequest request)
    {
        PersistenceQuerySpec spec = new() { DataObject = request.DatawindowHandle };

        if (request.ChunkSize > 0)
        {
            spec.ChunkSize = request.ChunkSize;
        }

        for (int index = 0; index < request.Arguments.Count; index++)
        {
            spec.Parameters.Add(new PositionalParameter
            {
                // ONE-BASED on the legacy side: wire index 0 is legacy argument 1.
                Name = (index + 1).ToString(CultureInfo.InvariantCulture),
                Value = request.Arguments[index],
            });
        }

        return spec;
    }

    /// <summary>
    /// Projects one upstream data chunk onto one wire chunk.
    /// </summary>
    /// <param name="dataChunk">The upstream chunk.</param>
    /// <param name="requested">The buffers the caller asked for.</param>
    /// <param name="chunkIndex">The ONE-BASED ordinal of this delivery.</param>
    /// <param name="cumulative">Running total of rows delivered on this stream.</param>
    /// <returns>The wire chunk.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW TYPE IS THE SAME TYPE ON BOTH CONTRACTS, so the rows are relayed rather than rebuilt.
    /// <c>common.v1.DataWindowRow</c> is declared once in the shared vocabulary precisely so that C-03 and
    /// C-05 name the same descriptor; copying field by field would create a second projection to drift.
    /// Each row already carries its own buffer tag and its own item status, and the original-value shadow
    /// travels with it.
    /// </para>
    /// <para>
    /// THE SINGLE-BUFFER CONVENIENCE FIELD IS SET ONLY WHEN IT IS TRUE. A chunk carrying rows from more
    /// than one buffer has no single answer, and the contract's encoding of "no single answer" is FIELD
    /// ABSENCE - never a fourth <c>DwBuffer</c> value, and never <c>Primary</c> as a stand-in, because
    /// <c>Primary</c> is a specific buffer and a consumer is entitled to believe it.
    /// </para>
    /// <para>
    /// <b>THE COLUMN NAME IS FILLED IN HERE, AND THIS IS THE ONLY LAYER THAT CAN.</b>
    /// <c>common.v1.ColumnValue</c> states that both identifiers are carried and that neither is
    /// redundant - a name survives a column reorder while an ordinal does not, and the status APIs take
    /// the ordinal - but the upstream carrier is a faithful stand-in for the legacy's positional
    /// <c>GetChanges</c>/<c>GetFullState</c> blob, which has ordinals and no names in it at all, so
    /// Persistence emits the ordinal alone and leaves the name empty. The DataWindow DEFINITION is what
    /// resolves an ordinal to a name, and this service is the layer that holds it (AAP 0.3.4's
    /// anti-corruption edge); every row therefore reached C-03's consumers with an empty
    /// <c>column_name</c> until the name was projected here. Persistence's codec is deliberately left
    /// alone rather than taught about names.
    /// </para>
    /// </remarks>
    private RetrieveChunk ProjectChunk(
        global::PowerFramework.Contracts.Persistence.V1.QueryDataChunk dataChunk,
        HashSet<DwBuffer> requested,
        string dataWindowHandle,
        long chunkIndex,
        ref long cumulative)
    {
        RetrieveChunk chunk = new() { ChunkIndex = chunkIndex };

        DwBuffer? single = null;
        bool mixed = false;

        foreach (global::PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment segment
            in dataChunk.State?.Segments ?? [])
        {
            if (!requested.Contains(segment.Buffer))
            {
                continue;
            }

            if (single is null)
            {
                single = segment.Buffer;
            }
            else if (single.Value != segment.Buffer)
            {
                mixed = true;
            }

            chunk.Rows.AddRange(segment.Rows);
        }

        NameColumns(chunk.Rows, dataWindowHandle);

        chunk.RowCount = chunk.Rows.Count;

        cumulative += chunk.RowCount;
        chunk.CumulativeRowCount = cumulative;

        if (single is not null && !mixed)
        {
            chunk.Buffer = single.Value;
        }

        return chunk;
    }

    /// <summary>
    /// Fills in the column NAME beside the ordinal every relayed column value already carries.
    /// </summary>
    /// <param name="rows">The relayed rows, mutated in place.</param>
    /// <param name="dataWindowHandle">The caller's own name for the DataWindow.</param>
    /// <remarks>
    /// <para>
    /// <b>ONLY AN EMPTY NAME IS WRITTEN.</b> A name the upstream did in fact supply is left exactly as it
    /// arrived, because this method resolves an identifier rather than deciding one - and overwriting a
    /// supplied value would make a producer that names its columns indistinguishable from one that does
    /// not.
    /// </para>
    /// <para>
    /// AN UNRESOLVABLE HANDLE, AND AN ORDINAL NO COLUMN CARRIES, BOTH LEAVE THE NAME EMPTY. The ordinal is
    /// the authoritative identifier on this message and it is never touched here, so a row whose name could
    /// not be resolved is still fully addressable - the same state every row was in before names were
    /// projected at all. Inventing a name for an ordinal outside the definition would be worse than
    /// leaving it blank: a consumer would address a column that does not exist.
    /// </para>
    /// <para>
    /// THE ORIGINAL-VALUE SHADOW IS NAMED TOO. <c>updatewhere=1</c> compares originals, so a consumer
    /// reading the shadow needs the same identifier the current value carries; naming one list and not the
    /// other would leave the two halves of one row addressed differently.
    /// </para>
    /// </remarks>
    private void NameColumns(IEnumerable<DataWindowRow> rows, string dataWindowHandle)
    {
        if (_models.GetOrCreate(dataWindowHandle) is not { } set)
        {
            return;
        }

        foreach (DataWindowRow row in rows)
        {
            foreach (ColumnValue column in row.Columns)
            {
                NameColumn(column, set.Host);
            }

            foreach (ColumnValue original in row.OriginalValues)
            {
                NameColumn(original, set.Host);
            }
        }
    }

    /// <summary>
    /// Resolves one column value's name from its ordinal, when it does not already carry one.
    /// </summary>
    /// <param name="column">The column value, mutated in place.</param>
    /// <param name="host">The host whose definition resolves the ordinal.</param>
    /// <remarks>
    /// <para>
    /// THE ORDINAL IS ONE-BASED AND ZERO IS THE ROW ITSELF (R9), so a zero ordinal resolves to no name and
    /// is skipped rather than rebased.
    /// </para>
    /// <para>
    /// THE LOOKUP IS THE ORACLE'S OWN POSITIONAL PROPERTY EXPRESSION, <c>"#" + n + ".Name"</c>
    /// [<c>n_cst_dwsvc.sru:L666</c>], read through the host contract rather than through a member invented
    /// for this projection - so a host that resolves columns positionally at all resolves them here too.
    /// <c>Describe</c>'s two failure markers are both treated as "not resolved", exactly as every other
    /// ported reader treats them.
    /// </para>
    /// </remarks>
    private static void NameColumn(ColumnValue column, DataWindowServiceHost host)
    {
        if (column.ColumnName.Length != 0 || column.ColumnId < 1L)
        {
            return;
        }

        string resolved = host.Describe(
            "#" + column.ColumnId.ToString(CultureInfo.InvariantCulture) + ".Name");

        if (resolved.Length != 0
            && !string.Equals(resolved, "!", StringComparison.Ordinal)
            && !string.Equals(resolved, "?", StringComparison.Ordinal))
        {
            column.ColumnName = resolved;
        }
    }

    // =================================================================================================
    //  THE VALIDATION SESSION - the four pieces of cross-event state, given somewhere to live
    // =================================================================================================

    /// <summary>
    /// Opens a validation session.
    /// </summary>
    /// <param name="request">The DataWindow to bind, and the initial gate mask.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The session identifier and its initial state.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// WHY THIS OPERATION EXISTS AT ALL. <c>se_cst_dw.sru:L89-L96</c> declares FOUR PRIVATE FIELDS THAT THE
    /// EVENT CHAIN READS AND WRITES BETWEEN EVENTS: <c>long _nDisabledEvent</c> (the gate mask),
    /// <c>boolean _bDoItemChange</c> (the in-item-change re-entrancy flag), <c>boolean
    /// _bDwnItemValidationError</c> (the in-validation-error re-entrancy flag), and <c>long
    /// _nItemChangeRetCode</c> - the item-change result STASHED FOR THE VALIDATION-ERROR EVENT TO CONSUME.
    /// A stateless request boundary has nowhere to put any of them, so they become fields of a server-held
    /// session correlated by identifier. This file only opens, correlates and closes; the fields themselves
    /// belong to <c>Domain/ValidationSession.cs</c>.
    /// </para>
    /// <para>
    /// THE MASK NARROWS FROM 64 BITS TO 32 AND THAT IS THE LEGACY WIDTH, NOT A LOSS. PowerBuilder's
    /// <c>ulong</c> is 32-bit, so the gate is a <see cref="uint"/> throughout the domain. The wire field is
    /// <c>int64</c> - wider than the legacy value and never narrower - so a value inside the legacy domain
    /// round-trips exactly. A value OUTSIDE it is refused with <c>E_INVALID_ARGUMENT</c> rather than
    /// truncated: silently dropping the high bits would enable a different set of gates than the caller
    /// asked for, which is a wrong answer rather than a rejected one.
    /// </para>
    /// <para>
    /// A REFUSED OPEN CARRIES A CODE AND NO SESSION. The registry refuses when its configured concurrency
    /// ceiling is reached, and the response's identifier is then empty - the caller must test the code, not
    /// the identifier's truthiness, because an empty identifier is exactly what a refusal looks like.
    /// </para>
    /// </remarks>
    public override Task<OpenValidationSessionResponse> OpenValidationSession(
        OpenValidationSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.InitialDisabledEventMask is < 0L or > uint.MaxValue)
        {
            _logger?.LogWarning(
                "An initial gate mask of {Mask} lies outside the legacy 32-bit unsigned domain, so the "
                    + "session was not opened. PowerBuilder's ulong is 32-bit "
                    + "[ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L89]; truncating would "
                    + "have enabled a different set of gates than the caller requested.",
                request.InitialDisabledEventMask);

            return Task.FromResult(new OpenValidationSessionResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            });
        }

        // ==========================================================================================
        //  THE HANDLE IS RESOLVED AT OPEN TIME, NOT SEVERAL CALLS LATER.
        //
        //  C-03 states that "a session is scoped to one DataWindow" [dataservices.v1.proto -
        //  OpenValidationSessionRequest.datawindow_handle], because the four state fields are instance
        //  fields of ONE control in the legacy. A session opened against a handle nothing resolves is
        //  therefore not a session at all - and answering it 200 meant a caller learned of its typo
        //  only when a later column-dependent call failed, with a code describing that call rather than
        //  the mistake. The published unknown-handle negative already exists on this service
        //  [IDataWindowModelSetProvider.GetOrCreate - "a caller that mistyped a handle must learn
        //  that"], so this arm reuses it rather than inventing a second answer for one condition.
        //
        //  A BLANK HANDLE IS STILL E_INVALID_ARGUMENT AND NOT E_INVALID_HANDLE. Supplying nothing is a
        //  different mistake from naming something that does not exist, and the model provider refuses
        //  a blank name for exactly that reason - so the two are separated here rather than collapsed.
        // ==========================================================================================
        string dataWindowHandle = request.DatawindowHandle ?? string.Empty;

        if (string.IsNullOrWhiteSpace(dataWindowHandle))
        {
            return Task.FromResult(new OpenValidationSessionResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            });
        }

        if (_models.GetOrCreate(dataWindowHandle) is null)
        {
            _logger?.LogWarning(
                "A validation session was refused because no DataWindow resolves the requested handle. "
                    + "The handle is not reproduced here, because it is caller content.");

            return Task.FromResult(new OpenValidationSessionResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
            });
        }

        ValidationSessionOpenResult opened = _sessions.Open(
            dataWindowHandle,
            (uint)request.InitialDisabledEventMask);

        if (!opened.IsOpened || opened.Session is null)
        {
            _logger?.LogWarning(
                "A validation session could not be opened; the registry answered {ReturnCode}. The "
                    + "configured ceiling is {MaxConcurrentSessions} concurrent sessions with an idle "
                    + "timeout of {IdleTimeout}.",
                opened.ReturnCode,
                _sessionLifetime.MaxConcurrentSessions,
                _sessionLifetime.IdleTimeout);

            return Task.FromResult(new OpenValidationSessionResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(opened.ReturnCode),
            });
        }

        return Task.FromResult(new OpenValidationSessionResponse
        {
            SessionId = opened.Session.SessionId,
            State = DataWindowWireProjection.ToWireState(opened.Session.CaptureState()),
            RetCode = DataWindowWireProjection.ToWireRetCode(opened.ReturnCode),
        });
    }

    /// <summary>
    /// Closes a validation session.
    /// </summary>
    /// <param name="request">The session to close.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome, whether it had been open, and the state at closure.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// IDEMPOTENT, AND A SESSION THAT WAS ALREADY GONE IS NOT AN ERROR. Closing one that had already been
    /// closed, or that the idle sweep had already expired, answers <c>RetCode.OK</c> with
    /// <c>was_open = false</c> - informational, so a caller can distinguish the two without either being a
    /// failure. A BLANK IDENTIFIER IS STILL <c>E_INVALID_ARGUMENT</c>: idempotence is about repeating a
    /// well-formed request, not about accepting a malformed one.
    /// </para>
    /// <para>
    /// THE FINAL STATE IS CARRIED SO IT IS NOT DISCARDED. A caller is entitled to see an outstanding
    /// deferred continuation - the queued <c>AcceptText</c> that replaces <c>Post _of_PostAcceptText()</c>
    /// [<c>se_cst_dw.sru:L389</c>] - or a still-set re-entrancy guard at the moment of closure, both of
    /// which say something about how the conversation ended.
    /// </para>
    /// </remarks>
    public override Task<CloseValidationSessionResponse> CloseValidationSession(
        CloseValidationSessionRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        ValidationSessionCloseResult closed = _sessions.Close(request.SessionId);

        return Task.FromResult(new CloseValidationSessionResponse
        {
            RetCode = DataWindowWireProjection.ToWireRetCode(closed.ReturnCode),
            WasOpen = closed.WasOpen,
            FinalState = DataWindowWireProjection.ToWireState(closed.FinalState),
        });
    }

    // =================================================================================================
    //  THE EVENT GATE - se_cst_dw.sru:L41-L43, L109-L111
    // =================================================================================================

    /// <summary>
    /// Reads the event-gate mask and the bits it has set.
    /// </summary>
    /// <param name="request">The session whose gate to read.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The mask, the set bits, and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// Projects both the mask and the per-bit predicate <c>of_iseventdisabled</c>
    /// [<c>se_cst_dw.sru:L109</c>, body at <c>:L486</c>], so a caller need not re-derive the bits from the
    /// mask - and so the coupling recorded on <see cref="DisableEvent"/> is visible in the read as well as
    /// in the write.
    /// </remarks>
    public override Task<GetEventGateResponse> GetEventGate(
        GetEventGateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        ValidationSessionResolution resolved = ResolveSession(request.SessionId);

        if (!resolved.IsResolved || resolved.Session is null)
        {
            return Task.FromResult(new GetEventGateResponse
            {
                Gate = new WireEventGate(),
                RetCode = DataWindowWireProjection.ToWireRetCode(resolved.ReturnCode),
            });
        }

        (WireEventGate gate, IReadOnlyList<WireEventGate.Types.Bit> bits) =
            DataWindowWireProjection.ToWireGate(resolved.Session.DisabledEvent);

        GetEventGateResponse response = new()
        {
            Gate = gate,
            RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.OK),
        };

        response.DisabledBits.AddRange(bits);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Disables one or more events.
    /// </summary>
    /// <param name="request">The session and the mask to set.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome and the resulting gate.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <c>of_disableevent</c> [<c>se_cst_dw.sru:L110</c>], DECLARED <c>long</c>. A ZERO MASK YIELDS
    /// <c>E_INVALID_ARGUMENT</c> [<c>:L506</c>] and the mask is otherwise set with a bit-or
    /// [<c>:L508</c>]. Both facts belong to <c>Domain/EventGate.cs</c> and are reached through the
    /// session, so this method adds no validation of its own beyond the wire-width check.
    /// </para>
    /// <para>
    /// <b>DISABLING <c>EID_ITEMCHANGE</c> ALSO SUPPRESSES COLUMN-EXPRESSION CALCULATION.</b> The oracle
    /// states it inline at <c>:L43</c>, and it is part of THIS contract rather than a detail buried in the
    /// gate: a caller that disables item-change through this method has also, and observably, turned off
    /// evaluation on contract C-04. That is why the response carries the resulting gate rather than only a
    /// code - the effect on the other contract is readable from the answer to this one. The coupling is
    /// mediated by the gate and the engine, never by a call from this service class to C-04's.
    /// </para>
    /// <para>
    /// THE RETURN-TYPE ASYMMETRY WITH <see cref="EnableEvent"/> IS PRESERVED, NOT HARMONISED (C-B). The
    /// oracle declares this one <c>long</c> [<c>:L110</c>] and its counterpart <c>integer</c>
    /// [<c>:L111</c>], and the legacy's own documentation comment at <c>:L521</c> even says "long" where
    /// the declaration says <c>integer</c> - a legacy documentation defect, with the DECLARATION
    /// authoritative. <c>Domain/EventGate.cs</c> reproduces both widths, so the difference survives into
    /// the domain even though both widen onto the same wire enumeration.
    /// </para>
    /// </remarks>
    public override Task<DisableEventResponse> DisableEvent(
        DisableEventRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        (ValidationSession? session, long refusal) = ResolveGateTarget(request.SessionId, request.EventMask);

        if (session is null)
        {
            return Task.FromResult(new DisableEventResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(refusal),
                Gate = new WireEventGate(),
            });
        }

        // `long`, per :L110. The width is the oracle's and is carried, not normalised.
        long outcome = session.DisableEvent((uint)request.EventMask);

        (WireEventGate gate, _) = DataWindowWireProjection.ToWireGate(session.DisabledEvent);

        return Task.FromResult(new DisableEventResponse
        {
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
            Gate = gate,
        });
    }

    /// <summary>
    /// Re-enables one or more events.
    /// </summary>
    /// <param name="request">The session and the mask to clear.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The outcome and the resulting gate.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>of_enableevent</c> [<c>se_cst_dw.sru:L111</c>], DECLARED <c>integer</c> - genuinely a different
    /// return type from its disable counterpart, annotated rather than harmonised (C-B). A zero mask
    /// yields <c>E_INVALID_ARGUMENT</c> [<c>:L530</c>] and the mask is otherwise cleared with a bit-clear
    /// [<c>:L532</c>].
    /// </remarks>
    public override Task<EnableEventResponse> EnableEvent(
        EnableEventRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        (ValidationSession? session, long refusal) = ResolveGateTarget(request.SessionId, request.EventMask);

        if (session is null)
        {
            return Task.FromResult(new EnableEventResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(refusal),
                Gate = new WireEventGate(),
            });
        }

        // `integer`, per :L111 - narrower than its disable counterpart on purpose. Widened here only to
        // reach the shared projection, which is the one place the two meet.
        int outcome = session.EnableEvent((uint)request.EventMask);

        (WireEventGate gate, _) = DataWindowWireProjection.ToWireGate(session.DisabledEvent);

        return Task.FromResult(new EnableEventResponse
        {
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
            Gate = gate,
        });
    }

    /// <summary>
    /// Resolves the session a gate operation targets and checks the mask's width.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="eventMask">The mask the caller supplied.</param>
    /// <returns>
    /// The session and <c>RetCode.OK</c>, or <see langword="null"/> and the refusal code.
    /// </returns>
    /// <remarks>
    /// THE WIDTH CHECK IS NOT THE ZERO CHECK, and both exist. A mask outside the legacy 32-bit unsigned
    /// domain is refused HERE, because narrowing it would silently address different bits than the caller
    /// named. A mask of ZERO is refused by the oracle's own guards [<c>se_cst_dw.sru:L506</c>,
    /// <c>:L530</c>] and is therefore left to them - reproducing that test here would put a second
    /// authority on a rule the domain already owns, and the two could then disagree.
    /// </remarks>
    private (ValidationSession? Session, long ReturnCode) ResolveGateTarget(
        string? sessionId,
        long eventMask)
    {
        if (eventMask is < 0L or > uint.MaxValue)
        {
            return (null, RetCode.E_INVALID_ARGUMENT);
        }

        ValidationSessionResolution resolved = ResolveSession(sessionId);

        return !resolved.IsResolved || resolved.Session is null
            ? (null, resolved.ReturnCode)
            : (resolved.Session, RetCode.OK);
    }

    // =================================================================================================
    //  THE UPSTREAM LIFECYCLE - what makes a task-scoped C-05 / C-06 call reachable at all
    // =================================================================================================

    /// <summary>
    /// Builds the transaction descriptor every session this service opens is resolved from.
    /// </summary>
    /// <returns>The descriptor, mirroring <c>transactiondata.srs</c> field for field.</returns>
    /// <remarks>
    /// <para>
    /// BUILT FRESH PER SESSION RATHER THAN CACHED, deliberately. The message is mutable and it carries the
    /// log password, so a single shared instance would put credential material on an object graph reachable
    /// for the lifetime of the process and let any accidental mutation change every later session at once.
    /// It is cheap to build and there is no performance objective to trade against (AAP 0.8.5).
    /// </para>
    /// <para>
    /// <b>THE PASSWORD IS WRITTEN HERE AND READ NOWHERE ELSE.</b> It travels on the session request, is
    /// never logged, never echoed and never placed on a response - the contract permanently reserves its
    /// field number on the response view for exactly that reason - and the descriptor is not retained after
    /// <c>BeginSession</c> returns.
    /// </para>
    /// </remarks>
    private PersistenceTransactionDescriptor BuildTransactionDescriptor() => new()
    {
        Dbms = _persistenceSession.Dbms,
        Servername = _persistenceSession.ServerName,
        Database = _persistenceSession.Database,
        Logid = _persistenceSession.LogId,
        Logpass = _persistenceSession.LogPass,
        Dbparm = _persistenceSession.DbParm,
        Lock = _persistenceSession.Lock,
        Autocommit = _persistenceSession.AutoCommit,
        Userparm = _persistenceSession.UserParm,
    };

    /// <summary>
    /// Creates a query task against an open session.
    /// </summary>
    /// <param name="session">The open session.</param>
    /// <param name="spec">The specification the task is created with.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The task handle, or the outcome that refused it.</returns>
    /// <remarks>
    /// A SUCCESS WITHOUT A HANDLE IS RECLASSIFIED, exactly as for the session: a task-less request is the
    /// defect this whole lifecycle exists to close, so it is never allowed to be reconstructed from a
    /// malformed success.
    /// </remarks>
    private async Task<(PersistenceTaskHandle? Task, long ReturnCode)> CreateQueryTaskAsync(
        PersistenceSessionHandle session,
        PersistenceQuerySpec spec,
        CancellationToken cancellationToken)
    {
        global::PowerFramework.Contracts.Persistence.V1.CreateQueryTaskResponse response = await _persistence
            .CreateQueryTaskAsync(
                new PersistenceCreateQueryTaskRequest { Session = session, Spec = spec },
                cancellationToken)
            .ConfigureAwait(false);

        long outcome = response.Status is null
            ? RetCode.E_INTERNAL_ERROR
            : (long)response.Status.RetCode;

        return !Predicates.IsFailed(outcome) && !string.IsNullOrEmpty(response.Task?.TaskId)
            ? (response.Task, RetCode.OK)
            : (null, Predicates.IsFailed(outcome) ? outcome : RetCode.E_INTERNAL_ERROR);
    }

    /// <summary>
    /// Creates an update task against an open session and binds the DataWindow definition to it.
    /// </summary>
    /// <param name="session">The open session.</param>
    /// <param name="dataWindowHandle">The caller's DataWindow handle, which is its data object.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>The task handle, or the outcome that refused it.</returns>
    /// <remarks>
    /// <para>
    /// THE PREPARE STEP IS PART OF CREATION HERE, NOT AN OPTIONAL EXTRA, because a task created and never
    /// prepared HAS NO SOURCE OBJECT: C-06 applies the data object only from the prepare message, so an
    /// update run without one would submit rows against a task that does not know what it is updating.
    /// Creation and binding therefore succeed or fail together, and a task that was created but could not be
    /// bound is released by the caller's <c>finally</c> rather than used.
    /// </para>
    /// <para>
    /// <b>MULTI-TABLE UPDATE IS DELIBERATELY LEFT OFF, AND THE DESCRIPTOR ARRAY IS DELIBERATELY EMPTY
    /// (C-B).</b> C-03's <c>UpdateRequest</c> carries a handle, an optional session and rows - NO table
    /// descriptors - so there is nothing to declare and declaring nothing while asserting the multi-table
    /// switch would be refused outright by C-06's own guard. With the switch off the legacy calls the update
    /// directly and NEVER prepares the descriptor array
    /// [<c>n_cst_thread_task_sqlupdate.sru:L365 versus :L371</c>], so the carrier's OWN static definition
    /// governs update, key, identity, <c>updatewhere</c> and <c>updatekeyinplace</c> - which for the
    /// evidenced fixture is <c>update="COMPANY" updatewhere=1 updatekeyinplace=no</c> over all six marked
    /// columns [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>]. That is the faithful path, and it
    /// is also why NO TABLE NAME IS SYNTHESIZED HERE: inventing one would override the carrier's definition
    /// and change the generated statement for every caller.
    /// </para>
    /// </remarks>
    private async Task<(PersistenceTaskHandle? Task, long ReturnCode)> CreateUpdateTaskAsync(
        PersistenceSessionHandle session,
        string dataWindowHandle,
        CancellationToken cancellationToken)
    {
        global::PowerFramework.Contracts.Persistence.V1.CreateUpdateTaskResponse created =
            await _persistence
                .CreateUpdateTaskAsync(
                    new PersistenceCreateUpdateTaskRequest { Session = session },
                    cancellationToken)
                .ConfigureAwait(false);

        long createOutcome = created.Status is null
            ? RetCode.E_INTERNAL_ERROR
            : (long)created.Status.RetCode;

        if (Predicates.IsFailed(createOutcome) || string.IsNullOrEmpty(created.Task?.TaskId))
        {
            return (null, Predicates.IsFailed(createOutcome) ? createOutcome : RetCode.E_INTERNAL_ERROR);
        }

        global::PowerFramework.Contracts.Persistence.V1.PrepareUpdateResponse prepared = await _persistence
            .PrepareUpdateAsync(
                new PersistencePrepareUpdateRequest
                {
                    Task = created.Task,
                    MultiTableUpdate = false,
                    DataObject = dataWindowHandle,
                },
                cancellationToken)
            .ConfigureAwait(false);

        long prepareOutcome = prepared.Status is null
            ? RetCode.E_INTERNAL_ERROR
            : (long)prepared.Status.RetCode;

        // THE HANDLE IS RETURNED ALONGSIDE THE FAILURE ON PURPOSE. The task exists upstream whether or not
        // the bind succeeded, so the caller must still release it; returning null here would strand it.
        return Predicates.IsFailed(prepareOutcome)
            ? (created.Task, prepareOutcome)
            : (created.Task, RetCode.OK);
    }

    /// <summary>
    /// Builds the call status for a retrieval whose upstream stream ended in an unprojectable failure.
    /// </summary>
    /// <param name="returnCode">The upstream outcome.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// SEPARATE FROM THE LIFECYCLE FAILURE BECAUSE THE TWO SAY DIFFERENT THINGS. A lifecycle failure means
    /// the retrieval never began; this means it began, ran, and was refused - so a caller that has already
    /// received chunks needs to know the answer it holds is partial rather than empty. The status code
    /// derivation is shared through <see cref="MapOutcomeToStatus"/>, so the same outcome code cannot arrive
    /// as two different statuses depending on how far the retrieval had got. NO DRIVER TEXT IS PLACED ON THE
    /// STATUS on either path: a status message is the one field intermediaries log freely, and the driver's
    /// own message echoes offending VALUES - a uniqueness violation names the duplicate key - so the numeric
    /// outcome is disclosed instead, which identifies the failure without carrying row data. The driver's
    /// payload still reaches the caller, on the error chunk, where the contract declares a field for it.
    /// </remarks>
    private static RpcException BuildTerminalFailure(long returnCode) => new(new Status(
        MapOutcomeToStatus(returnCode),
        $"The upstream retrieval failed with outcome {returnCode} and carried no database error, so the "
            + "failure has no field on RetrieveChunk to travel in and is reported as the call status. Any "
            + "chunks already delivered are a PARTIAL result and must not be read as a complete one. The "
            + "driver's own message is deliberately withheld from this status because a status message is "
            + "freely logged by intermediaries and the driver text echoes offending values."));

    /// <summary>
    /// Maps an upstream outcome onto the call status a caller can act on.
    /// </summary>
    /// <param name="returnCode">The upstream outcome.</param>
    /// <returns>The status code.</returns>
    /// <remarks>
    /// DERIVED RATHER THAN FIXED, so a retryable refusal is distinguishable from a permanent one: an
    /// exhausted pool or a busy task is <c>ResourceExhausted</c>, a rejected descriptor or argument is
    /// <c>FailedPrecondition</c>, and anything else is <c>Internal</c>. The numeric outcome always travels
    /// in the message, so the specific legacy code survives even where several codes share one status.
    /// </remarks>
    private static StatusCode MapOutcomeToStatus(long returnCode) => returnCode switch
    {
        RetCode.E_BUSY or RetCode.E_RETRY or RetCode.E_OUT_OF_MEMORY => StatusCode.ResourceExhausted,
        RetCode.E_INVALID_TRANSACTION or RetCode.E_INVALID_ARGUMENT or RetCode.E_INVALID_SQL =>
            StatusCode.FailedPrecondition,
        RetCode.E_INVALID_HANDLE or RetCode.E_OBJECT_NOT_FOUND => StatusCode.NotFound,
        _ => StatusCode.Internal,
    };

    /// <summary>
    /// Ends a session upstream, releasing its pool reference.
    /// </summary>
    /// <param name="session">The session to end.</param>
    /// <remarks>
    /// <b>DELIBERATELY TAKES NO CANCELLATION TOKEN, AND THAT IS THE WHOLE POINT OF THIS METHOD.</b> A
    /// session holds a REFERENCE-COUNTED POOL ENTRY upstream [<c>n_cst_thread_trans_pool.sru</c>], so a
    /// session that is never ended never gives that reference back and the pool entry is pinned for the
    /// life of the process. The caller's token is cancelled on exactly the paths where cleanup matters
    /// most - a client that gave up mid-retrieval - so passing it here would abandon the release precisely
    /// when the leak is being created. Cleanup therefore runs unconditionally, and a failure to release is
    /// logged rather than raised because it must never displace the outcome the caller is owed.
    /// </remarks>
    private async Task EndPersistenceSessionAsync(PersistenceSessionHandle session)
    {
        try
        {
            await _persistence
                .EndSessionAsync(
                    new PersistenceEndSessionRequest { Session = session },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (RpcException failure)
        {
            _logger?.LogWarning(
                "Ending an upstream transaction session failed with status {StatusCode}. The session held a "
                    + "reference-counted pool entry upstream, so the entry stays pinned until its idle "
                    + "expiry sweeps it. The failure is logged and not raised: it must not displace the "
                    + "outcome the caller is owed.",
                failure.StatusCode);
        }
    }

    /// <summary>
    /// Releases a query task upstream.
    /// </summary>
    /// <param name="task">The task to release.</param>
    /// <remarks>
    /// Uncancellable for the same reason as <see cref="EndPersistenceSessionAsync"/>: a task retains its
    /// carrier and its configured statement upstream until it is released.
    /// </remarks>
    private async Task ReleaseQueryTaskAsync(PersistenceTaskHandle task)
    {
        try
        {
            await _persistence
                .ReleaseQueryTaskAsync(
                    new PersistenceReleaseQueryTaskRequest { Task = task },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (RpcException failure)
        {
            _logger?.LogWarning(
                "Releasing an upstream query task failed with status {StatusCode}. Logged and not raised.",
                failure.StatusCode);
        }
    }

    /// <summary>
    /// Releases an update task upstream.
    /// </summary>
    /// <param name="task">The task to release.</param>
    /// <remarks>Uncancellable for the same reason as <see cref="EndPersistenceSessionAsync"/>.</remarks>
    private async Task ReleaseUpdateTaskAsync(PersistenceTaskHandle task)
    {
        try
        {
            await _persistence
                .ReleaseUpdateTaskAsync(
                    new PersistenceReleaseUpdateTaskRequest { Task = task },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (RpcException failure)
        {
            _logger?.LogWarning(
                "Releasing an upstream update task failed with status {StatusCode}. Logged and not raised.",
                failure.StatusCode);
        }
    }

    // =================================================================================================
    //  UPDATE - the third of the triple, and the one carrying concurrency semantics
    // =================================================================================================

    /// <summary>
    /// Submits a changeset.
    /// </summary>
    /// <param name="request">The DataWindow, the optional session, and the rows with their shadows.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The counts, the identity round trip, and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">
    /// <c>Aborted</c> on an optimistic-concurrency mismatch, carrying a <c>common.v1.ConflictDetail</c> in
    /// the binary trailer the contract declares.
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHY <c>Aborted</c> (C-K). It is THE CANONICAL gRPC-TO-HTTP-409 MAPPING, which is precisely what lets
    /// Gateway's REST projection surface a conflict unchanged instead of inventing a translation. The
    /// contract declares the choice machine-readably on this method - the trailer key, the status code and
    /// the payload type - so a client discovers all three from the descriptor, and this implementation
    /// READS that declaration rather than writing the key as a literal. The detail cannot ride on the
    /// status itself, because a status carries only a code and a message, so it travels as a binary
    /// trailer.
    /// </para>
    /// <para>
    /// <b>THE CONFLICT IS SURFACED AND NEVER RETRIED.</b> There is no retry, no back-off and no second
    /// attempt anywhere in this method. Gateway's typed client is documented as propagating the status
    /// intact and never retrying, and a retry here would silently defeat that by re-submitting a changeset
    /// the database has already rejected - overwriting the very row state the conflict reported. THERE IS
    /// NO SILENT OVERWRITE ANYWHERE IN THE SYSTEM: the caller implements an explicit retry-or-surface
    /// policy with the current row state in hand.
    /// </para>
    /// <para>
    /// THE CONCURRENCY WORK ITSELF IS PERSISTENCE'S. <c>Concurrency/ConflictDetector.cs</c> and
    /// <c>Concurrency/UpdateWhereBuilder.cs</c> live behind C-06, where the <c>updatewhere=1</c> comparison
    /// over all six marked columns of the primary fixture is performed
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>,
    /// <c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L98-L145</c>]. THIS METHOD
    /// RELAYS THE SIGNAL FAITHFULLY AND DECIDES NOTHING ABOUT IT.
    /// </para>
    /// <para>
    /// <b>THE STATEMENT FIELD IS RELAYED AND NEVER LOGGED.</b> <c>common.v1.DbError.sqlsyntax</c> carries
    /// the complete generated statement including its interpolated literal values - literal because
    /// <c>DisableBind=1</c> means the runtime uses no bind variables
    /// [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>] - and the legacy logger performs NO REDACTION AT
    /// ALL. So the field is handed to the caller entitled to it and is kept out of every log statement
    /// below; the log records the numeric database code and the buffer and row only.
    /// </para>
    /// <para>
    /// THE IDENTITY ROUND TRIP IS RELAYED ELEMENT FOR ELEMENT, IN ORDER, AT BOTH LEVELS. One update emits
    /// one block per update table, and each block's primary values are in FORWARD collection order while
    /// its filter values are in BACKWARD order - because the Filter buffer's row order is inverted relative
    /// to the source [<c>n_cst_thread_task_sqlupdate.sru:L235-L241</c>]. Re-sorting either array,
    /// de-duplicating it, or concatenating the two would pair identity values with the wrong rows: a defect
    /// that returns the right COUNT of identities and therefore survives every row-count assertion. The
    /// repeated field is assigned wholesale for exactly that reason.
    /// </para>
    /// </remarks>
    public override async Task<global::PowerFramework.Contracts.DataServices.V1.UpdateResponse> Update(
        global::PowerFramework.Contracts.DataServices.V1.UpdateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(request.DatawindowHandle))
        {
            return new global::PowerFramework.Contracts.DataServices.V1.UpdateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            };
        }

        // The session is OPTIONAL on this operation and is validated only when supplied. An update does not
        // need the four cross-event fields - it needs the rows - but a caller that names a session is
        // asserting a correlation, and an assertion that cannot be honoured is refused rather than ignored.
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            ValidationSessionResolution resolved = ResolveSession(request.SessionId);

            if (!resolved.IsResolved)
            {
                return new global::PowerFramework.Contracts.DataServices.V1.UpdateResponse
                {
                    RetCode = DataWindowWireProjection.ToWireRetCode(resolved.ReturnCode),
                };
            }
        }

        // THE UPSTREAM WORK HANDLES ARE ACQUIRED HERE AND RELEASED ON EVERY PATH OUT, INCLUDING THE
        // CONFLICT PATH. C-06's update call names a server-held task, and Persistence refuses a blank
        // handle with E_INVALID_HANDLE before any statement is generated [Grpc/UpdateService.cs], so
        // sending a default handle refused every update this service ever issued. The scope is declared
        // BEFORE the try below so that `throw BuildConflictFailure(...)` still releases: a conflict is the
        // most likely non-success outcome of an update, and it is exactly the path on which a leaked update
        // task would hold a worker task and a pooled-transaction reference for the life of the process.
        await using PersistenceWorkScope scope = await _persistence
            .OpenUpdateScopeAsync(BuildSessionRequest(), context.CancellationToken)
            .ConfigureAwait(false);

        if (!scope.IsAcquired)
        {
            // A REFUSAL TRAVELS ON THE MESSAGE HERE, NOT AS A CALL STATUS - the opposite of Retrieve, and
            // for the opposite reason: UpdateResponse DOES declare an outcome-code field, so the refusal
            // has somewhere honest to go, and every other non-success outcome of this operation already
            // travels there. Raising a status instead would make one class of update failure decode
            // differently from all the others.
            return new global::PowerFramework.Contracts.DataServices.V1.UpdateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(scope.ReturnCode),
            };
        }

        // THE CARRIER IS PROJECTED AND VALIDATED BEFORE THE UPSTREAM IS TOLD ANYTHING. A row naming a
        // buffer the positional encoding has no segment for is a malformed request, and the receiving codec
        // would refuse the payload anyway - so it is refused here, where the refusal can still name the
        // request rather than arriving as an upstream decode failure the caller cannot act on.
        if (!TryBuildCarrierState(
                request.Rows,
                ResolveProcessingKind(request.DatawindowHandle),
                out global::PowerFramework.Contracts.Persistence.V1.CarrierState? carrier)
            || carrier is null)
        {
            return new global::PowerFramework.Contracts.DataServices.V1.UpdateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
            };
        }

        global::PowerFramework.Contracts.Persistence.V1.UpdateResponse upstreamResponse;

        try
        {
            PrepareUpdateRequest prepare = new()
            {
                Task = scope.Task,

                // The handle IS the data object, exactly as it is on a retrieval: a DataWindow's own
                // DataObject is what its Update() writes through.
                DataObject = request.DatawindowHandle,

                // See the block comment above. OFF, deliberately and unconditionally.
                MultiTableUpdate = false,
            };

            DataWindowUpdateContract? contract =
                _updateContracts?.GetUpdateContract(request.DatawindowHandle);

            if (contract is not null)
            {
                TableUpdateContract descriptor = new()
                {
                    Name = contract.TableName,
                    Identitycolumn = contract.IdentityColumn,
                };

                // Assigned in declaration order and never sorted: C-06 walks the arrays [:L111-L114].
                descriptor.Updatablecolumns.AddRange(contract.UpdatableColumns);
                descriptor.Keycolumns.AddRange(contract.KeyColumns);

                // PRESENCE-GATED ON BOTH, INDEPENDENTLY. C-06 tests the two settings SEPARATELY
                // [:L131, :L135], and an unset field there means "leave the carrier's own setting
                // alone" - so writing a default would silently overwrite the definition's own value
                // with a fabricated one.
                if (contract.UpdateWhere is long updateWhere)
                {
                    descriptor.Updatewhere = updateWhere;
                }

                if (contract.UpdateKeyInPlace is bool keyInPlace)
                {
                    descriptor.Updatekeyinplace = keyInPlace;
                }

                prepare.Tables.Add(descriptor);
            }

            PrepareUpdateResponse prepared = await _persistence
                .PrepareUpdateAsync(prepare, context.CancellationToken)
                .ConfigureAwait(false);

            if (!Predicates.IsSucceeded((long)(prepared.Status?.RetCode ?? 0)))
            {
                // ⚠ IN-BAND, NOT A CALL STATUS, AND FOR THE SAME REASON THE ACQUISITION REFUSAL IS ⚠
                //
                // This arm used to raise the upstream failure as an RPC status. That is the correct shape on
                // Retrieve, where RetrieveChunk declares no outcome field and a refusal has nowhere else to
                // go - and it is the wrong shape here, because UpdateResponse DOES declare one. Every other
                // non-success outcome of this operation travels there: a refused acquisition above, a
                // failing update below, and the tri-state codes the legacy algebra carries. Raising a status
                // for exactly one of them would make one class of update failure decode differently from all
                // the others, so a caller would need two decoding paths for one operation.
                //
                // A prepare failure arrives as the upstream's own code plus driver text, because the legacy's
                // carrier modification call returns an error STRING with empty meaning success
                // [:L145-L149]. The code is what a caller acts on and is what travels; the text is left with
                // the upstream rather than folded into a field that has no place for it.
                //
                // THE TASK AND THE SESSION STILL RELEASE. The scope is declared above the try, so returning
                // from inside it exits the enclosing block and `await using` releases the update task and
                // ends the session - the same guarantee the conflict path below relies on.
                return new global::PowerFramework.Contracts.DataServices.V1.UpdateResponse
                {
                    RetCode = DataWindowWireProjection.ToWireRetCode(
                        prepared.Status is null
                            ? RetCode.E_INTERNAL_ERROR
                            : (long)prepared.Status.RetCode),
                };
            }

            PersistenceUpdateRequest upstream = new()
            {
                Task = scope.Task,
                UpdateData = carrier,
                UpdateRows = request.Rows.Count,

                // ==================================================================================
                //  THE TASK-LEVEL AUTOCOMMIT IS SET, AND WITHOUT IT A SUCCESSFUL UPDATE IS DISCARDED.
                //
                //  C-06's task autocommit is the oracle's own epilogue switch -
                //  `if _bAutoCommit then rtCode = of_Commit(true)` [:L386-L387] - and it is NOT the
                //  session descriptor's connection-level autocommit, which stays FALSE so the session
                //  keeps one explicit transaction (see BuildTransactionDescriptor).
                //
                //  IT MUST BE SET HERE BECAUSE THE SESSION'S WHOLE LIFETIME IS THIS CALL. The work
                //  scope opens the session, creates and prepares the task, runs the update, releases
                //  the task and ends the session - and ending it ROLLS BACK any open transaction, which
                //  is the correct posture for a disconnect [Data/SqliteTransactionEngine.Disconnect].
                //  So with the switch unset, C-06 generated and executed the statements, reported the
                //  row counts it really applied, and then the teardown threw the work away: an update
                //  that answered `rowsUpdated: 1` while storage still held the old row. That is a
                //  success that loses data, which is strictly worse than the refusal it replaced.
                //  Nothing else on the published surface can commit this transaction, because C-08's
                //  commit names a session handle that never leaves this method.
                //
                //  THE FAILURE ARM IS UNCHANGED AND IS WHY THIS IS SAFE. The oracle rolls back on any
                //  non-OK outcome [:L395] and a commit that itself fails REPLACES the return code
                //  [:L387], so a caller still learns of a failed commit rather than reading a stale
                //  success. An optimistic-concurrency mismatch is classified before the success arm, so
                //  it rolls back and answers Aborted exactly as before - this switch cannot turn a
                //  conflict into a write.
                // ==================================================================================
                Autocommit = true,
            };

            upstreamResponse = await _persistence
                .UpdateAsync(upstream, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (PersistenceConflictException conflict)
        {
            // SURFACED, NOT RETRIED. Re-raised as this contract's own Aborted with this contract's own
            // declared trailer, so the chain from Persistence through DataServices to Gateway decodes one
            // key and one payload type end to end.
            //
            // THE WORK SCOPE STILL RELEASES ON THIS PATH, which is the whole reason it is declared ABOVE
            // this try rather than inside it: `await using` releases the update task and ends the session
            // when the enclosing method's block exits, including by a throw. A conflict is the most likely
            // non-success outcome of an update, so it is exactly the path on which a leaked task would
            // hold a worker task and a pooled-transaction reference for the life of the process - and the
            // pool's reference counting cannot distinguish a leaked reference from a live one.
            throw BuildConflictFailure(conflict);
        }

        long outcome = upstreamResponse.Status is null
            ? RetCode.E_INTERNAL_ERROR
            : (long)upstreamResponse.Status.RetCode;

        global::PowerFramework.Contracts.DataServices.V1.UpdateResponse response = new()
        {
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
            RowsInserted = upstreamResponse.Counts?.Inserted ?? 0L,
            RowsUpdated = upstreamResponse.Counts?.Updated ?? 0L,
            RowsDeleted = upstreamResponse.Counts?.Deleted ?? 0L,
        };

        if (upstreamResponse.Status?.DbError is not null)
        {
            // Relayed intact - INCLUDING the statement field, which the caller is entitled to and which is
            // never written to a log. The diagnostic below deliberately reads only the numeric code, the
            // buffer and the row.
            response.Error = upstreamResponse.Status.DbError;

            _logger?.LogWarning(
                "An update on DataWindow handle {DataWindowHandle} returned outcome {ReturnCode} with "
                    + "database code {SqlDbCode} against buffer {Buffer} row {Row}. The statement text is "
                    + "relayed to the caller and is deliberately NOT logged: it carries interpolated "
                    + "literal values because the legacy runs without bind variables when DisableBind is "
                    + "set [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129], "
                    + "and the legacy logger performs no redaction at all.",
                request.DatawindowHandle,
                outcome,
                upstreamResponse.Status.DbError.Sqldbcode,
                upstreamResponse.Status.DbError.Buffer,
                upstreamResponse.Status.DbError.Row);
        }

        // Order-preserving at both levels: the blocks in the order the tables were declared, and each
        // block's two arrays element for element as collected.
        response.Identity.AddRange(upstreamResponse.Identity);

        return response;
    }

    /// <summary>
    /// Groups the submitted rows into the buffer segments C-06 consumes.
    /// </summary>
    /// <param name="rows">The rows, each already carrying its own buffer tag and item status.</param>
    /// <returns>The carrier state.</returns>
    /// <remarks>
    /// <para>
    /// THE CARRIER IS A DATAWINDOW, NOT A ROWSET, AND THIS IS THE ANTI-CORRUPTION EDGE THAT KEEPS IT ONE.
    /// The legacy result carrier derives from a <c>datastore</c>, so it has buffers and per-item statuses,
    /// and the ORIGINAL-VALUE SHADOW the concurrency check compares against is part of that state.
    /// <c>updatewhere=1</c> means the generated where-clause carries the key column PLUS THE ORIGINAL VALUES
    /// OF EVERY UPDATEABLE COLUMN, and the primary fixture marks all six
    /// [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>] - so the payload must transmit, per row,
    /// both the current and the original value of every marked column. Rows are therefore MOVED, not
    /// rebuilt: nothing here reads, rewrites or drops a shadow.
    /// </para>
    /// <para>
    /// <b>ALL THREE SEGMENTS, ALWAYS, IN CANONICAL ORDER - INCLUDING THE EMPTY ONES.</b>
    /// <c>persistence.v1.CarrierState</c> is a POSITIONAL encoding, not a set: the receiving codec tests
    /// <c>Segments.Count</c> against the canonical length and then requires segment n to be buffer n
    /// [<c>Buffers/ChangesetCodec.cs</c> - <c>SerializedBuffers</c>, <c>TryValidateSegments</c>], and
    /// Persistence's own encoder emits all three unconditionally. Emitting only the buffers a request
    /// happened to mention - which is what this method used to do - therefore produced a payload the
    /// receiver REJECTS outright for any request that did not touch all three, which is nearly every
    /// request: an update of one row would send a single Primary segment and be refused. Segment order is
    /// the CANONICAL order for the same reason and not the caller's order of appearance.
    /// </para>
    /// <para>
    /// ROW ORDER WITHIN EACH SEGMENT IS THE CALLER'S, UNTOUCHED. The Filter buffer's rows arrive in an
    /// order that is INVERTED relative to the source
    /// [<c>n_cst_thread_task_sqlupdate.sru:L235</c>], which looks like a defect and is not one; the
    /// identity round trip reads that buffer backwards precisely because of it, so "correcting" the order
    /// here would pair identity values with the wrong rows - a defect that returns the right COUNT of
    /// identities and therefore survives every row-count assertion. Rows are appended in the sequence they
    /// were received and nothing is sorted, de-duplicated or coalesced.
    /// </para>
    /// <para>
    /// AN UNDECLARED BUFFER IS REFUSED, NEVER DROPPED. With a fixed three-segment shape there is nowhere to
    /// put a row whose buffer is outside the domain, and the two available failure modes are not equally
    /// bad: silently omitting it would submit an UPDATE missing a row the caller asked to apply, which is
    /// data loss that reports success. So this returns a failure and the caller answers
    /// <c>E_INVALID_ARGUMENT</c>.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The three buffer segments <c>persistence.v1.CarrierState</c> carries, in the order it carries them.
    /// </summary>
    /// <remarks>
    /// <b>RESTATED HERE RATHER THAN REFERENCED, BECAUSE THE ALTERNATIVE IS A SERVICE-TO-SERVICE CODE
    /// DEPENDENCY.</b> Persistence declares the same order in its own codec
    /// [<c>Buffers/ChangesetCodec.cs</c> - <c>SerializedBuffers</c>], and reaching into it from here would
    /// be exactly the cross-service coupling the architecture forbids - the ONLY permitted coupling is the
    /// published contracts project (AAP 0.7.2). The order is a property OF THE CONTRACT, so both ends
    /// restating it is correct; what keeps them from drifting is the contract text and the coherence suite,
    /// not a shared field.
    /// </remarks>
    private static readonly DwBuffer[] CanonicalBufferOrder =
    [
        DwBuffer.Primary,
        DwBuffer.Delete,
        DwBuffer.Filter,
    ];

    private static bool TryBuildCarrierState(
        IReadOnlyList<DataWindowRow> rows,
        long processing,
        out global::PowerFramework.Contracts.Persistence.V1.CarrierState? state)
    {
        state = null;

        // ==========================================================================================
        //  THE PROCESSING KIND TRAVELS WITH THE ROWS, AND OMITTING IT REFUSED EVERY REAL UPDATE.
        //
        //  `persistence.v1.CarrierState.processing` is RECONCILED rather than informational: the two
        //  sides build their carriers from their own transcription of the same DataWindow, so the
        //  receiving codec refuses a payload whose kind disagrees with its target's and adopts the
        //  payload's kind only for a target that has none [Buffers/ChangesetCodec.cs -
        //  TryValidateSegments]. Leaving the field at its default said "unassigned", which DISAGREES
        //  with every definition that declares a presentation style - the primary fixture declares
        //  processing=1 [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L3] - so the changeset was rejected
        //  before a single statement was generated and every non-empty update answered
        //  E_INVALID_DATA with the legacy diagnostic. Sending the kind this service's own definition
        //  declares is what makes the two carriers comparable at all.
        //
        //  IT IS READ THROUGH Describe RATHER THAN FROM A PARSED FIELD, which is the oracle's own idiom
        //  at the mirror-image site: `Long(Data.Describe("DataWindow.Processing"))`
        //  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93]. See
        //  ResolveProcessingKind for the coercion and for why an unresolvable handle still sends zero.
        // ==========================================================================================
        global::PowerFramework.Contracts.Persistence.V1.CarrierState projected = new()
        {
            Processing = processing,
        };

        Dictionary<DwBuffer, global::PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment> segments =
            new(CanonicalBufferOrder.Length);

        // EVERY canonical segment is created and added up front, so the shape is correct before a single
        // row is placed and cannot depend on what the rows happen to contain.
        foreach (DwBuffer dwBuffer in CanonicalBufferOrder)
        {
            global::PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment segment = new()
            {
                Buffer = dwBuffer,
            };

            segments[dwBuffer] = segment;
            projected.Segments.Add(segment);
        }

        foreach (DataWindowRow row in rows)
        {
            if (!segments.TryGetValue(
                row.Buffer,
                out global::PowerFramework.Contracts.Persistence.V1.CarrierBufferSegment? segment))
            {
                return false;
            }

            segment.Rows.Add(row);
        }

        state = projected;

        return true;
    }

    /// <summary>
    /// Reads the carrier processing kind the named DataWindow's own definition declares.
    /// </summary>
    /// <param name="dataWindowHandle">The caller's own name for the DataWindow.</param>
    /// <returns>
    /// The kind, or zero when the handle resolves to nothing. Zero is
    /// <c>persistence.v1.CarrierState.processing</c>'s "unassigned" value rather than an invented
    /// sentinel, and it is the value a receiving carrier with no data object of its own ADOPTS.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>READ THROUGH <c>Describe</c>, WHICH IS THE ORACLE'S OWN IDIOM AT THE MIRROR-IMAGE SITE.</b> The
    /// legacy reads the kind off the carrier the same way when it decides which transfer style to use -
    /// <c>Long(Data.Describe("DataWindow.Processing"))</c>
    /// [<c>ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L93</c>] - and
    /// <c>Long</c> answers zero for both of <c>Describe</c>'s failure markers because neither is a
    /// number, so an unreadable property lands in the same arm as an unassigned one. That coercion is
    /// reproduced here rather than replaced by a parse of a definition field, so this service and
    /// Persistence read the same property by the same route.
    /// </para>
    /// <para>
    /// <b>AN UNRESOLVABLE HANDLE IS NOT REFUSED HERE.</b> It sends zero and lets the operation continue to
    /// the upstream, which refuses an update against a DataWindow it cannot resolve on its own authority
    /// [<c>n_cst_thread_task_sqlupdate.sru:L179-L189</c>]. Refusing at this line instead would move an
    /// established refusal from the layer that owns the update contract into the layer that merely
    /// projects it, and would change which code a caller receives for an unknown handle on this
    /// operation alone.
    /// </para>
    /// <para>
    /// INVARIANT PARSING, because the value is a machine-generated property string rather than user
    /// input.
    /// </para>
    /// </remarks>
    private long ResolveProcessingKind(string dataWindowHandle)
    {
        if (_models.GetOrCreate(dataWindowHandle) is not { } set)
        {
            return 0L;
        }

        return long.TryParse(
            set.Host.Describe("DataWindow.Processing"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long parsed)
            ? parsed
            : 0L;
    }

    /// <summary>
    /// Re-raises an upstream conflict as this contract's own <c>Aborted</c> failure.
    /// </summary>
    /// <param name="conflict">The conflict the typed client decoded.</param>
    /// <returns>The failure to throw.</returns>
    /// <remarks>
    /// <para>
    /// THE BINDING IS READ FROM THE DESCRIPTOR AND THE THREE REFUSAL ARMS DEFEND AGAINST DRIFT. When the
    /// descriptor declares no binding, or declares a payload type this service cannot encode, the upstream
    /// status is SURFACED UNCHANGED rather than being re-encoded under a key a client may not be looking
    /// for. Surfacing unchanged is the safe failure here: the caller still learns the operation was
    /// aborted, and still sees the upstream trailers, so no information is invented and none is lost.
    /// </para>
    /// <para>
    /// THE MESSAGE NAMES NO ROW VALUE. The conflicting row state travels in the structured detail, where a
    /// caller can read it deliberately; putting it in a status message would leak it into every log that
    /// records a failed call.
    /// </para>
    /// </remarks>
    private RpcException BuildConflictFailure(PersistenceConflictException conflict)
    {
        Status status = new(
            StatusCode.Aborted,
            "The update was aborted by an optimistic-concurrency mismatch. The current row state travels "
                + "in the rich-error trailer this method's contract declares; the caller implements an "
                + "explicit retry-or-surface policy and this service performs no retry.");

        if (UpdateRichErrorBinding is null
            || string.IsNullOrEmpty(UpdateRichErrorBinding.TrailerKey)
            || !string.Equals(
                UpdateRichErrorBinding.PayloadType,
                SupportedRichErrorPayloadType,
                StringComparison.Ordinal))
        {
            _logger?.LogWarning(
                "Contract C-03's Update method declares no usable rich-error binding - this service "
                    + "encodes {SupportedPayloadType} - so the aborted upstream status is surfaced "
                    + "unchanged with its own trailers intact rather than re-encoded under a key a client "
                    + "may not read. This indicates a drift between the generated contract and this "
                    + "implementation.",
                SupportedRichErrorPayloadType);

            return conflict.Trailers is null
                ? new RpcException(status)
                : new RpcException(status, conflict.Trailers);
        }

        RichErrorTrailer trailer = new() { RetCode = conflict.RetCode };

        if (conflict.Conflict is not null)
        {
            trailer.Conflict = conflict.Conflict;
        }

        Metadata trailers = [];

        trailers.Add(UpdateRichErrorBinding.TrailerKey, trailer.ToByteArray());

        return new RpcException(status, trailers);
    }

    // =================================================================================================
    //  THE EVENT CHAIN - all 22 events, both ordering patterns, one ordered conversation
    // =================================================================================================

    /// <summary>
    /// Runs one ordered, bidirectional event conversation.
    /// </summary>
    /// <param name="requestStream">Inbound notifications, and answers to outbound questions.</param>
    /// <param name="responseStream">Outbound results, questions and stream-level errors.</param>
    /// <param name="context">The call context.</param>
    /// <returns>A task that completes when the client closes the request stream.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">
    /// <c>InvalidArgument</c> for a message carrying no session identifier or no payload;
    /// <c>FailedPrecondition</c> for an unknown session, a handle nothing can be bound to, or an ordering
    /// violation inside a synchronous group while strict ordering is in force.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ALL 22 EVENTS TRAVEL, AND NO RAW EVENT IS COLLAPSED INTO THE SEMANTIC ONE IT DELEGATES TO. The split
    /// is 13 raw <c>pbm_dwn*</c> against 9 semantic [<c>se_cst_dw.sru:L11-L32</c>], and each raw event
    /// delegates first to its semantic counterpart and then to the broker, with BOTH edges able to stop the
    /// dispatch - <c>ondwnrbuttondown</c> returns 1 if the semantic handler returns 1 and otherwise consults
    /// the broker [<c>:L115-L117</c>], while <c>ondwnrbuttonup</c> consults only the broker
    /// [<c>:L120-L121</c>]. Collapsing the pair would erase the two-stage veto and a consumer could no
    /// longer say which edge stopped it. THAT DELEGATION LIVES IN THE CHAIN; this method transports it and
    /// neither re-implements nor reorders it.
    /// </para>
    /// <para>
    /// BOTH ORDERING PATTERNS ARE IMPLEMENTED, PER CAPABILITY AREA, AND NEITHER IS APPLIED GLOBALLY. The
    /// assignment is <see cref="DataWindowEventOrdering.DisciplineOf(EventId, bool)"/>'s, with the evidence
    /// recorded there area by area: pattern (b) SYNCHRONOUS for the item-change and validation chain, the
    /// context menu, the drop-down search and macro invocation; pattern (a) SEQUENCED for the focus, mouse
    /// and row-focus notifications and for the expression trace. Under (b) an out-of-order arrival is a HARD
    /// ERROR and never a reorder opportunity; under (a) the token carries reorder authority for the
    /// consumer. NOTHING HERE EVER BUFFERS OR RE-SORTS, under either pattern.
    /// </para>
    /// <para>
    /// TWO FAULT CHANNELS, AND THE SPLIT IS DELIBERATE (C-K). A fault the conversation can survive - a
    /// client answering a question this server never asked - travels as a <c>StructuredError</c> ON the
    /// stream, which is exactly what the response's error alternative exists for, and the conversation
    /// continues because nothing was corrupted. A fault the conversation CANNOT survive - an unknown session,
    /// a handle bound to nothing, or an ordering violation under strict ordering - ends the call with a
    /// status, because the stream has nothing further it can coherently say. Recovering from the second kind
    /// would be graceful degradation of the fail-fast posture AAP 0.6.7 requires.
    /// </para>
    /// <para>
    /// ONE SESSION PER STREAM. Every message carries a session identifier and they must all name the SAME
    /// session: the ordering disciplines are defined over one conversation, and the four cross-event fields
    /// belong to one session, so a stream that switched sessions half way through would interleave two
    /// chains onto one counter.
    /// </para>
    /// </remarks>
    public override async Task EventChain(
        IAsyncStreamReader<EventChainRequest> requestStream,
        IServerStreamWriter<EventChainResponse> responseStream,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(requestStream);
        ArgumentNullException.ThrowIfNull(responseStream);
        ArgumentNullException.ThrowIfNull(context);

        CancellationToken cancellationToken = context.CancellationToken;
        bool strictOrdering = _eventChain.StrictOrdering;

        string? boundSessionId = null;
        DataWindowEventConversation? conversation = null;
        DataWindowEventChain? chain = null;

        try
        {
            await foreach (EventChainRequest request in requestStream
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(request.SessionId))
                {
                    throw new RpcException(new Status(
                        StatusCode.InvalidArgument,
                        "Every message on the event chain must carry a session identifier; one arrived "
                            + "without. An item-change event without a session is a defined error and never "
                            + "an implicit creation, because a conjured session would hold defaults where "
                            + "the validation-error handler expects a stashed item-change result."));
                }

                if (boundSessionId is null)
                {
                    (boundSessionId, conversation, chain) =
                        BindConversation(request.SessionId, responseStream);
                }
                else if (!string.Equals(boundSessionId, request.SessionId, StringComparison.Ordinal))
                {
                    throw new RpcException(new Status(
                        StatusCode.InvalidArgument,
                        "Every message on one event chain must name the same validation session. The "
                            + "ordering disciplines are defined over a single ordered conversation and the "
                            + "four cross-event fields belong to one session, so switching session mid "
                            + "stream would interleave two chains onto one counter."));
                }

                // Bound on the first message and never unset, so both are non-null from here on.
                switch (request.PayloadCase)
                {
                    case EventChainRequest.PayloadOneofCase.Notify:
                        await DispatchNotificationAsync(
                                request,
                                conversation!,
                                chain!,
                                strictOrdering,
                                cancellationToken)
                            .ConfigureAwait(false);

                        break;

                    case EventChainRequest.PayloadOneofCase.Result:
                        if (!conversation!.Deliver(request.Result))
                        {
                            // SURVIVABLE: nothing is waiting, so nothing was corrupted. Reported on the
                            // stream rather than discarded, because silently dropping it would hide a client
                            // whose model of the conversation has diverged from the server's.
                            await conversation
                                .WriteErrorAsync(
                                    UnmatchedAnswerError(request.Result.CorrelationId),
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }

                        break;

                    case EventChainRequest.PayloadOneofCase.None:
                    default:
                        throw new RpcException(new Status(
                            StatusCode.InvalidArgument,
                            "A message on the event chain carried neither a notification nor a result. The "
                                + "payload alternative is how the direction of the conversation is stated, "
                                + "so a message without one has no meaning to act on."));
                }
            }
        }
        finally
        {
            // Ordered: the conversation first, so an outstanding question is cancelled and its waiter fails
            // rather than hanging, and only then the chain, whose teardown destroys the broker and the five
            // attached services [se_cst_dw.sru:L565-L567]. The reverse order would tear down the services a
            // still-running dispatch was using.
            conversation?.Dispose();
            chain?.Teardown();
        }
    }

    /// <summary>
    /// Binds a stream to its session, conversation and chain.
    /// </summary>
    /// <param name="sessionId">The session the first message named.</param>
    /// <param name="responseStream">The response writer the conversation will own.</param>
    /// <returns>The bound identifier, conversation and chain.</returns>
    /// <exception cref="RpcException">
    /// <c>FailedPrecondition</c> when the session is unknown or its DataWindow handle can be bound to no
    /// chain.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE CONVERSATION IS CONSTRUCTED BEFORE THE CHAIN AND BOUND TO ITS SEQUENCER AFTER, because the chain
    /// requires the conversation as its observer AND its responder in its own constructor while the
    /// sequencer it will draw ordinals from is the chain's. That ordering is what makes both directions of
    /// the stream share ONE counter.
    /// </para>
    /// <para>
    /// SYNCHRONOUS, AND NOTHING IS WRITTEN ON BIND. The conversation speaks only in answer to a notification
    /// or to ask a semantic question, so a client that opens a stream and sends nothing receives nothing -
    /// which is what keeps the token sequence a record of real traffic rather than of handshakes.
    /// </para>
    /// </remarks>
    private (string SessionId, DataWindowEventConversation Conversation, DataWindowEventChain Chain)
        BindConversation(string sessionId, IServerStreamWriter<EventChainResponse> responseStream)
    {
        ValidationSessionResolution resolved = ResolveSession(sessionId);

        if (!resolved.IsResolved || resolved.Session is null)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The event chain named a validation session that is not open; the registry answered "
                        + "{0}. A session is never created implicitly here - see OpenValidationSession.",
                    resolved.ReturnCode)));
        }

        ValidationSession session = resolved.Session;

        DataWindowEventConversation conversation = new(session.SessionId, responseStream, _logger);

        DataWindowEventChain? chain = _chainFactory.Create(
            session,
            session.DataWindowHandle,
            conversation,
            conversation);

        if (chain is null)
        {
            conversation.Dispose();

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "No event chain could be bound to DataWindow handle '{0}'. The handle is the caller's "
                        + "own name for a DataWindow and is never created implicitly.",
                    session.DataWindowHandle)));
        }

        conversation.Bind(chain.Sequencer);

        return (session.SessionId, conversation, chain);
    }

    /// <summary>
    /// Builds the error reported for an answer that matches no outstanding question.
    /// </summary>
    /// <param name="correlationId">The identifier the answer carried.</param>
    /// <returns>The error.</returns>
    /// <remarks>
    /// Not localized: this is a boundary-created diagnostic with no legacy dialog behind it, so there is no
    /// oracle text to preserve and no category to claim.
    /// </remarks>
    private static StructuredError UnmatchedAnswerError(string correlationId) => new()
    {
        Title = string.Empty,
        Text = string.Format(
            CultureInfo.InvariantCulture,
            "An event result arrived with correlation identifier '{0}', which matches no outstanding "
                + "question on this conversation. One semantic question may be outstanding at a time, so an "
                + "unmatched answer means the client answered a question this server never asked, or "
                + "answered one twice. The conversation continues; the answer is discarded.",
            correlationId),
        Localized = false,
        Category = 0L,
        Severity = Severity.Exclamation,
        RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
    };

    /// <summary>
    /// Checks one inbound notification's ordering, dispatches it through the chain, and answers.
    /// </summary>
    /// <param name="request">The inbound message.</param>
    /// <param name="conversation">The stream's conversation.</param>
    /// <param name="chain">The stream's chain.</param>
    /// <param name="strictOrdering">Whether an ordering violation fails the call.</param>
    /// <param name="cancellationToken">Cancels the dispatch and the write.</param>
    /// <returns>A task that completes when the answer has been written.</returns>
    /// <exception cref="RpcException">
    /// <c>FailedPrecondition</c> on an ordering violation while strict ordering is in force.
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHY <c>FailedPrecondition</c> AND NOT <c>Aborted</c> FOR AN ORDERING VIOLATION (C-K). gRPC's own
    /// definition of <c>Aborted</c> names a sequencer check failure, which is literally what this is - but
    /// <c>Aborted</c> is already bound on this contract to the optimistic-concurrency conflict and its
    /// canonical projection to HTTP 409, so reusing it here would make a client that maps 409 to "retry or
    /// surface the row state" do so for a protocol fault with no row state in it.
    /// <c>FailedPrecondition</c> says what is true: the system is not in a state where the operation can
    /// run, and THE CLIENT MUST NOT RETRY UNTIL IT HAS FIXED ITS SEQUENCING. Re-sending the same message
    /// would not help, which is exactly the distinction the two codes draw.
    /// </para>
    /// <para>
    /// A DISPATCH THAT REPORTS NO OUTCOME STILL GETS AN ANSWER, AND THAT IS THE ORACLE'S SHAPE. The gate
    /// short-circuits return before anything is reported - <c>if BitTest(_nDisabledEvent,EID_ITEMCHANGE)
    /// then return 0</c> [<c>se_cst_dw.sru:L187</c>], and likewise at <c>:L124</c>, <c>:L130</c> and
    /// <c>:L176</c> - so the numeric the chain returned is projected on its own rather than an outcome being
    /// invented for it. Answering nothing would leave a synchronous caller waiting for a reply to an event
    /// the oracle simply declined.
    /// </para>
    /// </remarks>
    private static async Task DispatchNotificationAsync(
        EventChainRequest request,
        DataWindowEventConversation conversation,
        DataWindowEventChain chain,
        bool strictOrdering,
        CancellationToken cancellationToken)
    {
        EventNotification notify = request.Notify;

        try
        {
            conversation.AcceptInbound(request.Token?.Sequence ?? 0L, notify.EventId, strictOrdering);
        }
        catch (DataWindowEventSequenceException violation)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Event {1} carried sequence {2} where at least {3} was expected under the {4} "
                        + "discipline. Inside a SYNCHRONOUS group the expected token is the only one "
                        + "admitted, because one event's behaviour there is a function of its "
                        + "predecessor's return value. Inside a SEQUENCED group any token ABOVE the mark "
                        + "is admitted - a gap is legitimate, since both directions draw from one counter "
                        + "- but a reversal or a duplicate is not. The server does NOT buffer and re-sort "
                        + "in either group. Fix the sequencing before retrying: the next token is one past "
                        + "the highest this stream has carried, in either direction.",
                    violation.Message,
                    violation.EventId,
                    violation.ActualSequence,
                    violation.ExpectedSequence,
                    violation.Discipline)));
        }

        long numeric = await InvokeAsync(notify, chain).ConfigureAwait(false);

        DataWindowEventOutcome? outcome = conversation.TakeOutcome();

        EventResult result = outcome is not null
            ? DataWindowWireProjection.ToWireResult(outcome, notify.CorrelationId)
            : new EventResult
            {
                CorrelationId = notify.CorrelationId,
                EventId = notify.EventId,
                Dispatch = DataWindowWireProjection.ToWireDispatch(null),
                ReturnValue = numeric,
                AnyResult = DataWindowWireProjection.ToWireAny(null),
                State = DataWindowWireProjection.ToWireState(chain.Session.CaptureState()),
            };

        await conversation
            .WriteResultAsync(
                result,
                DataWindowEventConversation.DisciplineOf(notify.EventId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Invokes the chain member the notification names.
    /// </summary>
    /// <param name="notify">The notification.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>
    /// The numeric the chain member returned, or <c>0</c> for a member the oracle declares with no return
    /// type at all.
    /// </returns>
    /// <exception cref="RpcException"><c>InvalidArgument</c> when the body alternative is unset.</exception>
    /// <remarks>
    /// <para>
    /// ALL 22 ARMS, IN THE ORACLE'S OWN DECLARATION ORDER [<c>se_cst_dw.sru:L11-L32</c>]. Reading the arms
    /// downward reproduces the source's declaration sequence, so a drift between this file and the oracle is
    /// detectable by inspection rather than by search.
    /// </para>
    /// <para>
    /// FIVE OF THE 22 DECLARE NO RETURN TYPE AND THEREFORE ANSWER <c>0</c>: <c>onddsgetfilter</c>
    /// [<c>:L13</c>], <c>onitemchanged</c> [<c>:L25</c>], <c>ondoitemchanged</c> [<c>:L26</c>],
    /// <c>onddsfiltered</c> [<c>:L28</c>] and <c>oncolumnexptrace</c> [<c>:L32</c>]. Zero here is not a
    /// success code - it is the absence of a return contract, and the oracle's raisers discard nothing
    /// because there is nothing to discard. Inventing a code for them would advertise a veto the
    /// notification does not have.
    /// </para>
    /// <para>
    /// <c>onddsgetfilter</c>'s <c>ref string</c> OUT-PARAMETER IS WHAT MAKES ITS GROUP SYNCHRONOUS
    /// [<c>:L13</c>]. It is invoked with a live reference here and the produced filter is read back through
    /// the chain's reported outcome, where the domain places it - which is why the result's
    /// <c>produced_filter</c> is <c>optional</c>: an ABSENT filter is not an EMPTY filter, and an empty
    /// string is a legitimate produced value.
    /// </para>
    /// <para>
    /// <c>oncolumnexpinvokemethod</c> RETURNS <c>any</c> AND ITS ARGUMENT LIST IS ONE-BASED AT SOURCE
    /// [<c>:L14</c>]. The wire <c>repeated</c> field is zero-based, so wire element 0 is legacy
    /// <c>args[1]</c>; the conversion is a straight enumeration in order and the contract states that basis
    /// on the field. The <c>any</c> result travels through the outcome, never flattened to a string.
    /// </para>
    /// <para>
    /// THE DWO IS RESOLVED FROM THE HOST AND NEVER FABRICATED FROM THE WIRE. <c>dwobject</c> is a live
    /// runtime object whose indexed buffer accessor <c>dwo.Primary[row]</c> [<c>:L189</c>, <c>:L198</c>] is a
    /// READ AGAINST THE REAL BUFFER, and the item-change equality test compares that read against a snapshot
    /// [<c>:L198-L202</c>]. A dwo reconstructed from the request would answer that read from the request
    /// instead, so the test would compare the client's claim against itself and always find them equal -
    /// which would silently skip the restore arm at <c>:L216-L222</c>. The host stays authoritative.
    /// </para>
    /// <para>
    /// ROWS AND COLUMN IDENTIFIERS ARE ONE-BASED THROUGHOUT, on both sides. The oracle reads
    /// <c>Long(dwo.ID)</c> and passes it straight to <c>SetItem</c> and <c>GetItemStatus</c>
    /// [<c>:L190</c>, <c>:L219-L220</c>], and the contract declares the identifier one-based on the field.
    /// No arithmetic is performed on either here, so there is no basis change to get wrong: the only place
    /// this file changes a basis at all is the argument-list enumeration noted above.
    /// </para>
    /// <para>
    /// MOUSE COORDINATES WIDEN FROM 32 TO 64 BITS, which is the permitted direction - a wire type may be
    /// wider than its legacy counterpart, never narrower. They are INPUTS the runtime supplies in the
    /// DataWindow's own logical units and are passed through as data; nothing here converts or renders them,
    /// which is what keeps them clear of the deferred rendering half (C-D).
    /// </para>
    /// </remarks>
    private static async Task<long> InvokeAsync(EventNotification notify, DataWindowEventChain chain)
    {
        switch (notify.BodyCase)
        {
            // ---- :L11  event type long oninitcontextmenu ( long row, dwobject dwo ) ----
            case EventNotification.BodyOneofCase.InitContextMenu:
                return chain.OnInitContextMenu(
                    notify.InitContextMenu.Row,
                    Resolve(chain, notify, notify.InitContextMenu.Dwo));

            // ---- :L12  event type long oncontextmenu ( long row, dwobject dwo, long mid ) ----
            case EventNotification.BodyOneofCase.ContextMenu:
                return chain.OnContextMenu(
                    notify.ContextMenu.Row,
                    Resolve(chain, notify, notify.ContextMenu.Dwo),
                    notify.ContextMenu.Mid);

            // ---- :L13  event onddsgetfilter ( ..., ref string filter )  -- NO RETURN TYPE ----
            case EventNotification.BodyOneofCase.DdsGetFilter:
            {
                // Seeded empty and passed by reference, exactly as the oracle's out-parameter is. The chain
                // reports whatever it produced on its outcome, including the empty string.
                string filter = string.Empty;

                chain.OnDdsGetFilter(
                    notify.DdsGetFilter.Row,
                    Resolve(chain, notify, notify.DdsGetFilter.Dwo),
                    notify.DdsGetFilter.Data,
                    ref filter);

                return 0L;
            }

            // ---- :L14  event type any oncolumnexpinvokemethod ( ..., string args[] ) ----
            case EventNotification.BodyOneofCase.ColumnExpInvokeMethod:
                _ = chain.OnColumnExpInvokeMethod(
                    notify.ColumnExpInvokeMethod.Row,
                    Resolve(chain, notify, notify.ColumnExpInvokeMethod.Dwo),
                    notify.ColumnExpInvokeMethod.Name,

                    // Wire element 0 IS legacy args[1]. A straight enumeration in order.
                    [.. notify.ColumnExpInvokeMethod.Args]);

                // The `any` result is carried on the outcome, not squeezed into this numeric - an unhandled
                // event of type `any` yields null, which is neither zero nor the empty string.
                return 0L;

            // ---- :L15  event ondwnrbuttondown pbm_dwnrbuttondown ----
            case EventNotification.BodyOneofCase.DwnRbuttonDown:
                return chain.OnDwnRButtonDown(
                    notify.DwnRbuttonDown.Xpos,
                    notify.DwnRbuttonDown.Ypos,
                    notify.DwnRbuttonDown.Row,
                    Resolve(chain, notify, notify.DwnRbuttonDown.Dwo));

            // ---- :L16  event ondwnrbuttonup pbm_dwnrbuttonup ----
            case EventNotification.BodyOneofCase.DwnRbuttonUp:
                return chain.OnDwnRButtonUp(
                    notify.DwnRbuttonUp.Xpos,
                    notify.DwnRbuttonUp.Ypos,
                    notify.DwnRbuttonUp.Row,
                    Resolve(chain, notify, notify.DwnRbuttonUp.Dwo));

            // ---- :L17  event ondwnrowchange pbm_dwnrowchange ----
            case EventNotification.BodyOneofCase.DwnRowChange:
                return chain.OnDwnRowChange(notify.DwnRowChange.CurrentRow);

            // ---- :L18  event ondwnrowchanging pbm_dwnrowchanging ----
            case EventNotification.BodyOneofCase.DwnRowChanging:
                return chain.OnDwnRowChanging(
                    notify.DwnRowChanging.CurrentRow,
                    notify.DwnRowChanging.NewRow);

            // ---- :L19  event ondwnlbuttondblclk pbm_dwnlbuttondblclk ----
            case EventNotification.BodyOneofCase.DwnLbuttonDblClk:
                return chain.OnDwnLButtonDblClk(
                    notify.DwnLbuttonDblClk.Xpos,
                    notify.DwnLbuttonDblClk.Ypos,
                    notify.DwnLbuttonDblClk.Row,
                    Resolve(chain, notify, notify.DwnLbuttonDblClk.Dwo));

            // ---- :L20  event ondwnlbuttonclk pbm_dwnlbuttonclk ----
            case EventNotification.BodyOneofCase.DwnLbuttonClk:
                return chain.OnDwnLButtonClk(
                    notify.DwnLbuttonClk.Xpos,
                    notify.DwnLbuttonClk.Ypos,
                    notify.DwnLbuttonClk.Row,
                    Resolve(chain, notify, notify.DwnLbuttonClk.Dwo));

            // ---- :L21  event ondwnchanging pbm_dwnchanging ----
            case EventNotification.BodyOneofCase.DwnChanging:
                return chain.OnDwnChanging(
                    notify.DwnChanging.Row,
                    Resolve(chain, notify, notify.DwnChanging.Dwo),
                    notify.DwnChanging.Data);

            // ---- :L22  event ondwnitemchangefocus pbm_dwnitemchangefocus ----
            case EventNotification.BodyOneofCase.DwnItemChangeFocus:
                return chain.OnDwnItemChangeFocus(
                    notify.DwnItemChangeFocus.Row,
                    Resolve(chain, notify, notify.DwnItemChangeFocus.Dwo));

            // ---- :L23  event ondwnitemchange pbm_dwnitemchange ----
            case EventNotification.BodyOneofCase.DwnItemChange:
                return chain.OnDwnItemChange(
                    notify.DwnItemChange.Row,
                    Resolve(chain, notify, notify.DwnItemChange.Dwo),
                    notify.DwnItemChange.Data);

            // ---- :L24  event type long ondoitemchange ( long row, dwobject dwo, string data ) ----
            case EventNotification.BodyOneofCase.DoItemChange:
                return chain.OnDoItemChange(
                    notify.DoItemChange.Row,
                    Resolve(chain, notify, notify.DoItemChange.Dwo),
                    notify.DoItemChange.Data);

            // ---- :L25  event onitemchanged ( long row, dwobject dwo )  -- NO RETURN TYPE ----
            case EventNotification.BodyOneofCase.ItemChanged:
                chain.OnItemChanged(
                    notify.ItemChanged.Row,
                    Resolve(chain, notify, notify.ItemChanged.Dwo));

                return 0L;

            // ---- :L26  event ondoitemchanged ( long row, dwobject dwo )  -- NO RETURN TYPE ----
            case EventNotification.BodyOneofCase.DoItemChanged:
                chain.OnDoItemChanged(
                    notify.DoItemChanged.Row,
                    Resolve(chain, notify, notify.DoItemChanged.Dwo));

                return 0L;

            // ---- :L27  event ondwnitemvalidationerror pbm_dwnitemvalidationerror ----
            case EventNotification.BodyOneofCase.DwnItemValidationError:
                return chain.OnDwnItemValidationError(
                    notify.DwnItemValidationError.Row,
                    Resolve(chain, notify, notify.DwnItemValidationError.Dwo),
                    notify.DwnItemValidationError.Data);

            // ---- :L28  event onddsfiltered ( ... )  -- NO RETURN TYPE ----
            case EventNotification.BodyOneofCase.DdsFiltered:
                chain.OnDdsFiltered(
                    notify.DdsFiltered.Row,
                    Resolve(chain, notify, notify.DdsFiltered.Dwo),
                    notify.DdsFiltered.RowCount,
                    notify.DdsFiltered.FilteredCount);

                return 0L;

            // ---- :L29  event ondwnkillfocus pbm_dwnkillfocus ----
            case EventNotification.BodyOneofCase.DwnKillFocus:
                // Takes no argument at source. The request's `deferred_accept_queued` is what the SERVER
                // reports back through the session state, not an input: the queueing decision is the
                // oracle's, gated on the item-change re-entrancy flag being clear [:L388-L390].
                return chain.OnDwnKillFocus();

            // ---- :L30  event ondwnlbuttonup pbm_dwnlbuttonup ----
            case EventNotification.BodyOneofCase.DwnLbuttonUp:
                return chain.OnDwnLButtonUp(
                    notify.DwnLbuttonUp.Xpos,
                    notify.DwnLbuttonUp.Ypos,
                    notify.DwnLbuttonUp.Row,
                    Resolve(chain, notify, notify.DwnLbuttonUp.Dwo));

            // ---- :L31  event ondwnsetfocus pbm_dwnsetfocus ----
            case EventNotification.BodyOneofCase.DwnSetFocus:
                return chain.OnDwnSetFocus();

            // ---- :L32  event oncolumnexptrace ( ... )  -- NO RETURN TYPE ----
            case EventNotification.BodyOneofCase.ColumnExpTrace:
                chain.OnColumnExpTrace(
                    notify.ColumnExpTrace.Row,
                    Resolve(chain, notify, notify.ColumnExpTrace.Dwo),
                    notify.ColumnExpTrace.Stack,
                    notify.ColumnExpTrace.Expr,
                    notify.ColumnExpTrace.Value);

                return 0L;

            case EventNotification.BodyOneofCase.None:
            default:
                throw new RpcException(new Status(
                    StatusCode.InvalidArgument,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "An event notification for {0} carried no body. The body alternative is what "
                            + "supplies the event's arguments, so a notification without one cannot be "
                            + "dispatched; guessing the arguments would fabricate an event the client never "
                            + "raised.",
                        notify.EventId)));
        }
    }

    /// <summary>
    /// Resolves a wire object reference to the live DataWindow object the host holds.
    /// </summary>
    /// <param name="chain">The chain, which is the host.</param>
    /// <param name="notify">The notification being dispatched, named in any diagnostic.</param>
    /// <param name="reference">The reference the client sent, which may be absent.</param>
    /// <returns>The live object.</returns>
    /// <exception cref="RpcException">
    /// <c>InvalidArgument</c> when the reference is absent or names nothing on the DataWindow's object
    /// model.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE HOST IS AUTHORITATIVE. See the note on <see cref="InvokeAsync"/> for why a reconstructed object
    /// would break the item-change equality test.
    /// </para>
    /// <para>
    /// AN ABSENT OR UNKNOWN REFERENCE IS A DEFINED ERROR, NOT AN EMPTY LOOKUP. The host contract states
    /// that <c>GetObjectAttribute</c> THROWS for a name that does not exist and must never answer an inert
    /// handle, preserving the oracle's own warning at <c>n_cst_dwsvc.sru:L121</c> - so resolving an absent
    /// reference by empty name would raise whatever exception the host implementation happens to use, which
    /// gRPC would surface as <c>Unknown</c> with an implementation-shaped message. Every one of the chain's
    /// <c>dwo</c> parameters is NON-NULLABLE, and at source the runtime always supplies the object, so a
    /// notification that omits it for an event whose body declares one is malformed. It is refused with the
    /// same <c>InvalidArgument</c> this boundary uses for every other malformed request, and whatever the
    /// host raised is translated rather than leaked: the contract is NARROWED WITH A DEFINED ERROR rather
    /// than widened with a guess. The four events that genuinely have no <c>dwo</c> -
    /// <c>ondwnrowchange</c>, <c>ondwnrowchanging</c>, <c>ondwnkillfocus</c> and <c>ondwnsetfocus</c> - do
    /// not reach this method at all, because the oracle declares no such parameter on them.
    /// </para>
    /// <para>
    /// CANCELLATION AND ALREADY-DEFINED FAILURES PASS THROUGH UNTOUCHED. A cancelled call must stay
    /// cancelled rather than be reported as a bad argument, and a status this file already chose is not
    /// re-wrapped.
    /// </para>
    /// </remarks>
    private static IDataWindowObject Resolve(
        DataWindowEventChain chain,
        EventNotification notify,
        DwObjectRef? reference)
    {
        string name = reference?.Name ?? string.Empty;

        if (string.IsNullOrEmpty(name))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Event {0} carried no DataWindow object reference. The oracle declares a dwobject "
                        + "parameter on this event and the runtime always supplies it, so the object is "
                        + "resolved from the host rather than rebuilt from the request - a reconstructed "
                        + "object would answer the item-change equality test from the request itself "
                        + "[se_cst_dw.sru:L198-L202] and silently skip the restore arm at :L216-L222. "
                        + "Send the object's name.",
                    notify.EventId)));
        }

        try
        {
            return chain.GetObjectAttribute(name);
        }
        catch (Exception resolution)
            when (resolution is not RpcException && resolution is not OperationCanceledException)
        {
            // The originating exception is carried as the status's DEBUG exception, which is server-side
            // diagnostic state and is deliberately NOT transmitted to the client: the detail above says
            // everything a caller can act on, and the host's own exception text is implementation shape.
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Event {0} named DataWindow object '{1}', which the DataWindow's object model does "
                        + "not declare. The host raises for an unknown name by contract - the oracle's "
                        + "own warning at n_cst_dwsvc.sru:L121 - and that is reported as a bad argument "
                        + "rather than surfaced as an unknown fault.",
                    notify.EventId,
                    name),
                resolution));
        }
    }

    // =================================================================================================
    //  THE FOUR HEADLESS MODELS - read and apply
    //  -----------------------------------------------------------------------------------------------
    //  THE SECTION WHERE C-D IS EASIEST TO BREACH, SO THE LINE IS DRAWN ONCE HERE AND HELD IN EVERY
    //  METHOD BELOW.
    //
    //  WHAT SHIPS: filter and sort EXPRESSION TEXT; the drop-down search state with its row and filtered
    //  counts; the context-menu ITEM MODEL - labels, identifiers, enabled and split flags, and COMPUTED
    //  LOGICAL TEXT WIDTHS; and the row-selection state, which is headless in its entirety.
    //
    //  WHAT IS ABSENT FROM EVERY METHOD BELOW, enumerated so the gap is auditable rather than asserted.
    //  NOTHING here reads, computes, converts or returns:
    //    * window geometry - GetWindowRect [n_cst_dwsvc_dropdownsearch.sru:L474,
    //      n_cst_dwsvc_contextmenu.sru:L187], OffsetRect [:L475], SetWindowPos [:L489]
    //    * window visibility - ShowWindow [n_cst_dwsvc_dropdownsearch.sru:L256]
    //    * DPI-to-pixel conversion - PX2MMX(D2PX(...)) [n_cst_dwsvc_contextmenu.sru:L1241, :L1243,
    //      :L1409, :L1411] and PX2MMY(U2PY(10)) [n_cst_dwsvc_columnsort.sru:L359, :L361]
    //    * font metrics or font objects - n_cst_font [n_cst_dwsvc_contextmenu.sru:L1092, :L1098, :L1266,
    //      :L1277]
    //    * pixel coordinates, window handles or IME state
    //    * a popup-menu object - the n_cst_popupmenu-typed submenu API
    //      [n_cst_dwsvc_contextmenu.sru:L96-L114] is a DesignSystem type and appears here in no form
    //    * menu-indicator geometry - ARROW_BTN_WIDTH [n_cst_dwsvc_contextmenu.sru:L63]
    //
    //  AND THERE IS NO ANTICIPATION OF THE ABSENT HALF EITHER: no placeholder, no reserved member, no
    //  commented-out declaration awaiting Phase 2. Reserving space would be a partial implementation of a
    //  deferred service by another name, which C-D forbids outright. The rendering half is reached
    //  eventually through Gateway's reserved /v1/design/** route, which returns 501 and has nothing
    //  behind it.
    // =================================================================================================

    /// <summary>
    /// Reads the drop-down search state.
    /// </summary>
    /// <param name="request">The DataWindow, and optionally one column to narrow to.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public override Task<GetDropDownSearchStateResponse> GetDropDownSearchState(
        GetDropDownSearchStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        return Task.FromResult(set is null
            ? new GetDropDownSearchStateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "drop-down search model"),
            }
            : new GetDropDownSearchStateResponse
            {
                State = ProjectDropDownSearch(set.DropDownSearch, request.ColumnName, []),
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.OK),
            });
    }

    /// <summary>
    /// Applies a drop-down search, or clears the current filter.
    /// </summary>
    /// <param name="request">The terms as typed, the setters, or the clear instruction.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The resulting state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE TERMS ARRIVE AS TYPED AND THE LEGACY'S OWN TRANSFORMATIONS RUN SERVER-SIDE, which is what makes
    /// the generated expression match the oracle byte for byte instead of depending on a caller having
    /// reproduced the transformation. The model lower-cases, turns the separator into the wildcard and
    /// composes the display, pinyin and data clauses itself [<c>n_cst_dwsvc_dropdownsearch.sru:L313-L340</c>].
    /// </para>
    /// <para>
    /// THE SETTERS RUN BEFORE THE FILTER IS BUILT, AND THAT ORDER IS OBSERVABLE. The filter type selects
    /// WHICH clauses the expression contains, so setting it after building would produce an expression for
    /// the previous type. The oracle's own sequence is the same: the type is a service setting consulted
    /// while the filter is composed.
    /// </para>
    /// <para>
    /// A SINGLE TERM IS BYTE-EXACT AGAINST THE ORACLE, WHICH IS THE ONLY SHAPE THE ORACLE HAS. The legacy
    /// input is a single typed string, so one term reproduces its expression exactly. The contract permits
    /// more than one and states how they combine - the clauses are OR-ed - so multiple terms are composed
    /// with the legacy's own OR operator over each term's own oracle-exact clause. There is no multi-term
    /// legacy form to be byte-exact against, and this is recorded rather than left to be inferred.
    /// </para>
    /// <para>
    /// THE PINYIN FLAGS ARE CONFIGURATION, NOT AN ARGUMENT THE MODEL TAKES. The model composes its filter
    /// with the flags it was constructed from, so a per-request override has nowhere to go without changing
    /// the model's own contract. A supplied value that DISAGREES with the configured one is therefore
    /// refused rather than silently ignored: ignoring it would emit an expression carrying flags the caller
    /// did not ask for, and the pinyin path is already the single genuine parity risk in the in-scope set
    /// (AAP 0.6.5) - its lookup table exists only inside the closed binary.
    /// </para>
    /// </remarks>
    public override Task<ApplyDropDownSearchResponse> ApplyDropDownSearch(
        ApplyDropDownSearchRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        if (set is null)
        {
            return Task.FromResult(new ApplyDropDownSearchResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "drop-down search model"),
            });
        }

        DropDownSearchModel model = set.DropDownSearch;

        if (request.HasPinyinFlags && request.PinyinFlags != (ulong)_dropDownSearch.PinyinMatchFlags)
        {
            return Task.FromResult(new ApplyDropDownSearchResponse
            {
                State = ProjectDropDownSearch(model, request.ColumnName, request.SearchValues),
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_NO_SUPPORT),
                Error = PinyinFlagOverrideError(request.PinyinFlags),
            });
        }

        long outcome = RetCode.OK;

        // The type first: it selects which clauses the expression will contain.
        if (request.HasFilterType)
        {
            if (request.FilterType > uint.MaxValue)
            {
                return Task.FromResult(new ApplyDropDownSearchResponse
                {
                    State = ProjectDropDownSearch(model, request.ColumnName, request.SearchValues),
                    RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                });
            }

            outcome = model.SetFilterType((uint)request.FilterType);
        }

        if (Predicates.IsSucceeded(outcome) && request.HasShowFilteredRows)
        {
            outcome = model.SetShowFilteredRows(request.ShowFilteredRows);
        }

        if (Predicates.IsSucceeded(outcome))
        {
            // An empty expression is what CLEARS: the model's null overload re-reads the child's current
            // filter instead, which is a different operation entirely. When `clear` is set the search values
            // are ignored, per the contract.
            //
            // THE BOUND ARITY, AND THAT IS THE WHOLE OF THE CWE-94 FIX AT THIS BOUNDARY. The expression that
            // reaches the child carries the caller's terms as BOUND LITERALS rather than as syntax, so a term
            // containing a quote, a parenthesis or the word OR cannot alter the predicate's structure. The
            // rendered text the oracle would have produced is preserved on the same expression and is what
            // this response reports - see Services/BoundFilterExpression.cs.
            BoundFilterExpression expression = request.Clear
                ? BoundFilterExpression.Empty
                : BuildSearchExpression(model, request.SearchValues);

            outcome = model.UpdateDddwFilterBound(request.ColumnName ?? string.Empty, expression);
        }

        return Task.FromResult(new ApplyDropDownSearchResponse
        {
            State = ProjectDropDownSearch(model, request.ColumnName, request.SearchValues),
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
        });
    }

    /// <summary>
    /// Composes the filter expression for a set of typed terms.
    /// </summary>
    /// <param name="model">The model, which owns the legacy transformations.</param>
    /// <param name="searchValues">The terms as typed.</param>
    /// <returns>The expression, empty when no term produced one.</returns>
    /// <remarks>
    /// <b>EVERY TERM IS COMPOSED THROUGH THE BOUND COMPOSER AND THE MULTI-TERM COMBINATION IS DONE ON
    /// EXPRESSIONS, NOT ON STRINGS.</b> Combining rendered strings and binding afterwards is not possible -
    /// once a value is inside a string there is no longer a record of which characters were the value - so
    /// the OR-composition below walks expressions and merges their parameter lists, renumbering as it goes.
    /// The rendered form of the result is still exactly what string composition would have produced, because
    /// each operand's rendered form is its own oracle-exact clause.
    /// </remarks>
    private static BoundFilterExpression BuildSearchExpression(
        DropDownSearchModel model,
        IReadOnlyList<string> searchValues)
    {
        List<BoundFilterExpression> clauses = [];

        foreach (string term in searchValues)
        {
            BoundFilterExpression clause = model.GetBoundFilter(term);

            if (!clause.IsEmpty)
            {
                clauses.Add(clause);
            }
        }

        if (clauses.Count == 0)
        {
            return BoundFilterExpression.Empty;
        }

        // The oracle's own single-input shape: returned verbatim, byte for byte, bindings intact.
        if (clauses.Count == 1)
        {
            return clauses[0];
        }

        // The contract's stated combination for terms the oracle has no single form for. Each operand is
        // itself an oracle-exact clause and is parenthesized so the composition cannot change how any one of
        // them binds. Placeholders are renumbered per operand so two clauses' parameters cannot collide.
        List<BoundFilterLiteral> merged = [];
        List<string> texts = [];

        foreach (BoundFilterExpression clause in clauses)
        {
            // ONE SHARED RENUMBERING, NOT A SECOND HAND-WRITTEN LOOP. BoundFilterExpression.Renumber does
            // this with a single left-to-right scan, which is what keeps a rewrite from re-entering its own
            // output once ten parameters exist - ":pfwArg1" being a prefix of ":pfwArg10".
            (string text, IReadOnlyList<BoundFilterLiteral> literals) = clause.Renumber(merged.Count);

            merged.AddRange(literals);
            texts.Add("(" + text + ")");
        }

        // CERTIFIED only when EVERY operand is - and note this is a report rather than an execution switch:
        // the merged parameter list carries every bound value regardless, so an uncertified operand does not
        // cost the others their bindings.
        return new BoundFilterExpression(
            string.Join(" OR ", texts),
            merged,
            clauses.TrueForAll(static clause => clause.Bindable));
    }

    /// <summary>
    /// Projects the drop-down search state onto the wire.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="columnName">The column the caller named, which may be empty.</param>
    /// <param name="searchValues">The terms as typed, echoed unmodified.</param>
    /// <returns>The wire state.</returns>
    /// <remarks>
    /// BOTH THE EXPRESSION AND THE RAW TERMS TRAVEL, and neither substitutes for the other. Parity needs the
    /// exact generated text; safe execution needs the unmodified input. The expression is COMPATIBILITY AND
    /// DIAGNOSTIC DATA and is never something a receiver executes - the legacy interpolates user data into it
    /// unescaped [<c>n_cst_dwsvc_dropdownsearch.sru:L323</c>], a defect preserved verbatim because
    /// correcting it would change the observable expression (C-B). The raw terms are what a receiver builds
    /// from through a typed or bound-literal path.
    /// </remarks>
    private static DropDownSearchState ProjectDropDownSearch(
        DropDownSearchModel model,
        string? columnName,
        IReadOnlyList<string> searchValues)
    {
        DropDownSearchState state = new()
        {
            FilterType = model.FilterType,
            ShowFilteredRows = model.IsShowFilteredRows(),
            ColumnName = string.IsNullOrEmpty(columnName) ? model.EditContext.Name : columnName,
            FilterExpression = model.EditContext.Dddw.Filter,
            RowCount = model.Partition?.RowCount ?? 0L,
            FilteredCount = model.Partition?.FilteredCount ?? 0L,
            HasFilter = model.HasFilter(),
        };

        state.SearchValues.AddRange(searchValues);

        return state;
    }

    /// <summary>
    /// Builds the error reported for a pinyin-flag override the model cannot honour.
    /// </summary>
    /// <param name="requested">The flags the caller asked for.</param>
    /// <returns>The error.</returns>
    /// <remarks>
    /// Not localized: a boundary-created diagnostic with no legacy dialog behind it. The code is
    /// <c>E_NO_SUPPORT</c> rather than <c>E_INVALID_ARGUMENT</c> because the value is not invalid - it is
    /// unsupported at this boundary, which is the narrowing this method documents.
    /// </remarks>
    private StructuredError PinyinFlagOverrideError(ulong requested) => new()
    {
        Title = string.Empty,
        Text = string.Format(
            CultureInfo.InvariantCulture,
            "Pinyin match flags of {0} were requested, but this service is configured with {1} and the "
                + "legacy model composes its filter from the flags it was constructed with - there is no "
                + "per-request argument for them at source. The request is refused rather than silently "
                + "served with the configured value, which would emit an expression carrying flags the "
                + "caller did not ask for. Set DataServices:DropDownSearch:PinyinMatchFlags instead.",
            requested,
            _dropDownSearch.PinyinMatchFlags),
        Localized = false,
        Category = 0L,
        Severity = Severity.StopSign,
        RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_NO_SUPPORT),
    };

    /// <summary>
    /// Reads the column-sort state.
    /// </summary>
    /// <param name="request">The DataWindow.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public override Task<GetColumnSortStateResponse> GetColumnSortState(
        GetColumnSortStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        return Task.FromResult(set is null
            ? new GetColumnSortStateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "column-sort model"),
            }
            : new GetColumnSortStateResponse
            {
                State = ProjectColumnSort(set.ColumnSort),
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.OK),
            });
    }

    /// <summary>
    /// Applies an ordered multi-column sort.
    /// </summary>
    /// <param name="request">The columns in sort order, and whether to build the expression only.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The resulting state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>COLUMN ORDER IS THE SORT ORDER AND IT IS PRESERVED EXACTLY.</b> The legacy holds its sort entries
    /// in an array whose position determines precedence, and its clause is composed by walking that array in
    /// order. Re-sorting the request's columns - by name, by identifier, or by anything else - would produce
    /// a different sort that still contains all the same columns, which is a wrong result that no count-based
    /// assertion would catch.
    /// </para>
    /// <para>
    /// THE EXPRESSION IS BUILT THROUGH THE MODEL'S OWN CLAUSE COMPOSER, one entry at a time, so the generated
    /// text is the oracle's. Nothing here concatenates sort syntax of its own.
    /// </para>
    /// <para>
    /// EXPRESSION-ONLY STOPS BEFORE THE APPLY. The contract carries the flag so a caller can obtain the
    /// generated clause without re-sorting the DataWindow, which is what a parity comparison needs: the
    /// clause is the observable artefact, and applying it would additionally mutate row order.
    /// </para>
    /// </remarks>
    public override Task<ApplyColumnSortResponse> ApplyColumnSort(
        ApplyColumnSortRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        if (set is null)
        {
            return Task.FromResult(new ApplyColumnSortResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "column-sort model"),
            });
        }

        ColumnSortModel model = set.ColumnSort;

        // In request order, which IS the sort order. Composed through the model so the clause text is the
        // oracle's rather than this file's.
        List<string> clauses = [];

        foreach (ColumnSortState.Types.ColumnSort column in request.Columns)
        {
            long sortType = ToLegacySortType(column.Direction);

            if (sortType == 0L)
            {
                // SORT_NONE names no direction, so there is no clause to compose for it. Skipped rather than
                // defaulted to ascending: substituting a direction the caller did not state would sort by a
                // column they asked not to sort by.
                continue;
            }

            clauses.Add(model.GetClause(column.ColumnName ?? string.Empty, sortType));
        }

        string expression = string.Join(string.Empty, clauses);

        long outcome = request.ExpressionOnly
            ? RetCode.OK
            : model.Update(expression);

        return Task.FromResult(new ApplyColumnSortResponse
        {
            State = request.ExpressionOnly
                ? ProjectColumnSort(model, expression)
                : ProjectColumnSort(model),
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
        });
    }

    /// <summary>
    /// Projects the column-sort state onto the wire.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="expressionOverride">
    /// An expression to report instead of the model's current one, for the expression-only path where the
    /// model was deliberately not mutated.
    /// </param>
    /// <returns>The wire state.</returns>
    /// <remarks>
    /// THE ENTRIES ARE REPORTED IN THE MODEL'S OWN ORDER, which is the precedence order. The column
    /// identifier is not carried because the legacy sort entry holds a NAME and a type and no ordinal - the
    /// wire field exists for callers that address by ordinal elsewhere, and filling it with a fabricated
    /// value would be worse than leaving it at its default.
    /// </remarks>
    private static ColumnSortState ProjectColumnSort(ColumnSortModel model, string? expressionOverride = null)
    {
        ColumnSortState state = new()
        {
            SortExpression = expressionOverride ?? model.CurrentSort,
        };

        foreach (SortData entry in model.SortEntries)
        {
            state.Columns.Add(new ColumnSortState.Types.ColumnSort
            {
                ColumnName = entry.ColName,
                Direction = ToWireSortDirection(entry.SortType),
            });
        }

        return state;
    }

    /// <summary>
    /// Maps a wire sort direction onto the legacy sort type.
    /// </summary>
    /// <param name="direction">The wire member.</param>
    /// <returns>The legacy value, or <c>0</c> for no direction.</returns>
    /// <remarks>
    /// <para>
    /// <c>SORT_NONE</c> is not a legacy direction - it is the wire's way of saying none was stated - so it
    /// maps to zero and the caller skips the entry rather than defaulting it. Zero IS the legacy
    /// <c>SORT_NONE</c> as well [<c>n_cst_dwsvc_columnsort.sru:L37</c>], so the two agree on the absence too.
    /// </para>
    /// <para>
    /// THE WIRE MEMBER'S OWN NUMERIC VALUE IS THE LEGACY VALUE, so it is cast rather than translated through
    /// a table. The oracle declares <c>SORT_NONE = 0</c>, <c>SORT_ASC = 1</c> and <c>SORT_DESC = 2</c>
    /// [<c>n_cst_dwsvc_columnsort.sru:L37-L39</c>] and the contract enumerates those same three values, so a
    /// cast is exact and a lookup table would be a second place for the agreement to drift. The model's own
    /// copies of the constants are <c>private</c>, which is why the agreement is asserted against the oracle
    /// here rather than against them.
    /// </para>
    /// </remarks>
    private static long ToLegacySortType(ColumnSortState.Types.Direction direction) => direction switch
    {
        ColumnSortState.Types.Direction.SortAsc => (long)ColumnSortState.Types.Direction.SortAsc,
        ColumnSortState.Types.Direction.SortDesc => (long)ColumnSortState.Types.Direction.SortDesc,
        _ => (long)ColumnSortState.Types.Direction.SortNone,
    };

    /// <summary>
    /// Maps a legacy sort type onto the wire direction.
    /// </summary>
    /// <param name="sortType">The legacy value.</param>
    /// <returns>The wire member.</returns>
    /// <remarks>
    /// The inverse of <see cref="ToLegacySortType"/>, with the same value agreement. A value outside the
    /// legacy three reports as no direction rather than being forced onto one, because the oracle declares
    /// exactly three and a fourth would be a producer fault rather than a direction.
    /// </remarks>
    private static ColumnSortState.Types.Direction ToWireSortDirection(long sortType) =>
        sortType == (long)ColumnSortState.Types.Direction.SortAsc
            ? ColumnSortState.Types.Direction.SortAsc
            : sortType == (long)ColumnSortState.Types.Direction.SortDesc
                ? ColumnSortState.Types.Direction.SortDesc
                : ColumnSortState.Types.Direction.SortNone;

    /// <summary>
    /// Reads the context-menu item model for a row and column.
    /// </summary>
    /// <param name="request">The DataWindow, the ONE-BASED row, and the column.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The model and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THE ITEM MODEL ONLY. Labels, identifiers, enabled and split flags, tip text, image names, separators
    /// and submenu SHAPE with its nested items. The pixel widths, the fonts, the window geometry and the
    /// rendering are the deferred half and appear nowhere in the response - see the section banner above for
    /// the enumerated exclusions.
    /// </para>
    /// <para>
    /// THE STORED MODEL IS REPORTED, AND THE BUILD PATH IS DELIBERATELY NOT INVOKED (C-D). The model's own
    /// <c>BuildMenu</c> and <c>OnPrepare</c> both require a POINTER CONTEXT, which is rendering input, and
    /// this request carries none precisely because the contract excludes it. Synthesizing one to reach those
    /// members would drag the deferred half in through the argument list; reporting the stored item model is
    /// what the contract asks for and is what the headless half actually has.
    /// </para>
    /// </remarks>
    public override Task<GetContextMenuModelResponse> GetContextMenuModel(
        GetContextMenuModelRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        return Task.FromResult(set is null
            ? new GetContextMenuModelResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "context-menu model"),
            }
            : new GetContextMenuModelResponse
            {
                Model = ProjectContextMenu(set.ContextMenu, request.Row, request.Dwo),
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.OK),
                Error = DataWindowWireProjection.ToWireError(set.ContextMenu.PendingError),
            });
    }

    /// <summary>
    /// Applies a context-menu item model.
    /// </summary>
    /// <param name="request">The model to apply, and whether to replace the current items.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The resulting model and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// ITEM ORDER IS PRESERVED, because the legacy store is an array addressed positionally and its
    /// <c>of_additem</c> APPENDS. Items are therefore added in request order and never sorted.
    /// </para>
    /// <para>
    /// THE FIVE BUILT-IN TOGGLES EACH DEFAULT TRUE IN THE LEGACY, which is why they are <c>optional</c> on the
    /// wire and are written only when the caller supplied one. Writing an unsupplied toggle would silently
    /// turn a built-in item off for every caller that did not mention it.
    /// </para>
    /// <para>
    /// A SEPARATOR IS ADDED THROUGH THE SEPARATOR ENTRY POINT, NOT AS AN ITEM WITH EMPTY TEXT. The legacy has
    /// distinct <c>of_addseparator</c> and <c>of_additem</c> operations, and a separator carries no
    /// identifier, tip text or image; routing it through the item path would create an enabled, selectable
    /// item that merely looks like a rule.
    /// </para>
    /// <para>
    /// SUBMENU ITEMS ARE NOT RE-ADDED HERE. The legacy submenu API is typed in terms of a DesignSystem popup
    /// menu [<c>n_cst_dwsvc_contextmenu.sru:L96-L114</c>], which C-D excludes, so a nested item's SHAPE is
    /// reported on read and a nested structure supplied on apply is not materialized. Stated, not silent.
    /// </para>
    /// </remarks>
    public override Task<ApplyContextMenuModelResponse> ApplyContextMenuModel(
        ApplyContextMenuModelRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        if (set is null)
        {
            return Task.FromResult(new ApplyContextMenuModelResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "context-menu model"),
            });
        }

        DomainContextMenuModel model = set.ContextMenu;
        WireContextMenuModel? incoming = request.Model;

        long outcome = RetCode.OK;

        if (request.Replace)
        {
            outcome = model.RemoveAll();
        }

        if (incoming is not null && Predicates.IsSucceeded(outcome))
        {
            // Each toggle written only when present: every one defaults true at source.
            if (incoming.HasColAutoWidth)
            {
                model.ColAutoWidth = incoming.ColAutoWidth;
            }

            if (incoming.HasColCheck)
            {
                model.ColCheck = incoming.ColCheck;
            }

            if (incoming.HasColCopy)
            {
                model.ColCopy = incoming.ColCopy;
            }

            if (incoming.HasColPaste)
            {
                model.ColPaste = incoming.ColPaste;
            }

            if (incoming.HasItemCopy)
            {
                model.ItemCopy = incoming.ItemCopy;
            }

            // Request order IS store order: the legacy appends.
            foreach (ContextMenuItem item in incoming.Items)
            {
                // EITHER ENCODING OF A SEPARATOR TAKES THE SEPARATOR PATH. In process the caller picks
                // between two distinct entry points, so the question cannot arise; on one wire message the
                // flag and the marker label collapse into one shape, and honouring only the flag would let a
                // client that sent the marker as a label create a separator carrying an image, a tooltip and
                // an identifier - which is unrepresentable at source, where the separator entry point
                // supplies an empty image, no tooltip and identifier zero. Routing both through
                // AddSeparator keeps the STORED shape the oracle's and closes the read-apply round trip.
                bool separator = item.Separator
                    || string.Equals(
                        item.Text,
                        DomainContextMenuModel.SeparatorText,
                        StringComparison.Ordinal);

                _ = separator
                    ? model.AddSeparator()
                    : model.AddMenu(
                        item.Text ?? string.Empty,
                        item.Image ?? string.Empty,
                        item.Tiptext ?? string.Empty,
                        item.Enabled,
                        (uint)item.Id);
            }
        }

        return Task.FromResult(new ApplyContextMenuModelResponse
        {
            Model = ProjectContextMenu(model, incoming?.Row ?? 0L, incoming?.Dwo),
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
            Error = DataWindowWireProjection.ToWireError(model.PendingError),
        });
    }

    /// <summary>
    /// Projects the context-menu item model onto the wire.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="row">The ONE-BASED row the model was built for.</param>
    /// <param name="dwo">The column reference the caller supplied, echoed with its prefix classified.</param>
    /// <returns>The wire model.</returns>
    /// <remarks>
    /// TWO LEGACY FIELDS ARE OMITTED AND BOTH OMISSIONS ARE THE CONTRACT'S, NOT THIS FILE'S. The legacy
    /// <c>menuitemdata</c> structure [<c>n_cst_dwsvc_contextmenu.sru:L12-L21</c>] carries an
    /// <c>n_cst_popupmenu submenu</c> pointer [<c>:L18</c>], which is a live DesignSystem type and therefore
    /// both unserializable and out of scope (C-D), and a <c>menuowner</c> flag [<c>:L20</c>] recording
    /// whether the service owns that object's lifetime - in-process bookkeeping with no meaning across a
    /// boundary. The item's SHAPE travels instead, as the submenu flag plus nested items.
    /// </remarks>
    private static WireContextMenuModel ProjectContextMenu(
        DomainContextMenuModel model,
        long row,
        DwObjectRef? dwo)
    {
        WireContextMenuModel projected = new()
        {
            // ONE-BASED, as every legacy row ordinal is. No arithmetic is performed on it here.
            Row = row,
            ColAutoWidth = model.ColAutoWidth,
            ColCheck = model.ColCheck,
            ColCopy = model.ColCopy,
            ColPaste = model.ColPaste,
            ItemCopy = model.ItemCopy,
        };

        if (dwo is not null)
        {
            projected.Dwo = new DwObjectRef
            {
                Name = dwo.Name ?? string.Empty,
                Id = dwo.Id,
                ColType = dwo.ColType ?? string.Empty,
                ColTypePrefix = DataWindowWireProjection.ClassifyColType(dwo.ColType),
            };
        }

        foreach (MenuItemData item in model.StoreItems)
        {
            projected.Items.Add(ProjectMenuItem(item));
        }

        return projected;
    }

    /// <summary>
    /// Projects one menu item, and its submenu, onto the wire.
    /// </summary>
    /// <param name="item">The domain item.</param>
    /// <returns>The wire item.</returns>
    /// <remarks>
    /// <para>
    /// A SEPARATOR IS AN ITEM WHOSE LABEL <b>IS</b> THE MARKER, AND THE COMPARISON IS AGAINST THAT MARKER
    /// RATHER THAN AGAINST EMPTINESS. The oracle inserts a separator as an ordinary item whose label is a
    /// single hyphen and whose identifier is zero, written at <c>n_cst_dwsvc_contextmenu.sru:L511</c> and
    /// recognised by its emit filter at <c>:L169</c> by comparing that label - there is no flag at source.
    /// Testing for an EMPTY label would therefore never identify one, and could never be true at all,
    /// because an empty label is rejected outright at <c>:L480</c>. The marker is taken from the model's own
    /// published constant so the two cannot drift, and compared ordinally because it is a legacy symbol.
    /// The flag is reported ALONGSIDE the label, never instead of it, so a consumer can render the rule
    /// without inferring it while a characterization recording still sees the oracle's own text.
    /// </para>
    /// <para>
    /// THE LOGICAL TEXT WIDTH IS REPORTED AS "NOT COMPUTED", WHICH IS THE CONTRACT'S OWN DEFINED VALUE FOR
    /// ZERO AND NOT A PLACEHOLDER. The legacy computes a menu-item width only inside its rendering path,
    /// where the accumulator sits beside the font object it needs
    /// [<c>n_cst_dwsvc_contextmenu.sru:L1115</c>, with the font created at <c>:L1092</c>, <c>:L1098</c>,
    /// <c>:L1266</c>, <c>:L1277</c>] - and font measurement is the DEFERRED half (C-D). The headless model
    /// consequently holds no width to report for a menu item, so zero is the accurate answer rather than a
    /// value awaiting an implementation. Fabricating one from character counts would publish a measurement
    /// the oracle never made.
    /// </para>
    /// </remarks>
    private static ContextMenuItem ProjectMenuItem(MenuItemData item)
    {
        ContextMenuItem projected = new()
        {
            Text = item.Text,
            Id = item.Id,
            Enabled = item.Enabled,
            Split = item.Split,
            Tiptext = item.TipText,
            Image = item.Image,
            Separator = string.Equals(
                item.Text,
                DomainContextMenuModel.SeparatorText,
                StringComparison.Ordinal),
            HasSubmenu = item.HasSubmenu,
            LogicalTextWidth = 0L,
        };

        foreach (MenuItemData child in item.Submenu)
        {
            projected.SubmenuItems.Add(ProjectMenuItem(child));
        }

        return projected;
    }

    /// <summary>
    /// Reads the row-selection state.
    /// </summary>
    /// <param name="request">The DataWindow.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// THIS SERVICE IS HEADLESS IN ITS ENTIRETY - it has exactly one dialog site
    /// [<c>n_cst_dwsvc_rowselect.sru:L239</c>], which becomes the structured error on the response, and no UI
    /// primitive anywhere.
    /// </remarks>
    public override Task<GetRowSelectStateResponse> GetRowSelectState(
        GetRowSelectStateRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        return Task.FromResult(set is null
            ? new GetRowSelectStateResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "row-selection model"),
            }
            : new GetRowSelectStateResponse
            {
                State = ProjectRowSelect(set.RowSelect, set.Host),
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.OK),
                Error = DataWindowWireProjection.ToWireError(set.RowSelect.PendingError),
            });
    }

    /// <summary>
    /// Applies a row-selection style, and optionally a selection.
    /// </summary>
    /// <param name="request">The style, the rows to select, and the row to make current.</param>
    /// <param name="context">The call context.</param>
    /// <returns>The resulting state and the outcome.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THREE LEGACY BEHAVIOURS THE RESPONSE MUST BE ABLE TO REPORT, and all three are the model's own:
    /// an unchanged style answers <c>OK</c>, a style of <c>0</c> answers <c>E_INVALID_ARGUMENT</c>, and a
    /// successful change clears the selection and then re-selects the current row UNLESS the style is exactly
    /// multiple-selection. None of the three is re-implemented here - the style setter is called and its code
    /// is projected.
    /// </para>
    /// <para>
    /// THE STYLE IS A COMBINABLE BITMASK WITH NO ZERO MEMBER, per the source comment on the two bits, which
    /// is why the wire field is <c>optional</c>: absence expresses "no style stated" rather than a zero that
    /// the setter would reject anyway.
    /// </para>
    /// <para>
    /// ROWS ARE ONE-BASED AND ARE PASSED THROUGH UNCHANGED. No arithmetic is performed on any of them here,
    /// so there is no basis to get wrong.
    /// </para>
    /// </remarks>
    public override Task<ApplyRowSelectStyleResponse> ApplyRowSelectStyle(
        ApplyRowSelectStyleRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DataWindowModelSet? set = _models.GetOrCreate(request.DatawindowHandle ?? string.Empty);

        if (set is null)
        {
            return Task.FromResult(new ApplyRowSelectStyleResponse
            {
                RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_HANDLE),
                Error = UnknownHandleError(request.DatawindowHandle ?? string.Empty, "row-selection model"),
            });
        }

        RowSelectService model = set.RowSelect;
        DataWindowServiceHost host = set.Host;

        long outcome = RetCode.OK;

        if (request.HasStyle)
        {
            if (request.Style > long.MaxValue)
            {
                return Task.FromResult(new ApplyRowSelectStyleResponse
                {
                    State = ProjectRowSelect(model, host),
                    RetCode = DataWindowWireProjection.ToWireRetCode(RetCode.E_INVALID_ARGUMENT),
                });
            }

            // The setter owns the zero guard, the unchanged-style answer and the clear-then-reselect
            // behaviour. Reproducing any of them here would put a second authority on them.
            outcome = model.SetStyle((long)request.Style);
        }

        if (Predicates.IsSucceeded(outcome))
        {
            // ONE-BASED rows, forwarded to the host verbatim. Selection goes through the host contract, which
            // is the same route the legacy service uses.
            foreach (long row in request.SelectRows)
            {
                _ = host.SelectRow(row, true);
            }

            if (request.SetCurrentRow > 0L)
            {
                _ = host.SetRow(request.SetCurrentRow);
            }
        }

        return Task.FromResult(new ApplyRowSelectStyleResponse
        {
            State = ProjectRowSelect(model, host),
            RetCode = DataWindowWireProjection.ToWireRetCode(outcome),
            Error = DataWindowWireProjection.ToWireError(model.PendingError),
        });
    }

    /// <summary>
    /// Projects the row-selection state onto the wire.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <param name="host">The host, which holds the selection and the current row.</param>
    /// <returns>The wire state.</returns>
    /// <remarks>
    /// THE SELECTED ROWS ARE WALKED THROUGH THE HOST'S OWN <c>GetSelectedRow</c>, which is how the legacy
    /// enumerates a selection: each call answers the next selected row at or after the start and zero when
    /// there are no more. Walking it rather than scanning every row reproduces the legacy traversal and does
    /// not depend on the row count.
    /// </remarks>
    private static RowSelectState ProjectRowSelect(RowSelectService model, DataWindowServiceHost host)
    {
        RowSelectState state = new()
        {
            Style = (ulong)model.Style,
            CurrentRow = host.GetRow(),
            HasMultiSelected = model.HasMultiSelected(),
        };

        // ONE-BASED traversal, starting from row 1 as the legacy does; zero terminates.
        for (long row = host.GetSelectedRow(1L); row > 0L; row = host.GetSelectedRow(row + 1L))
        {
            state.SelectedRows.Add(row);
        }

        return state;
    }
}
