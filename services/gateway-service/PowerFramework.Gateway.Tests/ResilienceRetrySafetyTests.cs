// =====================================================================================================
//  F-12 - RETRY SAFETY ON GATEWAY'S THREE OUTBOUND EDGES
// =====================================================================================================
//
//  WHY REPLAYING IS A CORRECTNESS PROBLEM HERE. A transport failure does not reveal whether the server
//  processed the request, and there is no end-to-end idempotency key or request deduplication anywhere in
//  this system, so a replay of a non-idempotent call can apply the work TWICE. gRPC transports every call
//  as an HTTP POST, so a default retry policy replays every method on both DataServices contracts.
//
//  WHAT A REPLAY WOULD COST, edge by edge:
//    C-03 DataWindowService        - Update applies rows, and the validation event chain carries ordered
//                                   state, so a replay applies rows again or re-enters the chain.
//    C-04 ColumnExpressionService  - carries the two inverted duplex streams AND the expression mutators;
//                                   adding a variable or setting one with recalculation CHANGES the
//                                   environment a later calculation reads, so a replay silently alters a
//                                   result rather than duplicating a request.
//    Security                      - the ONE edge with a genuine split, because it is REST and its methods
//                                   are distinguishable: the JWKS fetch is a GET and is safely retryable,
//                                   while token issuance is a POST and is not replayed.
//
//  THE TIMEOUT AND CIRCUIT BREAKER STAY - they are what the package was added for, because an in-process
//  call could not fail in transit and a network call can (AAP 0.5.3). No latency, throughput or
//  availability claim is made or implied (AAP 0.8.5).
//
//  THIS SUITE READS THE REAL RUNNING HOST rather than a reconstructed service collection, because
//  Gateway's clients are registered in top-level statements: the host IS the registration.
// =====================================================================================================

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The retry-safety posture of every resilience pipeline the composed host registers.
/// </summary>
/// <param name="host">The shared in-process host.</param>
public sealed class ResilienceRetrySafetyTests(GatewayTestHostFixture host)
    : IClassFixture<GatewayTestHostFixture>
{
    private readonly GatewayTestHostFixture _host = host;

    /// <summary>
    /// No registered pipeline will replay an unsafe method.
    /// </summary>
    /// <remarks>
    /// <b>NAME-AGNOSTIC ON PURPOSE, WHICH IS WHAT MAKES IT A GUARD RATHER THAN A RESTATEMENT.</b> It does
    /// not name the three clients; it discovers every named pipeline the host composed. A fourth outbound
    /// edge added later without the predicate fails this test the moment it is registered - the failure
    /// mode a per-client test misses, because nobody adds the test at the same time as the client.
    /// </remarks>
    [Fact]
    public async Task NoRegisteredResiliencePipelineWillReplayAnUnsafeMethod()
    {
        IReadOnlyList<(string Name, HttpStandardResilienceOptions Options)> pipelines = Pipelines();

        // Three outbound clients are composed - Security, C-03 and C-04 - so an empty enumeration would
        // pass silently and must itself be refused.
        Assert.NotEmpty(pipelines);

        foreach ((string name, HttpStandardResilienceOptions options) in pipelines)
        {
            Assert.False(
                await WouldRetryAsync(options, HttpMethod.Post),
                $"The pipeline configured as '{name}' would replay a POST. gRPC sends every call as a "
                    + "POST, so this replays non-idempotent work with no deduplication behind it.");
        }
    }

    /// <summary>
    /// Restricting the replay does not remove the resilience the package was added for.
    /// </summary>
    [Fact]
    public void TheTimeoutAndCircuitBreakerSurviveTheRetryRestriction()
    {
        foreach ((string name, HttpStandardResilienceOptions options) in Pipelines())
        {
            Assert.True(
                options.TotalRequestTimeout.Timeout > TimeSpan.Zero,
                $"The pipeline configured as '{name}' lost its total request timeout.");

            Assert.True(
                options.CircuitBreaker.MinimumThroughput > 0,
                $"The pipeline configured as '{name}' lost its circuit breaker.");

            Assert.True(
                options.Retry.MaxRetryAttempts > 0,
                $"The pipeline configured as '{name}' zeroed its attempt count instead of restricting the "
                    + "methods it applies to, which records the consequence rather than the reason.");
        }
    }

    /// <summary>
    /// A safe method is still retried, so the restriction discriminates by method.
    /// </summary>
    /// <remarks>
    /// SECURITY IS WHERE THIS IS OBSERVABLE: its verification-material fetch is a GET and stays retryable
    /// while its token issuance POST does not. A blanket disable would satisfy the POST assertion above and
    /// fail here, which is exactly the distinction worth one test.
    /// </remarks>
    [Fact]
    public async Task ASafeMethodIsStillRetried()
    {
        bool anySafeMethodRetried = false;

        foreach ((_, HttpStandardResilienceOptions options) in Pipelines())
        {
            anySafeMethodRetried |= await WouldRetryAsync(options, HttpMethod.Get);
        }

        Assert.True(
            anySafeMethodRetried,
            "Every pipeline refused to retry a GET, which means the restriction is a blanket disable "
                + "rather than the method-scoped predicate it is documented to be.");
    }

    /// <summary>
    /// Materializes the EFFECTIVE options of every named pipeline the host composed.
    /// </summary>
    /// <returns>The named pipelines.</returns>
    /// <remarks>
    /// <b>COMPOSED PER NAME THROUGH THE OPTIONS MONITOR, NOT PER REGISTRATION.</b> The resilience library
    /// registers an ALL-NAMES configuration carrying its recommended defaults, and those defaults DO retry
    /// a POST; only the per-client configuration then disables it. Judging registrations one at a time would
    /// therefore judge half of a composition. Asking the monitor per name reproduces what the handler
    /// resolves at runtime, ordering and post-configuration included.
    /// </remarks>
    private IReadOnlyList<(string Name, HttpStandardResilienceOptions Options)> Pipelines()
    {
        IServiceProvider provider = _host.Services;

        SortedSet<string> names = [];

        foreach (IConfigureOptions<HttpStandardResilienceOptions> configure in provider
            .GetServices<IConfigureOptions<HttpStandardResilienceOptions>>())
        {
            // Read through the property rather than a cast: the library uses its own named-configuration
            // types alongside the framework's, and a cast to one concrete type would silently skip the
            // others - which is how an edge would drop out of this suite without anything failing.
            if (configure.GetType().GetProperty("Name")?.GetValue(configure) is string name
                && !string.IsNullOrEmpty(name))
            {
                _ = names.Add(name);
            }
        }

        IOptionsMonitor<HttpStandardResilienceOptions> monitor =
            provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();

        return [.. names.Select(name => (name, monitor.Get(name)))];
    }

    /// <summary>Asks a pipeline whether it would retry a transient failure of the given method.</summary>
    /// <param name="options">The pipeline.</param>
    /// <param name="method">The request method.</param>
    /// <returns>Whether a retry would be attempted.</returns>
    private static async Task<bool> WouldRetryAsync(
        HttpStandardResilienceOptions options,
        HttpMethod method)
    {
        using HttpRequestMessage request = new(method, "http://upstream/probe");

        // 500 is a transient failure the default predicate DOES retry, so a false answer here is the
        // restriction taking effect rather than the response having been unretryable to begin with.
        using HttpResponseMessage response = new(HttpStatusCode.InternalServerError)
        {
            RequestMessage = request,
        };

        ResilienceContext context =
            ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);

        try
        {
            return await options.Retry.ShouldHandle(
                new RetryPredicateArguments<HttpResponseMessage>(
                    context,
                    Outcome.FromResult(response),
                    attemptNumber: 0));
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
