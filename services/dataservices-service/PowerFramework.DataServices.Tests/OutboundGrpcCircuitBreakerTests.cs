// ==================================================================================================
//  PowerFramework.DataServices.Tests.OutboundGrpcCircuitBreakerTests
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   That a PHYSICAL transport failure counts against the circuit, that an open
//  circuit refuses the next call WITHOUT STARTING IT, and that the refusal carries the shape Gateway's
//  own status adjudication needs. Clients/OutboundGrpcCircuitBreaker.cs.
//
//  WHY IT EXISTS, AND WHY THE SIBLING SUITE COULD NOT CATCH THIS. OutboundCallPolicyTests asserts that
//  `ShouldBreakAsync` classifies correctly and that Program installs it - both true, and both
//  irrelevant to a failure that never reaches the pipeline the predicate is installed on. Grpc.Net
//  establishes its connections in the balancer's subchannel transport, outside the HttpMessageHandler
//  chain, so a refused socket was judged by nothing: with the upstream stopped, 105 replay-safe requests
//  all answered 502, the following request still made four gRPC attempts over four seconds, and ZERO
//  circuit events were emitted on the sibling edge, and this edge had the identical defect. The assertion
//  that closes the gap has to be about the CALL layer, which is what this suite tests.
//
//  🔴 THE LOAD-BEARING ASSERTION IS "WITHOUT STARTING IT". Counting the failure is the easy half; the
//  property the breaker exists for is that an open circuit does not touch the upstream at all. Every
//  refusal case below therefore counts continuation invocations and asserts the count did not move -
//  a test that only asserted the status would pass on a breaker that refused after attempting.
//
//  REAL TIME, BOUNDED AND TINY. Polly's builder does not expose its TimeProvider, so the sampling and
//  break windows here are real - which is why they are the library's own minimums and why the recovery
//  case is driven by the manual control rather than by waiting out a break duration.
// ==================================================================================================

using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Http.Resilience;
using Polly.CircuitBreaker;
using PowerFramework.DataServices.Clients;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class OutboundGrpcCircuitBreakerTests
{
    /// <summary>
    /// Two failures inside a one-second window at a half ratio - the library's own minimum throughput
    /// and minimum sampling duration, so the suite is as fast as Polly permits and no faster.
    /// </summary>
    private static OutboundBreakerThresholds FastThresholds => new(
        FailureRatio: 0.5,
        MinimumThroughput: 2,
        SamplingDuration: TimeSpan.FromSeconds(1),
        BreakDuration: TimeSpan.FromSeconds(30));

    private static Method<string, string> UnaryMethod => new(
        MethodType.Unary,
        "powerframework.test.Service",
        "Unary",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    private static Method<string, string> ServerStreamingMethod => new(
        MethodType.ServerStreaming,
        "powerframework.test.Service",
        "ServerStreaming",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    /// <summary>
    /// A refused socket - the failure class the HTTP-level pipeline never sees - opens the circuit, and
    /// the call after that is refused without being started.
    /// </summary>
    [Fact]
    public async Task APhysicalTransportFailureOpensTheCircuitAndTheNextCallIsNotEvenAttempted()
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);
        int attempts = 0;

        for (int failure = 0; failure < 2; failure++)
        {
            _ = await Assert.ThrowsAsync<RpcException>(() => breaker.GuardAsync<string>(() =>
            {
                attempts++;

                throw SynthesizedTransportFailure();
            }));
        }

        Assert.Equal(2, attempts);
        Assert.Equal(CircuitState.Open, breaker.State);
        Assert.True(breaker.IsRefusing);

        RpcException refused = await Assert.ThrowsAsync<RpcException>(
            () => breaker.GuardAsync<string>(() =>
            {
                attempts++;

                return Task.FromResult("never reached");
            }));

        // THE ASSERTION THE WHOLE FILE EXISTS FOR: the upstream was not touched.
        Assert.Equal(2, attempts);
        Assert.Equal(StatusCode.Unavailable, refused.StatusCode);

        // AND THE SHAPE THE STATUS ADJUDICATIONS READ - this service's own and Gateway's one hop out: a
        // client-synthesized Unavailable carries a debug exception, a server-declared one does not, and the
        // two are projected to different HTTP statuses because they mean different things.
        Assert.IsType<BrokenCircuitException>(refused.Status.DebugException);
        Assert.Contains("persistence", refused.Status.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// A status the SERVER declared counts too, so the one breaker covers both failure classes and the
    /// HTTP-level breaker is not load-bearing.
    /// </summary>
    [Fact]
    public async Task AServerDeclaredUnavailableAlsoOpensTheCircuit()
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);

        for (int failure = 0; failure < 2; failure++)
        {
            _ = await Assert.ThrowsAsync<RpcException>(() => breaker.GuardAsync<string>(
                () => throw new RpcException(new Status(StatusCode.Unavailable, "shutting down"))));
        }

        Assert.Equal(CircuitState.Open, breaker.State);
    }

    /// <summary>
    /// A deliberate refusal is a correct answer and must not break the channel for everybody.
    /// </summary>
    /// <param name="declined">The status the upstream answered.</param>
    [Theory]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.ResourceExhausted)]
    [InlineData(StatusCode.InvalidArgument)]
    [InlineData(StatusCode.NotFound)]
    public async Task ADeliberateRefusalNeverOpensTheCircuit(StatusCode declined)
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);

        for (int refusal = 0; refusal < 6; refusal++)
        {
            _ = await Assert.ThrowsAsync<RpcException>(() => breaker.GuardAsync<string>(
                () => throw new RpcException(new Status(declined, "a correct answer"))));
        }

        Assert.Equal(CircuitState.Closed, breaker.State);
        Assert.False(breaker.IsRefusing);
    }

    /// <summary>
    /// A failure that is not a gRPC failure says nothing about the upstream's health.
    /// </summary>
    /// <remarks>
    /// THE CONCRETE CASE IS THE CREDENTIAL EDGE. Every outbound call fetches a token from Security
    /// first, so a Security outage surfaces here as a bare transport exception - and counting it would
    /// open the circuit to Persistence for a fault in a different service, which is exactly the
    /// misattribution the 502 body was already corrected for once.
    /// </remarks>
    [Fact]
    public async Task ANonGrpcFailureIsNotEvidenceAboutTheUpstream()
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);

        for (int failure = 0; failure < 6; failure++)
        {
            _ = await Assert.ThrowsAsync<HttpRequestException>(() => breaker.GuardAsync<string>(
                () => throw new HttpRequestException("Name or service not known (security-service:5104)")));
        }

        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    /// <summary>A successful call passes its response through unchanged and keeps the circuit closed.</summary>
    [Fact]
    public async Task ASuccessfulCallPassesThroughAndKeepsTheCircuitClosed()
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);

        Assert.Equal("ok", await breaker.GuardAsync(() => Task.FromResult("ok")));
        Assert.Equal(CircuitState.Closed, breaker.State);
    }

    /// <summary>
    /// The interceptor refuses a unary call while the circuit is isolated, and the refusal never reaches
    /// the continuation - which is where the channel would have asked for a subchannel.
    /// </summary>
    [Fact]
    public async Task TheInterceptorRefusesAUnaryCallWithoutInvokingTheContinuation()
    {
        CircuitBreakerManualControl control = new();
        OutboundGrpcCircuitBreaker breaker = new(
            "persistence",
            FastThresholds,
            logger: null,
            manualControl: control);

        await control.IsolateAsync(TestContext.Current.CancellationToken);

        OutboundGrpcCircuitBreakerInterceptor interceptor = new(breaker);
        int attempts = 0;

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            new ClientInterceptorContext<string, string>(UnaryMethod, host: null, new CallOptions()),
            (_, _) =>
            {
                attempts++;

                return SuccessfulCall("never reached");
            });

        RpcException refused = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        Assert.Equal(0, attempts);
        Assert.Equal(StatusCode.Unavailable, refused.StatusCode);
        Assert.Equal(StatusCode.Unavailable, call.GetStatus().StatusCode);

        // The deferred accessors must answer rather than throw for a call that was never started.
        Assert.Empty(await call.ResponseHeadersAsync);
        Assert.Empty(call.GetTrailers());

        call.Dispose();
    }

    /// <summary>
    /// A closed circuit leaves a unary call completely untouched, including its deferred accessors.
    /// </summary>
    [Fact]
    public async Task TheInterceptorIsTransparentWhileTheCircuitIsClosed()
    {
        OutboundGrpcCircuitBreaker breaker = new("persistence", FastThresholds);
        OutboundGrpcCircuitBreakerInterceptor interceptor = new(breaker);

        AsyncUnaryCall<string> call = interceptor.AsyncUnaryCall(
            "request",
            new ClientInterceptorContext<string, string>(UnaryMethod, host: null, new CallOptions()),
            (_, _) => SuccessfulCall("answered"));

        Assert.Equal("answered", await call.ResponseAsync);
        Assert.Equal(StatusCode.OK, call.GetStatus().StatusCode);
        Assert.Empty(await call.ResponseHeadersAsync);

        call.Dispose();
    }

    /// <summary>
    /// A streaming call is GATED by an open circuit even though its outcome is not sampled.
    /// </summary>
    [Fact]
    public async Task TheInterceptorGatesAStreamingCallWhileTheCircuitIsOpen()
    {
        CircuitBreakerManualControl control = new();
        OutboundGrpcCircuitBreaker breaker = new(
            "persistence",
            FastThresholds,
            logger: null,
            manualControl: control);

        await control.IsolateAsync(TestContext.Current.CancellationToken);

        OutboundGrpcCircuitBreakerInterceptor interceptor = new(breaker);
        int attempts = 0;

        RpcException refused = Assert.Throws<RpcException>(() => interceptor.AsyncServerStreamingCall(
            "request",
            new ClientInterceptorContext<string, string>(
                ServerStreamingMethod,
                host: null,
                new CallOptions()),
            (_, _) =>
            {
                attempts++;

                throw new InvalidOperationException("never reached");
            }));

        Assert.Equal(0, attempts);
        Assert.Equal(StatusCode.Unavailable, refused.StatusCode);
    }

    /// <summary>
    /// Closing the circuit lets calls through again, which is what makes the breaker a bound on a failing
    /// upstream rather than a permanent amputation of it.
    /// </summary>
    [Fact]
    public async Task ClosingTheCircuitAdmitsCallsAgain()
    {
        CircuitBreakerManualControl control = new();
        OutboundGrpcCircuitBreaker breaker = new(
            "persistence",
            FastThresholds,
            logger: null,
            manualControl: control);

        await control.IsolateAsync(TestContext.Current.CancellationToken);
        Assert.True(breaker.IsRefusing);

        await control.CloseAsync(TestContext.Current.CancellationToken);

        Assert.False(breaker.IsRefusing);
        Assert.Equal("recovered", await breaker.GuardAsync(() => Task.FromResult("recovered")));
    }

    /// <summary>
    /// The thresholds are the resilience package's own, so the two breakers on one channel cannot drift
    /// and neither of them is this repository asserting an availability posture (AAP 0.8.5).
    /// </summary>
    [Fact]
    public void TheThresholdsAreReadFromThePackageRatherThanRestated()
    {
        HttpCircuitBreakerStrategyOptions packaged = new HttpStandardResilienceOptions().CircuitBreaker;
        OutboundBreakerThresholds resolved = OutboundBreakerThresholds.FromPackageDefaults();

        Assert.Equal(packaged.FailureRatio, resolved.FailureRatio);
        Assert.Equal(packaged.MinimumThroughput, resolved.MinimumThroughput);
        Assert.Equal(packaged.SamplingDuration, resolved.SamplingDuration);
        Assert.Equal(packaged.BreakDuration, resolved.BreakDuration);
    }

    /// <summary>
    /// The status set is the same one the HTTP-level predicate admits - narrow on purpose, because an open
    /// circuit denies the reads that were still working.
    /// </summary>
    [Fact]
    public void OnlyTheCannotServeStatusCountsAgainstTheCircuit()
    {
        Assert.Equal([StatusCode.Unavailable], OutboundGrpcCircuitBreaker.BreakingStatuses.ToArray());
    }

    /// <summary>A transport failure as Grpc.Net synthesizes one: Unavailable with the cause attached.</summary>
    /// <returns>The failure.</returns>
    private static RpcException SynthesizedTransportFailure() => new(new Status(
        StatusCode.Unavailable,
        "Error connecting to subchannel.",
        new HttpRequestException("Connection refused (persistence-service:5101)")));

    /// <summary>A completed unary call, as a continuation would answer one.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The call.</returns>
    private static AsyncUnaryCall<string> SuccessfulCall(string response) => new(
        Task.FromResult(response),
        Task.FromResult(new Metadata()),
        static () => Status.DefaultSuccess,
        static () => new Metadata(),
        static () => { });
}
