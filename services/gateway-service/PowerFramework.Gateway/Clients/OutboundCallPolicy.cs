// ==================================================================================================
//  PowerFramework.Gateway.Clients.OutboundCallPolicy
//  ------------------------------------------------------------------------------------------------
//  The retry-admission, circuit-breaking and deadline policy for every call Gateway makes outward:
//  the two gRPC channels to DataServices (contracts C-03 and C-04) and the REST channel to Security
//  (contracts C-01 and C-02).
//
//  THIS FILE HAS NO LEGACY SOURCE, AND THAT IS STRUCTURAL RATHER THAN AN OMISSION. The legacy
//  PowerFramework is a library loaded in-process: every call in it is a synchronous method invocation
//  that cannot fail in transit, so there is no legacy retry policy, no legacy deadline and no legacy
//  idempotency classification to port. This policy exists because decomposition created the failure
//  mode - a call that could not fail in transit now can - and handling that failure mode is required
//  BY the transition rather than layered on top of it (AAP 0.5.3, the justification recorded there for
//  Microsoft.Extensions.Http.Resilience being the one permitted resilience dependency).
//
//  WHY A GENERIC HTTP RESILIENCE PIPELINE IS NOT SUFFICIENT ON A gRPC CHANNEL, stated once here
//  because it is the whole reason this file exists:
//
//    1. A gRPC call that the SERVER refused arrives as HTTP 200. The refusal lives in the
//       `grpc-status` metadata - in the response headers when the server answered trailers-only, and
//       in the trailers when it had already begun writing. An HTTP-level predicate therefore reads
//       "success" for a server-declared Unavailable and never retries it, which is the half of the
//       problem that costs availability.
//    2. A TRANSPORT fault - a reset connection, a refused socket, a closed HTTP/2 stream - arrives as
//       an exception, which every stock transient predicate treats as retryable. Every gRPC call is
//       an HTTP POST, so a method-based gate (DisableForUnsafeHttpMethods) is all-or-nothing and a
//       stock predicate happily REPLAYS an update, a session open or a transaction commit. That is
//       the half that costs correctness, and it is the more serious half.
//
//  So admission is decided from two independent facts: WHICH OPERATION this is, taken from the
//  request path, and WHAT HAPPENED, taken from the gRPC status when one is readable and from the
//  package's own transient definition when one is not.
//
//  THE CLASSIFICATION RULE, AND IT IS DELIBERATELY CONSERVATIVE. A transport-level replay is
//  INVISIBLE to the caller: it happens below the client member, so the caller cannot compensate for
//  it, cannot observe that it happened, and cannot decide not to allow it. Only two families of
//  operation are therefore admitted -
//
//      (i)  a pure READ, which changes nothing and whose repetition is unobservable; and
//      (ii) an idempotent TEARDOWN - a release or a close - whose repetition is not merely harmless
//           but actively useful, because a leaked server-held handle is the failure mode a lost
//           teardown response produces.
//
//  - and everything else is attempted exactly once. Several members of DataServicesClient document
//  themselves as safe to retry in a broader sense ("re-subscribing is safe", "a caller's macro
//  handler must tolerate being asked twice"): those annotations describe a CALLER-INITIATED retry
//  that the caller knows it is performing, and they are not licence for a silent replay underneath
//  it. Under-retrying costs availability, about which this repository publishes no commitment
//  whatsoever (AAP 0.8.5); over-retrying costs correctness, about which it publishes a great deal.
//
//  NO DURATION HERE IS A LATENCY TARGET, AND NONE IS INVENTED. AAP 0.8.5 forbids asserting a
//  performance objective, because the repository publishes no SLA, no latency budget and no
//  throughput target from which one could be derived. Every duration below is a CORRECTNESS bound
//  with its derivation named at the point of use, and each derives from a value that already exists
//  in this repository.
// ==================================================================================================

using System.Collections.Frozen;
using System.Globalization;
using System.Net.Http.Headers;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using PowerFramework.Contracts.DataServices.V1;

// ALIASED BECAUSE THE NAME IS TAKEN. Implicit usings bring in
// Microsoft.Extensions.DependencyInjection, whose ServiceDescriptor describes a container
// registration rather than a gRPC contract. The alias says which one is meant at every use site.
using ContractDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace PowerFramework.Gateway.Clients;

/// <summary>
/// Which class of call a set of <see cref="global::Grpc.Core.CallOptions"/> is being built for, and therefore
/// which deadline bounds it.
/// </summary>
/// <remarks>
/// The distinction is not cosmetic. A unary call is bounded by the point at which the CALLER abandons
/// it; a stream is legitimately long-lived and is bounded instead by the point at which the UPSTREAM
/// would have reclaimed the resource behind it. Collapsing the two would either cut streams off
/// mid-flight or leave unary calls effectively unbounded.
/// </remarks>
public enum OutboundCallClass
{
    /// <summary>
    /// A single request and a single response. Bounded by the caller's total request timeout.
    /// </summary>
    Unary = 0,

    /// <summary>
    /// A server-streaming or bidirectional call whose lifetime is the session it belongs to.
    /// </summary>
    Stream = 1,
}

/// <summary>
/// The two deadlines Gateway applies to its outbound calls, and the clock they are measured on.
/// </summary>
/// <remarks>
/// <para>
/// WHY A DEADLINE IS A CORRECTNESS CONCERN AND NOT A PERFORMANCE ONE. Without one, an upstream keeps
/// working - and keeps the server-held handle or session behind that work alive - for as long as the
/// transport appears open, including after the caller has gone away in a way the transport has not
/// yet noticed. A half-open connection is exactly that case. The deadline is the mechanism by which
/// the upstream learns the bound the caller is actually honouring and releases what it is holding, so
/// it protects the upstream's admission limits rather than the caller's latency.
/// </para>
/// <para>
/// A gRPC deadline is enforced by the CLIENT as a TOTAL bound across every HTTP attempt, because the
/// resilience pipeline sits beneath the gRPC call and its retries are invisible to it. The unary
/// deadline is therefore set to the pipeline's own total request timeout rather than to its
/// per-attempt timeout: a shorter value would pre-empt the retry budget the same pipeline was
/// configured with, and the two would silently disagree.
/// </para>
/// <para>
/// THE CLOCK IS SUBSTITUTABLE FOR ASSERTION, NOT FOR BEHAVIOUR. gRPC measures the deadline it is
/// given on the system clock, so a test that supplies a fake clock is observing the value this type
/// computes and not changing when a real call expires.
/// </para>
/// </remarks>
public sealed class OutboundDeadlines
{
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates the deadline pair.
    /// </summary>
    /// <param name="unary">The total bound on a unary call.</param>
    /// <param name="stream">The total bound on a streaming call.</param>
    /// <param name="clock">The clock the absolute deadline is computed from. Defaults to the system clock.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Either duration is zero or negative. A non-positive deadline would expire before the request
    /// reached the upstream, turning every call into a deadline failure.
    /// </exception>
    public OutboundDeadlines(TimeSpan unary, TimeSpan stream, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(unary, TimeSpan.Zero, nameof(unary));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stream, TimeSpan.Zero, nameof(stream));

        Unary = unary;
        Stream = stream;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// The pair a client falls back to when no configured instance was supplied.
    /// </summary>
    /// <remarks>
    /// THIS EXISTS SO THAT NO CONSTRUCTION PATH PRODUCES AN UNBOUNDED CALL. A hand-constructed client
    /// - in a test, or in any host that has not bound the configuration section - still sets a
    /// deadline, because "no deadline" is the defect this type was added to remove and leaving it
    /// reachable through an omitted argument would preserve it. The values match the defaults on
    /// <see cref="Configuration.GatewayOptions.OutboundCallOptions"/> so that the fallback and the
    /// configured instance cannot disagree about what the shipped posture is.
    /// </remarks>
    public static OutboundDeadlines Default { get; } = new(
        Configuration.GatewayOptions.OutboundCallOptions.DefaultRequestTimeout,
        Configuration.GatewayOptions.OutboundCallOptions.DefaultStreamDeadline);

    /// <summary>The total bound on a unary call.</summary>
    public TimeSpan Unary { get; }

    /// <summary>The total bound on a streaming call.</summary>
    public TimeSpan Stream { get; }

    /// <summary>
    /// Computes the absolute UTC instant a call of the given class must complete by.
    /// </summary>
    /// <param name="callClass">The class of call.</param>
    /// <returns>The deadline, in UTC, as gRPC requires.</returns>
    /// <remarks>
    /// An unrecognised member is treated as <see cref="OutboundCallClass.Unary"/> rather than left
    /// unbounded, because the safe answer to "which bound applies" is the tighter one.
    /// </remarks>
    public DateTime DeadlineFor(OutboundCallClass callClass)
    {
        TimeSpan budget = callClass is OutboundCallClass.Stream ? Stream : Unary;

        return _clock.GetUtcNow().UtcDateTime + budget;
    }
}

/// <summary>
/// Decides, per outbound operation and per observed outcome, whether an attempt may be replayed and
/// whether it counts against the circuit breaker.
/// </summary>
/// <remarks>
/// <para>
/// A PER-SERVICE COPY BY DESIGN. DataServices carries its own equivalent for its own outbound edges.
/// The classification is a statement about THIS service's callees and their contracts, so sharing one
/// implementation across a service boundary would make behaviour cross a boundary that the
/// architecture allows only published contracts to cross (AAP 0.7.2), and it would couple two
/// services' retry posture together. <c>SecurityClient</c> and <c>InternalTlsTrust</c> exist twice
/// for the same reason.
/// </para>
/// <para>
/// THE PATH TABLE IS BUILT FROM THE CONTRACT DESCRIPTORS RATHER THAN FROM STRING LITERALS, so a
/// renamed or removed method is a startup failure instead of a silently unclassified operation that
/// falls back to "not replay-safe" and quietly loses its retry. The build also refuses to admit a
/// streaming method, so the two rules cannot drift apart.
/// </para>
/// </remarks>
internal static class OutboundCallPolicy
{
    /// <summary>
    /// The metadata key carrying a gRPC status. Lower-case because gRPC metadata keys are
    /// case-insensitive by specification and are normalised to lower case on the wire.
    /// </summary>
    private const string GrpcStatusHeaderName = "grpc-status";

    /// <summary>
    /// The gRPC status codes an attempt may be replayed for, when - and only when - the operation is
    /// itself replay-safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// UNAVAILABLE is the canonical "this upstream cannot serve right now" answer: a container still
    /// starting, a listener draining for shutdown, a connection the platform reset. RESOURCE
    /// EXHAUSTED is this system's admission refusal - the Persistence handle quota answers with it -
    /// and it is retryable in the same narrow sense, because the condition it reports is transient by
    /// construction.
    /// </para>
    /// <para>
    /// EVERY OTHER STATUS IS EXCLUDED, and the exclusions are the part that matters:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// ABORTED is the optimistic-concurrency answer contract C-06 is built on. It is a DEFINITIVE
    /// statement that the row moved under the caller, and the caller's obligation is to re-read and
    /// rebase. Retrying it is the silent-overwrite failure mode this system forbids outright, so it
    /// is named here rather than merely omitted.
    /// </item>
    /// <item>
    /// CANCELLED means somebody asked for the call to stop. Replaying it defeats the request.
    /// </item>
    /// <item>
    /// UNAUTHENTICATED and PERMISSION_DENIED are decisions, not faults. A replay presents the same
    /// credential to the same policy and is refused identically, so it converts one refusal into
    /// several and can look like a credential-stuffing pattern in an audit trail.
    /// </item>
    /// <item>
    /// INVALID_ARGUMENT, FAILED_PRECONDITION and OUT_OF_RANGE are validation answers about the
    /// request. The request does not change between attempts.
    /// </item>
    /// <item>
    /// ALREADY_EXISTS and NOT_FOUND are answers about state; DEADLINE_EXCEEDED means the bound the
    /// caller itself set has passed, so there is no budget left to spend; UNIMPLEMENTED is the
    /// deferred-capability answer and will never become implemented mid-flight; INTERNAL, UNKNOWN and
    /// DATA_LOSS are faults whose repetition is not known to be safe on either side.
    /// </item>
    /// </list>
    /// </remarks>
    private static readonly FrozenSet<int> RetryableGrpcStatuses = FrozenSet.ToFrozenSet(
    [
        (int)global::Grpc.Core.StatusCode.Unavailable,
        (int)global::Grpc.Core.StatusCode.ResourceExhausted,
    ]);

    /// <summary>
    /// The gRPC status codes that count as a failure for circuit-breaking purposes.
    /// </summary>
    /// <remarks>
    /// NARROWER THAN THE RETRY SET, DELIBERATELY. A breaker that opens stops every call on the
    /// channel, including the pure reads that would still have succeeded, so the only status admitted
    /// is the one that genuinely reports the upstream cannot serve. RESOURCE_EXHAUSTED is excluded
    /// even though it is retryable: it is a HEALTHY, deliberate admission refusal aimed at one
    /// caller's quota, and breaking the channel on it would let one caller's over-use deny the whole
    /// service. ABORTED, the authorization statuses and the validation statuses are excluded for the
    /// stronger reason that they are successful, correct answers that happen to be refusals - counting
    /// them would let ordinary contested updates or a misconfigured caller trip the breaker.
    /// </remarks>
    private static readonly FrozenSet<int> CircuitBreakingGrpcStatuses = FrozenSet.ToFrozenSet(
    [
        (int)global::Grpc.Core.StatusCode.Unavailable,
    ]);

    /// <summary>
    /// Absolute request paths whose attempts may be replayed at the transport level.
    /// </summary>
    private static readonly FrozenSet<string> ReplaySafePaths = BuildReplaySafePaths();

    /// <summary>
    /// Decides whether a failed attempt may be retried.
    /// </summary>
    /// <param name="arguments">The attempt's outcome and its resilience context.</param>
    /// <returns><see langword="true"/> when the attempt may be replayed.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE THREE TESTS IS THE POLICY. The operation is checked FIRST, so an unsafe or
    /// unrecognised operation is refused before any outcome is examined at all - which means a new
    /// method added to a contract without being classified defaults to "attempted exactly once"
    /// rather than to "replayed". Then a readable gRPC status decides alone, because for a gRPC
    /// response the HTTP status is 200 and consulting it would answer "success" for a call the server
    /// refused. Only when no gRPC status is readable - a genuine transport fault, a proxy's own 5xx,
    /// or a REST response - does the package's transient definition apply.
    /// </para>
    /// <para>
    /// A REQUEST THAT CANNOT BE RECOVERED FROM THE CONTEXT IS REFUSED. It is the only honest answer:
    /// with no path there is no classification, and guessing would replay whatever the unknown call
    /// happened to be.
    /// </para>
    /// </remarks>
    internal static ValueTask<bool> ShouldRetryAsync(
        RetryPredicateArguments<HttpResponseMessage> arguments)
    {
        if (!IsReplaySafe(ReadRequest(arguments)))
        {
            return ValueTask.FromResult(false);
        }

        if (arguments.Outcome.Result is { } response
            && TryReadGrpcStatus(response, out int grpcStatus))
        {
            return ValueTask.FromResult(RetryableGrpcStatuses.Contains(grpcStatus));
        }

        return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
    }

    /// <summary>
    /// Decides whether an outcome counts against the circuit breaker.
    /// </summary>
    /// <param name="arguments">The outcome and its resilience context.</param>
    /// <returns><see langword="true"/> when the outcome is evidence the upstream is unhealthy.</returns>
    /// <remarks>
    /// UNLIKE RETRY, THIS IS NOT GATED ON THE OPERATION. Whether the upstream is healthy is a property
    /// of the upstream, not of the call that discovered it, so a server-declared Unavailable on an
    /// update is exactly as much evidence as one on a read. What IS filtered is the meaning of the
    /// answer: a status the server chose deliberately and correctly is not a failure of the server.
    /// </remarks>
    internal static ValueTask<bool> ShouldBreakAsync(
        CircuitBreakerPredicateArguments<HttpResponseMessage> arguments)
    {
        if (arguments.Outcome.Result is { } response
            && TryReadGrpcStatus(response, out int grpcStatus))
        {
            return ValueTask.FromResult(CircuitBreakingGrpcStatuses.Contains(grpcStatus));
        }

        return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(arguments.Outcome));
    }

    /// <summary>
    /// Forces the classification table to be built, so a contract mismatch is a startup failure.
    /// </summary>
    /// <returns>The number of classified replay-safe operations.</returns>
    /// <remarks>
    /// The table is built in a static initializer, which would otherwise run at the first outbound
    /// failure - the worst possible moment to discover that a method name no longer resolves. The
    /// composition root calls this immediately after building the host for the same reason it eagerly
    /// resolves the trust anchor and the client identity there.
    /// </remarks>
    internal static int Verify() => ReplaySafePaths.Count;

    /// <summary>
    /// Recovers the request an attempt was made for, from whichever of the two places carries it.
    /// </summary>
    /// <param name="arguments">The attempt's outcome and its resilience context.</param>
    /// <returns>The request, or <see langword="null"/> when neither place carries one.</returns>
    /// <remarks>
    /// <para>
    /// TWO PLACES, ONE OBJECT, AND BOTH ARE POPULATED IN THE DEPLOYED PIPELINE. The resilience handler
    /// puts the outgoing request on the resilience context before it executes the strategy, and the
    /// message handler beneath it sets <see cref="HttpResponseMessage.RequestMessage"/> on the response
    /// it returns - so in production these two references are the same request and reading either
    /// answers identically. The context is preferred because it is present even when there is no
    /// response at all, which is exactly the transport-fault case this policy has to classify.
    /// </para>
    /// <para>
    /// THE FALLBACK IS NOT DEFENSIVE PADDING. A caller that hands the predicate an outcome without
    /// seeding the context - which is how a pipeline's options are interrogated directly, rather than
    /// driven - would otherwise be told "unclassifiable, refuse" for every request, and a suite built
    /// that way would be asserting the absence of a seed rather than the policy. Reading the response's
    /// own back-reference makes the predicate answer the same question the same way however it is
    /// reached.
    /// </para>
    /// </remarks>
    private static HttpRequestMessage? ReadRequest(
        RetryPredicateArguments<HttpResponseMessage> arguments) =>
        arguments.Context.GetRequestMessage() ?? arguments.Outcome.Result?.RequestMessage;

    /// <summary>
    /// Answers whether the operation behind this attempt may be replayed.
    /// </summary>
    /// <param name="context">The resilience context the pipeline is executing under.</param>
    /// <returns><see langword="true"/> when the operation is classified replay-safe.</returns>
    /// <remarks>
    /// <para>
    /// TWO INDEPENDENT ADMISSIONS, AND THE SECOND ONE EXISTS BECAUSE THE FIRST CANNOT SEE A REST GET.
    /// The path table is exhaustive for the operations this system NAMES - the unary reads and the
    /// idempotent teardowns on the two gRPC contracts, plus the pure-function crypto endpoints - and it
    /// is the only admission that can apply on a gRPC channel, where every call is an HTTP POST and the
    /// operation is therefore knowable only from the path. It is also, by construction, an enumeration:
    /// an operation it does not name is attempted exactly once.
    /// </para>
    /// <para>
    /// THAT ENUMERATION IS THE RIGHT DEFAULT FOR A POST AND THE WRONG ONE FOR A SAFE METHOD, because a
    /// safe method is not merely likely to be a read - RFC 9110 section 9.2.1 DEFINES it as one, and
    /// the definition is what the whole HTTP ecosystem already relies on when it caches, prefetches and
    /// replays such requests. Refusing to replay a GET therefore does not add safety; it withholds the
    /// one retry whose harmlessness is guaranteed by the transport rather than argued from a table. The
    /// concrete case is Security's verification-material fetch: it is a GET, it is a pure read of public
    /// key material, and it stays retryable while token issuance on the same channel - a POST, and the
    /// one call whose replay would record an issuance of a credential that was never delivered - does
    /// not. That method-scoped split is exactly the distinction an HTTP-verb-only gate gets right on the
    /// REST channel and catastrophically wrong on the gRPC channels; combining the two admissions is
    /// what makes one predicate correct on all three.
    /// </para>
    /// <para>
    /// AND THE SAFE-METHOD ARM CHANGES NOTHING ON A gRPC CHANNEL, which is the property that keeps the
    /// conservative classification intact where it matters. gRPC transports every call as a POST, so
    /// the arm is unreachable there and an unclassified RPC is still attempted exactly once.
    /// </para>
    /// <para>
    /// A REQUEST WITH NO READABLE PATH IS STILL REFUSED OUTRIGHT, ahead of both admissions. With no
    /// path there is no operation, and admitting one on the strength of its verb alone would replay
    /// whatever the unidentifiable call happened to be.
    /// </para>
    /// </remarks>
    private static bool IsReplaySafe(HttpRequestMessage? request)
    {
        if (request is null)
        {
            return false;
        }

        string? path = ReadPath(request.RequestUri);

        if (path is null)
        {
            return false;
        }

        return ReplaySafePaths.Contains(path) || IsSafeMethod(request.Method);
    }

    /// <summary>
    /// Answers whether a request method is safe in the sense RFC 9110 defines.
    /// </summary>
    /// <param name="method">The request method.</param>
    /// <returns><see langword="true"/> when the method's semantics are read-only.</returns>
    /// <remarks>
    /// EXACTLY THE FOUR RFC 9110 SECTION 9.2.1 SAFE METHODS AND NO OTHERS. PUT and DELETE are
    /// idempotent but NOT safe - repeating one is defined to leave the same end state, which is a
    /// statement about the state and not about the work, and neither belongs in a set whose members are
    /// admitted because their repetition changes nothing. Compared by reference against the singletons
    /// the framework interns, with an ordinal name comparison behind it because a handler further up a
    /// pipeline may have built the method from a string.
    /// </remarks>
    private static bool IsSafeMethod(HttpMethod method) =>
        method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options
            || method == HttpMethod.Trace
            || string.Equals(method.Method, "GET", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "HEAD", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(method.Method, "TRACE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the request path from a request URI of either form.
    /// </summary>
    /// <param name="requestUri">The URI, which may be absent, absolute or relative.</param>
    /// <returns>The path, or <see langword="null"/> when there is none to read.</returns>
    /// <remarks>
    /// A PREDICATE MUST NEVER THROW, WHICH IS THE ONLY REASON THIS IS NOT ONE PROPERTY ACCESS.
    /// <see cref="Uri.AbsolutePath"/> throws for a RELATIVE URI, and an exception raised inside a
    /// resilience predicate does not fail politely: it replaces the failure the pipeline was deciding
    /// about, so the caller is told about a policy fault instead of about its own upstream. In the
    /// deployed shape the URI is always absolute, because the client factory resolves it against the
    /// configured base address before the handler pipeline runs - so this arm is unreachable in
    /// production and is here because "unreachable" is not a property worth betting a thrown exception
    /// on. The relative form is truncated at a query or fragment so that both forms yield the same
    /// value for the same operation.
    /// </remarks>
    private static string? ReadPath(Uri? requestUri)
    {
        if (requestUri is null)
        {
            return null;
        }

        if (requestUri.IsAbsoluteUri)
        {
            return requestUri.AbsolutePath;
        }

        string original = requestUri.OriginalString;
        int boundary = original.AsSpan().IndexOfAny('?', '#');

        return boundary < 0 ? original : original[..boundary];
    }

    /// <summary>
    /// Reads the gRPC status from a response, when the response carries one where a message handler
    /// can still see it.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="status">Receives the status value.</param>
    /// <returns><see langword="true"/> when a status was read.</returns>
    /// <remarks>
    /// <para>
    /// BOTH PLACES ARE CHECKED, AND ONE OF THEM IS USUALLY EMPTY. A server that failed before writing
    /// any message answers trailers-only, and the status is then in the response HEADERS, which is
    /// where every authorization refusal, admission refusal and not-ready answer in this system
    /// appears. A server that had already begun writing puts the status in the TRAILERS, which are
    /// not populated until the body has been read to completion - so at the moment a message handler
    /// observes the response they are ordinarily absent. Reading both is therefore not redundancy: it
    /// is the difference between classifying the statuses that are observable and pretending none are.
    /// </para>
    /// <para>
    /// An unparseable value is treated as no value rather than as a status, so a malformed header
    /// falls through to the transport-level definition instead of silently becoming status zero, which
    /// would read as success.
    /// </para>
    /// </remarks>
    private static bool TryReadGrpcStatus(HttpResponseMessage response, out int status)
    {
        if (TryReadGrpcStatus(response.Headers, out status))
        {
            return true;
        }

        return TryReadGrpcStatus(response.TrailingHeaders, out status);
    }

    /// <summary>
    /// Reads the gRPC status from one header collection.
    /// </summary>
    /// <param name="headers">The collection to read.</param>
    /// <param name="status">Receives the status value.</param>
    /// <returns><see langword="true"/> when a status was read.</returns>
    private static bool TryReadGrpcStatus(HttpHeaders headers, out int status)
    {
        status = 0;

        if (!headers.TryGetValues(GrpcStatusHeaderName, out IEnumerable<string>? values))
        {
            return false;
        }

        foreach (string value in values)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
            {
                return true;
            }
        }

        status = 0;

        return false;
    }

    /// <summary>
    /// Builds the replay-safe path table from the contract descriptors.
    /// </summary>
    /// <returns>The set of absolute request paths whose attempts may be replayed.</returns>
    /// <exception cref="InvalidOperationException">
    /// A classified method does not exist on its contract, or exists but is a streaming method.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE C-03 ENTRIES. Five pure reads - the event gate and the four presentational model reads,
    /// each of which derives a model from state it does not change - plus the validation-session
    /// close, which <c>DataServicesClient.CloseValidationSessionAsync</c> documents as idempotent by
    /// contract precisely so that a caller which cannot safely repeat a close does not leak a session
    /// on a transport hiccup.
    /// </para>
    /// <para>
    /// THE C-04 ENTRIES. Four pure reads plus the expression-session close, which reports a session
    /// that had already gone without that being an error.
    /// </para>
    /// <para>
    /// WHAT IS ABSENT AND WHY, because the absences carry more of the policy than the entries do.
    /// Every <c>Open*</c> and <c>Create*</c> is excluded: a replayed open SUCCEEDS and leaves a second
    /// server-held session nobody holds a handle to, which is a leak rather than a duplicate. Every
    /// <c>Add*</c> is excluded because a repeated add binds a second expression or redeclares a
    /// variable. <c>Update</c> is excluded for the reason C-06 exists at all. <c>ApplyContextMenuModel</c>
    /// is excluded because whether it replaces or extends is a property of the REQUEST, and a message
    /// handler cannot inspect the request body to find out. The <c>Set*</c> and <c>Calc*</c> families
    /// are excluded because a recalculation can invoke macros back across the inverted channel, so a
    /// replay is observable in the caller's own macro handler as an invocation it never asked for.
    /// All five streaming methods are excluded by construction below.
    /// </para>
    /// <para>
    /// THE C-01 AND C-02 ENTRIES ARE REST PATHS, so they are literals: there is no descriptor to
    /// resolve them from, and the client that owns them declares the same paths. The thirteen crypto
    /// operations admitted are PURE FUNCTIONS of their request - hashing, keyed hashing, symmetric and
    /// RSA transforms, and encoding conversions - so a replay computes the same answer over the same
    /// input. Three sibling operations are excluded: RSA key generation, because it RETAINS the
    /// generated key against a bounded capacity and a replay consumes that capacity twice, and the
    /// three random generators, because each attempt yields different material by definition. Token
    /// issuance is excluded too: every mint is separately recorded as an issuance, so a replay leaves
    /// an audit record of a credential that was never delivered, and the credential cache means
    /// issuance is off the hot path for as long as a token is held.
    /// </para>
    /// </remarks>
    private static FrozenSet<string> BuildReplaySafePaths()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        AddContractMethods(
            paths,
            DataWindowService.Descriptor,
            [
                "GetEventGate",
                "GetDropDownSearchState",
                "GetColumnSortState",
                "GetContextMenuModel",
                "GetRowSelectState",
                "CloseValidationSession",
            ]);

        AddContractMethods(
            paths,
            ColumnExpressionService.Descriptor,
            [
                "GetExpression",
                "GetVariableExpression",
                "GetServiceState",
                "GetExpressionState",
                "CloseExpressionSession",
            ]);

        // The Security REST surface. Gateway itself calls only the issuance endpoint, which is
        // excluded; the crypto paths are classified here so that this table describes the whole
        // outbound surface of the named Security client rather than only the part Gateway happens to
        // exercise today, and so that a future consumer of that client inherits the classification
        // instead of inheriting the unclassified default.
        foreach (string path in
            (string[])
            [
                "/v1/crypto/hash",
                "/v1/crypto/hmac",
                "/v1/crypto/hash-file",
                "/v1/crypto/hmac-file",
                "/v1/crypto/symmetric/encrypt",
                "/v1/crypto/symmetric/decrypt",
                "/v1/crypto/rsa/encrypt",
                "/v1/crypto/rsa/decrypt",
                "/v1/crypto/rsa/sign",
                "/v1/crypto/rsa/verify",
                "/v1/crypto/encoding/string-to-blob",
                "/v1/crypto/encoding/blob-to-string",
                "/v1/crypto/encoding/blob-reverse",
            ])
        {
            paths.Add(path);
        }

        return FrozenSet.ToFrozenSet(paths, StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves each named method on a contract and adds its absolute request path.
    /// </summary>
    /// <param name="paths">The set being built.</param>
    /// <param name="service">The contract descriptor.</param>
    /// <param name="methodNames">The methods to classify as replay-safe.</param>
    /// <exception cref="InvalidOperationException">
    /// A named method does not exist on the contract, or is a streaming method.
    /// </exception>
    private static void AddContractMethods(
        HashSet<string> paths,
        ContractDescriptor service,
        string[] methodNames)
    {
        foreach (string methodName in methodNames)
        {
            MethodDescriptor? method = service.FindMethodByName(methodName);

            if (method is null)
            {
                throw new InvalidOperationException(
                    $"Contract '{service.FullName}' declares no method '{methodName}', so the "
                        + "outbound retry classification names an operation that no longer exists. "
                        + "Either the contract was changed without updating the classification, or "
                        + "the classification carries a typo - in both cases the operation would "
                        + "otherwise fall through to 'attempted exactly once' silently.");
            }

            if (method.IsClientStreaming || method.IsServerStreaming)
            {
                throw new InvalidOperationException(
                    $"'{service.FullName}/{methodName}' is a streaming method and cannot be "
                        + "classified replay-safe. Replaying a stream re-establishes it from the "
                        + "beginning, which delivers a message sequence that never existed to a "
                        + "consumer that has already seen part of the original.");
            }

            paths.Add(string.Concat("/", service.FullName, "/", method.Name));
        }
    }
}
