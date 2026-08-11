// =====================================================================================================
//  F-12 - RETRY SAFETY ON THE NETWORK EDGES DECOMPOSITION CREATES
// =====================================================================================================
//
//  WHY RETRIES ARE A CORRECTNESS PROBLEM AND NOT A TUNING ONE. A transport failure does not reveal
//  whether the server processed the request, so replaying a non-idempotent call can apply it TWICE. There
//  is no end-to-end idempotency key and no request deduplication anywhere in this system, so a replay has
//  nothing to make it safe. gRPC transports EVERY call as an HTTP POST, which means a retry policy left at
//  its default replays every method on all four Persistence contracts.
//
//  WHAT A DOUBLE APPLICATION ACTUALLY COSTS, contract by contract:
//    C-06 Update   - applies rows; a replay applies them again.
//    C-07 Exec     - runs an arbitrary statement; a replay runs it again.
//    C-08 Begin    - takes a REFERENCE-COUNTED pool reference; a replay leaks one PERMANENTLY, because the
//                    reference the abandoned attempt took has no handle anyone can release.
//    C-05 Query    - only LOOKS retryable. It carries the task lifecycle, and SetWhereClause under
//                    SQL_MS_APPEND appends the clause a SECOND time - so a replay produces a DIFFERENT
//                    statement rather than a duplicate one, which is the worse of the two failures because
//                    it returns a plausible wrong answer instead of an error.
//
//  THE TIMEOUT AND THE CIRCUIT BREAKER STAY. They are what the resilience package was added for: an
//  in-process call could not fail in transit and a network call can (AAP 0.5.3). No latency, throughput or
//  availability claim is made or implied - the repository publishes no such budget (AAP 0.8.5).
// =====================================================================================================

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices;
using Polly;
using Polly.Retry;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The retry-safety posture of every resilience pipeline this service registers.
/// </summary>
public sealed class ResilienceRetrySafetyTests
{
    /// <summary>
    /// No registered pipeline will replay an unsafe method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE TEST IS NAME-AGNOSTIC ON PURPOSE, AND THAT IS WHAT MAKES IT A REAL GUARD.</b> It does not
    /// name the five clients; it enumerates EVERY <c>HttpStandardResilienceOptions</c> configuration the
    /// composition registers and applies each to its own fresh instance. So a sixth client added later
    /// without the predicate fails this test the moment it is registered - which is precisely the failure
    /// mode a per-client test would miss, because nobody adds the test at the same time as the client.
    /// </para>
    /// <para>
    /// A POST returning 500 is a transient failure the default pipeline WOULD retry, so the assertion
    /// distinguishes a disabled predicate from a request that was never retryable anyway.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoRegisteredResiliencePipelineWillReplayAnUnsafeMethod()
    {
        IReadOnlyList<(string Name, HttpStandardResilienceOptions Options)> pipelines = Pipelines();

        // The composition registers five HTTP clients - four Persistence contracts and Security - so an
        // empty enumeration would silently pass and must itself be refused.
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
    /// Disabling the replay does not disable the resilience the package was added for.
    /// </summary>
    /// <remarks>
    /// <b>THE POINT OF WRITING IT AS A PREDICATE RATHER THAN A ZERO ATTEMPT COUNT.</b> A zero count would
    /// have recorded the consequence; the predicate records the REASON and leaves the configured attempt
    /// count meaningful for an edge whose methods are distinguishable. The timeout and the breaker are the
    /// half that handles a failure mode decomposition itself created, and they must survive.
    /// </remarks>
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
    /// A safe method is still retried, so the restriction is by method and not a blanket disable.
    /// </summary>
    /// <remarks>
    /// SECURITY IS THE ONE EDGE WHERE THIS IS OBSERVABLE, because it is reached over REST and its methods
    /// are therefore distinguishable: the verification-material fetch is a GET and is safely retryable,
    /// while token issuance is a POST and is not replayed. Asserting the GET half is what proves the
    /// restriction discriminates - a blanket disable would pass the POST assertion above and fail here.
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
    /// Materializes the EFFECTIVE resilience options of every named pipeline the composition registers.
    /// </summary>
    /// <returns>The named pipelines.</returns>
    /// <remarks>
    /// <para>
    /// <b>THE OPTIONS ARE COMPOSED PER NAME THROUGH THE OPTIONS MONITOR, NOT PER REGISTRATION.</b> An
    /// earlier form of this helper applied each registration to its own instance, and that was wrong in a
    /// way that FAILED rather than passing silently: the resilience library registers an ALL-NAMES
    /// configuration carrying its recommended defaults, which retries a POST, and only the per-client
    /// configuration then disables it. Judging registrations individually therefore judged half of a
    /// composition. Asking the monitor for each name reproduces exactly what the handler resolves at
    /// runtime, including ordering and post-configuration.
    /// </para>
    /// <para>
    /// THE NAMES ARE DISCOVERED RATHER THAN LISTED, so a client added later is covered without editing this
    /// file. They are read off the named registrations themselves; the all-names registration contributes
    /// no name, which is correct because it is not a pipeline of its own.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<(string Name, HttpStandardResilienceOptions Options)> Pipelines()
    {
        ServiceCollection services = new();
        _ = services.AddDataServicesClients();

        using ServiceProvider provider = services.BuildServiceProvider();

        SortedSet<string> names = [];

        foreach (IConfigureOptions<HttpStandardResilienceOptions> configure in provider
            .GetServices<IConfigureOptions<HttpStandardResilienceOptions>>())
        {
            // Read through the property rather than a cast: the library uses its own named-configuration
            // types alongside the framework's, and a cast to one concrete type would silently skip the
            // others - which is how a client would drop out of this suite without anything failing.
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
