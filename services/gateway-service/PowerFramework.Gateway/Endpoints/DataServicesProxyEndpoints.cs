// ======================================================================================================
// DataServicesProxyEndpoints.cs
// `/v1/datawindow/**` - contract C-09's REST projection of C-03 and C-04.
// ======================================================================================================
//
// WHAT THIS FILE IS
//   The thirty-nine routes through which Gateway projects `dataservices.v1.DataWindowService` (C-03)
//   and `dataservices.v1.ColumnExpressionService` (C-04) onto HTTP. It is a TRANSLATION LAYER AND NOT A
//   SECOND IMPLEMENTATION: it holds no DataWindow logic, no expression engine and no validator. Every
//   route maps onto exactly one gRPC method, forwards the request payload, forwards the response
//   payload, and maps the returned gRPC status back to an HTTP status.
//
//   shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml is AUTHORITATIVE FOR THE WIRE and was read
//   in full - every route, every operation id, every declared status, every payload schema and the
//   authentication posture - before a single line below was written. Where it and docs/CONTRACTS.md
//   read differently the document wins, and each such adjudication is recorded by locator in the
//   ADJUDICATIONS block further down rather than being made silently.
//
// (C-K) DECISION 1 OF 2 - THE TRANSPORT ASYMMETRY, AND WHY IT EXISTS
//   The transport for each service in this system was decided from THE SHAPE OF ITS CURRENT INTERFACE
//   rather than chosen once and applied uniformly, and this file sits exactly on the seam between two
//   opposite answers.
//
//   DataServices is gRPC-PRIMARY because its legacy interface is an ORDERED 22-EVENT CHAIN. The
//   evidence is in the oracle: ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru declares nine
//   semantic events and thirteen raw `pbm_dwn*` events [:L11-L32], one of which produces its result
//   through a `ref string` OUT-PARAMETER [:L13] and another of which returns `any` over a `string[]`
//   argument [:L14]. The chain's veto is TRI-VALUED - prevent-once, prevent-deep, continue - and its
//   item-change result is a FOUR-VALUE alphabet `{0,1,2,3}` that is its own vocabulary and not the
//   return-code algebra [:L182-L253]. Carrying that shape needs compile-time contract enforcement,
//   bidirectional streaming, and a status model rich enough for a four-value alphabet and a tri-valued
//   veto. Protobuf over gRPC is the only transport in the mandated stack that carries all three.
//
//   Gateway is REST because it is the SOLE INGRESS, and an ingress needs three properties:
//
//     1. Reach            a browser or a third-party client must call it directly, with no
//                         intermediary of ours in the path;
//     2. Alignment with   standard caching semantics, standard proxying, and standard status codes an
//        HTTP itself      operator's existing tooling already understands;
//     3. Mature           so a consumer generates a client from the published document rather than
//        description      hand-writing one.
//        tooling
//
//   gRPC AT THE EDGE WOULD FORFEIT EXACTLY THOSE THREE. gRPC-Web REQUIRES A TRANSLATING PROXY in front
//   of it and SUPPORTS SERVER STREAMING ONLY, so the reach an ingress exists to provide would depend on
//   an extra hop, and the richer streaming the internal contract relies on would not survive the
//   translation anyway. The internal east-west edge is gRPC precisely because the reasoning inverts
//   there: both ends are owned and the contract is strongly typed and stable. docs/ARCHITECTURE.md
//   records the decision and its evidence for all four services.
//
//   THE CONSEQUENCE FOR THIS FILE is that three methods have NO REST PROJECTION AT ALL, and they are
//   named individually so the boundary is checkable rather than asserted: `EventChain` on C-03, and
//   `InvokeMethodChannel` and `TraceChannel` on C-04. All three are BIDIRECTIONAL, and the latter two
//   are additionally INVERTED - DataServices, the server, calls back INTO its client, because the
//   legacy expects the APPLICATION to implement the macro switch. An inverted stream has no
//   request/response direction to project. Nothing here invents one: there is no long-polling scheme,
//   no callback-URL registration, no WebSocket upgrade and no server-sent-events channel anywhere in
//   this file. Inventing one would be a new feature (C-B) and would silently change the event-ordering
//   guarantees the contract depends on - and a PARTIAL projection would be worse than none, because a
//   consumer would receive part of an ordered chain with no way to know what it had missed. Consumers
//   needing any of the three use DataServices' gRPC surface directly. That is a documented gap.
//
//   Every OTHER method is projected, including both server streams. A server stream IS projectable
//   because its ordering is the trivial one - the server produces a sequence, the client consumes it in
//   order - so `Retrieve` and `EventStream` are published as operations returning the same chunks the
//   stream would have delivered, in the same order, WITH THE CHUNKING CONTRACT UNCHANGED.
//
// (C-K) DECISION 2 OF 2 - gRPC `Aborted` BECOMES HTTP `409`, AND THE CONFLICT DETAIL TRAVELS INTACT
//   On an optimistic-concurrency mismatch DataServices answers gRPC `Aborted` with a
//   `common.v1.ConflictDetail` attached to the status as a versioned binary trailer.
//   Clients/DataServicesClient.cs decodes it and raises DataServicesConflictException carrying THE
//   MESSAGE OBJECT ITSELF. This file projects that as HTTP `409` carrying THE SAME PAYLOAD, serialized
//   once through the canonical protobuf JSON mapping and never reshaped. `Aborted` is the canonical
//   gRPC-to-409 mapping and the ingress contract fixes it as such.
//
//   WHY LOSING THE PAYLOAD WOULD BE A BEHAVIOURAL REGRESSION, MECHANICALLY. The only updatable
//   DataWindow in the legacy estate, ws_objects/pfw.tests.pbl.src/dw_sqlite.srd, is declared
//   `updatewhere=1` with ALL SIX of its columns marked `updatewhereclause=yes` [:L8-L14], and
//   `updatewhere=1` is the "key and updateable columns" concurrency mode - so the generated statement's
//   WHERE clause carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN. The
//   conflict answer therefore contains information the caller CANNOT RECONSTRUCT: which column moved
//   underneath it, and what its value is now. Comparing `currentValues` against `originalValues` per row
//   is the whole diagnostic. A 409 that summarised it, truncated it, or replaced it with a message
//   string would leave the caller a choice between a blind overwrite and a spurious failure, and both
//   are regressions.
//
//   A CONFLICT IS A DEFINITIVE ANSWER, NOT A TRANSIENT FAULT. It is therefore NEVER swallowed, NEVER
//   degraded to a generic 500, and NEVER RETRIED - not here, and not by any resilience policy.
//   Re-sending the same payload produces the same 409 because the original values it carries are still
//   stale. CALLERS IMPLEMENT AN EXPLICIT RETRY-OR-SURFACE POLICY: re-read, rebase and resubmit, or
//   surface the conflict to whoever can decide. THERE IS NO SILENT OVERWRITE ANYWHERE IN THIS SYSTEM.
//   Nothing in this file adds a retry, and the resilience pipeline that Program.cs attaches to the
//   generated clients is a transport-fault policy which never sees a decoded conflict, because the
//   conflict is raised as a typed exception above it.
//
// ADJUDICATIONS - recorded so the projection is auditable rather than merely asserted
//   A1  THE STATUS MAP. gateway.v1.yaml's introduction and docs/CONTRACTS.md section 12.1 publish the
//       SAME translation table, and it is followed literally: OK to 200, Aborted to 409, InvalidArgument
//       to 400, Unauthenticated to 401, PermissionDenied to 403, NotFound to 404, Unimplemented to 501,
//       Unavailable to 503, DeadlineExceeded to 504, and Internal or Unknown to 500. Where BOTH
//       documents are silent - FailedPrecondition, OutOfRange, AlreadyExists, ResourceExhausted,
//       Cancelled, DataLoss - the canonical gRPC-to-HTTP mapping is used, and each is recorded on its
//       arm of the map below. No status outside those two sources is invented, and no distinguishable
//       upstream failure is collapsed into 500 for convenience.
//
//   A2  WHY `502` IS REACHABLE AT ALL. Both documents state that 502 is "the one case the table cannot
//       describe, because it is the case where NO gRPC RESPONSE ARRIVED AT ALL" - an unreachable
//       upstream, or a call that failed in transit after the configured retry policy was exhausted. That
//       is a real distinction and it is implemented rather than paraphrased: a gRPC `Unavailable` that
//       the CLIENT synthesized from a transport failure carries the originating transport exception on
//       its status, whereas an `Unavailable` the SERVER genuinely answered does not. The first becomes
//       502 and names the upstream; the second becomes 503 per the table. A transport failure that
//       escapes as itself rather than as a gRPC status becomes 502 on the same grounds. THIS RESPONSE
//       EXISTS BECAUSE OF THE DECOMPOSITION ITSELF: an in-process call cannot fail in transit and a
//       network call can, so handling it is required BY the transition rather than layered on top of it.
//
//   A3  `Unimplemented` PRODUCES A PLAIN `501`, AND IT IS NOT ONE OF THE FOUR RESERVED ROUTES. Those
//       live exclusively in DeferredCapabilityEndpoints.cs and carry a machine-readable body naming a
//       deferred Phase 2 service. NOTHING in this file emits that marker, names any deferred service, or
//       declares a route under any reserved prefix. A 501 here means the upstream reported that a method
//       ITS OWN CONTRACT PUBLISHES is not implemented, which is deployment or version skew rather than a
//       deferred capability. It is consequently not a DECLARED response of any operation below, for the
//       same reason the cross-cutting 401 is not declared on the reserved routes: the contract's
//       per-operation response sets enumerate what the route normally produces.
//
//   A4  THE TWO `409`s ARE DISTINGUISHABLE, because a caller's retry-or-surface logic keys off which one
//       it received. The concurrency conflict carries its own problem type, its own title and the
//       `conflict` member; an `AlreadyExists` carries a different type, a different title and a
//       different `retCode`, and no `conflict` member. A third, deliberately distinct, shape covers the
//       case where the upstream answered `Aborted` but attached NO decodable detail: the status is still
//       409, because swallowing it would let a rejected update look successful, but no `conflict` member
//       is fabricated - an empty conflict would report a conflict the caller cannot act on, which is
//       precisely what the contract forbids.
//
//   A5  `closeValidationSession` DOES NOT SYNTHESIZE ITS `404`. The contract's prose says a double close
//       is reported as 404, while Proto/dataservices.v1.proto declares closure IDEMPOTENT with a
//       `wasOpen` flag that is "informational only - a false value is NOT an error". This file projects
//       TRANSPORT, NOT SEMANTICS: a 404 here is the projection of an upstream `NotFound`, and no status
//       is ever derived by inspecting a response field. Deriving one would be Gateway reinterpreting the
//       upstream's answer, which is the one thing a translation layer must not do.
//
//   A6  DEFAULT VALUES ARE FORMATTED ON THE WAY OUT. The canonical protobuf JSON mapping omits fields
//       holding their default value, and that would drop REQUIRED members of the contract's own mirrored
//       schemas - `RetrieveChunk` requires `rowCount`, `chunkIndex`, `final` and `cumulativeRowCount`,
//       every one of which is legitimately zero or false on a real chunk. The formatter therefore emits
//       default values. Fields with EXPLICIT PRESENCE are unaffected and stay absent when unset, which
//       preserves the contract's own signal that a chunk with no buffer field is a mixed-buffer chunk.
//
//   A7  REQUEST BINDING IS STRICT. The body must be the canonical protobuf JSON mapping of the message
//       the operation's `x-proto-request` names, and an unrecognised member is REJECTED with 400 rather
//       than dropped. Silently discarding a member a caller sent is silent data loss on an update path,
//       which is the class of defect this refactor exists to avoid. The rejection detail is FIXED PROSE:
//       a parser message can quote the offending body back, and a body is caller content that may
//       contain anything, so it never reaches a response or a log record (C-F).
//
//   A8  FIVE OF THE THIRTY-NINE RESPONSE SCHEMAS ARE DELEGATED RATHER THAN MIRRORED, and the reason is
//       mechanical. The contract mirrors `RetrieveResult`, the three event-gate responses and
//       `ExpressionEventStreamResult` field for field, and those shapes are built on the legacy's
//       preserved SCREAMING_SNAKE value sets - `RetCodeValue` and `DataWindowEventBit`. Declaring them
//       here would mean declaring those identifiers here, and the repository-root .editorconfig scopes
//       its naming-analyzer suppressions to a fixed list of seven files of which NO Gateway file is one,
//       while Directory.Build.props sets TreatWarningsAsErrors - so the declaration would be a BUILD
//       ERROR, not a style debate. It would also create a second source of truth for shapes
//       PowerFramework.Contracts already publishes. Each of the five therefore declares the free-form
//       envelope and names its exact message in `x-proto-response`, which is the same delegation
//       mechanism the contract itself uses for its per-operation envelopes.
//
// WHAT THIS FILE DELIBERATELY DOES NOT CONTAIN - the negatives are part of the specification, and they
// are mechanically checkable, which is the point
//   * No storage access of any kind: no connection string, no database context, no SQL (C-E). Gateway
//     holds no storage provider, and Persistence is the only service in the system that generates or
//     executes SQL.
//   * NO CALL TO PERSISTENCE and no client for it. Nothing but DataServices calls Persistence, and this
//     file reaches DataServices and nothing further.
//   * No import of any sibling SERVICE namespace. The only permitted cross-service coupling is the
//     published contracts project plus the Clients/ wrapper over it (C-A).
//   * No caching layer, no response reshaping for convenience, no pagination the contract does not
//     define, no field renaming, no client-side filtering, no aggregation across operations, and no
//     auto-retry of anything the contract says to surface (C-B).
//   * No session state. The four pieces of cross-event mutable state live in a SERVER-HELD session
//     inside DataServices, keyed by correlation id [se_cst_dw.sru:L89-L96]; this projection passes the
//     identifier through and holds nothing.
//   * No reinterpretation of a contract value. The item-change alphabet, the tri-valued veto and the
//     buffer discriminator travel exactly as the payload carries them: this file translates transport,
//     not semantics.
//   * No token minting and no signing key. Gateway holds verification material only; Security is the
//     sole issuer. The outbound credential is supplied by the token-provider abstraction the sibling
//     Security client implements and the DataServices client already uses, so nothing here acquires,
//     inspects or logs one (C-F, C-G).
//   * No unredacted statement text. `DbError.sqlsyntax` is redacted AT THE CONTRACT LEVEL because the
//     legacy field carried the complete generated statement including interpolated literals and the
//     legacy logger performed no redaction at all. Nothing here reconstructs it, concatenates
//     parameters back into it, echoes it, or passes an upstream error object into a log message that
//     would serialise it.
//   * No deferred-service name, no reserved-route prefix, no reserved marker, and no port number - not
//     this service's listener and not the slot reserved for a Phase 2 one. A port belongs in
//     configuration and documentation, never in endpoint code (C-D, C-I).
//   * No exception-throwing placeholder, and no not-implemented exception type named or thrown anywhere.
//     Returning the 501 STATUS is required of the map; a type that throws to make something compile is
//     the placeholder that is forbidden, and they are not the same thing.
//   * No rendering behaviour of any kind. The four headless models' data - filter and sort expressions,
//     search state, and the menu item model with its labels, ids, enabled and split flags and computed
//     logical text widths - is carried through as DATA. No DPI conversion, no font measurement, no
//     window geometry and no pixel coordinate crosses this boundary; those halves are deferred.
//
// NO PERFORMANCE CLAIM IS MADE OR IMPLIED by anything in this file. The repository publishes no
// service-level agreement, no latency budget, no throughput target and no availability commitment, so
// none may be asserted. This refactor is explicitly not a performance refactor.
//
// RULES POSITION
//   review_rules returns exactly "No user rules provided.", so NO USER-SPECIFIED RULE governs this
//   file. The enterprise-standard baseline applies in its place and is honoured above: nullable
//   reference types and warnings as errors inherited and never relaxed, no secret in source or in any
//   response body, structured logging with redaction on the one field known to carry interpolated
//   literals, and explicitly versioned contracts as the only cross-service coupling. The binding
//   constraints are the Agent Action Plan's C-A through C-L, which the rules facility cannot surface.
// ======================================================================================================

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.OpenApi;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Shared.Kernel;

// ALIASED RATHER THAN IMPORTED, DELIBERATELY. PowerFramework.Contracts.Common.V1 publishes a `RetCode`
// MESSAGE whose simple name collides with PowerFramework.Shared.Kernel's `RetCode` CATALOGUE, and the
// catalogue is what this file consumes on every problem body. Importing both namespaces would make the
// name ambiguous; aliasing the one type actually needed from the contracts side keeps `RetCode.*`
// unqualified and unmistakable.
using ConflictDetail = PowerFramework.Contracts.Common.V1.ConflictDetail;

namespace PowerFramework.Gateway.Endpoints;

/// <summary>
/// Declares <c>/v1/datawindow/**</c>, contract C-09's REST projection of C-03
/// (<c>dataservices.v1.DataWindowService</c>) and C-04
/// (<c>dataservices.v1.ColumnExpressionService</c>).
/// </summary>
/// <remarks>
/// <para>
/// A translation layer and not a second implementation. Every route maps onto exactly one gRPC method,
/// forwards the request payload, forwards the response payload and maps the returned gRPC status back to
/// HTTP. Gateway holds no DataWindow logic, no expression engine, no validator and no session state.
/// </para>
/// <para>
/// <b>The decisive behaviour is the status translation.</b> gRPC <see cref="StatusCode.Aborted"/> - an
/// optimistic-concurrency mismatch - becomes HTTP <c>409</c> carrying the upstream's own
/// <c>ConflictDetail</c> unchanged, including the current and original values of every marked column, so
/// a caller can implement an explicit retry-or-surface policy. The conflict is never swallowed, never
/// retried and never degraded to a generic <c>500</c>; there is no silent overwrite anywhere in this
/// system.
/// </para>
/// <para>
/// Three methods have no REST projection because they are bidirectional, and two of those are
/// additionally inverted: <c>EventChain</c> on C-03, and <c>InvokeMethodChannel</c> and
/// <c>TraceChannel</c> on C-04. Nothing here invents a representation for any of them. Consumers needing
/// them use DataServices' gRPC surface directly, which is a documented gap deliberately preferred over a
/// partial projection.
/// </para>
/// <para>
/// Every route requires a bearer token, unconditionally and in every environment. Gateway holds
/// verification material only and mints nothing.
/// </para>
/// </remarks>
public static class DataServicesProxyEndpoints
{
    // --------------------------------------------------------------------------------------------------
    //  ROUTES AND GROUPING
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The route prefix every operation in this file hangs from, spelled exactly as the contract spells
    /// it.
    /// </summary>
    private const string DataWindowGroupPrefix = "/v1/datawindow";

    /// <summary>
    /// The nested prefix carrying C-04's operations, so that the contract's own
    /// <c>/v1/datawindow/expression/**</c> spelling is produced by composition rather than repeated
    /// thirty-two times.
    /// </summary>
    private const string ExpressionGroupPrefix = "/expression";

    // --------------------------------------------------------------------------------------------------
    //  PUBLISHED METADATA VOCABULARY
    // --------------------------------------------------------------------------------------------------

    /// <summary>The contract's tag for C-03's operations.</summary>
    private const string DataWindowTag = "DataWindow";

    /// <summary>
    /// The contract's tag for C-04's operations. Separate from <see cref="DataWindowTag"/> for the same
    /// reason C-04 is a separate contract from C-03: the expansion engine versions independently.
    /// </summary>
    private const string ColumnExpressionTag = "ColumnExpression";

    /// <summary>The contract identifier C-03's projected operations carry.</summary>
    private const string DataWindowContractId = "C-03";

    /// <summary>The contract identifier C-04's projected operations carry.</summary>
    private const string ColumnExpressionContractId = "C-04";

    /// <summary>The specification extension naming the contract an operation belongs to.</summary>
    private const string ContractIdExtensionName = "x-contract-id";

    /// <summary>The specification extension naming the gRPC method an operation projects.</summary>
    private const string GrpcMethodExtensionName = "x-grpc-method";

    /// <summary>The specification extension naming the protobuf message the request body maps to.</summary>
    private const string ProtoRequestExtensionName = "x-proto-request";

    /// <summary>The specification extension naming the protobuf message the response body maps to.</summary>
    private const string ProtoResponseExtensionName = "x-proto-response";

    /// <summary>The specification extension declaring an operation's upstream streaming shape.</summary>
    private const string GrpcStreamingExtensionName = "x-grpc-streaming";

    /// <summary>
    /// The value <see cref="GrpcStreamingExtensionName"/> carries on the two projected server streams.
    /// </summary>
    private const string ServerStreamingValue = "server";

    /// <summary>The component name of the contract's bearer security scheme.</summary>
    private const string BearerSecuritySchemeId = "bearerAuth";

    /// <summary>The HTTP authentication scheme name the contract's security scheme declares.</summary>
    private const string BearerSchemeName = "bearer";

    /// <summary>The credential format the contract's security scheme declares.</summary>
    private const string BearerCredentialFormat = "JWT";

    // --------------------------------------------------------------------------------------------------
    //  PROBLEM-DETAILS VOCABULARY
    //
    //  The member names and the correlation-identifier resolution are IDENTICAL to the ones
    //  Diagnostics/SystemErrorHandler.cs uses. That is load bearing rather than tidy: the contract
    //  guarantees that the identifier a caller is handed is THE SAME VALUE that appears in the operator
    //  record for the same occurrence, so a caller quoting it always finds a record. Two resolutions
    //  would break that guarantee while still looking correct.
    // --------------------------------------------------------------------------------------------------

    /// <summary>The problem-details extension member carrying the legacy return code.</summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>The problem-details extension member naming the upstream a forwarded failure came from.</summary>
    private const string UpstreamExtensionMember = "upstream";

    /// <summary>The problem-details extension member carrying the correlation identifier.</summary>
    private const string TraceIdExtensionMember = "traceId";

    /// <summary>The problem-details extension member carrying the optimistic-concurrency payload.</summary>
    private const string ConflictExtensionMember = "conflict";

    /// <summary>
    /// The only upstream any operation in this file reaches, spelled as the contract's own enumeration
    /// spells it.
    /// </summary>
    /// <remarks>
    /// Gateway never calls Persistence and never calls Security from these routes, so no other value can
    /// legitimately appear on a problem this file produces.
    /// </remarks>
    private const string DataServicesUpstream = "dataservices";

    /// <summary>
    /// RFC 9457's own default problem type, used wherever no more specific type applies.
    /// </summary>
    private const string DefaultProblemType = "about:blank";

    /// <summary>The problem type identifying an optimistic-concurrency conflict carrying its detail.</summary>
    /// <remarks>
    /// A <c>urn:</c> rather than an <c>https:</c> reference, deliberately: a problem type is an
    /// identifier and not a promise that something can be fetched from it, and publishing a
    /// dereferenceable address would invite a consumer to depend on a resource this system does not
    /// serve.
    /// </remarks>
    private const string ConflictProblemType = "urn:powerframework:problem:optimistic-concurrency-conflict";

    /// <summary>
    /// The problem type identifying an upstream <c>Aborted</c> that arrived without a decodable conflict
    /// detail. Distinct from <see cref="ConflictProblemType"/> so a caller can tell an actionable
    /// conflict from one it cannot rebase against.
    /// </summary>
    private const string ConflictWithoutDetailProblemType =
        "urn:powerframework:problem:optimistic-concurrency-conflict-without-detail";

    /// <summary>
    /// The problem type identifying the other canonical <c>409</c>: an upstream <c>AlreadyExists</c>.
    /// Distinct from both conflict types because a caller's retry-or-surface logic keys off which of the
    /// three it received.
    /// </summary>
    private const string AlreadyExistsProblemType = "urn:powerframework:problem:already-exists";

    /// <summary>The title accompanying <see cref="ConflictProblemType"/>.</summary>
    private const string ConflictProblemTitle = "Optimistic-concurrency conflict";

    /// <summary>The title accompanying <see cref="ConflictWithoutDetailProblemType"/>.</summary>
    private const string ConflictWithoutDetailProblemTitle =
        "Optimistic-concurrency conflict without detail";

    /// <summary>The title accompanying <see cref="AlreadyExistsProblemType"/>.</summary>
    private const string AlreadyExistsProblemTitle = "Already exists";


    // --------------------------------------------------------------------------------------------------
    //  PROBLEM `detail` PROSE - FIXED, AND FIXED FOR A REASON (C-F, CWE-209)
    //
    //  Every string below is a constant. None is composed from an upstream status message, an exception
    //  message, a parser diagnostic or any part of a request body. All four are arbitrary content that a
    //  caller or an upstream chose, and any of them may quote a credential, a file path or - on the data
    //  path - an interpolated SQL literal back at whoever reads the response. The contract states the
    //  rule for this member directly: `detail` never carries key material, a stack trace, a file path, a
    //  connection string or an unredacted statement.
    //
    //  That redaction is defensible only because it is a REDIRECTION: the `traceId` member locates the
    //  operator record for the same occurrence, which is where the specifics live.
    // --------------------------------------------------------------------------------------------------

    /// <summary>The detail for a request whose body was absent where the operation requires one.</summary>
    private const string MissingBodyDetail =
        "A request body is required, and must be the canonical protobuf JSON mapping of the message named "
        + "by this operation's x-proto-request extension.";

    /// <summary>
    /// The detail for a request body that is not valid JSON, or that does not map onto the operation's
    /// request message - including a body carrying a member the message does not declare.
    /// </summary>
    private const string MalformedBodyDetail =
        "The request body could not be bound to this operation's request message. It must be the "
        + "canonical protobuf JSON mapping of the message named by the x-proto-response's sibling "
        + "x-proto-request extension, and an unrecognised member is rejected rather than discarded so "
        + "that a member a caller sent is never silently lost. The parser's own diagnostic is "
        + "deliberately withheld because it quotes the offending body, which is caller content.";

    /// <summary>The detail for an upstream <c>InvalidArgument</c>.</summary>
    private const string InvalidArgumentDetail =
        "DataServices rejected an argument. The retCode member carries the legacy return code for the "
        + "rejection, so the specific validation is identifiable rather than only the HTTP class.";

    /// <summary>The detail for an upstream <c>FailedPrecondition</c>.</summary>
    private const string FailedPreconditionDetail =
        "DataServices rejected the request because its own state does not permit the operation. The "
        + "request itself is not retryable unchanged.";

    /// <summary>The detail for an upstream <c>OutOfRange</c>.</summary>
    private const string OutOfRangeDetail =
        "An ordinal in the request lies outside the range the target accepts. Legacy row and column "
        + "ordinals are ONE-BASED throughout this system.";

    /// <summary>The detail for an upstream <c>Unauthenticated</c>.</summary>
    private const string UnauthenticatedDetail =
        "The credential presented to DataServices was not accepted. Gateway holds verification material "
        + "only; Security is the sole token issuer in this system.";

    /// <summary>The detail for an upstream <c>PermissionDenied</c>.</summary>
    private const string PermissionDeniedDetail =
        "The credential is valid but does not carry the scope this operation requires. Deliberately "
        + "distinct from an absent credential.";

    /// <summary>The detail for an upstream <c>NotFound</c>.</summary>
    private const string NotFoundDetail =
        "DataServices could not resolve something the request named - most often a session identifier "
        + "that has expired or was already closed, or a selector naming a column the DataWindow does not "
        + "have.";

    /// <summary>The detail for an upstream <c>AlreadyExists</c>.</summary>
    private const string AlreadyExistsDetail =
        "DataServices reported that what the request asked it to create already exists. This is NOT an "
        + "optimistic-concurrency conflict: it carries no conflict member, and a caller must not treat it "
        + "as one.";

    /// <summary>The detail for the conflict this projection exists to carry.</summary>
    private const string ConflictDetailProse =
        "An optimistic-concurrency conflict. The conflict member carries the upstream's own payload "
        + "unchanged, including, per failing row, both the current server-side values and the original "
        + "values the request believed were current - which is what identifies the column that moved. "
        + "Re-sending the same payload produces this same response, because those original values are "
        + "still stale: re-read and rebase, or surface the conflict. Nothing is overwritten silently.";

    /// <summary>
    /// The detail for an upstream <c>Aborted</c> that arrived with no decodable conflict detail.
    /// </summary>
    private const string ConflictWithoutDetailProse =
        "DataServices reported an optimistic-concurrency conflict but attached no decodable conflict "
        + "detail. The status is preserved, because reporting anything else would let a rejected update "
        + "appear to have succeeded, and no conflict member is fabricated, because an empty one would "
        + "describe a conflict no caller could act on. The update was NOT applied. Re-read before "
        + "resubmitting.";

    /// <summary>The detail for an upstream <c>ResourceExhausted</c>.</summary>
    private const string ResourceExhaustedDetail =
        "DataServices reported that a resource the operation needs is exhausted. The request was not "
        + "processed.";

    /// <summary>The detail for an upstream <c>Unimplemented</c>.</summary>
    /// <remarks>
    /// It names no service and carries no reserved marker: this is an upstream reporting that a method its
    /// own contract publishes is unavailable, which is deployment or version skew.
    /// </remarks>
    private const string UnimplementedDetail =
        "DataServices reported that this operation's upstream method is not implemented by the "
        + "deployment currently answering. The projected contract publishes it, so this indicates a "
        + "version skew between Gateway and its upstream rather than a capability boundary.";

    /// <summary>The detail for an upstream <c>DeadlineExceeded</c>.</summary>
    private const string DeadlineExceededDetail =
        "The call to DataServices did not complete within its deadline. Whether the operation took effect "
        + "is undetermined, so a caller must re-read before assuming either outcome.";

    /// <summary>The detail for a server-answered <c>Unavailable</c>.</summary>
    private const string UnavailableDetail =
        "DataServices answered that it is unavailable. The request was not processed.";

    /// <summary>The detail for a transport failure that produced no gRPC response at all.</summary>
    private const string UpstreamUnavailableDetail =
        "DataServices could not be reached, or the call to it failed in transit after the configured "
        + "retry policy was exhausted, so no response arrived. This failure mode is one decomposition "
        + "itself creates: an in-process call cannot fail in transit and a network call can.";

    /// <summary>The detail for an upstream <c>Cancelled</c>.</summary>
    private const string CancelledDetail =
        "The call to DataServices was cancelled before it produced a response. Under the preserved "
        + "return-code algebra a cancellation is neither a success nor a failure, and whether the "
        + "operation took effect is undetermined.";

    /// <summary>The detail for an upstream <c>DataLoss</c>.</summary>
    private const string DataLossDetail =
        "DataServices reported unrecoverable data loss or corruption on the data path. Any statement text "
        + "the underlying error carried is redacted and is not reproduced here.";

    /// <summary>The detail for an upstream <c>Internal</c>.</summary>
    private const string InternalErrorDetail =
        "DataServices reported an internal failure. The diagnostic is recorded on the operator channel "
        + "against the traceId member of this response; no exception text, host, path, key material or "
        + "statement text is reproduced here.";

    /// <summary>The detail for any status this projection does not classify.</summary>
    private const string UnclassifiedFailureDetail =
        "The call to DataServices failed with a status this projection does not classify. The diagnostic "
        + "is recorded on the operator channel against the traceId member of this response.";

    // --------------------------------------------------------------------------------------------------
    //  RESPONSE DESCRIPTIONS - carried across from the contract so the generated document and the
    //  hand-authored one say the same thing to a consumer.
    // --------------------------------------------------------------------------------------------------

    /// <summary>The contract's shared success description for a projected operation.</summary>
    private const string ProjectedSuccessDescription =
        "The projected gRPC method returned OK. The body is the canonical protobuf JSON mapping of the "
        + "message named in this operation's x-proto-response extension.";

    /// <summary>The contract's shared <c>400</c> description.</summary>
    private const string BadRequestDescription =
        "The projected gRPC method returned InvalidArgument, or the request failed Gateway's own binding. "
        + "retCode carries the originating legacy return code so the specific validation is identifiable "
        + "rather than merely the HTTP class.";

    /// <summary>
    /// The contract's <c>400</c> description for the five operations on which a cross-session foreign
    /// variable reference is a distinctive rejection reason.
    /// </summary>
    private const string CrossSessionReferenceBlockedDescription =
        "The request was rejected. On this operation one rejection reason is distinctive enough to record "
        + "separately: a cross-DataWindow variable reference spanning expression sessions or service "
        + "instances is BLOCKED. It is a 400 rather than a status of its own because the published status "
        + "mapping sanctions none, and that mapping already provides the mechanism - InvalidArgument "
        + "projects to 400 carrying the originating retCode - so a caller distinguishes this outcome by "
        + "reading retCode and the expression-error category, not by counting statuses. The narrowing is "
        + "deliberate: the legacy holds a live in-process pointer to another DataWindow's expression "
        + "service, a pointer cannot be serialized, and approximating a dereference across a network "
        + "would produce results wrong in a way no test would obviously catch. A caller receiving this "
        + "must not retry - the topology rejected it, not the request.";

    /// <summary>The contract's shared <c>401</c> description.</summary>
    private const string UnauthorizedDescription =
        "No token was presented, or the token presented is expired, malformed, or not valid for this "
        + "service. This response is part of the published contract rather than an implementation "
        + "detail: it is the standing proof that the boundary is authenticated (C-G).";

    /// <summary>The contract's shared <c>403</c> description.</summary>
    private const string ForbiddenDescription =
        "The projected gRPC method returned PermissionDenied. The token is valid but does not carry the "
        + "scope this operation requires. Deliberately distinguished from 401 so a caller can tell a "
        + "missing credential from an insufficient one.";

    /// <summary>The contract's shared <c>404</c> description.</summary>
    private const string NotFoundDescription =
        "The projected gRPC method returned NotFound - most often a session identifier that has expired "
        + "or was already closed, or a column selector naming a column the DataWindow does not have.";

    /// <summary>The contract's <c>409</c> description.</summary>
    private const string ConflictDescription =
        "The projected gRPC method returned Aborted: an optimistic-concurrency conflict. The body carries "
        + "the conflict detail UNCHANGED, including the current row state, so a caller has what it needs "
        + "to decide between retrying and surfacing. Callers implement an explicit retry-or-surface "
        + "policy, and there is no silent overwrite anywhere in the system. Re-sending the same payload "
        + "produces the same 409, because the original values it carries are still stale - a retry must "
        + "first re-read.";

    /// <summary>The contract's shared <c>500</c> description.</summary>
    private const string InternalErrorDescription =
        "The projected gRPC method returned Internal. The statement field is redacted: the legacy "
        + "sqlsyntax field carries the complete generated statement including interpolated literal values "
        + "and the legacy logger performs no redaction at all. detail never carries key material, a stack "
        + "trace, a file path or a connection string.";

    /// <summary>The contract's shared <c>502</c> description.</summary>
    private const string UpstreamUnavailableDescription =
        "An upstream service could not be reached, or the call to it failed in transit after the "
        + "configured retry policy was exhausted. This response exists because of the decomposition "
        + "itself: an in-process call cannot fail in transit and a network call can, so handling the "
        + "failure is required BY the transition rather than being a behavioural improvement layered on "
        + "top of it. The body names which upstream failed.";


    // --------------------------------------------------------------------------------------------------
    //  THE OPERATOR LOG MESSAGE - ALLOWLISTED, exactly as Diagnostics/SystemErrorHandler.cs allowlists
    //  its non-structural form. What is passed identifies the fault WITHOUT QUOTING IT: the request's own
    //  method, the route PATTERN rather than the path, the gRPC status name, the HTTP status the caller is
    //  about to receive, the legacy return code, the correlation identifier, and the exception TYPE name.
    //
    //  WHAT IS DELIBERATELY ABSENT, and why each absence is required rather than cautious:
    //    * the exception OBJECT, because the logging abstraction renders a passed exception through its
    //      own string conversion, which includes every message in the chain;
    //    * the gRPC status DETAIL, because it is upstream text that may quote a statement or a literal;
    //    * the request body and the response body, because both are caller or upstream content;
    //    * any header, and in particular the outbound bearer credential the client attaches;
    //    * any part of a DbError, whose statement field the legacy carried unredacted.
    // --------------------------------------------------------------------------------------------------

    /// <summary>The allowlisted operator record written once per projected failure.</summary>
    private const string ProjectedFailureLogMessage =
        "The /v1/datawindow projection is answering {HttpStatus} with retCode {RetCode} for {HttpMethod} "
        + "{RoutePattern}: DataServices reported gRPC status {GrpcStatus} ({ExceptionType}). "
        + "Correlation {CorrelationId}. The upstream status detail, the request body and the response body "
        + "are deliberately not recorded here - they are caller or upstream content.";

    /// <summary>The allowlisted operator record written once per Gateway-side binding failure.</summary>
    private const string BindingFailureLogMessage =
        "The /v1/datawindow projection rejected a request body before reaching DataServices and is "
        + "answering {HttpStatus} with retCode {RetCode} for {HttpMethod} {RoutePattern}. Correlation "
        + "{CorrelationId}. The parser diagnostic and the body itself are deliberately not recorded - the "
        + "body is caller content and may carry anything.";

    /// <summary>
    /// The logger category for the two records above, so an operator filters this projection's records
    /// without also matching the typed client's.
    /// </summary>
    private const string LoggerCategory = "PowerFramework.Gateway.Endpoints.DataServicesProxyEndpoints";

    /// <summary>
    /// The description used where a route is reached without a matched pattern, which is unreachable for
    /// these routes and guarded anyway so that a record can never publish an empty field.
    /// </summary>
    private const string UnroutedRouteDescription = "(unrouted)";

    // --------------------------------------------------------------------------------------------------
    //  THE PROTOBUF JSON CODECS
    // --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The strict request parser. Thread-safe and stateless, so one instance serves every route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// STRICT ON PURPOSE - see adjudication A7. The default settings reject a member the target message
    /// does not declare, which turns a caller's typo or a version skew into a <c>400</c> instead of
    /// silently discarding a value the caller believed it had sent. On an update path that discard would
    /// be silent data loss.
    /// </para>
    /// <para>
    /// No type registry is supplied because none is needed: <c>Proto/common.v1.proto</c> and
    /// <c>Proto/dataservices.v1.proto</c> use no <c>google.protobuf.Any</c> and no well-known wrapper
    /// anywhere in a payload message - the contract records that omission as deliberate.
    /// </para>
    /// </remarks>
    private static readonly JsonParser RequestParser = JsonParser.Default;

    /// <summary>
    /// The response formatter. Thread-safe and stateless, so one instance serves every route.
    /// </summary>
    /// <remarks>
    /// DEFAULT VALUES ARE FORMATTED - see adjudication A6. The canonical mapping omits them, which would
    /// drop REQUIRED members of the contract's own mirrored schemas: <c>RetrieveChunk</c> requires
    /// <c>rowCount</c>, <c>chunkIndex</c>, <c>final</c> and <c>cumulativeRowCount</c>, and every one of
    /// those is legitimately zero or false on a real chunk. Fields with explicit presence are unaffected
    /// and remain absent when unset, which preserves the contract's signal that a chunk carrying no
    /// buffer field is a mixed-buffer chunk whose rows are individually tagged.
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
    /// The name of the session-identifier parameter the two close operations take on the path and the
    /// event-gate read takes on the query string.
    /// </summary>
    /// <remarks>
    /// Composed into the route templates rather than spelled again, so a template and its declared
    /// parameter cannot drift apart.
    /// </remarks>
    private const string SessionIdParameter = "sessionId";

    /// <summary>The description the contract gives the session-identifier parameter.</summary>
    private const string SessionIdParameterDescription =
        "The correlation identifier a session-opening operation returned. The session state itself is held "
        + "by DataServices, not by Gateway.";

    /// <summary>
    /// Declares the thirty-nine <c>/v1/datawindow</c> operations on the supplied route builder.
    /// </summary>
    /// <param name="endpoints">The route builder the composition root is populating.</param>
    /// <returns>
    /// The same <paramref name="endpoints"/> instance, so the composition root can chain its endpoint
    /// registrations.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="endpoints"/> is <see langword="null"/>. A composition root that reached this method
    /// without a route builder is structurally broken, and the legacy framework's posture for a structural
    /// fault is to fail immediately rather than degrade past it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The only public entry point in this file, and the route definitions live here rather than at the
    /// composition root so that the whole projection - paths, methods, authorization, response surface and
    /// published metadata - is reviewable as one unit.
    /// </para>
    /// <para>
    /// <b>The return type is deliberately the route builder and not a route handler builder or a group.</b>
    /// Handing back either would let a caller append <c>AllowAnonymous</c>, which takes precedence over
    /// <c>RequireAuthorization</c> in endpoint metadata and would silently open thirty-nine authenticated
    /// routes from a different file. Withholding it makes that impossible.
    /// </para>
    /// <para>
    /// Both groups are authorized by the parent group's single unconditional
    /// <c>RequireAuthorization</c> call. There is no environment test and no configuration switch guarding
    /// it, here or anywhere else: a bypass that exists only in Development is still a bypass, and it would
    /// make the local build disagree with the published contract.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapDataServicesProxyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // C-G. One call, applied to the parent group, inherited by every endpoint declared below including
        // those declared on the nested expression group.
        RouteGroupBuilder dataWindow = endpoints.MapGroup(DataWindowGroupPrefix).RequireAuthorization();
        RouteGroupBuilder expression = dataWindow.MapGroup(ExpressionGroupPrefix);

        MapDataWindowOperations(dataWindow);
        MapColumnExpressionOperations(expression);

        return endpoints;
    }

    /// <summary>
    /// Declares C-03's fifteen projected operations: the retrieval, the paired validation session, the
    /// update, the event gate, and the four headless models as a read and an apply each.
    /// </summary>
    /// <param name="group">The <c>/v1/datawindow</c> group.</param>
    /// <remarks>
    /// C-03 declares sixteen methods. <c>EventChain</c> is the one exclusion, because it is bidirectional
    /// and carries a strictly ordered chain with a tri-valued veto - see the transport decision in this
    /// file's header. Fifteen are declared here and the sixteenth is deliberately absent.
    /// </remarks>
    private static void MapDataWindowOperations(RouteGroupBuilder group)
    {
        MapServerStream<RetrieveRequest, RetrieveChunk>(
            group,
            new("/retrieve", "retrieve", "Retrieve", ContractSurface.DataWindow,
                "Retrieve rows as an ordered sequence of buffer-shaped chunks. Token required.",
                "The retrieval third of the legacy retrieval/validation/update triple. A SERVER stream is "
                + "projectable and this is the form it takes: one request, and a response carrying the same "
                + "chunks the stream would have delivered, IN THE SAME ORDER. The body is NOT a flat "
                + "rowset - each chunk retains its own chunkIndex, its final flag, its own rowCount and the "
                + "running cumulativeRowCount, so a consumer reconstructs exactly what a gRPC consumer sees "
                + "and identifies the last chunk as the last rather than inferring it from the collection "
                + "ending. Each row names the buffer it came from and carries BOTH its current and its "
                + "original column values, because the update half's concurrency check compares original "
                + "values; a retrieval that discarded them would leave a caller unable to construct an "
                + "update at all. The Filter buffer's row order is INVERTED relative to the source - legacy "
                + "behaviour, preserved and stated rather than corrected, because a consumer re-sorting it "
                + "would produce wrong data that a row-count assertion would not catch.",
                SuccessDescription:
                "The retrieval succeeded. The body is the ordered sequence of chunks the gRPC server stream "
                + "would have delivered, with the chunking contract intact."),
            static (client, request, cancellationToken) => client.RetrieveAsync(request, cancellationToken));

        MapUnary<OpenValidationSessionRequest, OpenValidationSessionResponse>(
            group,
            new("/sessions", "openValidationSession", "OpenValidationSession", ContractSurface.DataWindow,
                "Open a validation session and receive its correlation identifier.",
                "Materializes the four pieces of cross-event mutable state the legacy holds as private "
                + "fields on the control [se_cst_dw.sru:L89-L96]: the disabled-event bitmask, the "
                + "inside-item-change flag, the inside-validation-error flag, and - most consequentially - "
                + "the item-changed return value STASHED for the validation-error event to consume. A "
                + "stateless request boundary has nowhere to put those, which is why the session is "
                + "explicit rather than implicit, and why Gateway holds none of it. Sessions must be "
                + "closed; an abandoned one holds server-side state until its configured idle expiry.",
                DeclaresNotFound: false),
            static (client, request, cancellationToken) =>
                client.OpenValidationSessionAsync(request, cancellationToken));

        MapSessionScoped<CloseValidationSessionRequest, CloseValidationSessionResponse>(
            group,
            new("/sessions/{" + SessionIdParameter + "}", "closeValidationSession",
                "CloseValidationSession", ContractSurface.DataWindow,
                "Close a validation session and release its cross-event state.",
                "Releases the session opened above. The 404 this operation declares is the projection of an "
                + "upstream NotFound and is never synthesized from a response field - see adjudication A5 "
                + "in this file's header.",
                BadRequest: BadRequestDeclaration.None),
            HttpMethods.Delete,
            static sessionId => new CloseValidationSessionRequest { SessionId = sessionId },
            static (client, request, cancellationToken) =>
                client.CloseValidationSessionAsync(request, cancellationToken));

        MapUnary<UpdateRequest, UpdateResponse>(
            group,
            new("/update", "updateDataWindow", "Update", ContractSurface.DataWindow,
                "Apply buffered changes. Returns 409 on an optimistic-concurrency conflict.",
                "Applies the buffered insert, update and delete set through C-03, which in turn reaches "
                + "C-06 on Persistence. THE 409 IS THE SUBSTANTIVE PART: the sole updatable DataWindow in "
                + "the legacy estate carries updatewhere=1 with all six columns marked updatewhereclause=yes "
                + "[dw_sqlite.srd:L8-L14], so the concurrency check spans ALL SIX COLUMNS' ORIGINAL VALUES. "
                + "On a mismatch the upstream returns gRPC Aborted and this operation returns HTTP 409 with "
                + "the conflict detail carrying the current row state, unchanged. Aborted is chosen over "
                + "FailedPrecondition deliberately: the operation may succeed if retried at a higher level, "
                + "which is what the status is defined to mean. Callers implement an explicit "
                + "retry-or-surface policy; there is no silent overwrite anywhere in the system, and a "
                + "caller that re-sends the same payload receives the same 409 because the original values "
                + "it carries are still stale. On success the response carries inserted, updated and "
                + "deleted counts plus the identity column and the identity value arrays that reproduce the "
                + "legacy identity round trip [n_cst_thread_task_sqlupdate.sru:L215-L245].",
                DeclaresConflict: true),
            static (client, request, cancellationToken) => client.UpdateAsync(request, cancellationToken));

        MapSessionScoped<GetEventGateRequest, GetEventGateResponse>(
            group,
            new("/event-gate", "getEventGate", "GetEventGate", ContractSurface.DataWindow,
                "Read the event-gate bitmask.",
                "Reports which of the three gated events are currently disabled. The bitmask is "
                + "EID_ROWFOCUSCHANGE = 1, EID_ITEMFOCUSCHANGE = 2 and EID_ITEMCHANGE = 4 "
                + "[se_cst_dw.sru:L41-L43], and the identifiers are composable. ONE COUPLING MUST BE "
                + "CARRIED AND IS EASY TO MISS: disabling item-change also SUPPRESSES COLUMN-EXPRESSION "
                + "EVALUATION [se_cst_dw.sru:L109-L111], so a caller that disables item change to stop a "
                + "validation cascade also silently stops every computed column from recalculating. That is "
                + "legacy behaviour, reproduced rather than corrected.",
                BadRequest: BadRequestDeclaration.None,
                SuccessDescription:
                "The current event gate. The bitmask and its decomposed bits are carried exactly as the "
                + "protobuf message names them, so the legacy EID_* identifiers are part of the published "
                + "body rather than only of its prose."),
            HttpMethods.Get,
            static sessionId => new GetEventGateRequest { SessionId = sessionId },
            static (client, request, cancellationToken) =>
                client.GetEventGateAsync(request, cancellationToken));

        MapUnary<DisableEventRequest, DisableEventResponse>(
            group,
            new("/event-gate/disable", "disableEvent", "DisableEvent", ContractSurface.DataWindow,
                "Disable one or more gated events.",
                "Sets bits in the event gate. Disabling EID_ITEMCHANGE also suppresses column-expression "
                + "evaluation - see the event-gate read. A zero mask is rejected by the upstream with the "
                + "legacy E_INVALID_ARGUMENT.",
                SuccessDescription:
                "The events were disabled. The body returns the RESULTING gate, so a caller never infers "
                + "the new mask from the one it sent - which matters because disabling EID_ITEMCHANGE also "
                + "suppresses column-expression evaluation."),
            static (client, request, cancellationToken) =>
                client.DisableEventAsync(request, cancellationToken));

        MapUnary<EnableEventRequest, EnableEventResponse>(
            group,
            new("/event-gate/enable", "enableEvent", "EnableEvent", ContractSurface.DataWindow,
                "Re-enable one or more gated events.",
                "Clears bits in the event gate, restoring both the named events and - when EID_ITEMCHANGE "
                + "is among them - column-expression evaluation. The legacy declares this member's return "
                + "type differently from its disable counterpart; that asymmetry is annotated by the "
                + "protocol contract rather than harmonised.",
                SuccessDescription:
                "The events were re-enabled. The body returns the resulting gate for the same reason the "
                + "disable projection does."),
            static (client, request, cancellationToken) =>
                client.EnableEventAsync(request, cancellationToken));

        // ----------------------------------------------------------------------------------------------
        //  THE FOUR HEADLESS MODELS, READ AND APPLY
        //
        //  DropDownSearch, ColumnSort, ContextMenu and RowSelect each split into a headless half that
        //  ships and a rendering half that does not. The eight operations below are the headless half, and
        //  EVERY ONE IS HEADLESS WITHOUT EXCEPTION: no geometry, no DPI conversion, no font metric, no
        //  pixel coordinate, no window handle and no IME state crosses this boundary. The rendering halves
        //  are deferred, and nothing in this file implements any part of one (C-D).
        //
        //  All eight are POST, INCLUDING THE FOUR READS, because each request is a STRUCTURED SELECTOR
        //  keyed on a DataWindow handle rather than a session id - and a structured selector does not
        //  survive a query string without inventing an encoding this contract would then have to specify.
        // ----------------------------------------------------------------------------------------------

        MapUnary<GetDropDownSearchStateRequest, GetDropDownSearchStateResponse>(
            group,
            new("/drop-down-search/state", "getDropDownSearchState", "GetDropDownSearchState",
                ContractSurface.DataWindow,
                "Read the drop-down search state, including the generated filter expression.",
                "Reports the search state for one column: the terms currently applied, the row and filtered "
                + "counts, and the generated filter expression. THE FILTER EXPRESSION IS COMPATIBILITY AND "
                + "DIAGNOSTIC DATA AND IS NOT SOMETHING A RECEIVER EXECUTES - the legacy interpolates "
                + "user-typed search data into it unescaped [n_cst_dwsvc_dropdownsearch.sru:L323], and the "
                + "field is published so a caller can compare it against the behavioural oracle byte for "
                + "byte. It is a recorded legacy defect, not a template to evaluate. Window positioning and "
                + "IME text input are the deferred half and are not reachable here."),
            static (client, request, cancellationToken) =>
                client.GetDropDownSearchStateAsync(request, cancellationToken));

        MapUnary<ApplyDropDownSearchRequest, ApplyDropDownSearchResponse>(
            group,
            new("/drop-down-search/apply", "applyDropDownSearch", "ApplyDropDownSearch",
                ContractSurface.DataWindow,
                "Apply or clear the drop-down search filter.",
                "Applies the filter, or clears it when the clear flag is set, and carries the filter-type "
                + "and show-filtered-rows settings. The search terms are sent AS TYPED: DataServices "
                + "performs the legacy's own transformations server-side so the generated expression matches "
                + "the oracle byte for byte without the caller having to build filter syntax - which is also "
                + "what keeps the unescaped-interpolation defect confined to one implementation instead of "
                + "every client."),
            static (client, request, cancellationToken) =>
                client.ApplyDropDownSearchAsync(request, cancellationToken));

        MapUnary<GetColumnSortStateRequest, GetColumnSortStateResponse>(
            group,
            new("/column-sort/state", "getColumnSortState", "GetColumnSortState",
                ContractSurface.DataWindow,
                "Read the column-sort state and its generated sort expression.",
                "The headless half of the column-sort service: the sort state and the sort expression it "
                + "constructs. The sort-indicator geometry is the deferred half - it reaches the legacy DPI "
                + "conversion family - and no part of it is reachable here."),
            static (client, request, cancellationToken) =>
                client.GetColumnSortStateAsync(request, cancellationToken));

        MapUnary<ApplyColumnSortRequest, ApplyColumnSortResponse>(
            group,
            new("/column-sort/apply", "applyColumnSort", "ApplyColumnSort", ContractSurface.DataWindow,
                "Apply a column sort.",
                "Applies the sort the request describes and returns the resulting state, so a caller never "
                + "infers the outcome from the request it sent."),
            static (client, request, cancellationToken) =>
                client.ApplyColumnSortAsync(request, cancellationToken));

        MapUnary<GetContextMenuModelRequest, GetContextMenuModelResponse>(
            group,
            new("/context-menu/state", "getContextMenuModel", "GetContextMenuModel",
                ContractSurface.DataWindow,
                "Read the context-menu item model.",
                "The headless half of the context-menu service: THE ITEM MODEL ONLY - labels, identifiers, "
                + "enabled and split flags, and computed LOGICAL text widths. Those are DATA. Font "
                + "measurement, DPI-to-pixel conversion, window geometry and the rendering itself are the "
                + "deferred half, and nothing here performs or exposes any of them. The request carries a "
                + "row and a DataWindow-object reference in addition to the handle, which is why this read "
                + "is a POST."),
            static (client, request, cancellationToken) =>
                client.GetContextMenuModelAsync(request, cancellationToken));

        MapUnary<ApplyContextMenuModelRequest, ApplyContextMenuModelResponse>(
            group,
            new("/context-menu/apply", "applyContextMenuModel", "ApplyContextMenuModel",
                ContractSurface.DataWindow,
                "Apply a context-menu item model.",
                "Applies the item model the request carries. Item data only, on the same terms as the read: "
                + "no geometry, no font metric and no rendering instruction crosses this boundary."),
            static (client, request, cancellationToken) =>
                client.ApplyContextMenuModelAsync(request, cancellationToken));

        MapUnary<GetRowSelectStateRequest, GetRowSelectStateResponse>(
            group,
            new("/row-select/state", "getRowSelectState", "GetRowSelectState", ContractSurface.DataWindow,
                "Read the row-selection state machine.",
                "The headless half of the row-selection service: the selection state machine and its "
                + "current selection. Its single legacy dialog becomes a structured error result on the "
                + "upstream side; only the delivery channel changed."),
            static (client, request, cancellationToken) =>
                client.GetRowSelectStateAsync(request, cancellationToken));

        MapUnary<ApplyRowSelectStyleRequest, ApplyRowSelectStyleResponse>(
            group,
            new("/row-select/style", "applyRowSelectStyle", "ApplyRowSelectStyle",
                ContractSurface.DataWindow,
                "Apply a row-selection style.",
                "Applies the selection style the request names and returns the resulting state. The style is "
                + "a selection MODE - which rows the state machine may hold selected - and not a visual "
                + "treatment; no colour, no font and no geometry is carried."),
            static (client, request, cancellationToken) =>
                client.ApplyRowSelectStyleAsync(request, cancellationToken));
    }


    /// <summary>
    /// Declares C-04's twenty-four projected operations: the paired expression session, the expression,
    /// variable, foreign-variable, relative-column and flag surface, the four calculation entry points,
    /// the two gates, the two state reads and the event stream.
    /// </summary>
    /// <param name="group">The <c>/v1/datawindow/expression</c> group.</param>
    /// <remarks>
    /// C-04 declares twenty-six methods. The two exclusions are <c>InvokeMethodChannel</c> and
    /// <c>TraceChannel</c>, both of which are bidirectional AND INVERTED - DataServices calls back into its
    /// client, because the legacy expects the application to implement the macro switch. An inverted stream
    /// has no request/response direction to project, so nothing here projects one.
    /// </remarks>
    private static void MapColumnExpressionOperations(RouteGroupBuilder group)
    {
        MapUnary<OpenExpressionSessionRequest, OpenExpressionSessionResponse>(
            group,
            new("/sessions", "openExpressionSession", "OpenExpressionSession",
                ContractSurface.ColumnExpression,
                "Open an expression session and receive its correlation identifier.",
                "Scopes the DataWindow handles that cross-DataWindow variable references may resolve "
                + "against. THIS SESSION IS THE REFACTOR'S ONE DELIBERATELY NARROWED CONTRACT: the legacy "
                + "holds a LIVE IN-PROCESS POINTER to another DataWindow's expression service, a pointer "
                + "cannot be serialized, so a foreign reference is supported only while both DataWindows are "
                + "co-resident in ONE session inside ONE DataServices instance. A reference spanning "
                + "sessions or instances is BLOCKED with a defined error rather than approximated.",
                DeclaresNotFound: false),
            static (client, request, cancellationToken) =>
                client.OpenExpressionSessionAsync(request, cancellationToken));

        MapSessionScoped<CloseExpressionSessionRequest, CloseExpressionSessionResponse>(
            group,
            new("/sessions/{" + SessionIdParameter + "}", "closeExpressionSession",
                "CloseExpressionSession", ContractSurface.ColumnExpression,
                "Close an expression session and release its scoped DataWindow handles.",
                "Releases the session opened above, and with it every cross-DataWindow handle scoped to it. "
                + "The 404 is the projection of an upstream NotFound and is never synthesized from a "
                + "response field.",
                BadRequest: BadRequestDeclaration.None),
            HttpMethods.Delete,
            static sessionId => new CloseExpressionSessionRequest { SessionId = sessionId },
            static (client, request, cancellationToken) =>
                client.CloseExpressionSessionAsync(request, cancellationToken));

        MapUnary<AddExpressionRequest, AddExpressionResponse>(
            group,
            new("/expressions/add", "addExpression", "AddExpression", ContractSurface.ColumnExpression,
                "Add a column expression.",
                "The legacy member returns THE NEW EXPRESSION'S INDEX rather than a return code, which is "
                + "why the response carries an index field distinct from retCode. A caller must read the "
                + "index and must not treat it as an outcome under the return-code algebra."),
            static (client, request, cancellationToken) =>
                client.AddExpressionAsync(request, cancellationToken));

        MapUnary<SetExpressionRequest, SetExpressionResponse>(
            group,
            new("/expressions/set", "setExpression", "SetExpression", ContractSurface.ColumnExpression,
                "Set a column expression, binding its variable references.",
                "THE EXPANSION MODE IS A PER-REFERENCE PROPERTY AND NEVER A PER-EXPRESSION ONE, and this is "
                + "the operation where that matters most. A STATIC reference is resolved and substituted "
                + "into the stored expression AT THE MOMENT IT IS SET, so the variable name is GONE from the "
                + "stored text and no later assignment can reach it; a DYNAMIC reference is retained as a "
                + "reference into the live variable table and is evaluated at calculation time, so later "
                + "mutation propagates. The payload therefore carries all three of the unexpanded source "
                + "expression, the bind-time snapshot and the live environment, plus the mode that applied "
                + "to each reference - transmitting an already-expanded string would make a static binding "
                + "indistinguishable from a literal and strip a dynamic one of its resolution environment."),
            static (client, request, cancellationToken) =>
                client.SetExpressionAsync(request, cancellationToken));

        MapUnary<GetExpressionRequest, GetExpressionResponse>(
            group,
            new("/expressions/get", "getExpression", "GetExpression", ContractSurface.ColumnExpression,
                "Read a column expression together with its bindings.",
                "Returns the UNEXPANDED source expression alongside its reference set, so a caller sees what "
                + "was bound rather than only what a calculation would produce. A POST because the selector "
                + "is structured."),
            static (client, request, cancellationToken) =>
                client.GetExpressionAsync(request, cancellationToken));

        MapUnary<RemoveExpressionRequest, RemoveExpressionResponse>(
            group,
            new("/expressions/remove", "removeExpression", "RemoveExpression",
                ContractSurface.ColumnExpression,
                "Remove one column expression.",
                "Removes the expression the selector names, by index or by column name as the request "
                + "chooses. The reverse dependency index is maintained by the engine, not by this "
                + "projection."),
            static (client, request, cancellationToken) =>
                client.RemoveExpressionAsync(request, cancellationToken));

        MapUnary<RemoveAllExpressionsRequest, RemoveAllExpressionsResponse>(
            group,
            new("/expressions/remove-all", "removeAllExpressions", "RemoveAllExpressions",
                ContractSurface.ColumnExpression,
                "Remove every column expression.",
                "Clears the whole expression table for the target DataWindow in one call."),
            static (client, request, cancellationToken) =>
                client.RemoveAllExpressionsAsync(request, cancellationToken));

        MapUnary<AddVariableRequest, AddVariableResponse>(
            group,
            new("/variables/add", "addVariable", "AddVariable", ContractSurface.ColumnExpression,
                "Declare an expression variable of one of the seven scalar types.",
                "The legacy declares SEVEN TYPED OVERLOADS - time, string, long, double, datetime, date and "
                + "boolean - so the variable value travels as a DISCRIMINATED UNION rather than a "
                + "stringly-typed map. Collapsing it to strings would lose exactly the type information the "
                + "engine's coercion behaviour depends on. A duplicate declaration is rejected by the "
                + "upstream."),
            static (client, request, cancellationToken) =>
                client.AddVariableAsync(request, cancellationToken));

        MapUnary<SetVariableRequest, SetVariableResponse>(
            group,
            new("/variables/set", "setVariable", "SetVariable", ContractSurface.ColumnExpression,
                "Assign an expression variable, optionally recalculating.",
                "The legacy publishes each of the seven types in three arities - value; value plus "
                + "recalculate; value plus recalculate plus force - and all three travel as explicit fields "
                + "rather than as separate operations. Assigning a variable that only STATIC references "
                + "bound changes nothing a later calculation can observe, which is the documented "
                + "consequence of static expansion and not a defect."),
            static (client, request, cancellationToken) =>
                client.SetVariableAsync(request, cancellationToken));

        MapUnary<AddVariableExpressionRequest, AddVariableExpressionResponse>(
            group,
            new("/variable-expressions/add", "addVariableExpression", "AddVariableExpression",
                ContractSurface.ColumnExpression,
                "Declare a variable whose value is itself an expression.",
                "A variable's value may itself be an expression carrying its own variable and function "
                + "references, so the local-variable shape is RECURSIVE. The engine owns the recursion "
                + "guard and its stack; this projection carries the declaration and nothing more."),
            static (client, request, cancellationToken) =>
                client.AddVariableExpressionAsync(request, cancellationToken));

        MapUnary<SetVariableExpressionRequest, SetVariableExpressionResponse>(
            group,
            new("/variable-expressions/set", "setVariableExpression", "SetVariableExpression",
                ContractSurface.ColumnExpression,
                "Set the expression backing a variable.",
                "Re-binds the expression a variable evaluates, on the same per-reference expansion terms as "
                + "setting a column expression."),
            static (client, request, cancellationToken) =>
                client.SetVariableExpressionAsync(request, cancellationToken));

        MapUnary<GetVariableExpressionRequest, GetVariableExpressionResponse>(
            group,
            new("/variable-expressions/get", "getVariableExpression", "GetVariableExpression",
                ContractSurface.ColumnExpression,
                "Read the expression backing a variable.",
                "Returns the variable's unexpanded expression and its reference set. A POST because the "
                + "selector is structured."),
            static (client, request, cancellationToken) =>
                client.GetVariableExpressionAsync(request, cancellationToken));

        MapUnary<AddForeignVariableRequest, AddForeignVariableResponse>(
            group,
            new("/foreign-variables/add", "addForeignVariable", "AddForeignVariable",
                ContractSurface.ColumnExpression,
                "Declare a cross-DataWindow variable within one expression session.",
                "A foreign variable resolves against ANOTHER DataWindow's expression service. The legacy "
                + "notes that adding one REQUIRES dynamic expansion, and the legacy mechanism is a live "
                + "in-process pointer. Across a network that pointer cannot exist, so the reference is "
                + "resolved by a SESSION-SCOPED DataWindow handle and is supported only while both "
                + "DataWindows are co-resident in one expression session inside one DataServices instance. "
                + "A reference spanning sessions or instances is BLOCKED and returns a defined error rather "
                + "than a silently wrong value; a caller receiving it must NOT retry, because the topology "
                + "rejected it and not the request.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (client, request, cancellationToken) =>
                client.AddForeignVariableAsync(request, cancellationToken));

        MapUnary<SetRelativeColumnsRequest, SetRelativeColumnsResponse>(
            group,
            new("/relative-columns/set", "setRelativeColumns", "SetRelativeColumns",
                ContractSurface.ColumnExpression,
                "Set the relative or relative-input columns an expression depends on.",
                "The legacy publishes these setters in BOTH singular and plural array forms and BOTH by "
                + "index and by name; the contract carries all four shapes as fields of one request rather "
                + "than as four operations. They populate the REVERSE DEPENDENCY INDEX - the graph that "
                + "makes dirty propagation work - so a wrong entry changes which columns recalculate."),
            static (client, request, cancellationToken) =>
                client.SetRelativeColumnsAsync(request, cancellationToken));

        MapUnary<SetExpressionFlagRequest, SetExpressionFlagResponse>(
            group,
            new("/flags/set", "setExpressionFlag", "SetExpressionFlag", ContractSurface.ColumnExpression,
                "Set the always-calculate, recursive or trigger-event flag on an expression.",
                "The legacy publishes each flag by index and by name; the contract carries the selector and "
                + "the flag identity as fields. The flags govern when the engine recalculates and whether it "
                + "raises its own events, so they change observable behaviour rather than only "
                + "performance."),
            static (client, request, cancellationToken) =>
                client.SetExpressionFlagAsync(request, cancellationToken));

        MapUnary<CalcRequest, CalcResponse>(
            group,
            new("/calc", "calc", "Calc", ContractSurface.ColumnExpression,
                "Calculate one expression.",
                "The legacy publishes calculation in four arities; the contract carries the arguments as "
                + "fields. A cross-session foreign reference encountered during a calculation is BLOCKED "
                + "with a defined error rather than resolved to a guess.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (client, request, cancellationToken) => client.CalcAsync(request, cancellationToken));

        MapUnary<CalcAllRequest, CalcAllResponse>(
            group,
            new("/calc-all", "calcAll", "CalcAll", ContractSurface.ColumnExpression,
                "Calculate every expression.",
                "Recalculates the whole expression table. A cross-session foreign reference encountered "
                + "during the pass is BLOCKED with a defined error.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (client, request, cancellationToken) =>
                client.CalcAllAsync(request, cancellationToken));

        MapUnary<CalcEmptyRequest, CalcEmptyResponse>(
            group,
            new("/calc-empty", "calcEmpty", "CalcEmpty", ContractSurface.ColumnExpression,
                "Calculate only the expressions whose value is currently empty.",
                "A distinct legacy entry point rather than a filtered form of calculate-all, and it is "
                + "carried as one. A cross-session foreign reference is BLOCKED with a defined error.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (client, request, cancellationToken) =>
                client.CalcEmptyAsync(request, cancellationToken));

        MapUnary<CalcItemRequest, CalcItemResponse>(
            group,
            new("/calc-item", "calcItem", "CalcItem", ContractSurface.ColumnExpression,
                "Calculate one item.",
                "The legacy member is PUBLIC DESPITE ITS PRIVATE-CONVENTION UNDERSCORE PREFIX and returns a "
                + "BOOLEAN rather than a return code. Both anomalies are preserved: the protocol definition "
                + "carries the original spelling as descriptor metadata because a protobuf identifier cannot "
                + "begin with an underscore, and comparators must read that metadata rather than assume the "
                + "RPC name is the legacy name. A cross-session foreign reference is BLOCKED with a defined "
                + "error.",
                BadRequest: BadRequestDeclaration.CrossSessionReferenceBlocked),
            static (client, request, cancellationToken) =>
                client.CalcItemAsync(request, cancellationToken));

        MapUnary<SetEnabledRequest, SetEnabledResponse>(
            group,
            new("/enabled", "setExpressionServiceEnabled", "SetEnabled", ContractSurface.ColumnExpression,
                "Enable or disable the expression service.",
                "VETOABLE, which is why it answers with a return code rather than a boolean: a veto is a "
                + "distinct outcome from a failure, and under the preserved algebra a prevention reads as a "
                + "SUCCESS. A caller must branch on the specific value and never on a two-way success "
                + "test."),
            static (client, request, cancellationToken) =>
                client.SetEnabledAsync(request, cancellationToken));

        MapUnary<SetTraceRequest, SetTraceResponse>(
            group,
            new("/trace", "setExpressionTrace", "SetTrace", ContractSurface.ColumnExpression,
                "Enable or disable expression tracing.",
                "Not vetoable, unlike the enable gate beside it - the asymmetry is the legacy's and is "
                + "preserved. Tracing gates what the event stream emits."),
            static (client, request, cancellationToken) =>
                client.SetTraceAsync(request, cancellationToken));

        MapUnary<GetServiceStateRequest, GetServiceStateResponse>(
            group,
            new("/state", "getExpressionServiceState", "GetServiceState",
                ContractSurface.ColumnExpression,
                "Read the expression service's own state.",
                "Reports the service gates - whether the engine is enabled and whether tracing is on - "
                + "rather than the expression table. A POST because the selector is structured."),
            static (client, request, cancellationToken) =>
                client.GetServiceStateAsync(request, cancellationToken));

        MapUnary<GetExpressionStateRequest, GetExpressionStateResponse>(
            group,
            new("/engine-state", "getExpressionState", "GetExpressionState",
                ContractSurface.ColumnExpression,
                "Read the engine-state snapshot.",
                "The operation that makes the engine's seven-structure model reachable rather than merely "
                + "declared: the expression table, the REVERSE DEPENDENCY INDEX, the grammar sentinels and, "
                + "on request, the global variable table and the three-part static-versus-dynamic bindings. "
                + "READ-ONLY BY DESIGN - state is mutated through the typed operations above, never by "
                + "posting a snapshot back, and this projection offers no route that would."),
            static (client, request, cancellationToken) =>
                client.GetExpressionStateAsync(request, cancellationToken));

        MapServerStream<EventStreamRequest, EventStreamResponse>(
            group,
            new("/event-stream", "getExpressionEventStream", "EventStream",
                ContractSurface.ColumnExpression,
                "Read the expression engine's event stream as an ordered collection.",
                "A SERVER stream of the three events the engine declares on ITSELF - item-changed, "
                + "do-item-changed with its from-input flag, and var-changed with its force-calculate flag. "
                + "A server stream and not an inverted one: these are notifications the engine EMITS, not "
                + "questions it asks, which is why this one is projectable while the two inverted channels "
                + "are not. EVERY RECORD CARRIES ITS SEQUENCING TOKEN AND ITS DECLARED ORDERING DISCIPLINE, "
                + "AND SEQUENCE NUMBERS ARE FOR DETECTION ONLY - an out-of-order arrival is a hard error, "
                + "never a reorder opportunity, so a consumer must not sort, buffer-and-reorder, "
                + "de-duplicate or replay what it receives.",
                SuccessDescription:
                "The stream completed. The body is the ordered sequence of records the gRPC server stream "
                + "would have delivered, each retaining its sequencing token and its declared ordering "
                + "discipline."),
            static (client, request, cancellationToken) =>
                client.StreamExpressionEventsAsync(request, cancellationToken));
    }


    // ==================================================================================================
    //  REGISTRATION HELPERS
    //
    //  Three shapes, and exactly three, because the contract publishes exactly three: a body-bound unary
    //  POST, a session-scoped operation whose only argument is an identifier on the path or the query
    //  string, and a body-bound POST over a server stream. Driving every operation through one of the
    //  three is what makes the thirty-nine impossible to drift apart, and what makes diffing them against
    //  the contract a mechanical exercise.
    // ==================================================================================================

    /// <summary>
    /// Declares a body-bound unary projection.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message the body maps onto.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message the body maps back from.</typeparam>
    /// <param name="group">The group the route is declared on.</param>
    /// <param name="operation">The published metadata for the operation.</param>
    /// <param name="invoke">The single typed-client member this route projects.</param>
    /// <remarks>
    /// The handler's declared return type is <see cref="IResult"/> rather than a concrete result type, so
    /// no response is INFERRED into the generated document: every response this operation publishes is
    /// declared explicitly, which is what lets the declared set match the contract's own exactly.
    /// </remarks>
    private static void MapUnary<TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        Func<DataServicesClient, TRequest, CancellationToken, Task<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        // TYPED AS Func RATHER THAN LEFT AS A BARE LAMBDA, DELIBERATELY. A one-parameter lambda taking
        // HttpContext is convertible to RequestDelegate, whose MapPost overload DISCARDS the returned
        // value - the projected body would never reach the caller, and the analyzer that catches it
        // (ASP0016) is an error here rather than a warning. Naming the delegate type selects the route
        // handler overload instead, which writes the result.
        Func<HttpContext, Task<IResult>> handler =
            httpContext => ProjectUnaryAsync(httpContext, invoke);

        RouteHandlerBuilder route = group.MapPost(operation.Route, handler);

        route.Accepts<ProtoPayload>(MediaTypeNames.Application.Json);
        route.Produces<ProtoPayload>(StatusCodes.Status200OK, MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, operation);
    }

    /// <summary>
    /// Declares a projection whose only argument is a session identifier, taken from the path on a
    /// <c>DELETE</c> and from the query string on a <c>GET</c>.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message built from the identifier.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="group">The group the route is declared on.</param>
    /// <param name="operation">The published metadata for the operation.</param>
    /// <param name="httpMethod">The single HTTP method the contract declares for this route.</param>
    /// <param name="buildRequest">Builds the upstream request from the identifier, and nothing else.</param>
    /// <param name="invoke">The single typed-client member this route projects.</param>
    /// <remarks>
    /// <para>
    /// The identifier is passed through FAITHFULLY and Gateway holds no state keyed by it. The session it
    /// names lives inside DataServices, which is where the four pieces of cross-event mutable state must
    /// live because a stateless request boundary has nowhere to put them.
    /// </para>
    /// <para>
    /// The parameter is non-nullable, so a request omitting it is rejected by PARAMETER BINDING before the
    /// route runs. That rejection is deliberately not a declared response of the operation, for exactly
    /// the reason the contract gives for not declaring the cross-cutting <c>401</c> on every operation: a
    /// declared response set describes what the ROUTE produces, and neither authentication nor binding is
    /// something the route evaluates.
    /// </para>
    /// </remarks>
    private static void MapSessionScoped<TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        string httpMethod,
        Func<string, TRequest> buildRequest,
        Func<DataServicesClient, TRequest, CancellationToken, Task<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        RouteHandlerBuilder route = group.MapMethods(
            operation.Route,
            [httpMethod],
            (HttpContext httpContext, string sessionId) =>
                ProjectSessionScopedAsync(httpContext, buildRequest(sessionId), invoke));

        route.Produces<ProtoPayload>(StatusCodes.Status200OK, MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, operation);
    }

    /// <summary>
    /// Declares a body-bound projection over an upstream SERVER stream, published as an ordered
    /// collection.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message the body maps onto.</typeparam>
    /// <typeparam name="TResponse">The protobuf message each streamed element carries.</typeparam>
    /// <param name="group">The group the route is declared on.</param>
    /// <param name="operation">The published metadata for the operation.</param>
    /// <param name="invoke">The single typed-client member this route projects.</param>
    /// <remarks>
    /// <para>
    /// A server stream's ordering is the trivial one - the server produces a sequence and the client
    /// consumes it in order - so it has a faithful request/response projection and this is it. THE
    /// SEQUENCE IS MATERIALIZED AT THIS BOUNDARY AND ONLY AT THIS BOUNDARY: the elements are rendered in
    /// arrival order and each keeps every field the stream carried, so a consumer reconstructs exactly
    /// what a gRPC consumer sees, including which element was the last. A consumer that genuinely needs
    /// progressive delivery uses DataServices' gRPC surface, where the stream is not materialized at all.
    /// </para>
    /// <para>
    /// The element metadata is emphatically NOT flattened away. On a retrieval each chunk keeps its own
    /// index, its final marker and both its own and the cumulative row count; on the event stream each
    /// record keeps its sequencing token and its declared ordering discipline. Collapsing either into a
    /// flat list would discard the contract this operation exists to carry.
    /// </para>
    /// </remarks>
    private static void MapServerStream<TRequest, TResponse>(
        RouteGroupBuilder group,
        ProjectedOperation operation,
        Func<DataServicesClient, TRequest, CancellationToken, IAsyncEnumerable<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        ProjectedOperation streaming = operation with { ServerStreaming = true };

        // Typed as Func for the reason recorded on the unary helper: a bare one-parameter lambda would bind
        // to the RequestDelegate overload, which discards the projected body.
        Func<HttpContext, Task<IResult>> handler =
            httpContext => ProjectServerStreamAsync(httpContext, invoke);

        RouteHandlerBuilder route = group.MapPost(streaming.Route, handler);

        route.Accepts<ProtoPayload>(MediaTypeNames.Application.Json);
        route.Produces<IReadOnlyList<ProtoPayload>>(
            StatusCodes.Status200OK,
            MediaTypeNames.Application.Json);

        Describe<TRequest, TResponse>(route, streaming);
    }

    /// <summary>
    /// Applies the published metadata and the declared response surface an operation carries.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="route">The route being described.</param>
    /// <param name="operation">The published metadata for the operation.</param>
    /// <remarks>
    /// <para>
    /// The two protobuf message names come from the GENERATED DESCRIPTOR rather than from a literal, so
    /// <c>x-proto-request</c> and <c>x-proto-response</c> cannot drift from the protocol definition. A
    /// message is instantiated once, at startup, purely to read its descriptor; that is local reflection
    /// over generated metadata and not I/O, so it keeps startup free of any dependency on the upstream
    /// being reachable.
    /// </para>
    /// <para>
    /// Each status is declared only where the contract declares it. A status a generated client must
    /// branch on but the route does not produce hides the real surface, which is why <c>400</c>,
    /// <c>404</c> and <c>409</c> are conditional here rather than applied uniformly.
    /// </para>
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

        route.ProducesProblem(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(StatusCodes.Status403Forbidden, MediaTypeNames.Application.ProblemJson);

        if (operation.DeclaresNotFound)
        {
            route.ProducesProblem(StatusCodes.Status404NotFound, MediaTypeNames.Application.ProblemJson);
        }

        if (operation.DeclaresConflict)
        {
            route.ProducesProblem(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson);
        }

        route.ProducesProblem(
            StatusCodes.Status500InternalServerError,
            MediaTypeNames.Application.ProblemJson);
        route.ProducesProblem(StatusCodes.Status502BadGateway, MediaTypeNames.Application.ProblemJson);

        route.AddOpenApiOperationTransformer((openApiOperation, context, _) =>
            ApplyContractMetadataAsync(openApiOperation, context, operation, protoRequest, protoResponse));
    }


    // ==================================================================================================
    //  THE PROJECTION ITSELF
    // ==================================================================================================

    /// <summary>
    /// Binds the body, invokes the projected unary method, and renders its response.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="invoke">The typed-client member being projected.</param>
    /// <returns>The projected result.</returns>
    private static Task<IResult> ProjectUnaryAsync<TRequest, TResponse>(
        HttpContext httpContext,
        Func<DataServicesClient, TRequest, CancellationToken, Task<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
        => ProjectAsync(httpContext, async (client, cancellationToken) =>
        {
            string body = await ReadRequestBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);

            if (!TryBindRequest(body, out TRequest? request, out StatusProjection bindingFailure))
            {
                return RejectRequest(httpContext, bindingFailure);
            }

            TResponse response = await invoke(client, request, cancellationToken).ConfigureAwait(false);

            return Render(response);
        });

    /// <summary>
    /// Invokes a projected method whose only argument is a session identifier, and renders its response.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="request">The request built from the identifier, and from nothing else.</param>
    /// <param name="invoke">The typed-client member being projected.</param>
    /// <returns>The projected result.</returns>
    private static Task<IResult> ProjectSessionScopedAsync<TRequest, TResponse>(
        HttpContext httpContext,
        TRequest request,
        Func<DataServicesClient, TRequest, CancellationToken, Task<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
        => ProjectAsync(httpContext, async (client, cancellationToken) =>
            Render(await invoke(client, request, cancellationToken).ConfigureAwait(false)));

    /// <summary>
    /// Binds the body, consumes the projected server stream in arrival order, and renders it as an ordered
    /// collection.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf message each streamed element carries.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="invoke">The typed-client member being projected.</param>
    /// <returns>The projected result.</returns>
    private static Task<IResult> ProjectServerStreamAsync<TRequest, TResponse>(
        HttpContext httpContext,
        Func<DataServicesClient, TRequest, CancellationToken, IAsyncEnumerable<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
        => ProjectAsync(httpContext, async (client, cancellationToken) =>
        {
            string body = await ReadRequestBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);

            if (!TryBindRequest(body, out TRequest? request, out StatusProjection bindingFailure))
            {
                return RejectRequest(httpContext, bindingFailure);
            }

            return await RenderStreamAsync(invoke(client, request, cancellationToken))
                .ConfigureAwait(false);
        });

    /// <summary>
    /// Runs one projection under the single failure-translation path every route in this file shares.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="project">The projection: bind, invoke, render.</param>
    /// <returns>Either the projected success or the translated failure.</returns>
    /// <remarks>
    /// <para>
    /// ONE FAILURE PATH FOR ALL THIRTY-NINE ROUTES, deliberately. A per-route translation would let one
    /// route classify a status differently from its neighbour, and the status map is the substantive part
    /// of this projection - the place where a divergence would be least visible and most damaging.
    /// </para>
    /// <para>
    /// The conflict arm is first because it is the specific case: only the update projection can raise it,
    /// and it is caught by its own type rather than by inspecting a status, so the payload the typed client
    /// decoded reaches the caller without this file re-deriving anything. NOTHING HERE RETRIES. A conflict
    /// is a definitive answer rather than a transient fault, and re-issuing the same request would produce
    /// the same conflict while risking exactly the silent overwrite the contract forbids.
    /// </para>
    /// <para>
    /// The cancellation arm answers the case where the CALLER has gone. No response can be delivered on a
    /// connection that no longer exists, and fabricating a 5xx would record a fault that did not occur, so
    /// the request is completed without a body. Cancellation raised by the upstream instead arrives as a
    /// gRPC status and is translated by the map like any other.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ProjectAsync(
        HttpContext httpContext,
        Func<DataServicesClient, CancellationToken, Task<IResult>> project)
    {
        DataServicesClient client = httpContext.RequestServices.GetRequiredService<DataServicesClient>();

        try
        {
            return await project(client, httpContext.RequestAborted).ConfigureAwait(false);
        }
        catch (DataServicesConflictException conflict)
        {
            return ProjectConflict(httpContext, conflict);
        }
        catch (RpcException failure)
        {
            return ProjectFailure(httpContext, ProjectStatus(failure), failure);
        }
        catch (HttpRequestException transport)
        {
            // Adjudication A2: a transport failure that escapes as itself rather than as a gRPC status is
            // the clearest instance of "no gRPC response arrived at all", which is the case the published
            // status table cannot describe and the case 502 exists for.
            return ProjectFailure(
                httpContext,
                new StatusProjection(
                    StatusCodes.Status502BadGateway,
                    RetCode.E_RETRY,
                    UpstreamUnavailableDetail,
                    FromUpstream: true),
                transport);
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }
    }

    /// <summary>
    /// Reads the request body as text, without interpreting it.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects.</param>
    /// <returns>The body, or an empty string when none was sent.</returns>
    /// <remarks>
    /// Read as TEXT and parsed once, by the protobuf JSON parser, rather than deserialized into an
    /// intermediate object and re-serialized. A round trip through a second representation is a second
    /// chance to lose a value - notably a 64-bit integer sent as a JSON number, which the protobuf mapping
    /// permits and which a naive intermediate can silently narrow.
    /// <para>
    /// A byte-order mark is consumed rather than passed through, because a client that emits one is
    /// otherwise rejected by the parser for a reason that has nothing to do with its payload. No size
    /// limit is imposed here: the host's own request-body limit governs, and inventing a second one would
    /// be a behaviour this contract does not define.
    /// </para>
    /// </remarks>
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
    /// Binds a request body onto the operation's protobuf request message, strictly.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <param name="body">The body as sent.</param>
    /// <param name="request">The bound message when binding succeeded.</param>
    /// <param name="failure">The projection to answer with when it did not.</param>
    /// <returns><see langword="true"/> when the body bound.</returns>
    /// <remarks>
    /// See adjudication A7. An unrecognised member is REJECTED rather than discarded, because silently
    /// dropping a value a caller believed it had sent is silent data loss - and on an update path that is
    /// the class of defect this refactor exists to avoid. The parser's own diagnostic never reaches the
    /// caller or the log, because it quotes the offending body and a body is caller content that may carry
    /// anything, credentials included (C-F).
    /// </remarks>
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
        catch (InvalidProtocolBufferException)
        {
            // InvalidJsonException derives from this type, so one arm covers both a body that is not JSON
            // at all and a body that is JSON but does not map onto the message.
            request = null;
            failure = BindingRejection(MalformedBodyDetail);

            return false;
        }
    }

    /// <summary>
    /// Builds the projection for a body Gateway itself rejected.
    /// </summary>
    /// <param name="detail">The fixed prose describing the rejection.</param>
    /// <returns>The projection.</returns>
    /// <remarks>
    /// <c>FromUpstream</c> is false, and that is a real statement rather than a default: the contract says
    /// the <c>upstream</c> member is absent when Gateway itself produced the response, and no upstream was
    /// reached on this path.
    /// </remarks>
    private static StatusProjection BindingRejection(string detail) => new(
        StatusCodes.Status400BadRequest,
        RetCode.E_INVALID_ARGUMENT,
        detail,
        FromUpstream: false);

    /// <summary>
    /// Renders one protobuf message as the canonical JSON mapping of its type.
    /// </summary>
    /// <param name="response">The message the upstream produced.</param>
    /// <returns><c>200</c> carrying the rendered message.</returns>
    /// <remarks>
    /// The message is forwarded, not reshaped: no member is renamed, dropped, reordered into a different
    /// envelope, or reinterpreted. The four-value item-change alphabet, the tri-valued veto and the buffer
    /// discriminator therefore reach the caller exactly as the upstream produced them - this projection
    /// translates transport, not semantics.
    /// </remarks>
    private static IResult Render(IMessage response) => Results.Text(
        ResponseFormatter.Format(response),
        MediaTypeNames.Application.Json);

    /// <summary>
    /// Renders a projected server stream as a JSON array, in arrival order.
    /// </summary>
    /// <typeparam name="TResponse">The protobuf message each element carries.</typeparam>
    /// <param name="elements">The upstream stream.</param>
    /// <returns><c>200</c> carrying the rendered collection.</returns>
    /// <remarks>
    /// The array is assembled from each element's own rendering, so no element is merged with another and
    /// none of the per-element metadata the chunking and sequencing contracts depend on is flattened away.
    /// Order is arrival order and is never sorted.
    /// </remarks>
    private static async Task<IResult> RenderStreamAsync<TResponse>(IAsyncEnumerable<TResponse> elements)
        where TResponse : class, IMessage, new()
    {
        StringBuilder rendered = new();
        rendered.Append('[');

        bool first = true;

        await foreach (TResponse element in elements.ConfigureAwait(false))
        {
            if (!first)
            {
                rendered.Append(',');
            }

            first = false;
            rendered.Append(ResponseFormatter.Format(element));
        }

        rendered.Append(']');

        return Results.Text(rendered.ToString(), MediaTypeNames.Application.Json);
    }


    // ==================================================================================================
    //  THE STATUS MAP - THE SUBSTANTIVE PART OF THE PROJECTION
    // ==================================================================================================

    /// <summary>
    /// Translates one gRPC failure into the HTTP status, the legacy return code and the fixed prose the
    /// published mapping assigns it.
    /// </summary>
    /// <param name="failure">The failure the typed client surfaced.</param>
    /// <returns>The projection to answer with.</returns>
    /// <remarks>
    /// <para>
    /// Every arm whose gRPC status appears in the published table follows the table. Every arm whose status
    /// does not appear there - <see cref="StatusCode.FailedPrecondition"/>,
    /// <see cref="StatusCode.OutOfRange"/>, <see cref="StatusCode.AlreadyExists"/>,
    /// <see cref="StatusCode.ResourceExhausted"/>, <see cref="StatusCode.Cancelled"/> and
    /// <see cref="StatusCode.DataLoss"/> - follows the canonical gRPC-to-HTTP mapping, and says so on the
    /// arm. No status outside those two sources is invented, and no distinguishable failure is collapsed
    /// into <c>500</c> for convenience.
    /// </para>
    /// <para>
    /// The legacy return code on each arm is consumed as a SYMBOL from the single transcription of the
    /// oracle's catalogue. No numeric literal from that catalogue appears anywhere in this file, and no
    /// member of it is redeclared here.
    /// </para>
    /// </remarks>
    private static StatusProjection ProjectStatus(RpcException failure)
    {
        // ADJUDICATION A2, IMPLEMENTED RATHER THAN PARAPHRASED. Both published mappings assign
        // `Unavailable` to 503 AND state that an unreachable upstream or an exhausted retry becomes 502 -
        // "the one case the table cannot describe, because it is the case where no gRPC response arrived at
        // all". Those are two different events wearing one status code, and the transport exception on the
        // status is what tells them apart: a status the CLIENT synthesized from a failed transport carries
        // one, and a status the SERVER genuinely answered does not. The first never reached DataServices at
        // all, so it is the 502; the second is DataServices' own answer, so it is the 503.
        if (failure.StatusCode == StatusCode.Unavailable && failure.Status.DebugException is not null)
        {
            return new(
                StatusCodes.Status502BadGateway,
                RetCode.E_RETRY,
                UpstreamUnavailableDetail,
                FromUpstream: true);
        }

        return failure.StatusCode switch
        {
            // ---- Published table ----
            StatusCode.InvalidArgument => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_INVALID_ARGUMENT,
                InvalidArgumentDetail,
                FromUpstream: true),

            StatusCode.Unauthenticated => new(
                StatusCodes.Status401Unauthorized,
                RetCode.E_ACCESS_DENIED,
                UnauthenticatedDetail,
                FromUpstream: true),

            StatusCode.PermissionDenied => new(
                StatusCodes.Status403Forbidden,
                RetCode.E_ACCESS_DENIED,
                PermissionDeniedDetail,
                FromUpstream: true),

            StatusCode.NotFound => new(
                StatusCodes.Status404NotFound,
                RetCode.E_OBJECT_NOT_FOUND,
                NotFoundDetail,
                FromUpstream: true),

            // The conflict WITHOUT a decodable detail - adjudication A4. The status is preserved because
            // reporting anything else would let a rejected update look successful, and no conflict member
            // is fabricated because an empty one would describe a conflict no caller could act on. A
            // conflict WITH its detail never reaches here: the typed client raises it by its own type.
            StatusCode.Aborted => new(
                StatusCodes.Status409Conflict,
                RetCode.E_RETRY,
                ConflictWithoutDetailProse,
                FromUpstream: true,
                ConflictWithoutDetailProblemType,
                ConflictWithoutDetailProblemTitle),

            // Adjudication A3. A plain 501 problem document: no reserved marker, and no deferred service is
            // named or reachable from this file.
            StatusCode.Unimplemented => new(
                StatusCodes.Status501NotImplemented,
                RetCode.E_NO_IMPLEMENTATION,
                UnimplementedDetail,
                FromUpstream: true),

            StatusCode.Unavailable => new(
                StatusCodes.Status503ServiceUnavailable,
                RetCode.E_RETRY,
                UnavailableDetail,
                FromUpstream: true),

            StatusCode.DeadlineExceeded => new(
                StatusCodes.Status504GatewayTimeout,
                RetCode.E_TIME_OUT,
                DeadlineExceededDetail,
                FromUpstream: true),

            StatusCode.Internal => new(
                StatusCodes.Status500InternalServerError,
                RetCode.E_INTERNAL_ERROR,
                InternalErrorDetail,
                FromUpstream: true),

            StatusCode.Unknown => new(
                StatusCodes.Status500InternalServerError,
                RetCode.UNKNOWN,
                InternalErrorDetail,
                FromUpstream: true),

            // ---- Canonical mapping, where the published table is silent ----

            // A state rejection rather than an argument rejection, and the legacy catalogue has no
            // precondition member, so it carries the same code the upstream's own validation path would
            // have produced. The distinct prose is what tells the two apart.
            StatusCode.FailedPrecondition => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_INVALID_ARGUMENT,
                FailedPreconditionDetail,
                FromUpstream: true),

            StatusCode.OutOfRange => new(
                StatusCodes.Status400BadRequest,
                RetCode.E_OUT_OF_RANGE,
                OutOfRangeDetail,
                FromUpstream: true),

            // The OTHER canonical 409, and adjudication A4 requires it be distinguishable from the
            // concurrency conflict: a different problem type, a different title, a different return code
            // and no conflict member. The legacy catalogue has no "already exists" member, and a duplicate
            // definition is rejected through the engine's own argument-validation path, so
            // E_INVALID_ARGUMENT is the closest member of a closed catalogue - and it is not the code
            // either conflict arm carries, which is what keeps the three readable apart.
            StatusCode.AlreadyExists => new(
                StatusCodes.Status409Conflict,
                RetCode.E_INVALID_ARGUMENT,
                AlreadyExistsDetail,
                FromUpstream: true,
                AlreadyExistsProblemType,
                AlreadyExistsProblemTitle),

            StatusCode.ResourceExhausted => new(
                StatusCodes.Status429TooManyRequests,
                RetCode.E_BUSY,
                ResourceExhaustedDetail,
                FromUpstream: true),

            // A cancellation the UPSTREAM reported. A caller-initiated cancellation never reaches the map:
            // it is answered without a body, because the connection to answer on is gone. The legacy code
            // is CANCELLED, which under the preserved algebra is NEITHER succeeded nor failed - a caller
            // must therefore not read it as either.
            StatusCode.Cancelled => new(
                StatusCodes.Status502BadGateway,
                RetCode.CANCELLED,
                CancelledDetail,
                FromUpstream: true),

            StatusCode.DataLoss => new(
                StatusCodes.Status500InternalServerError,
                RetCode.E_DB_ERROR,
                DataLossDetail,
                FromUpstream: true),

            // Anything else, including an RpcException carrying OK, which is a fault in the calling layer
            // rather than an upstream outcome. UNKNOWN exists in the oracle's catalogue precisely for the
            // unclassifiable case, so no value outside the closed set is needed.
            _ => new(
                StatusCodes.Status500InternalServerError,
                RetCode.UNKNOWN,
                UnclassifiedFailureDetail,
                FromUpstream: true),
        };
    }

    /// <summary>
    /// Projects an optimistic-concurrency conflict as <c>409</c> carrying the upstream's own payload.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="conflict">The conflict the typed client decoded and raised.</param>
    /// <returns><c>409</c> carrying the conflict detail, unchanged.</returns>
    /// <remarks>
    /// <para>
    /// THE PAYLOAD IS FORWARDED, NOT SUMMARISED. It is rendered once, through the same canonical mapping
    /// every success body uses, and attached whole. Every failing row therefore reaches the caller with its
    /// buffer, its one-based ordinal, its current item status, and BOTH its current and its original column
    /// values - and it is comparing those two sets that identifies the column which moved. Truncating it,
    /// flattening it to a boolean, or replacing it with a message string would leave the caller a choice
    /// between a blind overwrite and a spurious failure.
    /// </para>
    /// <para>
    /// The return code is the one the upstream's trailer carried, and it is admitted only if the PRESERVED
    /// TRI-STATE ALGEBRA classifies it as a failure. That test is deliberately not the negation of a
    /// success test: <c>PREVENT</c> reads as a SUCCESS and <c>CANCELLED</c> is NEITHER succeeded nor
    /// failed, so a trailer carrying either - or carrying nothing, which reads as zero - would describe
    /// something other than the rejection that actually occurred. In that case the code falls back to the
    /// legacy retry member, which is what a retry-or-surface decision is made against.
    /// </para>
    /// </remarks>
    private static IResult ProjectConflict(HttpContext httpContext, DataServicesConflictException conflict)
    {
        ConflictDetail? detail = conflict.Conflict;
        JsonNode? payload = detail is null ? null : JsonNode.Parse(ResponseFormatter.Format(detail));

        if (payload is null)
        {
            // The typed client only raises this exception WITH a decoded detail, so this is the
            // defensive arm rather than the expected one. It answers with the same shape an Aborted that
            // carried no decodable detail receives: the status is preserved and nothing is fabricated.
            return ProjectFailure(
                httpContext,
                new StatusProjection(
                    StatusCodes.Status409Conflict,
                    RetCode.E_RETRY,
                    ConflictWithoutDetailProse,
                    FromUpstream: true,
                    ConflictWithoutDetailProblemType,
                    ConflictWithoutDetailProblemTitle),
                conflict);
        }

        long retCode = Predicates.IsFailed(conflict.RetCode) ? conflict.RetCode : RetCode.E_RETRY;

        StatusProjection projection = new(
            StatusCodes.Status409Conflict,
            retCode,
            ConflictDetailProse,
            FromUpstream: true,
            ConflictProblemType,
            ConflictProblemTitle);

        ProblemDetails problem = BuildProblem(httpContext, projection);
        problem.Extensions[ConflictExtensionMember] = payload;

        LogProjectedFailure(httpContext, projection, conflict);

        return TypedResults.Problem(problem);
    }

    /// <summary>
    /// Answers with a translated failure, and records it once on the operator channel.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The translated status.</param>
    /// <param name="failure">The originating exception, read only for its type name.</param>
    /// <returns>The problem document.</returns>
    private static IResult ProjectFailure(
        HttpContext httpContext,
        StatusProjection projection,
        Exception failure)
    {
        LogProjectedFailure(httpContext, projection, failure);

        return TypedResults.Problem(BuildProblem(httpContext, projection));
    }

    /// <summary>
    /// Answers with a rejection Gateway itself produced, and records it once on the operator channel.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The rejection.</param>
    /// <returns>The problem document.</returns>
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
            ResolveCorrelationId(httpContext));

        return TypedResults.Problem(BuildProblem(httpContext, projection));
    }


    /// <summary>
    /// Builds the problem document a translated failure answers with.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The translated status.</param>
    /// <returns>The document, without the conflict member, which its own caller attaches.</returns>
    /// <remarks>
    /// <para>
    /// Four members and no more. <c>retCode</c> carries the legacy return code; <c>upstream</c> names the
    /// upstream a FORWARDED failure came from and is absent when Gateway itself produced the response, as
    /// the contract requires; <c>traceId</c> carries the correlation identifier; and <c>conflict</c> is
    /// attached only by the conflict projection. RFC 9457 permits extension members, which is why the
    /// contract's schema is the one object in it that accepts additional properties.
    /// </para>
    /// <para>
    /// <c>dbError</c> IS DELIBERATELY NEVER POPULATED HERE, and that is a finding rather than an omission.
    /// A database failure does not reach Gateway on a FAILURE path: C-03's update carries it as a field of
    /// its SUCCESS response, which this projection forwards whole, and no method of C-03 or C-04 declares a
    /// rich-error binding whose database alternative would arrive on a status instead. Decoding a trailer
    /// here to fill the member would duplicate the typed client's single decoding path - which reads the
    /// key, the status and the payload type from the descriptor precisely so that one path exists - and
    /// would put this file in the business of handling a payload whose statement field the legacy carried
    /// unredacted. The member stays available in the contract for a producer that has one.
    /// </para>
    /// <para>
    /// <c>instance</c> carries the caller's own request path, which discloses nothing the caller did not
    /// send. The query string is excluded from every field of this document, because a caller that
    /// mistakenly placed a credential in one must not have it reflected back.
    /// </para>
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
            problem.Extensions[UpstreamExtensionMember] = DataServicesUpstream;
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
    /// Writes the one allowlisted operator record for a translated failure.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="projection">The translated status.</param>
    /// <param name="failure">The originating exception, read only for its type name.</param>
    /// <remarks>
    /// <para>
    /// THE EXCEPTION OBJECT IS NOT PASSED TO THE LOGGER, and neither is the upstream status detail, the
    /// request body or the response body. The logging abstraction renders a passed exception through its own
    /// string conversion, which includes every message in the chain; the status detail and both bodies are
    /// caller or upstream content, and on the data path either may quote an interpolated literal. What is
    /// recorded instead IDENTIFIES the fault without quoting it: the request's own method, the route
    /// PATTERN rather than the path, the gRPC status name, the HTTP status the caller is about to receive,
    /// the legacy return code, the exception TYPE name and the correlation identifier.
    /// </para>
    /// <para>
    /// The correlation identifier is resolved the SAME WAY the service's fault handler resolves it, which is
    /// what makes the contract's guarantee true: the value a caller is handed is the value in the operator
    /// record for the same occurrence, so a caller quoting it always finds a record.
    /// </para>
    /// </remarks>
    private static void LogProjectedFailure(
        HttpContext httpContext,
        StatusProjection projection,
        Exception failure)
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
            DescribeGrpcStatus(failure),
            failure.GetType().Name,
            ResolveCorrelationId(httpContext));
    }

    /// <summary>
    /// Names the gRPC status an exception carried, without reading its detail text.
    /// </summary>
    /// <param name="failure">The originating exception.</param>
    /// <returns>The status name, or the unclassified marker when the exception carried none.</returns>
    /// <remarks>
    /// The status CODE only. <c>Status.Detail</c> is upstream text and is deliberately never read here.
    /// </remarks>
    private static string DescribeGrpcStatus(Exception failure) => failure switch
    {
        RpcException rpc => rpc.StatusCode.ToString(),
        DataServicesConflictException conflict => conflict.Status.StatusCode.ToString(),
        _ => UnroutedRouteDescription,
    };

    /// <summary>
    /// Names the route pattern the request matched.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The pattern, or a marker when none was matched.</returns>
    /// <remarks>
    /// THE PATTERN AND NOT THE PATH. A pattern names this system's own route table; a path carries whatever
    /// the caller put in it, including a session identifier that correlates to their data.
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
    /// <returns>The identifier, or an empty string when the host supplies none.</returns>
    /// <remarks>
    /// IDENTICAL to the resolution the service's fault handler uses, deliberately. The contract guarantees
    /// that the identifier a caller receives is the one in the operator record for the same occurrence, and
    /// two independent resolutions would break that guarantee while still looking correct.
    /// </remarks>
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        string? activityId = Activity.Current?.Id;

        return string.IsNullOrEmpty(activityId) ? httpContext.TraceIdentifier ?? string.Empty : activityId;
    }

    // ==================================================================================================
    //  PUBLISHED-DOCUMENT METADATA
    // ==================================================================================================

    /// <summary>
    /// Applies the parts of the contract that endpoint metadata alone cannot express to a generated
    /// operation.
    /// </summary>
    /// <param name="openApiOperation">The operation the document generator produced.</param>
    /// <param name="context">The transformation context, which carries the document being built.</param>
    /// <param name="operation">The published metadata for this operation.</param>
    /// <param name="protoRequest">The request message's fully-qualified protobuf name.</param>
    /// <param name="protoResponse">The response message's fully-qualified protobuf name.</param>
    /// <returns>A completed task; the transformer contract is asynchronous, this work is not.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="openApiOperation"/> or <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Endpoint-scoped, so it can never affect another operation, and applied through
    /// <c>AddOpenApiOperationTransformer</c> rather than <c>WithOpenApi</c> because the latter is documented
    /// as not integrating with the built-in document generation this service uses.
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
        // happen to share a value here, and the contract pins the operation id.
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

        return Task.CompletedTask;
    }

    /// <summary>
    /// Replaces the generator's default response descriptions with the contract's own wording.
    /// </summary>
    /// <param name="openApiOperation">The operation whose responses are being described.</param>
    /// <param name="operation">The published metadata for this operation.</param>
    /// <remarks>
    /// Each assignment is guarded: a response the generator did not produce is LEFT ALONE rather than
    /// created here, because inventing one would publish a status the operation does not return. The
    /// generator derives a description from the status code's reason phrase, which is accurate and says
    /// nothing; the contract's wording is what a consumer needs.
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
    /// Carries the contract's description onto the session-identifier parameter, where an operation has
    /// one.
    /// </summary>
    /// <param name="openApiOperation">The operation being described.</param>
    /// <remarks>
    /// Guarded rather than unconditional: thirty-seven of the thirty-nine operations declare no parameter at
    /// all, and declaring one on them would publish an argument they do not accept.
    /// </remarks>
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
    /// Declares that an operation requires the contract's bearer security scheme.
    /// </summary>
    /// <param name="openApiOperation">The operation to constrain.</param>
    /// <param name="document">
    /// The document being built, used to resolve the scheme reference and, if necessary, to make it
    /// resolvable. May be <see langword="null"/> when a generator invokes the transformer without one.
    /// </param>
    /// <remarks>
    /// The requirement names one scheme with an empty scope list, which is what the specification requires
    /// for an HTTP scheme - scopes belong to OAuth 2.0 and OpenID Connect schemes only. It is ADDED rather
    /// than assigned over any existing requirement, so a document-level default cannot be silently
    /// discarded here.
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
    /// Makes sure the document carries a <c>bearerAuth</c> security scheme, so that the requirement applied
    /// above cannot reference a component that does not exist.
    /// </summary>
    /// <param name="document">The document being built.</param>
    /// <remarks>
    /// A guard, not a registration: it only fills the gap in a document that has an operation-level
    /// requirement and no matching component, which is an invalid document no generated client could
    /// satisfy. Where the composition root publishes the full scheme this method is a no-op, and where it
    /// publishes it later the fuller description wins.
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


    // ==================================================================================================
    //  THE PROJECTION TABLE'S OWN TYPES
    // ==================================================================================================

    /// <summary>
    /// Which of the two projected contracts an operation belongs to.
    /// </summary>
    /// <remarks>
    /// Two members, and the distinction is not cosmetic: the tag, the contract identifier and the gRPC
    /// service name in <c>x-grpc-method</c> all follow from it, and C-04 is a separate contract from C-03
    /// precisely so the expansion engine can version independently.
    /// </remarks>
    private enum ContractSurface
    {
        /// <summary>C-03, <c>dataservices.v1.DataWindowService</c>.</summary>
        DataWindow,

        /// <summary>C-04, <c>dataservices.v1.ColumnExpressionService</c>.</summary>
        ColumnExpression,
    }

    /// <summary>
    /// Whether, and how, an operation declares a <c>400</c>.
    /// </summary>
    /// <remarks>
    /// Three members because the contract publishes three cases, and an operation that declared the wrong
    /// one would publish a response it cannot produce or omit one it can.
    /// </remarks>
    private enum BadRequestDeclaration
    {
        /// <summary>
        /// The operation declares the shared <c>400</c>: an upstream argument rejection, or a body Gateway
        /// itself could not bind.
        /// </summary>
        Standard,

        /// <summary>
        /// The operation declares no <c>400</c> at all. The two close operations and the event-gate read
        /// take no request body, so there is nothing for Gateway's own binding to reject.
        /// </summary>
        None,

        /// <summary>
        /// The operation declares the <c>400</c> whose distinctive reason is a cross-DataWindow variable
        /// reference spanning expression sessions or service instances, which is blocked. The five
        /// operations that can encounter one carry this.
        /// </summary>
        CrossSessionReferenceBlocked,
    }

    /// <summary>
    /// One row of the projection table: everything published about an operation that is not its request or
    /// response type.
    /// </summary>
    /// <param name="Route">The route template, relative to the group it is declared on.</param>
    /// <param name="OperationId">
    /// The contract's <c>operationId</c>, which is also the endpoint name. It is the method name a
    /// generated client exposes, so it is transcribed from the contract rather than derived from the RPC
    /// name - the two deliberately differ where the contract chose a clearer spelling.
    /// </param>
    /// <param name="RpcName">
    /// The gRPC method this operation projects, spelled as the protocol definition spells it. Composed with
    /// the service's own descriptor name to produce <c>x-grpc-method</c>.
    /// </param>
    /// <param name="Surface">Which of the two contracts the operation belongs to.</param>
    /// <param name="Summary">The contract's one-line summary.</param>
    /// <param name="Description">
    /// The contract's description, carried across so the generated document and the hand-authored one say
    /// the same thing to a consumer.
    /// </param>
    /// <param name="BadRequest">Whether, and how, the operation declares a <c>400</c>.</param>
    /// <param name="DeclaresNotFound">
    /// Whether the operation declares a <c>404</c>. The two session-opening operations do not: there is no
    /// prior identifier for them to fail to resolve.
    /// </param>
    /// <param name="DeclaresConflict">
    /// Whether the operation declares a <c>409</c>. Exactly one does - the update - because it is the only
    /// one that can encounter an optimistic-concurrency mismatch.
    /// </param>
    /// <param name="ServerStreaming">
    /// Whether the projected method is a server stream. Set by the streaming registration helper rather
    /// than by hand, so it cannot disagree with the helper that declared the route.
    /// </param>
    /// <param name="SuccessDescription">
    /// The contract's own <c>200</c> description where it differs from the shared one, and
    /// <see langword="null"/> where it does not.
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
        /// <summary>The contract's tag for this operation's surface.</summary>
        internal string Tag => Surface == ContractSurface.DataWindow ? DataWindowTag : ColumnExpressionTag;

        /// <summary>The contract identifier this operation's surface carries.</summary>
        internal string ContractId => Surface == ContractSurface.DataWindow
            ? DataWindowContractId
            : ColumnExpressionContractId;

        /// <summary>
        /// The fully-qualified gRPC method this operation projects, composed from the service's own
        /// generated descriptor so it cannot drift from the protocol definition.
        /// </summary>
        internal string GrpcMethod => string.Concat(
            Surface == ContractSurface.DataWindow ? DataWindowServiceName : ColumnExpressionServiceName,
            "/",
            RpcName);
    }

    /// <summary>
    /// One translated failure: the HTTP status, the legacy return code, the fixed prose, whether an upstream
    /// produced it, and the problem type and title where a specific one applies.
    /// </summary>
    /// <param name="HttpStatus">The HTTP status to answer with.</param>
    /// <param name="RetCode">
    /// The legacy return code, consumed as a symbol from the single transcription of the oracle's
    /// catalogue.
    /// </param>
    /// <param name="Detail">
    /// FIXED PROSE. Never composed from an upstream message, an exception message, a parser diagnostic or
    /// any part of a request body.
    /// </param>
    /// <param name="FromUpstream">
    /// Whether an upstream produced the failure. The contract requires the <c>upstream</c> member be absent
    /// when Gateway itself produced the response, so this is a real statement and not a default.
    /// </param>
    /// <param name="Type">
    /// A specific problem type, or <see langword="null"/> for RFC 9457's own default. Only the three
    /// mutually distinguishable conflict-class outcomes carry one.
    /// </param>
    /// <param name="Title">
    /// A specific title, or <see langword="null"/> to use the status code's reason phrase, which is what
    /// RFC 9457 recommends alongside its default type.
    /// </param>
    private readonly record struct StatusProjection(
        int HttpStatus,
        long RetCode,
        string Detail,
        bool FromUpstream,
        string? Type = null,
        string? Title = null);
}

/// <summary>
/// The free-form JSON envelope every projected request and response body uses.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the contract's <c>ProtoPayload</c> schema: an object whose members are the canonical protobuf
/// JSON mapping of the message the operation's <c>x-proto-request</c> or <c>x-proto-response</c> extension
/// names. The two extensions are how the contract itself delegates its per-operation envelopes, and this
/// type is the schema that delegation resolves to.
/// </para>
/// <para>
/// <b>Why the envelope is delegated rather than transcribed.</b> Transcribing all of them would create a
/// second source of truth for well over a hundred messages, in a different language, with nothing keeping
/// the two in step - and the first divergence would be silent. The <c>x-proto-*</c> extensions are the
/// alternative: mechanical, one-to-one, and checkable against the generated types. An envelope carries no
/// decision of its own - it names the operation's arguments - so delegating it costs a consumer nothing
/// that the generated client does not already give them.
/// </para>
/// <para>
/// It is also the schema the five operations whose responses the contract MIRRORS field for field resolve
/// to, and that is a recorded deviation rather than an oversight - see adjudication A8 in this file's
/// header. Those shapes are built on the legacy's preserved <c>SCREAMING_SNAKE</c> value sets, and
/// declaring them here would mean declaring those identifiers here, which the repository's analyzer
/// configuration makes a build error in a Gateway file. Each of the five names its exact message in
/// <c>x-proto-response</c> instead.
/// </para>
/// <para>
/// Declared in this file rather than in a file of its own: this folder permits exactly five files, and the
/// shape is small enough that inlining it keeps the whole projection reviewable in one place.
/// </para>
/// </remarks>
public sealed record ProtoPayload
{
    /// <summary>
    /// The payload's members, carried verbatim.
    /// </summary>
    /// <remarks>
    /// Captured as extension data so that the schema is an OPEN object rather than a closed one with no
    /// properties. This type exists to describe the wire shape in the published document; the projection
    /// itself parses and renders the body through the protobuf JSON codecs, never through this type, so no
    /// value is ever round-tripped by way of it.
    /// </remarks>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Members { get; init; }
}

