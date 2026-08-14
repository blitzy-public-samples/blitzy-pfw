// ==================================================================================================
//  CompositionRootTests - the wiring contract of Program.cs
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS. Program.cs is wiring, and wiring has a failure mode that unit tests on the
//  wired types cannot catch: every individual type can be correct while the container never produces
//  one, the route never carries its authorization requirement, or a lifetime is chosen that makes two
//  concurrent calls share state they must not share. None of that is visible from a test of the type
//  itself, and all of it is visible from here.
//
//  It also keeps the composition root from being an UNCOVERED ISLAND. Constraint C-H measures line
//  coverage per service, so an entry point exercised by nothing would drag the whole service's figure
//  down while being precisely the file whose faults break every request at once.
//
//  WHAT IS ASSERTED, IN FIVE GROUPS.
//    1. Every gRPC service actually activates from the container, and the deliberate lifetimes hold.
//    2. The published routes carry the mandated posture: anonymous readiness, authenticated ping, and
//       no gRPC catch-all reshaping the REST surface.
//    3. The central status mapping: a conflict passes through untouched, an assertion terminates, a
//       caller cancellation reports cancellation, and statement literals never reach a log or the wire.
//    4. The startup gate terminates on a storage directory it cannot write.
//    5. Every shipped seam answers its own documented defined negative - and no commit ever reports
//       success it did not achieve.
// ==================================================================================================
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Authorization;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Data;
using PowerFramework.Persistence.Grpc;
using PowerFramework.Persistence.Runtime;
using PowerFramework.Persistence.Sql.Paging;
using PowerFramework.Persistence.Tasks;
using PowerFramework.Persistence.Transactions;
using PowerFramework.Shared.Diagnostics;
using GrpcCommandService = PowerFramework.Persistence.Grpc.CommandService;
using GrpcQueryService = PowerFramework.Persistence.Grpc.QueryService;
using GrpcTransactionService = PowerFramework.Persistence.Grpc.TransactionService;
using GrpcUpdateService = PowerFramework.Persistence.Grpc.UpdateService;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// Holds the composition root to its wiring contract.
/// </summary>
public sealed class CompositionRootTests
{
    /// <summary>The issuer the host under test is configured to trust.</summary>
    /// <remarks>A reserved test host, so nothing resolves and no metadata is ever fetched.</remarks>
    private const string TrustedIssuer = CompositionHost.TrustedIssuer;

    /// <summary>
    /// The caller identity this host's forged token claims, and therefore the one its permitted-caller
    /// roster must carry.
    /// </summary>
    /// <remarks>
    /// Stated as a constant rather than written twice, so the token's <c>sub</c> claim and the configured
    /// roster cannot drift apart and leave a row failing on a permission it never meant to test.
    /// FORWARDED from the host that mints the token rather than restated, for the same reason and in
    /// the same way <see cref="TrustedAudience"/> is.
    /// </remarks>
    private const string TokenSubject = CompositionHost.TokenSubject;

    /// <summary>The audience the host under test is configured to accept.</summary>
    private const string TrustedAudience = CompositionHost.TrustedAudience;

    /// <summary>The compact-serialization scheme name.</summary>
    private const string BearerScheme = "Bearer";

    // ==============================================================================================
    //  1. THE CONTAINER
    // ==============================================================================================

    /// <summary>
    /// Every one of the four published gRPC contracts can actually be activated.
    /// </summary>
    /// <remarks>
    /// THE SINGLE HIGHEST-VALUE WIRING ASSERTION HERE. The gRPC hosting layer activates a service
    /// implementation per call and resolves its constructor arguments from the container, so a
    /// dependency nobody registered produces no build error and no test failure anywhere else - it
    /// produces a failure on the first real request against that contract. Activating all four the same
    /// way the host does turns that into a compile-time-fast check.
    /// </remarks>
    [Fact]
    public void EveryGrpcContractActivatesFromTheContainer()
    {
        using CompositionHost host = CompositionHost.Create();
        using IServiceScope scope = host.Services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        Assert.NotNull(ActivatorUtilities.CreateInstance<GrpcQueryService>(provider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<GrpcUpdateService>(provider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<GrpcCommandService>(provider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<GrpcTransactionService>(provider));
    }

    /// <summary>
    /// The pool and the handle tables are shared, because their contents outlive the call that made them.
    /// </summary>
    /// <remarks>
    /// A scoped registry would discard every handle at the end of the request that minted it, so the
    /// very next call would answer "unknown session" for a session the caller had just been told it
    /// owned. A scoped pool could never hold a reference count above one, which is the only reason a
    /// pool exists at all.
    /// </remarks>
    [Fact]
    public void ThePoolAndTheHandleTablesAreShared()
    {
        using CompositionHost host = CompositionHost.Create();

        Assert.Same(
            host.Services.GetRequiredService<TransactionPool>(),
            host.Services.GetRequiredService<TransactionPool>());
        Assert.Same(
            host.Services.GetRequiredService<TransactionSessionRegistry>(),
            host.Services.GetRequiredService<TransactionSessionRegistry>());
        Assert.Same(
            host.Services.GetRequiredService<QueryTaskRegistry>(),
            host.Services.GetRequiredService<QueryTaskRegistry>());
    }

    /// <summary>
    /// The worker-side task host is not in the container, and each task gets its own.
    /// </summary>
    /// <remarks>
    /// A host is meaningful only when bound, at construction, to the fault sink of the one task it
    /// serves, which a container cannot express. It must also be one host per task, because the host
    /// owns that task's datastore cache and a shared host would hand concurrent calls the same
    /// non-concurrent cache. Both halves are asserted: absent from the container, and distinct per task.
    /// </remarks>
    [Fact]
    public void EachTaskGetsItsOwnWorkerHost()
    {
        using CompositionHost host = CompositionHost.Create();

        Assert.Null(host.Services.GetService<ISqlTaskHost>());

        IQueryTaskFactory factory = host.Services.GetRequiredService<IQueryTaskFactory>();

        using SqlQueryTask first = factory.Create(new QueryFaultRecorder());
        using SqlQueryTask second = factory.Create(new QueryFaultRecorder());

        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Exactly two paging arms are registered - no third, and specifically none for SQLite.
    /// </summary>
    /// <remarks>
    /// The legacy enumerates two database types and a <c>case else</c> that answers not-implemented, and
    /// classifies anything whose DBMS string does not contain <c>ORACLE</c> - SQLite included - as the
    /// SQL Server type. A third registration here would be a third arm the legacy does not have.
    /// </remarks>
    [Fact]
    public void ExactlyTwoPagingArmsAreRegistered()
    {
        using CompositionHost host = CompositionHost.Create();

        List<IPagingRewriter> rewriters = [.. host.Services.GetServices<IPagingRewriter>()];

        Assert.Equal(2, rewriters.Count);
        Assert.Equal(rewriters.Count, rewriters.Select(rewriter => rewriter.Dialect).Distinct().Count());
    }

    /// <summary>
    /// The pool's idle collection is DRIVEN in production, by a hosted service the host starts, and the
    /// sweeper the host runs is the same instance a test can resolve and drive.
    /// </summary>
    /// <remarks>
    /// THE COLLECTION'S OWN LOGIC BEING COVERED PROVES NOTHING ABOUT IT RUNNING. The legacy subscribes the
    /// pool to the framework's idle notification inside its keep-alive branch
    /// [n_cst_thread_trans_pool.sru:L80]; a service has no such notification, so if nothing here registers
    /// a driver the collection is reachable only from a test and every retained transaction is held for
    /// the life of the process. That gap is invisible to the collection's own suite - each of its tests
    /// calls the entry point directly - so the REGISTRATION is asserted here rather than there.
    /// </remarks>
    [Fact]
    public void TheIdleSweepIsHostedAndIsTheSameInstanceATestCanDrive()
    {
        using CompositionHost host = CompositionHost.Create();

        TransactionPoolIdleSweeper sweeper =
            host.Services.GetRequiredService<TransactionPoolIdleSweeper>();

        // ONE sweeper, shared - a second instance would sweep a pool nobody else is using.
        Assert.Same(sweeper, host.Services.GetRequiredService<TransactionPoolIdleSweeper>());

        // AND THE HOST STARTS IT. Resolved through the hosted-service collection, because that is the
        // only registration the host itself consults.
        Assert.Contains(sweeper, host.Services.GetServices<IHostedService>());

        // Scheduled by default, so an operator who configures nothing still gets collection.
        Assert.True(sweeper.IsScheduled);
    }

    // ==============================================================================================
    //  2. THE PUBLISHED ROUTES
    // ==============================================================================================

    /// <summary>
    /// Each gRPC contract is routed, and every one of its methods requires authorization.
    /// </summary>
    /// <param name="prefix">The contract's route prefix, leading slash included.</param>
    [Theory]
    [InlineData("/persistence.v1.QueryService/")]
    [InlineData("/persistence.v1.UpdateService/")]
    [InlineData("/persistence.v1.CommandService/")]
    [InlineData("/persistence.v1.TransactionService/")]
    public void EveryGrpcContractIsRoutedAndRequiresAuthorization(string prefix)
    {
        using CompositionHost host = CompositionHost.Create();

        List<RouteEndpoint> mapped = host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is string text
                && text.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(mapped);
        Assert.All(mapped, endpoint =>
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    /// <summary>
    /// Readiness is anonymous and the ping route refuses an anonymous caller.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task ReadinessIsAnonymousAndPingIsNot()
    {
        // PROVISIONED FIRST, because readiness verifies the schema and not merely that the engine answers.
        // Deliberately still the REAL storage check rather than a stub: the property under test is that
        // the DEPLOYED composition answers an anonymous readiness request with 200 and an uncredentialled
        // ping with 401, and substituting the check would move the assertion off that composition.
        using CompositionHost host = CompositionHost.Create();

        await host.ProvisionSchemaAsync(TestContext.Current.CancellationToken);

        using HttpClient client = host.CreateClient();

        using HttpResponseMessage readiness = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);
        using HttpResponseMessage ping = await client.GetAsync(
            new Uri("/v1/ping", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
    }

    /// <summary>
    /// Every framework-generated failure status carries the same problem body this service's own error
    /// responses carry.
    /// </summary>
    /// <param name="route">The route to request.</param>
    /// <param name="method">The method to request it with.</param>
    /// <param name="authenticated">Whether the request presents a credential this host trusts.</param>
    /// <param name="expected">The status the framework answers.</param>
    /// <param name="expectedRetCode">The legacy code this service's vocabulary assigns to it.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// ONE SERVICE MUST NOT ANSWER TWO ERROR SHAPES DEPENDING ON WHICH LAYER FAILED. The readiness endpoint
    /// writes a problem body with a return code because its own code does; the challenge, the refusal, an
    /// unmatched route and a rejected method are produced beneath any of this service's code and used to
    /// carry no body at all. The row above asserts the STATUS of the refusal and passed throughout, which is
    /// exactly why the missing body went unnoticed.
    /// </para>
    /// <para>
    /// BOTH EXTENSION MEMBERS ARE ASSERTED, because the middleware alone would produce a body without them:
    /// <c>retCode</c> comes from the composition root's classification of a framework status into the legacy
    /// vocabulary, and <c>traceId</c> is what lets an operator find the record for the occurrence.
    /// </para>
    /// <para>
    /// The 405 row is also the assertion that the gRPC catch-all route stays switched off. That route is two
    /// parameter segments wide, so with it enabled it matches <c>/v1/ping</c> and answers from the gRPC
    /// handler - which reports 404 for a rejected method and writes a gRPC content type rather than a problem
    /// body.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/ping", "GET", false, HttpStatusCode.Unauthorized, RetCode.E_ACCESS_DENIED)]
    [InlineData("/v1/ping", "DELETE", true, HttpStatusCode.MethodNotAllowed, RetCode.E_NO_SUPPORT)]
    [InlineData("/v1/no-such-route", "GET", true, HttpStatusCode.NotFound, RetCode.E_OBJECT_NOT_FOUND)]
    public async Task EveryFrameworkGeneratedStatusCarriesTheProblemBody(
        string route,
        string method,
        bool authenticated,
        HttpStatusCode expected,
        long expectedRetCode)
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(new HttpMethod(method), new Uri(route, UriKind.Relative));

        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, host.MintToken());
        }

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // The RFC 9457 member, which is the HTTP status as an integer rather than a status word.
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedRetCode, body.RootElement.GetProperty("retCode").GetInt64());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("traceId").GetString()));
    }

    /// <summary>
    /// The ping route succeeds for a caller holding a credential the host trusts.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The positive arm, asserted deliberately alongside the refusal above. A suite that only proved
    /// the 401 would leave open the possibility that the route refuses everything.
    /// </remarks>
    [Fact]
    public async Task PingSucceedsForATrustedCaller()
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/v1/ping", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, host.MintToken());

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// No gRPC catch-all route reshapes the REST surface that shares the port.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE REGRESSION THIS EXISTS FOR, stated so nobody re-introduces it. Left at its default, the gRPC
    /// hosting layer maps an unknown-service route of the shape <c>/{service}/{method}</c>. That is two
    /// PARAMETER segments, so it matches ANY two-segment path - the ping route included - and because it
    /// accepts POST it becomes a live candidate for a verb the REST route does not map. Routing then
    /// stops seeing a candidate set rejected solely on method, and the 405 collapses into a 404 from the
    /// gRPC handler. Both halves are asserted here: the wrong verb still answers 405, and an unknown
    /// service still falls through to a genuine 404.
    /// </remarks>
    [Fact]
    public async Task NoGrpcCatchAllReshapesTheRestSurface()
    {
        using CompositionHost host = CompositionHost.Create();
        using HttpClient client = host.CreateClient();

        string token = host.MintToken();

        using HttpRequestMessage wrongVerb = new(HttpMethod.Post, new Uri("/v1/ping", UriKind.Relative));
        wrongVerb.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        using HttpRequestMessage unknownService = new(
            HttpMethod.Post,
            new Uri("/no.such.Service/Method", UriKind.Relative));
        unknownService.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);

        using HttpResponseMessage verbResponse = await client.SendAsync(
            wrongVerb,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage unknownResponse = await client.SendAsync(
            unknownService,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, verbResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
    }

    /// <summary>
    /// The composed host resolves exactly one status interceptor, and it carries the host-backed
    /// termination effect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ASSERTED BECAUSE THE REGISTRATION IS A FACTORY RATHER THAN A TYPE, and the difference is not
    /// observable anywhere else in this suite. Registered by type, the container fills every constructor
    /// parameter it can - including the termination seam, whose whole purpose is to be the host-backed
    /// default in production and an injected recorder only in a test. A factory states the three real
    /// dependencies and leaves the seam alone, so the deployed path cannot be displaced by an unrelated
    /// registration of the same delegate type.
    /// </para>
    /// <para>
    /// The single-instance half matters for a different reason: the gRPC hosting layer resolves the
    /// interceptor from the container when one is registered, and a per-call instance would rebuild the
    /// redactor and the logger on the hot path of every call on all four contracts.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheComposedHostResolvesOneStatusInterceptor()
    {
        using CompositionHost host = CompositionHost.Create();

        // Forces the host to build, so the resolution below runs against the real composition rather than
        // a lazily unstarted one.
        using HttpClient _ = host.CreateClient();

        PersistenceStatusInterceptor first =
            host.Services.GetRequiredService<PersistenceStatusInterceptor>();
        PersistenceStatusInterceptor second =
            host.Services.GetRequiredService<PersistenceStatusInterceptor>();

        Assert.Same(first, second);
    }

    // ==============================================================================================
    //  3. THE CENTRAL STATUS MAPPING
    // ==============================================================================================

    /// <summary>
    /// A status a handler already chose passes through with its detail and its trailers intact.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE CONFLICT PATH. An optimistic-concurrency mismatch is reported as
    /// <see cref="StatusCode.Aborted"/> with a structured conflict detail in the trailers, which the
    /// gateway projects as HTTP 409. Rewriting it centrally would strip the trailers and turn a 409
    /// carrying evidence into a bare 500, so the interceptor must not touch it - and no silent overwrite
    /// is permitted anywhere in this system.
    /// </remarks>
    [Fact]
    public async Task AChosenStatusPassesThroughUntouched()
    {
        RecordingLifetime lifetime = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            lifetime,
            NullLogger<PersistenceStatusInterceptor>.Instance);

        Metadata trailers = [];
        trailers.Add("conflict-detail-bin", [1, 2, 3]);

        RpcException conflict = new(new Status(StatusCode.Aborted, "the row changed"), trailers);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw conflict));

        Assert.Same(conflict, thrown);
        Assert.Equal(StatusCode.Aborted, thrown.StatusCode);
        Assert.Equal("the row changed", thrown.Status.Detail);
        Assert.NotNull(thrown.Trailers.Get("conflict-detail-bin"));
        Assert.False(lifetime.Stopped);
    }

    /// <summary>
    /// A failed assertion is a structural fault: the caller is told, and termination is requested with
    /// the documented non-zero exit code.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The legacy terminates the application after decoding an assertion payload
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. Preserved here as a termination request plus an
    /// immediate answer, rather than leaving the caller waiting for a socket to close.
    /// </para>
    /// <para>
    /// THE EXIT CODE IS ASSERTED, NOT MERELY THE STOP. A stop request on its own leaves the exit status at
    /// zero, so every mechanism that reads an exit status - a restart policy scoped to failures, a
    /// supervisor's success accounting - would treat a broken invariant as an orderly shutdown. The
    /// termination seam is injected so the assertion costs the test process nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFailedAssertionStopsTheHostAndAnswersImmediately()
    {
        List<int> requestedExitCodes = [];
        RecordingLifetime lifetime = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            lifetime,
            NullLogger<PersistenceStatusInterceptor>.Instance,
            requestProcessTermination: requestedExitCodes.Add);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new AssertionFailure("an invariant is broken")));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        Assert.Equal(PersistenceStatusInterceptor.AssertionFaultDetail, thrown.Status.Detail);
        Assert.Equal(
            [PersistenceStatusInterceptor.StructuralFaultExitCode],
            requestedExitCodes);
        Assert.NotEqual(0, PersistenceStatusInterceptor.StructuralFaultExitCode);
    }

    /// <summary>
    /// The default termination effect reports the structural-fault exit code BEFORE it asks the host to
    /// stop, and it asks rather than aborting.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ONLY TEST HERE THAT EXERCISES THE SHIPPED SEAM RATHER THAN AN INJECTED ONE, which is why it is
    /// also the only one that has to restore process state afterwards. Every other case injects, so the
    /// exit code the deployed path actually reports would otherwise be asserted nowhere at all - and the
    /// composition root supplies no callback, so the shipped path is the one that runs in production.
    /// </para>
    /// <para>
    /// The ordering claim is checked at the moment of the stop request rather than after it returns,
    /// because only the reading taken inside <c>StopApplication</c> can tell a code set beforehand from
    /// one set afterwards - and a code set afterwards races the host's own return from its run loop.
    /// </para>
    /// <para>
    /// Shutdown is REQUESTED and the process is not aborted, because the registered shutdown path is where
    /// the pooled transactions drain and the handle registries release what they hold, and because the
    /// legacy halt likewise runs the application close event - the framework finalize call
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>] - before terminating.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDefaultTerminationEffectSetsTheExitCodeBeforeRequestingShutdown()
    {
        int previousExitCode = Environment.ExitCode;
        RecordingLifetime lifetime = new();

        try
        {
            PersistenceStatusInterceptor interceptor = new(
                Errors.SqlRedactor.Instance,
                lifetime,
                NullLogger<PersistenceStatusInterceptor>.Instance);

            await Assert.ThrowsAsync<RpcException>(() =>
                interceptor.UnaryServerHandler<string, string>(
                    "request",
                    new StubCallContext(CancellationToken.None),
                    (_, _) => throw new AssertionFailure("an invariant is broken")));

            Assert.True(lifetime.Stopped);
            Assert.Equal(
                PersistenceStatusInterceptor.StructuralFaultExitCode,
                lifetime.ExitCodeWhenStopped);
            Assert.Equal(PersistenceStatusInterceptor.StructuralFaultExitCode, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = previousExitCode;
        }
    }

    /// <summary>
    /// Termination is requested even when reporting the fault fails.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE FAILURE MODE THIS CLOSES IS SILENT AND STRICTLY WORSE THAN A LOST LOG RECORD. Place the
    /// termination request AFTER the log write and a provider that throws carries the fault out of the
    /// mapping, leaving the process serving requests in a state its own invariants have already declared
    /// impossible - with the only trace being a write failure the caller never sees. The write is
    /// best effort and the termination is not, so the log fault still propagates to the caller as a fault
    /// while the process still ends.
    /// </remarks>
    [Fact]
    public async Task TerminationIsRequestedEvenWhenReportingTheFaultThrows()
    {
        List<int> requestedExitCodes = [];
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            new ThrowingLogger<PersistenceStatusInterceptor>(),
            requestProcessTermination: requestedExitCodes.Add);

        InvalidOperationException surfaced = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new AssertionFailure("an invariant is broken")));

        Assert.Same(ThrowingLogger<PersistenceStatusInterceptor>.WriteFault, surfaced);
        Assert.Equal(
            [PersistenceStatusInterceptor.StructuralFaultExitCode],
            requestedExitCodes);
    }

    /// <summary>
    /// Every handler shape terminates on a structural fault, not only the unary one.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted per shape because retrieval is a SERVER STREAM: a termination path wired into the unary
    /// shape alone would leave this service's primary read path able to break an invariant and carry on.
    /// </remarks>
    [Fact]
    public async Task EveryHandlerShapeTerminatesOnAStructuralFault()
    {
        List<int> requestedExitCodes = [];
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            NullLogger<PersistenceStatusInterceptor>.Instance,
            requestProcessTermination: requestedExitCodes.Add);

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.ServerStreamingServerHandler<string, string>(
                "request",
                new StubStreamWriter(),
                new StubCallContext(CancellationToken.None),
                (_, _, _) => throw new AssertionFailure("an invariant is broken")));

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.ClientStreamingServerHandler<string, string>(
                new StubStreamReader(),
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new AssertionFailure("an invariant is broken")));

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler<string, string>(
                new StubStreamReader(),
                new StubStreamWriter(),
                new StubCallContext(CancellationToken.None),
                (_, _, _) => throw new AssertionFailure("an invariant is broken")));

        Assert.Equal(
            [
                PersistenceStatusInterceptor.StructuralFaultExitCode,
                PersistenceStatusInterceptor.StructuralFaultExitCode,
                PersistenceStatusInterceptor.StructuralFaultExitCode,
            ],
            requestedExitCodes);
    }

    /// <summary>
    /// No fault other than a failed assertion terminates the process.
    /// </summary>
    /// <param name="faultKind">Which fault the handler raises.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE OTHER HALF OF THE FAIL-FAST POSTURE, AND THE HALF THAT IS EASIER TO BREAK. Terminating on a
    /// request fault would convert a caller's bad argument, a database error, or a caller hanging up into
    /// an outage for every other caller of the instance. Only a broken invariant is structural.
    /// </remarks>
    [Theory]
    [InlineData("chosen-status")]
    [InlineData("unhandled")]
    [InlineData("caller-cancelled")]
    [InlineData("internal-cancellation")]
    public async Task NoOtherFaultTerminatesTheProcess(string faultKind)
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        bool callerCancelled = faultKind == "caller-cancelled";
        List<int> requestedExitCodes = [];
        RecordingLifetime lifetime = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            lifetime,
            NullLogger<PersistenceStatusInterceptor>.Instance,
            requestProcessTermination: requestedExitCodes.Add);

        Exception fault = faultKind switch
        {
            "chosen-status" => new RpcException(new Status(StatusCode.Aborted, "the row changed")),
            "unhandled" => new InvalidOperationException("a request fault"),
            "caller-cancelled" => new OperationCanceledException(cancelled.Token),
            _ => new OperationCanceledException("nobody cancelled this"),
        };

        await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(callerCancelled ? cancelled.Token : CancellationToken.None),
                (_, _) => throw fault));

        Assert.Empty(requestedExitCodes);
        Assert.False(lifetime.Stopped);
        Assert.Null(lifetime.ExitCodeWhenStopped);
    }

    /// <summary>
    /// A cancellation raised while the call's own token is cancelled reports cancellation.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task CallerCancellationReportsCancelled()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            NullLogger<PersistenceStatusInterceptor>.Instance);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(cancelled.Token),
                (_, _) => throw new OperationCanceledException(cancelled.Token)));

        Assert.Equal(StatusCode.Cancelled, thrown.StatusCode);
    }

    /// <summary>
    /// A cancellation raised without the call being cancelled is an internal fault, not a cancellation.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The distinction matters: reporting an internal fault as cancelled would tell a caller it had hung
    /// up when it had not, and would hide a genuine defect behind a status nobody investigates.
    /// </remarks>
    [Fact]
    public async Task ACancellationWithoutACancelledCallIsAnInternalFault()
    {
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            NullLogger<PersistenceStatusInterceptor>.Instance);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new OperationCanceledException("nobody cancelled this")));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
    }

    /// <summary>
    /// A literal interpolated into a statement reaches neither the log record nor the wire, and it reaches
    /// neither from an inner exception either.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The legacy database-error structure's statement field carries the complete generated statement
    /// including its interpolated literal values, and the legacy logger performed no redaction at all. An
    /// exception message raised near statement generation carries the same thing, so every message in the
    /// chain is redacted before it is recorded and the wire receives a constant.
    /// </para>
    /// <para>
    /// AN ASSERTION LIKE THIS CAN PASS WHILE THE LITERAL IS PUBLISHED IN FULL, and both reasons are
    /// closed here. Format the record from a redacted message and attach the exception beside it, and a
    /// provider renders the unredacted original through <c>ToString()</c> - while a recorder capturing only
    /// the formatted text sees none of it. So the exception is recorded as well and asserted
    /// absent.
    /// </para>
    /// <para>
    /// THE LITERAL IS PLACED ON AN INNER EXCEPTION, which is where it really is. A provider fault arrives
    /// wrapped - a task fault around a command fault around the provider's own - so redacting only the
    /// outermost message would leave the ordinary case fully exposed while a test using a single flat
    /// exception passed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task StatementLiteralsReachNeitherTheLogNorTheWire()
    {
        const string Literal = "O'Hara-super-secret-salary-99999";

        RecordingLogger<PersistenceStatusInterceptor> logger = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            logger);

        InvalidOperationException wrapped = new(
            "the update task faulted",
            new InvalidOperationException($"UPDATE COMPANY SET NAME = '{Literal}' WHERE ID = 7"));

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw wrapped));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        Assert.Equal(PersistenceStatusInterceptor.UnhandledFaultDetail, thrown.Status.Detail);
        Assert.DoesNotContain(Literal, thrown.Status.Detail, StringComparison.Ordinal);

        Assert.NotEmpty(logger.Records);
        Assert.DoesNotContain(
            Literal,
            string.Join("\n", logger.Records),
            StringComparison.Ordinal);

        // NO EXCEPTION IS ATTACHED, which is the assertion the formatted text cannot make: an attached
        // exception is rendered in full by every provider, so its presence alone republishes the literal.
        Assert.All(logger.Exceptions, Assert.Null);

        // The chain is still identified, so dropping the object costs an operator nothing they needed: both
        // type names are named, outermost first.
        Assert.Contains(
            typeof(InvalidOperationException).FullName!
                + PersistenceStatusInterceptor.FaultChainSeparator
                + typeof(InvalidOperationException).FullName!,
            string.Join("\n", logger.Records),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The structural arm still attaches its exception, because that record reproduces the legacy dialog.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE ASYMMETRY IS DELIBERATE AND IS ASSERTED SO IT CANNOT BE "TIDIED" INTO UNIFORMITY. An assertion
    /// payload carries no statement, no path and no credential - it carries the seven fields the legacy
    /// producer emits - and the record is written at most once per process because the path it belongs to
    /// terminates the host. C-B requires the legacy dialog be preserved in full
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>], and the managed stack is the part of it that has no
    /// legacy equivalent to lose.
    /// </remarks>
    [Fact]
    public async Task TheStructuralArmStillCarriesItsException()
    {
        RecordingLogger<PersistenceStatusInterceptor> logger = new();
        AssertionFailure assertion = new("an invariant is broken");
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            logger,
            requestProcessTermination: static _ => { });

        _ = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw assertion));

        Assert.Same(assertion, Assert.Single(logger.Exceptions));
    }

    /// <summary>
    /// The streaming handler shapes route through the same mapping as the unary one.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Asserted because retrieval is a SERVER STREAM: a mapping applied to unary calls alone would leave
    /// this service's primary read path unmapped, which is the one shape it uses most.
    /// </remarks>
    [Fact]
    public async Task EveryHandlerShapeSharesTheMapping()
    {
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            new RecordingLifetime(),
            NullLogger<PersistenceStatusInterceptor>.Instance);

        InvalidOperationException fault = new("a fault");

        RpcException serverStreaming = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.ServerStreamingServerHandler<string, string>(
                "request",
                new StubStreamWriter(),
                new StubCallContext(CancellationToken.None),
                (_, _, _) => throw fault));

        RpcException clientStreaming = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.ClientStreamingServerHandler<string, string>(
                new StubStreamReader(),
                new StubCallContext(CancellationToken.None),
                (_, _) => throw fault));

        RpcException duplex = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler<string, string>(
                new StubStreamReader(),
                new StubStreamWriter(),
                new StubCallContext(CancellationToken.None),
                (_, _, _) => throw fault));

        Assert.Equal(StatusCode.Internal, serverStreaming.StatusCode);
        Assert.Equal(StatusCode.Internal, clientStreaming.StatusCode);
        Assert.Equal(StatusCode.Internal, duplex.StatusCode);
    }

    /// <summary>
    /// A call that succeeds is passed through completely untouched, in every handler shape.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE COMMON CASE, AND THE ONE MOST WORTH ASSERTING. An interceptor sits on every call to all four
    /// contracts, so a mistake in its success path - swallowing a response, replacing a result, failing
    /// to await - would break the entire service while every failure-mapping case above still passed.
    /// </remarks>
    [Fact]
    public async Task ASuccessfulCallIsPassedThroughUntouched()
    {
        RecordingLifetime lifetime = new();
        RecordingLogger<PersistenceStatusInterceptor> logger = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            lifetime,
            logger);

        string unary = await interceptor.UnaryServerHandler<string, string>(
            "request",
            new StubCallContext(CancellationToken.None),
            (request, _) => Task.FromResult(request + "-answered"));

        string clientStreaming = await interceptor.ClientStreamingServerHandler<string, string>(
            new StubStreamReader(),
            new StubCallContext(CancellationToken.None),
            (_, _) => Task.FromResult("client-streamed"));

        bool serverStreamRan = false;
        await interceptor.ServerStreamingServerHandler<string, string>(
            "request",
            new StubStreamWriter(),
            new StubCallContext(CancellationToken.None),
            (_, _, _) =>
            {
                serverStreamRan = true;
                return Task.CompletedTask;
            });

        bool duplexRan = false;
        await interceptor.DuplexStreamingServerHandler<string, string>(
            new StubStreamReader(),
            new StubStreamWriter(),
            new StubCallContext(CancellationToken.None),
            (_, _, _) =>
            {
                duplexRan = true;
                return Task.CompletedTask;
            });

        Assert.Equal("request-answered", unary);
        Assert.Equal("client-streamed", clientStreaming);
        Assert.True(serverStreamRan);
        Assert.True(duplexRan);

        // A successful call is neither logged as a fault nor allowed to stop the host.
        Assert.Empty(logger.Records);
        Assert.False(lifetime.Stopped);
    }

    // ==============================================================================================
    //  4. THE STARTUP GATE
    // ==============================================================================================

    /// <summary>
    /// A storage directory that cannot be created terminates startup instead of degrading past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The directory's parent is a FILE, which cannot be created into by any user, so this proves the
    /// probe without depending on the identity the suite happens to run as. In deployment the same arm
    /// is reached by a mounted volume owned by another user, which the image's non-root user cannot
    /// write - a fault that passes both an existence check and a permission-bit check, which is exactly
    /// why the gate probes with a real write.
    /// </para>
    /// <para>
    /// <b>THE DIAGNOSTIC IS ASSERTED AS WELL AS THE TERMINATION, AND THE ASSERTION CHANGED.</b> This case
    /// deliberately does not require the single sentence "not writable by this process", which covers every
    /// one of the four ways this can fail - including this one, where the problem is not permissions at all
    /// but a file standing where the parent directory should be. It also requires the
    /// CONFIGURATION KEY, which a record quoting only the configured PATH omits entirely, and the
    /// attached file-system exception republished that path in its own message. Measured on a running host
    /// the path appeared four times and the key none. The record now names the key, states the established
    /// failure class, describes the cause by type, and withholds the path - the rule
    /// <c>Program.cs</c>'s internal-trust anchor and both sibling services already applied to their own
    /// mounted paths.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnwritableStorageDirectoryTerminatesStartup()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"pfw-blocker-{Guid.NewGuid():n}");
        File.WriteAllText(blocker, string.Empty);

        string configured = Path.Combine(blocker, "nested");

        try
        {
            using ServiceProvider provider = BuildGateProvider(configured);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                provider.ValidatePersistenceStructuralPreconditions);

            // The key an operator changes.
            Assert.Contains(
                DataDirectoryFault.ConfigurationKey,
                failure.Message,
                StringComparison.Ordinal);

            // The established class: the containing directory is a file, so it is not "not writable".
            Assert.Contains("CONTAINING DIRECTORY", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "cannot write inside it",
                failure.Message,
                StringComparison.Ordinal);

            // The mount layout is withheld, asserted as booleans so a failure cannot print it.
            Assert.False(
                failure.Message.Contains(configured, StringComparison.Ordinal),
                "The terminal message reproduces the configured storage path.");
            Assert.False(
                failure.Message.Contains(blocker, StringComparison.Ordinal),
                "The terminal message reproduces the blocking path.");

            // No chained file-system exception, because its own message quotes the path.
            Assert.Null(failure.InnerException);
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    /// <summary>
    /// A storage directory inside the read-only legacy tree is refused BEFORE it is created.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE REFUSAL WAS NEVER IN DOUBT; THE ORDER WAS.</b> The connection factory refuses this path in
    /// its constructor, but that constructor is resolved by the runtime-graph gate, which runs AFTER the
    /// storage gate's writability probe - and that probe calls <c>Directory.CreateDirectory</c>. So the
    /// service used to CREATE a directory inside <c>ws_objects/**</c> and only then refuse to start,
    /// writing into the behavioural oracle that constraint C-C states is never an edit target: the very
    /// next characterization capture would read whatever landed there as legacy source.
    /// </para>
    /// <para>
    /// The assertion that matters is therefore the second one. The path used here is a real one inside
    /// this repository's own legacy tree, resolved from the test assembly's location rather than
    /// hard-coded, so the case exercises the same segment test a deployment would.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStorageDirectoryInsideTheReadOnlyLegacyTreeIsRefusedWithoutCreatingIt()
    {
        // The repository root, walked up from the test assembly: <repo>/services/persistence-service/
        // PowerFramework.Persistence.Tests/bin/<config>/<tfm>/
        string root = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

        string forbidden = Path.Combine(root, "ws_objects", $"pfw-gate-refused-{Guid.NewGuid():n}");

        using ServiceProvider provider = BuildGateProvider(forbidden);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            provider.ValidatePersistenceStructuralPreconditions);

        Assert.Contains("read-only legacy export tree", failure.Message, StringComparison.Ordinal);

        // NOT CREATED. This is the whole case: a refusal that arrives one mkdir too late is not a refusal.
        Assert.False(Directory.Exists(forbidden));
    }

    /// <summary>
    /// A writable directory passes, is created if absent, and is left exactly as it was found.
    /// </summary>
    /// <remarks>
    /// The second assertion is the load-bearing one. The gate writes a probe file to prove writability,
    /// and it must remove it: leaving debris in the persistence volume would be a change to the very
    /// volume state the paired characterization captures require to be untouched.
    /// </remarks>
    [Fact]
    public void AWritableStorageDirectoryPassesAndIsLeftAsItWasFound()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-gate-{Guid.NewGuid():n}");

        try
        {
            using ServiceProvider provider = BuildGateProvider(directory);

            provider.ValidatePersistenceStructuralPreconditions();

            Assert.True(Directory.Exists(directory));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// The gate resolves every SQL seam, so a fully composed graph starts and an incomplete one does not.
    /// </summary>
    /// <remarks>
    /// THE SECOND HALF IS THE LOAD-BEARING ONE. A provider missing a seam used to start happily and then
    /// answer every request with a refusal, which is the failure mode this gate exists to convert into a
    /// startup failure. Removing the transaction engine is the cheapest way to prove the conversion
    /// happened, because it is the seam every statement in the service ultimately runs through.
    /// </remarks>
    [Fact]
    public void TheGateResolvesEverySqlSeamAndRefusesAnIncompleteGraph()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-graph-{Guid.NewGuid():n}");

        try
        {
            using ServiceProvider complete = BuildRuntimeGraphProvider(directory, omitEngine: false);

            // A complete graph passes without terminating, and leaves no probe debris behind it.
            complete.ValidatePersistenceStructuralPreconditions();
            Assert.Empty(Directory.GetFileSystemEntries(directory));

            using ServiceProvider incomplete = BuildRuntimeGraphProvider(directory, omitEngine: true);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                incomplete.ValidatePersistenceStructuralPreconditions);

            Assert.Contains("runtime seam", refused.Message, StringComparison.Ordinal);

            // The seam that could not be produced is named by the INNER exception, so an operator reading
            // the terminal record learns which registration to fix rather than only that one is missing.
            Assert.NotNull(refused.InnerException);
            Assert.Contains(
                nameof(ITransactionEngine),
                refused.InnerException.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// The startup sequence provisions a fresh volume, so a first start converges without an operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW THAT MAKES THE ONE-COMMAND BRING-UP TRUE. The readiness probe reports the storage engine's
    /// real state, so before this behaviour existed a brand-new <c>persistence-db</c> volume answered
    /// <c>/health</c> with 503 for ever and the health-conditioned Compose chain held DataServices and
    /// Gateway behind a Persistence that could never become ready. The provisioning step is what closes
    /// that, and it has to happen AFTER the gate has proven the directory writable and BEFORE the pipeline
    /// is built - the position <c>Program.cs</c> runs it in and the order this row reproduces.
    /// </para>
    /// <para>
    /// IT DRIVES THE STEP OUT OF THE REAL GRAPH RATHER THAN CONSTRUCTING IT, which is this row's whole
    /// contribution over the provisioner's own suite. That suite builds the type directly and needs no
    /// host, so nothing in it would notice the registration being dropped from
    /// <c>AddPersistenceStorage</c> or the composition root ceasing to run it - and either would restore
    /// the never-ready volume this row exists to rule out.
    /// </para>
    /// <para>
    /// BOTH TABLES ARE ASSERTED, because the readiness probe requires both: the application table is what
    /// a query needs, and the migration history is what makes the database a migrated one rather than a
    /// hand-built lookalike the next migration would fail against.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheStartupSequenceProvisionsTheSchemaOnAFreshVolumeBeforeAnythingIsServed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-provision-{Guid.NewGuid():n}");

        try
        {
            using ServiceProvider provider = BuildRuntimeGraphProvider(
                directory,
                omitEngine: false,
                applyMigrationsOnStartup: true);

            provider.ValidatePersistenceStructuralPreconditions();

            await provider
                .GetRequiredService<SchemaProvisioner>()
                .ProvisionAsync(TestContext.Current.CancellationToken);

            Assert.True(File.Exists(Path.Combine(directory, "test.db")));
            Assert.True(TableExists(directory, "COMPANY"));
            Assert.True(TableExists(directory, "__EFMigrationsHistory"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Provisioning twice is a no-op the second time, and it never touches a row that is already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ASSERTION THE PAIRED-CAPTURE RULE DEPENDS ON. For one workflow identifier the legacy-side and
    /// target-side recordings must be taken against the SAME volume state, with the volume neither
    /// recreated nor reseeded between them - so a restart between the two halves must be observationally
    /// invisible. Writing a row, restarting the whole startup sequence, and finding the row byte-identical
    /// is what proves it: an <c>EnsureCreated</c>, a drop-and-recreate or a seed would each fail this row
    /// loudly.
    /// </para>
    /// <para>
    /// The migration history count is asserted too, because a re-applied migration would be a second row
    /// there even if the data survived.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ProvisioningTwiceLeavesTheDatabaseAndItsRowsExactlyAsTheyWere()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-provision-{Guid.NewGuid():n}");

        try
        {
            using (ServiceProvider first = BuildRuntimeGraphProvider(
                directory,
                omitEngine: false,
                applyMigrationsOnStartup: true))
            {
                first.ValidatePersistenceStructuralPreconditions();

                await first
                    .GetRequiredService<SchemaProvisioner>()
                    .ProvisionAsync(TestContext.Current.CancellationToken);
            }

            long historyAfterFirstStart = ScalarLong(directory, "SELECT COUNT(*) FROM __EFMigrationsHistory");

            Assert.Equal(1, historyAfterFirstStart);

            Execute(
                directory,
                "INSERT INTO COMPANY (NAME, AGE, ADDRESS, SALARY, BIRTH) "
                + "VALUES ('a-row-a-capture-depends-on', 41, 'an-address', 1234.5, '1985-03-04')");

            using (ServiceProvider second = BuildRuntimeGraphProvider(
                directory,
                omitEngine: false,
                applyMigrationsOnStartup: true))
            {
                second.ValidatePersistenceStructuralPreconditions();

                await second
                    .GetRequiredService<SchemaProvisioner>()
                    .ProvisionAsync(TestContext.Current.CancellationToken);
            }

            // THE ROW SURVIVED, WHICH IS THE POINT. Nothing was dropped, recreated or reseeded.
            Assert.Equal(1, ScalarLong(directory, "SELECT COUNT(*) FROM COMPANY"));
            Assert.Equal(
                "a-row-a-capture-depends-on",
                ScalarText(directory, "SELECT NAME FROM COMPANY"));

            // AND NO MIGRATION WAS RE-APPLIED.
            Assert.Equal(
                historyAfterFirstStart,
                ScalarLong(directory, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// With the switch off the startup sequence issues no schema statement, and creates no database file.
    /// </summary>
    /// <remarks>
    /// THE POSTURE A CHARACTERIZATION CAPTURE RUN NEEDS, AND THE SHIPPED DEFAULT. A capture must be able to
    /// state that nothing but the workflow under characterization touched the volume, so the switch has to
    /// mean what it says: with it off the gate still validates and still proves the directory writable, the
    /// provisioning step performs no file operation whatsoever, and the database file is not created - the
    /// readiness probe then reports not-ready and provisioning is the operator's, exactly as
    /// <c>docs/BUILD.md</c> §5.6 describes. The empty-directory assertion covers BOTH steps: the gate's own
    /// writability probe is deleted again, and the provisioner leaves not even its lock file behind.
    /// </remarks>
    [Fact]
    public async Task ProvisioningSwitchedOffIssuesNoSchemaStatementAndCreatesNoDatabase()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-provision-{Guid.NewGuid():n}");

        try
        {
            using ServiceProvider provider = BuildRuntimeGraphProvider(
                directory,
                omitEngine: false,
                applyMigrationsOnStartup: false);

            provider.ValidatePersistenceStructuralPreconditions();

            await provider
                .GetRequiredService<SchemaProvisioner>()
                .ProvisionAsync(TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(directory));

            // NOT MERELY "no COMPANY table" - no file whatsoever, and no probe debris either.
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// Reports whether one table exists in the database under a data directory.
    /// </summary>
    /// <param name="dataDirectory">The directory holding <c>test.db</c>.</param>
    /// <param name="tableName">The table to look for.</param>
    /// <returns><see langword="true"/> when the table is present.</returns>
    /// <remarks>
    /// Opened READ-ONLY, so a case that asserts a table's ABSENCE cannot create the very file it is
    /// checking for as a side effect of checking.
    /// </remarks>
    private static bool TableExists(string dataDirectory, string tableName) =>
        ScalarLong(
            dataDirectory,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '" + tableName + "'") == 1;

    /// <summary>Reads one integer from the database under a data directory.</summary>
    /// <param name="dataDirectory">The directory holding <c>test.db</c>.</param>
    /// <param name="sql">The statement to read one value from.</param>
    /// <returns>The value.</returns>
    private static long ScalarLong(string dataDirectory, string sql)
    {
        using SqliteConnection connection = OpenForInspection(dataDirectory);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Reads one string from the database under a data directory.</summary>
    /// <param name="dataDirectory">The directory holding <c>test.db</c>.</param>
    /// <param name="sql">The statement to read one value from.</param>
    /// <returns>The value.</returns>
    private static string ScalarText(string dataDirectory, string sql)
    {
        using SqliteConnection connection = OpenForInspection(dataDirectory);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>Executes one statement against the database under a data directory.</summary>
    /// <param name="dataDirectory">The directory holding <c>test.db</c>.</param>
    /// <param name="sql">The statement to execute.</param>
    private static void Execute(string dataDirectory, string sql)
    {
        SqliteConnectionStringBuilder writable = new()
        {
            DataSource = Path.Combine(dataDirectory, "test.db"),
            Mode = SqliteOpenMode.ReadWrite,
        };

        using SqliteConnection connection = new(writable.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        _ = command.ExecuteNonQuery();
    }

    /// <summary>Opens the database under a data directory read-only.</summary>
    /// <param name="dataDirectory">The directory holding <c>test.db</c>.</param>
    /// <returns>An open connection.</returns>
    private static SqliteConnection OpenForInspection(string dataDirectory)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(dataDirectory, "test.db"),
            Mode = SqliteOpenMode.ReadOnly,
        };

        SqliteConnection connection = new(builder.ConnectionString);
        connection.Open();

        return connection;
    }

    // ==============================================================================================
    //  5. THE PROVISIONED SEAMS AND THE NEGATIVES THAT REMAIN REACHABLE
    // ==============================================================================================
    //
    //  WHAT THIS SECTION DELIBERATELY DOES NOT ASSERT, AND WHY. Six seams of this service could ship as
    //  refusals - an engine that fails every write, two factories answering
    //  E_NO_IMPLEMENTATION, and three runtimes returning the datastore failure value - and a suite
    //  here could pin those refusals as intended behaviour. They are not intended. The AAP requires this
    //  service to be the one that generates and executes SQL and the only one holding a storage provider
    //  [AAP 0.1.1], its own file schema requires a conflict mismatch to surface as gRPC Aborted with a
    //  populated ConflictDetail and a database error's statement text to be provably redacted, and
    //  neither is reachable through a seam that refuses before it reads anything. So the seams are
    //  provisioned implementations, and these cases assert what those implementations do.
    //
    //  THE NEGATIVES ARE NOT ABSENT - THEY SIT ON THE INPUTS THAT GENUINELY EARN THEM. An unknown
    //  data-object name still resolves to nothing, because PowerBuilder leaves a datastore whose data
    //  object failed to load in exactly that state and the retrieval task detects it. An unknown session
    //  handle still refuses, with the transaction contract's own code rather than a not-implemented one.
    //  A malformed grid syntax still fails with a diagnostic. What a provisioned seam adds on top of those
    //  refusals is that a WELL-FORMED request against a LIVE session succeeds, which is the whole
    //  difference between a provisioned service and a documented gap.
    // ==============================================================================================
    /// <summary>
    /// The SQLite engine echoes the caller's dialect, refuses before it is connected, and then performs
    /// a real connect, execute, commit and rollback.
    /// </summary>
    /// <remarks>
    /// THE DIALECT ASSERTION IS THE LOAD-BEARING ONE, AND A REAL CONNECTION DOES NOT SOFTEN IT. The
    /// paging dispatcher classifies this string with two arms and a not-implemented else
    /// [AAP 0.6.4], and holding a real connection must not alter what a caller is told about its
    /// own dialect - the string is echoed back exactly as supplied, with no SQLite arm invented for it.
    /// The refuse-before-connect assertions guard the property that matters alongside it: a
    /// commit that reported success without a connection would tell a caller data had been written that
    /// never was.
    /// </remarks>
    [Fact]
    public void TheSqliteEngineEchoesTheDialectRefusesUnconnectedAndThenWritesForReal()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-engine-{Guid.NewGuid():n}");

        try
        {
            using SqliteConnectionFactory storage = CreateStorage(directory);
            using SqliteTransactionEngine engine = new(
                storage,
                NullLogger<SqliteTransactionEngine>.Instance);

            TransactionData descriptor = new() { Dbms = "MSS Microsoft SQL Server" };
            engine.ApplyConnectionFields(in descriptor);

            // Echoed unchanged, so the paging dispatcher classifies exactly as the legacy does.
            Assert.Equal("MSS Microsoft SQL Server", engine.Dbms);
            Assert.Equal(0, engine.DbHandle);

            // Before a connection exists, a write refuses rather than claiming a phantom success.
            Assert.Equal(-1, engine.Execute("SELECT 1", TestContext.Current.CancellationToken).SqlCode);
            Assert.Contains(
                "not connected",
                engine.Execute("SELECT 1", TestContext.Current.CancellationToken).SqlErrText,
                StringComparison.OrdinalIgnoreCase);

            Assert.Equal(0, engine.Connect(TestContext.Current.CancellationToken).SqlCode);
            Assert.Equal(SqliteTransactionEngine.ConnectedHandle, engine.DbHandle);

            Assert.Equal(
                0,
                engine.Execute("CREATE TABLE probe (id INTEGER PRIMARY KEY, name TEXT)", TestContext.Current.CancellationToken).SqlCode);
            Assert.Equal(0, engine.Execute("INSERT INTO probe (name) VALUES ('written')", TestContext.Current.CancellationToken).SqlCode);
            Assert.Equal(0, engine.Commit().SqlCode);

            // The rollback discards work the commit above did not cover, and the committed row survives.
            Assert.Equal(0, engine.Execute("INSERT INTO probe (name) VALUES ('discarded')", TestContext.Current.CancellationToken).SqlCode);
            Assert.Equal(0, engine.Rollback().SqlCode);

            using Microsoft.Data.Sqlite.SqliteCommand survivors = engine.CreateCommand();
            survivors.CommandText = "SELECT count(*) FROM probe";
            Assert.Equal(1L, Convert.ToInt64(survivors.ExecuteScalar(), CultureInfo.InvariantCulture));

            Assert.Equal(0, engine.Disconnect().SqlCode);
            Assert.Equal(0, engine.DbHandle);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// The update factory refuses an unknown session with the transaction contract's own code, and never
    /// hands back a half-built pair.
    /// </summary>
    /// <remarks>
    /// THIS IS THE NEGATIVE THAT SURVIVED PROVISIONING, AND ITS CODE CHANGED FOR A REASON. A refusing
    /// factory answered <c>E_NO_IMPLEMENTATION</c>, which told a caller the capability does not exist.
    /// The capability now exists, so the honest answer to an unusable handle is
    /// <c>E_INVALID_TRANSACTION</c> - what the oracle returns when asked to act on a transaction it
    /// cannot use [<c>n_cst_thread_trans.sru:L113</c>]. The null assertion is unchanged: a caller that
    /// was refused holds nothing it could mistake for a working task.
    /// </remarks>
    [Fact]
    public void TheUpdateFactoryRefusesAnUnknownSessionWithoutBuildingAPair()
    {
        using ServiceProvider provider = BuildTaskFactoryProvider();

        IUpdateTaskFactory factory = provider.GetRequiredService<IUpdateTaskFactory>();

        Assert.Equal(
            RetCode.E_INVALID_TRANSACTION,
            factory.TryCreate("no-such-session", out IUpdateTaskSurface? surface));
        Assert.Null(surface);
    }

    /// <summary>
    /// Both factories build a real, joined proxy pair rather than refusing.
    /// </summary>
    /// <remarks>
    /// THE PAIR IS WHAT IS BEING ASSERTED, NOT MERELY A NON-NULL RESULT. The AAP forbids flattening the
    /// caller-side proxy and the worker-side task into one object [AAP 0.4.5.4], so a successful create
    /// must yield BOTH halves with the worker joined to the proxy that will receive its published
    /// results. The command factory hands both back explicitly, which is what these assertions read; the
    /// update factory hides its pair behind the surface, so its pair is proven through the surface
    /// accepting the contract calls that only a live pair can service.
    /// </remarks>
    [Fact]
    public void TheFactoriesBuildAJoinedProxyPair()
    {
        using ServiceProvider provider = BuildTaskFactoryProvider();

        ICommandTaskFactory commands = provider.GetRequiredService<ICommandTaskFactory>();

        Assert.Equal(RetCode.OK, commands.Create(out CommandTaskComponents? components));
        Assert.NotNull(components);

        Assert.NotNull(components.Proxy);
        Assert.NotNull(components.Worker);

        // The worker holds the disposable half of the pair, and the proxy is released with it.
        components.Worker.Dispose();
    }

    /// <summary>
    /// The data-object runtime resolves the evidenced fixture and still answers nothing for a name it
    /// has never been given.
    /// </summary>
    /// <remarks>
    /// THE EVIDENCED DEFINITION IS TRANSCRIBED, NOT READ. The legacy tree is read-only and is the
    /// behavioural oracle [AAP C-C], so the one updatable DataWindow's literals - its select, its sort
    /// including the trailing space the oracle emits, and its update table - live in the catalogue as
    /// transcriptions of <c>dw_sqlite.srd:L14</c> rather than as a parse of the file at run time. The
    /// unknown-name arm is the negative that survived: it is a REACHED PowerBuilder behaviour the
    /// retrieval task detects through its own units probe, not a gap.
    /// </remarks>
    [Fact]
    public void TheDataObjectRuntimeResolvesTheEvidencedFixtureAndOnlyThat()
    {
        SqliteDataObjectRuntime runtime = new(
            new DataObjectDefinitionCatalogue(),
            new DataWindowStoreBindings(),
            NullLogger<SqliteDataObjectRuntime>.Instance);

        Assert.True(runtime.TryResolveDefinition(
            DataObjectDefinitionCatalogue.EvidencedDataObject,
            out DataObjectDefinition? evidenced));
        Assert.NotNull(evidenced);
        Assert.Equal(DataObjectDefinitionCatalogue.EvidencedSelect, evidenced.SqlSelect);

        // The trailing space is the oracle's, and it is preserved rather than trimmed.
        Assert.Equal(DataObjectDefinitionCatalogue.EvidencedSort, evidenced.Sort);

        // The units value is load bearing: the retrieval task probes DataWindow.Units to tell a resolved
        // data object from an unresolved one [n_cst_thread_task_sqlquery.sru:L554-L557].
        Assert.Equal(DataObjectDefinitionCatalogue.ResolvedUnits, evidenced.Units);

        Assert.False(runtime.TryResolveDefinition("d_never_registered", out DataObjectDefinition? absent));
        Assert.Null(absent);
    }

    /// <summary>
    /// The carrier runtime creates a store from well-formed grid syntax and fails a malformed one with a
    /// diagnostic.
    /// </summary>
    [Fact]
    public void TheCarrierRuntimeCreatesFromSyntaxAndDiagnosesMalformedSyntax()
    {
        DataObjectDefinitionCatalogue catalogue = new();
        DataWindowStoreBindings bindings = new();
        SqliteQueryDataWindowRuntime carriers = new(catalogue, bindings);
        SqlDataStoreFactory stores = new(
            new SqliteDataObjectRuntime(
                catalogue,
                bindings,
                NullLogger<SqliteDataObjectRuntime>.Instance),
            TimeProvider.System);

        string syntax = GridSyntax.From("SELECT id, name FROM COMPANY", ["id", "name"]);

        ISqlDataStore created = stores.Create(Buffers.CarrierThreadAffinity.WorkerThread);
        CarrierCreateOutcome success = carriers.CreateFromSyntax(created, syntax);

        Assert.Equal(Buffers.DataWindowBufferStore.DataStoreSuccess, success.Result);
        Assert.Equal(string.Empty, success.ErrorText);
        Assert.Equal("SELECT id, name FROM COMPANY", created.GetSqlSelect());

        // A syntax carrying no column declaration cannot describe a result, and says so.
        ISqlDataStore rejected = stores.Create(Buffers.CarrierThreadAffinity.WorkerThread);
        CarrierCreateOutcome failure = carriers.CreateFromSyntax(rejected, "table(retrieve=\"SELECT 1\")");

        Assert.Equal(Buffers.DataWindowBufferStore.DataStoreFailure, failure.Result);
        Assert.NotEmpty(failure.ErrorText);
    }

    /// <summary>
    /// The transaction surface derives real grid syntax, runs a real count query, and still vetoes
    /// nothing on the two retrieval hooks.
    /// </summary>
    /// <remarks>
    /// THE HOOK ASSERTIONS ARE UNCHANGED FROM THE REFUSING SURFACE, DELIBERATELY. Both hooks are
    /// vetoable NOTIFICATIONS, and PowerBuilder returns zero from an event nobody implemented, so a
    /// provisioned surface must prevent exactly as little as an unprovisioned one did. Answering a veto
    /// here would invent a refusal the legacy never issues.
    /// </remarks>
    [Fact]
    public async Task TheTransactionSurfaceDerivesSyntaxCountsForRealAndVetoesNothing()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-surface-{Guid.NewGuid():n}");

        try
        {
            using SqliteConnectionFactory storage = CreateStorage(directory);
            using IPooledTransaction transaction = CreateLiveTransaction(storage);

            Assert.Equal(0, transaction.Exec("CREATE TABLE probe (id INTEGER PRIMARY KEY, name TEXT)", TestContext.Current.CancellationToken));
            Assert.Equal(0, transaction.Exec("INSERT INTO probe (name) VALUES ('one'), ('two')", TestContext.Current.CancellationToken));

            SqliteQueryTransactionSurface surface = new(
                NullLogger<SqliteQueryTransactionSurface>.Instance);

            GridSyntaxOutcome syntax = surface.GridSyntaxFromSql(transaction, "SELECT id, name FROM probe");

            Assert.Equal(string.Empty, syntax.ErrorText);
            Assert.Contains("column=(name=id", syntax.Syntax, StringComparison.Ordinal);
            Assert.Contains("column=(name=name", syntax.Syntax, StringComparison.Ordinal);

            Buffers.DataWindowCarrier carrier = new(TimeProvider.System);
            Assert.Equal(RetCode.OK, surface.RaiseBeforeRetrieve(transaction, carrier));
            surface.RaiseAfterRetrieve(transaction, carrier, rowCount: 0);

            CountQueryOutcome counted = await surface.Query(
                transaction,
                "SELECT count(*) AS count FROM probe",
                TestContext.Current.CancellationToken);

            // THE RETURN CODE IS A ROW COUNT, NOT A RETURN CODE, and that is the legacy's own shape:
            // the count path tests it against 1 rather than against success
            // [n_cst_thread_task_sqlquery.sru:L851-L853]. One row retrieved is therefore the value here,
            // and it is what the caller's `rtCode = 1` arm needs to see.
            Assert.Equal(1L, counted.ReturnCode);
            Assert.NotNull(counted.Result);
            Assert.Equal(string.Empty, counted.ErrorText);

            // Two rows were inserted, so the scalar the count statement produced is 2 - read out of row
            // one, column one, exactly where the count path reads it [:L852].
            Assert.Equal(
                2L,
                Convert.ToInt64(
                    counted.Result.GetItemValue(1, 1, DwBuffer.Primary),
                    CultureInfo.InvariantCulture));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// The caller's cancellation outranks any work the surface would otherwise start.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task CancellationOutranksTheSurfacesWork()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pfw-cancel-{Guid.NewGuid():n}");

        try
        {
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            using SqliteConnectionFactory storage = CreateStorage(directory);
            using IPooledTransaction transaction = CreateLiveTransaction(storage);

            SqliteQueryTransactionSurface surface = new(
                NullLogger<SqliteQueryTransactionSurface>.Instance);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await surface.Query(transaction, "SELECT 1", cancelled.Token));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    /// <summary>
    /// A bound host resolves its own task at the sole index, and refuses every other index.
    /// </summary>
    /// <remarks>
    /// THE COMMIT-SIGNAL WALK IS THE ONE CALLER AND IT IS WHY THIS MATTERS. <c>SqlTaskBase.OnCommitted</c>
    /// counts DOWN from the host's task index and sets the commit signal of every task it resolves. While
    /// the host refused every index, that walk found nothing, no signal was ever set, and the caller-side
    /// <c>IsCommitted()</c> could not become true however the transaction actually ended - so a committed
    /// command reported as uncommitted. Rebinding is refused because a host serving two tasks would hand
    /// the walk the wrong one.
    /// </remarks>
    [Fact]
    public void ABoundHostResolvesItsOwnTaskAndRefusesEveryOtherIndex()
    {
        using ServiceProvider provider = BuildRuntimeProvider();

        ICommandTaskFactory commands = provider.GetRequiredService<ICommandTaskFactory>();

        Assert.Equal(RetCode.OK, commands.Create(out CommandTaskComponents? composed));
        Assert.NotNull(composed);

        try
        {
            // The factory bound the worker to its host, which is what makes the commit signal reachable:
            // the proxy resolved the worker through the host during initialization.
            Assert.False(composed!.Proxy.IsCommitted());

            PersistenceSqlTaskHost host = new(
                NullLogger<PersistenceSqlTaskHost>.Instance,
                Errors.SqlRedactor.Instance);

            host.BindTask(composed.Worker);

            Assert.Equal(
                RetCode.OK,
                host.GetTask(PersistenceSqlTaskHost.SoleTaskIndex, out SqlTaskBase? resolved));
            Assert.Same(composed.Worker, resolved);

            // Any other position names nothing, and saying so is the accurate answer.
            Assert.Equal(RetCode.E_OUT_OF_BOUND, host.GetTask(2, out SqlTaskBase? beyond));
            Assert.Null(beyond);

            Assert.Equal(RetCode.E_OUT_OF_BOUND, host.GetTask(0, out SqlTaskBase? below));
            Assert.Null(below);

            // Rebinding is refused rather than silently accepted.
            Assert.Throws<InvalidOperationException>(() => host.BindTask(composed.Worker));
        }
        finally
        {
            composed!.Proxy.Dispose();
            composed.Worker.Dispose();
        }
    }

    /// <summary>
    /// The worker-side host carries the task's data bag and forwards its faults to the caller side.
    /// </summary>
    /// <remarks>
    /// The bag is genuinely load bearing - it is where a task holds its datastore cache - and the fault
    /// forwarding is the marshalling boundary itself: the worker side raises, the host forwards, the
    /// caller-side collector reads.
    /// </remarks>
    [Fact]
    public void TheWorkerHostCarriesTheBagAndForwardsFaults()
    {
        QueryFaultRecorder collector = new();
        PersistenceSqlTaskHost host = new(
            NullLogger<PersistenceSqlTaskHost>.Instance,
            SqlRedactor.Instance,
            collector,
            new QueryFaultProxy(collector));

        Assert.False(host.IsMainThread);
        Assert.False(host.IsCancelled);
        Assert.Equal(PersistenceSqlTaskHost.SoleTaskIndex, host.TaskIndex);
        Assert.NotNull(host.ParentTasking);

        // AN UNBOUND HOST GENUINELY HOLDS NO TASK, and out-of-bound says so rather than inventing one.
        Assert.Equal(RetCode.E_OUT_OF_BOUND, host.GetTask(1, out SqlTaskBase? sibling));
        Assert.Null(sibling);

        Assert.False(host.HasData("cache"));
        Assert.Null(host.GetData("cache"));
        Assert.Equal(RetCode.OK, host.SetData("cache", "value"));
        Assert.True(host.HasData("cache"));
        Assert.Equal("value", host.GetData("cache"));

        // Uninitialising releases the cache deterministically rather than waiting for a finaliser.
        host.OnUninit();
        Assert.False(host.HasData("cache"));

        Assert.Equal(Buffers.DataWindowBufferStore.EventContinue, host.OnPrepare());
        Assert.Equal(Buffers.DataWindowBufferStore.EventContinue, host.OnError(RetCode.FAILED, "broken"));
        Assert.Equal(Buffers.DataWindowBufferStore.EventContinue, host.OnNotify(1, 2, "progress"));

        QueryFaultSnapshot snapshot = collector.Snapshot();
        Assert.Equal(RetCode.FAILED, snapshot.Code);
        Assert.Equal("broken", snapshot.ErrorText);
    }

    // ---------------------------------------------------------------------------------------------
    //  2c. THE WORKER HOST IS THE FINAL SINK FOR EVERY FRAMEWORK DIAGNOSTIC, SO IT IS WHERE IT MASKS
    // ---------------------------------------------------------------------------------------------
    //
    //  Every OnError raise in the whole task layer funnels through SqlTaskBase.OnError into this one
    //  member, and several of those raises carry statement-bearing text rather than a fixed sentence -
    //  the query task forwards the runtime's Modify diagnostic, which quotes the whole rejected
    //  `DataWindow.Table.Select='...'` assignment, and the command task forwards the driver's own
    //  message, which echoes offending values. Masking one raise site at a time would be a list that
    //  goes stale; masking the sink covers every present and future raise by construction.
    //
    //  THE ASYMMETRY IS THE POINT. The in-band forward keeps the text VERBATIM, because that is the
    //  diagnostic channel the contract carries out to the caller and the oracle delivers it unaltered
    //  (constraint C-B). Only the log record is masked (constraint C-F, AAP 0.6.3.8 - the oracle's own
    //  logger redacts nothing at all, so this is a required addition rather than a ported behaviour).

    [Fact]
    public void TheWorkerHostForwardsTheDiagnosticVerbatimAndMasksOnlyWhatItLogs()
    {
        // Shaped exactly like the real one: a fixed prefix, then the whole rejected property assignment
        // with a statement inside it, then a bare numeric.
        const string Raw =
            "Cannot set property: DataWindow.Table.Select='SELECT ID FROM COMPANY WHERE NAME = "
            + "\"Zhang Wei\"' at offset 82500";

        QueryFaultRecorder collector = new();
        RecordingLogger<PersistenceSqlTaskHost> logger = new();
        PersistenceSqlTaskHost host = new(
            logger,
            Errors.SqlRedactor.Instance,
            collector,
            new QueryFaultProxy(collector));

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.E_INTERNAL_ERROR, Raw));

        // IN BAND: byte for byte. A consumer classifying on the text still sees what the oracle sends.
        Assert.Equal(Raw, collector.Snapshot().ErrorText);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, collector.Snapshot().Code);

        // IN THE LOG: the literals are gone and nothing else is.
        string record = Assert.Single(logger.Records);
        Assert.DoesNotContain("Zhang Wei", record, StringComparison.Ordinal);
        Assert.DoesNotContain("82500", record, StringComparison.Ordinal);
        Assert.Contains(Errors.SqlRedactor.DefaultPlaceholder, record, StringComparison.Ordinal);

        // The message SHAPE survives, which is what makes the record still worth reading: the prefix, the
        // property name and the numeric code are all present.
        Assert.Contains("Cannot set property", record, StringComparison.Ordinal);
        Assert.Contains("DataWindow.Table.Select", record, StringComparison.Ordinal);
        Assert.Contains(
            RetCode.E_INTERNAL_ERROR.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerHostMasksAValueQuotedInsideTheProvidersOwnEnvelope()
    {
        // 🔴 THE SHAPE THE STRICT SCANNER GETS WRONG, AND IT IS THE ORDINARY ONE. One arm of what reaches
        // this channel is the transaction's own message
        // [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L390], which arrives inside
        // Microsoft.Data.Sqlite's envelope. When the diagnosis itself quotes a value - an unnamed CHECK
        // constraint failure does - the single quotes NEST, the SQL literal scanner closes the outer literal
        // at the inner opening quote, and the value between the two pairs is copied through verbatim. That
        // was observed in this service's own log before the routing was fixed, so it is pinned here.
        const string Envelope = "SQLite Error 19: 'CHECK constraint failed: NAME <> 'Zhang Wei''.";

        QueryFaultRecorder collector = new();
        RecordingLogger<PersistenceSqlTaskHost> logger = new();
        PersistenceSqlTaskHost host = new(
            logger,
            Errors.SqlRedactor.Instance,
            collector,
            new QueryFaultProxy(collector));

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.E_DB_ERROR, Envelope));

        // IN BAND: byte for byte, exactly as before - the contract channel is never masked (constraint C-B).
        Assert.Equal(Envelope, collector.Snapshot().ErrorText);

        string record = Assert.Single(logger.Records);

        // THE VALUE IS GONE.
        Assert.DoesNotContain("Zhang Wei", record, StringComparison.Ordinal);
        Assert.Contains(Errors.SqlRedactor.DefaultPlaceholder, record, StringComparison.Ordinal);

        // AND THE DIAGNOSIS STILL READS, which is the half the strict policy also destroyed: the result
        // code and the condition survive, so the record is worth reading rather than merely safe.
        Assert.Contains("SQLite Error 19", record, StringComparison.Ordinal);
        Assert.Contains("CHECK constraint failed", record, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerHostStillSendsStatementBearingTextThroughTheInjectedSeam()
    {
        // THE SEAM STILL GOVERNS WHAT IT WAS INTRODUCED FOR. The branch added for the envelope must not
        // take statement text away from the injected redactor, because that text - a fixed prefix plus the
        // externally supplied SORT or FILTER expression - is precisely the class a test substitutes a
        // redactor to govern. A recording redactor proves the seam is still consulted for it.
        const string StatementBearing =
            "Cannot set property: DataWindow.Table.Select='SELECT ID FROM COMPANY WHERE AGE = 41'";

        RecordingRedactor recording = new();
        RecordingLogger<PersistenceSqlTaskHost> logger = new();
        PersistenceSqlTaskHost host = new(logger, recording);

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.E_INTERNAL_ERROR, StatementBearing));

        Assert.Equal(StatementBearing, Assert.Single(recording.Seen));

        // AND THE ENVELOPE DOES NOT REACH IT, which is the other half of the same property.
        recording.Seen.Clear();

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.E_DB_ERROR, "SQLite Error 19: 'CHECK constraint failed: NAME <> 'Ada''."));

        Assert.Empty(recording.Seen);
    }

    /// <summary>
    /// An <see cref="Errors.ISqlRedactor"/> that records what it was asked to mask and then delegates to
    /// the real policy, so a test can assert WHICH texts reached the seam without weakening any of them.
    /// </summary>
    private sealed class RecordingRedactor : Errors.ISqlRedactor
    {
        /// <summary>Gets the texts this instance was handed, in order.</summary>
        internal List<string> Seen { get; } = [];

        /// <inheritdoc/>
        public string Redact([System.Diagnostics.CodeAnalysis.AllowNull] string statement)
        {
            Seen.Add(statement ?? string.Empty);

            return Errors.SqlRedactor.Instance.Redact(statement);
        }
    }

    [Fact]
    public void TheWorkerHostMasksNotificationTextOnTheSameGrounds()
    {
        // A LEVEL IS NOT AN ACCESS CONTROL. Notifications are recorded at trace level, but they land in
        // the same sink as everything else and their text is free-form from the task layer, so the same
        // rule applies.
        QueryFaultRecorder collector = new();
        RecordingLogger<PersistenceSqlTaskHost> logger = new();
        PersistenceSqlTaskHost host = new(
            logger,
            Errors.SqlRedactor.Instance,
            collector,
            new QueryFaultProxy(collector));

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnNotify(3, 4, "Retrieved 4711 rows for 'COMPANY'"));

        string record = Assert.Single(logger.Records);
        Assert.DoesNotContain("4711", record, StringComparison.Ordinal);
        Assert.DoesNotContain("COMPANY", record, StringComparison.Ordinal);
        Assert.Contains("Retrieved", record, StringComparison.Ordinal);
        Assert.Contains(Errors.SqlRedactor.DefaultPlaceholder, record, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostWithNoSinkStillMasksItsLogAndDoesNotThrowOnANullDiagnostic()
    {
        // The public constructor leaves both collaborators null - that is the shape the composition root
        // uses for a task nobody is collecting for - and a null diagnostic is a legitimate legacy outcome
        // rather than an error in its own right.
        RecordingLogger<PersistenceSqlTaskHost> logger = new();
        PersistenceSqlTaskHost host = new(logger, Errors.SqlRedactor.Instance);

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.FAILED, null!));
        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnNotify(0, 0, null!));

        Assert.Equal(2, logger.Records.Count);
    }

    /// <summary>
    /// The framework-error channel hands the sink the raw text and the log record a redacted one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO DESTINATIONS ARE DIFFERENT ON PURPOSE AND THIS PINS BOTH HALVES. The sink is the CONTRACT
    /// channel - it is what the caller is told about its own request, and the legacy's general-error event
    /// carries the text in full - so masking it there would change observable behaviour. The log record is
    /// not a contract, and it is the one that lands in a deployment's default log store.
    /// </para>
    /// <para>
    /// The payload used here is not invented. <c>Tasks/SqlQueryTask.cs</c> composes exactly this shape
    /// when a carrier rejects a caller's expression: the fixed prefix
    /// <see cref="FullStateCodec.SetFilterMessagePrefix"/> concatenated with the externally supplied
    /// filter, UNTRIMMED and in full. A filter expression carries literal comparison values, so a
    /// verbatim log record writes caller data into the log of the one service whose whole disclosure
    /// posture is that no literal reaches a log or a response.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheWorkerHostRedactsTheLogRecordAndNotTheContractChannel()
    {
        const string Filter = "name = 'Ada Lovelace' and salary > 92500";
        string raised = FullStateCodec.SetFilterMessagePrefix + Filter;

        QueryFaultRecorder collector = new();
        RecordingLogger<PersistenceSqlTaskHost> log = new();
        PersistenceSqlTaskHost host = new(log, SqlRedactor.Instance, collector, new QueryFaultProxy(collector));

        Assert.Equal(
            Buffers.DataWindowBufferStore.EventContinue,
            host.OnError(RetCode.E_INVALID_ARGUMENT, raised));

        // The contract channel is untouched, character for character.
        QueryFaultSnapshot snapshot = collector.Snapshot();
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, snapshot.Code);
        Assert.Equal(raised, snapshot.ErrorText);

        string record = Assert.Single(log.Records);

        // Neither literal survives into the record, and the string literal's quoted body is what a
        // verbatim log would have disclosed.
        Assert.DoesNotContain("Ada Lovelace", record, StringComparison.Ordinal);
        Assert.DoesNotContain("92500", record, StringComparison.Ordinal);

        // What DOES survive is the classification, the safe length metadata and the masked structure, so
        // an operator can still see that a value was present and where the expression failed.
        Assert.Contains(
            RetCode.E_INVALID_ARGUMENT.ToString(CultureInfo.InvariantCulture),
            record,
            StringComparison.Ordinal);
        Assert.Contains(
            raised.Length.ToString(CultureInfo.InvariantCulture),
            record,
            StringComparison.Ordinal);
        Assert.Contains("name =", record, StringComparison.Ordinal);
        Assert.Contains("salary >", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// The caller-side proxy hands a database error to the collector rather than logging it again.
    /// </summary>
    /// <remarks>
    /// The statement field carries the complete generated statement including interpolated literal
    /// values, and the task that raised it has already recorded a redacted projection - so a second log
    /// record here would be a second opportunity to leak the same thing. The proxy stores and nothing
    /// more.
    /// </remarks>
    [Fact]
    public void TheCallerSideProxyHandsTheDatabaseErrorStraightToTheCollector()
    {
        QueryFaultRecorder collector = new();
        QueryFaultProxy proxy = new(collector);

        Errors.DbErrorData error = Errors.DbErrorData.FromStatement(
            sqlDbCode: 2627,
            sqlErrText: "duplicate key",
            sqlSyntax: "INSERT INTO COMPANY (ID) VALUES (7)",
            buffer: DwBuffer.Primary,
            row: 3);

        proxy.OnDbError(in error);

        QueryFaultSnapshot snapshot = collector.Snapshot();
        Assert.True(snapshot.HasDbError);
        Assert.Equal(2627, snapshot.DbError.SqlDbCode);
        Assert.Equal(3, snapshot.DbError.Row);
    }

    /// <summary>
    /// Builds a provider carrying the WHOLE storage and SQL layer, which is what a factory needs.
    /// </summary>
    /// <param name="dataDirectory">The storage directory to configure.</param>
    /// <returns>A provider the task factories can be resolved from.</returns>
    /// <remarks>
    /// Composed through the host's own registration methods rather than by hand, so these cases exercise
    /// the same graph the service builds instead of a parallel one that could drift away from it unnoticed.
    /// </remarks>
    private static ServiceProvider BuildSqlLayerProvider(string dataDirectory)
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();
        services.AddOptions<PersistenceOptions>().Configure(options =>
        {
            options.Sqlite.DataDirectory = dataDirectory;
            options.Jwt.Authority = TrustedIssuer;
            options.Jwt.Audience = TrustedAudience;
        });

        _ = services
            .AddPersistenceStorage()
            .AddPersistenceSqlLayer()
            .AddPersistencePublishedSurface();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a provider carrying only what the startup gate reads.
    /// </summary>
    /// <param name="dataDirectory">The storage directory to configure.</param>
    /// <returns>A provider the gate can be run against.</returns>
    /// <remarks>
    /// THE FULL RUNTIME GRAPH, NOT A MINIMAL ONE, because the gate now resolves every SQL seam as well as
    /// probing the storage directory. A cut-down provider would fail the graph check for a reason that has
    /// nothing to do with the directory each of these cases is actually about. The ORDERING inside the
    /// gate is what keeps them honest anyway: the directory is validated first, so a case supplying an
    /// unusable directory still terminates for the directory's reason and never reaches the graph.
    /// </remarks>
    private static ServiceProvider BuildGateProvider(string dataDirectory) =>
        BuildRuntimeGraphProvider(dataDirectory, omitEngine: false);

    /// <summary>
    /// Builds a storage seam over a temporary directory, bypassing configuration binding so a case
    /// depends on nothing but the path it chose.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file should live in.</param>
    /// <returns>A seam that has not yet opened anything.</returns>
    /// <remarks>
    /// THE DIRECTORY IS CREATED HERE BECAUSE THE STARTUP GATE CREATES IT IN PRODUCTION. The gate under
    /// test in section 4 above is what guarantees a writable storage directory exists before the service
    /// serves anything, so a case exercising the layers BEHIND that gate has to stand where the gate
    /// leaves them. Leaving it absent would assert a state the service never reaches.
    /// </remarks>
    private static SqliteConnectionFactory CreateStorage(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);

        return new SqliteConnectionFactory(
            Options.Create(new PersistenceOptions
            {
                Sqlite = new SqliteOptions
                {
                    DataDirectory = dataDirectory,
                    DatabaseFileName = "test.db",
                    Mode = "rwc",
                    Journal = "DELETE",
                },
            }),
            NullLogger<SqliteConnectionFactory>.Instance,
            TimeProvider.System);
    }

    /// <summary>
    /// Produces a pooled transaction over a live SQLite engine, connected and ready to execute.
    /// </summary>
    /// <param name="storage">The storage seam the engine composes its connection string from.</param>
    /// <returns>A connected pooled transaction the caller owns and disposes.</returns>
    /// <remarks>
    /// THE REAL ACTIVATOR, NOT A PARALLEL GRAPH. These cases go through the same
    /// <see cref="PooledTransactionActivator"/> the host uses, so a change to how the host composes a
    /// pooled transaction cannot drift away from what is asserted here without a case noticing.
    /// </remarks>
    private static IPooledTransaction CreateLiveTransaction(SqliteConnectionFactory storage)
    {
        IPooledTransaction transaction = new PooledTransactionActivator(
            () => new SqliteTransactionEngine(storage, NullLogger<SqliteTransactionEngine>.Instance),
            TimeProvider.System).CreateDefault();

        transaction.AutoCommit = true;
        Assert.Equal(0, transaction.Connect());

        return transaction;
    }

    /// <summary>
    /// Builds the service's real runtime graph, optionally with the transaction engine withheld.
    /// </summary>
    /// <param name="dataDirectory">The storage directory to configure.</param>
    /// <param name="omitEngine">
    /// When <see langword="true"/>, the transaction engine is pre-registered as a factory that throws, so
    /// the graph is composed but not producible - the closest a container can come to a seam whose
    /// registration is present and broken.
    /// </param>
    /// <param name="applyMigrationsOnStartup">Whether the host applies pending migrations as it starts.</param>
    /// <returns>A provider the gate can be run against.</returns>
    /// <remarks>
    /// THE OMISSION IS EXPRESSED AS A THROWING FACTORY RATHER THAN A MISSING REGISTRATION, because every
    /// registration in this service is <c>TryAdd</c>-shaped: removing one is impossible from outside, and
    /// a pre-registration wins. A registration that cannot produce its instance is also the more
    /// realistic fault - a seam whose constructor rejects its configuration - and the gate must catch it
    /// either way.
    /// </remarks>
    private static ServiceProvider BuildRuntimeGraphProvider(
        string dataDirectory,
        bool omitEngine,
        bool applyMigrationsOnStartup = false)
    {
        ServiceCollection services = new();

        services.AddLogging();

        if (omitEngine)
        {
            services.AddTransient<ITransactionEngine>(static _ => throw new InvalidOperationException(
                $"This case withholds {nameof(ITransactionEngine)} on purpose, standing in for a seam "
                + "whose constructor rejects its configuration."));
        }

        services.AddOptions<PersistenceOptions>().Configure(options =>
        {
            options.Sqlite.DataDirectory = dataDirectory;
            options.Sqlite.DatabaseFileName = "test.db";

            // OFF UNLESS A CASE ASKS FOR IT, WHICH IS ALSO THE SHIPPED DEFAULT. The directory cases in
            // section 4 assert that the gate leaves the storage directory exactly as it found it, which is
            // an assertion about the WRITABILITY PROBE and its debris; provisioning legitimately creates a
            // database file there, so a case that wants it must say so. The orchestrated posture - the
            // switch ON - is exercised by the provisioning cases, which pass true.
            options.Schema.ApplyMigrationsOnStartup = applyMigrationsOnStartup;

            options.Jwt.Authority = TrustedIssuer;
            options.Jwt.Audience = TrustedAudience;

            // The gate reads Value, which runs the validator, and the validator requires a permitted-caller
            // roster: the four gRPC contracts refuse a caller identity this service does not serve, and an
            // empty roster refuses everyone rather than permitting everyone. Supplied here so these rows
            // fail on the storage condition they exist to assert rather than on configuration.
            options.Jwt.PermittedCallers.Add(TokenSubject);
        });

        // THE GATE RESOLVES THE TRUST ANCHOR, SO THIS PROVIDER HAS TO CARRY IT. The gate loads it with
        // GetRequiredService rather than GetService deliberately: a configured-but-unreadable anchor
        // means the bearer handler cannot fetch the key set it validates every inbound token against,
        // so the fault has to stop the host. Resolving it OPTIONALLY would also silently tolerate the
        // registration being dropped, at which point the anchor would never be loaded and nothing would
        // notice - which is the failure mode the gate exists to prevent. The options here leave the
        // path empty, so this registration exercises the platform-default-trust branch; the pinned and
        // unreadable branches are covered directly by InternalTlsTrustTests.
        services.AddSingleton(provider => new InternalTlsTrust(
            provider.GetRequiredService<IOptions<PersistenceOptions>>().Value.InternalTls));

        services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();
        services.AddPersistenceDeterminismSeam();
        services.AddPersistenceStorage();
        services.AddPersistenceSqlLayer();
        services.AddPersistencePublishedSurface();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds a provider carrying the graph the two task factories resolve their collaborators from.
    /// </summary>
    /// <returns>A provider the factories can be resolved out of.</returns>
    /// <remarks>
    /// REGISTERED THROUGH THE SERVICE'S OWN EXTENSION, so this fixture cannot describe a graph the host
    /// does not actually build. Only the storage directory is overridden, because a factory case must not
    /// depend on whichever directory the ambient configuration happens to name.
    /// </remarks>
    private static ServiceProvider BuildTaskFactoryProvider()
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddOptions<PersistenceOptions>().Configure(static options =>
        {
            options.Sqlite.DataDirectory = Path.Combine(
                Path.GetTempPath(),
                $"pfw-factory-{Guid.NewGuid():n}");
            options.Sqlite.DatabaseFileName = "test.db";
            options.Jwt.Authority = TrustedIssuer;
            options.Jwt.Audience = TrustedAudience;
        });

        services.AddPersistenceDeterminismSeam();
        services.AddPersistenceStorage();
        services.AddPersistenceSqlLayer();
        services.AddPersistencePublishedSurface();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Removes a directory a case created, tolerating one that was never created at all.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file lives in.</param>
    /// <remarks>
    /// SCOPED TO A PATH THIS CASE ITSELF CHOSE UNDER THE TEMPORARY ROOT, and never to a configured
    /// storage directory: the persistence volume's state is what the paired characterization captures
    /// compare against, and a case that deleted it would invalidate them.
    /// </remarks>

    private static IPooledTransaction CreatePooledTransaction(string dataDirectory) =>
        new PooledTransactionActivator(
            () => CreateEngine(dataDirectory),
            TimeProvider.System).CreateDefault();

    /// <summary>
    /// Produces the production engine over a throwaway data directory.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file is created under.</param>
    /// <returns>The engine.</returns>
    private static SqliteTransactionEngine CreateEngine(string dataDirectory) =>
        new(
            CreateConnectionFactory(dataDirectory),
            NullLogger<SqliteTransactionEngine>.Instance);

    /// <summary>
    /// Produces the ONE seam that owns the legacy connection URI grammar, over a throwaway directory.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file is created under.</param>
    /// <returns>The factory.</returns>
    private static SqliteConnectionFactory CreateConnectionFactory(string dataDirectory)
    {
        PersistenceOptions options = new();
        options.Sqlite.DataDirectory = dataDirectory;

        return new SqliteConnectionFactory(
            Options.Create(options),
            NullLogger<SqliteConnectionFactory>.Instance,
            TimeProvider.System);
    }

    /// <summary>
    /// Removes a throwaway directory, tolerating one that was never created.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Builds a provider carrying the whole runtime and published surface.
    /// </summary>
    /// <returns>A provider the factory registrations can be resolved from.</returns>
    private static ServiceProvider BuildRuntimeProvider()
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddOptions<PersistenceOptions>().Configure(static options =>
        {
            options.Jwt.Authority = TrustedIssuer;
            options.Jwt.Audience = TrustedAudience;
        });
        services.AddPersistenceDeterminismSeam();
        services.AddPersistenceStorage();
        services.AddPersistenceSqlLayer();
        services.AddPersistencePublishedSurface();

        return services.BuildServiceProvider();
    }

    /// <summary>An engine that records nothing and connects to nothing.</summary>
    /// <remarks>
    /// Present so the composition cases can produce a REAL pooled transaction - through the real
    /// activator, so they exercise the same composition the host performs - without opening a database.
    /// It composes no SQLite engine, which is precisely the case the read side's defined negative covers.
    /// </remarks>
    private sealed class StubEngine : ITransactionEngine
    {
        /// <inheritdoc/>
        public int DbHandle => 0;

        /// <inheritdoc/>
        public string Dbms { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public bool AutoCommit { get; set; }

        /// <summary>Moves the auto-commit mode and answers success, because this double opens no transaction.</summary>
        /// <param name="autoCommit">The mode to put in force.</param>
        /// <returns>Always a succeeded state.</returns>
        /// <remarks>
        /// ROUTED THROUGH THE PROPERTY so whatever the property records still records. A double with no
        /// provider behind it has nothing the transition can fail on, which is the contract's own
        /// nothing-to-do case.
        /// </remarks>
        public SqlState TrySetAutoCommit(bool autoCommit)
        {
            AutoCommit = autoCommit;

            return SqlState.Succeeded();
        }

        /// <inheritdoc/>
        public void ApplyConnectionFields(in TransactionData transData) => Dbms = transData.Dbms;

        /// <inheritdoc/>
        public SqlState Connect(CancellationToken cancellationToken = default) =>
            SqlState.Failed(RetCode.E_NO_IMPLEMENTATION, "not connected");

        /// <inheritdoc/>
        public SqlState Disconnect() => SqlState.Succeeded();

        /// <inheritdoc/>
        public SqlState Commit() => SqlState.Failed(RetCode.E_NO_IMPLEMENTATION, "not connected");

        /// <inheritdoc/>
        public SqlState Rollback() => SqlState.Succeeded();

        /// <inheritdoc/>
        public SqlState Execute(string statement, CancellationToken cancellationToken = default) =>
            SqlState.Failed(RetCode.E_NO_IMPLEMENTATION, "not connected");

        // THE BOUND OVERLOAD IS WHERE THE WORK BELONGS, so the double implements it and lets the
        // single-string form forward. A double that implemented only the string form would let a
        // production type opt back into splicing literals without any row noticing.
        public SqlState Execute(in SqlCommandText command, CancellationToken cancellationToken = default) =>
            Execute(command.CanonicalText, cancellationToken);

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }

    /// <summary>Records whether the host was asked to stop.</summary>
    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        /// <summary>Whether a stop was requested.</summary>
        internal bool Stopped { get; private set; }

        /// <summary>
        /// The process exit code as it stood AT THE MOMENT the stop was requested, or
        /// <see langword="null"/> while no stop has been requested.
        /// </summary>
        /// <remarks>
        /// CAPTURED HERE BECAUSE THE ORDERING IS THE SUBSTANCE OF THE REQUIREMENT, not just the values.
        /// Reading <see cref="Environment.ExitCode"/> after the call returns cannot distinguish a code set
        /// before the stop request from one set after it, and only the first ordering survives a host that
        /// returns from its run loop promptly.
        /// </remarks>
        internal int? ExitCodeWhenStopped { get; private set; }

        /// <inheritdoc/>
        public CancellationToken ApplicationStarted => CancellationToken.None;

        /// <inheritdoc/>
        public CancellationToken ApplicationStopping => CancellationToken.None;

        /// <inheritdoc/>
        public CancellationToken ApplicationStopped => CancellationToken.None;

        /// <inheritdoc/>
        public void StopApplication()
        {
            ExitCodeWhenStopped = Environment.ExitCode;
            Stopped = true;
        }
    }

    /// <summary>A logger whose every write throws, standing in for a saturated or broken sink.</summary>
    /// <typeparam name="T">The category type.</typeparam>
    /// <remarks>
    /// EXISTS TO PROVE ONE THING: that reporting a structural fault and terminating on it are
    /// independent. A logging provider can fail - a full disk, a saturated sink, a formatter that throws
    /// on an argument it did not expect - and before the termination request was moved into a
    /// <c>finally</c>, such a failure propagated out of the mapping and left the process serving requests
    /// in a state its own invariants had already declared impossible.
    /// </remarks>
    private sealed class ThrowingLogger<T> : ILogger<T>
    {
        /// <summary>The fault every write raises.</summary>
        internal static readonly InvalidOperationException WriteFault =
            new("the log sink is unavailable");

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => throw WriteFault;
    }

    /// <summary>Captures formatted log records so a redaction claim can be checked.</summary>
    /// <typeparam name="T">The category type.</typeparam>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>The formatted records.</summary>
        internal List<string> Records { get; } = [];

        /// <summary>
        /// The exception each record was written WITH, in the same order, with a null entry where a record
        /// carried none.
        /// </summary>
        /// <remarks>
        /// CAPTURED BECAUSE THE FORMATTED MESSAGE IS ONLY HALF OF WHAT A SINK WRITES, and the half this
        /// recorder used to ignore was where a leak could hide in plain sight. Every provider renders an
        /// attached exception by calling <c>ToString()</c> on it, which emits the UNREDACTED message, every
        /// inner exception's unredacted message and the stack - so a record whose formatted text is perfectly
        /// redacted still publishes all of it when an exception is attached beside it. A redaction claim has
        /// to be made about both, which is why both are recorded here.
        /// </remarks>
        internal List<Exception?> Exceptions { get; } = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Records.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    /// <summary>A response stream that is never written.</summary>
    private sealed class StubStreamWriter : IServerStreamWriter<string>
    {
        /// <inheritdoc/>
        public WriteOptions? WriteOptions { get; set; }

        /// <inheritdoc/>
        public Task WriteAsync(string message) => Task.CompletedTask;
    }

    /// <summary>A request stream that is never read.</summary>
    private sealed class StubStreamReader : IAsyncStreamReader<string>
    {
        /// <inheritdoc/>
        public string Current => string.Empty;

        /// <inheritdoc/>
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromResult(false);
    }

    /// <summary>The minimum call context the interceptor reads.</summary>
    private sealed class StubCallContext : ServerCallContext
    {
        /// <summary>The call's cancellation token.</summary>
        private readonly CancellationToken _cancellationToken;

        /// <summary>Initializes the context.</summary>
        /// <param name="cancellationToken">The token the call reports.</param>
        internal StubCallContext(CancellationToken cancellationToken) =>
            _cancellationToken = cancellationToken;

        /// <inheritdoc/>
        protected override string MethodCore => "/persistence.v1.QueryService/Probe";

        /// <inheritdoc/>
        protected override string HostCore => "localhost";

        /// <inheritdoc/>
        protected override string PeerCore => "ipv4:127.0.0.1:0";

        /// <inheritdoc/>
        protected override DateTime DeadlineCore => DateTime.MaxValue;

        /// <inheritdoc/>
        protected override Metadata RequestHeadersCore => [];

        /// <inheritdoc/>
        protected override CancellationToken CancellationTokenCore => _cancellationToken;

        /// <inheritdoc/>
        protected override Metadata ResponseTrailersCore { get; } = [];

        /// <inheritdoc/>
        protected override Status StatusCore { get; set; }

        /// <inheritdoc/>
        protected override WriteOptions? WriteOptionsCore { get; set; }

        /// <inheritdoc/>
        protected override AuthContext AuthContextCore =>
            new(string.Empty, new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal));

        /// <inheritdoc/>
        protected override ContextPropagationToken CreatePropagationTokenCore(
            ContextPropagationOptions? options) =>
            throw new NotSupportedException("This context propagates nothing.");

        /// <inheritdoc/>
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
            Task.CompletedTask;
    }

    // ==============================================================================================
    //  2b. THE TWO NAMED SCOPE POLICIES
    // ==============================================================================================

    /// <summary>
    /// The scope parser reads a space-delimited claim by exact name.
    /// </summary>
    /// <param name="claimType">The claim the set arrives in.</param>
    /// <param name="claimValue">The claim's value.</param>
    /// <param name="required">The scope being asked about.</param>
    /// <param name="expected">Whether the parser should answer true.</param>
    /// <remarks>
    /// THE ENCODING IS THE ISSUER'S, NOT A GUESS. Security mints ONE <c>scope</c> claim carrying the
    /// granted set as a single space-delimited value
    /// [services/security-service/PowerFramework.Security/Tokens/TokenIssuer.cs], so reading it means
    /// splitting - and a parser that compared the whole claim value would never match a token carrying
    /// more than one scope, which is every token this system issues for a multi-scope request.
    /// </remarks>
    [Theory]
    // The single-scope shape, both ways round.
    [InlineData("scope", "persistence.read", "persistence.read", true)]
    [InlineData("scope", "persistence.read", "persistence.write", false)]
    // The space-delimited shape the issuer actually mints, at each position.
    [InlineData("scope", "persistence.read persistence.write", "persistence.read", true)]
    [InlineData("scope", "persistence.read persistence.write", "persistence.write", true)]
    [InlineData("scope", "a persistence.write z", "persistence.write", true)]
    // A SUPERSET IS NOT A NARROWING: an unrelated scope alongside the required one changes nothing.
    [InlineData("scope", "something.else persistence.read", "persistence.read", true)]
    // PREFIX AND SUBSTRING MATCHES MUST NOT SATISFY IT, which is what makes the comparison exact.
    [InlineData("scope", "persistence.readonly", "persistence.read", false)]
    [InlineData("scope", "xpersistence.read", "persistence.read", false)]
    [InlineData("scope", "persistence", "persistence.read", false)]
    // CASE-SENSITIVE, because scope names are case-sensitive strings in the OAuth framework.
    [InlineData("scope", "PERSISTENCE.READ", "persistence.read", false)]
    // The alternative claim name some issuers use. Accepted so a deployment pointed at such an issuer
    // does not fail closed for a reason nobody can see.
    [InlineData("scp", "persistence.write", "persistence.write", true)]
    // A claim that is not a scope claim is not consulted at all.
    [InlineData("role", "persistence.write", "persistence.write", false)]
    // Degenerate values answer false rather than throwing.
    [InlineData("scope", "", "persistence.read", false)]
    [InlineData("scope", "   ", "persistence.read", false)]
    // Tabs and line breaks are treated as delimiters, so a value that arrived with one still reads.
    [InlineData("scope", "persistence.read\tpersistence.write", "persistence.write", true)]
    [InlineData("scope", "persistence.read\npersistence.write", "persistence.write", true)]
    public void TheScopeParserReadsASpaceDelimitedClaimByExactName(
        string claimType,
        string claimValue,
        string required,
        bool expected)
    {
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(claimType, claimValue)], "Test"));

        Assert.Equal(expected, PersistenceAuthorizationPolicies.HasScope(user, required));
    }

    /// <summary>
    /// A principal with no claims at all, and a null principal, both answer false.
    /// </summary>
    [Fact]
    public void TheScopeParserAnswersFalseForNothingRatherThanThrowing()
    {
        Assert.False(PersistenceAuthorizationPolicies.HasScope(null, "persistence.read"));
        Assert.False(PersistenceAuthorizationPolicies.HasScope(new ClaimsPrincipal(), "persistence.read"));

        // The scope being asked about is a programming input rather than caller data, so an empty one is
        // an error and not a question.
        Assert.Throws<ArgumentException>(() =>
            PersistenceAuthorizationPolicies.HasScope(new ClaimsPrincipal(), string.Empty));
    }

    /// <summary>
    /// The registered policies admit exactly the credential that names their own scope.
    /// </summary>
    /// <param name="policy">The policy being evaluated.</param>
    /// <param name="grantedScope">The scope value on the principal's claim.</param>
    /// <param name="expected">Whether authorization should succeed.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// EVALUATED THROUGH THE CONTAINER'S OWN <see cref="IAuthorizationService"/>, so what is under test is
    /// the policy AS REGISTERED - the assertion and the authenticated-user requirement together - rather
    /// than a re-statement of it. Authentication alone satisfying every contract is the
    /// least-privilege failure this rules out.
    /// </remarks>
    [Theory]
    [InlineData("persistence.read", "persistence.read", true)]
    [InlineData("persistence.read", "persistence.write", false)]
    [InlineData("persistence.read", "persistence.read persistence.write", true)]
    [InlineData("persistence.write", "persistence.write", true)]
    [InlineData("persistence.write", "persistence.read", false)]
    [InlineData("persistence.write", "persistence.read persistence.write", true)]
    [InlineData("persistence.write", "", false)]
    public async Task EachPolicyAdmitsExactlyTheCredentialNamingItsOwnScope(
        string policy,
        string grantedScope,
        bool expected)
    {
        using CompositionHost host = CompositionHost.Create();

        IAuthorizationService authorization = host.Services
            .GetRequiredService<IAuthorizationService>();

        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim("sub", "composition-root-test"), new Claim("scope", grantedScope)],
            JwtBearerDefaults.AuthenticationScheme));

        AuthorizationResult result = await authorization.AuthorizeAsync(user, resource: null, policy);

        Assert.Equal(expected, result.Succeeded);
    }

    /// <summary>
    /// An unauthenticated principal satisfies neither policy, even carrying the scope.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// Both policies pair the scope assertion with <c>RequireAuthenticatedUser</c>, so a claim on an
    /// unauthenticated identity - which is what an unsigned or unverified credential would produce -
    /// cannot buy access on its own.
    /// </remarks>
    [Fact]
    public async Task AnUnauthenticatedPrincipalSatisfiesNeitherPolicy()
    {
        using CompositionHost host = CompositionHost.Create();

        IAuthorizationService authorization = host.Services
            .GetRequiredService<IAuthorizationService>();

        // NO authentication type, so IsAuthenticated is false.
        ClaimsPrincipal user = new(new ClaimsIdentity(
            [new Claim("scope", "persistence.read persistence.write")]));

        Assert.False((await authorization.AuthorizeAsync(
            user,
            resource: null,
            PersistenceAuthorizationPolicies.Read)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            user,
            resource: null,
            PersistenceAuthorizationPolicies.Write)).Succeeded);
    }

    /// <summary>
    /// Every routed RPC declares the scope policy its contract requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE MAPPING IS ASSERTED ROUTE BY ROUTE, not per contract, because C-08 is annotated PER RPC: its
    /// readers need read and its state changers need write, and a class-level policy could only pick one
    /// side. A new RPC arriving on any contract without an attribute fails here rather than silently
    /// inheriting nothing beyond authentication.
    /// </para>
    /// <para>
    /// The route pattern is <c>/{package}.{service}/{method}</c>, which is how the method name is
    /// recovered without reflecting over the implementation.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRoutedRpcDeclaresTheScopePolicyItsContractRequires()
    {
        // The C-08 split. Everything not named here writes.
        string[] transactionReaders =
        [
            "BeginSession", "EndSession", "GetTransactionData", "IsConnected", "GetDatabaseType",
            "GetSessionState", "GridSyntaxFromSql",
        ];

        using CompositionHost host = CompositionHost.Create();

        List<RouteEndpoint> mapped = host.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(static endpoint => endpoint.RoutePattern.RawText is string text
                && text.StartsWith("/persistence.v1.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(mapped);

        foreach (RouteEndpoint endpoint in mapped)
        {
            string route = endpoint.RoutePattern.RawText!;
            string method = route[(route.LastIndexOf('/') + 1)..];

            string expected = route.StartsWith("/persistence.v1.QueryService/", StringComparison.Ordinal)
                ? PersistenceAuthorizationPolicies.Read
                : route.StartsWith("/persistence.v1.TransactionService/", StringComparison.Ordinal)
                    ? transactionReaders.Contains(method, StringComparer.Ordinal)
                        ? PersistenceAuthorizationPolicies.Read
                        : PersistenceAuthorizationPolicies.Write
                    : PersistenceAuthorizationPolicies.Write;

            string[] declared =
            [
                .. endpoint.Metadata
                    .GetOrderedMetadata<IAuthorizeData>()
                    .Select(static data => data.Policy)
                    .Where(static policy => !string.IsNullOrEmpty(policy))
                    .Select(static policy => policy!),
            ];

            Assert.Contains(expected, declared);

            // AND EXACTLY ONE SCOPE POLICY PER ROUTE. Two would be ANDed, so a caller would need both
            // scopes - which is not what either contract asks of anyone.
            Assert.Single(declared);
        }
    }

    /// <summary>
    /// The two policy names are the two scope names the client requests and the issuer mints.
    /// </summary>
    /// <remarks>
    /// The one string that has to agree across three services, and nothing in the build enforces it: the
    /// client names it in a token request, the issuer copies it into the claim, and this service compares
    /// it. Pinned so a rename in any one of the three breaks a test rather than a deployment.
    /// </remarks>
    [Fact]
    public void ThePolicyNamesAreTheScopeNames()
    {
        Assert.Equal("persistence.read", PersistenceAuthorizationPolicies.Read);
        Assert.Equal("persistence.write", PersistenceAuthorizationPolicies.Write);
    }

}

/// <summary>
/// Boots the service's own composition root in process.
/// </summary>
/// <remarks>
/// The bearer handler is given a static verification configuration, which is what makes it skip
/// metadata retrieval entirely - so these cases prove the DEPLOYED authorization posture without
/// depending on a Security instance being reachable.
/// </remarks>
internal sealed class CompositionHost : WebApplicationFactory<Program>
{
    /// <summary>The issuer this host trusts, and the one it stamps into a minted credential.</summary>
    /// <remarks>
    /// OWNED HERE RATHER THAN BY A TEST CLASS, because it is a property of the HOST: it is supplied as host
    /// configuration so the service's own startup gate runs against it, and it is stamped into every token
    /// this factory mints. Two suites boot this host, so a copy in either of them would be a second
    /// spelling that could drift from the configuration it has to match.
    /// </remarks>
    internal const string TrustedIssuer = "https://security.invalid";

    /// <summary>The audience this host accepts, and the one it stamps into a minted credential.</summary>
    internal const string TrustedAudience = "powerframework-persistence-composition";

    /// <summary>The key credentials are signed and verified with.</summary>
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>Extra configuration this host was created with, applied last.</summary>
    private readonly IReadOnlyDictionary<string, string?> _settings;

    /// <summary>Builds a host carrying no extra configuration.</summary>
    private CompositionHost()
        : this(new Dictionary<string, string?>(StringComparer.Ordinal))
    {
    }

    /// <summary>Builds a host carrying extra configuration.</summary>
    /// <param name="settings">The configuration keys to apply after this host's own.</param>
    private CompositionHost(IReadOnlyDictionary<string, string?> settings) => _settings = settings;

    /// <summary>Creates a host.</summary>
    /// <returns>A started-on-first-use host.</returns>
    internal static CompositionHost Create() => new();

    /// <summary>Creates a host carrying extra configuration.</summary>
    /// <param name="settings">
    /// Configuration keys applied AFTER this host's own, so a case can override any of them, supplied as
    /// host configuration so the service's own startup gate and options validator judge them.
    /// </param>
    /// <returns>A started-on-first-use host.</returns>
    /// <remarks>
    /// The overload exists so a case can assert a behaviour of the DEPLOYED composition root that only
    /// appears under a particular setting - a metadata refresh interval, say - without standing up a second
    /// factory that would then be a second, drifting definition of "the host this service boots".
    /// </remarks>
    internal static CompositionHost Create(IReadOnlyDictionary<string, string?> settings) =>
        new(settings);

    /// <summary>
    /// The data directory THIS host is configured with, unique to this instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PER INSTANCE RATHER THAN A FIXED NAME. Configuring every host in this file with
    /// one constant path under the system temporary directory produces three failures that have
    /// nothing to do with the composition root. Residue from an earlier run makes a host observe a
    /// database it did not create - and because readiness VERIFIES THE SCHEMA, a run can pass on a
    /// schema a previous run had left behind rather than on one it provisioned itself. Two concurrent
    /// runs of this suite on one agent, which is routine under a parallel batch, shared a directory and a
    /// SQLite file. And the readiness case's own provisioning was observable by every other case in the
    /// file. A fresh identifier per host removes all three by construction.
    /// </para>
    /// <para>
    /// NOTHING NEEDS TO PRE-CREATE IT. The service creates its own data directory during startup
    /// [<c>Program.cs:L1750</c>, <c>Data/SqliteConnectionFactory.cs:L2638</c>], which is the one
    /// filesystem mutation it permits itself, so a host pointed at a path that does not yet exist is the
    /// ORDINARY case rather than a fault - and it is the case a deployment on fresh storage presents.
    /// The rows that assert a data-directory FAULT compose their own paths and are unaffected.
    /// </para>
    /// <para>
    /// It is read by <see cref="ConfigureWebHost(IWebHostBuilder)"/> and by
    /// <see cref="ProvisionSchemaAsync(CancellationToken)"/>, and by nothing else, so the setting the
    /// host receives and the directory the schema is provisioned into cannot drift. An earlier form of
    /// this file spelled the path a SECOND time inside <c>ConfigureWebHost</c>, which is exactly the
    /// drift its own comment claimed to prevent.
    /// </para>
    /// </remarks>
    internal string DataDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        string.Create(CultureInfo.InvariantCulture, $"pfw-composition-root-{Guid.NewGuid():n}"));

    /// <summary>
    /// Provisions the real schema in <see cref="DataDirectory"/> by applying the real migrations.
    /// </summary>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the schema is present.</returns>
    /// <remarks>
    /// Needed because the readiness probe verifies the schema and not merely that the engine answers,
    /// so a service with no database is correctly NOT ready. Applying the real migrations - rather
    /// than hand-written DDL or <c>EnsureCreated</c> - is what makes this case assert that the
    /// deployed provisioning path satisfies the deployed readiness check. An INSTANCE method, because
    /// the directory it provisions is this host's own: a case now provisions the schema the host it is
    /// about to start will read, rather than a shared one every other case could also observe.
    /// </remarks>
    internal async Task ProvisionSchemaAsync(CancellationToken cancellationToken)
    {
        _ = Directory.CreateDirectory(DataDirectory);

        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(DataDirectory, "test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,

            // POOLING OFF, a TEST-HARNESS necessity and not a product concern.
            // Microsoft.Data.Sqlite pools by connection string, so disposing the context returns its
            // connection to the pool WITHOUT closing the underlying handle - and a later RUNTIME open
            // would then block for its whole busy timeout trying to set the journal mode, which needs an
            // exclusive lock. In production the migration tool is a separate PROCESS that exits, so no
            // handle survives it and nothing needs disabling; here the two share one process.
            Pooling = false,
        };

        DbContextOptions<PowerFrameworkDbContext> options =
            new DbContextOptionsBuilder<PowerFrameworkDbContext>()
                .UseSqlite(builder.ConnectionString)
                .Options;

        await using PowerFrameworkDbContext context = new(options);

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// Disposes the host and then removes this instance's data directory.
    /// </summary>
    /// <param name="disposing">Whether managed state is being released.</param>
    /// <remarks>
    /// <para>
    /// UNCONDITIONAL, WHICH IS THE HALF THAT MATTERS. Every case in this file creates its host with
    /// <c>using</c>, so this runs whether the case passed or failed - and it is the failure path that used
    /// to leave a directory, and a SQLite database, behind for the next run to find.
    /// </para>
    /// <para>
    /// ORDER IS LOAD-BEARING: the base disposal is what stops the host and closes the engine's handle on
    /// <c>test.db</c>, so it has to complete before the directory can be removed. Deleting first would
    /// race the shutdown.
    /// </para>
    /// <para>
    /// NOT WRAPPED IN A <c>catch</c>. The directory is one this instance composed and owns exclusively, so
    /// a failure to remove it is a real condition worth surfacing rather than a nuisance worth hiding - a
    /// suppressed teardown is indistinguishable from one that worked, which is how residue accumulates
    /// unnoticed in the first place.
    /// </para>
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        if (Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    /// <summary>
    /// The subject this host's forged credential claims, and the sole entry of the permitted-caller
    /// roster it starts with.
    /// </summary>
    /// <remarks>
    /// Declared HERE rather than on the suite, because the two have to agree: the roster below is
    /// supplied as host configuration so the service's own startup gate runs against it, and the token
    /// minted above must name a caller that gate accepts. Splitting the two across types is how they
    /// come to disagree.
    /// </remarks>
    internal const string TokenSubject = "composition-root-test";

    /// <summary>Mints a credential this host accepts.</summary>
    /// <param name="scopes">
    /// The scopes the credential carries as ONE space-delimited <c>scope</c> claim. Space-delimited and
    /// not one claim per scope, because that is the RFC 6749 form the shipped requirement handler
    /// splits [<c>Authorization/ScopeAuthorization.cs</c>] - minting one claim per scope would pass a
    /// handler that compared whole values and would not exercise the split at all.
    /// </param>
    /// <returns>A compact-serialized token.</returns>
    /// <remarks>
    /// <b>THREE DISTINCT CREDENTIALS ARE REACHABLE, AND THEY ARE NOT INTERCHANGEABLE.</b> Two suites boot
    /// this host and each needs a different one, so the mapping is written out rather than left to be
    /// inferred from a default:
    /// <list type="bullet">
    /// <item>
    /// <see langword="null"/> - the default - stamps BOTH of this service's scopes, which is the
    /// credential its only real caller sends when it holds both. This is what the REST rows want (they
    /// need only an authenticated principal) and what the "accepted everywhere" row means by "the
    /// credential the real caller sends". It must NOT mean "no scope", because that row would then assert
    /// the opposite of its own name and would be refused at every contract.
    /// </item>
    /// <item>
    /// An EMPTY collection omits the claim ENTIRELY - a token that never carried a scope at all.
    /// </item>
    /// <item>
    /// A collection holding one empty string stamps the claim PRESENT AND EMPTY. That is a different
    /// credential from the one above and reaches a different branch of the handler, and a policy that
    /// refused only the absent one would let the empty one through.
    /// </item>
    /// </list>
    /// </remarks>
    internal string MintToken(IReadOnlyList<string>? scopes = null)
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        DateTimeOffset issued = now - TimeSpan.FromMinutes(1);
        DateTimeOffset expires = now + TimeSpan.FromMinutes(10);

        string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";

        // The three-way mapping documented above. An ABSENT claim and a PRESENT EMPTY one are different
        // credentials and reach different branches of the handler, so neither is folded into the other,
        // and the unasked-for case stamps both scopes rather than none.
        string scopeClaim = scopes is null
            ? string.Create(
                CultureInfo.InvariantCulture,
                $",\"scope\":\"{PersistenceScopes.Read} {PersistenceScopes.Write}\"")
            : scopes.Count == 0
                ? string.Empty
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $",\"scope\":\"{string.Join(' ', scopes)}\"");

        string payload = string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"iss\":\"{TrustedIssuer}\",\"aud\":\"{TrustedAudience}\",\"sub\":\"{TokenSubject}\",\"iat\":{issued.ToUnixTimeSeconds()},\"nbf\":{issued.ToUnixTimeSeconds()},\"exp\":{expires.ToUnixTimeSeconds()}{scopeClaim}}}");

        string signingInput = string.Concat(
            Encode(Encoding.UTF8.GetBytes(header)),
            ".",
            Encode(Encoding.UTF8.GetBytes(payload)));

        return string.Concat(
            signingInput,
            ".",
            Encode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signingInput))));
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Production);

        // Supplied as host configuration so the service's OWN startup gate runs against them.
        builder.UseSetting("Jwt:Authority", TrustedIssuer);
        builder.UseSetting("Jwt:Audience", TrustedAudience);
        builder.UseSetting("Jwt:RequireHttpsMetadata", "true");

        // THE PERMITTED-CALLER ROSTER, SUPPLIED BECAUSE THE HOST REFUSES WITHOUT ONE. Authentication
        // is not authorization: the four gRPC contracts require the operation's scope AND a caller
        // identity this service serves, so an empty roster refuses every caller and the options contract
        // requires at least one entry. It carries the subject THIS host's own forged token claims, so
        // the positive rows still exercise a caller the service accepts rather than one it rejects for
        // a reason they were not written to assert.
        builder.UseSetting("Jwt:PermittedCallers:0", TokenSubject);

        // THIS HOST'S OWN DIRECTORY, read from the one property that names it. See DataDirectory for why
        // it is per instance and why this line must not spell the path a second time.
        builder.UseSetting("Sqlite:DataDirectory", DataDirectory);

        // LAST, so a case can override any setting above it. Empty for every host created through the
        // parameterless factory method, which is all but a handful of them.
        foreach (KeyValuePair<string, string?> setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }

        builder.ConfigureServices(services => services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                OpenIdConnectConfiguration verification = new() { Issuer = TrustedIssuer };
                verification.SigningKeys.Add(new SymmetricSecurityKey(_key));

                options.Configuration = verification;
                options.TokenValidationParameters.ValidIssuer = TrustedIssuer;
                options.TokenValidationParameters.ValidAudience = TrustedAudience;
            }));
    }

    /// <summary>Base64url-encodes a segment, unpadded, as the compact serialization requires.</summary>
    /// <param name="value">The bytes to encode.</param>
    /// <returns>The encoded segment.</returns>
    private static string Encode(byte[] value) => System.Buffers.Text.Base64Url.EncodeToString(value);
}
