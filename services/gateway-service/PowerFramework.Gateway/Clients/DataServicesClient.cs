// ==================================================================================================
//  PowerFramework.Gateway.Clients.DataServicesClient
//  ------------------------------------------------------------------------------------------------
//  Gateway's outbound edge to the DataServices service: a thin, typed wrapper over the generated gRPC
//  clients for contract C-03 (dataservices.v1.DataWindowService) and contract C-04
//  (dataservices.v1.ColumnExpressionService).
//
//  THIS FILE HAS NO LEGACY SOURCE, AND THAT IS A FINDING RATHER THAN AN OMISSION.
//  The legacy PowerFramework is a LIBRARY with no process of its own - no listener, no route table,
//  no serialization layer and no authentication of any kind, because there was nothing to
//  authenticate against. ws_objects/pfw.pbl.src/pfw.sra is 145 lines of application lifecycle
//  (open -> pfwInitialize, close -> pfwFinalize, systemerror -> unpack a seven-field assert payload
//  and HALT CLOSE) and contains no client, credential, token or HTTP concept whatsoever. So there is
//  nothing here to port and no legacy client shape to imitate: every cross-service contract in this
//  refactor is NET NEW. The legacy objects recorded as this file's sources - se_cst_dw.sru,
//  n_cst_dwsvc.sru, n_cst_dwsvc_columnexp.sru, n_cst_thread_task_sqlupdate.sru,
//  n_cst_threading.sru, retcode.sru, pfw.sra and docs/n_cst_dwsvc_columnexp.md - are REFERENCE ONLY:
//  they are where the BEHAVIOURS THE CONTRACT CARRIES come from, they are read to understand what
//  must survive the boundary, and they are never ported here and never edited.
//
//  ------------------------------------------------------------------------------------------------
//  (a) WHY gRPC FOR THIS UPSTREAM, AND NOT REST
//  ------------------------------------------------------------------------------------------------
//  The transport was chosen per service from the shape of the interface being replaced, and
//  DataServices' interface is the reason this one is gRPC while Gateway's own ingress is REST.
//  se_cst_dw.sru declares exactly 22 events at :L11-L32 - 9 semantic and 13 raw `pbm_dwn*` - as ONE
//  ordered chain in which a raw event has up to two vetoable edges, a partner call and an event-broker
//  trigger, with which edges exist and in which order VARYING BY EVENT [:L115-L180, :L387-L401]. Three
//  properties of that surface decide the transport:
//
//    1. AN ORDERED, STATEFUL CHAIN. The validation-error event READS AND CLEARS the item-change
//       result the preceding event stashed (the four cross-event fields at se_cst_dw.sru:L89-L96),
//       so its behaviour is a function of the prior event's return value. That needs one ordered
//       bidirectional conversation, not a set of independent requests.
//    2. A `ref string` OUT-PARAMETER. `onddsgetfilter(... ref string filter)` at :L13 produces its
//       result THROUGH the parameter, so "assigned an empty filter" and "did not touch the
//       parameter" are two different instructions. The wire keeps them apart with explicit field
//       presence (EventResult.produced_filter is `optional`); JSON's absent-versus-empty conflation
//       would merge them.
//    3. AN `any` RETURN OVER A `string args[]` ARGUMENT. `oncolumnexpinvokemethod` at :L14 returns
//       `any`, so a macro returning a decimal, a date or a null must stay distinguishable from one
//       returning their string renderings, because the coercion the expression engine then performs
//       depends on which it was.
//
//  Add the TRI-VALUED veto (continue / prevent-once / prevent-deep) and the FOUR-VALUE item-change
//  alphabet {0,1,2,3}, and the transport has to carry compile-time contract enforcement,
//  bidirectional streaming and a status model rich enough for both alphabets. Protobuf over gRPC
//  carries all three NATIVELY. JSON over REST can encode each by CONVENTION, but nothing in the format
//  ENFORCES the ordering or the veto's arity, so a consumer that flattens the veto to a boolean is
//  producing valid JSON while silently converting a deep prevention into a shallow one and letting the
//  events the caller meant to stop fire anyway. And a request/response projection cannot carry a
//  server-initiated question at all, which is what the answer-bearing semantic events are.
//
//  ------------------------------------------------------------------------------------------------
//  (b) RESILIENCE: WHY THE DEPENDENCY EXISTS, AND THE ONE STATUS THAT MUST NEVER BE RETRIED
//  ------------------------------------------------------------------------------------------------
//  Microsoft.Extensions.Http.Resilience wraps this client, and it is registered in Program.cs
//  together with the channel and the upstream address (see `Gateway:Upstreams:DataServices`). This
//  file constructs no channel, no message handler and no resilience pipeline: doing so would defeat
//  that registration and break the substitution seam the tests rely on.
//
//  The dependency is required BY the transition rather than layered on top of it, and the
//  justification is one sentence: AN IN-PROCESS CALL CANNOT FAIL IN TRANSIT AND A NETWORK CALL CAN.
//  Every edge this file speaks over is an edge the legacy simply did not have. Handling a failure
//  mode that decomposition itself CREATES is part of preserving behaviour, not an improvement: without
//  it the first transient network fault would surface as a defect the legacy could not have had,
//  which is a regression introduced by the refactor. No latency, throughput, scalability or
//  availability property is asserted or implied anywhere in this file - the repository publishes no
//  such budget, so none may be claimed, and no value here is justified by a performance argument.
//
//  WHAT MAY BE RETRIED IS NARROW, AND ONE STATUS IS ABSOLUTELY EXCLUDED:
//    * `Aborted` MUST NEVER BE RETRIED. It is an optimistic-concurrency conflict, which is a
//      DEFINITIVE ANSWER and not a transient fault. Re-sending the same payload produces the same
//      conflict, because the original values it carries are still stale; retrying it automatically
//      is precisely the silent-overwrite failure mode this system forbids everywhere. The caller
//      re-reads and rebases, or surfaces the conflict. Nothing else.
//    * `Update` IS NOT IDEMPOTENT and must not be retried by policy at all.
//    * A streaming call must not be retried mid-stream: replaying already-delivered events into an
//      ordered chain would corrupt exactly the ordering the chain exists to preserve.
//  Every public member below documents its own retry safety so the shape of this surface cannot
//  invite a wrong policy.
//
//  ------------------------------------------------------------------------------------------------
//  (c) WHY TWO OF THE C-04 STREAMS ARE INVERTED
//  ------------------------------------------------------------------------------------------------
//  On `InvokeMethodChannel` and `TraceChannel` the SERVER asks and the CLIENT answers, which is
//  visible in the generated signatures: InvokeMethodChannel yields
//  AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> - the client WRITES responses
//  and READS requests. That inversion is structural, not stylistic. The legacy expects THE
//  APPLICATION to implement the macro switch: docs/n_cst_dwsvc_columnexp.md shows the macro body
//  living in the DataWindow's own `OnColumnExpInvokeMethod` event, with the engine calling out to it
//  mid-calculation. Across this boundary the engine is in DataServices and the application is on
//  Gateway's side, so DataServices must call BACK into its client to finish a calculation. Gateway is
//  that client, so this file must SERVE inbound macro invocations rather than merely read a stream -
//  hence a duplex channel driven by a caller-supplied handler.
//  `TraceChannel` is inverted for the same structural reason but carries the opposite ordering
//  discipline: it is pure diagnostics, fire-and-forget, and therefore must never block or gate an
//  expression result. It is a separate call on its own stream and is never awaited from any other
//  member here.
//
//  ------------------------------------------------------------------------------------------------
//  (d) WHY NO PERSISTENCE TYPE IS REFERENCED, EVEN THOUGH THEY COMPILE
//  ------------------------------------------------------------------------------------------------
//  The whole contracts project arrives through ONE ProjectReference, so the Persistence service's own
//  generated protocol types are REACHABLE from here. They are deliberately not reached. The topology
//  is layered and acyclic - Gateway calls DataServices and Security; DataServices calls Persistence;
//  Persistence reads Security's keys only - and nothing but DataServices calls Persistence. Consuming
//  one of those types here would breach that layering just as surely as adding a Persistence client
//  would, and it would do so invisibly, because it costs one `using`. There is no Persistence client
//  type in this folder and there must never be one. DataServices has one of its own; that is its
//  file, at its layer, and it is not a pattern to mirror here.
//  The likeliest accidental route in is the database error shape, so it is worth naming: `DbError`
//  lives in the COMMON protocol definition, not in the Persistence one, and the common one is what
//  this file uses. Gateway's aggregate health view of Persistence is likewise not this file's concern
//  - that belongs to the anonymous /health probe and its health-check registration, never to a client
//  type here.
//
//  ------------------------------------------------------------------------------------------------
//  DELIBERATE ABSENCES, each a constraint rather than an oversight
//  ------------------------------------------------------------------------------------------------
//    * NO channel construction, address configuration or resilience registration (Program.cs owns
//      all three). No gRPC channel is constructed anywhere in this file, by any means.
//    * NO token minting, signing key, signing credential, token handler, JWKS retrieval or discovery
//      lookup. Gateway holds VERIFICATION material only and mints nothing; Security is the sole
//      issuer. Inbound validation is the stock JwtBearer handler's job in Program.cs. Outbound
//      credentials are obtained exclusively through IServiceTokenProvider, whose only implementation
//      is the sibling SecurityClient in this same folder - token acquisition is not duplicated here
//      and the issuance endpoint is never called directly.
//    * NO secret, key, credential, certificate, PEM block or token literal - in source, in a comment,
//      in a default value or in an XML-doc example. The two identifiers below are non-secret
//      protocol names, not credentials.
//    * NO storage of any kind: no connection string, no database context, no SQL text, no database
//      access. Persistence is the only service with a storage provider and Gateway's manifest omits
//      every data package.
//    * NO caching, response transformation, convenience aggregation, client-side reordering,
//      buffering, de-duplication, merging or "helpful" normalization of the event chain or the
//      expression payloads. This is a transport wrapper; the contract's payloads pass through it
//      unaltered.
//    * NO client, partial client, placeholder, interface or option anticipating any of the four
//      deferred capability areas - the design system, the document utilities, the outbound
//      integration transports and the script bridge. Those four are out of scope entirely and appear
//      in this system only as Gateway's four reserved routes, which are routing metadata elsewhere.
//      Relatedly, C-03 ships only the HEADLESS half of the column-sort, context-menu and
//      drop-down-search services - item models, computed LOGICAL text widths, filter and sort
//      expressions, search state - and this file therefore carries no window geometry, no
//      DPI-to-pixel conversion, no font metric and no rendering concern of any kind. That gap is
//      documented and deliberate.
//    * NO SCREAMING_SNAKE identifier is declared here. Those spellings are preserved only in the
//      files the repository-root .editorconfig names, and no Gateway file is among them; wire
//      constants are consumed from the generated protobuf enums instead.
//
// ==================================================================================================

using System.Globalization;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;

// THE ONE NAME COLLISION IN THIS FILE, RESOLVED DELIBERATELY RATHER THAN DISCOVERED AS A BUILD BREAK.
//
// `RetCode` exists in BOTH imported namespaces: PowerFramework.Contracts.Common.V1.RetCode is the
// generated protobuf WRAPPER MESSAGE whose nested `Types.Value` enum is the wire code, and
// PowerFramework.Shared.Kernel.RetCode is the ported static class of `long` constants. A bare
// `RetCode` with both namespaces in scope is CS0104 "ambiguous reference". This is exactly the class
// of collision a migration out of PowerBuilder's single flat global namespace has to resolve
// explicitly: there were no import statements to rewrite, so the work is namespace creation plus
// resolving what a flat namespace tolerated.
//
// The alias below names the WIRE enum, which is what this file reads off every response. The kernel
// class is never referred to by its simple name here at all; Shared.Kernel is imported solely for
// `Predicates`, so that success and failure are classified by the PORTED TRI-STATE ALGEBRA rather
// than by an ad-hoc `< 0` written here. That algebra matters: a prevention (1) reads as SUCCEEDED
// because the predicate tests `>= 0`, and CANCELLED (-2) is explicitly excluded from failure while
// also failing the success test, so it is NEITHER. Re-deriving those boundaries by hand is how they
// get quietly lost.
//
// Their numeric values are declared to agree value for value with the kernel constants, but that
// agreement is a REVIEW-TIME INVARIANT WITH NO COMPILE-TIME ENFORCEMENT - the contracts project
// deliberately holds no reference to Shared.Kernel. So nothing here assumes a cast between them is
// checked for anyone; where a wire code is handed to a kernel predicate it is widened explicitly and
// the reason is stated at the call site.
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Gateway.Clients;

/// <summary>
/// Evaluates one macro invocation that the column-expression engine has asked its client to perform,
/// and produces the answer the engine will consume to continue the calculation.
/// </summary>
/// <param name="request">
/// The invocation as the engine issued it: the expression session, the DataWindow handle, the
/// correlating <c>InvocationId</c>, the row and column being calculated, the macro name, its
/// positional arguments, whether the call was direct or dynamic (<c>Form</c>), and whether the engine
/// recognised the name as one of its own built-ins.
/// </param>
/// <param name="cancellationToken">
/// Cancels the evaluation. Honouring it tears the duplex channel down cleanly instead of leaving the
/// engine waiting for an answer that will never arrive.
/// </param>
/// <returns>
/// The answer. Its <c>InvocationId</c> MUST be the request's <c>InvocationId</c>, because that is the
/// only thing correlating an answer with the calculation that is blocked on it. Set <c>Unhandled</c>
/// when the application recognises no such macro, and populate <c>Error</c> when it recognised the
/// macro and refused it - those are different outcomes and the engine treats them differently.
/// </returns>
/// <remarks>
/// <para>
/// THIS DELEGATE IS THE INVERTED HALF OF CONTRACT C-04, and its existence is structural rather than
/// stylistic. The legacy expects the APPLICATION to implement the macro switch - the body lives in the
/// DataWindow's own <c>OnColumnExpInvokeMethod</c> event, and the engine calls out to it in the middle
/// of a calculation. Across this service boundary the engine is in DataServices while the application
/// is on Gateway's side, so DataServices has to call back into its client. This is that callback.
/// </para>
/// <para>
/// The invocation is SYNCHRONOUS WITHIN THE STREAM: the calculation cannot proceed without the value,
/// so the engine is waiting while this runs. Returning quickly is a correctness property of the
/// conversation - not a performance claim, of which this file makes none - because until an answer is
/// written the ordered chain the engine is running has not advanced.
/// </para>
/// </remarks>
public delegate ValueTask<InvokeMethodResponse> MacroInvocationHandler(
    InvokeMethodRequest request,
    CancellationToken cancellationToken);

/// <summary>
/// Thrown when DataServices reports an optimistic-concurrency conflict, carrying the conflict detail
/// intact so a caller can decide between retrying and surfacing.
/// </summary>
/// <remarks>
/// <para>
/// THIS IS THE MOST IMPORTANT TYPE IN THIS FILE. On an <c>updatewhere=1</c> mismatch DataServices
/// answers with gRPC <see cref="StatusCode.Aborted"/> and a <see cref="ConflictDetail"/> attached to
/// the status as a versioned binary trailer. <see cref="Conflict"/> holds THE MESSAGE OBJECT ITSELF,
/// never a stringified rendering, so the REST projection can serialize the same payload as an HTTP
/// 409 body without reconstructing anything. <c>Aborted</c> is the canonical gRPC-to-409 mapping and
/// the ingress contract fixes it as such.
/// </para>
/// <para>
/// WHY LOSING THE PAYLOAD WOULD BE A BEHAVIOURAL REGRESSION, MECHANICALLY. The generated update
/// statement's WHERE clause carries the key column PLUS THE ORIGINAL VALUES OF EVERY UPDATEABLE
/// COLUMN - on the one updatable DataWindow in the legacy estate that is all six marked columns - so
/// the conflict answer contains information the caller CANNOT RECONSTRUCT: which column moved
/// underneath it, and what its value is now. Comparing <c>current_values</c> against
/// <c>original_values</c> per row is the whole diagnostic. Swallowing it, flattening it to a boolean
/// or a null, or replacing it with a message string leaves the caller a choice between a blind
/// overwrite and a spurious failure, and both are regressions.
/// </para>
/// <para>
/// A CONFLICT IS A DEFINITIVE ANSWER, NOT A TRANSIENT FAULT, so it is never retried automatically -
/// not here and not by any resilience policy. Re-sending the same payload produces the same conflict
/// because the original values it carries are still stale. A caller re-reads, rebases and resubmits,
/// or surfaces the conflict to whoever can decide. There is no silent overwrite anywhere in this
/// system.
/// </para>
/// <para>
/// The originating <see cref="RpcException"/> is preserved as the inner exception, so
/// <see cref="Status"/>, <see cref="Trailers"/> and the raw trailer bytes all remain available to a
/// caller that wants them.
/// </para>
/// </remarks>
public sealed class DataServicesConflictException : Exception
{
    private const string DefaultMessage =
        "DataServices reported an optimistic-concurrency conflict. The submitted rows carried original "
        + "values that no longer match the current server state, so the update was not applied.";

    /// <summary>
    /// Creates an instance with the default message and no conflict detail.
    /// </summary>
    /// <remarks>
    /// Present because the exception-constructor convention requires it. The client never uses this
    /// path: a conflict without its detail is not actionable, and where the detail cannot be decoded
    /// the client rethrows the original <see cref="RpcException"/> untouched instead of raising a
    /// detail-less conflict.
    /// </remarks>
    public DataServicesConflictException()
        : base(DefaultMessage)
    {
    }

    /// <summary>
    /// Creates an instance with the supplied message and no conflict detail.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <remarks>See <see cref="DataServicesConflictException()"/> for why the client never uses this path.</remarks>
    public DataServicesConflictException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance with the supplied message and inner exception, and no conflict detail.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The originating exception.</param>
    /// <remarks>See <see cref="DataServicesConflictException()"/> for why the client never uses this path.</remarks>
    public DataServicesConflictException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates the instance the client actually raises: a conflict carrying its detail.
    /// </summary>
    /// <param name="conflict">
    /// The decoded conflict detail, forwarded unchanged. Never reshaped, never summarised.
    /// </param>
    /// <param name="retCode">
    /// The reconciled outcome code the trailer carried, numerically a wire <c>RetCode</c> value. It is
    /// kept as a <see cref="long"/> rather than being cast into an enum, because a code this client
    /// does not recognise must still reach the caller rather than becoming an undefined enum member.
    /// </param>
    /// <param name="innerException">
    /// The originating <see cref="RpcException"/>, preserved so the status and the full trailer set
    /// remain available.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="conflict"/> or <paramref name="innerException"/> is <see langword="null"/>.
    /// </exception>
    public DataServicesConflictException(
        ConflictDetail conflict,
        long retCode,
        RpcException innerException)
        : base(BuildMessage(conflict, retCode), innerException)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(innerException);

        Conflict = conflict;
        RetCode = retCode;
        Status = innerException.Status;
        Trailers = innerException.Trailers;
    }

    /// <summary>
    /// The conflict detail exactly as DataServices reported it, or <see langword="null"/> when the
    /// instance was created through one of the conventional constructors.
    /// </summary>
    /// <remarks>
    /// Every row that failed its check is present, each carrying its buffer, its ONE-BASED row
    /// ordinal within that buffer, its current item status, and both the current and the original
    /// values of the marked columns. The message also names the update table the failing statement
    /// targeted - which matters because multi-table update from one DataWindow is a real legacy
    /// capability - and reports rows expected against rows matched, which is what keeps "the row
    /// changed" distinguishable from "the row was deleted" without a second round trip.
    /// </remarks>
    public ConflictDetail? Conflict { get; }

    /// <summary>
    /// The reconciled outcome code from the trailer, numerically a wire <c>RetCode</c> value, or zero
    /// when no trailer was decoded.
    /// </summary>
    public long RetCode { get; }

    /// <summary>
    /// The gRPC status that carried the conflict. <see cref="StatusCode.Aborted"/> for every conflict
    /// this client raises.
    /// </summary>
    public Status Status { get; }

    /// <summary>
    /// The response trailers, retained so a caller may read the raw rich-error payload or any other
    /// trailer the upstream attached.
    /// </summary>
    public Metadata? Trailers { get; }

    /// <summary>
    /// Builds a message that states the shape of the conflict without reproducing any column value.
    /// </summary>
    /// <param name="conflict">The decoded detail.</param>
    /// <param name="retCode">The reconciled outcome code.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// Row counts and the table name only. Column values are deliberately absent from the message
    /// text: an exception message reaches logs, and the values are the caller's business data. They
    /// remain fully available on <see cref="Conflict"/>, which is where a caller reads them.
    /// </remarks>
    private static string BuildMessage(ConflictDetail conflict, long retCode) => string.Create(
        CultureInfo.InvariantCulture,
        $"{DefaultMessage} Conflicting row(s): {conflict.Rows.Count}; update table: "
        + $"'{conflict.UpdateTable}'; rows expected: {conflict.RowsExpected}; rows matched: "
        + $"{conflict.RowsMatched}; retCode: {retCode}.");
}

/// <summary>
/// Thrown when a message arrives on the event chain whose sequencing token violates the ordering
/// discipline the token itself declares.
/// </summary>
/// <remarks>
/// <para>
/// SEQUENCE NUMBERS ARE FOR DETECTION, NEVER FOR CORRECTION. Under the synchronous discipline the
/// group runs as one strictly ordered conversation inside one validation session on one stream, and an
/// out-of-order arrival is A HARD ERROR THAT FAILS THE SESSION - it is not a reorder opportunity. A
/// client that sorted, buffered-and-reordered, de-duplicated or replayed would be hiding a real fault,
/// and it would hide it in the one place where the damage is invisible: the validation-error handler
/// reads and clears the code the preceding item-change event stashed, so a reordered chain produces
/// the wrong branch while still looking like a successful conversation.
/// </para>
/// <para>
/// The session is unusable once this is raised and must be closed. Use the scope returned by
/// <see cref="DataServicesClient.OpenValidationSessionScopeAsync"/> and the session is closed for you,
/// on this path as on every other.
/// </para>
/// <para>
/// This is NOT a transient fault and must never be retried.
/// </para>
/// </remarks>
public sealed class DataServicesEventOrderingException : Exception
{
    private const string DefaultMessage =
        "A message on the DataServices event chain violated the ordering discipline its own sequencing "
        + "token declared. Sequence numbers on this stream are for detection only, so the session is "
        + "failed rather than the delivery reordered.";

    /// <summary>
    /// Creates an instance with the default message.
    /// </summary>
    public DataServicesEventOrderingException()
        : base(DefaultMessage)
    {
    }

    /// <summary>
    /// Creates an instance with the supplied message.
    /// </summary>
    /// <param name="message">The message.</param>
    public DataServicesEventOrderingException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an instance with the supplied message and inner exception.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The originating exception.</param>
    public DataServicesEventOrderingException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an instance describing the offending arrival.
    /// </summary>
    /// <param name="sessionId">The session the stream belongs to.</param>
    /// <param name="observedSequence">The sequence number that arrived. Zero means none was supplied.</param>
    /// <param name="lastObservedSequence">The highest sequence number seen before it.</param>
    /// <param name="discipline">The discipline the arriving token declared.</param>
    public DataServicesEventOrderingException(
        string? sessionId,
        long observedSequence,
        long lastObservedSequence,
        OrderingDiscipline discipline)
        : base(string.Create(
            CultureInfo.InvariantCulture,
            $"{DefaultMessage} Session '{sessionId}' received sequence {observedSequence} after "
            + $"{lastObservedSequence} under discipline {discipline}."))
    {
        SessionId = sessionId;
        ObservedSequence = observedSequence;
        LastObservedSequence = lastObservedSequence;
        Discipline = discipline;
    }

    /// <summary>The session whose stream faulted, when one was reported.</summary>
    public string? SessionId { get; }

    /// <summary>The sequence number that arrived; zero when the token supplied none.</summary>
    public long ObservedSequence { get; }

    /// <summary>The highest sequence number observed before the offending arrival.</summary>
    public long LastObservedSequence { get; }

    /// <summary>The ordering discipline the arriving token declared.</summary>
    public OrderingDiscipline Discipline { get; }
}

/// <summary>
/// One open bidirectional conversation on contract C-03's <c>EventChain</c>: the client reports events
/// inbound and answers handler invocations outbound, on a single ordered stream.
/// </summary>
/// <remarks>
/// <para>
/// WHY THE STREAM IS BIDIRECTIONAL. Nine of the twenty-two events are the framework ASKING THE
/// APPLICATION a question - what should happen on this item change, what filter should this drop-down
/// use, what does this macro evaluate to. In the legacy the application is in the same process. Across
/// this boundary DataServices holds the DataWindow logic while its client holds the application, so the
/// chain runs in both directions on one stream. Two unary calls could not do it, because the outbound
/// question arrives while the inbound call is still open.
/// </para>
/// <para>
/// ALL TWENTY-TWO EVENTS TRAVEL AS DISTINCT IDENTITIES - the 9 semantic and the 13 raw
/// <c>pbm_dwn*</c> - because a raw event has up to two independently vetoable edges, a partner call and
/// a broker trigger, and WHICH edges it has and in which order VARIES BY EVENT: two consult only the
/// broker, two trigger the broker before the partner, and one discards the broker's answer entirely
/// [<c>se_cst_dw.sru:L120-L122</c>, <c>:L387-L401</c>, <c>:L124-L128</c>]. Collapsing a pair would erase
/// a veto edge and leave a consumer unable to tell which one stopped the dispatch. This wrapper
/// collapses nothing: every response is yielded exactly as received.
/// </para>
/// <para>
/// ORDERING IS CHECKED, NEVER CORRECTED. Under
/// <see cref="OrderingDiscipline.Synchronous"/> the arriving sequence must be strictly greater than
/// every sequence seen before it on this stream, and a violation raises
/// <see cref="DataServicesEventOrderingException"/> and fails the session. Under
/// <see cref="OrderingDiscipline.Sequenced"/> the token is passed through untouched so the CONSUMER
/// may detect gaps and reorder if it chooses - that is the consumer's prerogative on those events
/// precisely because they carry no cross-event state, and it is not this client's to exercise.
/// Nothing is sorted, buffered-for-reordering, de-duplicated or replayed here under either discipline.
/// </para>
/// <para>
/// THREADING. A duplex conversation is meant to be written and read at the same time, so calling
/// <see cref="SendAsync"/> while <see cref="ReadAllAsync"/> is being enumerated is expected and
/// supported. What is NOT supported is two concurrent enumerations of <see cref="ReadAllAsync"/> or two
/// concurrent writers: gRPC permits one pending write per stream, and the ordering state below is
/// owned by the single reader.
/// </para>
/// <para>
/// Always dispose. Disposal releases the underlying call, and where the conversation has not been
/// completed it cancels it, which is what tells DataServices to stop waiting.
/// </para>
/// </remarks>
public sealed class DataWindowEventChannel : IAsyncDisposable
{
    private readonly AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> _call;
    private readonly ILogger _logger;

    private long _lastSequence;
    private bool _disposed;

    /// <summary>
    /// Wraps an already-opened duplex call. Created only by <see cref="DataServicesClient"/>, which is
    /// what keeps channel construction, address configuration and credential attachment in one place.
    /// </summary>
    /// <param name="call">The opened duplex call.</param>
    /// <param name="logger">The logger. Never receives a token or an authorization header.</param>
    internal DataWindowEventChannel(
        AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> call,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(logger);

        _call = call;
        _logger = logger;
    }

    /// <summary>
    /// The highest sequence number observed on the inbound half of this conversation so far, or zero
    /// before the first message.
    /// </summary>
    /// <remarks>
    /// Exposed for diagnostics and for a consumer implementing its own gap detection over the
    /// sequenced events. Read with a volatile read because the reader and the caller may be different
    /// threads.
    /// </remarks>
    public long LastObservedSequence => Volatile.Read(ref _lastSequence);

    /// <summary>
    /// Sends one message to DataServices: either an event notification the client is reporting, or the
    /// client's answer to a handler invocation DataServices sent.
    /// </summary>
    /// <param name="request">
    /// The message. It travels exactly as supplied - the payload, the session id and above all the
    /// sequencing token are the caller's, and this client neither assigns, renumbers nor rewrites them.
    /// </param>
    /// <param name="cancellationToken">Cancels the write and, with it, the conversation.</param>
    /// <returns>A task that completes when the message has been written to the stream.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="request"/> carries no sequencing token, carries one whose sequence is zero, or
    /// carries one whose discipline is <see cref="OrderingDiscipline.Unspecified"/>. Every message on
    /// this stream carries a token, zero means "not supplied", and an unstated discipline leaves the
    /// mandated ordering check with nothing to drive it - so all three are refused at the point where
    /// they are cheap to diagnose rather than becoming a fault at the far end.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The channel has been disposed.</exception>
    /// <exception cref="RpcException">The stream faulted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// NOT SAFE TO RETRY. Re-writing a message that may already have been delivered would duplicate an
    /// event in an ordered chain, which is the one thing the chain cannot absorb.
    /// </remarks>
    public async Task SendAsync(EventChainRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SequencingToken? token = request.Token;
        if (token is null || token.Sequence <= 0)
        {
            throw new ArgumentException(
                "Every message on the DataServices event chain carries a sequencing token whose "
                + "sequence starts at 1; a sequence of 0 means 'not supplied'. Set Token.Sequence and "
                + "Token.Discipline on the request. This client does not assign sequence numbers, "
                + "because the originator of an event is the only party that can order it.",
                nameof(request));
        }

        // AN UNSTATED DISCIPLINE IS REFUSED OUTBOUND FOR THE SAME REASON IT IS REFUSED INBOUND.
        //
        // InspectOrdering fails the session when an arriving token declares no discipline, because the
        // mandated ordering check would have nothing to drive it. That reasoning is symmetric, and
        // leaving it unenforced here was strictly worse than an argument fault: writing such a message
        // would hand the far end exactly the value its own guard treats as fatal, so a caller's
        // omission would be answered by the DESTRUCTION OF THE SESSION - and, on a chain whose whole
        // purpose is ordered cross-event state, a session lost mid-conversation cannot be resumed.
        //
        // Refusing here costs the caller an ArgumentException before anything is written, leaves the
        // session intact and names the omission. Nothing is defaulted on the caller's behalf: the
        // discipline is a property of the EVENT - synchronous for the item-change and validation chain,
        // sequenced for the notification events - so choosing one here would be this client deciding a
        // semantic the event catalogue owns.
        if (token.Discipline == OrderingDiscipline.Unspecified)
        {
            throw new ArgumentException(
                "Every message on the DataServices event chain declares its ordering discipline on the "
                + "token, and 'unspecified' is not a usable value: the far end fails the session on it, "
                + "so writing it would destroy the conversation rather than merely be rejected. Set "
                + "Token.Discipline to Synchronous for the item-change and validation chain, or to "
                + "Sequenced for the notification events. This client does not choose a default, "
                + "because the discipline is a property of the event rather than of the transport.",
                nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _call.RequestStream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals that the client will send no further messages, leaving the inbound half open until
    /// DataServices completes it.
    /// </summary>
    /// <param name="cancellationToken">
    /// Abandons the half-close and, with it, the conversation. See the remarks: the underlying gRPC
    /// API accepts no token, so cancellation is applied to the CALL rather than to the write.
    /// </param>
    /// <returns>A task that completes when the half-close has been written.</returns>
    /// <exception cref="ObjectDisposedException">The channel has been disposed.</exception>
    /// <exception cref="RpcException">The stream faulted while the half-close was being written.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled, either before the attempt or while it was
    /// pending. In the second case the conversation has been torn down and this channel is finished;
    /// the fault that the tear-down produced is retained as the inner exception.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is a half-close, not a cancellation: an in-flight answer from DataServices still arrives.
    /// Disposing without calling this cancels the call instead, which is the right outcome when the
    /// conversation is being abandoned rather than finished.
    /// </para>
    /// <para>
    /// WHY CANCELLATION IS REGISTERED AGAINST THE CALL RATHER THAN PASSED TO THE WRITE. gRPC's client
    /// stream writer exposes a completion method with NO cancellation-token overload - unlike its write
    /// method, which has one and which <see cref="SendAsync"/> uses. A half-close is nevertheless a
    /// network write and can block indefinitely: the far end may never read, or a wedged connection may
    /// leave flow control closed. Without a cancellation path this member was therefore the one place in
    /// this client where a request could hang forever with no way out, which is precisely what the
    /// file's end-to-end cancellation doctrine exists to prevent.
    /// </para>
    /// <para>
    /// So cancellation is applied one level down, at the only seam the API offers: a registration
    /// disposes the call, which cancels it and makes the pending completion fault. That fault - whatever
    /// shape it takes, and it varies with where the tear-down landed - is then translated into the
    /// cancellation the caller actually asked for, with the original retained as the inner exception so
    /// nothing is lost. The consequence is stated rather than hidden: CANCELLING A HALF-CLOSE ENDS THE
    /// CONVERSATION. That is the honest semantic, because a half-close the caller no longer wants to
    /// complete cannot be un-started, and a conversation whose outbound half is in an unknown state
    /// cannot safely carry another ordered message.
    /// </para>
    /// </remarks>
    public async Task CompleteSendingAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        Task completion = _call.RequestStream.CompleteAsync();

        if (!cancellationToken.CanBeCanceled)
        {
            // No registration is possible and none is needed; the caller has opted out of cancelling.
            await completion.ConfigureAwait(false);
            return;
        }

        // The registration is disposed on every exit, so a completed half-close leaves nothing attached
        // to the caller's token that could cancel the call later.
        using CancellationTokenRegistration registration = cancellationToken.UnsafeRegister(
            static state => ((IDisposable)state!).Dispose(),
            _call);

        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested)
        {
            // The half-close faulted because the registration tore the call down. The caller asked for
            // cancellation and receives exactly that, carrying its own token so a `catch` filtered on
            // that token matches, and carrying the underlying fault so an operator can still see how
            // the tear-down surfaced.
            throw new OperationCanceledException(
                "The half-close of the DataServices event chain was cancelled while it was pending, so "
                + "the conversation has been torn down and this channel is finished.",
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// Reads the inbound half of the conversation: the outcomes of notifications the client sent, the
    /// handler invocations DataServices is asking the client to run, and any stream-level fault.
    /// </summary>
    /// <param name="cancellationToken">Cancels the enumeration and tears the stream down.</param>
    /// <returns>
    /// The messages in arrival order, each yielded unchanged. Inspect <c>PayloadCase</c> to tell an
    /// outcome from an invocation from a fault.
    /// </returns>
    /// <exception cref="DataServicesEventOrderingException">
    /// A message arrived out of order under the synchronous discipline, or carried a token this stream
    /// cannot order by. The session is unusable afterwards and must be closed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The channel has been disposed.</exception>
    /// <exception cref="RpcException">The stream faulted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// NOT SAFE TO RETRY. Re-reading from the beginning is not possible and re-establishing the stream
    /// would replay already-delivered events into an ordered chain.
    /// </remarks>
    public async IAsyncEnumerable<EventChainResponse> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await foreach (EventChainResponse response in _call.ResponseStream
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            InspectOrdering(response);
            yield return response;
        }
    }

    /// <summary>
    /// Releases the underlying call, cancelling the conversation if it has not been completed.
    /// </summary>
    /// <returns>A completed task; disposal of a gRPC call is synchronous.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _call.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Applies the ordering discipline the arriving token declares. Detection only.
    /// </summary>
    /// <param name="response">The message that arrived.</param>
    /// <exception cref="DataServicesEventOrderingException">The token violated its own discipline.</exception>
    private void InspectOrdering(EventChainResponse response)
    {
        // A STREAM-LEVEL FAULT ALWAYS REACHES THE CALLER, WHATEVER ITS TOKEN LOOKS LIKE.
        //
        // The canonical fault on this stream IS an ordering violation the server detected, and the
        // session is already unusable by the time such a message is emitted. Refusing to deliver it
        // because its own token failed an ordering check would replace a reported fault with a
        // different, less informative one - so the check is skipped for this payload and the error is
        // handed to the caller to act on.
        if (response.PayloadCase == EventChainResponse.PayloadOneofCase.Error)
        {
            _logger.LogWarning(
                "DataServices reported a stream-level fault on the event chain for session "
                + "{SessionId}: severity {Severity}, retCode {RetCode}.",
                LogSafeText.Render(response.SessionId),
                response.Error?.Severity,
                response.Error?.RetCode);

            return;
        }

        SequencingToken? token = response.Token;
        long observed = token?.Sequence ?? 0L;
        OrderingDiscipline discipline = token?.Discipline ?? OrderingDiscipline.Unspecified;
        long last = _lastSequence;

        // EVERY MESSAGE ON THIS STREAM CARRIES A TOKEN, and a sequence of 0 means "not supplied" -
        // which the contract names a fault outright rather than a tolerable default.
        if (token is null || observed <= 0)
        {
            throw Fault(response.SessionId, observed, last, discipline);
        }

        // AN UNSTATED DISCIPLINE IS A FAULT, AND THIS IS THE DELIBERATE READING.
        //
        // The discipline is carried ON the token, and the stated reason is that a consumer's ordering
        // check must be driven by data it already has rather than by a table it has to keep in step
        // with the event catalogue. An unset discipline therefore leaves the mandated check with
        // nothing to drive it. The alternative - silently skip the check for such a message - was
        // rejected: it would let a producer opt out of ordering enforcement by omission, on the one
        // stream where a missed reordering produces a wrong branch that still looks like a successful
        // conversation. Failing loudly is detection; skipping quietly is concealment.
        if (discipline == OrderingDiscipline.Unspecified)
        {
            throw Fault(response.SessionId, observed, last, discipline);
        }

        // SYNCHRONOUS: strictly increasing, no exceptions. An out-of-order arrival fails the session.
        if (discipline == OrderingDiscipline.Synchronous && observed <= last)
        {
            throw Fault(response.SessionId, observed, last, discipline);
        }

        // SEQUENCED: the token is the CONSUMER's to reason about. Nothing is reordered, nothing is
        // dropped, and a gap is not an error here - it is the very thing the token exists to let the
        // consumer notice. The high-water mark is advanced only forwards so that a legitimately
        // out-of-order sequenced arrival cannot rewind it and mask a later synchronous violation.
        if (observed > last)
        {
            Volatile.Write(ref _lastSequence, observed);
        }
    }

    /// <summary>
    /// Builds the ordering fault, logging it before it is thrown.
    /// </summary>
    /// <param name="sessionId">The session reported on the offending message.</param>
    /// <param name="observed">The sequence that arrived; zero when none was supplied.</param>
    /// <param name="last">The highest sequence seen before it.</param>
    /// <param name="discipline">The discipline the arriving token declared.</param>
    /// <returns>The exception to throw.</returns>
    private DataServicesEventOrderingException Fault(
        string sessionId,
        long observed,
        long last,
        OrderingDiscipline discipline)
    {
        _logger.LogError(
            "Ordering violation on the DataServices event chain for session {SessionId}: sequence "
            + "{ObservedSequence} arrived after {LastObservedSequence} under discipline "
            + "{OrderingDiscipline}. Sequence numbers here are for detection only, so the session is "
            + "failed rather than the delivery reordered.",
            LogSafeText.Render(sessionId),
            observed,
            last,
            discipline);

        return new DataServicesEventOrderingException(sessionId, observed, last, discipline);
    }
}

/// <summary>
/// An open C-03 validation session that closes itself, whatever happens to the caller.
/// </summary>
/// <remarks>
/// <para>
/// WHAT A SESSION IS FOR. The legacy keeps four pieces of mutable state ON THE CONTROL, between events
/// - the disabled-event bitmask, a flag recording that execution is inside the item-change handler,
/// a flag recording that it is inside the validation-error handler, and THE ITEM-CHANGED RETURN VALUE
/// STASHED FOR THE VALIDATION-ERROR EVENT TO CONSUME. A stateless request boundary has nowhere to put
/// them, so they become fields of a server-held session correlated by id, opened and closed by
/// dedicated calls.
/// </para>
/// <para>
/// WHY A SCOPE TYPE EXISTS AT ALL. The state is HELD BY THE SERVER, so a caller that returns early,
/// throws, or is cancelled without closing leaks it. Disposal closes the session on every one of those
/// paths.
/// </para>
/// <para>
/// DISPOSAL DOES NOT OBSERVE THE CALLER'S CANCELLATION TOKEN, and that is the point rather than an
/// oversight: the case that most needs the session closed is the case where the caller's token has
/// just been cancelled. Closure is contractually IDEMPOTENT and answers the same way whether the
/// session was open or already gone, so closing again is always safe.
/// </para>
/// <para>
/// BUT DISPOSAL IS BOUNDED, on a deadline of its own. Not observing the caller's token is not the same
/// as observing nothing: an unbounded close is a close that can hang, and a hang inside
/// <c>await using</c> stalls the request that was trying to unwind - so a mechanism whose whole purpose
/// is to stop a session leaking would instead stop the caller from finishing. The disposal deadline is
/// <see cref="CloseTimeout"/>, and it is a LIVENESS BOUND rather than a latency budget: this repository
/// publishes no service-level agreement, no latency target and no throughput target anywhere, so no
/// performance objective is asserted here or anywhere else. The bound exists so that disposal
/// terminates, and for no other reason.
/// </para>
/// <para>
/// THERE ARE TWO WAYS TO CLOSE, AND THEY DIFFER IN EXACTLY ONE RESPECT - WHO LEARNS ABOUT A FAILURE.
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="CloseAsync"/> is the EXPLICIT close, for a caller finishing normally. It PROPAGATES a
///     failure, because on a normal path there is no primary exception for it to mask and a caller that
///     asked to close is entitled to learn that the close did not happen. A leaked upstream session is
///     not a detail to discover from a log later.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="DisposeAsync"/> is the SAFETY NET, and it does NOT rethrow. Throwing from disposal
///     would replace whatever exception is unwinding through the enclosing block with a less
///     informative one, and the primary fault is what the caller needs. The failure is not lost: it is
///     logged, and it is recorded on <see cref="CloseFailure"/> so it is observable in code rather than
///     only in telemetry.
///     </description>
///   </item>
/// </list>
/// <para>
/// SO A CLOSE THAT DID NOT HAPPEN IS ALWAYS DISTINGUISHABLE FROM ONE THAT DID.
/// <see cref="CloseResult"/> is non-null exactly when the upstream answered, and
/// <see cref="CloseFailure"/> is non-null exactly when the attempt failed; both are null only before
/// any attempt has been made. Reading a null result as "closed cleanly" is therefore impossible,
/// which matters because the whole point of this type is that a session cannot be left behind
/// silently.
/// </para>
/// </remarks>
public sealed class ValidationSessionScope : IAsyncDisposable
{
    /// <summary>
    /// The bound on a close attempt: how long a close is given before it is abandoned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A LIVENESS BOUND, EXPLICITLY NOT A LATENCY BUDGET. This repository publishes no service-level
    /// agreement, no latency target, no throughput target and no availability commitment anywhere, so
    /// this value asserts no performance objective and must not be read as one. It exists so that a
    /// close, and therefore a disposal, is guaranteed to TERMINATE - which is a correctness property
    /// rather than a performance one, because an unbounded disposal stalls the very request that was
    /// unwinding.
    /// </para>
    /// <para>
    /// The magnitude is chosen to be uninteresting in both directions: long enough that no close which
    /// is going to succeed is cut off by it, short enough that a wedged upstream cannot hold a disposing
    /// caller indefinitely. Nothing depends on the exact number, and a caller that needs a different one
    /// calls <see cref="CloseAsync"/> with a token of its own instead.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private readonly DataServicesClient _client;
    private readonly OpenValidationSessionResponse _opened;
    private readonly ILogger _logger;

    private bool _closeAttempted;
    private bool _disposed;

    /// <summary>
    /// Wraps a session that has already been opened. Created only by <see cref="DataServicesClient"/>.
    /// </summary>
    /// <param name="client">The client that opened the session and will close it.</param>
    /// <param name="opened">The open response, carrying the session id and its state as opened.</param>
    /// <param name="logger">The logger. Never receives a token or an authorization header.</param>
    internal ValidationSessionScope(
        DataServicesClient client,
        OpenValidationSessionResponse opened,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _opened = opened;
        _logger = logger;
    }

    /// <summary>
    /// The correlation identifier every event-chain message for this session must carry.
    /// </summary>
    /// <remarks>
    /// Sending an item-change event without it is a DEFINED ERROR at the far end and never an implicit
    /// session creation - an implicit session would give each event its own state, so the stash the
    /// validation-error event reads would always be empty and the chain would take the wrong branch
    /// every time while appearing to work.
    /// </remarks>
    public string SessionId => _opened.SessionId;

    /// <summary>
    /// The session's state as opened: the disabled-event mask, the two re-entrancy guards, the stashed
    /// item-change result, and whether a deferred accept is pending.
    /// </summary>
    public ValidationSessionState State => _opened.State;

    /// <summary>
    /// The outcome code the open call returned, as the wire enum.
    /// </summary>
    public WireRetCode RetCode => _opened.RetCode;

    /// <summary>
    /// The close outcome once a close has completed, or <see langword="null"/> when no close has been
    /// attempted or the attempt failed.
    /// </summary>
    /// <remarks>
    /// Worth reading rather than discarding: the response reports whether the session WAS still open,
    /// which is informational and not an error, and carries the state at the moment of closure - so an
    /// outstanding deferred continuation or a still-set re-entrancy guard is observable instead of
    /// being thrown away.
    /// <para>
    /// Non-null means the upstream answered. It never means "probably fine": a failed attempt leaves
    /// this <see langword="null"/> and sets <see cref="CloseFailure"/>.
    /// </para>
    /// </remarks>
    public CloseValidationSessionResponse? CloseResult { get; private set; }

    /// <summary>
    /// The failure a close attempt ended with, or <see langword="null"/> when no close has been
    /// attempted or the attempt succeeded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This member exists so that a failure swallowed by <see cref="DisposeAsync"/> is still OBSERVABLE
    /// IN CODE. Disposal must not rethrow - it would mask the fault unwinding through the enclosing
    /// block - but "must not rethrow" is not a licence to discard, and a leaked upstream session that
    /// can only be discovered by reading logs is a leak that will not be discovered.
    /// </para>
    /// <para>
    /// After a successful close this is <see langword="null"/>; after a failed one it holds the fault,
    /// which is either the transport status the upstream returned or the cancellation that
    /// <see cref="CloseTimeout"/> produced. Exactly one of this member and
    /// <see cref="CloseResult"/> is non-null once a close has been attempted.
    /// </para>
    /// </remarks>
    public Exception? CloseFailure { get; private set; }

    /// <summary>
    /// Closes the session explicitly, PROPAGATING a failure. Safe to call more than once; a second call
    /// does nothing and returns the first call's result.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancels the close. It is combined with <see cref="CloseTimeout"/>, so the attempt ends at
    /// whichever comes first and it can never be unbounded.
    /// </param>
    /// <returns>
    /// The close outcome: whether the session was still open, and its state at the moment of closure. A
    /// session already closed or expired reports so WITHOUT that being an error.
    /// </returns>
    /// <exception cref="RpcException">The close failed. The session may remain held upstream until it expires.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled, or <see cref="CloseTimeout"/> elapsed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A previous attempt FAILED. The failure is not silently re-attempted, because a caller that
    /// received it has already been told the session may be leaked and a second attempt reporting
    /// success would contradict the first answer; the original fault is the inner exception.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE MEMBER A CALLER FINISHING NORMALLY SHOULD USE, and the difference from disposal is
    /// only who learns about a failure. On a normal path there is no primary exception for a close
    /// failure to mask, so swallowing it would be pure information loss: the caller would proceed
    /// believing the upstream session was released when it was not. Disposal still runs afterwards
    /// through <c>await using</c> and does nothing, because this attempt already happened.
    /// </para>
    /// <para>
    /// RETRY SAFETY: the underlying operation's END STATE is idempotent, so a caller may retry a failed
    /// close - but it must do so by calling the client's own close operation, not by calling this member
    /// again. A scope records one attempt, deliberately: re-attempting from inside it would let a
    /// success overwrite a failure a caller has already acted on. And a retried close answers
    /// <c>was_open = false</c> with an empty <c>final_state</c> whether or not the first attempt had
    /// succeeded, so a caller that retries must treat both of those fields as unknown - which is also why
    /// the operation is not replayed transparently at the transport level
    /// [<c>Clients/OutboundCallPolicy</c>].
    /// </para>
    /// </remarks>
    public async Task<CloseValidationSessionResponse> CloseAsync(CancellationToken cancellationToken)
    {
        if (_closeAttempted)
        {
            return CloseResult ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Closing DataServices validation session '{_opened.SessionId}' has already been "
                    + $"attempted and it failed, so this scope has no close result to return. The "
                    + $"session may remain held by the upstream until it expires. Closure is idempotent "
                    + $"by contract, so a retry is safe - issue it through the client's own close "
                    + $"operation rather than through this scope."),
                CloseFailure);
        }

        _closeAttempted = true;

        // The caller's token AND the liveness bound. Linking rather than choosing is what makes the
        // attempt bounded even when the caller passes a token that is never cancelled.
        using CancellationTokenSource bounded = CreateBoundedSource(cancellationToken);

        try
        {
            CloseResult = await _client
                .CloseValidationSessionAsync(
                    new CloseValidationSessionRequest { SessionId = _opened.SessionId },
                    bounded.Token)
                .ConfigureAwait(false);

            return CloseResult;
        }
        catch (Exception exception) when (exception is RpcException or OperationCanceledException)
        {
            // Recorded before it is rethrown, so the two ways of closing agree on what is observable:
            // whichever path a caller took, CloseFailure holds the fault afterwards.
            CloseFailure = exception;
            throw;
        }
    }

    /// <summary>
    /// Closes the session as a safety net, without rethrowing. Safe to call more than once; the second
    /// call does nothing.
    /// </summary>
    /// <returns>A task that completes when the close has been attempted.</returns>
    /// <remarks>
    /// Bounded by <see cref="CloseTimeout"/> on a token of its own, and deliberately blind to the
    /// caller's token: the case that most needs the session closed is the case where that token has just
    /// been cancelled. A failure is logged and recorded on <see cref="CloseFailure"/> rather than
    /// rethrown, so it cannot displace the fault unwinding through the enclosing block.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // An explicit close already happened - including one that failed and was reported to the
        // caller. Re-attempting would either duplicate work or turn a failure the caller has acted on
        // into a success, so disposal defers to whatever the explicit attempt recorded.
        if (_closeAttempted)
        {
            return;
        }

        _closeAttempted = true;

        // A DEADLINE OF ITS OWN, NOT CancellationToken.None. Not observing the caller's token is
        // correct; observing NOTHING is not, because an unbounded close inside `await using` stalls the
        // request that was trying to unwind - so the mechanism that exists to stop a session leaking
        // would instead stop the caller finishing.
        using CancellationTokenSource bounded = new(CloseTimeout);

        try
        {
            CloseResult = await _client
                .CloseValidationSessionAsync(
                    new CloseValidationSessionRequest { SessionId = _opened.SessionId },
                    bounded.Token)
                .ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            CloseFailure = exception;

            // THE EXCEPTION IS RETAINED ON THE SCOPE AND DESCRIBED ON THE RECORD, which are different
            // trust domains. CloseFailure hands the whole fault to the enclosing code, which is inside this
            // process and entitled to it; the record is retained and shipped, and an RpcException's message
            // is the UPSTREAM's own status detail - another party's text, arriving on the one path where
            // that party was already failing. The status code is published in the clear because it is a
            // fixed enumeration that cannot carry content.
            _logger.LogWarning(
                "Closing DataServices validation session {SessionId} failed with status {StatusCode}. "
                + "The session may remain held by the upstream until it expires. This failure is not "
                + "rethrown, so that it cannot mask whichever fault is unwinding through the enclosing "
                + "scope; it is recorded on the scope's CloseFailure member instead. "
                + "FaultTypes={FaultTypes}",
                LogSafeText.Render(_opened.SessionId),
                exception.StatusCode,
                ExceptionChain.DescribeTypes(exception));
        }
        catch (OperationCanceledException exception)
        {
            CloseFailure = exception;

            // Two causes, and the record does not pretend to distinguish them because the consequence
            // is identical: the disposal deadline elapsed, or the underlying call was cancelled by the
            // channel or by the host shutting down.
            _logger.LogWarning(
                "Closing DataServices validation session {SessionId} did not complete within the "
                + "{CloseTimeoutSeconds}s disposal bound, or was cancelled by the channel or a host "
                + "shutdown. The session may remain held by the upstream until it expires. This failure "
                + "is not rethrown; it is recorded on the scope's CloseFailure member instead. "
                + "FaultTypes={FaultTypes}",
                LogSafeText.Render(_opened.SessionId),
                CloseTimeout.TotalSeconds,
                ExceptionChain.DescribeTypes(exception));
        }
    }

    /// <summary>
    /// Links a caller's token to the liveness bound, so an attempt ends at whichever comes first.
    /// </summary>
    /// <param name="cancellationToken">The caller's token, which may never be cancelled.</param>
    /// <returns>The linked source. The caller disposes it.</returns>
    private static CancellationTokenSource CreateBoundedSource(CancellationToken cancellationToken)
    {
        CancellationTokenSource bounded =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        bounded.CancelAfter(CloseTimeout);

        return bounded;
    }
}

/// <summary>
/// Gateway's typed client for the DataServices upstream: contract C-03
/// (<c>dataservices.v1.DataWindowService</c>) and contract C-04
/// (<c>dataservices.v1.ColumnExpressionService</c>).
/// </summary>
/// <remarks>
/// <para>
/// A TRANSPORT WRAPPER AND NOTHING MORE. Every member does exactly three things: obtain a Security
/// issued bearer credential, invoke the generated stub, and hand the contract's own message back
/// unaltered. There is no caching, no response reshaping, no convenience aggregation, no client-side
/// reordering, no de-duplication and no normalization anywhere in it. Two members translate a failure
/// rather than a success - <see cref="UpdateAsync"/> raises
/// <see cref="DataServicesConflictException"/> so the conflict payload survives, and
/// <see cref="DataWindowEventChannel"/> raises <see cref="DataServicesEventOrderingException"/> so an
/// ordering fault is reported instead of hidden - and both are documented at the point of translation.
/// </para>
/// <para>
/// WHAT PROGRAM.CS OWNS, NOT THIS TYPE. Channel creation, the upstream address
/// (<c>Gateway:Upstreams:DataServices</c>), and the resilience pipeline are all registered in the
/// composition root, which is why the two generated stubs arrive here BY CONSTRUCTOR INJECTION. That
/// is also the substitution seam: both generated client types declare every method <c>virtual</c> and
/// expose a protected parameterless constructor expressly so a test double can be derived, so a host
/// test can exercise this whole outbound path with NO DataServices instance running anywhere. Nothing
/// in this type performs I/O during construction, in a static initializer, in a hosted service or in
/// any eager warm-up, so starting the host never requires the upstream to be reachable and
/// <c>dotnet test</c> never depends on one.
/// </para>
/// <para>
/// AUTHENTICATION IS ATTACHED TO EVERY OUTBOUND CALL, INTERNAL EDGES INCLUDED. The credential comes
/// exclusively from <see cref="IServiceTokenProvider"/>, whose only implementation is the sibling
/// Security client. This type mints nothing, signs nothing, holds no signing key, constructs no token
/// handler and performs no key or discovery retrieval; the sole issuer in the system is Security, and
/// inbound validation belongs to the stock bearer handler in the composition root. The credential is
/// never logged, and neither is the header that carries it.
/// </para>
/// <para>
/// CANCELLATION IS THREADED END TO END. Every public member takes a
/// <see cref="CancellationToken"/> and passes it into the call and into every stream read and write.
/// This is the target-side expression of the legacy's cross-thread proxy pattern, in which every
/// concurrency class existed twice - a caller-side proxy and a worker-side implementation - so that no
/// object was ever touched from two threads. Those thread-affinity annotations are a CONTRACT rather
/// than an implementation detail, so the duality is not flattened away by hiding cancellation behind a
/// fire-and-forget call.
/// </para>
/// <para>
/// RETRY SAFETY IS DOCUMENTED PER MEMBER, and the rules are narrow: transient TRANSPORT faults only;
/// never <see cref="StatusCode.Aborted"/>, which is a definitive concurrency answer; never
/// <see cref="UpdateAsync"/>, which is not idempotent; and never a stream mid-flight, because
/// replaying delivered events into an ordered chain corrupts the ordering the chain exists to
/// preserve.
/// </para>
/// </remarks>
public sealed class DataServicesClient
{
    /// <summary>
    /// The gRPC metadata key carrying the bearer credential. Lower-case because gRPC metadata keys are
    /// case-insensitive by specification and are normalised to lower case on the wire.
    /// </summary>
    private const string AuthorizationHeaderName = "authorization";

    /// <summary>
    /// The identity Gateway claims when it asks Security for a service token.
    /// </summary>
    /// <remarks>
    /// A NON-SECRET PROTOCOL IDENTIFIER, NOT A CREDENTIAL. It is a name, it authenticates nothing on
    /// its own, and it is deliberately not configurable here: the identity actually honoured is the one
    /// the transport establishes when Security issues the token, and a mismatch between the two is
    /// refused by Security. Everything genuinely sensitive - key material, the upstream addresses -
    /// binds from environment configuration in the composition root and never appears in this file.
    /// </remarks>
    private const string TokenSubject = "powerframework-gateway";

    /// <summary>
    /// The single audience Gateway requests a token for when calling DataServices.
    /// </summary>
    /// <remarks>
    /// Follows the audience naming convention already fixed by the service's own inbound
    /// configuration, so DataServices' audience is the service name under the same prefix. The token
    /// contract carries ONE audience per request, deliberately, so a credential is never valid
    /// somewhere its holder did not intend it to be.
    /// </remarks>
    private const string DataServicesAudience = "powerframework-dataservices";

    /// <summary>The scope requested for the C-03 DataWindow surface.</summary>
    private const string DataWindowScope = "dataservices.datawindow";

    /// <summary>The scope requested for the C-04 column-expression surface.</summary>
    private const string ColumnExpressionScope = "dataservices.columnexpression";

    /// <summary>
    /// The C-03 method whose rich-error binding declares how a conflict payload is carried.
    /// </summary>
    private const string UpdateMethodName = "Update";

    /// <summary>
    /// The token request Gateway makes for calls on the C-03 DataWindow surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE SCOPE PER CREDENTIAL, AND THE SPLIT IS THE POINT. A single request carrying BOTH scopes used
    /// to serve every call this client makes, so the credential attached to a column-expression call also
    /// authorized the DataWindow surface and vice versa. That is a least-privilege failure with a real
    /// consequence rather than a theoretical one: a token captured from any single call - a log that
    /// recorded a header, a compromised upstream, an operator's packet capture - carried the whole of
    /// this client's authority rather than the authority of the call it was taken from. The two surfaces
    /// are separate contracts precisely so they can version and be authorized independently (AAP 0.4.3,
    /// C-04 "kept separate from C-03 so the expansion engine can version independently").
    /// </para>
    /// <para>
    /// TWO REQUESTS COST NOTHING EXTRA IN PRACTICE. The token provider's cache key is built from the
    /// subject, the audience and the SORTED SCOPE SET, so these two requests occupy distinct cache
    /// entries and each is minted once per lifetime rather than once per call. The steady state is two
    /// held credentials instead of one, each narrower than the one it replaced.
    /// </para>
    /// <para>
    /// Built once because <see cref="ServiceTokenRequest"/> validates on construction and copies its
    /// scope set defensively, which makes the instance immutable and safe to share. The GRANTED set may
    /// still be narrower than the requested one, and a narrowing is a SUCCESSFUL outcome rather than a
    /// failure, so it is read and reported and never asserted on.
    /// </para>
    /// </remarks>
    private static readonly ServiceTokenRequest DataWindowTokenRequest =
        new(TokenSubject, DataServicesAudience, [DataWindowScope]);

    /// <summary>
    /// The token request Gateway makes for calls on the C-04 column-expression surface.
    /// </summary>
    /// <remarks>
    /// The sibling of <see cref="DataWindowTokenRequest"/>; the reasoning for the split is recorded
    /// there.
    /// </remarks>
    private static readonly ServiceTokenRequest ColumnExpressionTokenRequest =
        new(TokenSubject, DataServicesAudience, [ColumnExpressionScope]);

    /// <summary>
    /// The rich-error binding declared on C-03's <c>Update</c>, read from the generated descriptor.
    /// </summary>
    /// <remarks>
    /// READ FROM THE DESCRIPTOR RATHER THAN HARDCODED, deliberately. A gRPC status carries only a code
    /// and a message, so the conflict detail travels as a versioned BINARY TRAILER, and the contract
    /// declares the trailer key, the accompanying status code and the payload type as a custom method
    /// option precisely so a client discovers all three from the descriptor instead of from a comment.
    /// Taking them from here means a future contract revision that renames the key or moves the status
    /// is picked up automatically, and it means no string in this file has to be kept in step with the
    /// protocol definition by hand.
    /// <para>
    /// This is local reflection over generated metadata, not I/O, so evaluating it in a static
    /// initializer keeps startup free of any dependency on the upstream being reachable. Both lookups
    /// return <see langword="null"/> rather than throwing when absent, so type initialization cannot
    /// fail here; an absent binding is handled where it is used.
    /// </para>
    /// </remarks>
    private static readonly RichErrorBinding? UpdateRichErrorBinding = ReadUpdateRichErrorBinding();

    /// <summary>
    /// The fully-qualified name of the rich-error payload type this client can decode.
    /// </summary>
    /// <remarks>
    /// Taken from the generated descriptor rather than written as a literal, and hoisted into a field so
    /// that comparing and reporting it costs a field read rather than a property walk at every use.
    /// </remarks>
    private static readonly string SupportedRichErrorPayloadType = RichErrorTrailer.Descriptor.FullName;

    private readonly DataWindowService.DataWindowServiceClient _dataWindow;
    private readonly ColumnExpressionService.ColumnExpressionServiceClient _columnExpression;
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly OutboundDeadlines _deadlines;
    private readonly ILogger<DataServicesClient> _logger;

    /// <summary>
    /// Creates the client over already-configured generated stubs.
    /// </summary>
    /// <param name="dataWindowClient">
    /// The generated C-03 stub. Injected rather than constructed so the composition root keeps
    /// ownership of the channel, the address and the resilience pipeline, and so a test can substitute
    /// a derived double.
    /// </param>
    /// <param name="columnExpressionClient">The generated C-04 stub, on the same terms.</param>
    /// <param name="tokenProvider">
    /// Supplies the bearer credential for every outbound call. The sibling Security client is its
    /// implementation; token acquisition is never duplicated here.
    /// </param>
    /// <param name="logger">The logger. Never receives a credential, a header or a statement text.</param>
    /// <param name="deadlines">
    /// The bounds every outbound call carries, supplied by the composition root from
    /// <c>Gateway:Outbound</c>. Omitting it selects <see cref="OutboundDeadlines.Default"/>, so a
    /// hand-constructed client still bounds its calls - "no deadline" is not a reachable state.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// PERFORMS NO I/O. Nothing here connects, resolves, probes or warms anything up, which is what
    /// lets the host start with the upstream absent.
    /// </remarks>
    public DataServicesClient(
        DataWindowService.DataWindowServiceClient dataWindowClient,
        ColumnExpressionService.ColumnExpressionServiceClient columnExpressionClient,
        IServiceTokenProvider tokenProvider,
        ILogger<DataServicesClient> logger,
        OutboundDeadlines? deadlines = null)
    {
        ArgumentNullException.ThrowIfNull(dataWindowClient);
        ArgumentNullException.ThrowIfNull(columnExpressionClient);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _dataWindow = dataWindowClient;
        _columnExpression = columnExpressionClient;
        _tokenProvider = tokenProvider;
        _deadlines = deadlines ?? OutboundDeadlines.Default;
        _logger = logger;
    }

    // ==============================================================================================
    //  CONTRACT C-03 - dataservices.v1.DataWindowService
    //  ----------------------------------------------------------------------------------------------
    //  Sixteen operations: the retrieval/validation/update triple, the paired validation session, the
    //  bidirectional event chain, the event gate, and the four headless models as a read and an apply
    //  each.
    // ==============================================================================================

    /// <summary>
    /// Streams a retrieval as buffer-shaped chunks, reproducing the legacy's progressive delivery.
    /// </summary>
    /// <param name="request">
    /// The DataWindow handle, the ONE-BASED positional retrieval arguments, which buffers to stream,
    /// and the requested rows per chunk (zero lets the server choose).
    /// </param>
    /// <param name="cancellationToken">Cancels the retrieval and tears the stream down.</param>
    /// <returns>
    /// The chunks in arrival order, each yielded as received. Every chunk carries its own row count,
    /// its ONE-BASED ordinal so a gap is detectable, the cumulative count, and a final marker - a
    /// stream that ends without that marker was truncated. A chunk whose buffer field is ABSENT is a
    /// mixed-buffer chunk, in which case each row's own buffer tag is the authority.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// GENUINELY STREAMED, NOT MATERIALISED. The chunks are yielded one at a time as they arrive; they
    /// are never collected into a list and returned in one piece. Collapsing the stream would discard
    /// the progressive-delivery behaviour this operation exists to carry - the legacy raises a data
    /// received event per delivery with that delivery's own row count, and a consumer is entitled to
    /// observe the same shape.
    /// </para>
    /// <para>
    /// RETRY SAFETY: the operation is read-only, so re-invoking it from the start is safe, but it must
    /// never be retried MID-STREAM - resuming a partly consumed stream would deliver a chunk sequence
    /// that never existed.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<RetrieveChunk> RetrieveAsync(
        RetrieveRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.DataWindow,
                OutboundCallClass.Stream,
                cancellationToken)
            .ConfigureAwait(false);

        using AsyncServerStreamingCall<RetrieveChunk> call = _dataWindow.Retrieve(request, options);

        await foreach (RetrieveChunk chunk in call.ResponseStream
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// Opens a validation session, materializing the four pieces of cross-event state the legacy keeps
    /// on the control.
    /// </summary>
    /// <param name="request">The DataWindow handle and, optionally, an initial disabled-event mask.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The session id, the state as opened, and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// PREFER <see cref="OpenValidationSessionScopeAsync"/>. The state is held by the SERVER, so a
    /// caller of this overload owns closing it on every path including exceptions and cancellation, and
    /// forgetting to leaks session state upstream. This overload exists for callers that genuinely need
    /// the session to outlive the scope in which it was opened.
    /// <para>RETRY SAFETY: safe to retry, but each successful attempt opens a SEPARATE session that must be closed.</para>
    /// </remarks>
    public Task<OpenValidationSessionResponse> OpenValidationSessionAsync(
        OpenValidationSessionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.OpenValidationSessionAsync, request, cancellationToken);

    /// <summary>
    /// Opens a validation session inside a scope that closes it on every exit path.
    /// </summary>
    /// <param name="request">The DataWindow handle and, optionally, an initial disabled-event mask.</param>
    /// <param name="cancellationToken">Cancels the OPEN call only; closure deliberately does not observe it.</param>
    /// <returns>
    /// A scope exposing the session id and the state as opened. Disposing it closes the session,
    /// including when the enclosing block is unwinding because of an exception or a cancellation.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The open call failed; no session was created, so none leaks.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THIS IS THE MEMBER TO USE. Closure is contractually idempotent, so the scope can close a session
    /// a caller already closed explicitly without that being an error.
    /// <para>RETRY SAFETY: as <see cref="OpenValidationSessionAsync"/> - safe, but each success is a separate session.</para>
    /// </remarks>
    public async Task<ValidationSessionScope> OpenValidationSessionScopeAsync(
        OpenValidationSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        OpenValidationSessionResponse opened = await OpenValidationSessionAsync(request, cancellationToken)
            .ConfigureAwait(false);

        return new ValidationSessionScope(this, opened, _logger);
    }

    /// <summary>
    /// Closes a validation session.
    /// </summary>
    /// <param name="request">The session id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The outcome, whether the session was still open, and its state at the moment of closure. A
    /// session that had already been closed or had expired reports so WITHOUT that being an error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE TO REPLAY TRANSPARENTLY, even though the END STATE is idempotent.</b>
    /// The two are different properties and only the second held. This response carries
    /// <c>final_state</c>, captured from the session at the moment it is removed, so a first attempt that
    /// SUCCEEDED and whose response was lost is followed by a replay that finds nothing and answers
    /// <c>OK / was_open = false</c> with an EMPTY final state - a response indistinguishable from "there
    /// was never such a session", in which the outstanding continuation or set re-entrancy guard the real
    /// answer reported has silently disappeared. The operation is therefore absent from the replay-safe
    /// roster [<c>Clients/OutboundCallPolicy</c>], and a lost close response surfaces as the transport
    /// fault it is. A CALLER may still choose to close again - the end state genuinely is idempotent, and
    /// the idle sweeper releases an unclosed session regardless - but it must then read <c>was_open</c> and
    /// <c>final_state</c> as unknown rather than as answers.
    /// </remarks>
    public Task<CloseValidationSessionResponse> CloseValidationSessionAsync(
        CloseValidationSessionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.CloseValidationSessionAsync, request, cancellationToken);

    /// <summary>
    /// Opens the bidirectional event chain: all 13 raw and all 9 semantic events in one ordered
    /// conversation.
    /// </summary>
    /// <param name="cancellationToken">Cancels the conversation and tears both halves down.</param>
    /// <returns>
    /// The channel. Send notifications and handler answers on it, read outcomes, invocations and
    /// stream-level faults from it, and dispose it when the conversation is over.
    /// </returns>
    /// <exception cref="RpcException">The call could not be established.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// The operation takes no request message because the session id travels on EVERY message: one
    /// channel can therefore carry one session's synchronous group, and an event that needs a session
    /// but names none is a defined error at the far end rather than an implicit session creation.
    /// </para>
    /// <para>
    /// This member has NO REST projection at Gateway's ingress, and that is deliberate: flattening a
    /// strictly ordered, veto-carrying conversation into independent HTTP requests would lose both the
    /// ordering and the typed veto.
    /// </para>
    /// <para>RETRY SAFETY: NOT SAFE once messages have flowed - re-establishing the stream would replay delivered events into an ordered chain.</para>
    /// </remarks>
    public async Task<DataWindowEventChannel> OpenEventChainAsync(CancellationToken cancellationToken)
    {
        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.DataWindow,
                OutboundCallClass.Stream,
                cancellationToken)
            .ConfigureAwait(false);

        return new DataWindowEventChannel(_dataWindow.EventChain(options), _logger);
    }

    /// <summary>
    /// Submits a changeset, and surfaces an optimistic-concurrency conflict with its detail intact.
    /// </summary>
    /// <param name="request">
    /// The DataWindow handle, the session if the update is issued inside a validation chain, and the
    /// rows. EVERY ROW MUST CARRY BOTH ITS CURRENT AND ITS ORIGINAL VALUES for each marked column,
    /// because the concurrency check compares the key column plus the original values of every
    /// updateable column.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The outcome code, the inserted, updated and deleted counts, a non-conflict database failure if
    /// there was one, and the identity block when identities were collected.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="DataServicesConflictException">
    /// The upstream answered <see cref="StatusCode.Aborted"/> and the conflict detail was decoded. The
    /// exception carries the <see cref="ConflictDetail"/> OBJECT so the REST projection can serialize
    /// the same payload as an HTTP 409 body.
    /// </exception>
    /// <exception cref="RpcException">
    /// Any other failure, rethrown UNTOUCHED with its status and trailers preserved - including an
    /// <see cref="StatusCode.Aborted"/> whose detail could not be decoded, so that even then the
    /// payload is not lost.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE UPDATE FLAGS ARE NOT PARAMETERS, and their absence is the contract rather than an omission.
    /// The legacy calls its update with accept-text true and reset-flag false as literals, on every
    /// update it performs, with no argument reaching them from any caller - so the server applies that
    /// pair unconditionally and the CALLER OWNS THE BUFFER STATE AFTERWARDS. The request's slots for
    /// them are reserved by number and by name, which is enforcement rather than advice: an omitted
    /// accept-text would have defaulted to false and silently submitted stale edit text, and a reset
    /// would have discarded the original-value shadow that optimistic concurrency is built on.
    /// </para>
    /// <para>
    /// EVERYTHING IS RELAYED UNSMOOTHED. The counts, the identity column and BOTH identity value
    /// arrays pass through exactly as received: the primary array is in FORWARD collection order and
    /// the filter array in BACKWARD order, because the filter buffer's row order is inverted relative
    /// to the source. Re-sorting either array, de-duplicating it or concatenating the two would pair
    /// values with the wrong rows - a defect that returns the right COUNT of identities and therefore
    /// survives every row-count assertion. An absent identity block means none were collected and is
    /// not an error.
    /// </para>
    /// <para>
    /// RETRY SAFETY: NOT SAFE. This operation is not idempotent, and a conflict is a definitive answer
    /// rather than a transient fault - retrying it automatically is the silent-overwrite failure mode
    /// this system forbids. The caller re-reads and rebases, or surfaces the conflict.
    /// </para>
    /// </remarks>
    public async Task<UpdateResponse> UpdateAsync(
        UpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.DataWindow,
                OutboundCallClass.Unary,
                cancellationToken)
            .ConfigureAwait(false);

        UpdateResponse response;
        try
        {
            response = await _dataWindow.UpdateAsync(request, options).ConfigureAwait(false);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted)
        {
            ConflictDetail? conflict = TryReadConflictDetail(exception, out long conflictRetCode);
            if (conflict is null)
            {
                // THE PAYLOAD IS STILL NOT LOST. Rethrowing preserves the status and the complete
                // trailer set, so a caller can decode whatever did arrive. What is NOT done here is
                // inventing a detail-less conflict, which would report a conflict a caller cannot act
                // on, or swallowing the status, which would let the update look successful.
                _logger.LogWarning(
                    "DataServices answered Aborted for an update on DataWindow {DataWindowHandle} but "
                    + "no decodable conflict detail accompanied it. The status and trailers are "
                    + "rethrown unchanged rather than being reported as a conflict without its detail.",
                    LogSafeText.Render(request.DatawindowHandle));

                throw;
            }

            _logger.LogWarning(
                "Optimistic-concurrency conflict on DataWindow {DataWindowHandle}: {ConflictRowCount} "
                + "row(s) on update table {UpdateTable}; {RowsExpected} row(s) expected, "
                + "{RowsMatched} matched; retCode {RetCode}. The conflict is surfaced with its detail "
                + "so the caller can re-read and rebase or surface it; it is never retried and never "
                + "overwritten.",
                LogSafeText.Render(request.DatawindowHandle),
                conflict.Rows.Count,
                conflict.UpdateTable,
                conflict.RowsExpected,
                conflict.RowsMatched,
                conflictRetCode);

            throw new DataServicesConflictException(conflict, conflictRetCode, exception);
        }

        // Guarded because both of the derived arguments below are computed purely FOR the log record and
        // have no other purpose: the classification does not affect control flow, and the identity block
        // count is a diagnostic. Computing them when nothing will read them would be work done for no
        // observable reason.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            // Classified with the PORTED tri-state algebra rather than an ad-hoc comparison written
            // here. The wire code is widened to long explicitly because the two RetCode families agree
            // by review-time invariant and not by any compile-time edge - the contracts project holds
            // no reference to the kernel - so the conversion is stated rather than assumed.
            bool succeeded = Predicates.IsSucceeded((long)response.RetCode);

            // THE IDENTITY DIAGNOSTIC IS A COUNT, NOT A PRESENCE TEST, because the field is REPEATED:
            // the reply carries ONE ORDERED BLOCK PER UPDATE TABLE, mirroring the legacy caller-side
            // proxy that appends a block per table and replays every one of them
            // [n_cst_threading_task_sqlupdate.sru:L73-L76, L142-L195]. A presence test would report
            // `true` unconditionally - protoc initialises a repeated field to an empty collection that
            // is never null - so it would have stopped diagnosing anything the moment the contract
            // became repeated. The count is the fact worth recording: zero means no block was
            // collected, and a count that disagrees with the number of update tables prepared is the
            // shape of a relay that dropped one.
            _logger.LogDebug(
                "Update on DataWindow {DataWindowHandle} returned retCode {RetCode} (succeeded: "
                + "{Succeeded}); {RowsInserted} inserted, {RowsUpdated} updated, {RowsDeleted} deleted; "
                + "{IdentityBlockCount} identity block(s), one per update table.",
                LogSafeText.Render(request.DatawindowHandle),
                response.RetCode,
                succeeded,
                response.RowsInserted,
                response.RowsUpdated,
                response.RowsDeleted,
                response.Identity.Count);
        }

        if (response.Error is not null)
        {
            // REDACTION RULE: the database error's statement field carries the complete generated
            // statement including interpolated literal values, and the legacy logger performed no
            // redaction at all. It is therefore NEVER logged here, NEVER echoed, and never
            // reconstructed from its parts for a message. The error text is withheld on the same
            // grounds, since it can quote the offending literal back. Only the numeric code and the
            // offending position are recorded; the whole payload remains on the response for a caller
            // that is entitled to it.
            _logger.LogWarning(
                "Update on DataWindow {DataWindowHandle} reported a non-conflict database failure: "
                + "sqldbcode {SqlDbCode}, buffer {Buffer}, row {Row}. The statement text is redacted "
                + "and is not logged.",
                LogSafeText.Render(request.DatawindowHandle),
                response.Error.Sqldbcode,
                response.Error.Buffer,
                response.Error.Row);
        }

        return response;
    }

    /// <summary>
    /// Reads the event-gate bitmask and the per-bit predicate results.
    /// </summary>
    /// <param name="request">The session id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The mask, the list of disabled bits, and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The disabled-bit LIST is carried alongside the mask rather than being left for the caller to
    /// derive, because the legacy exposes a per-bit PREDICATE and a bit test over a multi-bit value is
    /// not the same question as "are all of these bits set".
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetEventGateResponse> GetEventGateAsync(
        GetEventGateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.GetEventGateAsync, request, cancellationToken);

    /// <summary>
    /// Disables one or more events by setting their bits in the gate mask.
    /// </summary>
    /// <param name="request">The session id and a bit-or of gate bits. A mask of zero is rejected.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and the mask after the bit-or.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// DISABLING THE ITEM-CHANGE BIT ALSO SUPPRESSES COLUMN-EXPRESSION CALCULATION. That coupling is
    /// legacy behaviour, recorded in the oracle's own inline comment beside the constant, and it is
    /// PRESERVED rather than corrected - which is exactly why it is stated here instead of being left
    /// to be discovered. A caller that disables item change to quieten a notification will also,
    /// silently, stop every bound column expression from recalculating, and will then read stale
    /// computed values with no error reported anywhere. This is the mechanism by which a C-03 gate call
    /// has an observable effect on C-04.
    /// </para>
    /// <para>
    /// A ZERO MASK IS REJECTED with an invalid-argument outcome, reproducing the legacy guard exactly.
    /// </para>
    /// <para>RETRY SAFETY: SAFE - setting a bit that is already set is idempotent.</para>
    /// </remarks>
    public Task<DisableEventResponse> DisableEventAsync(
        DisableEventRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.DisableEventAsync, request, cancellationToken);

    /// <summary>
    /// Re-enables one or more events by clearing their bits in the gate mask.
    /// </summary>
    /// <param name="request">The session id and a bit-or of gate bits. A mask of zero is rejected.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and the mask after the bit-clear.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The legacy declares this mutator with a NARROWER return type than its disable counterpart - a
    /// 16-bit integer against a 32-bit long - for no reason its source gives, although the two bodies
    /// are otherwise mirror images. The inconsistency is annotated rather than harmonised, and the wire
    /// is not made lossy by it because every outcome value fits both widths. Clearing the item-change
    /// bit restores column-expression calculation, which is the other half of the coupling documented
    /// on <see cref="DisableEventAsync"/>.
    /// <para>RETRY SAFETY: SAFE - clearing a bit that is already clear is idempotent.</para>
    /// </remarks>
    public Task<EnableEventResponse> EnableEventAsync(
        EnableEventRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.EnableEventAsync, request, cancellationToken);

    // ---- The four headless models. Read and apply, and headless WITHOUT EXCEPTION: no geometry, no
    // ---- DPI conversion, no font metric, no pixel coordinate, no window handle, no input-method
    // ---- state. The rendering half of each is deferred and is reached, eventually, through Gateway's
    // ---- reserved design route - which has nothing behind it in this phase.

    /// <summary>
    /// Reads the drop-down search state for a column: which filter types apply, whether filtered rows
    /// are shown, the CONSTRUCTED FILTER EXPRESSION, the search terms, and the row and filtered counts.
    /// </summary>
    /// <param name="request">The DataWindow handle and the column name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The state and the outcome code, plus a structured error where the legacy raised a dialog.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The filter expression is COMPATIBILITY AND DIAGNOSTIC data - it is what the oracle generates,
    /// carried so a recording can be compared against it. It is never something a receiver executes.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetDropDownSearchStateResponse> GetDropDownSearchStateAsync(
        GetDropDownSearchStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.GetDropDownSearchStateAsync, request, cancellationToken);

    /// <summary>
    /// Applies or clears a drop-down search filter.
    /// </summary>
    /// <param name="request">
    /// The DataWindow handle, the column, the search terms AS TYPED, and the optional filter-type,
    /// pinyin-flag and show-filtered-rows settings.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The resulting state and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The terms travel AS TYPED and the legacy's own transformations happen server-side, so the
    /// generated expression matches the oracle byte for byte without a caller having to build syntax
    /// itself - and so this client never rewrites a search term on its way past.
    /// <para>RETRY SAFETY: SAFE - applying the same filter twice yields the same state.</para>
    /// </remarks>
    public Task<ApplyDropDownSearchResponse> ApplyDropDownSearchAsync(
        ApplyDropDownSearchRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.ApplyDropDownSearchAsync, request, cancellationToken);

    /// <summary>
    /// Reads the column-sort state and the generated sort-property string.
    /// </summary>
    /// <param name="request">The DataWindow handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The state and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Sort expression and sort state only. The sort-indicator geometry is the deferred half.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetColumnSortStateResponse> GetColumnSortStateAsync(
        GetColumnSortStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.GetColumnSortStateAsync, request, cancellationToken);

    /// <summary>
    /// Applies an ordered multi-column sort.
    /// </summary>
    /// <param name="request">
    /// The DataWindow handle and the columns. COLUMN ORDER IS THE SORT ORDER, so the sequence is
    /// significant and is transmitted exactly as supplied.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The resulting state and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - applying the same sort twice yields the same state.</remarks>
    public Task<ApplyColumnSortResponse> ApplyColumnSortAsync(
        ApplyColumnSortRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.ApplyColumnSortAsync, request, cancellationToken);

    /// <summary>
    /// Reads the context-menu ITEM MODEL for a row and column: labels, identifiers, enabled and split
    /// flags, tip text, image names, separators, submenu items and the COMPUTED LOGICAL TEXT WIDTHS.
    /// </summary>
    /// <param name="request">The DataWindow handle, the row, and the DataWindow object reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The model and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The widths are LOGICAL, computed from the text. The pixel widths, the fonts, the menu geometry
    /// and the rendering are the deferred half, and no field of this contract carries any of them.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetContextMenuModelResponse> GetContextMenuModelAsync(
        GetContextMenuModelRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.GetContextMenuModelAsync, request, cancellationToken);

    /// <summary>
    /// Applies a context-menu item model: items, separators and the five built-in toggles.
    /// </summary>
    /// <param name="request">The DataWindow handle, the model, and whether it replaces or extends.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The resulting model and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Each of the five built-in toggles defaults to TRUE in the legacy and is therefore carried with
    /// EXPLICIT PRESENCE on the wire, so "leave the legacy default alone" stays distinguishable from
    /// "switch it off". This client sets none of them and defaults none of them.
    /// <para>RETRY SAFETY: depends on the request - a replacing apply is idempotent, an extending one is not.</para>
    /// </remarks>
    public Task<ApplyContextMenuModelResponse> ApplyContextMenuModelAsync(
        ApplyContextMenuModelRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.ApplyContextMenuModelAsync, request, cancellationToken);

    /// <summary>
    /// Reads the row-selection state: the style, the selected rows, the current row, and the two
    /// in-flight flags.
    /// </summary>
    /// <param name="request">The DataWindow handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The state and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// This service is headless IN ITS ENTIRETY, unlike the other three: its single legacy dialog is a
    /// structured error on the wire, and it has no presentational half at all.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetRowSelectStateResponse> GetRowSelectStateAsync(
        GetRowSelectStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.GetRowSelectStateAsync, request, cancellationToken);

    /// <summary>
    /// Applies a row-selection style and the selection changes that follow from it.
    /// </summary>
    /// <param name="request">The DataWindow handle, the optional style, the rows to select, and the row to make current.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The resulting state and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Three legacy behaviours are reportable through the response and are preserved: an unchanged
    /// style returns success, a style of zero is an invalid argument, and a successful change CLEARS
    /// the selection and then re-selects the current row unless the style is exactly the multiple-select
    /// one.
    /// <para>RETRY SAFETY: SAFE - re-applying the same style and selection yields the same state.</para>
    /// </remarks>
    public Task<ApplyRowSelectStyleResponse> ApplyRowSelectStyleAsync(
        ApplyRowSelectStyleRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.DataWindow,
            _dataWindow.ApplyRowSelectStyleAsync, request, cancellationToken);

    // ==============================================================================================
    //  CONTRACT C-04 - dataservices.v1.ColumnExpressionService
    //  ----------------------------------------------------------------------------------------------
    //  A SEPARATE SERVICE FROM C-03, SO THE EXPANSION ENGINE CAN VERSION INDEPENDENTLY of the event
    //  chain. The two are wrapped by one client type because Gateway consumes both from the same
    //  endpoints, and wrapping them together couples nothing on the wire: they remain two service
    //  definitions with two generated stubs and two independent version histories.
    //
    //  THE THREE THINGS THIS GROUP MUST NOT DO TO A PAYLOAD, because doing any of them destroys the
    //  semantics the contract exists to carry across the boundary:
    //
    //    1. NEVER TOUCH AN EXPANSION MODE. Every variable reference on the wire is tagged static,
    //       dynamic, dynamic-indirect, macro-direct or macro-dynamic. A static reference is resolved
    //       and substituted INTO THE STORED EXPRESSION AT BIND TIME, so the variable name is gone and
    //       no later assignment can reach it; a dynamic reference is retained as an index into the live
    //       variable table, so a later assignment propagates. The legacy's own worked example fixes a
    //       static result permanently while the identical sequence with a dynamic reference yields the
    //       updated value. The mode is a PER-REFERENCE property - one expression may mix both - so it
    //       is never normalized, collapsed, defaulted or inferred here.
    //    2. NEVER SIMPLIFY THE THREE-PART BINDING. The payload carries the UNEXPANDED expression text
    //       AND the bind-time snapshot AND the live environment, all three, because that is the only
    //       way the distinction above survives serialization: an already-expanded string makes static
    //       bindings indistinguishable from literals and strips dynamic ones of their resolution
    //       environment.
    //    3. NEVER FLATTEN THE TYPED VARIABLE ENVIRONMENT TO STRINGS. A variable value is a seven-arm
    //       discriminated union - time, string, long, double, datetime, date, boolean - and the
    //       coercion the engine performs depends on which arm it was.
    // ==============================================================================================

    /// <summary>
    /// Opens an expression session over one or more co-resident DataWindow handles.
    /// </summary>
    /// <param name="request">The DataWindow handles to scope into the session.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The session id, the handles that were accepted, and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THE SESSION EXISTS FOR EXACTLY ONE REASON: to scope the DataWindow handles that replace the
    /// legacy's live in-process pointer to another DataWindow's expression service. Handles a caller
    /// wants to reference across each other must be opened in the SAME session - see
    /// <see cref="AddForeignVariableAsync"/> for what happens otherwise.
    /// <para>RETRY SAFETY: safe, but each success opens a SEPARATE session that must be closed.</para>
    /// </remarks>
    public Task<OpenExpressionSessionResponse> OpenExpressionSessionAsync(
        OpenExpressionSessionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.OpenExpressionSessionAsync, request, cancellationToken);

    /// <summary>
    /// Closes an expression session.
    /// </summary>
    /// <param name="request">The session id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and whether the session was still open.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// 🔴 RETRY SAFETY: <b>NOT SAFE TO REPLAY TRANSPARENTLY.</b> A session that had already gone reports so
    /// without that being an error, which makes the END STATE idempotent and not the ANSWER: a replay after
    /// a lost response flips <c>was_open</c> from true to false, so the caller is told the session was
    /// already closed by someone else when in fact its own first attempt closed it. Excluded from the
    /// replay-safe roster for the same reason as its C-03 sibling
    /// [<c>Clients/OutboundCallPolicy</c>].
    /// </remarks>
    public Task<CloseExpressionSessionResponse> CloseExpressionSessionAsync(
        CloseExpressionSessionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.CloseExpressionSessionAsync, request, cancellationToken);

    /// <summary>
    /// Binds a new expression to a column.
    /// </summary>
    /// <param name="request">The session, the DataWindow handle, the column name and the expression text.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The assigned index, the outcome code, and a structured expression error on a parse failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The expression text travels UNEXPANDED. Static references in it are resolved at bind time BY THE
    /// SERVER, which is what makes the moment of this call semantically significant and what makes
    /// pre-expanding it on the way past a semantic change rather than an optimisation.
    /// <para>
    /// A parse failure arrives as a structured error carrying the expression, the CARET POSITION and the
    /// message - the same information the legacy put in a dialog, with only the delivery channel
    /// changed. Note that those particular messages are hardcoded and do NOT route through
    /// localization, unlike the equivalents elsewhere in the DataWindow service layer; that
    /// inconsistency is legacy behaviour and is reproduced upstream rather than harmonised.
    /// </para>
    /// <para>RETRY SAFETY: NOT SAFE - a repeated add binds a second expression.</para>
    /// </remarks>
    public Task<AddExpressionResponse> AddExpressionAsync(
        AddExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.AddExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Replaces the expression bound to a column, addressed by index or by name.
    /// </summary>
    /// <param name="request">The session, the handle, the target column reference, the expression, and an optional recalculate flag.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and a structured expression error on a parse failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The recalculate flag carries EXPLICIT PRESENCE, so "the caller said nothing" stays distinct from
    /// "the caller said no". This client never supplies a default for it.
    /// <para>RETRY SAFETY: SAFE - setting the same expression twice is idempotent, though each set with recalculation recalculates again.</para>
    /// </remarks>
    public Task<SetExpressionResponse> SetExpressionAsync(
        SetExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Reads the expression bound to a column, together with its three-part binding.
    /// </summary>
    /// <param name="request">The session, the handle and the target column reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The expression text and the binding: the UNEXPANDED text, the bind-time snapshot, the live
    /// environment, and the variable and function references with their expansion modes.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// ALL THREE PARTS OF THE BINDING ARE RETURNED UNTOUCHED. A consumer needs every one of them to
    /// reason about what a value will be next time it is calculated.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetExpressionResponse> GetExpressionAsync(
        GetExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.GetExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Removes the expression bound to a column, addressed by index or by name.
    /// </summary>
    /// <param name="request">The session, the handle and the target column reference.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - removing what is already gone is not an error worth guarding against here.</remarks>
    public Task<RemoveExpressionResponse> RemoveExpressionAsync(
        RemoveExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.RemoveExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Removes every expression bound on a DataWindow.
    /// </summary>
    /// <param name="request">The session and the DataWindow handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - idempotent.</remarks>
    public Task<RemoveAllExpressionsResponse> RemoveAllExpressionsAsync(
        RemoveAllExpressionsRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.RemoveAllExpressionsAsync, request, cancellationToken);

    /// <summary>
    /// Declares a typed expression variable.
    /// </summary>
    /// <param name="request">The session, the handle, the variable name, and its TYPED value.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The legacy declares this across SEVEN TYPED OVERLOADS, and the value is a seven-arm
    /// discriminated union here for exactly that reason. Which arm is set is load-bearing, and this
    /// client neither converts between arms nor renders any of them as a string.
    /// <para>RETRY SAFETY: NOT SAFE in general - a second add of the same name is a duplicate declaration.</para>
    /// </remarks>
    public Task<AddVariableResponse> AddVariableAsync(
        AddVariableRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.AddVariableAsync, request, cancellationToken);

    /// <summary>
    /// Assigns a new value to an expression variable.
    /// </summary>
    /// <param name="request">The session, the handle, the name, the TYPED value, and the optional recalculate and force flags.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// THIS IS WHERE THE STATIC-VERSUS-DYNAMIC DISTINCTION BECOMES OBSERVABLE. An assignment here
    /// propagates to expressions that reference the variable DYNAMICALLY and does not reach those that
    /// captured it STATICALLY at bind time, because a static reference no longer names the variable at
    /// all. That asymmetry is the contract, not a defect, and nothing in this client papers over it.
    /// <para>RETRY SAFETY: SAFE for the assignment itself; each attempt that requests recalculation recalculates again.</para>
    /// </remarks>
    public Task<SetVariableResponse> SetVariableAsync(
        SetVariableRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetVariableAsync, request, cancellationToken);

    /// <summary>
    /// Declares a variable whose value is itself an expression.
    /// </summary>
    /// <param name="request">The session, the handle, the variable name and the expression text.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and a structured expression error on a parse failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The model is RECURSIVE: a variable's expression carries its own variable and function
    /// references, which may themselves be expressions. The wire mirrors that, and this client walks
    /// none of it.
    /// <para>RETRY SAFETY: NOT SAFE in general - a second add of the same name is a duplicate declaration.</para>
    /// </remarks>
    public Task<AddVariableExpressionResponse> AddVariableExpressionAsync(
        AddVariableExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.AddVariableExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Replaces the expression behind an expression variable.
    /// </summary>
    /// <param name="request">The session, the handle, the name, the expression, and the optional recalculate and force flags.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and a structured expression error on a parse failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE for the assignment; each attempt that requests recalculation recalculates again.</remarks>
    public Task<SetVariableExpressionResponse> SetVariableExpressionAsync(
        SetVariableExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetVariableExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Reads the expression behind an expression variable, with its local variable data.
    /// </summary>
    /// <param name="request">The session, the handle and the variable name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The expression text, the local variable data, and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - read-only.</remarks>
    public Task<GetVariableExpressionResponse> GetVariableExpressionAsync(
        GetVariableExpressionRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.GetVariableExpressionAsync, request, cancellationToken);

    /// <summary>
    /// Declares a variable that reads from ANOTHER DataWindow in the same expression session.
    /// </summary>
    /// <param name="request">The session, the owning handle, the variable name, and the FOREIGN DataWindow handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and, on rejection, the structured expression error carrying its category.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THIS OPERATION CARRIES THE REFACTOR'S ONE DELIBERATELY NARROWED CONTRACT, and the narrowing is
    /// surfaced faithfully here rather than worked around. The legacy foreign-variable structure holds a
    /// LIVE IN-PROCESS OBJECT POINTER to another DataWindow's expression service, and a pointer cannot
    /// be serialized. References are therefore resolved by a SESSION-SCOPED HANDLE and are supported
    /// only when both DataWindows are co-resident in one expression session inside one DataServices
    /// instance.
    /// </para>
    /// <para>
    /// A REFERENCE THAT SPANS SESSIONS OR SERVICE INSTANCES IS BLOCKED WITH A DEFINED ERROR - reported
    /// as an invalid argument carrying the originating outcome code and the foreign-reference-blocked
    /// expression-error category. THAT ERROR IS RETURNED TO THE CALLER EXACTLY AS RECEIVED. It is not
    /// retried, not fallen back from, and not answered with a guess: approximating a pointer dereference
    /// across a network would produce results wrong in a way no test would obviously catch, so the
    /// contract is narrowed with a defined error rather than widened with a guess. What rejected the
    /// call is the TOPOLOGY, not the request, so repeating it cannot help.
    /// </para>
    /// <para>
    /// Note also that a foreign variable REQUIRES dynamic expansion in the legacy, so its references
    /// carry the dynamic mode - one more reason those modes pass through untouched.
    /// </para>
    /// <para>RETRY SAFETY: NOT SAFE, and pointless on the blocked path for the reason above.</para>
    /// </remarks>
    public Task<AddForeignVariableResponse> AddForeignVariableAsync(
        AddForeignVariableRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.AddForeignVariableAsync, request, cancellationToken);

    /// <summary>
    /// Sets the columns an expression depends on, in either the change-triggered or the INPUT-triggered
    /// set.
    /// </summary>
    /// <param name="request">The session, the handle, the target, the related columns, and the plural and input-only selectors.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// One operation stands in for EIGHT legacy entry points - singular and plural, by index and by
    /// name, for both the change-triggered and the input-triggered sets - which is why the selectors
    /// are explicit fields rather than being inferred from the shape of the argument.
    /// <para>RETRY SAFETY: SAFE for a replacing set.</para>
    /// </remarks>
    public Task<SetRelativeColumnsResponse> SetRelativeColumnsAsync(
        SetRelativeColumnsRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetRelativeColumnsAsync, request, cancellationToken);

    /// <summary>
    /// Sets one per-expression flag: always-calculate, recursive, trigger-event or cacheable.
    /// </summary>
    /// <param name="request">The session, the handle, the target, the flag, and whether to enable it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// FOUR flags across TWO addressing modes. The fourth - cacheable - is declared in the legacy
    /// SOURCE and absent from its documented method list, which is why the contract carries it and this
    /// wrapper exposes it.
    /// <para>RETRY SAFETY: SAFE - idempotent.</para>
    /// </remarks>
    public Task<SetExpressionFlagResponse> SetExpressionFlagAsync(
        SetExpressionFlagRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetExpressionFlagAsync, request, cancellationToken);

    /// <summary>
    /// Calculates one expression for one row.
    /// </summary>
    /// <param name="request">The session, the handle, the row, the target column reference, and an optional force flag.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code, the results, and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Each result reports the column, the row, the value, the EVALUATED expression and whether it came
    /// from the cache - the cache being tri-state upstream (unknown, yes, no) rather than a boolean.
    /// A calculation may call back into this client for a macro; see
    /// <see cref="ServeMacroInvocationsAsync"/>.
    /// <para>RETRY SAFETY: SAFE in itself, but a calculation can raise events and invoke macros, so a caller's macro handler must tolerate being asked twice.</para>
    /// </remarks>
    public Task<CalcResponse> CalcAsync(CalcRequest request, CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.CalcAsync, request, cancellationToken);

    /// <summary>
    /// Calculates every bound expression across every row.
    /// </summary>
    /// <param name="request">The session, the handle and an optional force flag.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code, the results, and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: as <see cref="CalcAsync"/>.</remarks>
    public Task<CalcAllResponse> CalcAllAsync(
        CalcAllRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.CalcAllAsync, request, cancellationToken);

    /// <summary>
    /// Calculates the expressions whose inputs are empty, optionally for a single row.
    /// </summary>
    /// <param name="request">The session, the handle and an optional row.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code, the results, and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The row carries EXPLICIT PRESENCE, so "every row" and "row zero" remain different requests.
    /// <para>RETRY SAFETY: as <see cref="CalcAsync"/>.</para>
    /// </remarks>
    public Task<CalcEmptyResponse> CalcEmptyAsync(
        CalcEmptyRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.CalcEmptyAsync, request, cancellationToken);

    /// <summary>
    /// Calculates a single item by expression index.
    /// </summary>
    /// <param name="request">The session, the handle, the row and the expression index.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// A BOOLEAN outcome alongside the result and the outcome code, because the legacy member returns a
    /// boolean rather than a return code.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// TWO LEGACY ANOMALIES ARE PRESERVED HERE RATHER THAN TIDIED. The member is PUBLIC despite
    /// carrying the private-convention underscore prefix, and it returns a boolean where its siblings
    /// return a return code. The protobuf identifier cannot begin with an underscore, so the original
    /// spelling travels as descriptor metadata instead: a compatibility adapter or a characterization
    /// comparator must key on that option rather than on the operation name.
    /// <para>RETRY SAFETY: as <see cref="CalcAsync"/>.</para>
    /// </remarks>
    public Task<CalcItemResponse> CalcItemAsync(
        CalcItemRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.CalcItemAsync, request, cancellationToken);

    /// <summary>
    /// Enables or disables the column-expression service, which is a VETOABLE change.
    /// </summary>
    /// <param name="request">The session, the handle and the desired state.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code, whether the state actually changed, and the resulting state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE SETTER IS VETOABLE, WHICH IS WHY IT ANSWERS WITH AN OUTCOME CODE RATHER THAN A BOOLEAN. The
    /// legacy returns success immediately when the requested state equals the current one, and
    /// otherwise raises an enable event that a handler may refuse - in which case the state does NOT
    /// change and a failure is returned. The response reports the change and the resulting state
    /// separately so a caller can tell a refusal from a no-op, and this client asserts on neither.
    /// </para>
    /// <para>
    /// This is also the other side of the gate coupling: disabling the item-change event through
    /// <see cref="DisableEventAsync"/> suppresses calculation even while this service reports itself
    /// enabled.
    /// </para>
    /// <para>RETRY SAFETY: SAFE - but a refused attempt will be refused again, so it is not a transient failure.</para>
    /// </remarks>
    public Task<SetEnabledResponse> SetEnabledAsync(
        SetEnabledRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetEnabledAsync, request, cancellationToken);

    /// <summary>
    /// Turns expression tracing on or off. Unlike the enable setter, this one is NOT vetoable.
    /// </summary>
    /// <param name="request">The session, the handle and the desired trace state.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome code and the resulting trace state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// This is the gate on <see cref="StreamExpressionTraceAsync"/>: with tracing off, no trace record
    /// is emitted.
    /// <para>RETRY SAFETY: SAFE - idempotent.</para>
    /// </remarks>
    public Task<SetTraceResponse> SetTraceAsync(
        SetTraceRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.SetTraceAsync, request, cancellationToken);

    /// <summary>
    /// Reads whether the column-expression service is enabled and whether tracing is on.
    /// </summary>
    /// <param name="request">The session and the DataWindow handle.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The two states and the outcome code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>RETRY SAFETY: SAFE - read-only.</remarks>
    public Task<GetServiceStateResponse> GetServiceStateAsync(
        GetServiceStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.GetServiceStateAsync, request, cancellationToken);

    /// <summary>
    /// Reads the engine-state snapshot: the expression table, the REVERSE DEPENDENCY INDEX, the grammar
    /// sentinels and, on request, the global variable table and the three-part bindings.
    /// </summary>
    /// <param name="request">The session, the handle, an optional column filter, and whether to include the bindings.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The snapshot, the outcome code, and a structured expression error on failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// READ-ONLY BY DESIGN. Engine state is mutated through the typed operations above and never by
    /// posting a snapshot back, so there is no corresponding write here and none should be added. The
    /// reverse dependency index - from a column to the expressions and variables depending on it - is
    /// the graph that makes dirty propagation work, and it is returned as the contract shapes it.
    /// <para>RETRY SAFETY: SAFE - read-only.</para>
    /// </remarks>
    public Task<GetExpressionStateResponse> GetExpressionStateAsync(
        GetExpressionStateRequest request,
        CancellationToken cancellationToken) =>
        InvokeAsync(
            OutboundSurface.ColumnExpression,
            _columnExpression.GetExpressionStateAsync, request, cancellationToken);

    /// <summary>
    /// Streams the three events the expression engine declares on ITSELF: item-changed,
    /// do-item-changed with its from-input flag, and var-changed with its force-calculate flag.
    /// </summary>
    /// <param name="request">The session, the handle and an optional column filter.</param>
    /// <param name="cancellationToken">Cancels the subscription and tears the stream down.</param>
    /// <returns>
    /// The notifications in arrival order, each with its sequence number PASSED THROUGH UNCHANGED so
    /// the consumer can detect a gap.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The call failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// A SERVER STREAM AND NOT AN INVERTED ONE: these are notifications the engine EMITS, not questions
    /// it asks. They carry a sequencing token rather than strict ordering, so the sequence is neither
    /// consumed nor rewritten here - gap detection is the consumer's, and reordering is the consumer's
    /// prerogative, not this client's.
    /// <para>RETRY SAFETY: re-subscribing is safe; resuming mid-stream is not, because the sequence would not continue.</para>
    /// </remarks>
    public async IAsyncEnumerable<EventStreamResponse> StreamExpressionEventsAsync(
        EventStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.ColumnExpression,
                OutboundCallClass.Stream,
                cancellationToken)
            .ConfigureAwait(false);

        using AsyncServerStreamingCall<EventStreamResponse> call =
            _columnExpression.EventStream(request, options);

        await foreach (EventStreamResponse notification in call.ResponseStream
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return notification;
        }
    }

    /// <summary>
    /// SERVES macro invocations: DataServices asks Gateway to evaluate a macro, and the supplied handler
    /// answers.
    /// </summary>
    /// <param name="handler">
    /// Evaluates one invocation and produces the answer. It must echo the request's
    /// <c>InvocationId</c>, which is the only correlation between an answer and the calculation blocked
    /// on it.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels the service loop and tears the duplex channel down, which is how the engine learns to
    /// stop waiting.
    /// </param>
    /// <returns>
    /// A task that completes when DataServices closes the inbound half - that is, when it has no
    /// further invocations to make.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The handler returned <see langword="null"/>, or returned an answer whose <c>InvocationId</c>
    /// does not match the invocation it answers. Both would leave the engine waiting for an answer it
    /// can never correlate, so they are refused loudly instead of being silently corrected - filling the
    /// identifier in on the handler's behalf would be exactly the payload rewriting this client does not
    /// do.
    /// </exception>
    /// <exception cref="RpcException">The channel could not be established, or it faulted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// THE INVERSION IS STRUCTURAL, NOT A PREFERENCE. The legacy expects THE APPLICATION to implement
    /// the macro switch - the body lives in the DataWindow's own macro event and the engine calls out to
    /// it mid-calculation. Across this boundary the engine is in DataServices and the application is on
    /// Gateway's side, so DataServices must call BACK into its client to finish a calculation. That is
    /// why this member SERVES a stream rather than merely reading one, and it is visible in the
    /// generated signature: the client writes responses and reads requests.
    /// </para>
    /// <para>
    /// SYNCHRONOUS WITHIN THE STREAM: the calculation cannot proceed without the value, so invocations
    /// are answered ONE AT A TIME in arrival order. No answer is reordered, batched or coalesced - doing
    /// any of those would answer a calculation with another calculation's value.
    /// </para>
    /// <para>
    /// The invocation carries the expansion FORM - direct or dynamic - and whether the engine
    /// recognised the name as a built-in. Both reach the handler untouched, because a handler may
    /// legitimately answer a dynamic invocation differently from a direct one.
    /// </para>
    /// <para>RETRY SAFETY: NOT SAFE mid-stream. Re-establishing the channel is safe only when no invocation is outstanding.</para>
    /// </remarks>
    public async Task ServeMacroInvocationsAsync(
        MacroInvocationHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.ColumnExpression,
                OutboundCallClass.Stream,
                cancellationToken)
            .ConfigureAwait(false);

        using AsyncDuplexStreamingCall<InvokeMethodResponse, InvokeMethodRequest> call =
            _columnExpression.InvokeMethodChannel(options);

        await foreach (InvokeMethodRequest invocation in call.ResponseStream
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            InvokeMethodResponse answer = await handler(invocation, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The macro invocation handler returned no answer for invocation "
                    + $"'{invocation.InvocationId}'. The engine is waiting on this value, so a missing "
                    + $"answer would stall the calculation; return an answer marked unhandled instead."));

            if (!string.Equals(answer.InvocationId, invocation.InvocationId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The macro invocation handler answered invocation '{invocation.InvocationId}' with "
                    + $"an answer carrying invocation id '{answer.InvocationId}'. The identifier is the "
                    + $"only correlation the engine has, so the mismatch is refused rather than "
                    + $"rewritten on the handler's behalf."));
            }

            await call.RequestStream.WriteAsync(answer, cancellationToken).ConfigureAwait(false);
        }

        // The inbound half closed, so the conversation is finished rather than abandoned: half-close
        // the outbound half explicitly. On any other exit the `using` disposes the call, which cancels
        // it - the right signal when the conversation is being abandoned.
        await call.RequestStream.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Subscribes to the expression trace and streams the records, including the recursion call stack.
    /// </summary>
    /// <param name="request">The session, the handle, and whether this is a subscribe or an unsubscribe.</param>
    /// <param name="cancellationToken">Cancels the subscription and tears the channel down.</param>
    /// <returns>
    /// The trace records in arrival order, each yielded unchanged - the flattened stack, the same stack
    /// UNFLATTENED as frames, the expression, the rendered value, the recursion depth and the
    /// sequencing token.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The channel could not be established, or it faulted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// PURE DIAGNOSTICS, FIRE AND FORGET, AND IT MUST NEVER GATE AN EXPRESSION RESULT. That property is
    /// structural here rather than promised: the trace runs on its OWN call, and no other member of
    /// this client awaits it, subscribes to it, or waits on a record before returning a calculation. A
    /// caller consumes it on its own and a calculation completes whether or not anyone is reading.
    /// Consistent with that, the records carry a SEQUENCING TOKEN rather than strict ordering, and the
    /// token is passed through untouched: gap detection belongs to the consumer.
    /// </para>
    /// <para>
    /// The flattened stack is AMBIGUOUS by construction - a column name containing the frame delimiter
    /// would split into two frames - and the unflattened frames are carried alongside it for that
    /// reason. Both travel: the flattened form is what a characterization recording compares, the
    /// frames are what a consumer should actually read. This client chooses neither for the caller.
    /// </para>
    /// <para>
    /// THIS MEMBER IS ONE-SHOT, AND THE OUTBOUND HALF IS CLOSED AS SOON AS THE ONE CONTROL MESSAGE HAS
    /// BEEN WRITTEN. The underlying operation is duplex, but this signature exposes only the inbound
    /// half - it returns records and hands back no writer - so there is no way for a caller to send a
    /// second control message even in principle. Leaving the outbound half open on the strength of an
    /// ability the API does not grant would be worse than merely inaccurate: the far end cannot
    /// distinguish "will send more" from "cannot send more", so it would wait for a message that can
    /// never arrive, and it would hold the resources of that wait for as long as the subscription lasts.
    /// Half-closing states the truth on the wire.
    /// </para>
    /// <para>
    /// TO CHANGE A SUBSCRIPTION, CALL AGAIN. The request carries its own kind - subscribe or unsubscribe
    /// - so an unsubscribe is another one-shot call rather than a second message on this one. Ending the
    /// enumeration also unsubscribes: it disposes the call, which cancels it. That is the ordinary way
    /// to stop, and an explicit unsubscribe exists for a caller that wants the far end to be told.
    /// </para>
    /// <para>RETRY SAFETY: re-subscribing is safe - the trace is diagnostic, and a duplicate record has no effect on any result.</para>
    /// </remarks>
    public async IAsyncEnumerable<TraceRecord> StreamExpressionTraceAsync(
        TraceChannelRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                OutboundSurface.ColumnExpression,
                OutboundCallClass.Stream,
                cancellationToken)
            .ConfigureAwait(false);

        using AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> call =
            _columnExpression.TraceChannel(options);

        await call.RequestStream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        // ONE CONTROL MESSAGE, THEN HALF-CLOSE. This signature exposes no writer, so no further message
        // can be sent; saying so on the wire is what stops the far end waiting for one. It is a
        // half-close and not a cancellation, so the subscription itself stays live and records keep
        // arriving on the inbound half until the enumeration ends or the call is cancelled.
        await call.RequestStream.CompleteAsync().ConfigureAwait(false);

        await foreach (TraceRecord record in call.ResponseStream
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return record;
        }
    }

    /// <summary>
    /// Reads the rich-error binding off C-03's <c>Update</c> descriptor.
    /// </summary>
    /// <returns>The binding, or <see langword="null"/> when the descriptor declares none.</returns>
    private static RichErrorBinding? ReadUpdateRichErrorBinding()
    {
        MethodDescriptor? method = DataWindowService.Descriptor.FindMethodByName(UpdateMethodName);
        MethodOptions? options = method?.GetOptions();

        return options?.GetExtension(CommonV1Extensions.RichError);
    }

    /// <summary>
    /// Obtains a bearer credential and packages it as the call options every outbound call uses.
    /// </summary>
    /// <param name="callClass">
    /// Which bound applies: a unary call is bounded by the point at which this caller abandons it, a
    /// stream by the point at which the upstream would have reclaimed the session behind it.
    /// </param>
    /// <param name="cancellationToken">Cancels the token acquisition and the call it is built for.</param>
    /// <returns>
    /// Call options carrying the authorization header, the caller's cancellation token, and the
    /// deadline for this class of call.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE DEADLINE COMES FROM THE COMPOSITION ROOT, WHICH IS WHAT MAKES IT LEGITIMATE. Setting none here,
    /// on the reasoning that a duration invented in this file would have no derivation and
    /// that the policy belongs where the resilience pipeline is configured, is right in both halves and
    /// wrong as a conclusion: what it leaves missing is the policy. That policy is <c>Gateway:Outbound</c>,
    /// the unary bound is the same setting that configures the pipeline's total request timeout, and the
    /// stream bound is derived from the upstream's own session idle lifetime - so no duration originates here.
    /// </para>
    /// <para>
    /// WHY CANCELLATION ALONE IS NOT ENOUGH, even though it is threaded through every member. A
    /// cancellation token bounds THIS process's willingness to wait; it tells the upstream nothing. When
    /// the caller goes away in a way the transport has not yet noticed - a half-open connection, a
    /// container killed mid-request - the upstream keeps working and keeps the server-held session or
    /// task alive, and only its own idle sweep eventually reclaims it. The deadline is what puts that
    /// bound on the wire, where the upstream can act on it.
    /// </para>
    /// </remarks>
    private async Task<CallOptions> CreateAuthenticatedCallOptionsAsync(
        OutboundSurface surface,
        OutboundCallClass callClass,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // THE CREDENTIAL IS SCOPED TO THE SURFACE BEING CALLED, not to everything this client can reach.
        ServiceTokenRequest tokenRequest = surface switch
        {
            OutboundSurface.DataWindow => DataWindowTokenRequest,
            OutboundSurface.ColumnExpression => ColumnExpressionTokenRequest,

            // An unrecognised surface is a programming error in this file rather than a runtime condition,
            // and refusing is what stops it silently acquiring whichever credential happened to be first.
            _ => throw new ArgumentOutOfRangeException(
                nameof(surface),
                surface,
                "No token request is declared for this outbound surface."),
        };

        ServiceToken token = await _tokenProvider
            .GetTokenAsync(tokenRequest, cancellationToken)
            .ConfigureAwait(false);

        // A NARROWER GRANT IS A SUCCESSFUL OUTCOME, not a failure, so it is READ - which the token
        // contract requires - reported, and never enforced. Counts only: neither the credential nor the
        // scope strings are logged, because a scope set describes what a credential may do.
        // Read UNCONDITIONALLY - the contract obliges a caller to read the granted set rather than
        // assume its request was honoured in full - and only the reporting of it is level-guarded.
        int grantedScopeCount = token.GrantedScopes.Count;
        int requestedScopeCount = tokenRequest.Scopes.Count;
        bool narrowed = grantedScopeCount < requestedScopeCount;

        if (_logger.IsEnabled(LogLevel.Debug) && narrowed)
        {
            _logger.LogDebug(
                "Security granted {GrantedScopeCount} of {RequestedScopeCount} requested scope(s) for "
                + "the DataServices audience on the {Surface} surface. A narrowing is a successful "
                + "outcome, so the call proceeds and the upstream decides what the credential permits.",
                grantedScopeCount,
                requestedScopeCount,
                surface);
        }

        Metadata headers = new()
        {
            { AuthorizationHeaderName, string.Concat(token.TokenType, " ", token.AccessToken) },
        };

        return new CallOptions(
            headers: headers,
            deadline: _deadlines.DeadlineFor(callClass),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Runs one authenticated unary call: acquire the credential, invoke the stub, return the
    /// contract's own response message unchanged.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="operation">The generated stub method to invoke.</param>
    /// <param name="request">The request message.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, exactly as the upstream produced it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This helper exists so that credential attachment and argument validation are written once
    /// rather than once per unary member, which is the only way to be sure every member really does
    /// attach one.
    /// It adds no behaviour of its own: no status is caught here, no payload is inspected, and no
    /// response is transformed. Members that must translate a failure do so in their own bodies, where
    /// the translation is visible.
    /// </remarks>
    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        OutboundSurface surface,
        Func<TRequest, CallOptions, AsyncUnaryCall<TResponse>> operation,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        ArgumentNullException.ThrowIfNull(request);

        CallOptions options = await CreateAuthenticatedCallOptionsAsync(
                surface,
                OutboundCallClass.Unary,
                cancellationToken)
            .ConfigureAwait(false);

        return await operation(request, options).ConfigureAwait(false);
    }

    /// <summary>
    /// Decodes the conflict detail from a failed call's trailers, using the binding the descriptor
    /// declares.
    /// </summary>
    /// <param name="exception">The failed call.</param>
    /// <param name="retCode">
    /// Receives the reconciled outcome code the trailer carried, or zero when nothing was decoded.
    /// </param>
    /// <returns>
    /// The conflict detail, or <see langword="null"/> when this call declares no rich-error contract,
    /// when the status is not the one the binding names, when the trailer is absent, when it carries a
    /// database failure rather than a conflict, or when it cannot be parsed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EVERY ONE OF THOSE "NO" ANSWERS IS SAFE, because the caller's response to <see langword="null"/>
    /// is to rethrow the original exception with its status and its complete trailer set intact.
    /// Nothing is swallowed on any path through this method: the worst case is that the caller sees the
    /// raw gRPC failure rather than a typed conflict, and the payload remains available on it.
    /// </para>
    /// <para>
    /// THE TRAILER KEY, THE STATUS AND THE PAYLOAD TYPE ALL COME FROM THE DESCRIPTOR, not from a literal
    /// written here. The key's trailing binary marker is not decoration: gRPC transports a metadata key
    /// with that suffix as binary and permits only text under any other, so a protobuf payload under a
    /// non-binary key would be corrupted in transit - which is why the byte-valued accessor is the one
    /// used to read it.
    /// </para>
    /// <para>
    /// A DATABASE FAILURE IN THE TRAILER IS NOT DECODED INTO ANYTHING LOGGABLE HERE. The alternative arm
    /// of the payload carries a statement field that would hold the complete generated statement, and
    /// the legacy logger performed no redaction at all; it is therefore left on the exception for a
    /// caller that is entitled to it and is neither logged nor echoed.
    /// </para>
    /// </remarks>
    private ConflictDetail? TryReadConflictDetail(RpcException exception, out long retCode)
    {
        retCode = 0;

        RichErrorBinding? binding = UpdateRichErrorBinding;
        if (binding is null || string.IsNullOrEmpty(binding.TrailerKey))
        {
            _logger.LogWarning(
                "The update operation's descriptor declares no rich-error binding, so a conflict "
                + "trailer cannot be located. This indicates a drift between the generated contract and "
                + "the client; the gRPC failure is surfaced unchanged.");

            return null;
        }

        if (binding.GrpcStatusCode != (int)exception.StatusCode)
        {
            return null;
        }

        if (!string.Equals(binding.PayloadType, SupportedRichErrorPayloadType, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "The update operation's rich-error binding names payload type {DeclaredPayloadType}, "
                + "which this client cannot decode; it understands {SupportedPayloadType}. The gRPC "
                + "failure is surfaced unchanged rather than being parsed as a type it may not be.",
                binding.PayloadType,
                SupportedRichErrorPayloadType);

            return null;
        }

        byte[]? raw = exception.Trailers?.GetValueBytes(binding.TrailerKey);
        if (raw is null)
        {
            return null;
        }

        RichErrorTrailer trailer;
        try
        {
            trailer = RichErrorTrailer.Parser.ParseFrom(raw);
        }
        catch (InvalidProtocolBufferException parseFailure)
        {
            // Described rather than attached, exactly as on the sibling DataServices client. A protobuf
            // parse failure quotes the bytes and field numbers it choked on, and those bytes come from the
            // UPSTREAM's trailer.
            _logger.LogWarning(
                "The conflict trailer on an aborted update could not be parsed as {SupportedPayloadType}. "
                + "The gRPC failure is surfaced unchanged, with its trailers intact. "
                + "FaultTypes={FaultTypes}",
                SupportedRichErrorPayloadType,
                ExceptionChain.DescribeTypes(parseFailure));

            return null;
        }

        retCode = trailer.RetCode;

        return trailer.DetailCase == RichErrorTrailer.DetailOneofCase.Conflict ? trailer.Conflict : null;
    }
}
