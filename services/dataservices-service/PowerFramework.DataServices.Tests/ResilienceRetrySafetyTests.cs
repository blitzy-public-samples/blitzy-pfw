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

using System.Globalization;
using System.Net;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.Persistence.V1;
using PowerFramework.DataServices;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Xunit;

// ALIASED FOR THE SAME REASON THE PRODUCTION FILE ALIASES IT. Implicit usings bring in
// Microsoft.Extensions.DependencyInjection, whose ServiceDescriptor describes a container registration
// rather than a gRPC contract, so the bare name is ambiguous (CS0104).
using ContractDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;
using MethodDescriptor = Google.Protobuf.Reflection.MethodDescriptor;

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

    // ==============================================================================================
    //  F-10 - THE LAYER THAT ACTUALLY SEES A CONNECT FAILURE
    //
    //  Everything above this line asserts the HTTP-level pipeline. That pipeline is attached to the
    //  HttpClient's message handler, and Grpc.Net establishes its connection in the BALANCER's subchannel
    //  transport, which runs OUTSIDE the handler - so on the four Persistence channels the single most
    //  likely transient fault in a decomposed system, "the upstream is down", was never presented to the
    //  retry predicate at all. It failed in SocketConnectivitySubchannelTransport.TryConnectAsync, and the
    //  measurement on the sibling Gateway edge was unambiguous: a replay-safe read answered in a quarter of
    //  a second against three configured attempts, while the same log showed Polly executing for the REST
    //  client. The tests below assert the second retry layer that fixes it, and assert that the two layers
    //  agree about which methods may be replayed rather than each keeping its own opinion.
    // ==============================================================================================

    /// <summary>
    /// The four Persistence pipelines stand HTTP-level retry down by identity; the Security one keeps it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASSERTED BY IDENTITY RATHER THAN BY BEHAVIOUR, because the two predicates agree on almost every
    /// input: a POST to an unclassified path is refused by both. Comparing the installed delegate against
    /// the named method is what distinguishes "retry was deliberately moved to the gRPC layer" from "the
    /// operation-scoped predicate happened to say no", and only the first of those survives a method being
    /// added to the replay-safe roster later.
    /// </para>
    /// <para>
    /// THE DIVISION OF LABOUR IS THE POINT. Both layers retrying MULTIPLIES rather than adds - four
    /// attempts at each makes sixteen calls against an upstream that is by definition already struggling -
    /// so exactly one layer may retry a gRPC call, and it is the better-informed one. The Security edge is
    /// REST, has no service config and therefore no other mechanism, so it keeps the operation-scoped
    /// predicate and its thirteen crypto path classifications stay live.
    /// </para>
    /// <para>
    /// THE BREAKER IS ASSERTED ON EVERY PIPELINE WITHOUT EXCEPTION, because standing retry down must not
    /// stand down the protection an upstream gets from a caller that keeps trying.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheGrpcChannelsStandDownHttpRetryByIdentityAndTheRestEdgeKeepsIt()
    {
        ServiceCollection services = new();
        _ = services.AddDataServicesClients();

        using ServiceProvider provider = services.BuildServiceProvider();

        IReadOnlySet<string> grpcClientNames = GrpcClientNames(provider);

        // Four generated clients address the four Persistence contracts. An empty set would make every
        // assertion below vacuous, and the count is stated so a fifth or a lost one is a failure here.
        Assert.Equal(4, grpcClientNames.Count);

        IReadOnlyList<(string Name, HttpStandardResilienceOptions Options)> pipelines = Pipelines();

        Assert.NotEmpty(pipelines);

        int restEdges = 0;

        // THE NAMES ARE NOT INTERCHANGEABLE, and the difference is the resilience package's own
        // convention: a standard handler's pipeline is named for its HttpClient with a `-standard`
        // suffix, while a gRPC client registration is named for its client type. Comparing the two
        // spellings directly put every gRPC pipeline in the REST branch, which is a fault this test
        // caught in itself before it could assert anything about the composition.
        Assert.All(
            pipelines,
            entry => Assert.EndsWith(PipelineSuffix, entry.Name, StringComparison.Ordinal));

        foreach ((string name, HttpStandardResilienceOptions options) in pipelines)
        {
            if (grpcClientNames.Contains(name) || grpcClientNames.Contains(StripPipelineSuffix(name)))
            {
                Assert.Equal(
                    (Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>>)
                        OutboundCallPolicy.NeverRetryAtTheHttpLayerAsync,
                    options.Retry.ShouldHandle);
            }
            else
            {
                restEdges++;

                Assert.Equal(
                    (Func<RetryPredicateArguments<HttpResponseMessage>, ValueTask<bool>>)
                        OutboundCallPolicy.ShouldRetryAsync,
                    options.Retry.ShouldHandle);
            }

            Assert.Equal(
                (Func<CircuitBreakerPredicateArguments<HttpResponseMessage>, ValueTask<bool>>)
                    OutboundCallPolicy.ShouldBreakAsync,
                options.CircuitBreaker.ShouldHandle);
        }

        // Security is the only REST edge, and it is the reason the operation-scoped predicate must not be
        // deleted along with its use on the gRPC channels.
        Assert.Equal(1, restEdges);
    }

    /// <summary>
    /// The gRPC retry configuration names EXACTLY the methods the HTTP-level predicate would replay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THE ONE ASSERTION THAT STOPS THE TWO LAYERS DRIFTING. Each layer classifies differently - the
    /// HTTP one by absolute request path, the gRPC one by service and method name - so nothing about the
    /// code makes them agree except that both read the same rosters. This test checks the agreement the way
    /// it matters: it walks EVERY method of all four contracts and asserts that the gRPC configuration
    /// names it if and only if the HTTP predicate would admit a replay of it. A method admitted at one
    /// layer only is a double-apply on one path and a lost retry on the other, and neither shows up as a
    /// failure anywhere else in this suite.
    /// </para>
    /// <para>
    /// IT IS ALSO A CENSUS. Because it enumerates the descriptors rather than a list, a method added to a
    /// contract later is covered without editing this file: unclassified, it must be absent from the
    /// configuration, which is the safe default this policy is built on.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheGrpcRetryConfigurationNamesExactlyTheMethodsTheHttpPredicateAdmits()
    {
        ServiceConfig config = OutboundCallPolicy.BuildRetryServiceConfig(
            maxAttempts: 4,
            initialBackoff: TimeSpan.FromMilliseconds(10));

        HashSet<string> named = new(StringComparer.Ordinal);

        foreach (MethodConfig methodConfig in config.MethodConfigs)
        {
            foreach (MethodName method in methodConfig.Names)
            {
                Assert.True(
                    named.Add($"{method.Service}/{method.Method}"),
                    $"'{method.Service}/{method.Method}' is configured twice, so one configuration is "
                        + "silently shadowing the other.");
            }
        }

        HashSet<string> admitted = new(StringComparer.Ordinal);

        foreach (ContractDescriptor descriptor in (ContractDescriptor[])
            [
                QueryService.Descriptor,
                UpdateService.Descriptor,
                CommandService.Descriptor,
                TransactionService.Descriptor,
            ])
        {
            foreach (MethodDescriptor method in descriptor.Methods)
            {
                if (await WouldReplayGrpcMethodAsync(descriptor, method))
                {
                    _ = admitted.Add($"{descriptor.FullName}/{method.Name}");
                }
            }
        }

        // Stated rather than merely compared, so an empty roster on either side cannot pass as agreement.
        Assert.NotEmpty(admitted);
        Assert.Equal(admitted.OrderBy(name => name, StringComparer.Ordinal), named.OrderBy(name => name, StringComparer.Ordinal));

        // AND THE EXCLUSIONS ARE NAMED, because "equal to whatever the predicate says" would still pass if
        // both layers admitted an update. These four are the ones a double application actually damages.
        foreach (string excluded in (string[])
            [
                $"{UpdateService.Descriptor.FullName}/Update",
                $"{CommandService.Descriptor.FullName}/Exec",
                $"{TransactionService.Descriptor.FullName}/BeginSession",
                $"{TransactionService.Descriptor.FullName}/Commit",
            ])
        {
            Assert.DoesNotContain(excluded, named);
        }
    }

    /// <summary>
    /// The retry policy attached to each named method is bounded, inclusive, and retries only the two
    /// statuses the HTTP predicate admits.
    /// </summary>
    /// <param name="maxAttempts">The attempt count handed to the builder.</param>
    /// <param name="initialBackoff">The first delay handed to the builder.</param>
    /// <remarks>
    /// THE ATTEMPT COUNT IS INCLUSIVE HERE AND A RETRY COUNT IN POLLY, which is the arithmetic most likely
    /// to be got wrong later: a gRPC <c>RetryPolicy.MaxAttempts</c> counts the FIRST attempt, so it is one
    /// greater than the number of retries the same intent expresses in the HTTP pipeline. The bound on the
    /// backoff is asserted because an unbounded exponential inside a total budget spends the whole budget
    /// waiting rather than attempting.
    /// </remarks>
    [Theory]
    [InlineData(2, "00:00:00.010")]
    [InlineData(4, "00:00:02")]
    [InlineData(6, "00:00:00.250")]
    public void EveryConfiguredMethodCarriesTheSameBoundedPolicy(int maxAttempts, string initialBackoff)
    {
        TimeSpan backoff = TimeSpan.Parse(initialBackoff, CultureInfo.InvariantCulture);

        ServiceConfig config = OutboundCallPolicy.BuildRetryServiceConfig(maxAttempts, backoff);

        Assert.NotEmpty(config.MethodConfigs);

        foreach (MethodConfig methodConfig in config.MethodConfigs)
        {
            RetryPolicy policy = Assert.IsType<RetryPolicy>(methodConfig.RetryPolicy);

            Assert.Equal(maxAttempts, policy.MaxAttempts);
            Assert.Equal(backoff, policy.InitialBackoff);
            Assert.Equal(backoff * 8, policy.MaxBackoff);
            Assert.Equal(2, policy.BackoffMultiplier);

            // THE SAME TWO STATUSES THE HTTP PREDICATE ADMITS, and no others. UNAVAILABLE is the upstream
            // saying it cannot serve; RESOURCE_EXHAUSTED is a bounded admission refusal that a later
            // attempt can legitimately pass. Everything else is either a correct answer that will not
            // change between attempts or a fault whose repetition is not known to be safe.
            Assert.Equal(
                new[] { global::Grpc.Core.StatusCode.ResourceExhausted, global::Grpc.Core.StatusCode.Unavailable },
                policy.RetryableStatusCodes.OrderBy(status => status).ToArray());

            // Exactly one method per configuration, so a status-set edit cannot silently apply to a
            // method a reader did not expect to be grouped with it.
            _ = Assert.Single(methodConfig.Names);
        }
    }

    /// <summary>
    /// The four deployed Persistence channels carry the service configuration and a ceiling that admits
    /// its attempt count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONLY TEST HERE THAT WOULD HAVE CAUGHT THE ORIGINAL DEFECT, which was not a wrong decision but an
    /// unreachable one: the policy was written, correct, and never consulted. This resolves the channel
    /// options the gRPC client factory will actually apply and reads what is on them.
    /// </para>
    /// <para>
    /// THE CEILING IS ASSERTED AS WELL AS THE CONFIGURATION, because a channel whose own
    /// <c>MaxRetryAttempts</c> is lower than the policy's count silently CLAMPS it - the configuration
    /// would still be present and the configured number would still not be the number performed, which is
    /// exactly the shape of failure this whole section exists to remove.
    /// </para>
    /// <para>
    /// THE EXPECTED COUNT IS DERIVED FROM THE BOUND OPTIONS, never written as a literal. The fixture
    /// deliberately reduces the attempt count, so a literal would either encode the fixture's value and
    /// stop testing the derivation, or encode the shipped value and fail for the wrong reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeployedPersistenceChannelsCarryTheGrpcRetryConfiguration()
    {
        using DataServicesTestHostFactory host = new();

        // Starting the host is what materialises the registrations; the client itself is unused.
        using HttpClient started = host.CreateAnonymousClient();

        ClientResilienceOptions configured = host.Services
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Resilience
            .Persistence;

        int expectedAttempts = configured.MaxRetryAttempts + 1;

        IOptionsMonitor<GrpcClientFactoryOptions> monitor =
            host.Services.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();

        IReadOnlySet<string> grpcClientNames = GrpcClientNames(host.Services);

        Assert.Equal(4, grpcClientNames.Count);

        int expectedMethodConfigs = OutboundCallPolicy
            .BuildRetryServiceConfig(expectedAttempts, configured.RetryBaseDelay)
            .MethodConfigs
            .Count;

        foreach (string name in grpcClientNames)
        {
            GrpcClientFactoryOptions factoryOptions = monitor.Get(name);

            GrpcChannelOptions channelOptions = new();

            foreach (Action<GrpcChannelOptions> configure in factoryOptions.ChannelOptionsActions)
            {
                configure(channelOptions);
            }

            ServiceConfig config = Assert.IsType<ServiceConfig>(channelOptions.ServiceConfig);

            Assert.Equal(expectedMethodConfigs, config.MethodConfigs.Count);
            Assert.Equal(expectedAttempts, channelOptions.MaxRetryAttempts);

            foreach (MethodConfig methodConfig in config.MethodConfigs)
            {
                Assert.Equal(
                    expectedAttempts,
                    Assert.IsType<RetryPolicy>(methodConfig.RetryPolicy).MaxAttempts);
                Assert.Equal(
                    configured.RetryBaseDelay,
                    Assert.IsType<RetryPolicy>(methodConfig.RetryPolicy).InitialBackoff);
            }

            // A READ IS RETRIED AND AN UPDATE IS NOT, on the deployed channel rather than in the pure
            // function above, so the roster that reached the channel is the classified one.
            Assert.Contains(
                $"{TransactionService.Descriptor.FullName}/IsConnected",
                config.MethodConfigs.SelectMany(entry => entry.Names)
                    .Select(entry => $"{entry.Service}/{entry.Method}"));
            Assert.DoesNotContain(
                $"{UpdateService.Descriptor.FullName}/Update",
                config.MethodConfigs.SelectMany(entry => entry.Names)
                    .Select(entry => $"{entry.Service}/{entry.Method}"));
        }
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

    /// <summary>
    /// The suffix the resilience package appends to an HttpClient's name to name its standard pipeline.
    /// </summary>
    private const string PipelineSuffix = "-standard";

    /// <summary>
    /// Recovers the HttpClient name a standard pipeline is named for.
    /// </summary>
    /// <param name="pipelineName">The pipeline name.</param>
    /// <returns>The client name, or the input unchanged when it carries no pipeline suffix.</returns>
    private static string StripPipelineSuffix(string pipelineName) =>
        pipelineName.EndsWith(PipelineSuffix, StringComparison.Ordinal)
            ? pipelineName[..^PipelineSuffix.Length]
            : pipelineName;

    /// <summary>
    /// Discovers the names of every gRPC client the composition registers.
    /// </summary>
    /// <param name="provider">The provider the registrations are read from.</param>
    /// <returns>The client names.</returns>
    /// <remarks>
    /// DISCOVERED RATHER THAN LISTED, for the same reason the resilience pipelines are: a fifth gRPC
    /// client added later is covered without editing this file, and the count assertion at each call site
    /// is what turns "covered" into "noticed".
    /// </remarks>
    private static IReadOnlySet<string> GrpcClientNames(IServiceProvider provider)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        foreach (IConfigureOptions<GrpcClientFactoryOptions> configure in provider
            .GetServices<IConfigureOptions<GrpcClientFactoryOptions>>())
        {
            // Read through the property rather than a cast, because the factory uses its own
            // named-configuration types alongside the framework's: a cast to one concrete type would skip
            // the others, which is how a client would drop out of this suite silently.
            if (configure.GetType().GetProperty("Name")?.GetValue(configure) is string name
                && !string.IsNullOrEmpty(name))
            {
                _ = names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Asks the HTTP-level retry predicate whether it would replay one gRPC method.
    /// </summary>
    /// <param name="service">The contract the method belongs to.</param>
    /// <param name="method">The method.</param>
    /// <returns>Whether a replay would be admitted.</returns>
    /// <remarks>
    /// THE REQUEST IS SHAPED THE WAY THE HANDLER WOULD SEE IT - a POST to the method's absolute gRPC path,
    /// answered with HTTP 200 carrying UNAVAILABLE in a trailing <c>grpc-status</c> header. That is the
    /// exact shape the stock predicate reads as a success, so this asks the real question rather than an
    /// easier one.
    /// </remarks>
    private static async Task<bool> WouldReplayGrpcMethodAsync(
        ContractDescriptor service,
        MethodDescriptor method)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri($"https://upstream/{service.FullName}/{method.Name}", UriKind.Absolute));

        using HttpResponseMessage response = new(HttpStatusCode.OK) { RequestMessage = request };
        response.Headers.Add(
            "grpc-status",
            ((int)global::Grpc.Core.StatusCode.Unavailable).ToString(CultureInfo.InvariantCulture));

        ResilienceContext context =
            ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);

        context.Properties.Set(
            new ResiliencePropertyKey<HttpRequestMessage>("Resilience.Http.RequestMessage"),
            request);

        try
        {
            return await OutboundCallPolicy.ShouldRetryAsync(
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
