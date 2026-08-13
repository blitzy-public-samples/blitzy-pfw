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
//   veto. Protobuf over gRPC carries all three natively; JSON over REST can encode each by convention
//   but enforces neither the ordering nor the veto's arity, and cannot carry a server-initiated question
//   at all.
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
//       upstream, or a call that failed in transit - retried first only when the operation is replay-safe. That
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
//   A8  THE RUNTIME DOCUMENT SUMMARISES EVERY PROJECTED BODY; THE AUTHORED CONTRACT PUBLISHES THEM ALL
//       CONCRETELY. `gateway.v1.yaml` declares all 118 messages and 15 enums of the projected closure
//       member by member, closed to unknown members, with a `required` list stating what the wire
//       carries - and the sibling test project cross-checks every one against its compiled descriptor
//       on each build, so it cannot drift. `/openapi/v1.json`, served from this file's registrations,
//       remains a summary: every projected body resolves to the free-form envelope and names its exact
//       message in `x-proto-request` / `x-proto-response`.
//
//       That asymmetry is a decision, not an oversight, and it rests on two things. First, C-A leaves
//       no shared home for a descriptor-to-schema generator - a service may not reach into another
//       service's code, and PowerFramework.Contracts carries no behaviour - so building the schemas
//       here would be a THIRD derivation of the same descriptors, in a third place, with the sibling
//       projection needing a fourth. Second, the mechanical obstacle is real for the five MIRRORED
//       response shapes: `RetrieveResult`, the three event-gate responses and
//       `ExpressionEventStreamResult` are built on the legacy's preserved SCREAMING_SNAKE value sets -
//       `RetCodeValue` and `DataWindowEventBit` - and the repository-root .editorconfig scopes its
//       naming-analyzer suppressions to a fixed list of seven files of which NO Gateway file is one,
//       while Directory.Build.props sets TreatWarningsAsErrors, so declaring those identifiers here
//       would be a BUILD ERROR rather than a style debate.
//
//       WHAT THE OPEN ENVELOPE DOES NOT MEAN: it records this document's silence about the members, and
//       never a permissiveness in the binder. A7 is what actually happens to an unrecognised member.
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
// ======================================================================================================

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using PowerFramework.Gateway.Authorization;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
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
    /// The scope an authenticated caller must have been granted to reach any operation in the C-03 and C-04 projection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECLARED HERE, NEXT TO THE ROUTE THAT REQUIRES IT, and read by <c>Program.cs</c> when it builds
    /// the policy. A route and its entitlement are one decision; a scope name spelled independently in
    /// an authorization file and in a route file is the defect that enforces nothing while looking
    /// correct in both places.
    /// </para>
    /// <para>
    /// The spelling matches the grant the issuance roster hands the calling identity
    /// (<c>orchestration/.env.example</c>, <c>Security:Clients</c>), because Security refuses a scope it
    /// never granted rather than narrowing the request - so a mismatch here is not a smaller token, it
    /// is no token at all.
    /// </para>
    /// </remarks>
    internal const string RequiredScope = "datawindow";

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
    /// The extension member that marks a terminal element as a stream this gateway terminated.
    /// </summary>
    /// <remarks>
    /// A MEMBER A UNARY PROBLEM CANNOT CARRY, because a unary problem IS the whole response and a
    /// terminated stream's problem is the last element of one. It is what tells a reader that the object
    /// they are looking at is not a record: RFC 9457 permits extension members, and the contract's problem
    /// schema is the one object in it that accepts additional properties.
    /// </remarks>
    private const string StreamTerminatedExtensionMember = "streamTerminated";

    /// <summary>
    /// The extension member carrying how many elements were forwarded before the stream faulted.
    /// </summary>
    /// <remarks>
    /// THE ONE FACT NEITHER THE STATUS NOR THE DOCUMENT CAN SUPPLY. A caller that received a prefix needs
    /// to know how much of it is real, and an operator correlating with the upstream's own log needs the
    /// same number to tell "faulted immediately" from "faulted after twenty chunks".
    /// </remarks>
    private const string ElementsDeliveredExtensionMember = "elementsDelivered";

    /// <summary>
    /// The serialization used for a terminated stream's terminal element.
    /// </summary>
    /// <remarks>
    /// NULLS ARE OMITTED SO THE ELEMENT READS EXACTLY AS A PROBLEM BODY DOES. <see cref="ProblemDetails"/>
    /// carries its own member names as attributes and its extensions as extension data, so no naming
    /// policy is applied here - applying one would rename the members the contract fixes.
    /// </remarks>
    private static readonly JsonSerializerOptions TerminalElementSerialization = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    /// <summary>
    /// The largest session identifier this projection forwards, taken from the contract's own declaration.
    /// </summary>
    /// <remarks>
    /// TRANSCRIBED FROM <c>components.parameters.SessionIdQuery.schema.maxLength</c> and its path sibling,
    /// both of which declare 128. It is a bound the CONTRACT publishes rather than one chosen here, which
    /// is why it is enforced at all: a declared constraint nothing applies is a promise a consumer cannot
    /// rely on.
    /// </remarks>
    private const int MaximumSessionIdLength = 128;

    /// <summary>The detail for a session identifier the caller omitted entirely.</summary>
    /// <remarks>
    /// IT SAYS THE PARAMETER IS REQUIRED AND NAMES IT, because the parameter name is the contract's own and
    /// not caller content. No caller VALUE appears in any of these three details.
    /// </remarks>
    private const string AbsentSessionIdDetail =
        "This operation requires a sessionId and none was supplied. The published parameter declares it as "
        + "required, so an omitted identifier is a client error rather than a fault of this service; it is "
        + "answered identically to an empty one, because omitting a value and supplying an empty one are "
        + "two spellings of the same mistake.";

    /// <summary>The detail for an empty session identifier.</summary>
    private const string EmptySessionIdDetail =
        "The sessionId supplied is empty. The published parameter declares a minimum length of one, so an "
        + "empty identifier cannot name a session. It is answered identically to an omitted one.";

    /// <summary>The detail for a session identifier longer than the contract permits.</summary>
    /// <remarks>
    /// THE BOUND IS REPORTED AND THE VALUE IS NOT. A caller needs to know the limit it exceeded in order to
    /// correct its request; echoing the identifier back would put caller content in a problem body for no
    /// benefit, since the caller already has it.
    /// </remarks>
    private const string OverLongSessionIdDetail =
        "The sessionId supplied is longer than the published parameter permits, which is 128 characters. "
        + "It is refused here rather than forwarded, because an upstream asked about an identifier it "
        + "cannot resolve answers that no such session exists - which would tell the caller the session is "
        + "gone rather than that its request violated a declared bound.";

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
    /// own contract publishes is unavailable, which is deployment or version skew. Answered as 500 and never
    /// 501, because 501 identifies a reserved deferred-capability route and this operation is not one.
    /// </remarks>
    private const string UnimplementedDetail =
        "DataServices reported that this operation's upstream method is not implemented by the "
        + "deployment currently answering. The projected contract publishes it, so this indicates a "
        + "version skew between Gateway and its upstream rather than a capability boundary - which is why "
        + "it is not answered as 501: that status identifies a reserved deferred-capability route, and this "
        + "operation is an implemented one.";

    /// <summary>The detail for an upstream <c>DeadlineExceeded</c>.</summary>
    private const string DeadlineExceededDetail =
        "The call to DataServices did not complete within its deadline. Whether the operation took effect "
        + "is undetermined, so a caller must re-read before assuming either outcome.";

    /// <summary>The detail for a server-answered <c>Unavailable</c>.</summary>
    private const string UnavailableDetail =
        "DataServices answered that it is unavailable. The request was not processed.";

    /// <summary>The detail for a transport failure that produced no gRPC response at all.</summary>
    /// <remarks>
    /// 🔴 THIS TEXT USED TO CLAIM A RETRY THAT MOST OPERATIONS NEVER GET. It read "after the configured
    /// retry policy was exhausted" unconditionally, which is true only for an operation whose replay is
    /// safe. Retry here is deliberately OPERATION-SCOPED - see
    /// <c>Clients/OutboundCallPolicy.BuildReplaySafePaths</c> - and every operation that creates, mutates
    /// or advances upstream state, <c>Retrieve</c> and <c>Update</c> among them, is attempted EXACTLY ONCE
    /// on purpose. Telling an operator their failed update had exhausted a retry policy sends them looking
    /// for a transient fault behind a call that was tried once; worse, it implies an update may have been
    /// applied more than once. The text states both possibilities and which one applies to what.
    /// </remarks>
    private const string UpstreamUnavailableDetail =
        "DataServices could not be reached, or the call to it failed in transit, so no response arrived. "
        + "Whether the call was retried first depends on the operation: one whose replay is safe is "
        + "retried under the configured policy, and reaching this response means the policy was "
        + "exhausted, while one whose replay is NOT safe - every operation that creates, mutates or "
        + "advances upstream state - is attempted exactly once by design and was not retried. This "
        + "failure mode is one decomposition itself creates: an in-process call cannot fail in transit "
        + "and a network call can.";

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
    /// <remarks>
    /// It names the message rather than restating its members because the members are published, member
    /// by member, by the schema of that name in the authored contract - see adjudication A8.
    /// </remarks>
    private const string ProjectedSuccessDescription =
        "The projected gRPC method returned OK. The body is the canonical protobuf JSON mapping of the "
        + "message named in this operation's x-proto-response extension, whose members the contract "
        + "document publishes as a schema of the same name.";

    /// <summary>The contract's shared <c>400</c> description.</summary>
    private const string BadRequestDescription =
        "The projected gRPC method returned InvalidArgument, or the request failed Gateway's own binding. "
        + "retCode carries the originating legacy return code so the specific validation is identifiable "
        + "rather than merely the HTTP class.";

    /// <summary>
    /// The contract's <c>400</c> description for the three operations whose only argument is a session
    /// identifier.
    /// </summary>
    /// <remarks>
    /// These operations take no request body, so their <c>400</c> is entirely about the identifier: it is
    /// omitted, it is empty, or it exceeds the published maximum length. All three are answered identically
    /// with <c>E_INVALID_ARGUMENT</c>, and none echoes the value back.
    /// </remarks>
    private const string SessionIdConstraintDescription =
        "The sessionId does not satisfy the parameter contract this operation publishes: it was omitted, it "
        + "is empty, or it is longer than 128 characters. This operation takes no request body, so its 400 "
        + "can arise no other way. retCode carries E_INVALID_ARGUMENT and the value is not echoed back.";

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
    /// <remarks>
    /// Worded to match <see cref="UpstreamUnavailableDetail"/>: retry is operation-scoped, so this
    /// description must not promise every caller a retry that only replay-safe operations receive.
    /// </remarks>
    private const string UpstreamUnavailableDescription =
        "An upstream service could not be reached, or the call to it failed in transit. A replay-safe "
        + "operation is retried under the configured policy before this is returned; an operation whose "
        + "replay is not safe is attempted exactly once and is never retried, so for those this response "
        + "reports a single failed attempt. This response exists because of the decomposition itself: an "
        + "in-process call cannot fail in transit and a network call can, so handling the failure is "
        + "required BY the transition rather than being a behavioural improvement layered on top of it. "
        + "The body names which upstream failed.";

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
    /// <para>
    /// DEFAULT VALUES ARE FORMATTED - see adjudication A6. The canonical mapping omits them, which would
    /// drop REQUIRED members of the contract's own mirrored schemas: <c>RetrieveChunk</c> requires
    /// <c>rowCount</c>, <c>chunkIndex</c>, <c>final</c> and <c>cumulativeRowCount</c>, and every one of
    /// those is legitimately zero or false on a real chunk. Fields with explicit presence are unaffected
    /// and remain absent when unset, which preserves the contract's signal that a chunk carrying no
    /// buffer field is a mixed-buffer chunk whose rows are individually tagged.
    /// </para>
    /// <para>
    /// IT IS ALSO WHAT KEEPS <c>DataWindowRow.originalValues</c> PRESENT ON EVERY ROW. A repeated field has
    /// no explicit presence, so an empty one is a default value: without this setting an insert-shaped row -
    /// the one row that legitimately has no prior state - would be serialized without the member at all.
    /// The distinction that matters is between an EMPTY array and an ABSENT one, and only formatting
    /// defaults preserves it: an empty list says the row has no prior state, while an absent member says
    /// nothing and leaves a consumer building the concurrency predicate to guess which was meant. The
    /// schema deliberately marks nothing <c>required</c> on this shape - it travels in a request as well as
    /// a response, and the real obligation is conditional on the row's status, which is why it is enforced
    /// at runtime in <c>Validators/UpdateRowValidator</c> instead - so this setting is the whole of what
    /// makes the member's presence true of a response.
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

        // C-G, AND THE SCOPE THIS SERVICE PUBLISHES A 403 FOR. One call, applied to the parent group,
        // inherited by every endpoint declared below INCLUDING those on the nested expression group - and
        // here that inheritance is exactly what is wanted, which is why the nesting stays.
        //
        // ONE SCOPE FOR BOTH PROJECTED CONTRACTS, WHICH IS NOT WHAT DATASERVICES DOES. DataServices splits
        // C-03 and C-04 into two scopes because the two gRPC services version independently and a caller
        // may hold one contract without the other; it therefore had to UN-nest its expression group, since
        // an inherited convention cannot be removed. At the ingress the two are one capability area under
        // one path prefix and the only consumer requests one name for all of it, so the ingress does not
        // re-express an internal versioning boundary as an external permission. It gates what it
        // publishes, and the projection presents its own downstream credential.
        //
        // The parameterless form would stand here otherwise, which means any token addressed to this
        // service reaches all thirty-nine projected operations whatever it was scoped to.
        RouteGroupBuilder dataWindow = endpoints
            .MapGroup(DataWindowGroupPrefix)
            .RequireAuthorization(GatewayScopes.DataWindow);
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
                BadRequest: BadRequestDeclaration.SessionIdConstraint),
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
                BadRequest: BadRequestDeclaration.SessionIdConstraint,
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
                BadRequest: BadRequestDeclaration.SessionIdConstraint),
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
                client.StreamExpressionEventsAsync(request, cancellationToken),

            // ⚠ THE ONE OPERATION THAT OPTS IN, AND THE ONLY ONE THAT MAY. Its upstream is a
            // subscription that ends only when the client goes away, so a request/response projection of
            // it has to decide when the collection is complete; the retrieval above must NOT opt in,
            // because it terminates itself with a final-marked chunk and a window would truncate a
            // legitimate result.
            collectWithinWindow: true);
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
    /// <b>⚠ THE PARAMETER IS NULLABLE, AND THAT MATTERS MORE THAN IT LOOKS.</b> Declaring it
    /// non-nullable makes an OMITTED query parameter a PARAMETER-BINDING refusal raised before the
    /// route runs - a <c>BadHttpRequestException</c> that escapes into the host's exception handler and
    /// reaches the caller as <c>500</c> with <c>UNKNOWN</c>, while the same request with an EMPTY value
    /// answers <c>400</c> with <c>E_INVALID_ARGUMENT</c>. Two spellings of one mistake, answered as a
    /// server fault and a client error respectively, and the server-fault answer is the wrong one: nothing
    /// failed here except the caller's request. Binding it nullable moves the decision into the route,
    /// where the operation's own declared parameter contract can be applied and one answer produced for
    /// every violation of it.
    /// </para>
    /// <para>
    /// SO THE <c>400</c> IS A DECLARED RESPONSE OF THESE OPERATIONS, in the authored contract and in
    /// the generated document alike. It has to be: the operation declares <c>required</c>,
    /// <c>minLength</c> and <c>maxLength</c> on the parameter, and a declared constraint with no declared
    /// response for violating it is a promise a generated client cannot branch on. The objection that a
    /// binding refusal "is not something the route evaluates" does not apply here: this route evaluates it.
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
            (HttpContext httpContext, string? sessionId) =>
                ProjectSessionScopedAsync(httpContext, sessionId, buildRequest, invoke));

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
    /// <param name="collectWithinWindow">Whether collection is confined to the window.</param>
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
        Func<DataServicesClient, TRequest, CancellationToken, IAsyncEnumerable<TResponse>> invoke,
        bool collectWithinWindow = false)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        ProjectedOperation streaming = operation with { ServerStreaming = true };

        // Typed as Func for the reason recorded on the unary helper: a bare one-parameter lambda would bind
        // to the RequestDelegate overload, which discards the projected body.
        Func<HttpContext, Task<IResult>> handler =
            httpContext => ProjectServerStreamAsync(httpContext, invoke, collectWithinWindow);

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
            // NOT `ProducesProblem`, AND THE DIFFERENCE IS THE WHOLE POINT OF THE 409.
            //
            // `ProducesProblem` publishes the bare problem shape, so a consumer reading the generated
            // document could not see that a `conflict` member is present - while the authored contract
            // declares a `ConflictProblemDetails` schema for exactly this response, and the projection
            // does attach the detail. Three artifacts, one of them silently disagreeing, on the one body
            // a caller has to ACT on rather than merely read. See ConflictProblemDetails at the foot of
            // this file for why a caller that cannot see WHICH column moved cannot construct a retry.
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
        // One status the failure map also translates is deliberately NOT declared. AlreadyExists is
        // produced by exactly one method in the estate - the macro channel reporting an existing
        // attachment - and that method is bidirectional and unprojected, so no route here can return it.
        //
        // 🔴 AND 501 IS DECLARED BY NO PROJECTED ROUTE BECAUSE NO PROJECTED ROUTE PRODUCES IT, WHICH IS NOW
        // TRUE. It was not: the failure map sent an upstream Unimplemented, and the in-band pair
        // E_NO_SUPPORT / E_NO_IMPLEMENTATION, to 501 - a status gateway.v1.yaml declares on the eight
        // reserved deferred-capability operations and on no projected one (AAP 0.4.4, C-D). Both conditions
        // now answer the 500 declared immediately below, carrying the originating legacy code on retCode, so
        // the declared set and the reachable set agree, and 501 goes on meaning exactly one thing at this
        // ingress: an entire capability area is unbuilt.
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

            return Render(httpContext, response);
        });

    /// <summary>
    /// Invokes a projected method whose only argument is a session identifier, and renders its response.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf response message.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="sessionId">
    /// The identifier as it arrived, or <see langword="null"/> when the caller omitted it entirely.
    /// </param>
    /// <param name="buildRequest">Builds the upstream request from the identifier, once it is accepted.</param>
    /// <param name="invoke">The typed-client member being projected.</param>
    /// <returns>The projected result.</returns>
    /// <remarks>
    /// THE PARAMETER CONTRACT IS APPLIED BEFORE THE REQUEST IS BUILT, so an identifier the operation
    /// declares as unacceptable never becomes an upstream call. That is not merely tidier: forwarding one
    /// meant the answer to a caller's own mistake was whatever the upstream happened to say about a handle
    /// it could not resolve - a <c>404</c> for an identifier 129 characters long, which tells the caller the
    /// session does not exist rather than that its request violated a bound the contract publishes.
    /// </remarks>
    private static Task<IResult> ProjectSessionScopedAsync<TRequest, TResponse>(
        HttpContext httpContext,
        string? sessionId,
        Func<string, TRequest> buildRequest,
        Func<DataServicesClient, TRequest, CancellationToken, Task<TResponse>> invoke)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
        => ProjectAsync(httpContext, async (client, cancellationToken) =>
        {
            if (!TryAcceptSessionId(sessionId, out StatusProjection rejection))
            {
                return RejectRequest(httpContext, rejection);
            }

            return Render(
                httpContext,
                await invoke(client, buildRequest(sessionId!), cancellationToken).ConfigureAwait(false));
        });

    /// <summary>
    /// Applies the session-identifier parameter contract the operation itself declares.
    /// </summary>
    /// <param name="sessionId">The identifier as it arrived, or <see langword="null"/> when omitted.</param>
    /// <param name="rejection">The refusal to answer with, when the identifier is not acceptable.</param>
    /// <returns><see langword="true"/> when the identifier satisfies every declared constraint.</returns>
    /// <remarks>
    /// <para>
    /// <b>EXACTLY THE THREE CONSTRAINTS THE CONTRACT DECLARES, AND NOT ONE MORE.</b>
    /// <c>components.parameters.SessionIdQuery</c> and its path sibling both declare
    /// <c>required: true</c>, <c>minLength: 1</c> and <c>maxLength: 128</c>
    /// [<c>gateway.v1.yaml</c>], so absence, emptiness and an over-long value are refused here and
    /// everything else is forwarded.
    /// </para>
    /// <para>
    /// <b>WHITESPACE IS NOT TRIMMED AND NOT REFUSED, WHICH IS DELIBERATE AND IS THE ONE LINE HERE THAT
    /// LOOKS LIKE AN OMISSION.</b> A single space satisfies <c>minLength: 1</c>, so refusing it would be
    /// this gateway enforcing a constraint the contract does not publish - and trimming it would be this
    /// gateway REPAIRING a caller value, which changes which session the request names. The identifier is
    /// opaque to Gateway: the session it names lives inside DataServices, which owns the decision about
    /// whether such a session exists and answers its own defined negative. Inventing a stricter rule here
    /// would make the published parameter schema a description of something other than the deployed
    /// behaviour.
    /// </para>
    /// <para>
    /// LENGTH IS COUNTED IN UTF-16 CODE UNITS, which is what <c>maxLength</c> in a JSON Schema means for a
    /// string and what the framework's own <c>String.Length</c> reports. A surrogate pair therefore counts
    /// as two, consistently with the document a consumer validates against.
    /// </para>
    /// </remarks>
    private static bool TryAcceptSessionId(
        string? sessionId,
        out StatusProjection rejection)
    {
        if (sessionId is null)
        {
            rejection = BindingRejection(AbsentSessionIdDetail);

            return false;
        }

        if (sessionId.Length == 0)
        {
            rejection = BindingRejection(EmptySessionIdDetail);

            return false;
        }

        if (sessionId.Length > MaximumSessionIdLength)
        {
            rejection = BindingRejection(OverLongSessionIdDetail);

            return false;
        }

        rejection = default;

        return true;
    }

    /// <summary>
    /// Binds the body, consumes the projected server stream in arrival order, and renders it as an ordered
    /// collection.
    /// </summary>
    /// <typeparam name="TRequest">The protobuf request message.</typeparam>
    /// <typeparam name="TResponse">The protobuf message each streamed element carries.</typeparam>
    /// <param name="httpContext">The current request.</param>
    /// <param name="invoke">The typed-client member being projected.</param>
    /// <param name="collectWithinWindow">Whether collection is confined to the window.</param>
    /// <returns>The projected result.</returns>
    private static Task<IResult> ProjectServerStreamAsync<TRequest, TResponse>(
        HttpContext httpContext,
        Func<DataServicesClient, TRequest, CancellationToken, IAsyncEnumerable<TResponse>> invoke,
        bool collectWithinWindow)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
        => ProjectAsync(httpContext, async (client, cancellationToken) =>
        {
            string body = await ReadRequestBodyAsync(httpContext, cancellationToken).ConfigureAwait(false);

            if (!TryBindRequest(body, out TRequest? request, out StatusProjection bindingFailure))
            {
                return RejectRequest(httpContext, bindingFailure);
            }

            // ==================================================================================
            //  THE FIRST ELEMENT IS PULLED HERE, INSIDE THE SHARED FAILURE PATH, AND THAT PLACEMENT
            //  IS THE WHOLE OF THE FIX.
            //
            //  The result this returns writes the status line and the opening bracket as its FIRST
            //  act, and the framework executes it only after this method has returned - so a stream
            //  that faulted before producing anything used to fault INSIDE the result, after 200 and
            //  `[` were already on the wire, and could not reach the translation every other route
            //  shares. A caller asking for a retrieval against a DataWindow that does not exist
            //  received `200` and a truncated array where it should have received `404`, and the
            //  streaming type's own remarks claimed the opposite.
            //
            //  Pulling the first element while still inside ProjectAsync's try means a pre-first-item
            //  RpcException, HttpRequestException or cancellation is caught by the arms above and
            //  becomes a proper problem response with nothing written. Once the first element is in
            //  hand the trade reverts to the documented one: the headers go out and a LATER fault
            //  ends the body unterminated.
            // ==================================================================================
            RestProjectionOptions projection = httpContext.RequestServices
                .GetRequiredService<IOptions<GatewayOptions>>()
                .Value
                .RestProjection;

            // ==================================================================================
            //  THE COLLECTION WINDOW IS SUPPLIED ONLY WHERE THE UPSTREAM STREAM NEVER ENDS.
            //
            //  The expression event stream is a SUBSCRIPTION - the upstream ends it when the client
            //  goes away and not before - so on an idle session the first element never arrives, the
            //  outbound pipeline's per-attempt timeout fired instead, and the caller received 500
            //  with E_INTERNAL_ERROR after ten seconds for a session that was simply quiet. Nothing
            //  had failed. Every other projected stream terminates itself, so passing null leaves
            //  those on exactly the behaviour they had.
            // ==================================================================================
            return await StreamedSequenceResult<TResponse>
                .PrefetchAsync(
                    invoke(client, request, cancellationToken),
                    projection.MaxStreamedElements,
                    collectWithinWindow ? projection.StreamCollectionWindow : null,
                    cancellationToken)
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
            // A body that IS JSON but does not map onto the message: an unknown member, a wrong shape, a
            // value the field cannot hold.
            request = null;
            failure = BindingRejection(MalformedBodyDetail);

            return false;
        }
        catch (InvalidJsonException)
        {
            // A body that is not well-formed JSON at all: a truncated document, a bad literal, a stray
            // character.
            //
            // TWO ARMS, AND THE SECOND ONE IS LOAD BEARING RATHER THAN DEFENSIVE. It would be natural to
            // assume one arm covers both cases, because the parser's two failure types read like a
            // hierarchy - but Google.Protobuf derives InvalidJsonException from System.IO.IOException and
            // NOT from InvalidProtocolBufferException, so `catch (InvalidProtocolBufferException)` alone
            // lets every malformed-JSON body escape this method entirely. Escaping it means the request
            // reaches the host's exception handler and the CALLER'S mistake is reported as HTTP 500 - this
            // service announcing its own failure for a body it correctly refused. The contract publishes
            // 400 for an unbindable body, and 400 is what a client needs in order to know not to retry.
            // Verified by exercising the parser directly: `{`, and `not json at all`, both raise
            // InvalidJsonException, while `[]` and an unknown member raise InvalidProtocolBufferException.
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
    /// <param name="httpContext">The request being handled.</param>
    /// <param name="response">The message the upstream produced.</param>
    /// <returns><c>200</c> carrying the rendered message.</returns>
    /// <remarks>
    /// The message is forwarded, not reshaped: no member is renamed, dropped, reordered into a different
    /// envelope, or reinterpreted. The four-value item-change alphabet, the tri-valued veto and the buffer
    /// discriminator therefore reach the caller exactly as the upstream produced them - this projection
    /// translates transport, not semantics.
    /// </remarks>
    private static IResult Render(HttpContext httpContext, IMessage response)
    {
        // ============ THE IN-BAND STATUS DECIDES THE HTTP STATUS ===================================
        // An upstream gRPC method can complete SUCCESSFULLY and still answer a failure: the transport says
        // OK and the message body says E_INVALID_ARGUMENT, E_BUSY or E_DB_ERROR. Rendering that as 200
        // because no RpcException was raised is the most misleading thing an ingress can do, and it is
        // worse HERE than one hop upstream: this is the SOLE PUBLIC SURFACE of the whole system, so a
        // caller that branches on the status line - which is every HTTP client - would treat a rejected
        // update as an applied one with nothing downstream left to correct it.
        //
        // THE BODY IS FORWARDED UNCHANGED EITHER WAY. On a failure the caller still receives the same
        // upstream message: the in-band code, the diagnostic and any db_error, exactly as produced. Only
        // the status LINE changes. This projection still translates transport rather than semantics
        // (constraint C-B).
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
    /// Renders an upstream response whose in-band status reports a failure, under the mapped HTTP status.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="response">
    /// The upstream message. Read for nothing but the fact of the failure; it is NOT attached to the
    /// response.
    /// </param>
    /// <param name="failure">The projection the in-band code mapped to.</param>
    /// <returns>A problem response carrying the mapped status and this gateway's own allow-listed body.</returns>
    /// <remarks>
    /// <para>
    /// 🔴 THE UPSTREAM MESSAGE IS NO LONGER ATTACHED, AND ITS ATTACHMENT WAS THE DEFECT. An earlier
    /// revision serialised the WHOLE upstream response into a <c>response</c> problem extension, on the
    /// reasoning that a caller of a failed operation needs the contract's own answer. The reasoning
    /// mistakes what this boundary is: Gateway is the system's only external ingress, and a whole
    /// upstream message is an unbounded, unreviewed payload. A relayed <c>db_error</c> carries
    /// <c>sqlsyntax</c>, which is the complete generated statement - the legacy interpolates literal
    /// values into it and its logger performs no redaction at all (AAP 0.6.4) - so the extension was a
    /// channel through which row data and internal structure could leave the system in a body nobody had
    /// screened. "It is redacted before it reaches this gateway" was doing all the work in that argument,
    /// and a disclosure control that depends on another service having got it right is not a control.
    /// </para>
    /// <para>
    /// WHAT A CALLER GETS INSTEAD IS AN ALLOW-LIST, and it is the part a client can actually act on: the
    /// mapped HTTP status, the numeric <c>retCode</c> from the legacy return-code algebra, and this
    /// gateway's own fixed detail prose. Those are declared in the published contract; the whole-message
    /// extension never was, so nothing documented is withdrawn by removing it.
    /// </para>
    /// <para>
    /// THE OPERATOR STILL GETS THE DETAIL, through the structured log record below rather than through
    /// the caller's response body - which is the right destination for it, and the one the legacy dialog
    /// (AAP 0.6.1) actually corresponded to.
    /// </para>
    /// </remarks>
    private static IResult RenderInBandFailure(
        HttpContext httpContext,
        IMessage response,
        StatusProjection failure)
    {
        ILogger logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(LoggerCategory);

        // NUMERIC ONLY. The diagnostic belongs to the caller; it can quote a statement fragment.
        logger.LogWarning(
            "A projected upstream operation answered in-band failure {RetCode}, rendered as HTTP "
                + "{HttpStatus} for {Method} {Route}. Correlation {CorrelationId}.",
            failure.RetCode,
            failure.HttpStatus,
            httpContext.Request.Method,
            DescribeRoute(httpContext),
            ResolveCorrelationId(httpContext));

        // THE MESSAGE IS DELIBERATELY NOT READ INTO THE RESPONSE. It is a parameter because the caller of
        // this method holds it and a future revision may need to inspect it here; discarding it explicitly
        // is what makes the omission legible rather than looking like a dropped line.
        _ = response;

        ProblemDetails problem = BuildProblem(httpContext, failure);

        return TypedResults.Problem(problem);
    }

    /// <summary>
    /// Forwards an upstream server stream to the response as one JSON array, element by element.
    /// </summary>
    /// <typeparam name="TResponse">The protobuf message each element carries.</typeparam>
    /// <param name="elements">The upstream stream, consumed once.</param>
    /// <param name="hasFirstElement">Whether the sequence carries a first element.</param>
    /// <param name="maximumElements">The configured bound on how many elements will be forwarded.</param>
    /// <param name="collectionWindow">How long the collection may run before it gives up.</param>
    /// <param name="callerToken">The caller's bearer token.</param>
    /// <remarks>
    /// <para>
    /// <b>NOTHING IS BUFFERED HERE.</b> Draining the entire upstream
    /// stream into a <see cref="StringBuilder"/>, calling <c>ToString</c> - a second complete copy - and
    /// handing that to a text result, which encodes a third, is the obvious implementation. For a large
    /// retrieval that is three simultaneous representations of the whole result held in this gateway's
    /// memory, and the gateway is the process every request in the system passes through. Each element is
    /// instead formatted and written straight to the response writer as it arrives, so one element is live
    /// at a time.
    /// </para>
    /// <para>
    /// <b>THE GATEWAY CAN STREAM WHERE THE DATASERVICES PROJECTION CANNOT, AND THE ASYMMETRY IS
    /// DELIBERATE.</b> That projection collects first so that a mid-stream failure can still produce a
    /// clean problem body. Here the trade is made the other way round, because the buffering it buys costs
    /// far more at the ingress: the consequence is recorded honestly below rather than glossed.
    /// </para>
    /// <para>
    /// <b>A FAILURE AFTER THE FIRST ELEMENT CANNOT CHANGE THE STATUS LINE, AND THAT IS UNAVOIDABLE ONCE
    /// STREAMING.</b> The status and headers are already sent, so an upstream fault mid-stream ends the
    /// response body without a closing bracket rather than turning into a problem document. A caller
    /// therefore detects it as malformed JSON, which is a detectable failure and not a silent one - a
    /// truncated array that closed cleanly would be indistinguishable from a complete one, and that is the
    /// outcome this deliberately does NOT produce.
    /// </para>
    /// <para>
    /// <b>⚠ A FAILURE BEFORE THE FIRST ELEMENT DOES BECOME A PROPER PROBLEM RESPONSE - AND IT IS
    /// <see cref="PrefetchAsync"/> THAT MAKES THAT TRUE RATHER THAN THIS PARAGRAPH.</b> Taking the
    /// sequence itself and pulling its first element from inside <see cref="ExecuteAsync"/> - which
    /// the framework runs AFTER the projection has returned - puts the status line and the opening bracket
    /// on the wire first, so a pre-first-item fault cannot reach the shared failure path at all.
    /// A caller retrieving a DataWindow that does not exist then receives <c>200</c> and a truncated array
    /// instead of <c>404</c>, whatever a remark like this one claims. The first element is pulled by the
    /// factory, inside the projection, and only an instance holding it can be constructed - so the
    /// distinction the paragraph above draws is enforced by the type's shape and not by convention.
    /// </para>
    /// <para>
    /// The bound is enforced while forwarding rather than after, so an unbounded upstream cannot make this
    /// gateway write an unbounded response. Exceeding it truncates the document WITHOUT a closing bracket,
    /// for the same reason: the caller must be able to tell that it did not receive everything. It is a
    /// resource bound and not a performance claim - no performance objective is asserted anywhere in this
    /// refactor (AAP 0.8.5).
    /// </para>
    /// <para>
    /// <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE STREAMING CLAIM IS
    /// TESTABLE.</b> Two properties here are assertions about behaviour rather than shape - that the
    /// sequence is never drained whole, and that exceeding the bound abandons the document WITHOUT a
    /// closing bracket - and neither is observable from outside this assembly without an upstream that can
    /// be made to overproduce on demand. Only this service's own test assembly sees it.
    /// </para>
    /// </remarks>
    internal sealed class StreamedSequenceResult<TResponse>(
        IAsyncEnumerator<TResponse> elements,
        bool hasFirstElement,
        int maximumElements,
        CancellationTokenSource? collectionWindow = null,
        CancellationToken callerToken = default) : IResult
        where TResponse : class, IMessage, new()
    {
        private static readonly byte[] ArrayOpen = "["u8.ToArray();
        private static readonly byte[] ArrayClose = "]"u8.ToArray();
        private static readonly byte[] ElementSeparator = ","u8.ToArray();

        /// <summary>
        /// Opens the upstream stream and pulls its FIRST element, then hands back a result that will
        /// forward that element and everything after it.
        /// </summary>
        /// <param name="elements">The upstream stream, enumerated once.</param>
        /// <param name="maximumElements">The configured bound on how many elements will be forwarded.</param>
        /// <param name="collectionWindow">How long the collection may run before it gives up.</param>
        /// <param name="cancellationToken">The caller's cancellation, bound into the enumeration.</param>
        /// <returns>The result to answer with.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="elements"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// <b>THIS METHOD IS WHERE A PRE-FIRST-ITEM FAULT BECOMES A PROBLEM RESPONSE.</b> It is awaited
        /// inside the projection's shared try, so an <see cref="RpcException"/>, a transport failure or a
        /// cancellation raised while opening the stream is caught and translated exactly as it is on a
        /// unary route - with nothing written, because the result that writes has not been constructed
        /// yet. Pulling the same element from inside <see cref="ExecuteAsync"/> instead put the fault
        /// after the status line, where no translation can reach it.
        /// </para>
        /// <para>
        /// <b>THE ENUMERATOR IS DISPOSED HERE IF AND ONLY IF THE PULL THROWS.</b> On success it is owned
        /// by the returned result, which disposes it on every path out of
        /// <see cref="ExecuteAsync"/> - including the bound refusal and the caller's abort. Disposing it
        /// on the failure path matters because a gRPC call enumerator holds the call: leaving it
        /// undisposed would hold the upstream call open for a request that has already been answered with
        /// a problem document.
        /// </para>
        /// <para>
        /// <b>NOTHING IS BUFFERED BY THE PREFETCH.</b> Exactly one element is pulled, which is the
        /// element that has to be pulled in order to know whether the stream can start at all. The rest
        /// are still forwarded one at a time.
        /// </para>
        /// </remarks>
        internal static async Task<IResult> PrefetchAsync(
            IAsyncEnumerable<TResponse> elements,
            int maximumElements,
            TimeSpan? collectionWindow,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(elements);

            // ==========================================================================================
            //  THE WINDOW IS A LINKED SOURCE RATHER THAN A TIMEOUT AROUND THE AWAIT, and the difference
            //  is that the enumerator is created WITH the linked token - so expiry cancels the gRPC call
            //  itself and releases it, instead of abandoning an await while the call stays open.
            // ==========================================================================================
            CancellationTokenSource? window = null;
            CancellationToken enumerationToken = cancellationToken;

            if (collectionWindow is { } budget)
            {
                window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                window.CancelAfter(budget);
                enumerationToken = window.Token;
            }

            IAsyncEnumerator<TResponse> enumerator = elements.GetAsyncEnumerator(enumerationToken);

            bool hasFirst;

            try
            {
                hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (IsWindowExpiry(failure, window, cancellationToken))
            {
                // ======================================================================================
                //  AN EXPIRED WINDOW IS AN EMPTY COLLECTION, NOT A FAULT.
                //
                //  The caller asked for the records available now and there were none, which is a
                //  complete answer to the question the operation asks - and it is the answer the
                //  streaming response type already knows how to express, so nothing here invents a
                //  second success shape. The real enumerator is disposed first because it holds the
                //  upstream call, and it is disposed defensively: disposing an enumerator whose token
                //  was just cancelled is exactly the situation in which a dispose can itself fault, and
                //  a fault there would replace a legitimate empty answer with a 500.
                // ======================================================================================
                await DisposeQuietlyAsync(enumerator).ConfigureAwait(false);

                window?.Dispose();

                return new StreamedSequenceResult<TResponse>(
                    NoElements().GetAsyncEnumerator(CancellationToken.None),
                    hasFirstElement: false,
                    maximumElements);
            }
            catch
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);

                window?.Dispose();

                throw;
            }

            // OWNERSHIP OF THE WINDOW PASSES TO THE RESULT, which disposes it on every path out of
            // ExecuteAsync - the window still bounds the REST of the collection, not merely its first
            // element, because a subscription can produce one record and then go quiet again.
            return new StreamedSequenceResult<TResponse>(
                enumerator,
                hasFirst,
                maximumElements,
                window,
                cancellationToken);
        }

        /// <summary>
        /// Decides whether a failure is the collection window expiring rather than a real fault.
        /// </summary>
        /// <param name="failure">The failure raised while enumerating.</param>
        /// <param name="window">The linked source the window cancels, or <see langword="null"/>.</param>
        /// <param name="callerToken">The caller's own cancellation.</param>
        /// <returns><see langword="true"/> when the window, and nothing else, ended the enumeration.</returns>
        /// <remarks>
        /// <para>
        /// <b>THE CALLER'S OWN ABORT MUST NOT BE SWALLOWED, which is what the second condition is for.</b>
        /// The window source is LINKED to the caller's token, so a caller that hangs up cancels it too -
        /// and treating that as a completed collection would answer 200 to a request nobody is waiting for
        /// while hiding the abort from the shared failure path. Testing the caller's token first is what
        /// keeps the two apart.
        /// </para>
        /// <para>
        /// BOTH FAILURE SHAPES ARE ACCEPTED because the gRPC client raises either one depending on where
        /// the cancellation lands: an <see cref="OperationCanceledException"/> from the await itself, or an
        /// <see cref="RpcException"/> carrying <see cref="StatusCode.Cancelled"/> from the call. A test on
        /// the token state alone would be simpler and wrong, because it would also swallow an unrelated
        /// fault that happened to arrive after expiry.
        /// </para>
        /// </remarks>
        private static bool IsWindowExpiry(
            Exception failure,
            CancellationTokenSource? window,
            CancellationToken callerToken) =>
            window is not null
            && window.IsCancellationRequested
            && !callerToken.IsCancellationRequested
            && (failure is OperationCanceledException
                || failure is RpcException { StatusCode: StatusCode.Cancelled });

        /// <summary>Disposes an enumerator whose cancellation has already been requested.</summary>
        /// <param name="enumerator">The enumerator to release.</param>
        /// <returns>A task that completes once the release has been attempted.</returns>
        /// <remarks>
        /// A FAULT HERE IS SWALLOWED AND THAT IS THE POINT: the collection has already been decided, the
        /// upstream call is already cancelled, and letting a dispose failure propagate would turn a
        /// legitimate empty answer into a server fault. Nothing is lost, because there is nothing left to
        /// read from an enumerator that is being abandoned.
        /// </remarks>
        private static async ValueTask DisposeQuietlyAsync(IAsyncEnumerator<TResponse> enumerator)
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (RpcException)
            {
            }
        }

        /// <summary>An enumerable that yields nothing, for a window that expired before the first element.</summary>
        /// <returns>An empty sequence.</returns>
        /// <remarks>
        /// A REAL ENUMERATOR RATHER THAN A NULL ONE, so the result type keeps exactly one shape and its
        /// disposal path stays unconditional. The cost is one allocation on a path that answers an empty
        /// collection.
        /// </remarks>
        private static async IAsyncEnumerable<TResponse> NoElements()
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield break;
        }

        /// <inheritdoc/>
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            CancellationToken cancellationToken = httpContext.RequestAborted;

            // OWNED FROM HERE, AND RELEASED ON EVERY PATH - the ordinary end, the bound refusal, a
            // mid-stream upstream fault and the caller's abort alike. A gRPC call enumerator holds the
            // call, so an undisposed one holds the upstream open after this response has ended. The
            // collection window's linked source is owned on exactly the same terms.
            await using IAsyncEnumerator<TResponse> owned = elements;
            using CancellationTokenSource? ownedWindow = collectionWindow;

            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = MediaTypeNames.Application.Json;

            PipeWriter writer = httpContext.Response.BodyWriter;

            await writer.WriteAsync(ArrayOpen, cancellationToken).ConfigureAwait(false);

            int written = 0;

            // The FIRST element is already in hand from the prefetch, so the loop reads Current and then
            // advances rather than advancing first. That ordering is what makes the prefetched element
            // part of the forwarded document instead of being consumed and dropped.
            bool available = hasFirstElement;

            while (available)
            {
                if (written >= maximumElements)
                {
                    // ABANDONED WITHOUT THE CLOSING BRACKET, deliberately - see the remarks. The bound is
                    // recorded so an operator can see it was this gateway that stopped, not the upstream
                    // that ended.
                    httpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger(LoggerCategory)
                        .LogWarning(
                            "A projected upstream stream exceeded the configured bound of {Limit} elements "
                                + "for {Method} {Route}; the response was abandoned without its closing "
                                + "bracket so the caller detects it as incomplete. Correlation "
                                + "{CorrelationId}.",
                            maximumElements,
                            httpContext.Request.Method,
                            DescribeRoute(httpContext),
                            ResolveCorrelationId(httpContext));

                    return;
                }

                if (written > 0)
                {
                    await writer.WriteAsync(ElementSeparator, cancellationToken).ConfigureAwait(false);
                }

                // Encoded one element at a time, so the transient string is one element wide.
                byte[] encoded = Encoding.UTF8.GetBytes(ResponseFormatter.Format(elements.Current));

                await writer.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);

                written++;

                try
                {
                    available = await elements.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception failure)
                    when (IsWindowExpiry(failure, collectionWindow, callerToken))
                {
                    // ==============================================================================
                    //  THE WINDOW ENDING A COLLECTION MID-WAY CLOSES THE ARRAY CLEANLY, and the
                    //  contrast with the bound above is deliberate rather than inconsistent. Exceeding
                    //  the element bound is a TRUNCATION the caller must be able to detect, so that
                    //  path abandons the document without its closing bracket. An expired window is a
                    //  COMPLETE answer - the caller asked for the records available now and this is
                    //  all of them - so the document is well formed and the response is an ordinary
                    //  200. Abandoning it here would tell a caller its complete answer was corrupt.
                    // ==============================================================================
                    available = false;
                }
                catch (RpcException failure)
                    when (!cancellationToken.IsCancellationRequested
                        && !callerToken.IsCancellationRequested)
                {
                    // ==============================================================================
                    //  🔴 A MID-STREAM UPSTREAM FAULT IS HANDLED HERE RATHER THAN ESCAPING.
                    //
                    //  Only the window-expiry arm above existed, so every other fault raised by this
                    //  await left ExecuteAsync - and left it AFTER the status line and the opening
                    //  bracket had gone. Measured rather than argued: killing DataServices two seconds
                    //  into a fifty-thousand-row retrieval produced
                    //  `ExceptionHandlerMiddleware[2] "The response has already started, the error
                    //  handler will not be executed"`, then
                    //  `ExceptionHandlerMiddleware[1] "An unhandled exception has occurred while
                    //  executing the request"`, then
                    //  `Kestrel[13] "An unhandled exception was thrown by the application"` - while the
                    //  access log recorded the request as a 200. So the one fault this projection cannot
                    //  translate was also the one it did not record as handled, and the operator's only
                    //  evidence was a stack trace at the connection layer.
                    //
                    //  THE CALLER'S OWN ABORT IS EXCLUDED BY THE FILTER, for the same reason
                    //  IsWindowExpiry tests the caller's token: an abort is not an upstream fault, there
                    //  is nobody left to read a sentinel, and its existing behaviour - propagating, so
                    //  the framework tears the request down - is correct and is asserted elsewhere.
                    // ==============================================================================
                    await TerminateOnUpstreamFaultAsync(
                            httpContext,
                            writer,
                            failure,
                            written,
                            cancellationToken)
                        .ConfigureAwait(false);

                    return;
                }
            }

            await writer.WriteAsync(ArrayClose, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records a mid-stream upstream fault, appends a self-describing terminal element, and ends the
        /// transfer abnormally.
        /// </summary>
        /// <param name="httpContext">The request whose response has already been committed.</param>
        /// <param name="writer">The response body writer.</param>
        /// <param name="failure">The upstream fault.</param>
        /// <param name="written">How many elements had already been forwarded.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>A task that completes once the response has been terminated.</returns>
        /// <remarks>
        /// <para>
        /// <b>THREE THINGS HAPPEN HERE, AND EACH ANSWERS A DIFFERENT AUDIENCE.</b> The log record answers
        /// the OPERATOR, whose only other evidence is a connection-layer stack trace beside an access log
        /// saying 200. The terminal element answers a HUMAN reading the bytes that did arrive, who would
        /// otherwise see a chunk indistinguishable from any other. The abnormal termination answers a
        /// PROGRAM, which detects an interrupted transfer without having to parse anything at all.
        /// </para>
        /// <para>
        /// <b>THE TERMINAL ELEMENT IS THE PROBLEM DOCUMENT THE UNARY ROUTE WOULD HAVE SENT</b>, built by
        /// the same <see cref="BuildProblem"/> from the same <see cref="ProjectStatus"/>. That is the
        /// point of reusing them rather than composing prose here: a caller comparing a pre-first-item
        /// failure with a mid-stream one gets the SAME classification, the same legacy return code and the
        /// same correlation identifier, and a later change to the status map reaches both. Two extension
        /// members are added that a unary problem cannot carry - the marker naming this as a terminated
        /// stream, and the number of elements that were delivered before it stopped.
        /// </para>
        /// <para>
        /// <b>THE ARRAY IS STILL NOT CLOSED, DELIBERATELY.</b> The truncation is the machine-readable
        /// signal, and the element bound above relies on exactly the same one - so closing the bracket
        /// here would make a faulted stream parse as a complete document whose last element happens to
        /// describe a failure, which is precisely the "reads as clean" risk this exists to remove. The
        /// element is therefore an explanation for a reader, never a substitute for the truncation.
        /// </para>
        /// <para>
        /// <b>EVERY FAILURE OF THIS METHOD IS SWALLOWED.</b> It runs because the request already failed;
        /// a fault while writing the explanation would put the escaping exception straight back, which is
        /// the defect being fixed. The abort is issued in a finally, so the transfer terminates abnormally
        /// even when the sentinel could not be written - which is the case where a caller needs the
        /// transport-level signal most.
        /// </para>
        /// <para>
        /// 🔴 <b>THE TERMINAL ELEMENT IS BEST EFFORT, AND THE ABORT IS PREFERRED OVER IT ANYWAY.</b> A
        /// flush hands bytes to the transport; it does not wait for the peer to read them. So when the
        /// consumer is BEHIND, the reset discards whatever is still queued in the socket's send buffer -
        /// including this element. Measured, not theorised: with a consumer rate-limited to 900 kB/s the
        /// element never arrived and the body was cut mid-chunk, while the same injection against a
        /// consumer keeping up delivered seven chunks and then the element intact.
        /// </para>
        /// <para>
        /// That trade is taken deliberately, because the two signals it protects are the GUARANTEED ones
        /// and this one is not. Draining first would mean completing the response normally, which writes
        /// the terminating chunk and hands a caller a transfer that ended cleanly - and a consumer that
        /// streams elements without checking for the closing bracket would then treat a partial result set
        /// as the whole answer. A silently truncated result is a correctness failure; a lost explanation is
        /// an operability one, and it is already covered: the log record above is unconditional and
        /// carries the same correlation identifier the element would have carried.
        /// </para>
        /// </remarks>
        private static async Task TerminateOnUpstreamFaultAsync(
            HttpContext httpContext,
            PipeWriter writer,
            RpcException failure,
            int written,
            CancellationToken cancellationToken)
        {
            StatusProjection projection = ProjectStatus(failure);

            httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(LoggerCategory)
                .LogError(
                    "A projected upstream stream faulted with {StatusCode} after {Delivered} forwarded "
                        + "element(s) for {Method} {Route}, which projects to {HttpStatus} and legacy "
                        + "{ReturnCode}. The 200 status line and the opening bracket had already gone, so "
                        + "no problem document could replace them: a terminal element describing the "
                        + "failure was appended, the array was left unterminated and the transfer was "
                        + "aborted, so the caller detects an answer it could not finish reading. "
                        + "Correlation {CorrelationId}.",
                    failure.StatusCode,
                    written,
                    httpContext.Request.Method,
                    DescribeRoute(httpContext),
                    projection.HttpStatus,
                    projection.RetCode,
                    ResolveCorrelationId(httpContext));

            try
            {
                ProblemDetails terminal = BuildProblem(httpContext, projection);

                terminal.Extensions[StreamTerminatedExtensionMember] = true;
                terminal.Extensions[ElementsDeliveredExtensionMember] = written;

                byte[] encoded = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(terminal, TerminalElementSerialization));

                if (written > 0)
                {
                    await writer.WriteAsync(ElementSeparator, cancellationToken).ConfigureAwait(false);
                }

                await writer.WriteAsync(encoded, cancellationToken).ConfigureAwait(false);
                _ = await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception secondary) when (secondary is OperationCanceledException
                or InvalidOperationException
                or IOException
                or ObjectDisposedException)
            {
                // The caller has gone or the response pipe is already finished. There is nobody to read
                // the explanation and nothing further to do; the abort below is still issued, and the log
                // record above has already been written.
            }
            finally
            {
                httpContext.Abort();
            }
        }
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

            // Adjudication A3, CORRECTED: a plain 500 problem document carrying E_NO_IMPLEMENTATION - no
            // reserved marker, no deferred service named or reachable from this file, and NOT 501.
            //
            // 🔴 THIS ARM USED TO ANSWER 501 AND THAT BROKE THE PUBLISHED CONTRACT TWICE OVER. Every
            // operation projected here is one gateway.v1.yaml publishes as implemented, and 501 is declared
            // on the eight reserved-route operations and nowhere else - so the status was undeclared on the
            // very operations that could produce it, leaving a generated client with no branch for it. It
            // also collapsed the one distinction the reserved routes exist to draw: a caller could not tell
            // "this whole capability area is unbuilt" (AAP 0.4.4, C-D) from "the upstream answering me does
            // not implement a method its own contract declares", which is deployment or version skew. The
            // in-band arm above answers 500 for the same reason and cites the same precedent.
            StatusCode.Unimplemented => new(
                StatusCodes.Status500InternalServerError,
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
    /// <para>
    /// <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE HEADER OBLIGATION IS
    /// TESTABLE.</b> This method is where the <c>Retry-After</c> a capacity refusal must carry is applied,
    /// and a header is not observable in the document it returns - so asserting it needs the method itself.
    /// The four call sites that answer with a problem all reach it, so exercising it here is exercising all
    /// four. Only this service's own test assembly sees it.
    /// </para>
    /// </remarks>
    internal static ProblemDetails BuildProblem(HttpContext httpContext, StatusProjection projection)
    {
        // 🔴 THE ONE HEADER A PROBLEM DOCUMENT CANNOT CARRY IN ITS BODY, APPLIED AT THE ONE PLACE EVERY
        // PROBLEM DOCUMENT IN THIS FILE IS BUILT. Retry-After is a header, so no amount of body detail
        // substitutes for it, and this gateway has FOUR paths that answer with a problem - the in-band
        // projection, the exception projection, the conflict projection and Gateway's own rejection.
        // Attaching it per path is how one of them ends up without it, which is exactly what happened:
        // both 429 paths, the in-band E_BUSY projection and an upstream ResourceExhausted, answered with
        // no header at all. See ApplyRetryAfter for why only 429 gets one.
        ApplyRetryAfter(httpContext, projection.HttpStatus);

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
    /// Sets <c>Retry-After</c> when, and only when, the response being built is a capacity refusal.
    /// </summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="httpStatus">The status the caller is about to receive.</param>
    /// <remarks>
    /// <para>
    /// <b>ONLY 429, DELIBERATELY.</b> RFC 9110 &#167;10.2.3 permits the header on a 503 and on a 3xx as
    /// well, and it is withheld from both here. A 503 from this gateway means an upstream is unreachable
    /// rather than at a ceiling, and nothing in this system knows when an unreachable service will return -
    /// so a delta there would be a fabricated availability promise, which no part of this refactor may
    /// assert (AAP 0.8.5). 429 is different in kind: the refusal is a CONCURRENCY CEILING that clears when
    /// a session or handle is released, so a short hint is a genuine statement about the shape of the
    /// condition rather than a guess about a service's recovery.
    /// </para>
    /// <para>
    /// DELTA-SECONDS, whole and rounded UP, so a sub-second configured value can never render as <c>0</c>
    /// and tell a caller to retry immediately - the one answer a capacity refusal must not give.
    /// </para>
    /// <para>
    /// THE HEADER IS SET RATHER THAN APPENDED, so a value already present - from a middleware, or from a
    /// second pass over the same response - is replaced instead of producing two conflicting deltas.
    /// </para>
    /// <para>
    /// A RESPONSE THAT HAS ALREADY STARTED IS LEFT ALONE. Headers cannot be changed once sent, and the one
    /// route that can fail after its status line is the streamed retrieval; touching them there would throw
    /// inside a failure path. The guard is what keeps this safe to call from the single choke point.
    /// </para>
    /// </remarks>
    private static void ApplyRetryAfter(HttpContext httpContext, int httpStatus)
    {
        if (httpStatus != StatusCodes.Status429TooManyRequests || httpContext.Response.HasStarted)
        {
            return;
        }

        TimeSpan configured = httpContext.RequestServices
            .GetRequiredService<IOptions<GatewayOptions>>()
            .Value
            .RestProjection
            .RetryAfter;

        long seconds = (long)Math.Ceiling(configured.TotalSeconds);

        httpContext.Response.Headers.RetryAfter =
            Math.Max(seconds, 1L).ToString(CultureInfo.InvariantCulture);
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
            operation.BadRequest switch
            {
                BadRequestDeclaration.CrossSessionReferenceBlocked =>
                    CrossSessionReferenceBlockedDescription,

                // NAMED SEPARATELY BECAUSE THESE THREE OPERATIONS TAKE NO REQUEST BODY, so the shared
                // wording - which is about a body this gateway could not bind - would describe something
                // that cannot happen on them.
                BadRequestDeclaration.SessionIdConstraint => SessionIdConstraintDescription,

                _ => BadRequestDescription,
            });

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
    /// Guarded rather than unconditional: only the TWO session-close operations carry a path parameter -
    /// <c>DELETE /v1/datawindow/sessions/{sessionId}</c> and
    /// <c>DELETE /v1/datawindow/expression/sessions/{sessionId}</c>. Every other projected operation
    /// declares none at all, and declaring one on them would publish an argument they do not accept.
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
        /// The operation declares no <c>400</c> at all.
        /// </summary>
        /// <remarks>
        /// NO MEMBER CARRIES THIS TODAY, and it is retained rather than deleted because it is the correct
        /// declaration for a future operation that takes neither a body nor a parameter with declared
        /// constraints. The two close operations and the event-gate read are the tempting carriers, on the
        /// reasoning that an operation with no request body has nothing for Gateway's own binding to
        /// reject - which overlooks their session-identifier PARAMETER, whose declared
        /// <c>required</c>/<c>minLength</c>/<c>maxLength</c> constraints Gateway does evaluate. See
        /// <see cref="SessionIdConstraint"/>.
        /// </remarks>
        None,

        /// <summary>
        /// The operation declares the <c>400</c> raised by its own session-identifier parameter contract:
        /// an omitted, empty or over-long identifier.
        /// </summary>
        /// <remarks>
        /// DISTINCT FROM <see cref="Standard"/> BECAUSE THE REASON IS DIFFERENT AND THE PROSE SHOULD SAY
        /// SO. These three operations take no request body at all, so their <c>400</c> can only ever be
        /// about the identifier - and a consumer reading "a body this gateway could not bind" on an
        /// operation with no body would be reading a description of something that cannot happen.
        /// </remarks>
        SessionIdConstraint,

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

    // One translated failure: the HTTP status, the legacy return code, the fixed prose, whether an upstream
    // produced it, and the problem type and title where a specific one applies.
    // HttpStatus: The HTTP status to answer with.
    // The legacy return code, consumed as a symbol from the single transcription of the oracle's
    // catalogue.
    // FIXED PROSE. Never composed from an upstream message, an exception message, a parser diagnostic or
    // any part of a request body.
    // Whether an upstream produced the failure. The contract requires the upstream member be absent
    // when Gateway itself produced the response, so this is a real statement and not a default.
    // A specific problem type, or null for RFC 9457's own default. Only the three
    // mutually distinguishable conflict-class outcomes carry one.
    // A specific title, or null to use the status code's reason phrase, which is what
    // RFC 9457 recommends alongside its default type.
    // ==================================================================================================
    //  THE IN-BAND STATUS MAP - THE OTHER HALF OF THE PROJECTION
    // ==================================================================================================

    /// <summary>
    /// Reads the in-band outcome an upstream response carries and maps a failure onto its HTTP answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>WHY THIS EXISTS.</b> The exception map below translates an upstream call that FAILED. This one
    /// translates an upstream call that SUCCEEDED and answered a failure in its body, which the contract
    /// does constantly: <c>OperationStatus.ret_code</c> and the bare <c>ret_code</c> fields carry the whole
    /// <c>E_*</c> band on a perfectly healthy transport. Without this, every one of those left this
    /// gateway as <c>200 OK</c>.
    /// </para>
    /// <para>
    /// <b>IT MIRRORS THE DATASERVICES COPY DELIBERATELY, AND THAT IS ARCHITECTURAL RATHER THAN
    /// DUPLICATION BY NEGLECT.</b> Both projections need the same translation. Putting one copy in the
    /// contracts project would make it a shared-code back door - that project carries the boundary
    /// DEFINITION and no behaviour - and putting it in a shared library would be behaviour crossing a
    /// service boundary, which is precisely the coupling the decomposition forbids (AAP 0.7.2). What keeps
    /// the two together is the published contract, not a reference; the mapping itself is stated in the
    /// contract documentation.
    /// </para>
    /// <para>
    /// <b><see langword="internal"/> RATHER THAN <see langword="private"/> SO THE MAP ITSELF IS TESTABLE.</b>
    /// The risk here is a code sent to the wrong status or a tri-state value misclassified as a failure,
    /// and neither is observable from outside without a live upstream producing that exact code. Only this
    /// service's own test assembly can see it; C-A forbids any other reaching in.
    /// </para>
    /// </remarks>
    internal static class InBandStatus
    {
        /// <summary>
        /// The problem-body member the upstream response USED to be attached under, retained only so a
        /// test can assert its absence.
        /// </summary>
        /// <remarks>
        /// NOTHING WRITES THIS ANY MORE. The whole upstream message was serialised into a problem
        /// extension under this name, which made an unbounded, unscreened upstream payload part of a
        /// response crossing the system's only external boundary - a relayed <c>db_error</c> carries the
        /// complete generated statement with interpolated literal values. The constant survives because
        /// the guarantee worth holding is that the member does NOT appear, and a test asserting absence
        /// needs the name.
        /// </remarks>
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
        /// <param name="response">The upstream response message.</param>
        /// <param name="failure">The projection, when the message reports a failure.</param>
        /// <returns>
        /// <see langword="true"/> when the message carries a failing outcome; otherwise
        /// <see langword="false"/>, and the message is forwarded as an ordinary success.
        /// </returns>
        /// <remarks>
        /// <b>THE FAILURE TEST IS THE PORTED PREDICATE AND THE TRI-STATE HOLE IS PRESERVED (C-B).</b> The
        /// legacy algebra tests failure as strictly less than zero WITH CANCELLED EXPLICITLY EXCLUDED
        /// [<c>isfailed.srf:L11-L13</c>], so three outcomes are not failures and each for its own reason:
        /// <c>OK</c> is zero, <c>PREVENT</c> is 1 and reads as a SUCCESS in that algebra, and
        /// <c>CANCELLED</c> is -2 and is neither succeeded nor failed. Rendering a prevention or a
        /// cancellation as an HTTP failure would be this gateway inventing a classification the contract
        /// does not make - and inventing it at the system's only public surface.
        /// </remarks>
        internal static bool TryProjectFailure(IMessage response, out StatusProjection failure)
        {
            failure = default;

            if (!TryReadOutcome(response, out long retCode, out string? errorText))
            {
                return false;
            }

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
        /// BY DESCRIPTOR RATHER THAN BY TYPE SWITCH. Well over a hundred response messages are projected,
        /// and a switch over them would be a list that silently stops covering new ones. The nested
        /// <c>OperationStatus</c> shape is tried first because it is the richer form.
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
        /// THE MAPPING MIRRORS THE EXCEPTION MAP BELOW so one condition cannot leave this gateway under two
        /// different HTTP statuses depending on which channel reported it: a concurrency mismatch is
        /// <c>409</c> whether it arrived as <see cref="StatusCode.Aborted"/> or as <c>E_RETRY</c> in a
        /// body. It also matches the DataServices projection's own map, so a caller sees one answer
        /// regardless of which surface it reached.
        /// </para>
        /// <para>
        /// THE DEFAULT IS <c>500</c> AND NOT <c>400</c>: an unrecognised outcome is a contract this
        /// projection has not been taught, which is a fault on this side of the boundary, and blaming the
        /// caller would send it into a retry-with-different-input loop that can never succeed.
        /// </para>
        /// <para>
        /// 🔴 THE UPSTREAM DIAGNOSTIC IS NEVER RELAYED, AND THE PARAMETER IS ACCEPTED ONLY SO THE CALLER
        /// NEED NOT KNOW THAT. An earlier revision preferred the upstream's own text whenever it had one,
        /// which put text this gateway does not control into a body crossing the system's only external
        /// boundary - and the legacy diagnostics name DataWindow objects, columns and buffer positions,
        /// while the SQL error path's <c>sqlsyntax</c> carries the whole generated statement with
        /// interpolated literal values. The detail is now always this gateway's own fixed prose, one
        /// string per recognised outcome, and the numeric <c>retCode</c> - which is what a client
        /// branches on - is carried unchanged.
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
                // answered 500, telling a caller that this gateway had failed and inviting it to retry an
                // identical request that can never succeed.
                RetCode.E_INVALID_DATA =>
                    (StatusCodes.Status400BadRequest, InBandInvalidDataDetail),

                // ⚠ E_INVALID_DATAOBJECT JOINS THE ARGUMENT-REJECTION ARM, AND IT IS 400 RATHER THAN 404
                // FOR A SPECIFIC REASON. It is what the upstream answers when the DataWindow name a
                // request carried resolves to nothing - and the RETRIEVAL side answers the SAME mistake
                // with the oracle's own E_INVALID_ARGUMENT [n_cst_thread_task_sqlquery.sru:L554], which is
                // already 400 here. One caller mistake must not produce two different statuses depending
                // on which verb was used, so the update side is aligned to the retrieval side rather than
                // to the handle family below. Falling to the default answered 500 for a retrieval and 502
                // for an update, neither of which tells a caller that it named a DataWindow that does not
                // exist. 404 was considered and rejected: the name is a member of the request BODY, not
                // the request target, and 422 is closed to this document by docs/CONTRACTS.md 12.1.
                RetCode.E_INVALID_DATAOBJECT =>
                    (StatusCodes.Status400BadRequest, InBandInvalidDataObjectDetail),

                // ⚠ E_NOT_EXISTS JOINS THE NOT-FOUND FAMILY for the same reason its two siblings are
                // already in it: the request named something the upstream could not find. A 500 here
                // reported a fault where the honest answer is that the named thing is not there.
                //
                // ⚠ AND SO DO E_VAR_NOT_FOUND AND E_MEMBER_NOT_FOUND, which are the column-expression
                // service's own not-found codes: E_VAR_NOT_FOUND is what it answers for a variable name no
                // global-variable table carries [n_cst_dwsvc_columnexp.sru, the of_GetVar family] and
                // E_MEMBER_NOT_FOUND for a member it cannot bind. Both are a caller naming something that
                // is not there - the identical situation to the three codes above - yet both fell to the
                // default and reported HTTP 500, which told the caller this gateway had failed and invited
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

                // ⚠ THE UNAVAILABLE-CAPABILITY PAIR IS 500 AND MUST NEVER BE 501. Both codes say "this
                // operation exists and the specific cell it was asked for has no available
                // implementation" - a pinyin comparison whose lookup table lives only inside the closed
                // binary [PinyinFirstLetterMatcher], a macro or foreign-variable arm the expression
                // engine declines [ColumnExpressionEngine], a pinyin flag mask that disagrees with the
                // configured one [DataWindowService.ApplyDropDownSearch]. Every one of those is reported
                // by an operation this document PUBLISHES AS IMPLEMENTED.
                //
                // 501 IS RESERVED SYSTEM-WIDE for Gateway's four deferred-capability routes, whose whole
                // purpose is to declare that an entire capability area is unbuilt (AAP 0.4.4, C-D). A 501
                // here would present an implemented operation as a placeholder and would leave a caller
                // unable to tell a reserved route from a blocked cell inside a live service, using only
                // the published contract. It was also UNDECLARED: gateway.v1.yaml carries 501 on the
                // eight reserved-route operations and on no other, so a generated client had no branch
                // for the status this arm produced.
                //
                // 500 is the declared status every projected operation already publishes, and the legacy
                // vocabulary carries the distinction the status cannot: the problem document's retCode
                // member names E_NO_SUPPORT (-2000) or E_NO_IMPLEMENTATION (-2001) exactly as the
                // upstream reported it. This is the SAME resolution Security applies to its two
                // symmetric-cipher narrowings, which answer 500 with E_NO_IMPLEMENTATION for the same
                // reason - see OpenApi/security.v1.yaml, CryptoSymCryptMode, and docs/CONTRACTS.md 14.4.
                // It is not 400: the request is well formed and the limitation is this port's rather than
                // the caller's. It is not 502 either: no upstream FAILED - it answered normally and
                // reported a capability it does not have.
                RetCode.E_NO_SUPPORT or RetCode.E_NO_IMPLEMENTATION =>
                    (StatusCodes.Status500InternalServerError, InBandNotImplementedDetail),

                RetCode.E_DB_ERROR or RetCode.E_INVALID_TRANSACTION =>
                    (StatusCodes.Status502BadGateway, InBandDataPathDetail),

                // ⚠ THE GENERIC LEGACY FAILURE IS AN UPSTREAM FAILURE, NOT A GATEWAY ONE.
                //
                // FAILED = -1 [retcode.sru] is the oracle's unspecific failure and the upstream really
                // answers it - so it is a RECOGNISED outcome, and letting it fall to the default was the
                // one arm where the default's own reasoning did not hold: the default is 500 because an
                // UNRECOGNISED code is a contract this projection has not been taught, which is a fault on
                // this side of the boundary. A code this projection recognises, reported by a service that
                // answered normally, is the opposite situation. 502 is the declared status whose meaning is
                // "the service behind me failed", and it is what sends an operator to the right service.
                //
                // 422 WOULD HAVE BEEN THE INTUITIVE CHOICE AND IS FORBIDDEN. The status surface of this
                // document is closed to the set docs/CONTRACTS.md 12.1 sanctions plus 502 and 503, and the
                // contracts suite asserts that 422 appears nowhere; adding one would be this projection
                // overriding its own specification.
                RetCode.FAILED => (StatusCodes.Status502BadGateway, InBandGenericFailureDetail),

                _ => (StatusCodes.Status500InternalServerError, InBandUnclassifiedDetail),
            };

            // ⚠ THE DETAIL IS ALWAYS THIS GATEWAY'S OWN FIXED PROSE. THE UPSTREAM TEXT IS NEVER RELAYED.
            //
            // This used to read `IsNullOrWhiteSpace(errorText) ? mapped.Detail : errorText`, preferring the
            // upstream's own diagnostic whenever it had one. That put text this gateway does not control
            // into a response body crossing the system's only external boundary, and the legacy diagnostics
            // it carries are not written for that audience: they are internal operator messages that name
            // DataWindow objects, column identifiers and buffer positions, and the SQL error path's own
            // `sqlsyntax` field carries the complete generated statement including interpolated literal
            // VALUES with no redaction anywhere in the legacy logger (AAP 0.6.4). Relaying them
            // discloses the upstream's internal structure to an external caller and makes the response
            // body an exfiltration channel for row data that never had to cross this boundary.
            //
            // THE NUMERIC CODE IS STILL CARRIED, WHICH IS WHAT A CALLER ACTUALLY BRANCHES ON. `retCode` is
            // the legacy return-code algebra's own value and it is published in the contract, so a client
            // loses no ability to distinguish outcomes - it loses only prose it could not parse. The fixed
            // detail per arm is the allow-list: a closed set of strings this gateway authored, one per
            // recognised outcome.
            //
            // THIS IS NOT A BEHAVIOUR CHANGE THE LEGACY WOULD HAVE NOTICED (C-B). The legacy had no
            // process boundary and no external caller at all - these diagnostics went to a MessageBox on
            // the operator's own screen [AAP 0.6.1, the dialog call sites]. Choosing not to forward an
            // internal operator message to an anonymous network caller preserves nothing and discloses
            // nothing; the diagnostic still reaches an operator through this gateway's own structured log,
            // which is where the equivalent of that dialog now lives.
            _ = errorText;

            return new StatusProjection(
                mapped.HttpStatus,
                retCode,
                mapped.Detail,
                FromUpstream: true);
        }
    }

    // --------------------------------------------------------------------------------------------------
    //  IN-BAND FAILURE PROSE - the allow-list, and the ONLY detail an in-band failure ever carries
    // --------------------------------------------------------------------------------------------------

    /// <summary>Fallback prose for a rejected argument reported in band.</summary>
    private const string InBandInvalidArgumentDetail =
        "The upstream operation rejected an argument. Its own outcome code is on the retCode member.";

    /// <summary>Fallback prose for an out-of-range or out-of-bound outcome reported in band.</summary>
    private const string InBandOutOfRangeDetail =
        "The upstream operation rejected a value outside its permitted range.";

    /// <summary>Fallback prose for a DataWindow name that resolved to nothing.</summary>
    /// <remarks>
    /// It names the member at fault, because the whole point of classifying this separately from the
    /// generic argument rejection is that a caller can act on it without reading a log.
    /// </remarks>
    private const string InBandInvalidDataObjectDetail =
        "The upstream operation could not resolve the DataWindow the request named. The handle is the "
            + "caller's own name for a DataWindow and is never created implicitly, so check the "
            + "datawindowHandle member against the DataWindows this deployment carries.";

    /// <summary>Fallback prose for an access refusal reported in band.</summary>
    private const string InBandAccessDeniedDetail = "The upstream operation refused this caller.";

    /// <summary>Fallback prose for an unknown handle or missing object reported in band.</summary>
    private const string InBandNotFoundDetail =
        "The upstream operation could not resolve the handle or object named in the request.";

    /// <summary>Fallback prose for a payload the upstream could not apply, reported in band.</summary>
    /// <remarks>
    /// 400 rather than 500: the request's own DATA is what the upstream rejected, so the caller can correct
    /// it. The upstream's own diagnostic replaces this prose whenever it supplied one, which on the update
    /// path is the legacy sentence itself.
    /// </remarks>
    private const string InBandInvalidDataDetail =
        "The upstream operation refused the data carried in the request. On the update path this is the "
        + "buffered carrier failing validation before any statement is generated, so nothing was applied "
        + "and re-sending the same payload will be refused again.";

    /// <summary>Fallback prose for the oracle's unspecific failure reported in band.</summary>
    /// <remarks>
    /// 502 rather than 500, for the reason recorded on the arm: the service BEHIND this gateway reported a
    /// failure, and this gateway did not fail. The distinction decides which service an operator
    /// investigates.
    /// </remarks>
    private const string InBandGenericFailureDetail =
        "The upstream operation reported the legacy unspecific failure. It answered normally and reported "
        + "that it could not complete the request; this gateway relayed that answer unchanged and carries "
        + "the originating return code on the retCode member.";

    /// <summary>Fallback prose for a retryable conflict reported in band.</summary>
    /// <remarks>
    /// 409, the SAME status an Aborted receives, so a concurrency mismatch answers one way regardless of
    /// which channel reported it. There is no silent overwrite on either path.
    /// </remarks>
    private const string InBandRetryDetail =
        "The upstream operation was rejected and can be retried. When it carries a conflict, the current "
        + "row state is on the relayed response and the caller implements an explicit retry-or-surface "
        + "policy; this gateway performs no retry of its own.";

    /// <summary>Fallback prose for a busy resource reported in band.</summary>
    private const string InBandBusyDetail =
        "The upstream resource is in use by an operation already in flight. The request was not applied "
        + "and can be retried.";

    /// <summary>Fallback prose for a timeout reported in band.</summary>
    private const string InBandTimeoutDetail =
        "The upstream operation did not complete within its budget.";

    /// <summary>Fallback prose for an unsupported or unimplemented outcome reported in band.</summary>
    /// <remarks>
    /// 500 rather than 501, and the sentence says so, because a caller reading only this response has to be
    /// able to tell this apart from a reserved deferred-capability route. 501 belongs to those four routes
    /// alone (AAP 0.4.4, C-D); an implemented operation reporting that one cell of its surface has no
    /// available implementation is a different statement, and the <c>retCode</c> member carries which of the
    /// two legacy codes was reported.
    /// </remarks>
    private const string InBandNotImplementedDetail =
        "The upstream operation is implemented but reported that the specific capability this request "
        + "asked for has no available implementation, so it was not performed. Re-sending the same request "
        + "produces the same answer. This is NOT a reserved deferred-capability route - those are the only "
        + "routes in this system that answer 501, and this operation is implemented and answering. The "
        + "retCode member carries the upstream's own code.";

    /// <summary>Fallback prose for a data-path failure reported in band.</summary>
    /// <remarks>
    /// 502 rather than 500: the failure is the data path BEHIND this gateway answering badly, not this
    /// gateway faulting, and the two call for different investigations.
    /// </remarks>
    private const string InBandDataPathDetail =
        "The data path reported a failure. Any driver-level detail travels on the relayed response.";

    /// <summary>Fallback prose for an in-band outcome this projection does not classify.</summary>
    private const string InBandUnclassifiedDetail =
        "The upstream operation reported a failure this projection does not classify. Its own outcome code "
        + "is on the retCode member.";

    internal readonly record struct StatusProjection(
        int HttpStatus,
        long RetCode,
        string Detail,
        bool FromUpstream,
        string? Type = null,
        string? Title = null);
}

/// <summary>
/// The RUNTIME document's summary of every projected request and response body, whose authoritative
/// member-by-member schema is published by <c>OpenApi/gateway.v1.yaml</c> under the name the operation's
/// <c>x-proto-request</c> or <c>x-proto-response</c> extension gives.
/// </summary>
/// <remarks>
/// <para>
/// <b>⚠ THIS TYPE IS A SUMMARY, AND THE PUBLISHED CONTRACT IS NOT.</b> The document served from
/// <c>/openapi/v1.json</c> is a convenience mirror for whoever is holding this service; the CONTRACT a
/// consumer is given is the authored <c>gateway.v1.yaml</c> in <c>PowerFramework.Contracts</c>. That
/// document publishes all 118 messages and 15 enums of the projected closure CONCRETELY - every member,
/// its canonical JSON name, its canonical scalar encoding, <c>additionalProperties: false</c>, and a
/// <c>required</c> list stating what the wire actually carries - and the sibling test project compares
/// every schema against its compiled descriptor on each build, so it cannot drift from the protocol
/// definition.
/// </para>
/// <para>
/// <b>WHAT A CONSUMER MUST NOT INFER FROM THE OPEN SHAPE BELOW.</b> The extension-data member makes this
/// an open object in the generated document, and that openness describes THIS DOCUMENT'S SILENCE about
/// the members - never a permissiveness in the projection. <c>RequestParser</c> is
/// <c>JsonParser.Default</c>, whose <c>IgnoreUnknownFields</c> is false, so a member the target message
/// does not declare is answered with <c>400</c> rather than discarded - adjudication A7. The same open
/// shape in the AUTHORED contract would be a defect rather than a convenience, because a contract's
/// audience has nothing else to read - which is why that document publishes the concrete tier.
/// </para>
/// <para>
/// <b>Why the summary remains here rather than being generated too.</b> Building the concrete schemas at
/// runtime would put a third derivation of the same descriptors in a third place, and constraint C-A
/// leaves no shared home for one: a service may not reach into another service's code, and
/// <c>PowerFramework.Contracts</c> carries no behaviour. Adjudication A8's mechanical obstacle also
/// applies to the five MIRRORED response shapes - they are built on the legacy's preserved
/// <c>SCREAMING_SNAKE</c> value sets, and declaring those identifiers in a Gateway file is a build error
/// under the repository-root analyzer configuration.
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

/// <summary>
/// The published shape of the <c>409</c> body: the problem object plus the optimistic-concurrency conflict
/// detail, mirroring the authored contract's <c>ConflictProblemDetails</c> schema.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE ONE PROJECTED BODY IN THIS FILE THAT IS NOT PUBLISHED AS A BARE PROBLEM, AND THE REASON IS
/// BEHAVIOURAL.</b> Every other failure body a consumer only has to READ; this one a consumer has to ACT
/// on. On an <c>updatewhereclause</c> mismatch the caller must choose between retrying and surfacing, and
/// it can only construct a retry if it can see which column moved underneath it. Registering the 409 as
/// <c>ProducesProblem</c> published the bare problem shape, so the generated document gave no indication
/// that a <c>conflict</c> member is present at all - and a caller reading only that document would
/// re-send the same stale original values and receive the same 409 for ever
/// [AAP §0.6.3.8: callers implement an explicit retry-or-surface policy, and there is no silent overwrite
/// anywhere].
/// </para>
/// <para>
/// <b>THE MEMBER IS TYPED AS THE GENERATED <c>common.v1.ConflictDetail</c> RATHER THAN TRANSCRIBED.</b>
/// Transcribing it would mean restating <c>ConflictDetail</c>, <c>ConflictRow</c>, <c>ColumnValue</c>,
/// <c>AnyValue</c> and its nested value types here, five levels of a second source of truth with nothing
/// keeping it in step with <c>Proto/common.v1.proto</c> - and the first divergence would be silent, on the
/// one payload where a silent divergence stops a caller retrying. Pointing at the generated type makes the
/// published schema DERIVED from the protocol definition.
/// </para>
/// <para>
/// <b>⚠ THIS TYPE DESCRIBES THE BODY; IT DOES NOT PRODUCE IT.</b> <c>ProjectConflict</c> renders the
/// detail through the canonical protobuf JSON mapping and attaches it as a problem extension member, and
/// that is deliberate rather than an inconsistency: the canonical mapping is what makes 64-bit fields
/// arrive as JSON strings and what a consumer of the upstream contract already parses, so serialising the
/// generated type through the default JSON options instead would change the emitted body. The member name
/// below is taken from the same constant the projection writes, so the published schema and the emitted
/// body cannot disagree about it. The sibling projection in DataServices resolves it the same way, for the
/// same reason.
/// </para>
/// <para>
/// Both value sets travel on every row, which is a requirement rather than a convenience: the one updatable
/// DataWindow in the legacy estate declares <c>updatewhere=1</c> and marks all six of its columns
/// <c>updatewhereclause=yes</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], and
/// <c>updatewhere=1</c> is the "key and updateable columns" mode - so the generated WHERE clause compared
/// the key column PLUS the original value of every updateable column. Current values alone could not
/// express what the failed statement actually compared.
/// </para>
/// <para>
/// A CLASS RATHER THAN A RECORD, unlike <see cref="ProtoPayload"/> above: it extends the framework's
/// <see cref="ProblemDetails"/> so the problem members and the <c>retCode</c> extension stay
/// single-sourced, and a record may only inherit from another record.
/// </para>
/// </remarks>
public sealed class ConflictProblemDetails : ProblemDetails
{
    /// <summary>
    /// The conflict detail exactly as it arrived, field for field with <c>common.v1.ConflictDetail</c>.
    /// </summary>
    /// <remarks>
    /// Integer members of the referenced message are 64-bit and are emitted as JSON STRINGS, because that
    /// is what the canonical protobuf JSON mapping requires of a 64-bit field; the authored contract
    /// expresses the same fact as an <c>[integer, string]</c> union. A consumer parsing them as unquoted
    /// numbers will fail on real traffic.
    /// </remarks>
    [JsonPropertyName(ConflictMemberName)]
    public ConflictDetail? Conflict { get; init; }

    /// <summary>
    /// The member name, matching the extension member the projection actually writes so the published
    /// schema and the emitted body cannot disagree.
    /// </summary>
    private const string ConflictMemberName = "conflict";
}
