// ==================================================================================================
//  PowerFramework.DataServices.Clients.OutboundGrpcCircuitBreaker
//  ------------------------------------------------------------------------------------------------
//  THE CIRCUIT BREAKER FOR THE FOUR gRPC CHANNELS, PLACED AT THE gRPC CALL LAYER RATHER THAN IN THE
//  HTTP MESSAGE HANDLER - AND THE PLACEMENT IS THE WHOLE POINT OF THE FILE.
//
//  🔴 WHAT WAS WRONG, MEASURED RATHER THAN REASONED. `Clients/OutboundCallPolicy.ShouldBreakAsync` is
//  a correct predicate attached to a pipeline that the failure never reaches. The Polly pipeline
//  installed by `AddStandardResilienceHandler()` is an HttpMessageHandler, and `Grpc.Net` establishes
//  its connection in the balancer's SUBCHANNEL TRANSPORT, which sits outside that handler - so a
//  refused socket, an unresolvable name or a reset connection is classified by nothing. The finding was
//  measured on the sibling edge - with the upstream stopped, 105 replay-safe requests all answered 502,
//  the next request still made four gRPC attempts over four seconds, and the pipeline emitted ZERO
//  circuit events - and a reachable server ANSWERING `Unavailable` did open the breaker, which is what
//  proved the split: response-level failures reached the handler and physical ones bypassed it
//  entirely. THIS EDGE HAS THE IDENTICAL DEFECT FOR THE IDENTICAL REASON, and it carries the update
//  contract, so it is the edge where an unprotected caller matters most. `Program.cs` already recorded
//  the mechanism in the banner above `ApplyGrpcRetry` and drew the retry conclusion from it while
//  leaving the breaker where it could not see the same event.
//
//  WHERE THIS SITS, AND WHY THAT GIVES THE PROPERTY THE BREAKER EXISTS FOR. A client interceptor wraps
//  the channel's own `CallInvoker`, so it is ABOVE the gRPC retry policy `ApplyGrpcRetry` installs.
//  An open circuit therefore refuses the call before the channel is asked for a subchannel at all:
//  no connect attempt, no retry cascade, no backoff wait. That is what "immediate refusal" means here,
//  and it is not achievable from inside the handler pipeline, which by definition only runs once a
//  call has already reached it.
//
//  THE HTTP-LEVEL BREAKER IS LEFT EXACTLY AS IT WAS. It is not redundant to keep it and it is not
//  load-bearing either: this breaker sees BOTH failure classes, because a server-declared status
//  arrives at the client as an `RpcException` just as a synthesized transport status does. Removing
//  the HTTP one would be an unrelated change to a working component, so it stays and its predicate
//  stays.
//
//  NO THRESHOLD IS INVENTED HERE, WHICH MATTERS BECAUSE A THRESHOLD IS AN AVAILABILITY POSTURE. AAP
//  0.8.5 forbids asserting one: the repository publishes no SLA, no latency budget and no throughput
//  target. `Program.cs` already resolved that by leaving the HTTP breaker's thresholds at the
//  resilience package's own defaults, and `OutboundBreakerThresholds.FromPackageDefaults` READS those
//  same four values rather than restating them, so the two breakers cannot drift and neither of them
//  is this repository's opinion about availability.
//
//  ONE BREAKER PER UPSTREAM, NOT PER CLIENT. All four gRPC clients address Persistence, and whether
//  Persistence is healthy is a property of Persistence - so a failure discovered by the query channel
//  is evidence for the update, command and transaction channels too. Four independent breakers would
//  each need the full failure threshold on its own traffic before any of them protected anything, and
//  the transaction channel in particular carries too little traffic to reach one on its own.
//
//  WHAT AN OPEN CIRCUIT ANSWERS, AND WHY IT IS INDISTINGUISHABLE FROM THE OUTAGE IT REPORTS. The
//  refusal is an `RpcException` carrying `Unavailable` WITH a debug exception attached, which is
//  precisely the shape this service's own `Endpoints/RestProjectionEndpoints` adjudication tests for -
//  a status the CLIENT synthesized from a failed transport is told apart from one the SERVER answered by
//  that exception's presence - and it is the same test Gateway applies one hop further out. A refused
//  call never reached Persistence, so the caller sees the same answer it saw before this file existed;
//  the difference is that it arrives immediately and the upstream is not touched.
// ==================================================================================================

using System.Collections.Frozen;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;

namespace PowerFramework.DataServices.Clients;

/// <summary>
/// The four values that define a circuit breaker's sampling window and its response to it.
/// </summary>
/// <param name="FailureRatio">The proportion of failures within the window that opens the circuit.</param>
/// <param name="MinimumThroughput">
/// How many actions must be sampled within the window before the ratio is consulted at all.
/// </param>
/// <param name="SamplingDuration">The window the ratio is computed over.</param>
/// <param name="BreakDuration">How long an opened circuit stays open before a trial call is admitted.</param>
/// <remarks>
/// A RECORD RATHER THAN FOUR LOOSE ARGUMENTS, so a test can state a fast window and production cannot
/// accidentally receive one. Every production construction goes through
/// <see cref="FromPackageDefaults"/>, which reads the resilience package's own numbers.
/// </remarks>
internal readonly record struct OutboundBreakerThresholds(
    double FailureRatio,
    int MinimumThroughput,
    TimeSpan SamplingDuration,
    TimeSpan BreakDuration)
{
    /// <summary>
    /// The thresholds the HTTP-level pipeline is already running with.
    /// </summary>
    /// <returns>The resilience package's own circuit-breaker defaults.</returns>
    /// <remarks>
    /// READ FROM THE PACKAGE, NOT TRANSCRIBED FROM ITS DOCUMENTATION. Transcribing them would make this
    /// repository the author of an availability posture it is not entitled to assert (AAP 0.8.5), and it
    /// would silently diverge from the HTTP breaker the first time the package changed a default. One
    /// default instance is constructed and its four values are copied; nothing else on it is used.
    /// </remarks>
    internal static OutboundBreakerThresholds FromPackageDefaults()
    {
        HttpCircuitBreakerStrategyOptions packaged = new HttpStandardResilienceOptions().CircuitBreaker;

        return new OutboundBreakerThresholds(
            packaged.FailureRatio,
            packaged.MinimumThroughput,
            packaged.SamplingDuration,
            packaged.BreakDuration);
    }
}

/// <summary>
/// Counts outbound gRPC failures against one upstream and refuses calls to it while the circuit is
/// open, including failures the HTTP message-handler pipeline never sees.
/// </summary>
/// <remarks>
/// THREAD-SAFE AND SHARED. One instance serves every gRPC client addressing the same upstream; Polly's
/// circuit-breaker strategy and its state provider are both safe for concurrent use.
/// </remarks>
internal sealed class OutboundGrpcCircuitBreaker
{
    /// <summary>
    /// The gRPC statuses that count as evidence the upstream cannot serve.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY NARROW, AND THE SAME SET THE HTTP-LEVEL PREDICATE ADMITS. A breaker that opens stops
    /// every call on the channel including the reads that would still have succeeded, so only the status
    /// that genuinely reports "cannot serve" is admitted. <c>ResourceExhausted</c> is excluded even
    /// though it is retryable - it is a healthy, deliberate admission refusal aimed at one caller's
    /// quota. <c>Aborted</c>, the authorization statuses and the validation statuses are excluded for the
    /// stronger reason that they are correct answers which happen to be refusals: counting them would let
    /// ordinary contested updates or one misconfigured caller trip the breaker for everybody.
    /// </remarks>
    internal static readonly FrozenSet<StatusCode> BreakingStatuses =
        FrozenSet.ToFrozenSet([StatusCode.Unavailable]);

    private readonly ResiliencePipeline _pipeline;
    private readonly CircuitBreakerStateProvider _state = new();
    private readonly string _upstream;

    /// <summary>
    /// Creates a breaker for one upstream.
    /// </summary>
    /// <param name="upstream">
    /// The upstream's name, used only in the refusal detail and in the circuit-event records. Never a
    /// URL, so no address is published into a caller-visible message.
    /// </param>
    /// <param name="thresholds">The sampling window and its response.</param>
    /// <param name="logger">Where circuit-state transitions are recorded, or null to record none.</param>
    /// <param name="manualControl">
    /// An optional manual control, so a test can isolate the circuit deterministically instead of having
    /// to manufacture a failure ratio in real time.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="upstream"/> is blank.</exception>
    internal OutboundGrpcCircuitBreaker(
        string upstream,
        OutboundBreakerThresholds thresholds,
        ILogger? logger = null,
        CircuitBreakerManualControl? manualControl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upstream);

        _upstream = upstream;

        CircuitBreakerStrategyOptions options = new()
        {
            FailureRatio = thresholds.FailureRatio,
            MinimumThroughput = thresholds.MinimumThroughput,
            SamplingDuration = thresholds.SamplingDuration,
            BreakDuration = thresholds.BreakDuration,
            StateProvider = _state,
            ManualControl = manualControl,

            // EXCEPTIONS ONLY, WHICH IS SUFFICIENT HERE AND IS NOT A SIMPLIFICATION. Every gRPC failure
            // reaches a client as an RpcException - the ones the server declared and the ones the client
            // synthesized from a dead transport alike - so an outcome-based predicate would have nothing
            // extra to judge. A successful call returns a response and is counted as a success by the
            // absence of an exception.
            ShouldHandle = static arguments =>
                new ValueTask<bool>(CountsAsUpstreamUnhealthy(arguments.Outcome.Exception)),

            // THE TRANSITIONS ARE RECORDED BECAUSE THEIR ABSENCE WAS PART OF THE FINDING: "emits zero
            // circuit events" is how the defect was detected, so a working breaker has to be visible to
            // the same observation. No payload, no address and no credential appears in any of them.
            OnOpened = arguments =>
            {
                logger?.LogError(
                    "Outbound circuit to {Upstream} OPENED for {BreakDuration} after {Outcome}. "
                        + "Calls are refused without reaching the upstream until it closes.",
                    upstream,
                    arguments.BreakDuration,
                    DescribeOutcome(arguments.Outcome.Exception));

                return default;
            },

            OnClosed = arguments =>
            {
                logger?.LogInformation(
                    "Outbound circuit to {Upstream} CLOSED after a successful trial call (manual: "
                        + "{Manual}).",
                    upstream,
                    arguments.IsManual);

                return default;
            },

            OnHalfOpened = _ =>
            {
                logger?.LogInformation(
                    "Outbound circuit to {Upstream} is HALF-OPEN and will admit one trial call.",
                    upstream);

                return default;
            },
        };

        _pipeline = new ResiliencePipelineBuilder().AddCircuitBreaker(options).Build();
    }

    /// <summary>The circuit's current state.</summary>
    internal CircuitState State => _state.CircuitState;

    /// <summary>Whether the circuit is currently refusing calls.</summary>
    internal bool IsRefusing => _state.CircuitState is CircuitState.Open or CircuitState.Isolated;

    /// <summary>
    /// Runs one unary call under the breaker, counting its outcome.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="start">
    /// Starts the call and answers its response task. Invoked INSIDE the breaker, so an open circuit
    /// never runs it at all.
    /// </param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="start"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">
    /// The circuit is open, or the call itself failed. An open circuit answers
    /// <see cref="StatusCode.Unavailable"/> with a debug exception attached.
    /// </exception>
    internal async Task<TResponse> GuardAsync<TResponse>(Func<Task<TResponse>> start)
    {
        ArgumentNullException.ThrowIfNull(start);

        try
        {
            return await _pipeline
                .ExecuteAsync(
                    static async (invoke, _) => await invoke().ConfigureAwait(false),
                    start)
                .ConfigureAwait(false);
        }
        catch (BrokenCircuitException refused)
        {
            throw RefusedByOpenCircuit(refused);
        }
    }

    /// <summary>
    /// Runs one blocking unary call under the breaker, counting its outcome.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="call">Invoked INSIDE the breaker.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="call"/> is <see langword="null"/>.</exception>
    /// <exception cref="RpcException">The circuit is open, or the call itself failed.</exception>
    internal TResponse Guard<TResponse>(Func<TResponse> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            return _pipeline.Execute(static (invoke, _) => invoke(), call);
        }
        catch (BrokenCircuitException refused)
        {
            throw RefusedByOpenCircuit(refused);
        }
    }

    /// <summary>
    /// Refuses a call while the circuit is open, for call shapes whose outcome this breaker does not
    /// sample.
    /// </summary>
    /// <exception cref="RpcException">The circuit is open.</exception>
    /// <remarks>
    /// <para>
    /// THE STREAMING SHAPES ARE GATED BUT NOT SAMPLED, AND THE ASYMMETRY IS DELIBERATE RATHER THAN
    /// UNFINISHED. A stream's outcome is not a single value: a duplex or client-streaming call reports
    /// its status only after the CLIENT has finished writing, so sampling one would attribute a caller's
    /// own pace - or its abandonment of the stream - to the upstream's health. Gating them still
    /// delivers the property the breaker exists for, because an open circuit is what must stop new work
    /// reaching a dead upstream, and the unary traffic that shares the channel is what discovers the
    /// outage in the first place.
    /// </para>
    /// <para>
    /// A HALF-OPEN CIRCUIT ADMITS A STREAM RATHER THAN RESERVING THE TRIAL FOR IT, because the trial
    /// call is what closes the circuit and only a sampled call can do that. Admitting the stream is the
    /// same answer the circuit would give a moment later if the trial succeeded, and refusing it would
    /// deny work while the circuit was already recovering.
    /// </para>
    /// </remarks>
    internal void ThrowIfRefusing()
    {
        if (IsRefusing)
        {
            throw RefusedByOpenCircuit(new BrokenCircuitException());
        }
    }

    /// <summary>
    /// Whether a failure is evidence that the upstream cannot serve.
    /// </summary>
    /// <param name="failure">The failure, or <see langword="null"/> for a successful outcome.</param>
    /// <returns><see langword="true"/> when it counts against the circuit.</returns>
    /// <remarks>
    /// A NON-gRPC EXCEPTION IS NOT COUNTED. Anything that is not an <see cref="RpcException"/> escaped
    /// from below the gRPC client - the credential fetch, a serialization fault, a caller's own
    /// cancellation - and none of those is a statement about the upstream's health. Counting them would
    /// let a Security outage or a caller's abandoned request open the circuit to Persistence, which is
    /// exactly the class of misattribution the status projections were already corrected for once.
    /// </remarks>
    private static bool CountsAsUpstreamUnhealthy(Exception? failure) =>
        failure is RpcException reported && BreakingStatuses.Contains(reported.StatusCode);

    /// <summary>
    /// Describes an outcome for a circuit-event record without quoting anything a caller supplied.
    /// </summary>
    /// <param name="failure">The failure, or <see langword="null"/>.</param>
    /// <returns>The gRPC status name, or the exception type name, or a fixed string.</returns>
    private static string DescribeOutcome(Exception? failure) => failure switch
    {
        RpcException reported => reported.StatusCode.ToString(),
        { } other => other.GetType().Name,
        _ => "an outcome carrying no exception",
    };

    /// <summary>
    /// Builds the refusal an open circuit answers.
    /// </summary>
    /// <param name="cause">The circuit-breaker exception, carried as the debug exception.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// THE DEBUG EXCEPTION IS LOAD-BEARING, NOT DIAGNOSTIC DECORATION. This service's own
    /// <c>Endpoints/RestProjectionEndpoints</c> tells a client-synthesized
    /// <see cref="StatusCode.Unavailable"/> from a server-declared one by its presence, and so does
    /// Gateway one hop further out. A refused call never reached the upstream, so it must carry one -
    /// without it, an open circuit would be reported as though Persistence had answered.
    /// </remarks>
    private RpcException RefusedByOpenCircuit(Exception cause) =>
        new(new Status(
            StatusCode.Unavailable,
            $"The outbound circuit to {_upstream} is open, so this call was refused without being "
                + "attempted. The upstream reported itself unable to serve, or could not be reached, "
                + "often enough within the sampling window to open it.",
            cause));
}

/// <summary>
/// Puts every call a gRPC client makes through <see cref="OutboundGrpcCircuitBreaker"/>.
/// </summary>
/// <param name="breaker">The breaker for the upstream this client addresses.</param>
/// <remarks>
/// REGISTERED WITH <c>AddInterceptor</c>, WHICH IS WHAT PUTS IT ABOVE THE RETRY LAYER. The client
/// factory composes interceptors around the channel's own <c>CallInvoker</c>, and the gRPC retry policy
/// lives inside that invoker - so this runs ONCE per logical call, and an open circuit refuses before
/// the channel is asked for a subchannel. An interceptor is also the only place a gRPC-shaped refusal
/// can be produced: below it there are HTTP messages, and above it there are the four typed Persistence
/// clients whose signatures this must not change.
/// </remarks>
internal sealed class OutboundGrpcCircuitBreakerInterceptor(OutboundGrpcCircuitBreaker breaker)
    : Interceptor
{
    /// <summary>
    /// The name the single gRPC upstream is recorded under.
    /// </summary>
    /// <remarks>
    /// THE SAME SPELLING THE PUBLISHED CONTRACTS' <c>upstream</c> MEMBER USES, so an operator reading a
    /// circuit event and a caller reading the projected failure are naming the same service. It is a name
    /// and never an address: an address in a caller-visible message publishes topology.
    /// </remarks>
    internal const string PersistenceUpstream = "persistence";

    private readonly OutboundGrpcCircuitBreaker _breaker = breaker
        ?? throw new ArgumentNullException(nameof(breaker));

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        return _breaker.Guard(() => continuation(request, context));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        // THE INNER CALL IS CAPTURED FROM INSIDE THE BREAKER, because on an open circuit it is never
        // created at all - which is the whole point - and the four accessors below therefore have to
        // answer for a call that may not exist.
        InnerCall<TResponse> inner = new();

        Task<TResponse> response = _breaker.GuardAsync(() =>
        {
            AsyncUnaryCall<TResponse> started = continuation(request, context);

            inner.Started = started;

            return started.ResponseAsync;
        });

        return new AsyncUnaryCall<TResponse>(
            response,

            // THE HEADER TASK IS THE CONSTRUCTOR'S SECOND PARAMETER AND IS A TASK RATHER THAN A FACTORY,
            // so it is started here. It completes successfully even for a refused or failed call, which
            // is what keeps it from becoming an unobserved fault when a caller only awaits the response.
            ReadHeadersAsync(response, inner),
            () => inner.Started is { } started ? started.GetStatus() : RefusedStatus(response),
            () => inner.Started is { } started ? started.GetTrailers() : new Metadata(),
            () => inner.Started?.Dispose());
    }

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _breaker.ThrowIfRefusing();

        return continuation(request, context);
    }

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _breaker.ThrowIfRefusing();

        return continuation(context);
    }

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);

        _breaker.ThrowIfRefusing();

        return continuation(context);
    }

    /// <summary>
    /// Answers the started call's response headers, or an empty set when the circuit refused the call.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The guarded response task.</param>
    /// <param name="inner">The holder the started call was captured into.</param>
    /// <returns>The headers.</returns>
    /// <remarks>
    /// THE RESPONSE TASK IS AWAITED FIRST AND ITS FAULT IS SWALLOWED HERE, which is the only way to know
    /// whether a call was ever started without racing the breaker. The fault is not lost: it is the
    /// response task's own, and every caller of a unary call awaits that. A refused call has no headers
    /// and an empty set is the truthful answer - the alternative would be to fabricate a
    /// <c>grpc-status</c> the upstream never sent.
    /// </remarks>
    private static async Task<Metadata> ReadHeadersAsync<TResponse>(
        Task<TResponse> response,
        InnerCall<TResponse> inner)
    {
        try
        {
            _ = await response.ConfigureAwait(false);
        }
        catch (RpcException)
        {
            // The response task carries it to the caller; this accessor only needs to know the call
            // settled.
        }

        return inner.Started is { } started
            ? await started.ResponseHeadersAsync.ConfigureAwait(false)
            : new Metadata();
    }

    /// <summary>
    /// The status of a call the circuit refused.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="response">The guarded response task, whose fault carries the real status.</param>
    /// <returns>The refusal status.</returns>
    private static Status RefusedStatus<TResponse>(Task<TResponse> response) =>
        response.Exception?.InnerExceptions.OfType<RpcException>().FirstOrDefault() is { } refused
            ? refused.Status
            : new Status(StatusCode.Unavailable, "The call was refused by an open outbound circuit.");

    /// <summary>
    /// Holds the started call so the deferred accessors can reach it.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <remarks>
    /// A CLASS RATHER THAN A CLOSED-OVER LOCAL, so the assignment made inside the breaker is visible to
    /// the accessors afterwards without either of them capturing a mutable local by reference.
    /// </remarks>
    private sealed class InnerCall<TResponse>
    {
        internal AsyncUnaryCall<TResponse>? Started { get; set; }
    }
}
