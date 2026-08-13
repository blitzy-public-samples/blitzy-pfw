// ==================================================================================================
//  RestProjectionEndpoints.cs - THE THIN REST PROJECTION OF THIS SERVICE'S OWN gRPC SURFACE
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS FILE IS
//  The REST projection of contract C-03 (dataservices.v1.DataWindowService) and contract C-04
//  (dataservices.v1.ColumnExpressionService), declared on this service's REST listener - port 5102,
//  inside the 5101-5105 band the environment fixes (constraint C-L). Forty operations under
//  /v1/datawindow/**, spelled exactly as shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml
//  spells them, so that Gateway's documented /v1/datawindow/** ingress path and this projection
//  correspond route for route.
//
//  ITS ONLY INTENDED CONSUMER IS GATEWAY. This is a projection, not a second API, and not a way for
//  a caller to bypass gRPC for the hard parts.
//
//  WHY gRPC IS PRIMARY AND THIS IS ONLY A PROJECTION (constraint C-K)
//  The transport for each service in this system was decided from THE SHAPE OF THE INTERFACE BEING
//  REPLACED, not chosen once and applied uniformly. DataServices' legacy interface is an ordered
//  22-event chain - 9 semantic and 13 raw `pbm_dwn*`
//  [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L11-L32] - carrying three properties
//  that decide it:
//
//    1. A `ref string` OUT-PARAMETER. `onddsgetfilter(row, dwo, data, ref string filter)` at
//       [:L13] produces its result THROUGH the parameter, so "assigned an empty filter" and "did
//       not touch the parameter" are two different instructions.
//    2. AN `any` RETURN OVER A `string args[]` ARGUMENT. `oncolumnexpinvokemethod` at [:L14]
//       returns `any`, so a macro returning a decimal, a date or a null must stay distinguishable
//       from one returning their string renderings.
//    3. THE ORDERED CHAIN ITSELF, with a TRI-VALUED veto and a FOUR-VALUE item-change alphabet.
//
//  Protobuf over gRPC is the only transport in the mandated stack that carries all three. JSON over
//  REST would lose both the ordering and the typed veto. So gRPC is primary, the protocol
//  definitions in PowerFramework.Contracts are the authority, and this file exists so that Gateway
//  can compose a REST ingress over them.
//
//  ------------------------------------------------------------------------------------------------
//  THE CENTRAL RESPONSIBILITY: ONE SHARED STATUS MAPPING, AND `Aborted` BECOMES `409`
//  ------------------------------------------------------------------------------------------------
//  The status translation is the substantive part of a projection, and it is implemented EXACTLY
//  ONCE here - `ProjectStatus` plus `ProjectAsync` - and used by every one of the forty
//  routes. There is no per-endpoint `try`/`catch (RpcException)` anywhere in this file. One mapper
//  is both the correctness property (forty copies would drift) and the coverage property
//  (constraint C-H measures a line gate per service, and one well-tested mapper is reachable from
//  every route).
//
//  The full translation, which is docs/CONTRACTS.md 12.1's and gateway.v1.yaml's, restated here so
//  the projection is auditable from this file alone:
//
//      gRPC status              HTTP   Note
//      ---------------------    ----   --------------------------------------------------------
//      OK                       200    The canonical protobuf JSON mapping of the response message
//      Aborted                  409    MANDATORY AND CANONICAL - an optimistic-concurrency
//                                      mismatch, carrying common.v1.ConflictDetail UNCHANGED
//      InvalidArgument          400    Carries the originating legacy retCode
//      Unauthenticated          401
//      PermissionDenied         403    A valid-but-insufficient credential, distinct from none
//      NotFound                 404
//      Unimplemented            501
//      Unavailable              503    502 instead when no response arrived at all - see below
//      DeadlineExceeded         504
//      Internal / Unknown       500    With the statement field redacted - see DbError
//
//  Four statuses the published table does not name are mapped on the canonical gRPC-to-HTTP
//  correspondence rather than left to the default, because C-03 and C-04 can produce all four:
//  FailedPrecondition -> 400 (an unknown session, a handle bound to nothing, or an ordering
//  violation), OutOfRange -> 400, AlreadyExists -> 409 WITHOUT a conflict member, and
//  ResourceExhausted -> 429. Cancelled -> 502 and DataLoss -> 500 complete the set, and anything
//  unclassified is 500.
//
//  THE 502 ARM IS THE CASE THE TABLE CANNOT DESCRIBE: no gRPC response arrived at all. These
//  operations traverse exactly one outbound edge - Clients/PersistenceClient.cs - and a transport
//  failure on it surfaces as `Unavailable` carrying a debug exception. That failure mode is one the
//  DECOMPOSITION ITSELF CREATES: an in-process call cannot fail in transit and a network call can,
//  so handling it is required BY the transition rather than layered on top of it.
//
//  NO SILENT OVERWRITE, ANYWHERE. An `Aborted` is never swallowed, never retried, never collapsed
//  into a generic 500, and its conflict detail is never dropped or reshaped: the current row state
//  is precisely what lets a caller choose between retrying and surfacing. THIS FILE ADDS NO RETRY
//  OF ANY KIND - transient-fault resilience belongs to the typed clients registered in Program.cs,
//  and a concurrency conflict is not a transient fault.
//
//  AND THE HTTP STATUS IS NEVER DERIVED FROM A SUCCESS PREDICATE. The preserved return-code algebra
//  is tri-state: `IsSucceeded` tests `>= 0`, so PREVENT (1) reads as a SUCCESS
//  [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13]; `IsFailed` tests `< 0` WITH AN EXPLICIT
//  EXCLUSION OF CANCELLED (-2) [isfailed.srf:L11-L13], so cancelled is NEITHER succeeded nor
//  failed; and both return false on null, so null is likewise neither. Asking "did it succeed?"
//  here would turn a prevention into a 200 and a cancellation into whatever the last arm happened
//  to be. Every status in this file is decided from the gRPC status code alone, the numeric retCode
//  is surfaced VERBATIM in the problem body, and the caller applies the algebra.
//
//  ------------------------------------------------------------------------------------------------
//  WHAT IS DELIBERATELY NOT PROJECTED - THREE STREAMS, AND THE REASON IS STRUCTURAL
//  ------------------------------------------------------------------------------------------------
//  C-03 declares sixteen methods and C-04 twenty-seven. Fifteen and twenty-five are projected. The
//  three exclusions are all BIDIRECTIONAL, and they are named individually so the boundary is
//  checkable rather than asserted:
//
//    * `EventChain` (C-03). It carries all 13 raw and all 9 semantic events as one ordered
//      conversation. The item-change and validation chain is STRICTLY SYNCHRONOUS WITH NO
//      REORDERING PERMITTED: the validation-error handler READS AND CLEARS the item-change result
//      the preceding event stashed [se_cst_dw.sru:L89-L96], so its behaviour is a function of the
//      prior event's return value, and the item-change handler fires a nested event from inside
//      itself [:L182-L253]. Part of the ordering is encoded in the topic STRING itself - the
//      item-changed topic is spelled with a leading "0-" [:L54] and the edit-changed topic with a
//      leading "1-" [:L57], and the broker's dispatch order derives from the lexical sort of the
//      name. JSON over independent REST requests loses that, and would collapse the TRI-VALUED veto
//      to a boolean the moment it was flattened - which silently converts a deep prevention into a
//      shallow one and lets the events the caller meant to stop fire anyway.
//    * `InvokeMethodChannel` (C-04). INVERTED: the legacy expects THE APPLICATION to implement the
//      macro switch, so across a boundary DataServices must call BACK into its client to finish a
//      calculation. An inverted stream has no request/response direction to project at all.
//    * `TraceChannel` (C-04). Inverted for the same structural reason.
//
//  A PARTIAL PROJECTION WOULD BE WORSE THAN NONE: a consumer would receive part of an ordered chain
//  with no way to know what it had missed. Consumers needing any of the three use this service's
//  gRPC surface directly, which is a documented gap deliberately preferred over an invented
//  representation. Nothing in this file polls, flattens or approximates them.
//
//  THE TWO SERVER STREAMS ARE PROJECTED, and the difference from the three above is the whole
//  point. `Retrieve` (C-03) and `EventStream` (C-04) are SERVER streams: one request, and a
//  determinate response sequence the server produces and the client consumes in order. There is no
//  per-message veto to lose and no client-to-server message to invert, so each projects to one
//  operation whose response is that same sequence as an ordered collection - a bare JSON array, not
//  an invented envelope, because the protocol definition declares no envelope message for a stream.
//  THE CHUNKING CONTRACT TRAVELS UNCHANGED: each element keeps its own chunk index, its final flag,
//  its own row count and the running cumulative count, so the last element is identifiable as the
//  last rather than inferred from the collection ending. Nothing is truncated and nothing is
//  flattened into a rowset.
//
//  ------------------------------------------------------------------------------------------------
//  STRUCTURED ERRORS: THIS FILE RELAYS THEM, IT DOES NOT COMPOSE THEM (constraints C-B, C-K)
//  ------------------------------------------------------------------------------------------------
//  Every MessageBox and MessageBoxEx reachable from in-scope logic became a
//  dataservices.v1.StructuredError, and ONLY THE DELIVERY CHANNEL CHANGED: the text, the
//  localization category, the Sprintf substitution arguments, the severity and - for an expression
//  parse failure - the expression text and the NUMERIC caret position are all preserved. The live
//  census, counted in the oracle, is FORTY sites:
//
//      n_cst_dwsvc_columnexp.sru        28   MessageBox("错误", <msg>, StopSign!) - NO localization
//      n_cst_dwsvc_contextmenu.sru      10   localized, CAT_DWSVC, with Sprintf arguments
//      se_cst_dw.sru:L357                1   localized, CAT_DWSVC, title AND fallback body
//      n_cst_dwsvc_rowselect.sru:L239    1   localized, CAT_DWSVC, with Sprintf arguments
//      n_cst_dwsvc.sru, _columnsort,
//      _dropdownsearch                   0
//
//  THE LOCALIZATION INCONSISTENCY IS THE POINT, NOT A BUG TO FIX. All 28 column-expression messages
//  are hardcoded Chinese that do NOT route through the localization layer, while the validation
//  path's message DOES - `MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"), sErrMsg, StopSign!)` at
//  [se_cst_dw.sru:L357]. That is an accident of where the code was written, not a pattern, and
//  harmonizing it would change observable output, which is exactly the silent correction the
//  mandate forbids. `StructuredError.localized` exists to CARRY that inconsistency, and
//  `StructuredError.category` is meaningful only when it is set.
//
//  SO THIS FILE CONSUMES NO LOCALIZATION SURFACE AND DECLARES NO ERROR SHAPE OF ITS OWN, AND BOTH
//  ABSENCES ARE DELIBERATE:
//    * The text a StructuredError carries is already AFTER any localization the legacy itself
//      performed and AFTER Sprintf substitution, applied in the layer that owns each legacy site.
//      Re-localizing here would double-translate the twelve sites that do route through the
//      category and would wrongly translate the twenty-eight that deliberately do not. There is
//      therefore no `I18n` reference in this file, and no missing-translation warning either -
//      the silent-passthrough fallback is preserved behaviour and reporting on it would change it.
//    * Expressions/ParseErrorFormatter.cs already rendered the caret gutter, whose width is
//      measured in BYTES (`LenA`, where a wide character counts as two) rather than characters
//      [n_cst_dwsvc_columnexp.sru:L2402-L2407]. Re-rendering it here would be a second
//      implementation of a byte-exact format. This file transports `expression`, the numeric caret
//      position and the message as the separate fields the contract declares, untouched.
//    * A parallel error record declared here would be a second source of truth for a shape the
//      contracts project already publishes, which constraint C-A forbids.
//
//  DEFAULT VALUES ARE FORMATTED for exactly this reason among others: `localized: false` and the
//  severity travel EXPLICITLY, so a consumer never has to infer the deliberate absence of a
//  localization category from an omitted member.
//
//  AND ONE DORMANT DIALOG STAYS DORMANT. `se_cst_dw.sru:L286` - a byte-length check raising
//  `MessageBoxEx(... StopSign!)` and returning 2 - sits INSIDE a block comment in the oracle. It is
//  carried across as commented and inert, so it has no wire representation, no route, no status and
//  no behaviour anywhere in this projection. Reviving it would be a behaviour change.
//
//  ------------------------------------------------------------------------------------------------
//  THE ONE FIELD THAT MUST NOT LEAK (constraint C-F)
//  ------------------------------------------------------------------------------------------------
//  `common.v1.DbError.sqlsyntax` carries statement text. In the legacy it carried the COMPLETE
//  generated statement including interpolated literal values - the mechanical root being
//  `DisableBind=1` in the connection parameters, which means PowerBuilder does not use bind
//  variables [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129] - and the
//  legacy logger performed no redaction at all.
//
//  REDACTION IS PERSISTENCE'S RESPONSIBILITY AND IT HAPPENS BEFORE THE VALUE REACHES THIS SERVICE
//  (Errors/SqlRedactor.cs on that side; the protocol definition states the field carries
//  placeholders only). THIS FILE'S OBLIGATION IS NON-LEAKAGE, NOT RE-REDACTION:
//    * the value is RELAYED exactly as it arrived, through the one response formatter;
//    * it is never reconstructed, enriched, re-interpolated or merged back together with
//      parameter values;
//    * it is never written to a log record - not at Debug, not inside an exception message, and
//      not into a problem-details `detail`, which is fixed prose on every arm precisely so that no
//      upstream or caller content can reach it (CWE-209);
//    * and NO SECOND REDACTOR IS BUILT HERE. Two redactors would be two rules free to diverge. An
//      unredacted statement arriving would be a defect in Persistence to report, not something to
//      paper over at the projection.
//
//  ------------------------------------------------------------------------------------------------
//  DELIBERATE ABSENCES, each a constraint rather than an oversight
//  ------------------------------------------------------------------------------------------------
//    * NO route outside the /v1/datawindow prefix, and no route, name, type, options group or
//      comment naming any capability area Phase 1 does not implement. The four reserved boundary
//      declarations for those belong to Gateway's own reserved-route endpoint file, which together
//      with the deferral roster document is the only place in this refactor where they may be named
//      at all. On THIS listener such a path matches nothing and answers 404, which is the auditable
//      proof that nothing was stubbed here (constraint C-D).
//    * NO rendering behaviour of any kind. The four headless models' data - the filter expression
//      and search state, the sort expression and sort state, and the menu ITEM model with its
//      labels, ids, enabled and split flags and computed logical text widths - travels as DATA
//      through the projected read-and-apply operations. No window positioning, no DPI-to-pixel
//      conversion, no font measurement, no input-method handling and no menu drawing crosses this
//      boundary; those halves are deferred and named as Gateway extension points.
//    * NO type reaching into a deferred capability area. The submenu surface is carried as the
//      child-item collection and handle reference the headless model publishes, and nothing more.
//    * NO anonymous route. Authorization is required once at the group level, so a route added to
//      this projection later cannot be anonymous by omission (constraint C-G). /health is the only
//      anonymous endpoint on this service and it lives in its own file.
//    * NO package reference, NO Protobuf item and NO edit to the project file. The generated server
//      bases and message types arrive through the PowerFramework.Contracts ProjectReference, which
//      compiles every protocol definition once with GrpcServices="Both".
//    * NO second file in this folder: every request and response shape this projection declares is
//      declared inline below.
//    * NO placeholder, NO stub, and no member that exists only to throw a not-implemented
//      exception. The one refusal in this file - context propagation, which HTTP cannot carry - is a
//      DEFINED and unreachable refusal on a capability the transport genuinely lacks, documented at
//      its own declaration, and it is not a stand-in for work left undone.
//
//  NO PERFORMANCE CLAIM IS MADE OR IMPLIED anywhere in this file. The repository publishes no
//  service-level agreement, no latency budget, no throughput target and no availability commitment,
//  so none may be asserted. This refactor is explicitly not a performance refactor.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for this file. PowerFramework is a LIBRARY: 39 PowerBuilder
//  libraries loaded into one process, with no listener, no route table, no serialization layer and
//  no authentication of any kind. Every cross-service contract in this refactor is NET NEW. The
//  legacy objects cited above are REFERENCE ONLY - they are where the behaviours this boundary
//  carries come from, they are read to understand what must survive it, and they are never ported
//  here.
//
//  RULES POSITION
//  `review_rules` returns exactly "No user rules provided.", so NO USER-SPECIFIED RULE governs this
//  file. The enterprise-standard baseline applies in its place and is honoured: nullable reference
//  types and warnings-as-errors inherited and never relaxed, no secret in source or in any response
//  body, structured logging with the one interpolating field never recorded, and explicitly
//  versioned contracts as the only cross-service coupling. The binding constraints are the Agent
//  Action Plan's C-A through C-L, which the rules facility cannot surface.
// ==================================================================================================

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using PowerFramework.DataServices.Authorization;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Shared.Kernel;

// ALIASED RATHER THAN IMPORTED, AND EACH ALIAS EARNS ITS PLACE.
//
// PowerFramework.Contracts.Common.V1 publishes a `RetCode` MESSAGE whose simple name collides with
// PowerFramework.Shared.Kernel's `RetCode` CATALOGUE, and the catalogue is what every problem body
// here reads, so that namespace is never imported wholesale - only the four types actually needed
// are named.
//
// PowerFramework.DataServices.Grpc publishes `DataWindowService` and `ColumnExpressionService`
// IMPLEMENTATION classes whose simple names collide with the GENERATED containers of the same names
// in PowerFramework.Contracts.DataServices.V1, and this file needs both: the implementations to
// invoke, and the generated containers for their service descriptors. Aliasing the two
// implementations keeps `DataWindowService.Descriptor` unambiguous and unmistakable.

// AND ONE MORE, FOR A COLLISION THE WEB SDK CREATES RATHER THAN THE CONTRACTS PROJECT.
// `Microsoft.Extensions.DependencyInjection` is an IMPLICIT using under Microsoft.NET.Sdk.Web and it
// publishes a `ServiceDescriptor` of its own - the container's registration record - whose simple name
// collides with the protobuf reflection type of the same name. The protobuf one is what this file
// reads, to derive the rich-error trailer keys and the service names from the descriptor rather than
// from a literal, so it is named explicitly. VERIFIED: without this alias the collision is a hard
// CS0104, not a warning.

using System.IO.Pipelines;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;

using ColumnExpressionImplementation = PowerFramework.DataServices.Grpc.ColumnExpressionService;
using CommonV1Extensions = PowerFramework.Contracts.Common.V1.CommonV1Extensions;
using ConflictDetail = PowerFramework.Contracts.Common.V1.ConflictDetail;
using DataWindowImplementation = PowerFramework.DataServices.Grpc.DataWindowService;
using DbError = PowerFramework.Contracts.Common.V1.DbError;
using ProtoServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;
using RichErrorBinding = PowerFramework.Contracts.Common.V1.RichErrorBinding;
using RichErrorTrailer = PowerFramework.Contracts.Common.V1.RichErrorTrailer;

namespace PowerFramework.DataServices.Endpoints;

/// <summary>
/// Declares <c>/v1/datawindow/**</c> on this service's REST listener: the thin projection of
/// contract C-03 (<c>dataservices.v1.DataWindowService</c>) and contract C-04
/// (<c>dataservices.v1.ColumnExpressionService</c>), consumed by Gateway.
/// </summary>
/// <remarks>
/// <para>
/// A translation layer over this service's own gRPC surface, not a second implementation. Every
/// route binds one request message, invokes exactly one gRPC method, renders the response message
/// and maps the returned gRPC status onto HTTP. No DataWindow logic, no expression engine, no
/// validator and no session bookkeeping lives in this file.
/// </para>
/// <para>
/// <b>The decisive behaviour is the status translation.</b> gRPC <see cref="StatusCode.Aborted"/> -
/// an optimistic-concurrency mismatch - becomes HTTP <c>409</c> carrying
/// <c>common.v1.ConflictDetail</c> unchanged, including the current and original values of every
/// marked column, so a caller can implement an explicit retry-or-surface policy. The conflict is
/// never swallowed, never retried and never degraded to a generic <c>500</c>; there is no silent
/// overwrite anywhere in this system.
/// </para>
/// <para>
/// Three methods have no REST projection because they are bidirectional, and two of those are
/// additionally inverted: <c>EventChain</c> on C-03, and <c>InvokeMethodChannel</c> and
/// <c>TraceChannel</c> on C-04. Nothing here invents a representation for any of them. The two
/// SERVER streams - <c>Retrieve</c> and <c>EventStream</c> - are projected, because a single request
/// and a determinate response sequence has a faithful request/response form.
/// </para>
/// <para>
/// Every route requires a bearer token, unconditionally and in every environment. This service
/// holds verification material only and mints nothing.
/// </para>
/// </remarks>
public static class RestProjectionEndpoints
{
    // ----------------------------------------------------------------------------------------------
    //  ROUTES AND GROUPING
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The route prefix every operation in this file hangs from, spelled exactly as the authored
    /// contract spells it so that Gateway's <c>/v1/datawindow/**</c> ingress and this projection
    /// correspond route for route.
    /// </summary>
    private const string DataWindowGroupPrefix = "/v1/datawindow";

    /// <summary>
    /// The nested prefix carrying C-04's operations, so that the contract's own
    /// <c>/v1/datawindow/expression/**</c> spelling is produced by composition rather than repeated
    /// twenty-five times.
    /// </summary>
    private const string ExpressionGroupPrefix = "/expression";

    // ----------------------------------------------------------------------------------------------
    //  PUBLISHED METADATA VOCABULARY - the spellings are the contract's, not this file's
    // ----------------------------------------------------------------------------------------------

    /// <summary>The contract's tag for C-03's operations.</summary>
    private const string DataWindowTag = "DataWindow";

    /// <summary>The contract's tag for C-04's operations.</summary>
    private const string ColumnExpressionTag = "ColumnExpression";

    /// <summary>The contract identifier C-03's projected operations carry.</summary>
    private const string DataWindowContractId = "C-03";

    /// <summary>The contract identifier C-04's projected operations carry.</summary>
    private const string ColumnExpressionContractId = "C-04";

    /// <summary>The specification extension naming the contract an operation belongs to.</summary>
    private const string ContractIdExtensionName = "x-contract-id";

    /// <summary>The specification extension naming the gRPC method an operation projects.</summary>
    private const string GrpcMethodExtensionName = "x-grpc-method";

    /// <summary>The specification extension naming the message the request body maps to.</summary>
    private const string ProtoRequestExtensionName = "x-proto-request";

    /// <summary>The specification extension naming the message the response body maps to.</summary>
    private const string ProtoResponseExtensionName = "x-proto-response";

    /// <summary>The specification extension declaring an operation's upstream streaming shape.</summary>
    private const string GrpcStreamingExtensionName = "x-grpc-streaming";

    /// <summary>
    /// The value of <see cref="GrpcStreamingExtensionName"/> on the two projected server streams, so
    /// a consumer knows the body is the WHOLE sequence rather than one message.
    /// </summary>
    private const string ServerStreamingValue = "server";

    /// <summary>The component name of the contract's bearer security scheme.</summary>
    private const string BearerSecuritySchemeId = "bearerAuth";

    /// <summary>The HTTP authentication scheme name the contract's security scheme declares.</summary>
    private const string BearerSchemeName = "bearer";

    /// <summary>The credential format the contract's security scheme declares.</summary>
    private const string BearerCredentialFormat = "JWT";

    /// <summary>
    /// The name of the session-identifier argument the two close operations take on the path and the
    /// event-gate read takes on the query string.
    /// </summary>
    /// <remarks>
    /// Composed into the route templates rather than spelled again, so a template and its declared
    /// parameter cannot drift apart.
    /// </remarks>
    private const string SessionIdParameter = "sessionId";

    /// <summary>The description the contract gives the session-identifier parameter.</summary>
    private const string SessionIdParameterDescription =
        "The correlation identifier a session-opening operation returned. It is the handle for the "
        + "server-held state that a stateless boundary cannot carry per request.";

    // ----------------------------------------------------------------------------------------------
    //  PROBLEM-DETAILS VOCABULARY
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The problem-details extension member carrying the legacy PowerFramework return code.
    /// </summary>
    /// <remarks>
    /// Surfaced on EVERY problem body, verbatim and numeric. The tri-state algebra is the consumer's
    /// to apply: a non-zero value is not evidence of failure, because PREVENT (1) satisfies the
    /// success predicate and CANCELLED (-2) satisfies neither predicate.
    /// </remarks>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>The problem-details extension member carrying the optimistic-concurrency payload.</summary>
    private const string ConflictExtensionMember = "conflict";

    /// <summary>
    /// The problem-details extension member carrying the database-layer detail, present only when
    /// the failure originated on the data path and the upstream attached one.
    /// </summary>
    private const string DbErrorExtensionMember = "dbError";

    /// <summary>
    /// The problem-details extension member naming the upstream a forwarded failure came from.
    /// </summary>
    /// <remarks>
    /// Set only where the evidence is in the failure itself. These operations traverse exactly one
    /// outbound edge, and this service answers <see cref="StatusCode.Unavailable"/> on its own
    /// account nowhere, so an <c>Unavailable</c> reaching this projection came from that edge.
    /// Omitted otherwise, because this service produced the response itself.
    /// </remarks>
    private const string UpstreamExtensionMember = "upstream";

    /// <summary>The correlation identifier that locates the operator record for an occurrence.</summary>
    private const string TraceIdExtensionMember = "traceId";

    /// <summary>The upstream name the contract's enumeration declares for the storage service.</summary>
    private const string PersistenceUpstream = "persistence";

    /// <summary>RFC 9457's own default problem type, used where no more specific type applies.</summary>
    private const string DefaultProblemType = "about:blank";

    /// <summary>The problem type identifying an optimistic-concurrency conflict carrying its detail.</summary>
    private const string ConflictProblemType = "urn:powerframework:problem:optimistic-concurrency-conflict";

    /// <summary>
    /// The problem type for an <c>Aborted</c> that arrived without a decodable conflict detail.
    /// Distinct so a caller's retry-or-surface logic can tell the two apart.
    /// </summary>
    private const string ConflictWithoutDetailProblemType =
        "urn:powerframework:problem:optimistic-concurrency-conflict-without-detail";

    /// <summary>
    /// The problem type for <see cref="StatusCode.AlreadyExists"/>, which shares HTTP <c>409</c>
    /// with a concurrency conflict and is emphatically not one.
    /// </summary>
    private const string AlreadyExistsProblemType = "urn:powerframework:problem:already-exists";

    /// <summary>The title accompanying <see cref="ConflictProblemType"/>.</summary>
    private const string ConflictProblemTitle = "Optimistic-concurrency conflict";

    /// <summary>The title accompanying <see cref="ConflictWithoutDetailProblemType"/>.</summary>
    private const string ConflictWithoutDetailProblemTitle =
        "Optimistic-concurrency conflict without detail";

    /// <summary>The title accompanying <see cref="AlreadyExistsProblemType"/>.</summary>
    private const string AlreadyExistsProblemTitle = "Already exists";

    // ----------------------------------------------------------------------------------------------
    //  PROBLEM `detail` PROSE - FIXED, AND FIXED FOR A REASON (constraint C-F, CWE-209)
    //
    //  Every string below is a constant. None is composed from a gRPC status message, an exception
    //  message, a parser diagnostic or any part of a request body. All four are arbitrary content
    //  that a caller or an upstream chose, and any of them may quote a credential, a file path or -
    //  on the data path - an interpolated SQL literal back at whoever reads the response. The
    //  contract states the rule for this member directly: `detail` never carries key material, a
    //  stack trace, a file path, a connection string or an unredacted statement.
    //
    //  The redaction is defensible only because it is a REDIRECTION: the `traceId` member locates
    //  the operator record for the same occurrence, which is where the specifics live.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The detail for a request whose body was absent where the operation requires one.</summary>
    private const string MissingBodyDetail =
        "A request body is required, and must be the canonical protobuf JSON mapping of the message "
        + "named by this operation's x-proto-request extension.";

    /// <summary>
    /// The detail for a body that is not valid JSON, or that does not map onto the operation's
    /// request message - including a body carrying a member the message does not declare.
    /// </summary>
    private const string MalformedBodyDetail =
        "The request body could not be bound to this operation's request message. It must be the "
        + "canonical protobuf JSON mapping of the message named by this operation's x-proto-request "
        + "extension, and an unrecognised member is rejected rather than discarded so that a member "
        + "a caller sent is never silently lost. The parser's own diagnostic is deliberately "
        + "withheld because it quotes the offending body, which is caller content.";

    /// <summary>The detail for <see cref="StatusCode.InvalidArgument"/>.</summary>
    private const string InvalidArgumentDetail =
        "An argument was rejected. The retCode member carries the legacy return code for the "
        + "rejection, so the specific validation is identifiable rather than only the HTTP class.";

    /// <summary>The detail for <see cref="StatusCode.FailedPrecondition"/>.</summary>
    private const string FailedPreconditionDetail =
        "The request was rejected because the service's own state does not permit the operation - an "
        + "unknown session, a handle bound to nothing, or an ordering violation inside a "
        + "synchronous group. The request is not retryable unchanged.";

    /// <summary>The detail for <see cref="StatusCode.OutOfRange"/>.</summary>
    private const string OutOfRangeDetail =
        "An ordinal in the request lies outside the range the target accepts. Legacy row and column "
        + "ordinals are ONE-BASED throughout this system.";

    /// <summary>The detail for <see cref="StatusCode.Unauthenticated"/>.</summary>
    // ---------------------------------------------------------------------------------------------
    //  IN-BAND FAILURE PROSE - used ONLY when the contract left its own diagnostic empty
    // ---------------------------------------------------------------------------------------------
    //  Every one of these is a fallback. When the contract supplied error_text, THAT is what the caller
    //  receives, because it is the legacy diagnostic and the migration preserves it verbatim (C-B).

    /// <summary>Fallback prose for a rejected argument reported in band.</summary>
    private const string InBandInvalidArgumentDetail =
        "The operation rejected an argument. The contract's own outcome code is on the retCode member.";

    /// <summary>Fallback prose for an out-of-range or out-of-bound outcome reported in band.</summary>
    private const string InBandOutOfRangeDetail =
        "The operation rejected a value outside its permitted range.";

    /// <summary>Fallback prose for an access refusal reported in band.</summary>
    private const string InBandAccessDeniedDetail = "The operation refused this caller.";

    /// <summary>Fallback prose for a payload the operation could not apply, reported in band.</summary>
    /// <remarks>
    /// 400 rather than 500: the request's own DATA is what was rejected, so the caller can correct it. The
    /// contract's own diagnostic replaces this prose whenever it supplied one, which on the update path is
    /// the legacy sentence itself.
    /// </remarks>
    private const string InBandInvalidDataDetail =
        "The operation refused the data carried in the request. On the update path this is the buffered "
        + "carrier failing validation before any statement is generated, so nothing was applied and "
        + "re-sending the same payload will be refused again.";

    /// <summary>Fallback prose for a DataWindow name that resolves to nothing, reported in band.</summary>
    /// <remarks>
    /// 400 AND NOT 404, for the reason recorded on the arm: the name is a member of the request BODY rather
    /// than the request target, and the retrieval side answers the same mistake with the oracle's own
    /// <c>E_INVALID_ARGUMENT</c>, which is already 400.
    /// </remarks>
    private const string InBandInvalidDataObjectDetail =
        "The DataWindow named in the request resolves to nothing. The name travels in the request body, so "
        + "this is a rejected argument rather than a missing resource.";

    /// <summary>Fallback prose for an unknown handle or missing object reported in band.</summary>
    private const string InBandNotFoundDetail =
        "The operation could not resolve the handle or object named in the request.";

    /// <summary>Fallback prose for the oracle's unspecific failure reported in band.</summary>
    /// <remarks>
    /// 502 rather than 500, for the reason recorded on the arm: the DATA PATH behind this service reported
    /// a failure and this projection did not fail. The distinction decides which service an operator
    /// investigates, and it is the same distinction the ingress draws for the same code.
    /// </remarks>
    private const string InBandGenericFailureDetail =
        "The operation reported the legacy unspecific failure. It completed normally and reported that it "
        + "could not do what was asked; the originating return code is on the retCode member.";

    /// <summary>Fallback prose for a retryable conflict reported in band.</summary>
    /// <remarks>
    /// 409, the SAME status an Aborted receives, so a concurrency mismatch answers one way regardless of
    /// which channel reported it. There is no silent overwrite on either path.
    /// </remarks>
    private const string InBandRetryDetail =
        "The operation was rejected and can be retried. When it carries a conflict, the current row state "
        + "is on the relayed response and the caller implements an explicit retry-or-surface policy.";

    /// <summary>Fallback prose for a busy resource reported in band.</summary>
    private const string InBandBusyDetail =
        "The resource is in use by an operation already in flight. The request was not applied and can be "
        + "retried.";

    /// <summary>Fallback prose for a timeout reported in band.</summary>
    private const string InBandTimeoutDetail = "The operation did not complete within its budget.";

    /// <summary>Fallback prose for an unsupported or unimplemented outcome reported in band.</summary>
    private const string InBandNotImplementedDetail =
        "The operation is not supported for the arguments supplied.";

    /// <summary>Fallback prose for a data-path failure reported in band.</summary>
    /// <remarks>
    /// 502 rather than 500: the failure is the DATA PATH BEHIND this service answering badly, not this
    /// service faulting, and the two call for different investigations.
    /// </remarks>
    private const string InBandDataPathDetail =
        "The data path reported a failure. Any driver-level detail travels on the relayed response.";

    /// <summary>Fallback prose for an in-band outcome this projection does not classify.</summary>
    private const string InBandUnclassifiedDetail =
        "The operation reported a failure this projection does not classify. The contract's own outcome "
        + "code is on the retCode member.";

    private const string UnauthenticatedDetail =
        "The credential presented was not accepted. This service holds verification material only; "
        + "Security is the sole token issuer in this system.";

    /// <summary>The detail for <see cref="StatusCode.PermissionDenied"/>.</summary>
    private const string PermissionDeniedDetail =
        "The credential is valid but does not carry the scope this operation requires. Deliberately "
        + "distinct from an absent credential.";

    /// <summary>The detail for <see cref="StatusCode.NotFound"/>.</summary>
    private const string NotFoundDetail =
        "Something the request named could not be resolved - most often a session identifier that "
        + "has expired or was already closed, or a selector naming a column the DataWindow does not "
        + "have.";

    /// <summary>The detail for <see cref="StatusCode.AlreadyExists"/>.</summary>
    private const string AlreadyExistsDetail =
        "What the request asked to create already exists. This is NOT an optimistic-concurrency "
        + "conflict: it carries no conflict member, and a caller must not treat it as one.";

    /// <summary>The detail for the conflict this projection exists to carry.</summary>
    private const string ConflictDetailProse =
        "An optimistic-concurrency conflict. The conflict member carries the upstream's own payload "
        + "unchanged, including, per failing row, both the current server-side values and the "
        + "original values the request believed were current - which is what identifies the column "
        + "that moved. Re-sending the same payload produces this same response, because those "
        + "original values are still stale: re-read and rebase, or surface the conflict. Nothing is "
        + "overwritten silently.";

    /// <summary>
    /// The detail for an <c>Aborted</c> that arrived with no decodable conflict detail.
    /// </summary>
    private const string ConflictWithoutDetailProse =
        "An optimistic-concurrency conflict was reported but no decodable conflict detail was "
        + "attached. The status is preserved, because reporting anything else would let a rejected "
        + "update appear to have succeeded, and no conflict member is fabricated, because an empty "
        + "one would describe a conflict no caller could act on. The update was NOT applied. "
        + "Re-read before resubmitting.";

    /// <summary>The detail for <see cref="StatusCode.ResourceExhausted"/>.</summary>
    private const string ResourceExhaustedDetail =
        "A resource the operation needs is exhausted. The request was not processed.";

    /// <summary>The detail for <see cref="StatusCode.Unimplemented"/>.</summary>
    /// <remarks>
    /// It names no capability area and carries no reserved marker. This is a method that the
    /// contract publishes reporting itself unavailable, which is deployment or version skew - not a
    /// capability boundary. The four reserved boundary declarations live on Gateway alone.
    /// </remarks>
    private const string UnimplementedDetail =
        "This operation's method is not implemented by the deployment currently answering. The "
        + "published contract declares it, so this indicates a version skew rather than a "
        + "capability boundary.";

    /// <summary>The detail for <see cref="StatusCode.DeadlineExceeded"/>.</summary>
    private const string DeadlineExceededDetail =
        "The operation did not complete within its deadline. Whether it took effect is "
        + "undetermined, so a caller must re-read before assuming either outcome.";

    /// <summary>The detail for an answered <see cref="StatusCode.Unavailable"/>.</summary>
    private const string UnavailableDetail =
        "An upstream answered that it is unavailable. The request was not processed.";

    /// <summary>The detail for a transport failure that produced no gRPC response at all.</summary>
    /// <remarks>
    /// Retry is OPERATION-SCOPED on this service's outbound calls to Persistence exactly as it is on
    /// Gateway's to this one, so this text must not claim a retry that only replay-safe operations get.
    /// An operation that creates, mutates or advances upstream state is attempted exactly once by design.
    /// </remarks>
    private const string UpstreamUnavailableDetail =
        "An upstream could not be reached, or the call to it failed in transit, so no response arrived. "
        + "Whether the call was retried first depends on the operation: one whose replay is safe is "
        + "retried under the configured policy, and reaching this response means the policy was "
        + "exhausted, while one whose replay is NOT safe - every operation that creates, mutates or "
        + "advances upstream state - is attempted exactly once by design and was not retried. This "
        + "failure mode is one decomposition itself creates: an in-process call cannot fail in transit "
        + "and a network call can.";

    /// <summary>The detail for <see cref="StatusCode.Cancelled"/>.</summary>
    private const string CancelledDetail =
        "The operation was cancelled before it produced a response. Under the preserved return-code "
        + "algebra a cancellation is neither a success nor a failure, and whether the operation took "
        + "effect is undetermined.";

    /// <summary>The detail for <see cref="StatusCode.DataLoss"/>.</summary>
    private const string DataLossDetail =
        "Unrecoverable data loss or corruption was reported on the data path. Any statement text the "
        + "underlying error carried is redacted and is not reproduced here.";

    /// <summary>The detail for <see cref="StatusCode.Internal"/> and <see cref="StatusCode.Unknown"/>.</summary>
    private const string InternalErrorDetail =
        "An internal failure occurred. The diagnostic is recorded on the operator channel against "
        + "the traceId member of this response; no exception text, host, path, key material or "
        + "statement text is reproduced here.";

    /// <summary>The detail for any status this projection does not classify.</summary>
    private const string UnclassifiedFailureDetail =
        "The operation failed with a status this projection does not classify. The diagnostic is "
        + "recorded on the operator channel against the traceId member of this response.";


    // ----------------------------------------------------------------------------------------------
    //  RESPONSE DESCRIPTIONS - carried across from the authored contract so the document this
    //  service generates and the one Gateway publishes say the same thing to a consumer.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The contract's shared success description for a projected operation.</summary>
    /// <remarks>
    /// It names the message rather than restating its members because the members are published, member
    /// by member, by the schema of that name in the authored contract - see <see cref="ProtoPayload"/>.
    /// </remarks>
    private const string ProjectedSuccessDescription =
        "The projected gRPC method returned OK. The body is the canonical protobuf JSON mapping of "
        + "the message named in this operation's x-proto-response extension, whose members the "
        + "contract document publishes as a schema of the same name.";

    /// <summary>The contract's shared <c>400</c> description.</summary>
    private const string BadRequestDescription =
        "The projected gRPC method returned InvalidArgument, or the request failed this projection's "
        + "own binding. retCode carries the originating legacy return code so the specific "
        + "validation is identifiable rather than merely the HTTP class.";

    /// <summary>
    /// The contract's <c>400</c> description for the five operations on which a cross-session
    /// foreign-variable reference is a distinctive rejection reason.
    /// </summary>
    private const string CrossSessionReferenceBlockedDescription =
        "The request was rejected. On this operation one rejection reason is distinctive enough to "
        + "record separately: a cross-DataWindow variable reference spanning expression sessions or "
        + "service instances is BLOCKED. It is a 400 rather than a status of its own because the "
        + "published status mapping sanctions none, and that mapping already provides the mechanism "
        + "- InvalidArgument projects to 400 carrying the originating retCode - so a caller "
        + "distinguishes this outcome by reading retCode and the expression-error category, not by "
        + "counting statuses. The narrowing is deliberate: the legacy holds a live in-process "
        + "pointer to another DataWindow's expression service, a pointer cannot be serialized, and "
        + "approximating a dereference across a network would produce results wrong in a way no test "
        + "would obviously catch. A caller receiving this must not retry - the topology rejected it, "
        + "not the request.";

    /// <summary>The contract's shared <c>401</c> description.</summary>
    private const string UnauthorizedDescription =
        "No token was presented, or the token presented is expired, malformed, or not valid for this "
        + "service. This response is part of the published contract rather than an implementation "
        + "detail: it is the standing proof that the boundary is authenticated (C-G).";

    /// <summary>The contract's shared <c>403</c> description.</summary>
    private const string ForbiddenDescription =
        "The projected gRPC method returned PermissionDenied. The token is valid but does not carry "
        + "the scope this operation requires. Deliberately distinguished from 401 so a caller can "
        + "tell a missing credential from an insufficient one.";

    /// <summary>The contract's shared <c>404</c> description.</summary>
    private const string NotFoundDescription =
        "The projected gRPC method returned NotFound - most often a session identifier that has "
        + "expired or was already closed, or a column selector naming a column the DataWindow does "
        + "not have.";

    /// <summary>The contract's <c>409</c> description.</summary>
    private const string ConflictDescription =
        "The projected gRPC method returned Aborted: an optimistic-concurrency conflict. The body "
        + "carries the conflict detail UNCHANGED, including the current row state, so a caller has "
        + "what it needs to decide between retrying and surfacing. Callers implement an explicit "
        + "retry-or-surface policy, and there is no silent overwrite anywhere in the system. "
        + "Re-sending the same payload produces the same 409, because the original values it carries "
        + "are still stale - a retry must first re-read.";

    /// <summary>The contract's shared <c>500</c> description.</summary>
    private const string InternalErrorDescription =
        "The projected gRPC method returned Internal. The statement field is redacted: the legacy "
        + "sqlsyntax field carries the complete generated statement including interpolated literal "
        + "values and the legacy logger performs no redaction at all. detail never carries key "
        + "material, a stack trace, a file path or a connection string.";

    /// <summary>The contract's shared <c>502</c> description.</summary>
    /// <remarks>
    /// Worded to match <see cref="UpstreamUnavailableDetail"/>, for the same reason.
    /// </remarks>
    private const string UpstreamUnavailableDescription =
        "An upstream service could not be reached, or the call to it failed in transit. A replay-safe "
        + "operation is retried under the configured policy before this is returned; an operation whose "
        + "replay is not safe is attempted exactly once and is never retried, so for those this response "
        + "reports a single failed attempt. This response exists because of the decomposition itself: an "
        + "in-process call cannot fail in transit and a network call can, so handling the failure is "
        + "required BY the transition rather than being a behavioural improvement layered on top of it. "
        + "The body names which upstream failed.";

    // ----------------------------------------------------------------------------------------------
    //  THE OPERATOR LOG MESSAGES - ALLOWLISTED
    //
    //  What is passed identifies the fault WITHOUT QUOTING IT: the request's own method, the route
    //  PATTERN rather than the path, the gRPC status NAME, the HTTP status the caller is about to
    //  receive, the legacy return code, the correlation identifier, and the exception TYPE name.
    //
    //  WHAT IS DELIBERATELY ABSENT, and why each absence is required rather than merely cautious:
    //    * the exception OBJECT, because the logging abstraction renders a passed exception through
    //      its own string conversion, which includes every message in the chain;
    //    * the gRPC status DETAIL, because it is upstream text that may quote a statement or a
    //      literal;
    //    * the request body and the response body, because both are caller or upstream content;
    //    * any header, and in particular the inbound bearer credential;
    //    * any part of a DbError, whose statement field the legacy carried unredacted.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The allowlisted operator record written once per projected failure.</summary>
    private const string ProjectedFailureLogMessage =
        "The /v1/datawindow REST projection is answering {HttpStatus} with retCode {RetCode} for "
        + "{HttpMethod} {RoutePattern}: the projected method reported gRPC status {GrpcStatus} "
        + "({ExceptionType}). Correlation {CorrelationId}. The status detail, the request body and "
        + "the response body are deliberately not recorded here - they are caller or upstream "
        + "content.";

    /// <summary>The allowlisted operator record written once per binding failure.</summary>
    private const string BindingFailureLogMessage =
        "The /v1/datawindow REST projection rejected a request body before invoking the projected "
        + "method and is answering {HttpStatus} with retCode {RetCode} for {HttpMethod} "
        + "{RoutePattern}. Correlation {CorrelationId}. The parser diagnostic and the body itself "
        + "are deliberately not recorded - the body is caller content and may carry anything.";

    /// <summary>
    /// The logger category for the two records above, so an operator filters this projection's
    /// records without also matching the gRPC surface's or the typed clients'.
    /// </summary>
    private const string LoggerCategory =
        "PowerFramework.DataServices.Endpoints.RestProjectionEndpoints";

    /// <summary>
    /// The placeholder used where a record would otherwise publish an empty field. Unreachable for
    /// these routes, and guarded anyway.
    /// </summary>
    private const string UnroutedRouteDescription = "(unrouted)";

    // ----------------------------------------------------------------------------------------------
    //  THE PROTOBUF JSON CODECS
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The strict request parser. Thread-safe and stateless, so one instance serves every route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STRICT ON PURPOSE. The default settings reject a member the target message does not declare,
    /// which turns a caller's typo or a version skew into a <c>400</c> instead of silently
    /// discarding a value the caller believed it had sent. On the update path that discard would be
    /// silent data loss.
    /// </para>
    /// <para>
    /// No type registry is supplied because none is needed: neither <c>Proto/common.v1.proto</c> nor
    /// <c>Proto/dataservices.v1.proto</c> uses <c>google.protobuf.Any</c> or a well-known wrapper
    /// anywhere in a payload message.
    /// </para>
    /// </remarks>
    private static readonly JsonParser RequestParser = JsonParser.Default;

    /// <summary>
    /// The response formatter. Thread-safe and stateless, so one instance serves every route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DEFAULT VALUES ARE FORMATTED. The canonical mapping omits them, which would drop members the
    /// contract's own mirrored schemas declare as required: <c>RetrieveChunk</c> requires
    /// <c>rowCount</c>, <c>chunkIndex</c>, <c>final</c> and <c>cumulativeRowCount</c>, and every one
    /// of those is legitimately zero or false on a real chunk. It also keeps
    /// <c>StructuredError.localized</c> explicit, so a consumer never has to infer the deliberate
    /// absence of a localization category from an omitted member.
    /// </para>
    /// <para>
    /// Fields with EXPLICIT PRESENCE are unaffected and remain absent when unset, which is what
    /// preserves the contract's absent-versus-empty distinctions - the produced-filter signal that
    /// mirrors the legacy <c>ref string</c> out-parameter among them.
    /// </para>
    /// <para>
    /// IT IS ALSO WHAT KEEPS <c>DataWindowRow.originalValues</c> PRESENT ON EVERY ROW. A repeated field
    /// has no explicit presence, so an empty one is a default value: without this setting an
    /// insert-shaped row - the one row that legitimately has no prior state - would serialize without
    /// the member, and the member is REQUIRED. The schema's distinction is between an EMPTY array and an
    /// ABSENT one, and only formatting defaults keeps that distinction expressible.
    /// </para>
    /// </remarks>
    private static readonly JsonFormatter ResponseFormatter =
        new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));

    /// <summary>
    /// The fully-qualified name of C-03's service, read from the generated descriptor so that
    /// <c>x-grpc-method</c> cannot drift from the protocol definition.
    /// </summary>
    private static readonly string DataWindowServiceName = DataWindowService.Descriptor.FullName;

    /// <summary>
    /// The fully-qualified name of C-04's service, on the same terms as
    /// <see cref="DataWindowServiceName"/>.
    /// </summary>
    private static readonly string ColumnExpressionServiceName =
        ColumnExpressionService.Descriptor.FullName;

    /// <summary>
    /// The fully-qualified name of the only rich-error payload this projection can decode.
    /// </summary>
    /// <remarks>
    /// DECLARATION ORDER IS LOAD BEARING HERE. Static field initializers run in declaration order,
    /// and <see cref="CollectRichErrorTrailerKeys"/> reads this field, so the payload-type name must
    /// be declared BEFORE the key set or the collection would filter against a null.
    /// </remarks>
    private static readonly string SupportedRichErrorPayloadType =
        RichErrorTrailer.Descriptor.FullName;

    /// <summary>
    /// The metadata keys, deduplicated, that a decodable rich-error trailer can arrive under, taken
    /// from the descriptors of both projected services rather than written as a literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trailer key is part of the published contract: every method that can fail with a rich
    /// error repeats it in its own <c>RichErrorBinding</c>, so the descriptor is self-describing to
    /// a client that never reads the protocol definition. Deriving the set from the descriptor means
    /// a change to the key on either service reaches this projection automatically, and that a
    /// hardcoded spelling can never fall out of step with the contract.
    /// </para>
    /// <para>
    /// Only bindings whose declared payload type is the one this projection can decode are
    /// collected. A binding announcing some future payload type is deliberately NOT collected, so
    /// its status is surfaced with its own trailers intact rather than misread as this payload.
    /// </para>
    /// </remarks>
    private static readonly string[] RichErrorTrailerKeys = CollectRichErrorTrailerKeys();



    // ==============================================================================================
    //  THE ONLY PUBLIC ENTRY POINT
    // ==============================================================================================

    /// <summary>
    /// Declares the forty <c>/v1/datawindow</c> operations on the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The route builder the composition root is populating.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so the composition root can chain its endpoint
    /// registrations.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>. A composition root that reached this
    /// method without a route builder is structurally broken, and the legacy framework's posture for
    /// a structural fault is to fail immediately rather than degrade past it
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>].
    /// </exception>
    /// <remarks>
    /// <para>
    /// The route definitions live here rather than at the composition root so that the whole
    /// projection - paths, methods, authorization, response surface and published metadata - is
    /// reviewable as one unit.
    /// </para>
    /// <para>
    /// <b>The return type is deliberately the route builder and not a route handler builder or a
    /// group.</b> Handing back either would let a caller append <c>AllowAnonymous</c>, which takes
    /// precedence over <c>RequireAuthorization</c> in endpoint metadata and would silently open
    /// forty authenticated routes from a different file. Withholding it makes that impossible.
    /// </para>
    /// <para>
    /// Both groups are authorized by the parent group's single unconditional
    /// <c>RequireAuthorization</c> call. There is no environment test and no configuration switch
    /// guarding it: a bypass that exists only in Development is still a bypass, and it would make the
    /// local build disagree with the published contract.
    /// </para>
    /// <para>
    /// This method registers nothing in the service collection and constructs no channel, no client
    /// and no options type. Registration is <c>Program.cs</c>'s responsibility, and the projected
    /// implementations are resolved per request with the same semantics the gRPC hosting layer uses -
    /// see <see cref="ResolveDataWindowService"/>.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapRestProjectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Constraint C-G, AND THE POLICY IS NAMED RATHER THAN LEFT AT THE DEFAULT. The parameterless form
        // used to be here, which required an authenticated user and nothing more - so any holder of any
        // token minted for this audience could drive every one of these routes, and a credential obtained
        // to read a DataWindow could drive the expression engine (CWE-862, CWE-863). Each group now names
        // the policy its own contract is served under: the DataWindow scope on the parent, and the
        // expression scope on the nested group, which is the same split the two gRPC contracts are mapped
        // under and the same split Gateway requests its credential under. Each policy also requires the
        // caller to be a configured permitted identity, because this projection exists for Gateway alone.
        //
        // ⚠ THE EXPRESSION GROUP IS MAPPED FROM THE ROOT WITH THE COMBINED PREFIX, NOT NESTED ⚠
        //
        // It was nested on the DataWindow group, on the reading that the added requirement is harmless
        // because Gateway's credential happens to carry both scopes. Harmless today is not the same as
        // correct, and this one is not correct: authorization metadata COMBINES and a group convention
        // cannot be removed further down, so every projected expression operation demanded the C-03 scope
        // as well as its own. A caller granted exactly C-04 could not reach the contract it held.
        //
        // THE PUBLISHED MODEL FORBIDS THE IMPLICATION EXPLICITLY. C-04 is a separate contract from C-03 so
        // the expansion engine can version independently of the event chain, and DataServicesScopes records
        // that its scope is "SEPARATE FROM DataWindow AND NOT IMPLIED BY IT ... an implication either way
        // would undo that at the authorization layer while leaving the contracts looking independent". The
        // two gRPC services are mapped as two independent surfaces for exactly that reason, and this REST
        // projection of them must carry the same permission model or the projection is not a projection.
        //
        // The URLs are unchanged either way, which is what makes re-nesting a silent regression: only the
        // permission would move. The prefixes are therefore composed here rather than by nesting.
        RouteGroupBuilder dataWindow = endpoints
            .MapGroup(DataWindowGroupPrefix)
            .RequireAuthorization(CallerAuthorization.DataWindowPolicyName);

        RouteGroupBuilder expression = endpoints
            .MapGroup(DataWindowGroupPrefix + ExpressionGroupPrefix)
            .RequireAuthorization(CallerAuthorization.ColumnExpressionPolicyName);

        MapDataWindowOperations(dataWindow);
        MapColumnExpressionOperations(expression);

        return endpoints;
    }

    /// <summary>
    /// Collects the distinct rich-error trailer keys both projected services declare.
    /// </summary>
    /// <returns>
    /// The declared keys, deduplicated and ordinal-ordered. Empty when neither service declares a
    /// decodable binding, in which case an <c>Aborted</c> is still projected as <c>409</c> - with the
    /// no-detail body, because a conflict payload is never fabricated.
    /// </returns>
    private static string[] CollectRichErrorTrailerKeys()
    {
        SortedSet<string> keys = new(StringComparer.Ordinal);

        Collect(DataWindowService.Descriptor);
        Collect(ColumnExpressionService.Descriptor);

        return [.. keys];

        void Collect(ProtoServiceDescriptor service)
        {
            foreach (MethodDescriptor method in service.Methods)
            {
                RichErrorBinding? binding = method.GetOptions()?.GetExtension(CommonV1Extensions.RichError);

                if (binding is null
                    || string.IsNullOrEmpty(binding.TrailerKey)
                    || !string.Equals(
                        binding.PayloadType,
                        SupportedRichErrorPayloadType,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                _ = keys.Add(binding.TrailerKey);
            }
        }
    }


    // ==============================================================================================
    //  C-03 - THE FIFTEEN PROJECTED DataWindowService OPERATIONS
    //
    //  C-03 declares sixteen methods. `EventChain` is the one exclusion, because it is BIDIRECTIONAL
    //  and carries a strictly ordered chain with a tri-valued veto - see this file's header. Fifteen
    //  are declared here and the sixteenth is deliberately absent.
    //
    //  Each operation's declared response set is the authored contract's, not a template pasted onto
    //  every route: a status every generated client must branch on but no operation can return hides
    //  the real surface. So the two session opens declare no 404, the two closes and the gate read
    //  declare no 400, and 409 appears on the update alone.
    // ==============================================================================================

    /// <summary>
    /// Declares C-03's fifteen projected operations: the retrieval, the paired validation session,
    /// the update, the event gate, and the four headless models as a read and an apply each.
    /// </summary>
    /// <param name="group">The <c>/v1/datawindow</c> group.</param>
    private static void MapDataWindowOperations(RouteGroupBuilder group)
    {
        MapServerStream<DataWindowImplementation, RetrieveRequest, RetrieveChunk>(
            group,
            new("/retrieve", "retrieve", "Retrieve", ContractSurface.DataWindow,
                "Retrieve rows as an ordered sequence of buffer-shaped chunks. Token required.",
                "The RETRIEVAL third of the legacy retrieval/validation/update triple. A SERVER stream "
                + "is projectable and this is the form it takes: one request, and a response carrying "
                + "the same chunks the stream would have delivered, IN THE SAME ORDER. The body is NOT "
                + "a flat rowset - each chunk keeps its own chunkIndex, its final flag, its own "
                + "rowCount and the running cumulativeRowCount, so a consumer reconstructs exactly "
                + "what a gRPC consumer sees and the last chunk is identifiable as the last rather "
                + "than inferred from the collection ending. Each row names the buffer it came from "
                + "and carries BOTH its current and its original column values, because the sole "
                + "updatable DataWindow in the legacy estate declares updatewhere=1 with all six "
                + "columns marked, so the concurrency check the update half performs compares "
                + "ORIGINAL values and a retrieval that discarded them would leave a caller unable to "
                + "construct an update at all. Filter! row order is INVERTED relative to the source; "
                + "that is the legacy's own behaviour, preserved rather than corrected, because a "
                + "consumer re-sorting it would produce wrong data a row-count assertion would not "
                + "catch. Chunk size and its legacy guard are unchanged by this projection, and "
                + "nothing is truncated.",
                SuccessDescription:
                "The retrieval succeeded. The body is the ordered sequence of chunks the gRPC server "
                + "stream would have delivered, with the chunking contract intact."),
            static (service, request, stream, context) => service.Retrieve(request, stream, context));

        MapUnary<DataWindowImplementation, OpenValidationSessionRequest, OpenValidationSessionResponse>(
            group,
            new("/sessions", "openValidationSession", "OpenValidationSession",
                ContractSurface.DataWindow,
                "Open a validation session and receive its correlation identifier.",
                "Materializes the FOUR pieces of cross-event mutable state that "
                + "ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L89-L96 holds as private "
                + "fields: the disabled-event bitmask, the inside-item-change flag, the "
                + "inside-validation-error flag, and - most consequentially - the item-changed return "
                + "value STASHED FOR THE VALIDATION-ERROR EVENT TO CONSUME. A stateless request "
                + "boundary has nowhere to put those, which is why the session is explicit rather "
                + "than implicit, and the returned identifier correlates every subsequent operation "
                + "in the chain. Sessions must be closed; an abandoned one holds server-side state "
                + "until its configured idle expiry.",
                DeclaresNotFound: false),
            static (service, request, context) => service.OpenValidationSession(request, context));

        MapSessionScoped<DataWindowImplementation, CloseValidationSessionRequest, CloseValidationSessionResponse>(
            group,
            new($"/sessions/{{{SessionIdParameter}}}", "closeValidationSession",
                "CloseValidationSession", ContractSurface.DataWindow,
                "Close a validation session and release its cross-event state.",
                "Releases the session the paired open returned. Closing a session that does not exist "
                + "is reported as 404 rather than silently succeeding, so a caller can detect a "
                + "double close - which in the legacy would have been a use-after-destroy.",
                BadRequest: BadRequestDeclaration.None),
            HttpMethods.Delete,
            static sessionId => new CloseValidationSessionRequest { SessionId = sessionId },
            static (service, request, context) => service.CloseValidationSession(request, context));

        MapUnary<DataWindowImplementation, UpdateRequest, UpdateResponse>(
            group,
            new("/update", "updateDataWindow", "Update", ContractSurface.DataWindow,
                "Apply buffered changes. Returns 409 on an optimistic-concurrency conflict.",
                "The UPDATE third of the triple, and THE 409 IS THE SUBSTANTIVE PART OF THIS "
                + "OPERATION. The sole updatable DataWindow in the legacy estate carries "
                + "updatewhere=1 with all six columns marked updatewhereclause=yes "
                + "[ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], so the check spans ALL SIX "
                + "COLUMNS' ORIGINAL VALUES. On a mismatch the projected method returns gRPC Aborted "
                + "and this operation returns HTTP 409 with the conflict detail carrying the current "
                + "row state UNCHANGED. Callers implement an explicit retry-or-surface policy: "
                + "re-sending the same payload produces the same 409, because the original values it "
                + "carries are still stale. There is no silent overwrite anywhere in this system and "
                + "this projection performs no retry of any kind. The response also relays the "
                + "inserted, updated and deleted counts and the identity round trip - the identity "
                + "column and its two value arrays - unchanged, because a caller that inserted rows "
                + "could not otherwise learn the values the database assigned them.",
                DeclaresConflict: true),
            static (service, request, context) => service.Update(request, context));

        MapSessionScoped<DataWindowImplementation, GetEventGateRequest, GetEventGateResponse>(
            group,
            new("/event-gate", "getEventGate", "GetEventGate", ContractSurface.DataWindow,
                "Read the event gate for a session.",
                "Reports the composable gate mask and the bits currently disabled. The bit "
                + "identifiers and their values are preserved verbatim from se_cst_dw.sru:L41-L43 - "
                + "EID_ROWFOCUSCHANGE is 1, EID_ITEMFOCUSCHANGE is 2 and EID_ITEMCHANGE is 4. "
                + "DISABLING EID_ITEMCHANGE ALSO SUPPRESSES COLUMN-EXPRESSION EVALUATION: the legacy "
                + "couples the two [inline note at :L43], the coupling is reproduced rather than "
                + "split, and a caller disabling item-change to stop validation firing needs to know "
                + "that expressions stop recalculating too - before it does so rather than after. "
                + "Splitting them would be a behaviour improvement, which the mandate forbids as "
                + "firmly as it forbids a regression.",
                BadRequest: BadRequestDeclaration.None),
            HttpMethods.Get,
            static sessionId => new GetEventGateRequest { SessionId = sessionId },
            static (service, request, context) => service.GetEventGate(request, context));

        MapUnary<DataWindowImplementation, DisableEventRequest, DisableEventResponse>(
            group,
            new("/event-gate/disable", "disableEvent", "DisableEvent", ContractSurface.DataWindow,
                "Disable one or more gated events for a session.",
                "Sets bits in the gate mask. THE LEGACY RETURN-TYPE ASYMMETRY IS PRESERVED AND NOT "
                + "NORMALIZED: of_iseventdisabled returns a boolean, of_disableevent returns a LONG "
                + "and of_enableevent returns an INTEGER [se_cst_dw.sru:L109-L111]. The two widths "
                + "travel as the contract declares them, so the response of this operation and of its "
                + "enable counterpart are not interchangeable shapes. Remember the coupling recorded "
                + "on the gate read: disabling item-change also suppresses column-expression "
                + "evaluation."),
            static (service, request, context) => service.DisableEvent(request, context));

        MapUnary<DataWindowImplementation, EnableEventRequest, EnableEventResponse>(
            group,
            new("/event-gate/enable", "enableEvent", "EnableEvent", ContractSurface.DataWindow,
                "Enable one or more gated events for a session.",
                "Clears bits in the gate mask, carrying the legacy INTEGER-width result rather than "
                + "the LONG the disable counterpart carries [se_cst_dw.sru:L109-L111]. The asymmetry "
                + "is the legacy's and is reproduced deliberately."),
            static (service, request, context) => service.EnableEvent(request, context));

        MapUnary<DataWindowImplementation, GetDropDownSearchStateRequest, GetDropDownSearchStateResponse>(
            group,
            new("/drop-down-search/state", "getDropDownSearchState", "GetDropDownSearchState",
                ContractSurface.DataWindow,
                "Read the drop-down search model's headless state.",
                "THE HEADLESS HALF ONLY, surfaced as DATA. It carries the constructed filter "
                + "EXPRESSION and the search state, with the legacy defaults preserved exactly: the "
                + "filter type defaults to 3 - display text plus its pinyin first letters - and NOT "
                + "to the all-columns value, and the show-filtered-rows setting is THREE-STATE whose "
                + "unset default means auto-determine rather than false. Every selection range it "
                + "reports is ONE-BASED, as every legacy ordinal is. Window positioning and "
                + "input-method text entry are the rendering half and are deferred; nothing of either "
                + "crosses this boundary."),
            static (service, request, context) => service.GetDropDownSearchState(request, context));

        MapUnary<DataWindowImplementation, ApplyDropDownSearchRequest, ApplyDropDownSearchResponse>(
            group,
            new("/drop-down-search/apply", "applyDropDownSearch", "ApplyDropDownSearch",
                ContractSurface.DataWindow,
                "Apply a drop-down search term and read the resulting filter and counts.",
                "Applies a search term through the headless model and returns the filter expression "
                + "it constructed together with the row and filtered counts the legacy reports to its "
                + "filtered event. The pinyin clause travels as part of the expression; the lookup "
                + "table behind pinyin first-letter matching exists only inside the closed legacy "
                + "binary, so the matcher is characterized against the behavioural oracle or reports "
                + "itself blocked, and this projection neither approximates it nor hides its "
                + "refusal."),
            static (service, request, context) => service.ApplyDropDownSearch(request, context));

        MapUnary<DataWindowImplementation, GetColumnSortStateRequest, GetColumnSortStateResponse>(
            group,
            new("/column-sort/state", "getColumnSortState", "GetColumnSortState",
                ContractSurface.DataWindow,
                "Read the column sort model's headless state.",
                "THE HEADLESS HALF ONLY: the constructed sort EXPRESSION and the sort state. The "
                + "legacy object carries no dialog and no localization use at all, so this operation "
                + "contributes no error path of its own. Sort-indicator geometry is the rendering "
                + "half and is deferred."),
            static (service, request, context) => service.GetColumnSortState(request, context));

        MapUnary<DataWindowImplementation, ApplyColumnSortRequest, ApplyColumnSortResponse>(
            group,
            new("/column-sort/apply", "applyColumnSort", "ApplyColumnSort",
                ContractSurface.DataWindow,
                "Apply a column sort and read the resulting expression and state.",
                "Applies a sort through the headless model and returns the sort expression it "
                + "constructed and the resulting state. No pixel coordinate, no DPI conversion and no "
                + "indicator geometry crosses this boundary."),
            static (service, request, context) => service.ApplyColumnSort(request, context));

        MapUnary<DataWindowImplementation, GetContextMenuModelRequest, GetContextMenuModelResponse>(
            group,
            new("/context-menu/state", "getContextMenuModel", "GetContextMenuModel",
                ContractSurface.DataWindow,
                "Read the context menu's complete headless item model.",
                "THE HEADLESS HALF ONLY, and it is the complete ITEM model: labels, ids, enabled and "
                + "split flags, and the COMPUTED LOGICAL TEXT WIDTHS. A submenu is carried as the "
                + "child-item collection and handle reference the headless model publishes, and "
                + "nothing more. Font measurement, DPI-to-pixel conversion, window geometry and the "
                + "actual drawing of a menu are the rendering half and are deferred; naming that gap "
                + "is deliberate, and it is a documented capability boundary rather than a silent "
                + "omission."),
            static (service, request, context) => service.GetContextMenuModel(request, context));

        MapUnary<DataWindowImplementation, ApplyContextMenuModelRequest, ApplyContextMenuModelResponse>(
            group,
            new("/context-menu/apply", "applyContextMenuModel", "ApplyContextMenuModel",
                ContractSurface.DataWindow,
                "Apply a context menu selection and read the outcome.",
                "Applies a chosen menu identifier through the headless model. Where the legacy would "
                + "have raised one of its TEN live dialogs, the response carries a structured error "
                + "instead - preserving the exact text, the localization category the legacy used, "
                + "the Sprintf substitution arguments in order and the stop-sign severity. ONLY THE "
                + "DELIVERY CHANNEL CHANGES."),
            static (service, request, context) => service.ApplyContextMenuModel(request, context));

        MapUnary<DataWindowImplementation, GetRowSelectStateRequest, GetRowSelectStateResponse>(
            group,
            new("/row-select/state", "getRowSelectState", "GetRowSelectState",
                ContractSurface.DataWindow,
                "Read the row-selection service's state.",
                "Reports the selection style - single is 1 and multiple is 2, the legacy values - and "
                + "the current selection state machine. Selection ranges are ONE-BASED."),
            static (service, request, context) => service.GetRowSelectState(request, context));

        MapUnary<DataWindowImplementation, ApplyRowSelectStyleRequest, ApplyRowSelectStyleResponse>(
            group,
            new("/row-select/style", "applyRowSelectStyle", "ApplyRowSelectStyle",
                ContractSurface.DataWindow,
                "Set the row-selection style and read the resulting state.",
                "Sets the selection style and returns the resulting state. Where the model reaches a "
                + "click entry point, the SHIFT AND CONTROL MODIFIERS ARE EXPLICIT REQUEST FIELDS "
                + "rather than ambient state, because a headless service cannot read OS keyboard "
                + "state - the legacy read it from the message, and the modifier is now data. The "
                + "single dialog the legacy raises on this path "
                + "[n_cst_dwsvc_rowselect.sru:L239] becomes a structured error carrying its text, its "
                + "localization category, its Sprintf arguments and its severity."),
            static (service, request, context) => service.ApplyRowSelectStyle(request, context));
    }



    // ==============================================================================================
    //  C-04 - THE TWENTY-FIVE PROJECTED ColumnExpressionService OPERATIONS
    //
    //  C-04 declares twenty-seven methods. The two exclusions are `InvokeMethodChannel` and
    //  `TraceChannel`, both BIDIRECTIONAL and both INVERTED - the server asks and the client answers -
    //  so neither has a request/response direction to project. Twenty-five are declared here.
    //
    //  THE `$` VERSUS `$$` DISTINCTION IS THE REASON THIS SURFACE IS SO LARGE, and it does not
    //  survive naive serialization. Static expansion substitutes the variable's value AT THE MOMENT
    //  THE EXPRESSION IS SET, so the variable name is GONE from the stored text and a later
    //  assignment cannot reach it; dynamic expansion retains the variable AS A REFERENCE, resolved at
    //  calculation time, so a later assignment propagates. The legacy specification's worked example
    //  fixes a static result at 5 for ever while the identical sequence under dynamic expansion
    //  yields 6 [docs/n_cst_dwsvc_columnexp.md]. Three things must therefore travel together on every
    //  payload that carries a binding - the UNEXPANDED source text, the BIND-TIME snapshot and the
    //  LIVE environment - and THE MODE IS TAGGED PER REFERENCE, never per expression, because the
    //  specification documents an example mixing both inside one expression. This projection carries
    //  all of it verbatim; it never expands, re-expands or normalizes an expression.
    // ==============================================================================================

    /// <summary>
    /// Declares C-04's twenty-five projected operations: the session pair, the expression table, the
    /// typed variable environment, the four calculation entry points, the two service switches and
    /// the two state reads, plus the projected event stream.
    /// </summary>
    /// <param name="group">The <c>/v1/datawindow/expression</c> group.</param>
    private static void MapColumnExpressionOperations(RouteGroupBuilder group)
    {
        MapUnary<ColumnExpressionImplementation, OpenExpressionSessionRequest, OpenExpressionSessionResponse>(
            group,
            new("/sessions", "openExpressionSession", "OpenExpressionSession",
                ContractSurface.ColumnExpression,
                "Open an expression session and receive its correlation identifier.",
                "THE SESSION IS THE REPLACEMENT FOR A POINTER. The legacy foreign-variable structure "
                + "holds a LIVE IN-PROCESS OBJECT POINTER to another DataWindow's expression service "
                + "[n_cst_dwsvc_columnexp.sru:L80-L83], and a pointer cannot be serialized. A session "
                + "scopes the DataWindow handles a cross-DataWindow reference may resolve against, so "
                + "such a reference is supported when both DataWindows are CO-RESIDENT in one session "
                + "inside one service instance and is BLOCKED with a defined error otherwise. That is "
                + "this refactor's one deliberately narrowed contract, narrowed with a defined error "
                + "rather than widened with a guess.",
                DeclaresNotFound: false),
            static (service, request, context) => service.OpenExpressionSession(request, context));

        MapSessionScoped<ColumnExpressionImplementation, CloseExpressionSessionRequest, CloseExpressionSessionResponse>(
            group,
            new($"/sessions/{{{SessionIdParameter}}}", "closeExpressionSession",
                "CloseExpressionSession", ContractSurface.ColumnExpression,
                "Close an expression session and release its scoped handles.",
                "Releases the session the paired open returned, together with every DataWindow handle "
                + "scoped to it. Closing a session that does not exist is reported as 404 rather than "
                + "silently succeeding, so a caller can detect a double close.",
                BadRequest: BadRequestDeclaration.None),
            HttpMethods.Delete,
            static sessionId => new CloseExpressionSessionRequest { SessionId = sessionId },
            static (service, request, context) => service.CloseExpressionSession(request, context));

        MapUnary<ColumnExpressionImplementation, AddExpressionRequest, AddExpressionResponse>(
            group,
            new("/expressions/add", "addExpression", "AddExpression",
                ContractSurface.ColumnExpression,
                "Add a column expression.",
                "Registers an expression against a column. The payload carries the UNEXPANDED source "
                + "text and the per-reference expansion mode; the engine emits the reference arrays "
                + "and the reverse dependency index that make dirty propagation work. Nothing here "
                + "expands the text."),
            static (service, request, context) => service.AddExpression(request, context));

        MapUnary<ColumnExpressionImplementation, SetExpressionRequest, SetExpressionResponse>(
            group,
            new("/expressions/set", "setExpression", "SetExpression",
                ContractSurface.ColumnExpression,
                "Replace a column expression.",
                "Replaces the expression registered against a column. THE BIND MOMENT MATTERS: a "
                + "static reference is resolved and substituted into the rewritten text HERE, so no "
                + "later assignment can reach it, while a dynamic reference is left pointing into the "
                + "live variable table so a later assignment propagates. The legacy declares this "
                + "member in four arities; the contract carries the union as optional fields rather "
                + "than four operations."),
            static (service, request, context) => service.SetExpression(request, context));

        MapUnary<ColumnExpressionImplementation, GetExpressionRequest, GetExpressionResponse>(
            group,
            new("/expressions/get", "getExpression", "GetExpression",
                ContractSurface.ColumnExpression,
                "Read a column expression.",
                "Returns the expression as the engine holds it: the unexpanded source text, the "
                + "bind-time snapshot and the live environment, with each reference tagged by its "
                + "expansion mode. A caller receiving an already-expanded string could not tell a "
                + "static binding from a literal, which is precisely why all three travel."),
            static (service, request, context) => service.GetExpression(request, context));

        MapUnary<ColumnExpressionImplementation, RemoveExpressionRequest, RemoveExpressionResponse>(
            group,
            new("/expressions/remove", "removeExpression", "RemoveExpression",
                ContractSurface.ColumnExpression,
                "Remove a column expression.",
                "Removes one expression and the reverse dependency index entries that pointed at it. "
                + "The legacy declares this member in two arities and the contract carries both "
                + "through optional fields."),
            static (service, request, context) => service.RemoveExpression(request, context));

        MapUnary<ColumnExpressionImplementation, RemoveAllExpressionsRequest, RemoveAllExpressionsResponse>(
            group,
            new("/expressions/remove-all", "removeAllExpressions", "RemoveAllExpressions",
                ContractSurface.ColumnExpression,
                "Remove every column expression in the session.",
                "Clears the whole expression table and its dependency index. The variable environment "
                + "is a separate concern and is not cleared by this operation."),
            static (service, request, context) => service.RemoveAllExpressions(request, context));

        MapUnary<ColumnExpressionImplementation, AddVariableRequest, AddVariableResponse>(
            group,
            new("/variables/add", "addVariable", "AddVariable", ContractSurface.ColumnExpression,
                "Add a typed expression variable.",
                "THE VARIABLE ENVIRONMENT IS STRONGLY TYPED across seven distinct scalar types - "
                + "time, string, long, double, datetime, date and boolean - which the legacy declares "
                + "as seven overloads. Its wire representation is a DISCRIMINATED UNION, never a "
                + "stringly-typed map: collapsing it to strings would lose the type information the "
                + "engine's coercion behaviour depends on."),
            static (service, request, context) => service.AddVariable(request, context));

        MapUnary<ColumnExpressionImplementation, SetVariableRequest, SetVariableResponse>(
            group,
            new("/variables/set", "setVariable", "SetVariable", ContractSurface.ColumnExpression,
                "Set a typed expression variable's value.",
                "Assigns a variable across the same seven types, each of which the legacy declares in "
                + "three arities - value, value plus recalculate, and value plus recalculate plus "
                + "force. THE ASSIGNMENT IS WHERE THE EXPANSION DISTINCTION BECOMES OBSERVABLE: it "
                + "propagates to every DYNAMIC reference and to none of the STATIC ones, because a "
                + "static reference no longer names the variable at all."),
            static (service, request, context) => service.SetVariable(request, context));

        MapUnary<ColumnExpressionImplementation, AddVariableExpressionRequest, AddVariableExpressionResponse>(
            group,
            new("/variable-expressions/add", "addVariableExpression", "AddVariableExpression",
                ContractSurface.ColumnExpression,
                "Add a variable whose value is itself an expression.",
                "The local-variable structure is RECURSIVE: a variable's value may itself be an "
                + "expression with its own variable and function references "
                + "[n_cst_dwsvc_columnexp.sru:L73-L78]. The payload therefore nests, and the "
                + "has-macro flag travels with each level rather than only at the top."),
            static (service, request, context) => service.AddVariableExpression(request, context));

        MapUnary<ColumnExpressionImplementation, SetVariableExpressionRequest, SetVariableExpressionResponse>(
            group,
            new("/variable-expressions/set", "setVariableExpression", "SetVariableExpression",
                ContractSurface.ColumnExpression,
                "Replace a variable expression.",
                "Replaces a variable's expression, re-binding its static references at that moment "
                + "and leaving its dynamic ones as references. The legacy declares this member in "
                + "three arities, carried through optional fields."),
            static (service, request, context) => service.SetVariableExpression(request, context));

        MapUnary<ColumnExpressionImplementation, GetVariableExpressionRequest, GetVariableExpressionResponse>(
            group,
            new("/variable-expressions/get", "getVariableExpression", "GetVariableExpression",
                ContractSurface.ColumnExpression,
                "Read a variable expression.",
                "Returns the variable's expression with its nested references and its per-reference "
                + "expansion modes intact."),
            static (service, request, context) => service.GetVariableExpression(request, context));

        MapUnary<ColumnExpressionImplementation, AddForeignVariableRequest, AddForeignVariableResponse>(
            group,
            new("/foreign-variables/add", "addForeignVariable", "AddForeignVariable",
                ContractSurface.ColumnExpression,
                "Add a cross-DataWindow variable. Subject to the session co-residency limit.",
                "THE ONE DELIBERATELY NARROWED OPERATION IN THIS CONTRACT. The legacy holds a live "
                + "in-process pointer to the other DataWindow's expression service "
                + "[n_cst_dwsvc_columnexp.sru:L80-L83] and additionally notes that a foreign variable "
                + "REQUIRES dynamic expansion. A pointer cannot be serialized, so the reference is "
                + "resolved through a SESSION-SCOPED DataWindow handle and is supported only when both "
                + "DataWindows are co-resident in one expression session inside one service instance. "
                + "A reference spanning sessions or instances is BLOCKED and returns a defined error "
                + "rather than a silently wrong value. A caller receiving that rejection must NOT "
                + "retry: the topology rejected it, not the request. It reads as a 400 carrying the "
                + "originating retCode and the expression-error category, because the published "
                + "status mapping sanctions no status of its own and already provides that mechanism.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (service, request, context) => service.AddForeignVariable(request, context));

        MapUnary<ColumnExpressionImplementation, SetRelativeColumnsRequest, SetRelativeColumnsResponse>(
            group,
            new("/relative-columns/set", "setRelativeColumns", "SetRelativeColumns",
                ContractSurface.ColumnExpression,
                "Declare the columns an expression depends on.",
                "Writes the reverse dependency index the engine uses to decide what a column change "
                + "makes dirty [n_cst_dwsvc_columnexp.sru:L44-L48]. The legacy declares both the "
                + "relative-column and the relative-input-column family in singular and plural array "
                + "forms, addressed by index and by name; the contract carries all four shapes as "
                + "fields of one request rather than as four operations."),
            static (service, request, context) => service.SetRelativeColumns(request, context));

        MapUnary<ColumnExpressionImplementation, SetExpressionFlagRequest, SetExpressionFlagResponse>(
            group,
            new("/flags/set", "setExpressionFlag", "SetExpressionFlag",
                ContractSurface.ColumnExpression,
                "Set the always-calculate, recursive and trigger-event flags.",
                "Sets the three per-expression flags, each of which the legacy exposes by index and by "
                + "name. They are carried as one operation over an explicit flag selector so a caller "
                + "cannot set one flag while believing it set another."),
            static (service, request, context) => service.SetExpressionFlag(request, context));

        MapUnary<ColumnExpressionImplementation, CalcRequest, CalcResponse>(
            group,
            new("/calc", "calc", "Calc", ContractSurface.ColumnExpression,
                "Calculate one expression.",
                "Evaluates one expression, resolving every DYNAMIC reference against the live "
                + "environment at this moment while every STATIC reference keeps the value it was "
                + "bound with. Where the evaluation reaches a parse failure the response carries a "
                + "structured error with the expression text and the NUMERIC caret position as "
                + "separate fields, so the legacy rendering - whose gutter is measured in BYTES, a "
                + "wide character counting as two [n_cst_dwsvc_columnexp.sru:L2402-L2407] - is "
                + "reproducible byte for byte without this projection re-rendering it. THOSE 28 "
                + "MESSAGES ARE HARDCODED AND DO NOT ROUTE THROUGH LOCALIZATION, unlike the "
                + "validation path's, and that inconsistency is reproduced rather than harmonized. A "
                + "cross-session foreign reference is blocked here as it is on the add.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (service, request, context) => service.Calc(request, context));

        MapUnary<ColumnExpressionImplementation, CalcAllRequest, CalcAllResponse>(
            group,
            new("/calc-all", "calcAll", "CalcAll", ContractSurface.ColumnExpression,
                "Calculate every expression in the session.",
                "Evaluates the whole expression table. The tri-state calculation cache - unknown, yes "
                + "and no - and the recursion stack the engine's vector holds are the engine's own "
                + "and are not reshaped here. The legacy declares this member in two arities, carried "
                + "through optional fields.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (service, request, context) => service.CalcAll(request, context));

        MapUnary<ColumnExpressionImplementation, CalcEmptyRequest, CalcEmptyResponse>(
            group,
            new("/calc-empty", "calcEmpty", "CalcEmpty", ContractSurface.ColumnExpression,
                "Calculate only the expressions whose value is currently empty.",
                "Evaluates the subset the engine considers empty, honouring the per-expression "
                + "empty-string-is-null setting exactly as the legacy structure declares it "
                + "[n_cst_dwsvc_columnexp.sru:L22-L42].",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (service, request, context) => service.CalcEmpty(request, context));

        MapUnary<ColumnExpressionImplementation, CalcItemRequest, CalcItemResponse>(
            group,
            new("/calc-item", "calcItem", "CalcItem", ContractSurface.ColumnExpression,
                "Calculate one row-and-column item.",
                "Evaluates a single item. THE LEGACY MEMBER BEHIND THIS OPERATION IS DECLARED PUBLIC "
                + "DESPITE CARRYING THE PRIVATE-CONVENTION UNDERSCORE PREFIX - an anomaly carried "
                + "across rather than tidied away, which is why the protocol definition records the "
                + "original spelling on the method itself. Row and column ordinals are ONE-BASED.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (service, request, context) => service.CalcItem(request, context));

        MapUnary<ColumnExpressionImplementation, SetEnabledRequest, SetEnabledResponse>(
            group,
            new("/enabled", "setExpressionServiceEnabled", "SetEnabled",
                ContractSurface.ColumnExpression,
                "Enable or disable the expression service.",
                "Projects the legacy enabled property. ITS VETO MAPS TO THE FAILED RETURN CODE (-1) "
                + "AND NOT TO PREVENT (1), which matters because PREVENT satisfies the success "
                + "predicate under the preserved algebra while FAILED does not - a caller reading the "
                + "wrong one would see a refusal as a success. Note also the coupling recorded on the "
                + "event gate: disabling item-change on a validation session suppresses expression "
                + "evaluation independently of this switch."),
            static (service, request, context) => service.SetEnabled(request, context));

        MapUnary<ColumnExpressionImplementation, SetTraceRequest, SetTraceResponse>(
            group,
            new("/trace", "setExpressionTrace", "SetTrace", ContractSurface.ColumnExpression,
                "Enable or disable expression tracing.",
                "Turns the engine's trace on or off. Tracing is PURE DIAGNOSTICS and fire-and-forget: "
                + "it never blocks or gates an expression result. The bidirectional trace channel "
                + "itself has no REST projection because it is INVERTED; the projected event stream "
                + "below is the request/response-shaped read."),
            static (service, request, context) => service.SetTrace(request, context));

        MapUnary<ColumnExpressionImplementation, GetServiceStateRequest, GetServiceStateResponse>(
            group,
            new("/state", "getExpressionServiceState", "GetServiceState",
                ContractSurface.ColumnExpression,
                "Read the expression service's state.",
                "Reports whether the service is enabled, whether tracing is on, and the session's own "
                + "scoped state. A read, with no side effect on the calculation cache."),
            static (service, request, context) => service.GetServiceState(request, context));

        MapUnary<ColumnExpressionImplementation, GetExpressionStateRequest, GetExpressionStateResponse>(
            group,
            new("/engine-state", "getExpressionState", "GetExpressionState",
                ContractSurface.ColumnExpression,
                "Read the engine-state snapshot - the expression table, the reverse dependency index "
                + "and the bindings.",
                "The engine's own model, projected as data: the expression table, the reverse "
                + "dependency index from a column to the expressions and variables depending on it, "
                + "and the BINDINGS - each carrying the unexpanded source text, the bind-time snapshot "
                + "and the live environment, with the expansion mode tagged PER REFERENCE. This is "
                + "the payload the requirements single out as not surviving naive serialization, and "
                + "it is the reason this operation is projected rather than treated as an internal "
                + "detail."),
            static (service, request, context) => service.GetExpressionState(request, context));

        MapServerStream<ColumnExpressionImplementation, EventStreamRequest, EventStreamResponse>(
            group,
            new("/event-stream", "getExpressionEventStream", "EventStream",
                ContractSurface.ColumnExpression,
                "Poll the engine's own event sequence as an ordered collection. Token required.",
                "A SERVER stream of the events the engine emits ON ITSELF - item-changed, "
                + "do-item-changed with its from-input flag, and var-changed with its force-calculate "
                + "flag. A server stream and not an inverted one: these are notifications the engine "
                + "EMITS, not questions it asks, which is why this one is projectable while the two "
                + "inverted channels are not. EVERY RECORD CARRIES ITS SEQUENCING TOKEN AND ITS "
                + "DECLARED ORDERING DISCIPLINE, AND SEQUENCE NUMBERS ARE FOR DETECTION ONLY - an "
                + "out-of-order arrival is a hard error, never a reorder opportunity, so a consumer "
                + "must not sort, buffer-and-reorder, de-duplicate or replay what it receives. "
                + "THIS OPERATION IS A BOUNDED POLL AND NOT THE SUBSCRIPTION'S WHOLE LIFETIME, WHICH "
                + "IS THE ONE PLACE THIS PROJECTION DOES NOT MIRROR ITS gRPC TWIN. The gRPC stream ends "
                + "only when the client goes away, and a request/response operation cannot wait for "
                + "that, so the collection is bounded by this service's configured collection window "
                + "(DataServices:RestProjection:StreamCollectionWindow, two seconds as shipped) and the "
                + "response is the records that arrived within it. An empty collection therefore means "
                + "'nothing was emitted during the window' and never 'the subscription ended'. A "
                + "consumer that wants continuous delivery calls again - the sequencing token on the "
                + "last record it received is how it detects a gap between polls - or uses the gRPC "
                + "stream, which has no window because it needs none.",
                SuccessDescription:
                "The collection window closed. The body is the ordered sequence of records the gRPC "
                + "server stream delivered within it, each retaining its sequencing token and its "
                + "declared ordering discipline. An empty array is a complete, successful answer "
                + "meaning no event was emitted during the window."),
            static (service, request, stream, context) =>
                service.EventStream(request, stream, context),
            collectWithinWindow: true);
    }



    // ==============================================================================================
    //  REGISTRATION HELPERS
    //
    //  Three shapes, and exactly three, because the contract publishes exactly three: a body-bound
    //  unary POST, a session-scoped operation whose only argument is an identifier on the path or the
    //  query string, and a body-bound POST over a server stream. Driving all thirty-nine operations
    //  through one of the three is what makes them impossible to drift apart, and what makes diffing
    //  them against the authored contract a mechanical exercise.
    //
    //  THE SERVICE TYPE IS AN EXPLICIT TYPE ARGUMENT AT EVERY CALL SITE rather than inferred. The two
    //  projected implementations share no base type beyond `object`, and stating which one an
    //  operation reaches is information a reader of the table wants anyway.
    // ==============================================================================================

    /// <summary>
    /// Declares one body-bound unary operation.
    /// </summary>
    /// <typeparam name="TService">The gRPC service implementation the operation reaches.</typeparam>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="group">The route group the operation is declared on.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <param name="invoke">Invokes the projected method on a resolved service instance.</param>
    private static void MapUnary<TService, TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        Func<TService, TRequest, ServerCallContext, Task<TResponse>> invoke)
        where TService : class
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        // TYPED AS Func RATHER THAN LEFT AS A BARE LAMBDA, DELIBERATELY. A one-parameter lambda taking
        // HttpContext is convertible to RequestDelegate, whose MapPost overload DISCARDS the returned
        // value - the projected body would never reach the caller, and the analyzer that catches it
        // (ASP0016) is an error here rather than a warning. Naming the delegate type selects the route
        // handler overload instead, which writes the result.
        Func<HttpContext, Task<IResult>> handler = httpContext => ProjectAsync(
            httpContext,
            operation,
            async (service, context) =>
            {
                string body = await ReadRequestBodyAsync(httpContext, context.CancellationToken)
                    .ConfigureAwait(false);

                if (!TryBindRequest(body, out TRequest? request, out StatusProjection rejection))
                {
                    return RejectRequest(httpContext, rejection);
                }

                return Render(
                    httpContext,
                    await invoke(service, request, context).ConfigureAwait(false));
            },
            static services => ResolveService<TService>(services));

        RouteHandlerBuilder route = group.MapPost(operation.Route, handler);

        route.Accepts<ProtoPayload>(MediaTypeNames.Application.Json);
        route.Produces<ProtoPayload>(StatusCodes.Status200OK, MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, operation);
    }

    /// <summary>
    /// Declares one session-scoped operation whose only argument is a session identifier taken from
    /// the route template or the query string.
    /// </summary>
    /// <typeparam name="TService">The gRPC service implementation the operation reaches.</typeparam>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="group">The route group the operation is declared on.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <param name="httpMethod">The single HTTP method the contract declares for it.</param>
    /// <param name="buildRequest">Builds the request message from the identifier.</param>
    /// <param name="invoke">Invokes the projected method on a resolved service instance.</param>
    /// <remarks>
    /// The identifier is bound by the framework - from the path where the template names it and from
    /// the query string where it does not - and it is declared as a non-nullable argument because the
    /// contract declares the parameter REQUIRED. No request body is accepted, and none is declared.
    /// </remarks>
    private static void MapSessionScoped<TService, TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        string httpMethod,
        Func<string, TRequest> buildRequest,
        Func<TService, TRequest, ServerCallContext, Task<TResponse>> invoke)
        where TService : class
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        RouteHandlerBuilder route = group.MapMethods(
            operation.Route,
            [httpMethod],
            (HttpContext httpContext, string sessionId) => ProjectAsync(
                httpContext,
                operation,
                async (service, context) => Render(
                    httpContext,
                    await invoke(service, buildRequest(sessionId), context).ConfigureAwait(false)),
                static services => ResolveService<TService>(services)));

        route.Produces<ProtoPayload>(StatusCodes.Status200OK, MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, operation);
    }

    /// <summary>
    /// Declares one body-bound operation over a server-streaming method.
    /// </summary>
    /// <typeparam name="TService">The gRPC service implementation the operation reaches.</typeparam>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf message each stream element carries.</typeparam>
    /// <param name="group">The route group the operation is declared on.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <param name="invoke">Invokes the projected method against a collecting stream writer.</param>
    /// <param name="collectWithinWindow">
    /// Whether this operation's upstream is a subscription that never completes on its own, so the
    /// collection must be bounded by a finite window. Defaults to <see langword="false"/>, which is
    /// correct for every stream that terminates itself.
    /// </param>
    /// <remarks>
    /// <para>
    /// The response is the WHOLE sequence as an ordered collection, which is the shape the authored
    /// contract declares for both projected server streams - a bare array rather than an invented
    /// envelope, because the protocol definition has no envelope message for a stream.
    /// </para>
    /// <para>
    /// The elements are collected before anything is written to the response, which is what makes a
    /// mid-stream failure produce a clean problem body rather than a half-written success. NOTHING IS
    /// TRUNCATED: a caller either receives every element the stream produced, in order, or receives a
    /// failure.
    /// </para>
    /// <para>
    /// 🔴 <b>"EVERY ELEMENT THE STREAM PRODUCED" NEEDS A DEFINITION FOR A STREAM THAT NEVER STOPS
    /// PRODUCING, AND <paramref name="collectWithinWindow"/> IS IT.</b> One projected operation - the
    /// expression event stream - is a SUBSCRIPTION whose upstream ends only when the client goes away, so
    /// collecting it to completion meant a normal HTTP request received neither its events nor a success
    /// status and held a request thread, a relay subscription and a connection until the caller gave up.
    /// With the flag set, the collection is bounded by
    /// <see cref="RestProjectionOptions.StreamCollectionWindow"/> and an expired window is a COMPLETE
    /// ANSWER - the records available now, empty included - rather than a truncation or a fault. It is
    /// opt-in per operation and not a property of streaming, because a retrieval ends with its
    /// final-marked chunk and windowing one of those would truncate a legitimate result.
    /// </para>
    /// </remarks>
    private static void MapServerStream<TService, TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        Func<TService, TRequest, IServerStreamWriter<TResponse>, ServerCallContext, Task> invoke,
        bool collectWithinWindow = false)
        where TService : class
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        ProjectedOperation streaming = operation with { ServerStreaming = true };

        // Typed as Func for the reason recorded on the unary helper: a bare one-parameter lambda would
        // bind to the RequestDelegate overload, which discards the projected body.
        Func<HttpContext, Task<IResult>> handler = httpContext => ProjectAsync(
            httpContext,
            streaming,
            async (service, context) =>
            {
                // THE BODY IS READ ON THE REQUEST'S OWN TOKEN AND NOT ON THE CONTEXT'S, which matters only
                // for a windowed operation and matters absolutely there: the window bounds the
                // SUBSCRIPTION, so letting it also bound the upload would answer an empty collection to a
                // slow client whose collection had not begun. The window is armed below, after binding.
                string body = await ReadRequestBodyAsync(httpContext, httpContext.RequestAborted)
                    .ConfigureAwait(false);

                if (!TryBindRequest(body, out TRequest? request, out StatusProjection rejection))
                {
                    return RejectRequest(httpContext, rejection);
                }

                RestProjectionOptions projection = httpContext.RequestServices
                    .GetRequiredService<IOptions<DataServicesOptions>>()
                    .Value
                    .RestProjection;

                CollectingStreamWriter<TResponse> collected = new(projection.MaxStreamedElements);

                if (collectWithinWindow)
                {
                    context.StartCollectionWindow(projection.StreamCollectionWindow);
                }

                try
                {
                    await invoke(service, request, collected, context).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (context.WindowExpired)
                {
                    // AN EXPIRED WINDOW IS AN EMPTY-OR-PARTIAL COLLECTION, NEVER A FAULT, AND THIS ARM IS
                    // WHY THAT DOES NOT DEPEND ON THE PROJECTED METHOD'S OWN MANNERS. EventStream ends its
                    // read loop quietly on cancellation today, so this arm is not reached by it; a
                    // server-streaming method that PROPAGATED the cancellation instead would otherwise
                    // turn a completed poll into a 500, and the projection would be relying on an
                    // implementation detail of the thing it projects. Everything written before expiry is
                    // already in the collector, so the answer below is the records available now.
                }
                catch (StreamedResponseTooLargeException tooLarge)
                {
                    // 502, NOT 500 AND NOT 413. The bound is this service's, but what exceeded it is the
                    // UPSTREAM producing more than this projection can answer as one document - so the
                    // fault is behind this service rather than in the caller's request, and 413 would be
                    // untrue because the REQUEST was not too large. Nothing has been written to the
                    // response at this point, which is exactly why the sequence is collected first.
                    return RejectRequest(
                        httpContext,
                        new StatusProjection(
                            StatusCodes.Status502BadGateway,
                            RetCode.E_OUT_OF_BOUND,
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"The projected stream produced more than the {tooLarge.Limit} elements "
                                + $"this projection will answer as one document. The response is refused "
                                + $"rather than truncated, because a truncated array cannot be told apart "
                                + $"from a complete one."),
                            FromUpstream: true));
                }

                // A CALLER THAT WENT AWAY IS ANSWERED WITH NOTHING, matching the shared failure path's own
                // posture. It is checked here rather than left to that path because a projected method may
                // END QUIETLY on cancellation - EventStream does - so an aborted request would otherwise
                // reach this point looking exactly like a completed collection and render a 200 body for a
                // connection nobody is reading.
                if (httpContext.RequestAborted.IsCancellationRequested)
                {
                    return Results.Empty;
                }

                return RenderSequence(collected.Written);
            },
            static services => ResolveService<TService>(services));

        RouteHandlerBuilder route = group.MapPost(streaming.Route, handler);

        route.Accepts<ProtoPayload>(MediaTypeNames.Application.Json);
        route.Produces<IReadOnlyList<ProtoPayload>>(
            StatusCodes.Status200OK,
            MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, streaming);
    }

    /// <summary>
    /// Declares one operation's name, tags, prose and response surface, and attaches the transformer
    /// that writes its published metadata into the generated document.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="route">The route being described.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <remarks>
    /// Each operation declares the responses it can ACTUALLY produce and no others. A status every
    /// generated client must branch on but no operation can return is noise that hides the real
    /// surface, which is why the two session opens declare no <c>404</c>, the two closes and the gate
    /// read declare no <c>400</c>, and <c>409</c> appears on the update alone.
    /// </remarks>
    private static void Describe<TRequest, TResponse>(
        RouteHandlerBuilder route,
        ProjectedOperation operation)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        string protoRequest = new TRequest().Descriptor.FullName;
        string protoResponse = new TResponse().Descriptor.FullName;

        route
            .WithName(operation.OperationId)
            .WithTags(operation.Tag)
            .WithSummary(operation.Summary)
            .WithDescription(operation.Description);

        if (operation.BadRequest != BadRequestDeclaration.None)
        {
            route.ProducesProblem(
                StatusCodes.Status400BadRequest,
                MediaTypeNames.Application.ProblemJson);
        }

        // The 401 is a DECLARED response on every route, not an implementation detail: it is the
        // standing proof that this boundary is authenticated (constraint C-G).
        route.ProducesProblem(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson);

        if (operation.DeclaresNotFound)
        {
            route.ProducesProblem(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson);
        }

        if (operation.DeclaresConflict)
        {
            // NOT `ProducesProblem`, and the difference is the whole point of the 409.
            //
            // `ProducesProblem` would publish the bare problem shape, and a consumer reading this
            // document could not see that a `conflict` member is present. The contract's own
            // `ConflictProblemDetails` says why that is unacceptable: the conflict payload is the one
            // body in this document a caller must be able to read in order to ACT. A caller that
            // cannot see WHICH column moved cannot construct a retry - it would re-send the same stale
            // original values and receive the same 409 for ever [AAP 0.6.3.8: callers implement an
            // explicit retry-or-surface policy, and there is no silent overwrite anywhere].
            route.Produces<ConflictProblemDetails>(
                StatusCodes.Status409Conflict,
                MediaTypeNames.Application.ProblemJson);
        }

        // THE FOUR STATUSES EVERY PROJECTED OPERATION CAN REALLY PRODUCE, declared unconditionally
        // because none of them depends on which method is projected.
        //
        // 429 is a capacity ceiling refusing to take more work - the handle registries behind C-05..C-08
        // answer the legacy E_BUSY code once a per-principal or global limit is reached, and that surfaces
        // as ResourceExhausted on any call that needs a handle. 503 is an upstream answering that it is not
        // currently serving, which is a different fact from 502: a 502 means no gRPC response arrived at
        // all. 504 is the deadline this service sets on EVERY outbound call elapsing, so its expiry is an
        // ordinary outcome of a slow upstream rather than a hypothetical.
        //
        // Two statuses the failure map also translates are deliberately NOT declared. AlreadyExists is
        // produced by exactly one method in the estate - the macro channel reporting an existing
        // attachment - and that method is bidirectional and unprojected, so no route here can return it.
        // Unimplemented on a projected method would mean the upstream does not implement a method this
        // projection publishes, which under explicitly versioned contracts is a deployment defect rather
        // than an outcome; the 501 that this service does publish belongs to the reserved routes, which
        // declare it themselves.
        route.ProducesProblem(
            StatusCodes.Status429TooManyRequests,
            MediaTypeNames.Application.ProblemJson);

        route.ProducesProblem(
            StatusCodes.Status500InternalServerError,
            MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(
            StatusCodes.Status503ServiceUnavailable,
            MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(
            StatusCodes.Status504GatewayTimeout,
            MediaTypeNames.Application.ProblemJson);

        route.AddOpenApiOperationTransformer((openApiOperation, context, _) =>
            ApplyContractMetadataAsync(openApiOperation, context, operation, protoRequest, protoResponse));
    }

    /// <summary>
    /// Removes the generated C# scaffolding from the published conflict schemas, leaving exactly the
    /// members the protocol definition declares.
    /// </summary>
    /// <param name="document">The document being built, or <see langword="null"/> when none is.</param>
    /// <remarks>
    /// <para>
    /// REQUIRED FOR TRUTHFULNESS, NOT TIDINESS. Deriving the conflict schema from the generated message
    /// type is what keeps it from drifting out of step with <c>Proto/common.v1.proto</c>, but the
    /// generated C# carries members that never appear on the wire: a <c>hasX</c> presence accessor for
    /// every field with explicit presence, and a <c>kindCase</c> discriminator for every <c>oneof</c>.
    /// Published unfiltered, a consumer generating a strict client models members that no payload will
    /// ever contain - on the one payload a caller must read correctly in order to construct a retry.
    /// </para>
    /// <para>
    /// <b>The retained set is exact rather than heuristic.</b> It is read from each message's own
    /// <see cref="MessageDescriptor"/> - the canonical JSON name and the original proto name of every
    /// declared field - so the protocol definition decides what is published. A name-pattern rule
    /// would be guesswork that could drop a legitimate field and would need revisiting whenever a field
    /// is added; this cannot, because it asks the descriptor.
    /// </para>
    /// <para>
    /// The walk starts at the conflict detail and follows message-typed fields transitively, so the
    /// whole published closure is covered - the detail, its rows, their column values and the value
    /// union with its nested types - without naming any of them here. Only schemas whose component name
    /// matches a message in that closure are touched, so the problem shape and the summary envelope are
    /// left exactly as the framework produced them.
    /// </para>
    /// </remarks>
    private static void PruneGeneratedMessageSchemas(OpenApiDocument? document)
    {
        IDictionary<string, IOpenApiSchema>? schemas = document?.Components?.Schemas;

        if (schemas is null || schemas.Count == 0)
        {
            return;
        }

        Queue<MessageDescriptor> pending = new();
        HashSet<string> visited = new(StringComparer.Ordinal);

        pending.Enqueue(ConflictDetail.Descriptor);

        while (pending.Count > 0)
        {
            MessageDescriptor descriptor = pending.Dequeue();

            if (!visited.Add(descriptor.FullName))
            {
                continue;
            }

            HashSet<string> declared = new(StringComparer.Ordinal);

            foreach (FieldDescriptor field in descriptor.Fields.InFieldNumberOrder())
            {
                declared.Add(field.JsonName);
                declared.Add(field.Name);

                if (field.FieldType == FieldType.Message && field.MessageType is not null)
                {
                    pending.Enqueue(field.MessageType);
                }
            }

            if (schemas.TryGetValue(descriptor.Name, out IOpenApiSchema? published)
                && published is OpenApiSchema concrete
                && concrete.Properties is not null)
            {
                foreach (string member in concrete.Properties.Keys
                    .Where(name => !declared.Contains(name))
                    .ToList())
                {
                    concrete.Properties.Remove(member);
                }
            }
        }
    }


    // ==============================================================================================
    //  THE PROJECTION ITSELF - ONE ENTRY POINT, ONE FAILURE PATH
    // ==============================================================================================

    /// <summary>
    /// Resolves the projected implementation, runs the projection, and translates every failure it can
    /// raise through the single shared status mapping.
    /// </summary>
    /// <typeparam name="TService">The gRPC service implementation the operation reaches.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="operation">The operation being projected, which names the gRPC method.</param>
    /// <param name="project">Binds, invokes and renders.</param>
    /// <param name="resolve">Resolves the implementation from request services.</param>
    /// <returns>The projected result.</returns>
    /// <remarks>
    /// <para>
    /// EVERY ROUTE IN THIS FILE PASSES THROUGH HERE, so the status translation exists exactly once.
    /// There is no per-endpoint <c>try</c>/<c>catch</c> anywhere in this file, which is both the
    /// correctness property - forty copies would drift apart - and the coverage property, since
    /// one mapper is reachable from every route.
    /// </para>
    /// <para>
    /// THE CATCH SET IS DELIBERATELY NARROW. <see cref="RpcException"/> is the one failure channel the
    /// projected methods use, including for a transport fault on the single outbound edge these
    /// operations traverse, which arrives as <see cref="StatusCode.Unavailable"/> carrying a debug
    /// exception. A caller-abandoned request is answered with nothing, because there is nobody left to
    /// answer. Nothing else is caught: swallowing a broader exception would hide a fault that belongs
    /// on the operator channel, and softening a structural fault into a response would be graceful
    /// degradation of the preserved fail-fast posture.
    /// </para>
    /// <para>
    /// NO RETRY IS PERFORMED HERE, ON ANY STATUS. Transient-fault resilience belongs to the typed
    /// clients registered in <c>Program.cs</c>, and an optimistic-concurrency conflict is a definitive
    /// answer rather than a transient fault - retrying one automatically is precisely the
    /// silent-overwrite failure mode this system forbids everywhere.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ProjectAsync<TService>(
        HttpContext httpContext,
        ProjectedOperation operation,
        Func<TService, ProjectionCallContext, Task<IResult>> project,
        Func<IServiceProvider, TService> resolve)
        where TService : class
    {
        TService service = resolve(httpContext.RequestServices);

        // DISPOSED ON EVERY PATH OUT, because one operation arms a linked cancellation source on this
        // context and a linked source holds a registration on the token it links to. The delegate is typed
        // to the CONCRETE context rather than to ServerCallContext so that the one operation which arms a
        // window can do so without a downcast; every other call site is unaffected, since the concrete
        // type IS a ServerCallContext and the projected methods take that.
        using ProjectionCallContext context = new(httpContext, operation.GrpcMethod);

        try
        {
            return await project(service, context).ConfigureAwait(false);
        }
        catch (RpcException failure)
        {
            return ProjectFailure(httpContext, failure);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }
    }

    /// <summary>
    /// Resolves a projected gRPC implementation from request services.
    /// </summary>
    /// <typeparam name="TService">The implementation type.</typeparam>
    /// <param name="services">The request's service provider.</param>
    /// <returns>The instance to invoke.</returns>
    /// <remarks>
    /// <para>
    /// THESE ARE EXACTLY THE RESOLUTION SEMANTICS THE gRPC HOSTING LAYER USES: the service is taken
    /// from the container when it is registered there and constructed from the container's registrations
    /// otherwise, which is what the default gRPC service activator does for every gRPC call. Matching
    /// it means a REST route can never be reachable when its gRPC twin is not, and that this projection
    /// imposes NO registration requirement the gRPC surface does not already impose. A composition root
    /// that has not registered what these implementations need therefore fails the same way on both
    /// transports, visibly, rather than one of them silently degrading.
    /// </para>
    /// <para>
    /// An instance per request, again matching the hosting layer. Neither projected implementation is
    /// disposable, so there is nothing to dispose and nothing pretending to.
    /// </para>
    /// </remarks>
    private static TService ResolveService<TService>(IServiceProvider services)
        where TService : class
        => ActivatorUtilities.GetServiceOrCreateInstance<TService>(services);

    /// <summary>
    /// Reads the request body as text.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="cancellationToken">Abandons the read when the caller goes away.</param>
    /// <returns>The body, or an empty string when none was sent.</returns>
    private static async Task<string> ReadRequestBodyAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(
            httpContext.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Binds a request body onto its protobuf message.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <param name="body">The body as read.</param>
    /// <param name="request">The bound message on success.</param>
    /// <param name="failure">The rejection to answer with on failure.</param>
    /// <returns><see langword="true"/> when the body bound.</returns>
    private static bool TryBindRequest<TRequest>(
        string body,
        [NotNullWhen(true)] out TRequest? request,
        out StatusProjection failure)
        where TRequest : class, IMessage, new()
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            request = null;
            failure = BindingRejection(MissingBodyDetail);

            return false;
        }

        try
        {
            request = RequestParser.Parse<TRequest>(body);
            failure = default;

            return true;
        }
        catch (InvalidJsonException)
        {
            // A body that is not JSON at all. TWO ARMS ARE REQUIRED, and that is a measured fact rather
            // than defensiveness: in Google.Protobuf 3.31.1 InvalidJsonException derives from
            // System.Exception DIRECTLY and NOT from InvalidProtocolBufferException, so the arm below
            // does not cover it and a single arm would let a malformed body escape as an unhandled
            // fault instead of the defined rejection. Verified by reflecting the base type.
            request = null;
            failure = BindingRejection(MalformedBodyDetail);

            return false;
        }
        catch (InvalidProtocolBufferException)
        {
            // A body that IS JSON but does not map onto the message - an unknown member, or a member
            // whose value has the wrong shape for its declared field.
            //
            // Neither arm carries the parser's own diagnostic into the response, and that is the point
            // of the fixed prose: the diagnostic quotes the offending body, so relaying it would echo
            // caller-supplied content back out of a service that must not echo payloads (C-F).
            request = null;
            failure = BindingRejection(MalformedBodyDetail);

            return false;
        }
    }

    /// <summary>
    /// Builds the projection for a body this service rejected before invoking anything.
    /// </summary>
    /// <param name="detail">The fixed prose for the rejection.</param>
    /// <returns>The projection.</returns>
    /// <remarks>
    /// <c>FromUpstream</c> is false, and that is a statement rather than a default: nothing was sent
    /// anywhere, so naming an upstream would misattribute the rejection.
    /// </remarks>
    private static StatusProjection BindingRejection(string detail) => new(
        StatusCodes.Status400BadRequest,
        RetCode.E_INVALID_ARGUMENT,
        detail,
        FromUpstream: false);

    /// <summary>
    /// Renders one response message as the canonical protobuf JSON mapping.
    /// </summary>
    /// <param name="response">The message the projected method returned.</param>
    /// <returns>A <c>200</c> carrying the rendered message.</returns>
    private static IResult Render(HttpContext httpContext, IMessage response)
    {
        // ============ THE IN-BAND STATUS DECIDES THE HTTP STATUS (F-06) ============================
        // A gRPC method can complete SUCCESSFULLY and still answer a failure: the transport says OK and
        // the message body says E_DB_ERROR, E_INVALID_ARGUMENT or E_BUSY. Rendering that as 200 because
        // no RpcException was thrown is the single most misleading thing this projection could do - an
        // HTTP client that branches on the status line, which is every HTTP client, would treat a
        // rejected update as an applied one. The exception path was already mapped comprehensively; this
        // is the other half, and it was missing.
        //
        // THE BODY IS UNCHANGED EITHER WAY. On a failure the same protobuf JSON is still what the caller
        // receives - the in-band code, the diagnostic text and any db_error travel exactly as the
        // contract declares them - so nothing is lost and no legacy diagnostic is rewritten (C-B). Only
        // the status LINE changes, which is the part an HTTP caller is entitled to read.
        // ==========================================================================================
        if (InBandStatus.TryProjectFailure(response, out StatusProjection failure))
        {
            return RenderInBandFailure(httpContext, response, failure);
        }

        return Results.Text(
            ResponseFormatter.Format(response),
            MediaTypeNames.Application.Json);
    }

    /// <summary>
    /// Renders a response whose in-band status reports a failure, under the mapped HTTP status.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="response">The response message, rendered unchanged as the problem's payload.</param>
    /// <param name="failure">The projection the in-band code mapped to.</param>
    /// <returns>A problem response carrying the mapped status and the original body.</returns>
    /// <remarks>
    /// THE ORIGINAL MESSAGE IS ATTACHED RATHER THAN SUMMARISED. A caller of a failed operation needs the
    /// contract's own answer - the numeric code, the diagnostic text, the <c>db_error</c> when one is
    /// present - and a problem body that only paraphrased it would force a caller to choose between the
    /// status line and the contract. The <c>sqlsyntax</c> field inside a relayed <c>db_error</c> is passed
    /// through as it arrived: it is redacted before it reaches this service, nothing here re-interpolates
    /// it, and it is never written into <c>detail</c> or into a log record (constraint C-F).
    /// </remarks>
    private static IResult RenderInBandFailure(
        HttpContext httpContext,
        IMessage response,
        StatusProjection failure)
    {
        ILogger logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory);

        // NUMERIC ONLY. The diagnostic text belongs to the caller; it can quote a statement fragment, and
        // this route is reached by clients whose logs this service does not own.
        logger.LogWarning(
            "A projected operation answered in-band failure {RetCode}, rendered as HTTP {HttpStatus} "
                + "for {Method} {Route}. Correlation {CorrelationId}.",
            failure.RetCode,
            failure.HttpStatus,
            httpContext.Request.Method,
            DescribeRoute(httpContext),
            LogSafeText.Render(ResolveCorrelationId(httpContext)));

        ProblemDetails problem = BuildProblem(httpContext, failure);

        problem.Extensions[InBandStatus.ResponseExtensionMember] =
            ToJsonNode(response);

        return TypedResults.Problem(problem);
    }

    /// <summary>
    /// Renders a projected server stream as the ordered collection the contract declares.
    /// </summary>
    /// <typeparam name="TResponse">The protobuf message each element carries.</typeparam>
    /// <param name="elements">The elements the stream produced, in the order it produced them.</param>
    /// <returns>A <c>200</c> carrying the elements as a JSON array.</returns>
    /// <remarks>
    /// A bare array, not an envelope: the protocol definition declares no envelope message for a
    /// stream, so wrapping one here would add a member the gRPC contract does not have and the two
    /// halves of the boundary would stop corresponding. An empty array is valid and means the method
    /// produced no element at all.
    /// </remarks>
    private static IResult RenderSequence<TResponse>(IReadOnlyList<TResponse> elements)
        where TResponse : class, IMessage, new()
        => new StreamedSequenceResult<TResponse>(elements);

    /// <summary>
    /// Writes a collected sequence to the response as one JSON array, element by element.
    /// </summary>
    /// <typeparam name="TResponse">The protobuf message each element carries.</typeparam>
    /// <param name="elements">The elements, in the order the stream produced them.</param>
    /// <remarks>
    /// <para>
    /// <b>ONE PASS OVER THE ELEMENTS AND NO SECOND FULL COPY OF THE PAYLOAD (F-15).</b> The previous
    /// implementation built the entire array into a <see cref="StringBuilder"/>, called
    /// <c>ToString</c> on it - a second complete copy - and handed that to a text result, which encoded a
    /// third. For a large streamed response that is three simultaneous representations of the same data
    /// where one is needed. Each element is now formatted and written straight to the response writer, so
    /// only the collected messages and one element's JSON are live at a time.
    /// </para>
    /// <para>
    /// <b>THE COLLECTED SEQUENCE IS STILL BUFFERED BEFORE ANYTHING IS WRITTEN, DELIBERATELY.</b> That is
    /// what makes a mid-stream failure produce a clean problem body rather than a half-written success:
    /// once bytes are on the wire under a 200, no status can be corrected. The bound in
    /// <c>RestProjectionOptions.MaxStreamedElements</c> is what keeps that buffering finite, and the
    /// collector refuses past it rather than truncating. This is a resource bound and not a performance
    /// claim - no performance objective is asserted anywhere (AAP 0.8.5).
    /// </para>
    /// <para>
    /// The brackets and separators are written as UTF-8 bytes rather than through a text writer because the
    /// element bodies are already encoded strings; mixing a text writer over the same stream would mean two
    /// encoders sharing one buffer.
    /// </para>
    /// </remarks>
    private sealed class StreamedSequenceResult<TResponse>(IReadOnlyList<TResponse> elements) : IResult
        where TResponse : class, IMessage, new()
    {
        private static readonly byte[] ArrayOpen = "["u8.ToArray();
        private static readonly byte[] ArrayClose = "]"u8.ToArray();
        private static readonly byte[] ElementSeparator = ","u8.ToArray();

        /// <inheritdoc/>
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;

            PipeWriter writer = httpContext.Response.BodyWriter;

            await writer.WriteAsync(ArrayOpen, httpContext.RequestAborted).ConfigureAwait(false);

            for (int index = 0; index < elements.Count; index++)
            {
                if (index > 0)
                {
                    await writer
                        .WriteAsync(ElementSeparator, httpContext.RequestAborted)
                        .ConfigureAwait(false);
                }

                // Encoded one element at a time, so the transient string is one element wide rather than
                // the whole array.
                byte[] encoded = Encoding.UTF8.GetBytes(ResponseFormatter.Format(elements[index]));

                await writer.WriteAsync(encoded, httpContext.RequestAborted).ConfigureAwait(false);
            }

            await writer.WriteAsync(ArrayClose, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }



    // ==============================================================================================
    //  THE STATUS MAP - THE SUBSTANTIVE PART OF THE PROJECTION, AND IT EXISTS ONCE
    // ==============================================================================================

    /// <summary>
    /// Translates one gRPC failure into its HTTP answer, attaching the rich-error payload the failure
    /// carried.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="failure">The failure the projected method raised.</param>
    /// <returns>The problem response.</returns>
    /// <remarks>
    /// <para>
    /// The rich-error trailer is decoded ONCE, here, and it decides two things: which of the two
    /// <c>409</c> bodies an <c>Aborted</c> receives, and the numeric <c>retCode</c> the body carries.
    /// The payload itself is attached UNCHANGED - the conflict as the <c>conflict</c> member, a
    /// database-layer detail as the <c>dbError</c> member - because a caller deciding between retrying
    /// and surfacing needs the current row state exactly as the database reported it, and a caller
    /// diagnosing a data-path failure needs the driver's own code.
    /// </para>
    /// <para>
    /// THE <c>sqlsyntax</c> FIELD OF A RELAYED <c>dbError</c> IS PASSED THROUGH AS IT ARRIVED. It is
    /// redacted before it reaches this service and carries placeholders rather than interpolated
    /// literals; nothing here reconstructs, enriches or re-interpolates it, no second redactor exists
    /// here, and it is never written to a log record or into <c>detail</c>.
    /// </para>
    /// <para>
    /// <b>The return type is deliberately the polymorphic <see cref="IResult"/>, and analyzer CA1859 is
    /// deliberately not acted on here or on <c>RejectRequest</c>.</b> The single shared translator is
    /// specified as an <c>RpcException</c>-to-<see cref="IResult"/> mapping, so <see cref="IResult"/> is
    /// the agreed shape rather than an incidental one, and every projected route is written against it.
    /// CA1859's rationale is performance, and no performance objective may be asserted or used to
    /// justify a design choice in this refactor - the repository publishes none. Narrowing the type
    /// would also couple all thirty-nine routes to one concrete result kind, so the mapper could no
    /// longer answer with a different one without a change that reaches every call site.
    /// </para>
    /// </remarks>
    private static IResult ProjectFailure(HttpContext httpContext, RpcException failure)
    {
        RichErrorTrailer? trailer = TryDecodeRichError(failure.Trailers);
        JsonNode? conflict = null;
        JsonNode? dbError = null;

        if (trailer is not null)
        {
            switch (trailer.DetailCase)
            {
                case RichErrorTrailer.DetailOneofCase.Conflict:
                    // Bound to its declared type on the way through rather than relayed as a bare
                    // message, so the contract commitment - the 409 carries `common.v1.ConflictDetail`
                    // and nothing else - is checked by the compiler rather than only asserted in prose.
                    ConflictDetail? conflictDetail = trailer.Conflict;

                    conflict = ToJsonNode(conflictDetail);
                    break;

                case RichErrorTrailer.DetailOneofCase.DbError:
                    // Bound for the same reason. NOTHING IS DONE TO IT BEYOND RENDERING: its
                    // `sqlsyntax` member arrives redacted from the service that owns the redaction, and
                    // it is neither reconstructed, enriched nor logged here.
                    DbError? databaseError = trailer.DbError;

                    dbError = ToJsonNode(databaseError);
                    break;

                case RichErrorTrailer.DetailOneofCase.None:
                default:
                    // A trailer carrying a return code and no payload is a real statement rather than a
                    // malformed one, so the code below still adopts the code. Nothing is fabricated.
                    break;
            }
        }

        StatusProjection projection = ProjectStatus(failure, trailer, conflict is not null);
        ProblemDetails problem = BuildProblem(httpContext, projection);

        if (conflict is not null)
        {
            problem.Extensions[ConflictExtensionMember] = conflict;
        }

        if (dbError is not null)
        {
            problem.Extensions[DbErrorExtensionMember] = dbError;
        }

        LogProjectedFailure(httpContext, projection, failure);

        return TypedResults.Problem(problem);
    }

    /// <summary>
    /// The single shared gRPC-status-to-HTTP-status mapping, used by every projected route.
    /// </summary>
    /// <param name="failure">The failure the projected method raised.</param>
    /// <param name="trailer">The decoded rich-error trailer, when the failure carried one.</param>
    /// <param name="hasConflictDetail">
    /// Whether a conflict payload decoded, which selects between the two <c>409</c> bodies.
    /// </param>
    /// <returns>The HTTP status, the legacy return code, the fixed prose and the problem identity.</returns>
    /// <remarks>
    /// <para>
    /// <b><see cref="StatusCode.Aborted"/> becomes <c>409</c>.</b> That is the canonical gRPC-to-HTTP
    /// conflict mapping and it is mandatory: the conflict is never swallowed, never retried, never
    /// collapsed into a generic <c>500</c>, and its detail is never dropped.
    /// </para>
    /// <para>
    /// The first arm is the case the published table cannot describe: <see cref="StatusCode.Unavailable"/>
    /// carrying a debug exception means NO gRPC RESPONSE ARRIVED AT ALL on the one outbound edge these
    /// operations traverse, which is <c>502</c> rather than <c>503</c>. That failure mode is one the
    /// decomposition itself creates - an in-process call cannot fail in transit and a network call can.
    /// </para>
    /// <para>
    /// Four statuses the published table does not name are mapped explicitly rather than left to the
    /// default arm, because C-03 and C-04 genuinely produce them:
    /// <see cref="StatusCode.FailedPrecondition"/> for an unknown session, a transaction the upstream will
    /// not accept or an ordering violation under strict ordering - but NOT for a handle naming nothing,
    /// which is <see cref="StatusCode.NotFound"/> on every one of the estate's status maps and therefore
    /// takes the published <c>404</c> row above; <see cref="StatusCode.OutOfRange"/> for an ordinal
    /// outside its range; <see cref="StatusCode.AlreadyExists"/>, which shares <c>409</c> and is
    /// EMPHATICALLY NOT a concurrency conflict, so it carries its own problem type and no
    /// <c>conflict</c> member; and <see cref="StatusCode.ResourceExhausted"/>.
    /// </para>
    /// <para>
    /// <b>THE HTTP STATUS IS NEVER DERIVED FROM A SUCCESS PREDICATE.</b> The preserved algebra is
    /// tri-state - a prevention satisfies the success predicate, and a cancellation and a null satisfy
    /// neither predicate - so asking "did it succeed?" would turn a prevention into a <c>200</c>. Every
    /// status here is decided from the gRPC status code alone.
    /// </para>
    /// <para>
    /// <b>The return code is surfaced, not inferred.</b> When the trailer carried one it travels
    /// VERBATIM, which is how the originating legacy validation stays identifiable rather than only the
    /// HTTP class. A zero is not adopted, because zero is <c>OK</c> and a body asserting success on a
    /// failure would be worse than the defined per-status code; that is a presence test on a field, not
    /// a success predicate, so <c>PREVENT</c> (1) and <c>CANCELLED</c> (-2) are both adopted unchanged
    /// and a null trailer is never coerced to zero.
    /// </para>
    /// </remarks>
    private static StatusProjection ProjectStatus(
        RpcException failure,
        RichErrorTrailer? trailer,
        bool hasConflictDetail)
    {
        StatusProjection projection = MapStatusCode(failure, hasConflictDetail);

        return trailer is not null && trailer.RetCode != RetCode.OK
            ? projection with { RetCode = trailer.RetCode }
            : projection;
    }

    /// <summary>
    /// The status table itself, kept separate so it is one expression a theory can drive end to end.
    /// </summary>
    /// <param name="failure">The failure the projected method raised.</param>
    /// <param name="hasConflictDetail">Whether a conflict payload decoded.</param>
    /// <returns>The projection for that status.</returns>
    private static StatusProjection MapStatusCode(RpcException failure, bool hasConflictDetail)
    {
        if (failure.StatusCode == StatusCode.Unavailable && failure.Status.DebugException is not null)
        {
            return new(
                StatusCodes.Status502BadGateway,
                RetCode.E_RETRY,
                UpstreamUnavailableDetail,
                FromUpstream: true);
        }

        // ADJUDICATION A2 EXTENDED TO `Internal`, AND THIS IS THE HALF THAT WAS MISSING.
        //
        // `Internal` wears the same two events as `Unavailable` did, and only one of them was being told
        // apart. A stalled TLS or HTTP/2 HANDSHAKE - an upstream process that is running but not reading
        // its socket - is reported by Grpc.Net as `Internal`, not `Unavailable`, because the failure
        // happened while the connection was still being established rather than after it was refused.
        // That produced HTTP 500 with E_INTERNAL_ERROR for a call THAT NEVER REACHED THE UPSTREAM: a 500
        // says "this service faulted", sends an operator to the wrong service's logs, and tells a caller
        // nothing is worth retrying. It is the same misclassification A2 exists to prevent, so it gets
        // the same test: a status the CLIENT synthesized from a failed transport carries a transport
        // exception, and a status the SERVER genuinely answered does not.
        //
        // A SERVER-ANSWERED `Internal` STILL MAPS TO 500, unchanged. That one really is an upstream fault
        // with an upstream diagnosis, and its arm below is where the statement-redaction contract lives.
        if (failure.StatusCode == StatusCode.Internal && failure.Status.DebugException is not null)
        {
            return new(
                StatusCodes.Status502BadGateway,
                RetCode.E_RETRY,
                UpstreamUnavailableDetail,
                FromUpstream: true);
        }

        return failure.StatusCode switch
        {
            StatusCode.InvalidArgument => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_INVALID_ARGUMENT,
                InvalidArgumentDetail,
                FromUpstream: false),

            StatusCode.Unauthenticated => new(
                StatusCodes.Status401Unauthorized,
                RetCode.E_ACCESS_DENIED,
                UnauthenticatedDetail,
                FromUpstream: false),

            StatusCode.PermissionDenied => new(
                StatusCodes.Status403Forbidden,
                RetCode.E_ACCESS_DENIED,
                PermissionDeniedDetail,
                FromUpstream: false),

            StatusCode.NotFound => new(
                StatusCodes.Status404NotFound,
                RetCode.E_OBJECT_NOT_FOUND,
                NotFoundDetail,
                FromUpstream: false),

            // The mandatory arm. Two bodies, one status: the conflict is reported either way, because
            // reporting anything else would let a rejected update appear to have succeeded, and no
            // conflict member is fabricated when none arrived, because an empty one would describe a
            // conflict no caller could act on.
            StatusCode.Aborted => hasConflictDetail
                ? new(
                    StatusCodes.Status409Conflict,
                    RetCode.E_RETRY,
                    ConflictDetailProse,
                    FromUpstream: false,
                    ConflictProblemType,
                    ConflictProblemTitle)
                : new(
                    StatusCodes.Status409Conflict,
                    RetCode.E_RETRY,
                    ConflictWithoutDetailProse,
                    FromUpstream: false,
                    ConflictWithoutDetailProblemType,
                    ConflictWithoutDetailProblemTitle),

            StatusCode.Unimplemented => new(
                StatusCodes.Status501NotImplemented,
                RetCode.E_NO_IMPLEMENTATION,
                UnimplementedDetail,
                FromUpstream: false),

            // An ANSWERED unavailable, as opposed to the transport arm above. Every route to this arm is
            // ABOUT the single outbound edge, which is the evidence that lets the body name that upstream:
            // either Persistence answered it, or this service answered it BECAUSE Persistence would not
            // issue the work handles an operation needs - see DataWindowService.AcquisitionFailure, whose
            // default arm is this status precisely because the operation could not be served now and a
            // retrieval is a safe read.
            StatusCode.Unavailable => new(
                StatusCodes.Status503ServiceUnavailable,
                RetCode.E_RETRY,
                UnavailableDetail,
                FromUpstream: true),

            StatusCode.DeadlineExceeded => new(
                StatusCodes.Status504GatewayTimeout,
                RetCode.E_TIME_OUT,
                DeadlineExceededDetail,
                FromUpstream: false),

            StatusCode.Internal => new(
                StatusCodes.Status500InternalServerError,
                RetCode.E_INTERNAL_ERROR,
                InternalErrorDetail,
                FromUpstream: false),

            StatusCode.Unknown => new(
                StatusCodes.Status500InternalServerError,
                RetCode.UNKNOWN,
                InternalErrorDetail,
                FromUpstream: false),

            StatusCode.FailedPrecondition => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_INVALID_ARGUMENT,
                FailedPreconditionDetail,
                FromUpstream: false),

            StatusCode.OutOfRange => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_OUT_OF_RANGE,
                OutOfRangeDetail,
                FromUpstream: false),

            StatusCode.AlreadyExists => new(
                StatusCodes.Status409Conflict,
                RetCode.E_INVALID_ARGUMENT,
                AlreadyExistsDetail,
                FromUpstream: false,
                AlreadyExistsProblemType,
                AlreadyExistsProblemTitle),

            StatusCode.ResourceExhausted => new(
                StatusCodes.Status429TooManyRequests,
                RetCode.E_BUSY,
                ResourceExhaustedDetail,
                FromUpstream: false),

            StatusCode.Cancelled => new(
                StatusCodes.Status502BadGateway,
                RetCode.CANCELLED,
                CancelledDetail,
                FromUpstream: false),

            StatusCode.DataLoss => new(
                StatusCodes.Status500InternalServerError,
                RetCode.E_DB_ERROR,
                DataLossDetail,
                FromUpstream: false),

            // NOT a silent default. Every status gRPC defines and this contract can produce is named
            // above; anything else is genuinely unclassified, and the correlation identifier in the body
            // is what locates the operator record for it.
            _ => new(
                StatusCodes.Status500InternalServerError,
                RetCode.UNKNOWN,
                UnclassifiedFailureDetail,
                FromUpstream: false),
        };
    }

    /// <summary>
    /// Decodes the rich-error trailer from a failure's trailers, when one is present and decodable.
    /// </summary>
    /// <param name="trailers">The failure's trailers.</param>
    /// <returns>The decoded trailer, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The keys come from the service descriptors rather than from a literal, so the spelling cannot
    /// fall out of step with the contract. A trailer that will not parse yields <see langword="null"/>
    /// rather than an exception: the status is the load-bearing part and must still reach the caller, and
    /// a caller receiving <c>409</c> with no conflict member learns exactly what happened - the update
    /// was rejected and no detail could be decoded.
    /// </remarks>
    private static RichErrorTrailer? TryDecodeRichError(Metadata? trailers)
    {
        if (trailers is null || RichErrorTrailerKeys.Length == 0)
        {
            return null;
        }

        foreach (Metadata.Entry entry in trailers)
        {
            if (!entry.IsBinary || !IsRichErrorKey(entry.Key))
            {
                continue;
            }

            try
            {
                return RichErrorTrailer.Parser.ParseFrom(entry.ValueBytes);
            }
            catch (InvalidProtocolBufferException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Tests one trailer key against the descriptor-declared rich-error keys.
    /// </summary>
    /// <param name="key">The key as the transport reported it.</param>
    /// <returns><see langword="true"/> when it is a declared rich-error key.</returns>
    /// <remarks>
    /// Compared case-insensitively because metadata keys are normalized to lower case in transit while
    /// the descriptor carries whatever the contract declared.
    /// </remarks>
    private static bool IsRichErrorKey(string key)
    {
        foreach (string declared in RichErrorTrailerKeys)
        {
            if (string.Equals(key, declared, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renders one relayed payload message for embedding in a problem body.
    /// </summary>
    /// <param name="payload">The message to render, which may be <see langword="null"/>.</param>
    /// <returns>The rendered node, or <see langword="null"/> when there is nothing to embed.</returns>
    /// <remarks>
    /// Rendered through the SAME formatter every success body uses, so an embedded payload and a
    /// top-level one are byte-identical for the same message. Nothing is reshaped, renamed, filtered or
    /// enriched on the way through.
    /// </remarks>
    private static JsonNode? ToJsonNode(IMessage? payload) => payload is null
        ? null
        : JsonNode.Parse(ResponseFormatter.Format(payload));

    /// <summary>
    /// Answers a request this service rejected before invoking anything.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The rejection.</param>
    /// <returns>The problem response.</returns>
    private static IResult RejectRequest(HttpContext httpContext, StatusProjection projection)
    {
        ILogger logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory);

        logger.LogWarning(
            BindingFailureLogMessage,
            projection.HttpStatus,
            projection.RetCode,
            httpContext.Request.Method,
            DescribeRoute(httpContext),
            LogSafeText.Render(ResolveCorrelationId(httpContext)));

        return TypedResults.Problem(BuildProblem(httpContext, projection));
    }

    /// <summary>
    /// Builds the problem body for a projection.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The projection to render.</param>
    /// <returns>The problem body, before any payload member is attached.</returns>
    /// <remarks>
    /// <c>detail</c> is always the projection's FIXED prose. No gRPC status message, exception message,
    /// parser diagnostic, header, request body or response body reaches it, because all of those are
    /// caller or upstream content and any of them may quote a credential, a path or an interpolated SQL
    /// literal. The redaction is defensible because <c>traceId</c> redirects to the operator record for
    /// the same occurrence.
    /// </remarks>
    private static ProblemDetails BuildProblem(HttpContext httpContext, StatusProjection projection)
    {
        ProblemDetails problem = new()
        {
            Type = projection.Type ?? DefaultProblemType,
            Title = projection.Title ?? ReasonPhrases.GetReasonPhrase(projection.HttpStatus),
            Status = projection.HttpStatus,
            Detail = projection.Detail,
            Instance = httpContext.Request.Path.HasValue ? httpContext.Request.Path.Value : null,
        };

        problem.Extensions[RetCodeExtensionMember] = projection.RetCode;

        if (projection.FromUpstream)
        {
            problem.Extensions[UpstreamExtensionMember] = PersistenceUpstream;
        }

        string correlationId = ResolveCorrelationId(httpContext);

        // The guard remains because a host that supplies no identifier at all would otherwise publish an
        // empty member, which advertises a bridge with no far side. This service ships no such
        // configuration, so in practice the member is always present.
        if (!string.IsNullOrEmpty(correlationId))
        {
            problem.Extensions[TraceIdExtensionMember] = correlationId;
        }

        return problem;
    }

    /// <summary>
    /// Writes the one allowlisted operator record for a projected failure.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The projection being answered with.</param>
    /// <param name="failure">The failure being reported.</param>
    /// <remarks>
    /// The exception OBJECT is deliberately not passed: the logging abstraction renders a passed
    /// exception through its own string conversion, which includes every message in the chain, and one
    /// of those messages can be a driver's own text. Only the exception TYPE name and the gRPC status
    /// NAME are recorded.
    /// </remarks>
    private static void LogProjectedFailure(
        HttpContext httpContext,
        StatusProjection projection,
        RpcException failure)
    {
        ILogger logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory);

        logger.LogWarning(
            ProjectedFailureLogMessage,
            projection.HttpStatus,
            projection.RetCode,
            httpContext.Request.Method,
            DescribeRoute(httpContext),
            failure.StatusCode.ToString(),
            failure.GetType().Name,
            LogSafeText.Render(ResolveCorrelationId(httpContext)));
    }

    /// <summary>
    /// Describes the matched route by its PATTERN rather than by the requested path.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The route pattern, or a fixed placeholder when none matched.</returns>
    /// <remarks>
    /// The pattern is this file's own text; a path can carry a session identifier a caller chose. Only
    /// the pattern is recorded.
    /// </remarks>
    private static string DescribeRoute(HttpContext httpContext)
    {
        string? pattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

        return string.IsNullOrEmpty(pattern) ? UnroutedRouteDescription : pattern;
    }

    /// <summary>
    /// Resolves the correlation identifier for this occurrence.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The ambient distributed-trace identifier when the request carried one, and the host's own
    /// per-request identifier otherwise.
    /// </returns>
    /// <remarks>
    /// The same value is written to the operator record and to the problem body, resolved once per use
    /// from the same two sources, so a caller quoting it to an operator will always find a record.
    /// </remarks>
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        string? activityId = Activity.Current?.Id;

        return string.IsNullOrEmpty(activityId) ? httpContext.TraceIdentifier ?? string.Empty : activityId;
    }



    // ==============================================================================================
    //  PUBLISHED-DOCUMENT METADATA
    //
    //  There is no authored OpenApi/dataservices.v1.yaml, and that omission is deliberate: this
    //  service's document is GENERATED from the metadata declared here, while gateway.v1.yaml is the
    //  authored public mirror and is authoritative for the projected operation list, the schemas and
    //  the status codes. So every operation states its own identity, its contract, the gRPC method it
    //  projects, both protobuf message names, its streaming shape and its bearer requirement - which
    //  is what makes the correspondence between the two halves checkable rather than asserted.
    // ==============================================================================================

    /// <summary>
    /// Writes one operation's published metadata into the generated document.
    /// </summary>
    /// <param name="openApiOperation">The operation being described.</param>
    /// <param name="context">The transformer context, exposing the document being built.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <param name="protoRequest">The fully-qualified request message name.</param>
    /// <param name="protoResponse">The fully-qualified response message name.</param>
    /// <returns>A completed task; the transformation is synchronous.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// A per-endpoint operation transformer is the sanctioned mechanism on .NET 10. The older
    /// per-endpoint OpenAPI configuration extension is deprecated on this toolchain and raises a
    /// deprecation diagnostic, which the repository-wide warnings-as-errors setting turns into a build
    /// failure, so it is not an option here even as a fallback.
    /// </remarks>
    private static Task ApplyContractMetadataAsync(
        OpenApiOperation openApiOperation,
        OpenApiOperationTransformerContext context,
        ProjectedOperation operation,
        string protoRequest,
        string protoResponse)
    {
        ArgumentNullException.ThrowIfNull(openApiOperation);
        ArgumentNullException.ThrowIfNull(context);

        // Stated explicitly as well as through the endpoint name. The two are different concepts that
        // happen to share a value here, and the authored contract pins the operation identifier.
        openApiOperation.OperationId = operation.OperationId;

        openApiOperation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
        openApiOperation.Extensions[ContractIdExtensionName] = new JsonNodeExtension(operation.ContractId);
        openApiOperation.Extensions[GrpcMethodExtensionName] = new JsonNodeExtension(operation.GrpcMethod);
        openApiOperation.Extensions[ProtoRequestExtensionName] = new JsonNodeExtension(protoRequest);
        openApiOperation.Extensions[ProtoResponseExtensionName] = new JsonNodeExtension(protoResponse);

        if (operation.ServerStreaming)
        {
            openApiOperation.Extensions[GrpcStreamingExtensionName] =
                new JsonNodeExtension(ServerStreamingValue);
        }

        DescribeResponses(openApiOperation, operation);
        DescribeSessionIdParameter(openApiOperation);
        ApplyBearerSecurityRequirement(openApiOperation, context.Document);

        if (operation.DeclaresConflict)
        {
            PruneGeneratedMessageSchemas(context.Document);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Carries the authored contract's response prose onto the generated document.
    /// </summary>
    /// <param name="openApiOperation">The operation being described.</param>
    /// <param name="operation">The operation's published metadata.</param>
    /// <remarks>
    /// Each description is applied only where the operation actually declared that response, so this
    /// method needs no per-operation status list of its own and cannot advertise a status the route
    /// cannot produce.
    /// </remarks>
    private static void DescribeResponses(OpenApiOperation openApiOperation, ProjectedOperation operation)
    {
        Describe(StatusCodes.Status200OK, operation.SuccessDescription ?? ProjectedSuccessDescription);

        Describe(
            StatusCodes.Status400BadRequest,
            operation.BadRequest == BadRequestDeclaration.CrossSessionReferenceBlocked
                ? CrossSessionReferenceBlockedDescription
                : BadRequestDescription);

        Describe(StatusCodes.Status401Unauthorized, UnauthorizedDescription);
        Describe(StatusCodes.Status403Forbidden, ForbiddenDescription);
        Describe(StatusCodes.Status404NotFound, NotFoundDescription);
        Describe(StatusCodes.Status409Conflict, ConflictDescription);
        Describe(StatusCodes.Status500InternalServerError, InternalErrorDescription);
        Describe(StatusCodes.Status502BadGateway, UpstreamUnavailableDescription);

        void Describe(int statusCode, string description)
        {
            string key = statusCode.ToString(CultureInfo.InvariantCulture);

            if (openApiOperation.Responses is not null
                && openApiOperation.Responses.TryGetValue(key, out IOpenApiResponse? response)
                && response is OpenApiResponse concrete)
            {
                concrete.Description = description;
            }
        }
    }

    /// <summary>
    /// Carries the authored contract's description onto the session-identifier parameter, wherever the
    /// framework bound it from.
    /// </summary>
    /// <param name="openApiOperation">The operation being described.</param>
    private static void DescribeSessionIdParameter(OpenApiOperation openApiOperation)
    {
        if (openApiOperation.Parameters is null)
        {
            return;
        }

        foreach (IOpenApiParameter parameter in openApiOperation.Parameters)
        {
            if (string.Equals(parameter.Name, SessionIdParameter, StringComparison.Ordinal)
                && parameter is OpenApiParameter concrete)
            {
                concrete.Description = SessionIdParameterDescription;
            }
        }
    }

    /// <summary>
    /// Declares the bearer requirement on one operation, registering the scheme when the document does
    /// not already carry it.
    /// </summary>
    /// <param name="openApiOperation">The operation being described.</param>
    /// <param name="document">The document being built, when the transformer exposes one.</param>
    /// <remarks>
    /// The generator does not synthesise a security requirement from authorization metadata by itself,
    /// so requiring a token without declaring it would leave the published document claiming an
    /// anonymous operation while the running service answers <c>401</c>. No key material, key-set
    /// address or discovery address appears in the declaration: it states the transport scheme and the
    /// token format, which is all a consumer needs in order to point its own stock bearer handler at
    /// the issuer named in that consumer's configuration.
    /// </remarks>
    private static void ApplyBearerSecurityRequirement(
        OpenApiOperation openApiOperation,
        OpenApiDocument? document)
    {
        if (document is not null)
        {
            EnsureBearerSecurityScheme(document);
        }

        OpenApiSecurityRequirement requirement = new()
        {
            [new OpenApiSecuritySchemeReference(BearerSecuritySchemeId, document)] = [],
        };

        openApiOperation.Security ??= [];
        openApiOperation.Security.Add(requirement);
    }

    /// <summary>
    /// Registers the contract's bearer security scheme on the document, once.
    /// </summary>
    /// <param name="document">The document being built.</param>
    /// <remarks>
    /// Idempotent, so this composes with whatever document-wide security the host registers instead of
    /// fighting it, and so forty operations describe ONE scheme rather than forty.
    /// </remarks>
    private static void EnsureBearerSecurityScheme(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        if (document.Components.SecuritySchemes.ContainsKey(BearerSecuritySchemeId))
        {
            return;
        }

        document.Components.SecuritySchemes[BearerSecuritySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = BearerSchemeName,
            BearerFormat = BearerCredentialFormat,
        };
    }


    // ==============================================================================================
    //  THE IN-PROCESS INVOCATION SEAM
    //
    //  A projection of THIS service's own gRPC surface invokes the implementations directly rather than
    //  looping back over a socket, which is why a call context and a stream collector are needed. Both
    //  are mechanical plumbing: neither carries any DataWindow, expression or validation behaviour.
    // ==============================================================================================

    /// <summary>
    /// The <see cref="ServerCallContext"/> a projected in-process invocation runs under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED RATHER THAN ASSUMED: across both projected services the only context member the
    /// projected methods read is <see cref="ServerCallContext.CancellationToken"/>. The only two reads
    /// of <see cref="ServerCallContext.RequestHeaders"/> in either file are inside
    /// <c>InvokeMethodChannel</c>, which has no REST projection, so an EMPTY header collection is
    /// provably sufficient here.
    /// </para>
    /// <para>
    /// AND EMPTY IS ALSO THE SAFE CHOICE (constraint C-F). Copying the inbound HTTP headers across
    /// would move the caller's bearer credential into a structure this file does not control and cannot
    /// account for. The caller's identity is established by the ASP.NET Core authentication layer and
    /// lives on the request's own principal; nothing needs it restated as gRPC metadata.
    /// </para>
    /// <para>
    /// For the same reason the auth context carries no peer identity property and no claim: inventing
    /// one would assert a peer identity that nothing on this transport verified.
    /// </para>
    /// <para>
    /// The cancellation token is the request's own, so a caller that goes away cancels the projected
    /// method exactly as a cancelled gRPC call would. There is no deadline, because HTTP carries no gRPC
    /// deadline header and inventing one would impose a limit no contract states.
    /// </para>
    /// <para>
    /// 🔴 <b>ONE OPERATION ARMS A FINITE COLLECTION WINDOW ON TOP OF THAT, AND IT IS THE ONE WHOSE
    /// UPSTREAM NEVER ENDS.</b> See <see cref="StartCollectionWindow"/>: the expression event stream is a
    /// subscription, so a projection of it that waited for the stream to complete waited forever. The
    /// window is a token LINKED to the request's own rather than a timer around an await, which is what
    /// makes expiry end the projected method - and release its subscription - instead of abandoning an
    /// await while the method keeps running.
    /// </para>
    /// </remarks>
    private sealed class ProjectionCallContext : ServerCallContext, IDisposable
    {
        /// <summary>The placeholder reported when the transport exposes no remote address.</summary>
        private const string UnknownPeer = "unknown";

        private readonly HttpContext _httpContext;
        private readonly string _method;

        /// <summary>The armed collection window, or <see langword="null"/> when none was armed.</summary>
        private CancellationTokenSource? _window;

        /// <summary>
        /// Initializes a context for one projected invocation.
        /// </summary>
        /// <param name="httpContext">The request being projected.</param>
        /// <param name="grpcMethod">
        /// The fully-qualified gRPC method the operation projects, as the published metadata spells it.
        /// </param>
        internal ProjectionCallContext(HttpContext httpContext, string grpcMethod)
        {
            _httpContext = httpContext;

            // gRPC spells a method with a leading slash. The published metadata does not, because the
            // `x-grpc-method` extension names the method rather than a path, so the slash is added here
            // instead of being carried twice.
            _method = grpcMethod.StartsWith('/')
                ? grpcMethod
                : string.Concat("/", grpcMethod);
        }

        /// <inheritdoc/>
        protected override string MethodCore => _method;

        /// <inheritdoc/>
        protected override string HostCore => _httpContext.Request.Host.Value ?? string.Empty;

        /// <inheritdoc/>
        protected override string PeerCore => DescribePeer();

        /// <inheritdoc/>
        /// <remarks>
        /// No deadline. HTTP carries no gRPC deadline header, and the request's own cancellation is what
        /// carries a caller's abandonment.
        /// </remarks>
        protected override DateTime DeadlineCore => DateTime.MaxValue;

        /// <inheritdoc/>
        protected override Metadata RequestHeadersCore { get; } = [];

        /// <inheritdoc/>
        /// <remarks>
        /// THE WINDOW WHEN ONE IS ARMED, THE REQUEST'S OWN OTHERWISE - and the armed token is LINKED to
        /// the request's, so a caller that goes away still cancels the projected method. The projected
        /// method cannot tell the two apart and does not need to: ending the stream is the correct
        /// response to either. <see cref="WindowExpired"/> is how the PROJECTION tells them apart, which
        /// it must, because one answers the collected sequence and the other answers nothing at all.
        /// </remarks>
        protected override CancellationToken CancellationTokenCore =>
            _window?.Token ?? _httpContext.RequestAborted;

        /// <summary>
        /// Whether the collection window - and not the caller's own abandonment - ended the invocation.
        /// </summary>
        /// <remarks>
        /// <b>THE CALLER'S ABORT MUST NOT READ AS A COMPLETED COLLECTION, WHICH IS WHAT THE SECOND
        /// CONDITION IS FOR.</b> The window is linked to the request, so a caller that hangs up cancels it
        /// too - and treating that as a complete answer would render a 200 for a request nobody is waiting
        /// for while hiding the abort from the shared failure path. Testing the request's own token second
        /// is what keeps the two apart.
        /// </remarks>
        internal bool WindowExpired =>
            _window is { IsCancellationRequested: true }
            && !_httpContext.RequestAborted.IsCancellationRequested;

        /// <summary>
        /// Arms a finite collection window over the invocation this context serves.
        /// </summary>
        /// <param name="budget">How long the projection collects before answering with what it has.</param>
        /// <remarks>
        /// <para>
        /// CALLED AFTER THE REQUEST BODY HAS BEEN READ AND BOUND, DELIBERATELY. The window bounds the
        /// SUBSCRIPTION, not the request parse: arming it earlier would let a slow upload consume the
        /// budget and answer an empty collection for a request that had not started collecting yet.
        /// </para>
        /// <para>
        /// A LINKED SOURCE RATHER THAN A TIMEOUT AROUND THE AWAIT. The projected method observes
        /// <see cref="ServerCallContext.CancellationToken"/>, so expiry ends its own read loop and
        /// disposes its relay subscription; a timer around the await would leave the method running and
        /// the subscription registered with nobody to consume it.
        /// </para>
        /// </remarks>
        internal void StartCollectionWindow(TimeSpan budget)
        {
            _window = CancellationTokenSource.CreateLinkedTokenSource(_httpContext.RequestAborted);
            _window.CancelAfter(budget);
        }

        /// <summary>Releases the collection window, if one was armed.</summary>
        /// <remarks>
        /// A LINKED SOURCE REGISTERS A CALLBACK ON THE TOKEN IT LINKS TO, so leaving it undisposed holds a
        /// registration on the request's own token for the lifetime of that request. The projection
        /// disposes this context on every path out, including the failure paths.
        /// </remarks>
        public void Dispose()
        {
            _window?.Dispose();
            _window = null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Writable and observed: a projected method may set a trailer, and the rich-error decode reads
        /// trailers from the RAISED failure rather than from here, so nothing is lost by not forwarding
        /// these onto the HTTP response - where gRPC metadata has no defined meaning anyway.
        /// </remarks>
        protected override Metadata ResponseTrailersCore { get; } = [];

        /// <inheritdoc/>
        protected override Status StatusCore { get; set; }

        /// <inheritdoc/>
        protected override WriteOptions? WriteOptionsCore { get; set; }

        /// <inheritdoc/>
        protected override AuthContext AuthContextCore { get; } =
            new(null, new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

        /// <inheritdoc/>
        /// <remarks>
        /// A DEFINED REFUSAL, NOT A PLACEHOLDER. A propagation token exists to carry a deadline and a
        /// cancellation from an inbound gRPC call into an outbound one, and this invocation arrived over
        /// HTTP with neither a gRPC deadline nor a gRPC call to propagate from. Neither projected service
        /// requests one - verified across both implementations - so this cannot be reached; refusing
        /// explicitly is honest, whereas returning a fabricated token would hand a caller a deadline
        /// nothing set.
        /// </remarks>
        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) =>
            throw new NotSupportedException(
                "Context propagation is not available on the REST projection: the invocation arrived "
                + "over HTTP, so there is no inbound gRPC call and no gRPC deadline to propagate. The "
                + "request's own cancellation token is carried instead.");

        /// <inheritdoc/>
        /// <remarks>
        /// Accepted and not forwarded. gRPC response headers have no meaning on a projected HTTP
        /// response, and copying them onto it would publish upstream metadata a consumer has no contract
        /// for. Neither projected method writes any.
        /// </remarks>
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;

        /// <summary>
        /// Describes the remote endpoint in the shape gRPC uses.
        /// </summary>
        /// <returns>The peer description, or a fixed placeholder when the transport exposes none.</returns>
        private string DescribePeer()
        {
            System.Net.IPAddress? address = _httpContext.Connection.RemoteIpAddress;

            return address is null
                ? UnknownPeer
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{address}:{_httpContext.Connection.RemotePort}");
        }
    }

    /// <summary>
    /// Collects everything a projected server-streaming method writes, in the order it writes it.
    /// </summary>
    /// <typeparam name="TResponse">The protobuf message each element carries.</typeparam>
    /// <remarks>
    /// IT DELIBERATELY IMPLEMENTS ONLY THE SINGLE-ARGUMENT <c>WriteAsync</c>, exactly as the production
    /// ASP.NET Core stream writer does, so a projected method that wrongly forwarded a cancellation
    /// token to a write would fail here as it fails on the gRPC transport rather than silently
    /// succeeding on one and not the other.
    /// </remarks>
    /// <remarks>
    /// <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE BOUND IS TESTABLE.</b>
    /// The property that matters - refuse rather than truncate, at exactly the configured element - is not
    /// observable from outside without provoking an upstream into producing more elements than the bound,
    /// which no test can arrange against a real service. Only this service's own test assembly sees it.
    /// </remarks>
    internal sealed class CollectingStreamWriter<TResponse> : IServerStreamWriter<TResponse>
        where TResponse : class, IMessage, new()
    {
        private readonly List<TResponse> _written = [];

        private readonly int _maximumElements;

        /// <summary>Initializes the collector with the bound it will refuse beyond.</summary>
        /// <param name="maximumElements">The configured element bound. At least one.</param>
        internal CollectingStreamWriter(int maximumElements) => _maximumElements = maximumElements;

        /// <inheritdoc/>
        public WriteOptions? WriteOptions { get; set; }

        /// <summary>The elements written so far, in write order.</summary>
        internal IReadOnlyList<TResponse> Written => _written;

        /// <inheritdoc/>
        /// <exception cref="StreamedResponseTooLargeException">
        /// The producer wrote more elements than the configured bound permits.
        /// </exception>
        /// <remarks>
        /// <b>REFUSED, NEVER TRUNCATED.</b> A truncated JSON array is indistinguishable from a complete
        /// one, so dropping the tail would answer a retrieval with the WRONG answer under a success
        /// status - the worst of the three available outcomes. Throwing at the moment the bound is
        /// crossed also stops the producer, so nothing further is allocated after the refusal.
        /// </remarks>
        public Task WriteAsync(TResponse message)
        {
            if (_written.Count >= _maximumElements)
            {
                throw new StreamedResponseTooLargeException(_maximumElements);
            }

            _written.Add(message);

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Raised when a projected server stream produced more elements than the configured bound permits.
    /// </summary>
    /// <remarks>
    /// A private exception type rather than a generic one, so the handler below can tell this refusal - a
    /// bound this service imposed and knows the value of - apart from every other fault, and answer it
    /// with a status that says so.
    /// </remarks>
    internal sealed class StreamedResponseTooLargeException : Exception
    {
        /// <summary>Initializes the exception.</summary>
        /// <param name="limit">The bound that was exceeded.</param>
        internal StreamedResponseTooLargeException(int limit)
            : base("A projected server stream exceeded the configured element bound.") => Limit = limit;

        /// <summary>The configured bound that was exceeded.</summary>
        internal int Limit { get; }
    }


    // ==============================================================================================
    //  THE PROJECTION TABLE'S OWN TYPES
    // ==============================================================================================

    /// <summary>Which of the two projected contracts an operation belongs to.</summary>
    private enum ContractSurface
    {
        /// <summary>Contract C-03, <c>dataservices.v1.DataWindowService</c>.</summary>
        DataWindow,

        /// <summary>Contract C-04, <c>dataservices.v1.ColumnExpressionService</c>.</summary>
        ColumnExpression,
    }

    /// <summary>How an operation declares its <c>400</c>, when it declares one at all.</summary>
    private enum BadRequestDeclaration
    {
        /// <summary>The shared description.</summary>
        Standard,

        /// <summary>No <c>400</c> is declared, because the operation binds no body.</summary>
        None,

        /// <summary>
        /// The variant recording that a cross-session foreign-variable reference is BLOCKED, which is
        /// the refactor's one deliberately narrowed contract.
        /// </summary>
        CrossSessionReferenceBlocked,
    }

    /// <summary>One projected operation's published metadata.</summary>
    /// <param name="Route">The route, relative to the group it is declared on.</param>
    /// <param name="OperationId">The operation identifier the authored contract pins.</param>
    /// <param name="RpcName">The gRPC method name this operation projects.</param>
    /// <param name="Surface">Which contract the operation belongs to.</param>
    /// <param name="Summary">The one-line summary.</param>
    /// <param name="Description">The prose, carried across from the authored contract.</param>
    /// <param name="BadRequest">How the operation declares its <c>400</c>.</param>
    /// <param name="DeclaresNotFound">Whether the operation can answer <c>404</c>.</param>
    /// <param name="DeclaresConflict">Whether the operation can answer <c>409</c>.</param>
    /// <param name="ServerStreaming">Whether the projected method is a server stream.</param>
    /// <param name="SuccessDescription">
    /// The <c>200</c> description where it differs from the shared one, which is the case for the two
    /// projected server streams.
    /// </param>
    private sealed record ProjectedOperation(
        string Route,
        string OperationId,
        string RpcName,
        ContractSurface Surface,
        string Summary,
        string Description,
        BadRequestDeclaration BadRequest = BadRequestDeclaration.Standard,
        bool DeclaresNotFound = true,
        bool DeclaresConflict = false,
        bool ServerStreaming = false,
        string? SuccessDescription = null)
    {
        /// <summary>The OpenAPI tag, derived from the surface so the two cannot disagree.</summary>
        internal string Tag => Surface == ContractSurface.DataWindow ? DataWindowTag : ColumnExpressionTag;

        /// <summary>The contract identifier, derived from the surface for the same reason.</summary>
        internal string ContractId => Surface == ContractSurface.DataWindow
            ? DataWindowContractId
            : ColumnExpressionContractId;

        /// <summary>
        /// The fully-qualified gRPC method, composed from the service descriptor's own name so that the
        /// published metadata cannot drift from the protocol definition.
        /// </summary>
        internal string GrpcMethod => string.Concat(
            Surface == ContractSurface.DataWindow ? DataWindowServiceName : ColumnExpressionServiceName,
            "/",
            RpcName);
    }

    /// <summary>One resolved HTTP answer: the status, the legacy code, the prose and the identity.</summary>
    /// <param name="HttpStatus">The HTTP status to answer with.</param>
    /// <param name="RetCode">
    /// The legacy PowerFramework return code to surface. Carried verbatim from the rich-error trailer
    /// when one arrived and otherwise the defined code for this status; never derived from a success
    /// predicate, and never zero on a failure.
    /// </param>
    /// <param name="Detail">The FIXED prose for the problem body's <c>detail</c> member.</param>
    /// <param name="FromUpstream">
    /// Whether the failure demonstrably originated on the single outbound edge these operations
    /// traverse, which is what licenses naming an upstream in the body.
    /// </param>
    /// <param name="Type">The problem type, where a more specific one than the default applies.</param>
    /// <param name="Title">The problem title, where a more specific one than the reason phrase applies.</param>
    // ==============================================================================================
    //  THE IN-BAND STATUS MAP - THE OTHER HALF OF THE PROJECTION (F-06)
    // ==============================================================================================

    /// <summary>
    /// Reads the in-band outcome a projected response carries and maps a failure onto its HTTP answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS EXISTS AT ALL.</b> The exception map above translates a gRPC method that FAILED. This
    /// one translates a gRPC method that SUCCEEDED and answered a failure in its body, which the contract
    /// does constantly: <c>OperationStatus.ret_code</c> and the bare <c>ret_code</c> fields carry
    /// <c>E_INVALID_ARGUMENT</c>, <c>E_BUSY</c>, <c>E_DB_ERROR</c> and the rest on a perfectly healthy
    /// transport. Without this, every one of those arrived as <c>200 OK</c>.
    /// </para>
    /// <para>
    /// <b>IT IS IMPLEMENTED HERE RATHER THAN SHARED WITH THE GATEWAY, AND THAT IS ARCHITECTURAL.</b> The
    /// gateway needs the same translation and has its own copy. Putting one in the contracts project would
    /// make it a shared-code back door - that project carries the boundary DEFINITION and no behaviour -
    /// and putting it in a shared library would be behaviour crossing a service boundary, which is exactly
    /// the coupling the decomposition forbids (AAP 0.7.2). Two implementations of a published mapping is
    /// the intended shape; what keeps them together is the contract, not a reference.
    /// </para>
    /// <para>
    /// <b>THE READ IS BY DESCRIPTOR, NOT BY TYPE SWITCH.</b> Well over a hundred response messages are
    /// projected, and a switch over them would be a list that silently stops covering new ones. Reading
    /// the descriptor covers every message that carries either shape and needs no maintenance when one is
    /// added.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE MAP ITSELF IS TESTABLE.</b>
    /// What can go wrong here is the MAPPING - a code sent to the wrong status, or a tri-state value
    /// misclassified as a failure - and neither is observable from the outside without a live upstream
    /// producing that exact code. Driving the projection directly tests the rule rather than one path
    /// through it. Only this service's own test assembly can see it; C-A forbids any other reaching in.
    /// </remarks>
    internal static class InBandStatus
    {
        /// <summary>The problem-body member the original response is attached under.</summary>
        internal const string ResponseExtensionMember = "response";

        /// <summary>The <c>OperationStatus</c>-shaped field name.</summary>
        private const string StatusFieldName = "status";

        /// <summary>The outcome field, both at message level and inside an <c>OperationStatus</c>.</summary>
        private const string RetCodeFieldName = "ret_code";

        /// <summary>The optional diagnostic beside an in-band outcome.</summary>
        private const string ErrorTextFieldName = "error_text";

        /// <summary>
        /// Projects a failing in-band outcome onto its HTTP answer.
        /// </summary>
        /// <param name="response">The response message the projected method returned.</param>
        /// <param name="failure">The projection, when the message reports a failure.</param>
        /// <returns>
        /// <see langword="true"/> when the message carries a failing outcome; otherwise
        /// <see langword="false"/>, and the response is rendered as an ordinary success.
        /// </returns>
        /// <remarks>
        /// <b>THE FAILURE TEST IS THE PORTED PREDICATE, NOT A TRUTHINESS TEST, AND THE TRI-STATE HOLE IS
        /// PRESERVED (constraint C-B).</b> The legacy algebra tests failure as strictly less than zero
        /// WITH CANCELLED EXPLICITLY EXCLUDED [<c>isfailed.srf:L11-L13</c>], so three outcomes are NOT
        /// failures here and each for its own reason: <c>OK</c> is zero, <c>PREVENT</c> is 1 and reads as a
        /// SUCCESS in that algebra, and <c>CANCELLED</c> is -2 and is neither succeeded nor failed. A
        /// prevention rendered as an HTTP failure, or a cancellation rendered as one, would both be this
        /// projection inventing a classification the contract does not make.
        /// </remarks>
        internal static bool TryProjectFailure(IMessage response, out StatusProjection failure)
        {
            failure = default;

            if (!TryReadOutcome(response, out long retCode, out string? errorText))
            {
                return false;
            }

            // Zero and every positive value pass, which keeps PREVENT a success. CANCELLED is excluded by
            // name, exactly as the ported predicate excludes it.
            if (retCode >= RetCode.OK || retCode == RetCode.CANCELLED)
            {
                return false;
            }

            failure = Project(retCode, errorText);

            return true;
        }

        /// <summary>
        /// Reads the outcome and its diagnostic from either in-band shape.
        /// </summary>
        /// <param name="response">The message to read.</param>
        /// <param name="retCode">The outcome, when one is present.</param>
        /// <param name="errorText">The diagnostic beside it, when one is present.</param>
        /// <returns><see langword="true"/> when the message carries an in-band outcome at all.</returns>
        /// <remarks>
        /// TWO SHAPES, AND THE NESTED ONE IS TRIED FIRST because a message carrying an
        /// <c>OperationStatus</c> is the richer form and is what the newer methods use. A message with
        /// neither - a pure data response - reports no outcome and is rendered as it always was.
        /// </remarks>
        private static bool TryReadOutcome(IMessage response, out long retCode, out string? errorText)
        {
            retCode = RetCode.OK;
            errorText = null;

            MessageDescriptor descriptor = response.Descriptor;

            if (descriptor.FindFieldByName(StatusFieldName) is { } statusField
                && statusField.FieldType == FieldType.Message
                && statusField.Accessor.GetValue(response) is IMessage status)
            {
                if (status.Descriptor.FindFieldByName(RetCodeFieldName) is not { } nestedCode)
                {
                    return false;
                }

                retCode = Convert.ToInt64(
                    nestedCode.Accessor.GetValue(status),
                    CultureInfo.InvariantCulture);

                errorText = status.Descriptor.FindFieldByName(ErrorTextFieldName)
                    ?.Accessor
                    .GetValue(status) as string;

                return true;
            }

            if (descriptor.FindFieldByName(RetCodeFieldName) is { } topLevelCode
                && topLevelCode.FieldType == FieldType.Enum)
            {
                retCode = Convert.ToInt64(
                    topLevelCode.Accessor.GetValue(response),
                    CultureInfo.InvariantCulture);

                errorText = descriptor.FindFieldByName(ErrorTextFieldName)
                    ?.Accessor
                    .GetValue(response) as string;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Maps one failing outcome onto its published HTTP status.
        /// </summary>
        /// <param name="retCode">The failing in-band outcome.</param>
        /// <param name="errorText">The contract's own diagnostic, or <see langword="null"/>.</param>
        /// <returns>The projection.</returns>
        /// <remarks>
        /// <para>
        /// THE MAPPING MIRRORS THE EXCEPTION MAP ABOVE so one condition cannot arrive under two different
        /// HTTP statuses depending on which channel reported it: a concurrency mismatch is <c>409</c>
        /// whether it came as <see cref="StatusCode.Aborted"/> or as <c>E_RETRY</c> in a body, a bad
        /// argument is <c>400</c> either way, and a busy resource is <c>429</c> either way.
        /// </para>
        /// <para>
        /// 🔴 <b>AND IT MIRRORS THE INGRESS'S MAP FOR THE SAME REFUSAL, WHICH IS A PUBLISHED PROPERTY
        /// RATHER THAN A COINCIDENCE.</b> The two surfaces are documented as equivalent, so the SAME
        /// refusal must carry the SAME status whether a caller reached it through the gateway or reached
        /// this projection directly. Six codes broke that: <c>E_INVALID_DATA</c> and
        /// <c>E_INVALID_DATAOBJECT</c> were 400 there and 500 here; <c>E_NOT_EXISTS</c>,
        /// <c>E_VAR_NOT_FOUND</c> and <c>E_MEMBER_NOT_FOUND</c> were 404 there and 500 here; and
        /// <c>FAILED</c> - the oracle's own unspecific failure, which the projected methods really answer -
        /// was 502 there and 500 here. Each is now an explicit arm, and each arm carries the reasoning that
        /// chose its status rather than only the status. A table-driven test in this service's suite and its
        /// twin in the gateway's pin the whole published mapping on both sides, deliberately duplicated
        /// rather than hoisted into the contracts project, because no behaviour crosses a service boundary
        /// in this system (constraint C-A) - the cross-reference between the two tests is what keeps them
        /// in step.
        /// </para>
        /// <para>
        /// THE PROSE IS NOT PART OF THAT EQUIVALENCE, AND IT SHOULD NOT BE. Each fallback sentence names
        /// the surface a caller is talking to, so the gateway's says "upstream" where this one does not -
        /// and either way the sentence is replaced by the contract's own diagnostic whenever one was
        /// supplied, which is the case that matters for behaviour preservation (constraint C-B). The STATUS
        /// is the contract; the sentence is the courtesy.
        /// </para>
        /// <para>
        /// THE DEFAULT IS <c>500</c> AND NOT <c>400</c>. An outcome this map does not recognise is a
        /// contract this projection has not been taught, which is a fault on this side of the boundary -
        /// blaming the caller for it would send a client into a retry-with-different-input loop that can
        /// never succeed.
        /// </para>
        /// <para>
        /// THE DIAGNOSTIC IS CARRIED WHEN THE CONTRACT SUPPLIED ONE. It is the legacy text and is relayed
        /// unchanged; the fixed prose below is used only when the contract left it empty, so nothing is
        /// paraphrased over the top of a real message (constraint C-B).
        /// </para>
        /// <para>
        /// 🔴 <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE PUBLISHED MAPPING
        /// IS PINNABLE AS A TABLE, AND THAT IS THE ONLY WAY THE EQUIVALENCE CLAIM CAN BE TESTED AT ALL.</b>
        /// Reaching this map through a deployed host exercises only the handful of outcomes a real
        /// operation can be provoked into answering - which is exactly how six codes came to diverge
        /// between the two published surfaces unnoticed. Calling it directly makes every arm assertable,
        /// including the arms no test can provoke, so the two services' tables can be compared row for row.
        /// It is visible to this service's own test assembly alone, through the
        /// <c>InternalsVisibleTo</c> item the project file already declares.
        /// </para>
        /// </remarks>
        internal static StatusProjection Project(long retCode, string? errorText)
        {
            (int HttpStatus, string Detail) mapped = retCode switch
            {
                RetCode.E_INVALID_ARGUMENT or RetCode.E_INVALID_SQL =>
                    (StatusCodes.Status400BadRequest, InBandInvalidArgumentDetail),

                RetCode.E_OUT_OF_RANGE or RetCode.E_OUT_OF_BOUND =>
                    (StatusCodes.Status400BadRequest, InBandOutOfRangeDetail),

                RetCode.E_ACCESS_DENIED => (StatusCodes.Status403Forbidden, InBandAccessDeniedDetail),

                // ⚠ E_INVALID_DATA JOINS THE ARGUMENT-REJECTION ARM, and it belongs there rather than in
                // the default. It is what the update path answers when the carrier it was handed cannot be
                // applied [n_cst_thread_task_sqlupdate.sru, the legacy diagnostic 无效的更新数据!] - the
                // caller's PAYLOAD is at fault, which is the definition of a 400. Falling to the default
                // answered 500, telling a caller that this service had failed and inviting it to retry an
                // identical request that can never succeed.
                RetCode.E_INVALID_DATA =>
                    (StatusCodes.Status400BadRequest, InBandInvalidDataDetail),

                // ⚠ E_INVALID_DATAOBJECT JOINS THE ARGUMENT-REJECTION ARM, AND IT IS 400 RATHER THAN 404
                // FOR A SPECIFIC REASON. It is answered when the DataWindow name a request carried
                // resolves to nothing - and the RETRIEVAL side answers the SAME mistake with the oracle's
                // own E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L554], which is already 400 here.
                // One caller mistake must not produce two different statuses depending on which verb was
                // used, so the update side is aligned to the retrieval side rather than to the handle
                // family below. 404 was considered and rejected: the name is a member of the request BODY,
                // not the request target, and 422 is closed to this surface by docs/CONTRACTS.md 12.1.
                RetCode.E_INVALID_DATAOBJECT =>
                    (StatusCodes.Status400BadRequest, InBandInvalidDataObjectDetail),

                // ⚠ E_NOT_EXISTS JOINS THE NOT-FOUND FAMILY for the same reason its two siblings are
                // already in it: the request named something that could not be found. A 500 here reported
                // a fault where the honest answer is that the named thing is not there.
                //
                // ⚠ AND SO DO E_VAR_NOT_FOUND AND E_MEMBER_NOT_FOUND, which are the column-expression
                // service's own not-found codes: E_VAR_NOT_FOUND is what it answers for a variable name no
                // global-variable table carries [n_cst_dwsvc_columnexp.sru, the of_GetVar family] and
                // E_MEMBER_NOT_FOUND for a member it cannot bind. Both are a caller naming something that
                // is not there - the identical situation to the three codes above - yet both fell to the
                // default and reported HTTP 500, which told the caller this service had failed and invited
                // it to retry a request that can never succeed.
                RetCode.E_INVALID_HANDLE
                    or RetCode.E_OBJECT_NOT_FOUND
                    or RetCode.E_NOT_EXISTS
                    or RetCode.E_VAR_NOT_FOUND
                    or RetCode.E_MEMBER_NOT_FOUND =>
                    (StatusCodes.Status404NotFound, InBandNotFoundDetail),

                RetCode.E_RETRY => (StatusCodes.Status409Conflict, InBandRetryDetail),

                RetCode.E_BUSY => (StatusCodes.Status429TooManyRequests, InBandBusyDetail),

                RetCode.E_TIME_OUT => (StatusCodes.Status504GatewayTimeout, InBandTimeoutDetail),

                RetCode.E_NO_SUPPORT or RetCode.E_NO_IMPLEMENTATION =>
                    (StatusCodes.Status501NotImplemented, InBandNotImplementedDetail),

                RetCode.E_DB_ERROR or RetCode.E_INVALID_TRANSACTION =>
                    (StatusCodes.Status502BadGateway, InBandDataPathDetail),

                // ⚠ THE GENERIC LEGACY FAILURE IS A DATA-PATH FAILURE, NOT A PROJECTION ONE.
                //
                // FAILED = -1 [retcode.sru] is the oracle's unspecific failure and the projected methods
                // really answer it - so it is a RECOGNISED outcome, and letting it fall to the default was
                // the one arm where the default's own reasoning did not hold: the default is 500 because an
                // UNRECOGNISED code is a contract this projection has not been taught, which is a fault on
                // this side. A code this projection recognises, reported by an operation that completed
                // normally, is the opposite situation. 502 is the status whose meaning is "the path behind
                // me failed", and it is what the ingress answers for this same code - so answering 500 here
                // was also the single largest divergence between the two published mappings.
                //
                // 422 WOULD HAVE BEEN THE INTUITIVE CHOICE AND IS FORBIDDEN. The status surface is closed
                // to the set docs/CONTRACTS.md 12.1 sanctions plus 502 and 503, and the contracts suite
                // asserts that 422 appears nowhere.
                RetCode.FAILED => (StatusCodes.Status502BadGateway, InBandGenericFailureDetail),

                _ => (StatusCodes.Status500InternalServerError, InBandUnclassifiedDetail),
            };

            return new StatusProjection(
                mapped.HttpStatus,
                retCode,
                string.IsNullOrWhiteSpace(errorText) ? mapped.Detail : errorText,
                FromUpstream: false);
        }
    }

    internal readonly record struct StatusProjection(
        int HttpStatus,
        long RetCode,
        string Detail,
        bool FromUpstream,
        string? Type = null,
        string? Title = null);
}

/// <summary>
/// The RUNTIME document's summary of a body whose authoritative, member-by-member schema is published
/// by <c>OpenApi/gateway.v1.yaml</c> under the name the enclosing operation's <c>x-proto-request</c> or
/// <c>x-proto-response</c> extension gives.
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ THIS TYPE IS A SUMMARY, AND THE PUBLISHED CONTRACT IS NOT.</b> The document served from
/// <c>/openapi/v1.json</c> is a convenience mirror for whoever is holding this service; the CONTRACT a
/// consumer is given is the authored <c>gateway.v1.yaml</c> in <c>PowerFramework.Contracts</c>, which
/// publishes all 120 messages and 15 enums of the projected closure CONCRETELY - every member, its
/// canonical JSON name, its canonical scalar encoding, <c>additionalProperties: false</c>, and a
/// <c>required</c> list that states what the wire actually carries. The sibling test project compares
/// every one of those schemas against its compiled descriptor on each build, so it cannot drift from
/// the protocol definition.
/// </para>
/// <para>
/// <b>WHAT A CONSUMER MUST NOT INFER FROM THE OPEN SHAPE BELOW.</b> The extension-data member makes
/// this an open object in the generated document, and that openness describes THIS DOCUMENT'S SILENCE
/// about the members - never a permissiveness in the projection. <see cref="RestProjectionEndpoints"/>
/// binds with <c>JsonParser.Default</c>, whose <c>IgnoreUnknownFields</c> is false: a member the target
/// message does not declare is answered with <c>400</c>, not discarded. An earlier revision of the
/// authored contract carried the same open shape and that WAS a defect, because a contract's audience
/// has nothing else to read; it was replaced by the concrete tier. This summary remains because
/// reproducing the generator here would put a third derivation of the same descriptors in a third
/// place, and constraint C-A leaves no shared home for one - a service may not reach into another
/// service's code, and <c>PowerFramework.Contracts</c> carries no behaviour.
/// </para>
/// <para>
/// The <c>x-proto-*</c> extensions this projection attaches are the link between the two: each names
/// the exact message, and the authored contract publishes a schema of that name.
/// </para>
/// </remarks>
public sealed record ProtoPayload
{
    /// <summary>
    /// The members of the named message, whatever that message declares - carried as extension data so
    /// the generated document describes an object without enumerating them.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Members { get; init; }
}

/// <summary>
/// The published shape of the <c>409</c> body: the problem object plus the optimistic-concurrency
/// conflict detail, mirroring the contract's <c>ConflictProblemDetails</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE ONE PAYLOAD IN THIS PROJECTION THAT IS NOT DELEGATED TO <see cref="ProtoPayload"/>, and the
/// reason is behavioural rather than stylistic. Every other body is summarised in the runtime document
/// because a consumer only has to READ it and the authored contract publishes its members concretely;
/// this one a consumer has to ACT on, so its shape is real even here. On an <c>updatewhereclause</c> mismatch the
/// caller must decide between retrying and surfacing, and it can only construct a retry if it can see
/// which column moved underneath it - so the conflict member is published as a real schema.
/// </para>
/// <para>
/// <b>The member is typed as the GENERATED <c>common.v1.ConflictDetail</c> rather than transcribed by
/// hand.</b> Transcribing it would mean restating <c>ConflictDetail</c>, <c>ConflictRow</c>,
/// <c>ColumnValue</c>, <c>AnyValue</c> and its four nested value types in this file, creating a second
/// source of truth five levels deep with nothing keeping it in step with
/// <c>Proto/common.v1.proto</c> - and the first divergence would be silent, on the one payload where a
/// silent divergence stops a caller retrying. Pointing at the generated type instead makes the
/// published schema DERIVED from the protocol definition, so it cannot drift from it.
/// </para>
/// <para>
/// Both value sets travel on every row, which is a requirement and not a convenience: the one
/// updatable DataWindow in the legacy estate declares <c>updatewhere=1</c> and marks all six of its
/// columns <c>updatewhereclause=yes</c>
/// [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], and <c>updatewhere=1</c> is the "key and
/// updateable columns" mode, so the generated WHERE clause compared the key column PLUS the original
/// value of every updateable column. Current values alone could not express what the failed statement
/// actually compared.
/// </para>
/// <para>
/// The detail is FORWARDED UNCHANGED from the upstream conflict - never reshaped, re-sorted, trimmed or
/// re-derived - because a caller needs the row state exactly as the database reported it.
/// </para>
/// <para>
/// A CLASS RATHER THAN A RECORD, unlike the other published shapes in this file: it extends the
/// framework's <c>ProblemDetails</c> so the problem members and the <c>retCode</c> extension stay
/// single-sourced, and a record may only inherit from another record.
/// </para>
/// </remarks>
public sealed class ConflictProblemDetails : ProblemDetails
{
    /// <summary>
    /// The conflict detail exactly as it arrived, field for field with
    /// <c>common.v1.ConflictDetail</c>.
    /// </summary>
    /// <remarks>
    /// Integer members of the referenced message are 64-bit and are emitted as JSON STRINGS, because
    /// that is what the canonical protobuf JSON mapping requires of a 64-bit field; the contract
    /// expresses the same fact as an <c>[integer, string]</c> union. A consumer parsing them as
    /// unquoted numbers will fail on real traffic.
    /// </remarks>
    [JsonPropertyName(ConflictMemberName)]
    public ConflictDetail? Conflict { get; init; }

    /// <summary>
    /// The member name, matching the extension member the projection actually writes so the published
    /// schema and the emitted body cannot disagree.
    /// </summary>
    private const string ConflictMemberName = "conflict";
}
