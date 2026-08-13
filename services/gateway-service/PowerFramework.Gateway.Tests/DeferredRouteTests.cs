// ======================================================================================================
//  DeferredRouteTests - the four reserved Phase 2 extension points, and the adjacent live surface
// ======================================================================================================
//
//  THE ONE SENTENCE A REVIEWER IS LOOKING FOR (constraints C-D and C-K)
//  ----------------------------------------------------------------------------------------------------
//  `/v1/design/**`, `/v1/documents/**`, `/v1/integration/**` and `/v1/scripting/**` ARE ROUTING METADATA
//  AND NOTHING ELSE. Behind not one of them is there a service directory, a project file, a container
//  definition, a test project, a partial implementation, or an exception-throwing placeholder class for
//  DesignSystem, Documents, Integration or ScriptBridge. This file therefore asserts THE CONTRACT of
//  four route declarations - the status, the machine-readable body, the named destination, the marker and
//  the return code - and it does not, anywhere, test a deferred service. It cannot: there is nothing to
//  test, which is precisely the property being pinned.
//
//  The corollary is mechanical and self-checkable, which is why it is worth stating as a negative:
//  NO TYPE BELONGING TO ANY DEFERRED SERVICE IS IMPORTED, NAMED, INSTANTIATED OR REFERENCED HERE. There
//  is no `PowerFramework.DesignSystem`, `.Documents`, `.Integration` or `.ScriptBridge` assembly,
//  project, namespace, class or enum anywhere in the repository, and this file must never be the thing
//  that motivates creating one. The four names appear below EXCLUSIVELY as string data - `[Theory]` rows
//  and expected values in body assertions - never as a type, never through `nameof`, and never as an
//  enumeration declared for the purpose. Naming a destination is the permitted metadata that makes the
//  eventual system legible from Gateway's own contract; implementing any part of it is what C-D forbids,
//  and asserting a string is not implementing anything.
//
//  501 IS THE CORRECT ANSWER, NOT A DEFECT AWAITING REPAIR (constraint C-B). No test here expects a
//  working response from a reserved route, and none treats the not-implemented status as something to be
//  fixed. The contract is asserted as it is.
//
//  WHAT IS AUTHORITATIVE, AND WHERE IT DISAGREES WITH ITS OWN PROSE
//  ----------------------------------------------------------------------------------------------------
//  `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` is the wire authority for every observable
//  detail asserted below. Its `ReservedForPhaseTwo` response declares the reserved body under
//  `application/json` with schema `ReservedRouteBody`, NOT under `application/problem+json` - that media
//  type is reserved in the same document for the `ProblemDetails` family that every 4xx and 5xx uses. A
//  reserved route is a declaration with a fixed shape rather than a fault to be diagnosed, so the two
//  bodies are deliberately different shapes and this file asserts each against its own.
//
//  What matters either way, and what is asserted, is the substance: the body is MACHINE-READABLE. It is
//  parsed and its members are asserted individually. Nothing here substring-matches a rendered page, and
//  nothing infers a member's presence from prose.
//
//  THE LEGACY ANCHOR
//  ----------------------------------------------------------------------------------------------------
//  `ws_objects/pfw.shared.pbl.src/retcode.sru:L78` declares `Constant Long E_NO_IMPLEMENTATION = -2001`,
//  with `E_NO_SUPPORT = -2000` beside it at `:L77` as a DIFFERENT statement - "this framework does not
//  support that" rather than "this is not implemented here". The reserved body carries the former, and
//  this file consumes it as the symbol `RetCode.E_NO_IMPLEMENTATION` from
//  `shared/PowerFramework.Shared.Kernel/RetCode.cs` rather than writing `-2001`. The identifier spelling
//  is preserved from the oracle because these values appear in serialized payloads, in log records and in
//  characterization recordings, where a rename would silently invalidate every stored comparison.
//
//  `ws_objects/**` IS READ ONLY (constraint C-C). It is the behavioural oracle. The 47 `w_test_*.srw`
//  windows and `ws_objects/pfw.pbl.src/pfw.sra` - always cited by full path, because a second, unrelated
//  file of the same name is the PowerBuilder packager application - were read for scenarios. Not one line
//  of either was translated into a test, and neither was edited, moved or reformatted.
//
//  THE ADJACENT LIVE SURFACE, AND WHY IT BELONGS IN THIS FILE
//  ----------------------------------------------------------------------------------------------------
//  `/v1/datawindow/**` is the other half of what a caller meets at this ingress, and its decisive
//  behaviour is STATUS TRANSLATION: gRPC `Aborted` becomes HTTP `409`, carrying the upstream's own
//  `ConflictDetail` unchanged. 409 rather than 500 is the whole point (C-K). A 500 says "this service
//  failed"; a 409 says "your update was refused because the row moved underneath you, here is what it
//  looks like now, decide whether to rebase or surface it". The legacy's sole updatable DataWindow
//  carries `updatewhere=1` with all six columns marked `updatewhereclause=yes`
//  [`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14`], so the concurrency check spans every marked
//  column's ORIGINAL value - which is why the payload must survive the boundary whole, with both the
//  current and the original values per row, and why summarising it would leave a caller choosing between
//  a blind overwrite and a spurious failure. Nothing is swallowed, nothing is auto-retried, nothing is
//  degraded to a generic 500, and A CONFLICT NEVER SURFACES AS A SUCCESS. There is no silent overwrite
//  anywhere in this system, and this is where that guarantee is observed from the outside.
//
//  HOW IT RUNS WITH NOTHING BEHIND IT
//  ----------------------------------------------------------------------------------------------------
//  On `GatewayTestHostFixture` from `AuthorizationTests.cs`, consumed as a CLASS fixture. A collection
//  fixture would serialise every test in every class that joined the collection; these must stay
//  parallel-safe across classes. The fixture's substitution of `IFrameworkRuntime` is what makes any of
//  this possible: `pfwinitialize.srf:L3` and `pfwfinalize.srf:L3` both declare their function object
//  `native "pfw.dll"`, a closed-source Win32 binary that does not exist inside the Linux container this
//  service ships as, so without the seam the host could not start and every test here would fail at once.
//
//  NO DATABASE, NOT EVEN AN IN-MEMORY ONE (constraint C-E). Gateway holds no storage provider and this
//  file adds none. The conflict is produced by a substituted upstream client, never by a store. No
//  container, no live sibling service, and no socket: the fixture forces an unreachable primary handler
//  onto every factory-created client rather than trusting that nothing dials out.
//
//  NO KEY MATERIAL (constraint C-F). Every credential presented below is minted by the fixture from a key
//  it generates per instance with the platform's cryptographic generator. There is no PEM block, no
//  base64 DER, no certificate, no passphrase and no connection string in this file.
//
//  DETERMINISM. The clock inside the host is frozen by the fixture, the substituted upstream answers from
//  data written in the test, and the trailer key the conflict travels under is READ FROM THE GENERATED
//  DESCRIPTOR rather than restated - so a contract revision that renamed it would fail these tests
//  instead of quietly bypassing them. Every test is order-independent: the two that need to script an
//  upstream own their host rather than mutating the shared one.
//
//  NO PERFORMANCE ASSERTION APPEARS ANYWHERE IN THIS FILE. The repository publishes no service-level
//  agreement, no latency budget, no throughput target and no availability commitment, so none may be
//  asserted. The one bounded wait below exists to keep a cancellation test from hanging a run, and is not
//  a timing claim about anything.
// ======================================================================================================

using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Endpoints;
using PowerFramework.Shared.Kernel;
using Xunit;

// The contracts project declares its own RetCode - a protobuf message wrapping the legacy catalogue for
// the wire - and the shared kernel declares the in-process transcription of the same oracle. Both are
// needed here, so the wire side is aliased member by member exactly as Clients/DataServicesClient.cs and
// Endpoints/DataServicesProxyEndpoints.cs already do. That leaves the bare name `RetCode` meaning the
// kernel's catalogue, which is the one this file's assertions are written against.
using ColumnValue = PowerFramework.Contracts.Common.V1.ColumnValue;
using CommonV1Extensions = PowerFramework.Contracts.Common.V1.CommonV1Extensions;
using ConflictDetail = PowerFramework.Contracts.Common.V1.ConflictDetail;
using ConflictRow = PowerFramework.Contracts.Common.V1.ConflictRow;
using DbError = PowerFramework.Contracts.Common.V1.DbError;
using DwBuffer = PowerFramework.Contracts.Common.V1.DwBuffer;
using ItemStatus = PowerFramework.Contracts.Common.V1.ItemStatus;
using ProtobufAnyValue = PowerFramework.Contracts.Common.V1.AnyValue;
using RichErrorBinding = PowerFramework.Contracts.Common.V1.RichErrorBinding;
using RichErrorTrailer = PowerFramework.Contracts.Common.V1.RichErrorTrailer;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// A scripted double for contract C-03's generated client, answering only the three operations the
/// projection tests in this file reach.
/// </summary>
/// <remarks>
/// <para>
/// The generated stubs declare every method <see langword="virtual"/> and expose a protected parameterless
/// constructor expressly so a double can be derived, which is how a status translation is exercised with
/// no DataServices instance anywhere. Nothing here opens a socket or needs the upstream to exist - and
/// that is not merely convenient: several of the statuses under test are ones a real upstream cannot be
/// asked to produce on demand.
/// </para>
/// <para>
/// It is a double for a SIBLING SERVICE'S CLIENT STUB, which is a published contract type. It is not a
/// stand-in for any deferred service, and it reaches nothing.
/// </para>
/// </remarks>
internal sealed class ScriptedDataWindowServiceClient : DataWindowService.DataWindowServiceClient
{
    private readonly Func<UpdateRequest, CallOptions, Task<UpdateResponse>>? _update;
    private readonly IReadOnlyList<RetrieveChunk> _chunks;
    private readonly Func<GetEventGateRequest, CallOptions, Task<GetEventGateResponse>>? _eventGate;

    /// <summary>Creates the double.</summary>
    /// <param name="update">
    /// How an update should answer, or <see langword="null"/> for a plain success. Receives the call
    /// options so a scripted answer can observe the cancellation token the client supplied.
    /// </param>
    /// <param name="chunks">The chunks a retrieval should stream, or <see langword="null"/> for none.</param>
    /// <param name="eventGate">
    /// How an event-gate read should answer, or <see langword="null"/> for a plain success.
    /// </param>
    internal ScriptedDataWindowServiceClient(
        Func<UpdateRequest, CallOptions, Task<UpdateResponse>>? update = null,
        IReadOnlyList<RetrieveChunk>? chunks = null,
        Func<GetEventGateRequest, CallOptions, Task<GetEventGateResponse>>? eventGate = null)
    {
        _update = update;
        _chunks = chunks ?? [];
        _eventGate = eventGate;
    }

    /// <summary>Every update request the double received, in order.</summary>
    /// <remarks>
    /// A LIST RATHER THAN A FLAG, because "the conflict was not retried" is a statement about HOW MANY
    /// times the upstream was invoked. A boolean could not distinguish one invocation from three.
    /// </remarks>
    internal List<UpdateRequest> UpdateRequests { get; } = [];

    /// <summary>Every session identifier an event-gate read arrived with, in order.</summary>
    internal List<string> EventGateSessionIds { get; } = [];

    /// <summary>How many retrievals were invoked.</summary>
    internal int RetrieveCallCount { get; private set; }

    /// <inheritdoc/>
    public override AsyncUnaryCall<UpdateResponse> UpdateAsync(UpdateRequest request, CallOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);

        UpdateRequests.Add(request);

        Task<UpdateResponse> answer = _update is null
            ? Task.FromResult(new UpdateResponse { RetCode = WireRetCode.Ok })
            : _update(request, options);

        return UnaryCall(answer);
    }

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<RetrieveChunk> Retrieve(
        RetrieveRequest request,
        CallOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);

        RetrieveCallCount++;

        return new AsyncServerStreamingCall<RetrieveChunk>(
            new ListAsyncStreamReader<RetrieveChunk>(_chunks),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<GetEventGateResponse> GetEventGateAsync(
        GetEventGateRequest request,
        CallOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);

        EventGateSessionIds.Add(request.SessionId);

        Task<GetEventGateResponse> answer = _eventGate is null
            ? Task.FromResult(new GetEventGateResponse { RetCode = WireRetCode.Ok })
            : _eventGate(request, options);

        return UnaryCall(answer);
    }

    /// <summary>Wraps an answer as a unary call carrying the metadata a caller may read.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="answer">The answer, which may already be faulted.</param>
    /// <returns>The call.</returns>
    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(Task<TResponse> answer)
        => new(
            answer,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
}

/// <summary>
/// A double for contract C-04's generated client that answers nothing.
/// </summary>
/// <remarks>
/// It exists only because the typed Gateway client takes both generated stubs, and no test in this file
/// reaches a column-expression operation. Overriding nothing is deliberate: a member reached by accident
/// fails loudly through the base stub's absent call invoker rather than answering something plausible.
/// </remarks>
internal sealed class InertColumnExpressionServiceClient
    : ColumnExpressionService.ColumnExpressionServiceClient;

/// <summary>
/// The reserved Phase 2 route contract, and the status translation on the live surface beside it.
/// </summary>
/// <param name="host">The shared in-process host, supplied as an xUnit CLASS fixture.</param>
/// <remarks>
/// <para>
/// EVERY ASSERTION IS AGAINST THE DEPLOYED COMPOSITION ROOT rather than a hand-assembled pipeline. That
/// distinction carries the file: the reserved declarations, the authorization fallback, the proxy group's
/// single authorization call and the exception handler all interact, and only the real host exercises the
/// interaction. A suite that built its own pipeline could pass while the shipped service answered
/// something else.
/// </para>
/// <para>
/// The four reserved families are asserted from one table so that each is checked against ITS OWN
/// destination. A test that only proved "some deferred service is named" would pass if all four routes
/// named the same one, which is exactly the mis-wiring worth catching.
/// </para>
/// </remarks>
public sealed class DeferredRouteTests(GatewayTestHostFixture host) : IClassFixture<GatewayTestHostFixture>
{
    // --------------------------------------------------------------------------------------------------
    //  THE RESERVED CONTRACT, AS THE PUBLISHED DOCUMENT FIXES IT
    // --------------------------------------------------------------------------------------------------

    /// <summary>The literal marker the contract fixes as a constant on every reserved body.</summary>
    /// <remarks>
    /// Matched exactly rather than by a looser "contains Phase 2" test, because the contract declares it
    /// with a <c>const</c> keyword and a constant that drifted to different wording would still satisfy a
    /// substring check.
    /// </remarks>
    private const string ReservedMarker = "reserved for Phase 2";

    /// <summary>The body member naming the deferred service, under the spelling published for v1 first.</summary>
    private const string ServiceMember = "service";

    /// <summary>The body member naming the deferred service, under the unambiguous spelling.</summary>
    private const string DeferredServiceMember = "deferredService";

    /// <summary>The body member carrying the reserved marker.</summary>
    private const string MarkerMember = "marker";

    /// <summary>The body member echoing the requested path.</summary>
    private const string RouteMember = "route";

    /// <summary>The body member repeating the status, so a logged body is self-describing.</summary>
    private const string StatusMember = "status";

    /// <summary>The extension member carrying the legacy return code on both body shapes.</summary>
    private const string RetCodeMember = "retCode";

    /// <summary>The problem-details extension member naming the upstream a forwarded failure came from.</summary>
    private const string UpstreamMember = "upstream";

    /// <summary>The problem-details extension member carrying the correlation identifier.</summary>
    private const string TraceIdMember = "traceId";

    /// <summary>The problem-details extension member carrying the optimistic-concurrency payload.</summary>
    private const string ConflictMember = "conflict";

    /// <summary>The only upstream any projected route in this contract reaches.</summary>
    private const string DataServicesUpstream = "dataservices";

    /// <summary>The specification extension each reserved operation carries in the published contract.</summary>
    private const string DeferredServiceExtension = "x-deferred-service";

    /// <summary>
    /// The published contract document. Protected by the fallback policy - the AAP's anonymous
    /// exceptions are enumerated (<c>/health</c> on all four, Security's key set and discovery document)
    /// and this is not one of them.
    /// </summary>
    private const string DocumentRoute = "/openapi/v1.json";

    // --------------------------------------------------------------------------------------------------
    //  THE LIVE SURFACE BESIDE IT
    // --------------------------------------------------------------------------------------------------

    /// <summary>The projected update, the one operation in the contract that declares a <c>409</c>.</summary>
    private const string UpdateRoute = "/v1/datawindow/update";

    /// <summary>
    /// The gRPC method whose descriptor declares how a conflict payload is carried.
    /// </summary>
    private const string UpdateMethodName = "Update";

    /// <summary>The projected retrieval, a server stream published as an ordered collection.</summary>
    private const string RetrieveRoute = "/v1/datawindow/retrieve";

    /// <summary>The projected event-gate read, whose only argument is a session identifier.</summary>
    private const string EventGateRoute = "/v1/datawindow/event-gate";

    /// <summary>RFC 9457's own default problem type, used where no more specific type applies.</summary>
    private const string DefaultProblemType = "about:blank";

    /// <summary>The problem type identifying an optimistic-concurrency conflict carrying its detail.</summary>
    private const string ConflictProblemType = "urn:powerframework:problem:optimistic-concurrency-conflict";

    /// <summary>
    /// The problem type identifying an <c>Aborted</c> that arrived without a decodable conflict detail.
    /// </summary>
    private const string ConflictWithoutDetailProblemType =
        "urn:powerframework:problem:optimistic-concurrency-conflict-without-detail";

    /// <summary>The problem type identifying the other canonical <c>409</c>: an upstream duplicate.</summary>
    private const string AlreadyExistsProblemType = "urn:powerframework:problem:already-exists";

    /// <summary>The update table the conflict fixture names, taken from the sole updatable DataWindow.</summary>
    private const string ConflictUpdateTable = "COMPANY";

    /// <summary>
    /// The rich-error binding contract C-03's <c>Update</c> declares, read from the generated descriptor.
    /// </summary>
    /// <remarks>
    /// READ RATHER THAN RESTATED. The trailer key, the accompanying gRPC status and the payload type are
    /// declared once, as a custom method option on the protocol definition, precisely so that a client -
    /// and a test - discovers all three from the descriptor instead of from a comment. Restating the key
    /// here would let this suite keep passing against a key nothing sends.
    /// </remarks>
    private static readonly RichErrorBinding UpdateRichErrorBinding = ReadUpdateRichErrorBinding();

    /// <summary>
    /// The canonical protobuf JSON mapping, configured exactly as the projection configures it.
    /// </summary>
    /// <remarks>
    /// Default values are formatted, matching the projection, so that comparing a forwarded payload
    /// against a locally rendered one is an assertion about the PAYLOAD rather than about two different
    /// serializer configurations.
    /// </remarks>
    private static readonly JsonFormatter CanonicalJson =
        new(JsonFormatter.Settings.Default.WithFormatDefaultValues(true));

    /// <summary>
    /// The four reserved families: the route prefix, and the deferred service the family names.
    /// </summary>
    /// <remarks>
    /// FOUR ROWS, AND THE SERVICE NAMES ARE STRING DATA. A fifth row would mean a service had joined the
    /// roster without being deferred; a missing row would mean an extension point had been dropped and the
    /// eventual topology had become invisible from the contract. Both are contract changes.
    /// <para>
    /// The names are plain string literals on purpose (constraint C-D). No deferred-service type exists to
    /// name, none may be created, and an enumeration declared to hold these four would be the first step
    /// towards modelling a capability that must not be modelled.
    /// </para>
    /// </remarks>
    private static readonly (string Prefix, string DeferredService)[] ReservedFamilies =
    [
        ("/v1/design", "DesignSystem"),
        ("/v1/documents", "Documents"),
        ("/v1/integration", "Integration"),
        ("/v1/scripting", "ScriptBridge"),
    ];

    /// <summary>
    /// Path remainders exercising the wildcard nature of each family.
    /// </summary>
    /// <remarks>
    /// These are declared as ROUTE FAMILIES rather than literal paths, so the assertion has to span the
    /// bare prefix, the prefix with a trailing separator, a single extra segment and a deeply nested one.
    /// A suite that only probed one segment would not distinguish a catch-all declaration from four
    /// literal routes, and it is the catch-all that makes a Phase 2 consumer's eventual URL shape legible.
    /// </remarks>
    private static readonly string[] PathRemainders =
    [
        string.Empty,
        "/",
        "/anything",
        "/theming/color-tokens.v1",
        "/deeply/nested/remainder/that/exists/nowhere",
    ];

    /// <summary>
    /// Every HTTP method that must answer identically, and that must carry the body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contract publishes <c>GET</c> and <c>POST</c> as operations and requires that EVERY OTHER
    /// method answer the same way, so that no verb reports <c>405</c> and therefore no verb appears
    /// implemented. <c>HEAD</c> is asserted separately because its relationship with a response body
    /// belongs to the method and the transport rather than to this route.
    /// </para>
    /// <para>
    /// <c>CONNECT</c> is the one mapped verb absent from this list, and its absence is a HARNESS limitation
    /// stated rather than an assertion quietly dropped: the method establishes a tunnel rather than
    /// addressing a resource, so a client cannot send it at a relative path and an in-memory transport has
    /// no tunnel to establish. It is mapped alongside the others in the route table, and it reaches the same
    /// single handler as every verb here, so there is no separate behaviour for it to have.
    /// </para>
    /// </remarks>
    private static readonly string[] BodyCarryingMethods =
    [
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "OPTIONS",
        "TRACE",
    ];

    /// <summary>The four reserved families, keyed by prefix.</summary>
    public static TheoryData<string, string> ReservedFamilyRoots
    {
        get
        {
            TheoryData<string, string> data = [];

            foreach ((string prefix, string deferredService) in ReservedFamilies)
            {
                data.Add(prefix, deferredService);
            }

            return data;
        }
    }

    /// <summary>Every family crossed with every path remainder.</summary>
    public static TheoryData<string, string> ReservedPaths
    {
        get
        {
            TheoryData<string, string> data = [];

            foreach ((string prefix, string deferredService) in ReservedFamilies)
            {
                foreach (string remainder in PathRemainders)
                {
                    data.Add(prefix + remainder, deferredService);
                }
            }

            return data;
        }
    }

    /// <summary>Every family crossed with every body-carrying HTTP method.</summary>
    public static TheoryData<string, string, string> ReservedPathsByMethod
    {
        get
        {
            TheoryData<string, string, string> data = [];

            foreach ((string prefix, string deferredService) in ReservedFamilies)
            {
                foreach (string method in BodyCarryingMethods)
                {
                    data.Add(method, prefix + "/probe", deferredService);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// The upstream statuses whose translation the ingress contract fixes, with what each becomes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE STATUS MAP IS THE SUBSTANTIVE PART OF THE PROJECTION, which is why it is asserted as a table
    /// rather than sampled. Every one of the thirty-nine projected routes shares a single failure
    /// translation, so a divergence here would be least visible and most damaging.
    /// </para>
    /// <para>
    /// The fourth column is the problem type where the contract fixes a specific one, and
    /// <see langword="null"/> where RFC 9457's default applies. It is what keeps the THREE distinct
    /// <c>409</c>s readable apart - a concurrency conflict with its detail, one without, and a duplicate -
    /// which a caller's retry-or-surface policy keys off.
    /// </para>
    /// </remarks>
    public static TheoryData<StatusCode, HttpStatusCode, long, string?> UpstreamStatusTranslations =>
        new()
        {
            { StatusCode.InvalidArgument, HttpStatusCode.BadRequest, RetCode.E_INVALID_ARGUMENT, null },
            { StatusCode.Unauthenticated, HttpStatusCode.Unauthorized, RetCode.E_ACCESS_DENIED, null },
            { StatusCode.PermissionDenied, HttpStatusCode.Forbidden, RetCode.E_ACCESS_DENIED, null },
            { StatusCode.NotFound, HttpStatusCode.NotFound, RetCode.E_OBJECT_NOT_FOUND, null },
            {
                StatusCode.Aborted,
                HttpStatusCode.Conflict,
                RetCode.E_RETRY,
                ConflictWithoutDetailProblemType
            },
            {
                StatusCode.AlreadyExists,
                HttpStatusCode.Conflict,
                RetCode.E_INVALID_ARGUMENT,
                AlreadyExistsProblemType
            },
            // 🔴 500, NOT 501, AND THIS ROW SITTING IN THIS FILE IS WHY THE DEFECT SURVIVED SO LONG. The
            // reserved deferred-capability routes are this suite's subject and they answer 501; this table is
            // about the PROJECTED routes, every one of which is implemented. An upstream reporting a method
            // its own contract publishes as unimplemented is deployment or version skew, and answering it
            // with the reserved routes' status made the two indistinguishable to a caller reading only the
            // published contract - while gateway.v1.yaml declares 501 on the eight reserved operations and on
            // no projected one, so the status was undeclared as well (AAP 0.4.4, C-D). The legacy code is
            // unchanged and is what names the condition.
            {
                StatusCode.Unimplemented,
                HttpStatusCode.InternalServerError,
                RetCode.E_NO_IMPLEMENTATION,
                null
            },
            { StatusCode.Unavailable, HttpStatusCode.ServiceUnavailable, RetCode.E_RETRY, null },
            { StatusCode.DeadlineExceeded, HttpStatusCode.GatewayTimeout, RetCode.E_TIME_OUT, null },
            {
                StatusCode.Internal,
                HttpStatusCode.InternalServerError,
                RetCode.E_INTERNAL_ERROR,
                null
            },
            { StatusCode.Unknown, HttpStatusCode.InternalServerError, RetCode.UNKNOWN, null },
            {
                StatusCode.FailedPrecondition,
                HttpStatusCode.BadRequest,
                RetCode.E_INVALID_ARGUMENT,
                null
            },
            { StatusCode.OutOfRange, HttpStatusCode.BadRequest, RetCode.E_OUT_OF_RANGE, null },
            { StatusCode.ResourceExhausted, HttpStatusCode.TooManyRequests, RetCode.E_BUSY, null },
            { StatusCode.Cancelled, HttpStatusCode.BadGateway, RetCode.CANCELLED, null },
            { StatusCode.DataLoss, HttpStatusCode.InternalServerError, RetCode.E_DB_ERROR, null },
        };

    /// <summary>Bodies a caller might send that the projection must refuse before reaching upstream.</summary>
    /// <remarks>
    /// Every one is rejected by Gateway ITSELF, which is why each is asserted to omit the <c>upstream</c>
    /// member: the contract reserves that member for a FORWARDED failure, and naming an upstream that was
    /// never reached would send an operator to the wrong service.
    /// </remarks>
    public static TheoryData<string> UnbindableRequestBodies =>
    [
        string.Empty,
        "   ",
        "not json at all",
        "{",
        "[]",
        "{\"noSuchMember\":1}",
    ];

    /// <summary>
    /// Trailer return codes, with the code the projection must publish for each.
    /// </summary>
    /// <remarks>
    /// THE PRESERVED TRI-STATE ALGEBRA, OBSERVED AT THE INGRESS. The projection admits the trailer's own
    /// code only when <c>IsFailed</c> classifies it as a failure, and that test is deliberately not the
    /// negation of a success test: <c>PREVENT</c> is <c>1</c> and therefore reads as a SUCCESS
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>], while <c>CANCELLED</c> is <c>-2</c>
    /// and is explicitly excluded from failure [<c>isfailed.srf:L11-L13</c>], so it is NEITHER succeeded
    /// nor failed. A trailer carrying either - or carrying nothing, which reads as zero - would describe
    /// something other than the rejection that actually occurred, so the retry code is published instead.
    /// </remarks>
    public static TheoryData<long, long> ConflictTrailerReturnCodes =>
        new()
        {
            { RetCode.E_DB_ERROR, RetCode.E_DB_ERROR },
            { RetCode.E_RETRY, RetCode.E_RETRY },
            { RetCode.FAILED, RetCode.FAILED },
            { RetCode.PREVENT, RetCode.E_RETRY },
            { RetCode.CANCELLED, RetCode.E_RETRY },
            { RetCode.OK, RetCode.E_RETRY },
        };

    // ==================================================================================================
    //  SECTION 1 - THE RESERVED CONTRACT
    //
    //  Everything in this section asserts a ROUTE DECLARATION. None of it touches a deferred service,
    //  because there is nothing behind any of the four to touch.
    // ==================================================================================================

    /// <summary>
    /// Every path in every reserved family answers exactly <c>501</c> with the machine-readable body,
    /// naming ITS OWN deferred service.
    /// </summary>
    /// <param name="path">The requested path.</param>
    /// <param name="deferredService">The deferred service this family, and only this family, names.</param>
    /// <remarks>
    /// <para>
    /// EXACTLY <c>501</c>. Not <c>404</c>, which would make the eventual topology invisible from the
    /// contract; not <c>405</c>, which would say the path exists but the verb does not; not <c>503</c>,
    /// which would say "try again"; and not <c>500</c>, which would say this service had failed. The
    /// equality assertion excludes all four at once.
    /// </para>
    /// <para>
    /// The body is PARSED into the contract's own published type and its members are asserted
    /// individually. Nothing here substring-matches a rendered page, and the deserialization is itself an
    /// assertion: every member of that type is <c>required</c>, so a body missing one fails here rather
    /// than passing with a silent default.
    /// </para>
    /// <para>
    /// The service name is asserted PER ROW against that row's own expected value. A suite that checked
    /// only "a deferred service is named" would pass if all four routes named the same one.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedPaths))]
    public async Task AReservedRouteAnswersNotImplementedNamingItsOwnDeferredService(
        string path,
        string deferredService)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);

        // Machine-readable, and the media type says so. A text or HTML answer would force a client to
        // parse prose, which is the whole thing the reserved body exists to avoid.
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        ReservedRouteBody body = await ReadReservedBodyAsync(response);

        Assert.Equal(deferredService, body.DeferredService);
        Assert.Equal(deferredService, body.Service);
        Assert.Equal(ReservedMarker, body.Marker);
        Assert.Equal((int)HttpStatusCode.NotImplemented, body.Status);
        Assert.Equal(path, body.Route);

        // The legacy framework's own not-implemented code, consumed as a symbol from the single
        // transcription of the oracle rather than written here as -2001.
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, body.RetCode);
    }

    /// <summary>
    /// The reserved body carries exactly the six members its schema declares, and no others.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// <para>
    /// The schema sets <c>additionalProperties: false</c>, so an extra member is a contract break rather
    /// than a harmless addition - and the member that would appear first if capability modelling ever crept
    /// in is exactly the kind this asserts against. It also pins the two spellings of the destination:
    /// <c>service</c> is the member the contract published for v1 FIRST and <c>deferredService</c> the
    /// unambiguous one added later, and both are emitted carrying the identical value so that a consumer
    /// written against either keeps working.
    /// </para>
    /// <para>
    /// Asserted on the raw JSON rather than on the deserialized object, because a deserializer cannot
    /// report a member the type does not declare.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task TheReservedBodyCarriesExactlyTheMembersItsSchemaDeclares(
        string prefix,
        string deferredService)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, prefix + "/members");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        List<string> declared =
        [
            .. document.RootElement
                .EnumerateObject()
                .Select(static member => member.Name)
                .Order(StringComparer.Ordinal),
        ];

        List<string> expected =
        [
            .. new[]
            {
                DeferredServiceMember,
                MarkerMember,
                RetCodeMember,
                RouteMember,
                ServiceMember,
                StatusMember,
            }.Order(StringComparer.Ordinal),
        ];

        Assert.Equal(expected, declared);

        Assert.Equal(
            deferredService,
            document.RootElement.GetProperty(DeferredServiceMember).GetString());
        Assert.Equal(deferredService, document.RootElement.GetProperty(ServiceMember).GetString());
    }

    /// <summary>
    /// Every HTTP method on a reserved route answers identically, so no verb appears implemented.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">A path inside the reserved family.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// The contract declares <c>GET</c> and <c>POST</c> as operations and requires every OTHER method to
    /// answer the same <c>501</c>. Without that, an unlisted verb would draw <c>405 Method Not Allowed</c>,
    /// which is a different statement about the route: it says the path is real and the verb is not, which
    /// invites a caller to go looking for the verb that is. One answer for every verb says the only true
    /// thing - nothing here is implemented at all.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedPathsByMethod))]
    public async Task EveryMethodOnAReservedRouteAnswersTheSameWay(
        string method,
        string path,
        string deferredService)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Parse(method), path);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        ReservedRouteBody body = await ReadReservedBodyAsync(response);

        Assert.Equal(deferredService, body.DeferredService);
        Assert.Equal(ReservedMarker, body.Marker);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, body.RetCode);
    }

    /// <summary>
    /// A <c>HEAD</c> request answers <c>501</c> too, with the body the method suppresses.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names, unused in the assertion.</param>
    /// <remarks>
    /// Separated from the verb matrix above because <c>HEAD</c>'s relationship with a response body is a
    /// property of the METHOD AND THE TRANSPORT, not of this route: a network host discards the body while
    /// this suite's in-memory transport delivers it. Asserting either way would be asserting something about
    /// the harness. What is asserted instead is what the route owes: the SAME status, so a caller probing
    /// with <c>HEAD</c> cannot conclude the capability exists, and - when a body does arrive - the same
    /// reserved shape naming the same destination, so <c>HEAD</c> cannot be a quieter second answer.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task AHeadRequestToAReservedRouteAnswersNotImplemented(
        string prefix,
        string deferredService)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        string path = prefix + "/head-probe";

        using HttpResponseMessage head = await SendAsync(client, HttpMethod.Head, path);

        Assert.Equal(HttpStatusCode.NotImplemented, head.StatusCode);

        string headBody = await ReadTextAsync(head);

        if (headBody.Length > 0)
        {
            Assert.Equal(deferredService, ParseReservedBody(headBody).DeferredService);
        }

        // The same path under a body-carrying method, so the family is demonstrably one route with one
        // answer rather than a pair that happen to agree on a status code.
        using HttpResponseMessage get = await SendAsync(client, HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.NotImplemented, get.StatusCode);
        Assert.Equal(deferredService, (await ReadReservedBodyAsync(get)).DeferredService);
    }

    /// <summary>
    /// Sending a request body to a reserved route still produces no success.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// The contract declares NO request schema on any reserved operation, deliberately: a request schema
    /// would model a deferred capability, and modelling one is the thing C-D forbids. So a caller that
    /// sends a body - whether well-formed JSON, a foreign media type, or nonsense - must meet the identical
    /// answer, and the body must be neither bound nor read nor echoed. A route that accepted a payload and
    /// answered differently because of it would be evaluating something, and nothing behind these four
    /// evaluates anything.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task SendingABodyToAReservedRouteChangesNothing(string prefix, string deferredService)
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using StringContent payload = JsonBody("{\"pretendCapability\":\"render\",\"count\":3}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            prefix + "/with-a-body",
            payload);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);

        string raw = await ReadTextAsync(response);

        // The payload is not read, so nothing from it can appear in the answer.
        Assert.DoesNotContain("pretendCapability", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("render", raw, StringComparison.Ordinal);

        ReservedRouteBody body = ParseReservedBody(raw);

        Assert.Equal(deferredService, body.DeferredService);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, body.RetCode);
    }

    /// <summary>
    /// The four reserved routes name four DISTINCT deferred services, and exactly the expected four.
    /// </summary>
    /// <remarks>
    /// THE MIS-WIRING GUARD, and the reason it is a separate test. Every per-route assertion above could
    /// pass while two routes shared a destination only if the expectations themselves were wrong; this one
    /// closes the remaining gap from the other side by collecting all four answers and requiring the SET to
    /// be four distinct names. A copy-paste that pointed <c>/v1/documents/**</c> at the DesignSystem
    /// destination fails here even if nothing else did.
    /// </remarks>
    [Fact]
    public async Task TheFourReservedRoutesNameFourDistinctDeferredServices()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        List<string> named = [];

        foreach ((string prefix, string _) in ReservedFamilies)
        {
            using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, prefix + "/probe");

            Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

            ReservedRouteBody body = await ReadReservedBodyAsync(response);

            named.Add(body.DeferredService);
        }

        List<string> expected =
        [
            .. ReservedFamilies
                .Select(static family => family.DeferredService)
                .Order(StringComparer.Ordinal),
        ];

        List<string> observed = [.. named.Order(StringComparer.Ordinal)];

        Assert.Equal(4, named.Count);
        Assert.Equal(4, named.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected, observed);
    }

    /// <summary>
    /// The echoed route is the request path, and never the query string.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// The path is echoed so a client can log what it asked for. The query string is excluded on two
    /// independent grounds: it is not part of a route, and a caller who mistakenly placed a credential in
    /// one must not have it reflected back into a body that may then be logged.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task TheEchoedRouteIsThePathAndNeverTheQueryString(
        string prefix,
        string deferredService)
    {
        const string marker = "must-not-be-echoed";

        using HttpClient client = host.CreateAuthenticatedClient();

        string path = prefix + "/echo";

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{path}?diagnostic={marker}");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);

        string raw = await ReadTextAsync(response);

        Assert.DoesNotContain(marker, raw, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostic", raw, StringComparison.Ordinal);

        ReservedRouteBody body = ParseReservedBody(raw);

        Assert.Equal(path, body.Route);
        Assert.Equal(deferredService, body.DeferredService);
    }

    /// <summary>
    /// A reserved route is refused anonymously, and the refusal discloses nothing about the roster.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names once a credential is presented.</param>
    /// <remarks>
    /// <para>
    /// Constraint C-G, applied consistently with the rest of the ingress: every declaration in the reserved
    /// file requires a bearer credential, so the authentication middleware answers first and the handler is
    /// never reached. THE ORDER IS THE POINT - the reserved body names a Phase-2 plan, so an anonymous
    /// caller must not be able to enumerate the deferred roster by walking the route table.
    /// </para>
    /// <para>
    /// The refusal is asserted to be <c>401</c> and explicitly NOT <c>501</c>, and the response is asserted
    /// not to carry the destination name, because a challenge that leaked it would defeat the ordering it
    /// exists to enforce.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task AReservedRouteIsRefusedAnonymouslyWithoutDisclosingItsDestination(
        string prefix,
        string deferredService)
    {
        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, prefix + "/probe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains(
            GatewayTestHostFixture.BearerScheme,
            string.Join(", ", response.Headers.WwwAuthenticate.Select(static value => value.ToString())),
            StringComparison.Ordinal);

        Assert.DoesNotContain(deferredService, await ReadTextAsync(response), StringComparison.Ordinal);
    }

    /// <summary>
    /// The reserved return code is the legacy's not-implemented value, and not its neighbour.
    /// </summary>
    /// <remarks>
    /// <c>E_NO_IMPLEMENTATION</c> is <c>-2001</c> and <c>E_NO_SUPPORT</c> is <c>-2000</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/retcode.sru:L77-L78</c>], and they are DIFFERENT statements: "this
    /// is not implemented here" against "this framework does not support that". A reserved route is the
    /// first, so the second is asserted absent. Both are read as symbols; neither literal appears in this
    /// file, because the transcription of the oracle belongs in one place.
    /// </remarks>
    [Fact]
    public async Task TheReservedReturnCodeIsNotImplementedRatherThanNotSupported()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            ReservedFamilies[0].Prefix + "/code");

        ReservedRouteBody body = await ReadReservedBodyAsync(response);

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, body.RetCode);
        Assert.NotEqual(RetCode.E_NO_SUPPORT, body.RetCode);

        // Classified with the ported algebra rather than compared against zero, because that algebra is
        // what a client branches on: a not-implemented answer is unambiguously a failure, and it is neither
        // a prevention nor a cancellation.
        Assert.True(Predicates.IsFailed(body.RetCode));
        Assert.False(Predicates.IsSucceeded(body.RetCode));
        Assert.False(Predicates.IsCancelled(body.RetCode));
    }

    /// <summary>
    /// The published document declares exactly <c>401</c> and <c>501</c> for each reserved family, no
    /// success, and no request body.
    /// </summary>
    /// <param name="prefix">The reserved family's prefix.</param>
    /// <param name="deferredService">The deferred service the family names.</param>
    /// <remarks>
    /// <para>
    /// THE CONTRACT AS A GENERATED ARTIFACT, not merely as authored YAML. The runtime answer and the
    /// published description are two separate things a consumer relies on, and this asserts the second: a
    /// generated client must be unable to express a successful call to a reserved route, because there is
    /// no success to express, and it must be able to express the refusal it will actually meet.
    /// </para>
    /// <para>
    /// THE SET IS ASSERTED AS EXACTLY <c>{401, 501}</c>, WHICH IS THE SAME SET
    /// <c>shared/PowerFramework.Contracts.Tests/ReservedRouteMetadataTests.cs</c> PINS ON THE AUTHORED
    /// DOCUMENT. Pinning both is the point: the authored YAML and the runtime-generated description are
    /// two artifacts a consumer may fetch, and if they disagree about a response set then which contract
    /// a client obeys depends on where it was generated from. A runtime that declared <c>501</c> alone
    /// while the authored document declared both is exactly the divergence this exactness detects.
    /// <c>501</c> is the only outcome any handler computes; <c>401</c> is the
    /// pre-handler refusal the bearer requirement guarantees, and declaring it says nothing about the
    /// route evaluating anything.
    /// </para>
    /// <para>
    /// No <c>2xx</c> is separately asserted rather than being left implied by the exact set, because the
    /// exactness could later be relaxed and the no-success rule may not be: a success response would say
    /// part of a deferred service had been built, which constraint C-D forbids outright.
    /// </para>
    /// <para>
    /// The specification extension naming the destination is asserted too, so that the routing metadata and
    /// the wire member read the same. Naming is the permitted metadata; it is what makes the eventual
    /// system legible from this contract, and it is the only reason the routes are declared.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ReservedFamilyRoots))]
    public async Task ThePublishedDocumentDeclaresOnlyNotImplementedForEachReservedFamily(
        string prefix,
        string deferredService)
    {
        // Authenticated because the contract document is protected: it is not one of the AAP's
        // enumerated anonymous exceptions, so an anonymous caller is answered 401 here.
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, DocumentRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        JsonElement path = document.RootElement.GetProperty("paths").GetProperty(prefix + "/{path}");

        string[] declaredOperations = ["get", "post"];

        // THE GENERATED PATH PARAMETER IS OPENAPI-CONFORMANT, AND SAYS WHAT IT MEANS IN A VENDOR
        // EXTENSION. `allowReserved` is defined for `in: query` parameters only, so emitting it on this
        // path parameter made the generated document invalid while still not expressing the catch-all
        // matching it was reaching for. Both halves are pinned: the invalid field is absent, and the
        // extension that actually carries the behaviour is present. Same shape as the authored contract's
        // `x-catch-all` block, so the two descriptions read alike.
        foreach (string operationName in declaredOperations)
        {
            JsonElement parameter = Assert.Single(
                path.GetProperty(operationName).GetProperty("parameters").EnumerateArray());

            Assert.Equal("path", parameter.GetProperty("in").GetString());

            Assert.False(
                parameter.TryGetProperty("allowReserved", out _),
                "The generated reserved path parameter declares 'allowReserved'. OpenAPI 3.1 defines "
                    + "that field for query parameters only, so the generated document fails 3.1 "
                    + "validation - and it never expressed catch-all matching in any case.");

            JsonElement catchAll = parameter.GetProperty("x-catch-all");

            Assert.True(catchAll.GetProperty("capturesNestedSegments").GetBoolean());
            Assert.True(catchAll.GetProperty("matchesEmptyRemainder").GetBoolean());
            Assert.True(catchAll.GetProperty("matchesEveryHttpMethod").GetBoolean());
        }

        foreach (string operationName in declaredOperations)
        {
            JsonElement operation = path.GetProperty(operationName);

            Assert.Equal(
                deferredService,
                operation.GetProperty(DeferredServiceExtension).GetString());

            Assert.False(
                operation.TryGetProperty("requestBody", out _),
                "A reserved operation must declare no request body: a request schema would model a "
                    + "deferred capability.");

            JsonElement responses = operation.GetProperty("responses");

            Assert.True(responses.TryGetProperty("501", out _));

            // THE REFUSAL THAT PRECEDES THE 501 IS DECLARED TOO. gateway.v1.yaml gives each of these eight
            // operations exactly two responses, and runtime metadata that declares only one of them leaves
            // a caller reading the generated document seeing a route that can only ever answer 501, while
            // an untokened request actually gets 401. Asserting it here is what keeps the projection and the
            // authored contract from drifting apart.
            Assert.True(
                responses.TryGetProperty("401", out _),
                "A reserved route is authenticated, so its declared response set includes the challenge.");

            // NO 403, and its absence is asserted rather than left implicit. These routes require
            // authentication and NO SCOPE - they reach no capability by construction - so a scope would be
            // a permission over a feature that does not exist, and requiring one would turn the published
            // 501 into a 403 and hide the very shape these declarations exist to publish.
            Assert.False(
                responses.TryGetProperty("403", out _),
                "A reserved route requires no scope, so it can never answer 403.");

            // The complete set, so that a third status added later is a finding rather than a silent
            // widening of what these declarations claim.
            Assert.Equal(
                ["401", "501"],
                responses.EnumerateObject().Select(static declared => declared.Name).Order(StringComparer.Ordinal));

            Assert.DoesNotContain(
                responses.EnumerateObject().Select(static declared => declared.Name),
                static status => status.StartsWith('2'));

            // EXACTLY {401, 501}, ordered so the comparison is stable whatever order the generator
            // emitted them in. Matches the set ReservedRouteMetadataTests pins on the authored YAML, so
            // the two artifacts a consumer may generate from cannot describe different contracts.
            string[] declaredStatuses =
                [.. responses.EnumerateObject()
                    .Select(static declared => declared.Name)
                    .Order(StringComparer.Ordinal)];

            Assert.Equal(["401", "501"], declaredStatuses);
        }
    }

    /// <summary>
    /// Every projected operation in the LIVE document declares the four statuses the failure map can
    /// produce on any of them.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE AUTHORED CONTRACT AND THE LIVE DOCUMENT ARE TWO DIFFERENT ARTEFACTS, AND ONLY ONE OF THEM IS
    /// WHAT A CONSUMER FETCHES. The sibling contracts suite reads
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> and can prove what the contract
    /// PROMISES; nothing there can see what this host actually serves, which is generated from the route
    /// metadata. A declaration added to the authored file and not to the routes would leave the two
    /// disagreeing, with the live document - the one a client generator consumes - being the wrong one.
    /// </para>
    /// <para>
    /// The four statuses are the ones no projected operation can avoid. 429 is a capacity ceiling in a
    /// handle registry refusing to hold more work; 502 is no gRPC response arriving at all; 503 is an
    /// upstream answering that it is not currently serving; 504 is the outbound deadline elapsing, and
    /// every outbound call carries one. All four were emitted while none was declared.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedDocumentDeclaresEveryStatusTheProjectionCanProduce()
    {
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, DocumentRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = await ReadJsonAsync(response);

        string[] required = ["429", "500", "502", "503", "504"];
        int projected = 0;

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith("/v1/datawindow", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("responses", out JsonElement responses))
                {
                    continue;
                }

                projected++;

                foreach (string status in required)
                {
                    Assert.True(
                        responses.TryGetProperty(status, out _),
                        $"{operation.Name.ToUpperInvariant()} {path.Name} must declare {status}: the "
                            + "failure map can produce it on any projected operation.");
                }
            }
        }

        // The count is asserted so that a projection removed from the route table cannot make this pass by
        // finding nothing to check. Thirty-nine is the contract's own figure: fifteen of C-03's sixteen
        // methods and twenty-four of C-04's twenty-six - every unary and every server-streaming one, and
        // none of the three bidirectional ones.
        Assert.Equal(39, projected);
    }

    // ==================================================================================================
    //  SECTION 2 - THE ADJACENT LIVE SURFACE: `Aborted` BECOMES `409`, PAYLOAD INTACT
    //
    //  Nothing in this section touches a deferred service either. It is the OTHER half of what a caller
    //  meets at this ingress, and it is here because the reserved routes are only meaningful beside a
    //  surface that genuinely answers - and because the optimistic-concurrency guarantee for the whole
    //  architecture is observable at exactly one place, which is Gateway's edge.
    // ==================================================================================================

    /// <summary>
    /// An aborted update becomes <c>409</c> carrying the upstream's own conflict payload, unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CANONICAL gRPC-TO-HTTP CONFLICT MAPPING, AND THE REASON IT IS NOT A <c>500</c>. A <c>500</c>
    /// says this service failed; a <c>409</c> says the caller's update was refused because the row moved
    /// underneath it. Only the second is actionable, and acting on it is the whole point: the sole updatable
    /// DataWindow in the legacy estate carries <c>updatewhere=1</c> with all six columns marked
    /// <c>updatewhereclause=yes</c> [<c>ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14</c>], so the
    /// generated statement's WHERE clause compares every marked column's ORIGINAL value and a caller needs
    /// both value sets to discover which column changed.
    /// </para>
    /// <para>
    /// THE PAYLOAD IS COMPARED AGAINST ITS OWN CANONICAL RENDERING, member for member. That is stronger
    /// than spot-checking a field: it fails if anything was summarised, truncated, flattened to a boolean,
    /// reordered into a different envelope or replaced with a message string. Every one of those would leave
    /// a caller choosing between a blind overwrite and a spurious failure.
    /// </para>
    /// <para>
    /// NO DATABASE IS INVOLVED, not even an in-memory one. The conflict is produced by a substituted
    /// upstream client. Gateway holds no storage provider and this test adds none.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAbortedUpdateBecomesConflictCarryingTheUpstreamPayloadUnchanged()
    {
        ConflictDetail detail = BuildConflictDetail();

        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                AbortedWithConflict(detail, RetCode.E_DB_ERROR)));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            UpdateRoute,
            request);

        // Not swallowed, not degraded, and above all not a success.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument problem = await ReadJsonAsync(response);

        Assert.Equal(
            (int)HttpStatusCode.Conflict,
            problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(ConflictProblemType, problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            DataServicesUpstream,
            problem.RootElement.GetProperty(UpstreamMember).GetString());
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty(TraceIdMember).GetString()));

        // The trailer's own code, admitted because the preserved algebra classifies it as a failure.
        Assert.Equal(RetCode.E_DB_ERROR, problem.RootElement.GetProperty(RetCodeMember).GetInt64());

        JsonElement carried = problem.RootElement.GetProperty(ConflictMember);

        Assert.Equal(
            NormalizeJson(CanonicalJson.Format(detail)),
            NormalizeJson(carried.GetRawText()));

        // Restated structurally as well, so a future change to the canonical rendering cannot make the
        // comparison above vacuous without also failing here.
        Assert.Equal(ConflictUpdateTable, carried.GetProperty("updateTable").GetString());
        Assert.Single(carried.GetProperty("rows").EnumerateArray());

        JsonElement row = carried.GetProperty("rows")[0];

        Assert.NotEmpty(row.GetProperty("currentValues").EnumerateArray());
        Assert.NotEmpty(row.GetProperty("originalValues").EnumerateArray());
        Assert.Equal(
            row.GetProperty("currentValues").GetArrayLength(),
            row.GetProperty("originalValues").GetArrayLength());

        // The upstream was invoked exactly once. A conflict is a definitive answer, so re-issuing the same
        // request would produce the same conflict while risking the silent overwrite the contract forbids.
        Assert.Single(upstream.UpdateRequests);
    }

    /// <summary>
    /// A conflict is never auto-retried, and a caller that re-sends the same payload gets the same answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent properties, and each would be a defect on its own. Gateway must not retry - the
    /// original values the payload carries are stale, so a retry either fails again or, worse, succeeds
    /// against a row it was never entitled to overwrite. And the answer must be STABLE, because a caller's
    /// retry-or-surface policy is only implementable if re-sending the same stale payload is deterministic.
    /// Invocation counting is what distinguishes "answered once" from "quietly retried and then answered".
    /// </para>
    /// <para>
    /// THE CORRELATION IDENTIFIER IS EXCLUDED FROM THE STABILITY COMPARISON, AND THEN ASSERTED TO DIFFER.
    /// It identifies the OCCURRENCE rather than the answer, and the contract guarantees a caller is handed
    /// the same value that appears in the operator record for the same occurrence - so two occurrences
    /// sharing one identifier would break that guarantee while making this assertion easier. Both
    /// properties are therefore asserted: the substance is identical, and the identifier is not.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AConflictIsNeverAutoRetriedAndReSendingTheSamePayloadRepeatsIt()
    {
        ConflictDetail detail = BuildConflictDetail();

        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                AbortedWithConflict(detail, RetCode.E_RETRY)));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();

        string first = await PostUpdateAndReadAsync(client, HttpStatusCode.Conflict);

        Assert.Single(upstream.UpdateRequests);

        string second = await PostUpdateAndReadAsync(client, HttpStatusCode.Conflict);

        Assert.Equal(2, upstream.UpdateRequests.Count);
        Assert.Equal(WithoutCorrelationId(first), WithoutCorrelationId(second));
        Assert.NotEqual(ReadCorrelationId(first), ReadCorrelationId(second));
        Assert.False(string.IsNullOrEmpty(ReadCorrelationId(first)));
    }

    /// <summary>
    /// An abort that arrives without a decodable conflict detail is still <c>409</c>, and still not a
    /// success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The status is PRESERVED rather than reclassified, because reporting anything else would let a
    /// rejected update look successful. And no conflict member is fabricated, because an empty one would
    /// describe a conflict no caller could act on - it would invite a rebase against nothing.
    /// </para>
    /// <para>
    /// It carries a DISTINCT problem type from the conflict that arrived with its detail, which is the
    /// property that lets a caller tell an actionable conflict from one it cannot rebase against. Three
    /// different <c>409</c>s reach this ingress and all three must stay readable apart.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAbortWithoutADecodableDetailIsStillConflictAndCarriesNoConflictMember()
    {
        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new RpcException(new Status(StatusCode.Aborted, "the row moved"), new Metadata())));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            UpdateRoute,
            request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        using JsonDocument problem = await ReadJsonAsync(response);

        Assert.Equal(
            ConflictWithoutDetailProblemType,
            problem.RootElement.GetProperty("type").GetString());
        Assert.NotEqual(ConflictProblemType, problem.RootElement.GetProperty("type").GetString());
        Assert.Equal(RetCode.E_RETRY, problem.RootElement.GetProperty(RetCodeMember).GetInt64());
        Assert.False(problem.RootElement.TryGetProperty(ConflictMember, out _));
    }

    /// <summary>
    /// The trailer's return code is admitted only when the preserved algebra calls it a failure.
    /// </summary>
    /// <param name="trailerCode">The code the upstream's trailer carried.</param>
    /// <param name="expectedRetCode">The code the projection must publish.</param>
    /// <remarks>
    /// The classification is deliberately not the negation of a success test, because the legacy algebra has
    /// a hole in it that must survive: <c>PREVENT</c> reads as a SUCCESS and <c>CANCELLED</c> is NEITHER
    /// succeeded nor failed. A trailer carrying either describes something other than the rejection that
    /// occurred, so the retry code - the one a retry-or-surface decision is actually made against - is
    /// published instead. Reproducing that hole rather than tidying it is the behaviour-preservation
    /// requirement applied to error ergonomics.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ConflictTrailerReturnCodes))]
    public async Task TheTrailerCodeIsAdmittedOnlyWhenThePreservedAlgebraCallsItAFailure(
        long trailerCode,
        long expectedRetCode)
    {
        ConflictDetail detail = BuildConflictDetail();

        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                AbortedWithConflict(detail, trailerCode)));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();

        string payload = await PostUpdateAndReadAsync(client, HttpStatusCode.Conflict);

        using JsonDocument problem = JsonDocument.Parse(payload);

        Assert.Equal(expectedRetCode, problem.RootElement.GetProperty(RetCodeMember).GetInt64());

        // The payload survives in every case: the return code decides how the failure is CLASSIFIED, never
        // whether the caller is told what conflicted.
        Assert.True(problem.RootElement.TryGetProperty(ConflictMember, out _));
    }

    /// <summary>
    /// Every upstream status translates exactly as the ingress contract fixes it.
    /// </summary>
    /// <param name="upstreamStatus">The gRPC status the upstream answered with.</param>
    /// <param name="synthesizedByTheTransport">Whether the transport synthesized the value rather than the caller supplying it.</param>
    /// <param name="expectedHttpStatus">The HTTP status the projection must publish.</param>
    /// <param name="expectedRetCode">The legacy return code the projection must publish.</param>
    /// <remarks>
    /// <para>
    /// THE TABLE IS THE CONTRACT. All thirty-nine projected operations share ONE failure translation, so an
    /// error here would be simultaneously the least visible and the most damaging kind: every route would
    /// misreport together, consistently, and therefore plausibly.
    /// </para>
    /// <para>
    /// Two rows carry the whole conflict story between them. <c>Aborted</c> becomes <c>409</c> and never
    /// <c>500</c>, and <c>AlreadyExists</c> becomes the OTHER canonical <c>409</c> under its own problem
    /// type and its own return code, so a caller's retry-or-surface logic can tell a concurrency mismatch
    /// from a duplicate. And no row anywhere in this table is a <c>2xx</c>: a failure never surfaces as a
    /// success, which is the same guarantee the conflict path rests on, asserted across the whole map.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A TRANSPORT-SYNTHESIZED status becomes 502, and the SAME code answered by the server does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE HALF OF ADJUDICATION A2 MOST EASILY LEFT OUT. <c>Unavailable</c> is the obvious status to tell
    /// apart by the transport exception on it: a status the CLIENT synthesized from a failed transport carries
    /// one, a status the SERVER answered does not. <c>Internal</c> needs the same treatment - a stalled
    /// TLS or HTTP/2 HANDSHAKE, which is what an upstream process that is running but no longer reading its
    /// socket produces, is reported by Grpc.Net as <c>Internal</c> rather than <c>Unavailable</c>, because
    /// the failure happens while the connection is still being established. Without it a call that never
    /// reached the upstream at all is answered <c>500 E_INTERNAL_ERROR</c>: blaming this service for an
    /// upstream that has frozen, sending an operator to the wrong logs, and telling the caller nothing is
    /// worth retrying.
    /// </para>
    /// <para>
    /// BOTH DIRECTIONS ARE ASSERTED, which is the point. The two server-answered rows are what stop this
    /// being "fixed" by mapping <c>Internal</c> to 502 unconditionally - that would lose a genuine upstream
    /// fault's own diagnosis, and the 500 arm is where the statement-redaction contract lives.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(StatusCode.Unavailable, true, HttpStatusCode.BadGateway, RetCode.E_RETRY)]
    [InlineData(StatusCode.Unavailable, false, HttpStatusCode.ServiceUnavailable, RetCode.E_RETRY)]
    [InlineData(StatusCode.Internal, true, HttpStatusCode.BadGateway, RetCode.E_RETRY)]
    [InlineData(StatusCode.Internal, false, HttpStatusCode.InternalServerError, RetCode.E_INTERNAL_ERROR)]
    public async Task ATransportSynthesizedStatusIsTheUpstreamsUnreachabilityAndNotItsAnswer(
        StatusCode upstreamStatus,
        bool synthesizedByTheTransport,
        HttpStatusCode expectedHttpStatus,
        long expectedRetCode)
    {
        // The transport exception IS the discriminator, so it is the only thing that differs between the
        // two rows of each pair. Grpc.Net attaches one when it synthesizes a status itself.
        Status status = synthesizedByTheTransport
            ? new Status(upstreamStatus, "the call failed in transit", new IOException("frozen socket"))
            : new Status(upstreamStatus, "the upstream answered");

        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(new RpcException(status)));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            UpdateRoute,
            request);

        Assert.Equal(expectedHttpStatus, response.StatusCode);

        using JsonDocument problem = await ReadJsonAsync(response);

        Assert.Equal(expectedRetCode, problem.RootElement.GetProperty(RetCodeMember).GetInt64());

        // AND THE PROSE MUST NOT CLAIM A RETRY THAT NEVER HAPPENED. Update is not replay-safe, so it is
        // attempted exactly once by design; a detail asserting the retry policy had been exhausted would
        // be telling an operator to look for a transient fault behind a single attempt.
        if (expectedHttpStatus == HttpStatusCode.BadGateway)
        {
            string detail = problem.RootElement.GetProperty("detail").GetString() ?? string.Empty;

            Assert.DoesNotContain(
                "retry policy was exhausted, so no response arrived",
                detail,
                StringComparison.Ordinal);
            Assert.Contains("replay is NOT safe", detail, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(UpstreamStatusTranslations))]
    public async Task EveryUpstreamStatusTranslatesAsTheContractFixesIt(
        StatusCode upstreamStatus,
        HttpStatusCode expectedHttpStatus,
        long expectedRetCode,
        string? expectedProblemType)
    {
        ScriptedDataWindowServiceClient upstream = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new RpcException(new Status(upstreamStatus, "the upstream answered"))));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            UpdateRoute,
            request);

        Assert.Equal(expectedHttpStatus, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument problem = await ReadJsonAsync(response);

        Assert.Equal(expectedRetCode, problem.RootElement.GetProperty(RetCodeMember).GetInt64());
        Assert.Equal(
            expectedProblemType ?? DefaultProblemType,
            problem.RootElement.GetProperty("type").GetString());

        // A forwarded failure names its upstream. Gateway reaches exactly one from these routes, so no
        // other value can legitimately appear.
        Assert.Equal(DataServicesUpstream, problem.RootElement.GetProperty(UpstreamMember).GetString());
    }

    /// <summary>
    /// An upstream that was never reached is <c>502</c>, while one that answered unavailable is <c>503</c>.
    /// </summary>
    /// <remarks>
    /// Two different events wearing one gRPC status code, and the distinction is worth an operator's time:
    /// a status the CLIENT synthesized from a failed transport never reached DataServices at all, while a
    /// status DataServices genuinely answered is its own report about itself. The transport exception
    /// attached to the status is what tells them apart. Sending an operator to debug a service that was
    /// never contacted is the failure this separation prevents.
    /// </remarks>
    [Fact]
    public async Task AnUnreachedUpstreamIsBadGatewayWhileAnAnsweringOneIsServiceUnavailable()
    {
        ScriptedDataWindowServiceClient synthesized = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new RpcException(new Status(
                    StatusCode.Unavailable,
                    "no connection could be established",
                    new HttpRequestException("the upstream address refused the connection")))));

        await using GatewayTestHostFixture synthesizedHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(synthesizedHost, synthesized);

        using HttpClient synthesizedClient = synthesizedHost.CreateAuthenticatedClient();

        string synthesizedPayload = await PostUpdateAndReadAsync(
            synthesizedClient,
            HttpStatusCode.BadGateway);

        using JsonDocument synthesizedProblem = JsonDocument.Parse(synthesizedPayload);

        Assert.Equal(
            RetCode.E_RETRY,
            synthesizedProblem.RootElement.GetProperty(RetCodeMember).GetInt64());

        // The other half of the distinction: a transport failure that escapes as ITSELF rather than as a
        // gRPC status is the clearest instance of "no response arrived at all", and answers the same way.
        ScriptedDataWindowServiceClient transportFailure = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new HttpRequestException("the upstream address refused the connection")));

        await using GatewayTestHostFixture transportHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(transportHost, transportFailure);

        using HttpClient transportClient = transportHost.CreateAuthenticatedClient();

        await PostUpdateAndReadAsync(transportClient, HttpStatusCode.BadGateway);
    }

    /// <summary>
    /// A body the projection cannot bind is refused before the upstream is reached, and names no upstream.
    /// </summary>
    /// <param name="body">The unbindable body.</param>
    /// <remarks>
    /// <para>
    /// Gateway produced this refusal itself, so the <c>upstream</c> member must be ABSENT: the contract
    /// reserves it for a forwarded failure, and naming a service that was never contacted would send an
    /// operator to the wrong place. The upstream invocation count is asserted at zero for the same reason -
    /// a rejection that had already crossed the boundary would be a different event with the same status.
    /// </para>
    /// <para>
    /// ASSERTED ON BOTH BODY-BOUND SHAPES, the unary projection and the server-stream one, because they bind
    /// separately and a rejection arm present on one and missing on the other would let a malformed body
    /// reach an upstream through the other door. Neither may produce a <c>2xx</c>, and neither does.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnbindableRequestBodies))]
    public async Task AnUnbindableBodyIsRefusedBeforeTheUpstreamIsReached(string body)
    {
        ScriptedDataWindowServiceClient upstream = new();

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();

        string[] bodyBoundRoutes = [UpdateRoute, RetrieveRoute];

        foreach (string route in bodyBoundRoutes)
        {
            using StringContent request = JsonBody(body);

            using HttpResponseMessage response = await SendAsync(
                client,
                HttpMethod.Post,
                route,
                request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.IsSuccessStatusCode);

            using JsonDocument problem = await ReadJsonAsync(response);

            Assert.Equal(
                RetCode.E_INVALID_ARGUMENT,
                problem.RootElement.GetProperty(RetCodeMember).GetInt64());
            Assert.False(problem.RootElement.TryGetProperty(UpstreamMember, out _));
        }

        Assert.Empty(upstream.UpdateRequests);
        Assert.Equal(0, upstream.RetrieveCallCount);
    }

    /// <summary>
    /// A successful update is forwarded whole, including a database error whose statement is already
    /// redacted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CONTROL CASE, and the conflict assertions above are worth nothing without it: "a conflict never
    /// surfaces as a success" is only a real property if a success is reachable at all through the same
    /// path. Both outcomes travel the one failure-translation path, so both have to be demonstrated on it.
    /// </para>
    /// <para>
    /// It also pins the redaction rule at the boundary. A database failure reaches Gateway as a FIELD OF A
    /// SUCCESS RESPONSE rather than on a failure path, and its statement member would carry the complete
    /// generated statement - which the legacy logger emitted with no redaction whatsoever. The projection
    /// forwards the payload for a caller entitled to it and never logs or reconstructs it, and this test's
    /// fixture deliberately carries an ALREADY-REDACTED placeholder rather than a statement with
    /// interpolated literal values, so that no realistic statement text exists in this file to leak.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASuccessfulUpdateIsForwardedWholeWithItsAlreadyRedactedDatabaseError()
    {
        UpdateResponse answer = new()
        {
            RetCode = WireRetCode.Ok,
            RowsInserted = 1,
            RowsUpdated = 2,
            RowsDeleted = 0,
            Error = new DbError
            {
                Sqldbcode = -1,
                Sqlerrtext = "[redacted]",
                Sqlsyntax = "[redacted]",
                Buffer = DwBuffer.Primary,
                Row = 3,
            },
        };

        ScriptedDataWindowServiceClient upstream = new(update: (_, _) => Task.FromResult(answer));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();

        string payload = await PostUpdateAndReadAsync(client, HttpStatusCode.OK);

        Assert.Equal(NormalizeJson(CanonicalJson.Format(answer)), NormalizeJson(payload));

        using JsonDocument body = JsonDocument.Parse(payload);

        Assert.Equal("1", body.RootElement.GetProperty("rowsInserted").GetString());
        Assert.Equal("2", body.RootElement.GetProperty("rowsUpdated").GetString());
        Assert.Equal(
            "[redacted]",
            body.RootElement.GetProperty("error").GetProperty("sqlsyntax").GetString());
        Assert.False(body.RootElement.TryGetProperty(ConflictMember, out _));
    }

    /// <summary>
    /// The projected retrieval keeps chunk order and every chunk's own metadata.
    /// </summary>
    /// <remarks>
    /// The retrieval third of the legacy triple is a server stream, and it is published as an ordered
    /// collection rather than flattened into a rowset. Each chunk keeps its own ordinal, its final marker
    /// and its own row count, so a consumer reconstructs exactly what a gRPC consumer sees and identifies
    /// the last chunk AS the last rather than inferring it from the collection ending. Order is arrival
    /// order and is never sorted.
    /// </remarks>
    [Fact]
    public async Task TheProjectedRetrievalPreservesChunkOrderAndPerChunkMetadata()
    {
        RetrieveChunk[] chunks =
        [
            new() { ChunkIndex = 1, RowCount = 2, CumulativeRowCount = 2, Final = false },
            new() { ChunkIndex = 2, RowCount = 1, CumulativeRowCount = 3, Final = true },
        ];

        ScriptedDataWindowServiceClient upstream = new(chunks: chunks);

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            RetrieveRoute,
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument body = await ReadJsonAsync(response);

        Assert.Equal(1, upstream.RetrieveCallCount);
        Assert.Equal(2, body.RootElement.GetArrayLength());
        Assert.Equal("1", body.RootElement[0].GetProperty("chunkIndex").GetString());
        Assert.Equal("2", body.RootElement[1].GetProperty("chunkIndex").GetString());
        Assert.False(body.RootElement[0].GetProperty("final").GetBoolean());
        Assert.True(body.RootElement[1].GetProperty("final").GetBoolean());
    }

    /// <summary>
    /// The projected event-gate read passes the session identifier through unchanged.
    /// </summary>
    /// <remarks>
    /// The third registration shape: an operation whose only argument is a correlation identifier. Gateway
    /// holds no state keyed by it - the four pieces of cross-event mutable state the legacy keeps as private
    /// fields on the control [<c>se_cst_dw.sru:L89-L96</c>] live inside DataServices, because a stateless
    /// request boundary has nowhere to put them. So the only thing to assert here is that the identifier
    /// arrives upstream exactly as it was sent, and that this shape reaches the same translation path as the
    /// other two.
    /// </remarks>
    [Fact]
    public async Task TheProjectedEventGateReadPassesTheSessionIdentifierThroughUnchanged()
    {
        const string sessionId = "session-under-test";

        ScriptedDataWindowServiceClient upstream = new(
            eventGate: (_, _) => Task.FromResult(new GetEventGateResponse
            {
                RetCode = WireRetCode.Ok,
                Gate = new EventGate { Mask = 0 },
            }));

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Get,
            $"{EventGateRoute}?sessionId={sessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(sessionId, Assert.Single(upstream.EventGateSessionIds));
    }

    /// <summary>
    /// A caller that disconnects mid-request draws no fabricated server error.
    /// </summary>
    /// <remarks>
    /// No response can be delivered on a connection that no longer exists, so the request is completed
    /// without a body. Fabricating a <c>5xx</c> here would record a fault that did not occur and would put
    /// a failure in the operator's record for a caller's own decision to walk away. The property is
    /// observable from the outside only as "the caller sees its own cancellation and nothing else was
    /// invented", which is what this asserts.
    /// </remarks>
    [Fact]
    public async Task ACallerThatDisconnectsDrawsNoFabricatedServerError()
    {
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ScriptedDataWindowServiceClient upstream = new(
            update: async (_, options) =>
            {
                reached.TrySetResult();

                // STALLS ON THE TOKEN AND ON NOTHING ELSE. The token is the one the projection handed
                // down, which is the caller's own connection lifetime, so the ONLY thing that can end this
                // wait is the cancellation the test is about to issue - which is exactly the property under
                // test.
                //
                // AN INFINITE STALL RATHER THAN A FINITE ONE, and the difference is not cosmetic. This used
                // to wait five seconds, and a finite bound inside the subject is a second, silent exit from
                // the wait: on a heavily loaded agent the delay could elapse before the cancellation was
                // observed, the double would return a normal response, and the test would fail while
                // reporting nothing true about cancellation propagation. With no duration there is nothing
                // to expire, so the test can only pass by the token being honoured. The liveness bound
                // belongs to the test runner, which ends a genuinely hung run on its own timeout - a
                // separate mechanism outside the assertion, which is where it should be (AAP 0.8.5).
                await Task.Delay(Timeout.InfiniteTimeSpan, options.CancellationToken);

                return new UpdateResponse { RetCode = WireRetCode.Ok };
            });

        await using GatewayTestHostFixture proxyHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(proxyHost, upstream);

        using HttpClient client = proxyHost.CreateAuthenticatedClient();
        using CancellationTokenSource caller = new();
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(UpdateRoute, UriKind.Relative))
        {
            Content = request,
        };

        Task<HttpResponseMessage> pending = client.SendAsync(message, caller.Token);

        await reached.Task.WaitAsync(TestContext.Current.CancellationToken);

        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        // The upstream was reached exactly once and nothing was re-issued behind the caller's back.
        Assert.Single(upstream.UpdateRequests);
    }

    /// <summary>
    /// The conflict trailer contract is declared on the update descriptor, which is what the mapping rests
    /// on.
    /// </summary>
    /// <remarks>
    /// A gRPC status carries only a code and a message, so the conflict payload travels as a versioned
    /// BINARY trailer and the contract declares the key, the accompanying status and the payload type as a
    /// custom method option - precisely so a client discovers all three from the descriptor rather than from
    /// a comment. This asserts the declaration itself: the accompanying status is <c>Aborted</c>, which is
    /// the canonical mapping to <c>409</c>, and the key ends in the binary marker without which gRPC would
    /// transport a protobuf payload as text and corrupt it. Every other assertion in this section reads the
    /// key from here rather than restating it, so a renamed key fails this suite instead of bypassing it.
    /// </remarks>
    [Fact]
    public void TheConflictTrailerContractIsDeclaredOnTheUpdateDescriptor()
    {
        Assert.Equal((int)StatusCode.Aborted, UpdateRichErrorBinding.GrpcStatusCode);
        Assert.Equal(RichErrorTrailer.Descriptor.FullName, UpdateRichErrorBinding.PayloadType);
        Assert.EndsWith("-bin", UpdateRichErrorBinding.TrailerKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unclassifiable upstream status is an internal error, and is not conflated with a reported one.
    /// </summary>
    /// <remarks>
    /// A gRPC failure carrying <c>OK</c> is a fault in the calling layer rather than an upstream outcome, so
    /// there is no honest classification for it and the legacy catalogue's <c>UNKNOWN</c> - which exists in
    /// the oracle precisely for the unclassifiable case - is published. What must NOT happen is that it
    /// becomes indistinguishable from an upstream that genuinely reported an internal failure: those call
    /// for different investigations, so the two carry different return codes and different prose.
    /// </remarks>
    [Fact]
    public async Task AnUnclassifiableUpstreamStatusIsDistinguishableFromAReportedInternalError()
    {
        ScriptedDataWindowServiceClient unclassifiable = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new RpcException(new Status(StatusCode.OK, "a fault in the calling layer"))));

        await using GatewayTestHostFixture unclassifiableHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(unclassifiableHost, unclassifiable);

        using HttpClient unclassifiableClient = unclassifiableHost.CreateAuthenticatedClient();

        using JsonDocument unclassifiableProblem = JsonDocument.Parse(
            await PostUpdateAndReadAsync(unclassifiableClient, HttpStatusCode.InternalServerError));

        Assert.Equal(
            RetCode.UNKNOWN,
            unclassifiableProblem.RootElement.GetProperty(RetCodeMember).GetInt64());

        ScriptedDataWindowServiceClient reported = new(
            update: (_, _) => Task.FromException<UpdateResponse>(
                new RpcException(new Status(StatusCode.Internal, "the upstream reported a failure"))));

        await using GatewayTestHostFixture reportedHost =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteDataServicesClient(reportedHost, reported);

        using HttpClient reportedClient = reportedHost.CreateAuthenticatedClient();

        using JsonDocument reportedProblem = JsonDocument.Parse(
            await PostUpdateAndReadAsync(reportedClient, HttpStatusCode.InternalServerError));

        Assert.Equal(
            RetCode.E_INTERNAL_ERROR,
            reportedProblem.RootElement.GetProperty(RetCodeMember).GetInt64());

        Assert.NotEqual(
            unclassifiableProblem.RootElement.GetProperty("detail").GetString(),
            reportedProblem.RootElement.GetProperty("detail").GetString());
    }


    // ==================================================================================================
    //  HELPERS
    // ==================================================================================================

    /// <summary>
    /// Reads contract C-03's <c>Update</c> rich-error binding off the generated descriptor.
    /// </summary>
    /// <returns>The declared binding.</returns>
    /// <exception cref="InvalidOperationException">
    /// The descriptor declares no such method, no options, or no binding. Each is a drift between the
    /// generated contract and the conflict mapping that rests on it, and a drift must fail loudly here
    /// rather than let the suite fall back to a key the service does not send.
    /// </exception>
    private static RichErrorBinding ReadUpdateRichErrorBinding()
    {
        MethodDescriptor method =
            DataWindowService.Descriptor.FindMethodByName(UpdateMethodName)
            ?? throw new InvalidOperationException(
                $"Contract C-03 declares no method named {UpdateMethodName}.");

        MethodOptions options =
            method.GetOptions()
            ?? throw new InvalidOperationException(
                $"Contract C-03's {UpdateMethodName} declares no method options, so it cannot declare a "
                    + "rich-error binding.");

        return options.GetExtension(CommonV1Extensions.RichError)
            ?? throw new InvalidOperationException(
                $"Contract C-03's {UpdateMethodName} declares no rich-error binding, so a conflict trailer "
                    + "has no key to travel under.");
    }

    /// <summary>
    /// Builds the conflict payload the projection must forward whole.
    /// </summary>
    /// <returns>One conflicting row with both value sets.</returns>
    /// <remarks>
    /// ONE COLUMN DIFFERS BETWEEN THE TWO VALUE SETS, on purpose. Comparing them is what tells a caller
    /// WHICH column moved underneath it, and that comparison is the whole diagnostic value of the payload -
    /// so a fixture whose two sets were identical would let a projection that dropped one of them pass.
    /// The counts state the classic single-row optimistic-concurrency failure: one row expected, none
    /// matched, which keeps "the row changed" distinguishable from "the row was deleted".
    /// </remarks>
    private static ConflictDetail BuildConflictDetail()
    {
        ConflictRow row = new()
        {
            Buffer = DwBuffer.Primary,
            Row = 1,
            ItemStatus = ItemStatus.DataModified,
        };

        row.CurrentValues.Add(new ColumnValue
        {
            ColumnName = "id",
            ColumnId = 1,
            Value = new ProtobufAnyValue { Int64Value = 7 },
        });
        row.CurrentValues.Add(new ColumnValue
        {
            ColumnName = "salary",
            ColumnId = 5,
            Value = new ProtobufAnyValue { DoubleValue = 61000d },
        });

        row.OriginalValues.Add(new ColumnValue
        {
            ColumnName = "id",
            ColumnId = 1,
            Value = new ProtobufAnyValue { Int64Value = 7 },
        });
        row.OriginalValues.Add(new ColumnValue
        {
            ColumnName = "salary",
            ColumnId = 5,
            Value = new ProtobufAnyValue { DoubleValue = 58000d },
        });

        ConflictDetail detail = new()
        {
            UpdateTable = ConflictUpdateTable,
            RowsExpected = 1,
            RowsMatched = 0,
        };

        detail.Rows.Add(row);

        return detail;
    }

    /// <summary>
    /// Builds the aborted status a conflicting update answers with, payload attached as the contract says.
    /// </summary>
    /// <param name="detail">The conflict payload.</param>
    /// <param name="retCode">The reconciled outcome code the trailer carries.</param>
    /// <returns>The failure the upstream stub raises.</returns>
    /// <remarks>
    /// The key comes from the descriptor, and the byte-valued accessor is the one used because the key's
    /// trailing binary marker is not decoration: gRPC transports a key with that suffix as binary and
    /// permits only text under any other, so a protobuf payload under a non-binary key would be corrupted
    /// in transit.
    /// </remarks>
    private static RpcException AbortedWithConflict(ConflictDetail detail, long retCode)
    {
        RichErrorTrailer trailer = new() { Conflict = detail, RetCode = retCode };

        Metadata trailers = new() { { UpdateRichErrorBinding.TrailerKey, trailer.ToByteArray() } };

        return new RpcException(
            new Status(StatusCode.Aborted, "the row moved underneath the caller"),
            trailers);
    }

    /// <summary>
    /// Installs a scripted upstream on a locally owned host, in place of the fixture's refusing default.
    /// </summary>
    /// <param name="fixture">The locally owned host to configure.</param>
    /// <param name="upstream">The scripted C-03 stub the typed client should call.</param>
    /// <remarks>
    /// <para>
    /// A LOCALLY OWNED HOST EVERY TIME, never the shared class fixture. The shared instance registers a
    /// DataWindow client that throws on resolution - which is correct for a test whose subject is the
    /// authorization boundary - and mutating it would make every other test in this class depend on
    /// execution order. Owning the host keeps each projection test order-independent and parallel-safe.
    /// </para>
    /// <para>
    /// The registration goes through the fixture's own extension point, which is applied LAST, so it wins
    /// over the refusing default without weakening anything else: token validation, the frozen clock and the
    /// unreachable-network handler all stay exactly as the fixture established them. Nothing here opens a
    /// socket, and no store of any kind is introduced - Gateway holds no storage provider.
    /// </para>
    /// </remarks>
    private static void SubstituteDataServicesClient(
        GatewayTestHostFixture fixture,
        ScriptedDataWindowServiceClient upstream)
    {
        DataServicesClient substituted = new(
            upstream,
            new InertColumnExpressionServiceClient(),
            new StubServiceTokenProvider(),
            NullLogger<DataServicesClient>.Instance);

        fixture.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();
            services.AddSingleton(substituted);
        });
    }

    /// <summary>Sends one request to a relative path.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The relative route, query string included when one is intended.</param>
    /// <param name="content">The body, or <see langword="null"/> to send none.</param>
    /// <returns>The response, which the caller disposes.</returns>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Posts the canonical update body and asserts the status the case expects.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="expectedStatus">The status the projection must answer with.</param>
    /// <returns>The response body, for further assertions.</returns>
    /// <remarks>
    /// The body names the sole updatable DataWindow in the legacy estate and carries no rows, which is
    /// sufficient for every status assertion here: what is under test is the TRANSLATION of the upstream's
    /// answer, and the upstream is scripted rather than driven by the request.
    /// </remarks>
    private static async Task<string> PostUpdateAndReadAsync(
        HttpClient client,
        HttpStatusCode expectedStatus)
    {
        using StringContent request = JsonBody("{\"datawindowHandle\":\"dw_sqlite\"}");

        using HttpResponseMessage response = await SendAsync(
            client,
            HttpMethod.Post,
            UpdateRoute,
            request);

        Assert.Equal(expectedStatus, response.StatusCode);

        return await ReadTextAsync(response);
    }

    /// <summary>Reads a response body as text, without interpreting it.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The body, empty when the response carried none.</returns>
    private static async Task<string> ReadTextAsync(HttpResponseMessage response)
        => await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    /// <summary>Reads a response body as JSON.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed document, which the caller disposes.</returns>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await ReadTextAsync(response));

    /// <summary>Reads a reserved response body as the contract's own published type.</summary>
    /// <param name="response">The response to read.</param>
    /// <returns>The parsed body.</returns>
    private static async Task<ReservedRouteBody> ReadReservedBodyAsync(HttpResponseMessage response)
        => ParseReservedBody(await ReadTextAsync(response));

    /// <summary>Parses a reserved response body as the contract's own published type.</summary>
    /// <param name="payload">The raw body.</param>
    /// <returns>The parsed body.</returns>
    /// <remarks>
    /// DESERIALIZING INTO THE PUBLISHED TYPE IS ITSELF AN ASSERTION. Every member of that record is
    /// <c>required</c>, so a body that omitted one fails here instead of passing with a silent default -
    /// and it is the type the endpoint actually serializes, so a member renamed on one side cannot be
    /// silently accommodated on the other.
    /// </remarks>
    private static ReservedRouteBody ParseReservedBody(string payload)
        => Assert.IsType<ReservedRouteBody>(JsonSerializer.Deserialize<ReservedRouteBody>(payload));

    /// <summary>Wraps a body as a JSON request payload.</summary>
    /// <param name="body">The body text, which may deliberately be unbindable.</param>
    /// <returns>The content, which the caller disposes.</returns>
    private static StringContent JsonBody(string body)
        => new(body, Encoding.UTF8, MediaTypeNames.Application.Json);

    /// <summary>Re-renders JSON compactly so that only its content is compared.</summary>
    /// <param name="payload">The JSON to normalize.</param>
    /// <returns>The compact rendering.</returns>
    /// <remarks>
    /// Whitespace is not part of a payload's meaning, and member ORDER is preserved by the parse - so two
    /// normalized documents comparing equal is a statement about content and ordering, which is exactly the
    /// claim "the payload was forwarded unchanged" makes.
    /// </remarks>
    private static string NormalizeJson(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        return JsonSerializer.Serialize(document.RootElement);
    }

    /// <summary>Renders a problem document with its correlation identifier removed.</summary>
    /// <param name="payload">The problem document.</param>
    /// <returns>The remaining members, in order, rendered compactly.</returns>
    /// <remarks>
    /// The identifier names the OCCURRENCE, so it is the one member of a problem document that must differ
    /// between two occurrences of the same failure. Removing it is what turns a comparison of two responses
    /// into a comparison of the ANSWER, which is the property a caller's retry-or-surface policy depends on.
    /// </remarks>
    private static string WithoutCorrelationId(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);

        foreach (JsonProperty member in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(member.Name, TraceIdMember, StringComparison.Ordinal))
            {
                members[member.Name] = member.Value.Clone();
            }
        }

        return JsonSerializer.Serialize(members);
    }

    /// <summary>Reads the correlation identifier a problem document carries.</summary>
    /// <param name="payload">The problem document.</param>
    /// <returns>The identifier, or an empty string when the document carried none.</returns>
    private static string ReadCorrelationId(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);

        return document.RootElement.TryGetProperty(TraceIdMember, out JsonElement traceId)
            ? traceId.GetString() ?? string.Empty
            : string.Empty;
    }
}
