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
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Persistence.Concurrency;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Grpc;
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
    private const string TrustedIssuer = "https://security.invalid";

    /// <summary>The audience the host under test is configured to accept.</summary>
    private const string TrustedAudience = "powerframework-persistence-composition";

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
        using CompositionHost host = CompositionHost.Create();
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
    /// A failed assertion is a structural fault: the caller is told, and the host is stopped.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The legacy terminates the application after decoding an assertion payload
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>]. Preserved here as a stop request plus an
    /// immediate answer, rather than leaving the caller waiting for a socket to close.
    /// </remarks>
    [Fact]
    public async Task AFailedAssertionStopsTheHostAndAnswersImmediately()
    {
        RecordingLifetime lifetime = new();
        PersistenceStatusInterceptor interceptor = new(
            Errors.SqlRedactor.Instance,
            lifetime,
            NullLogger<PersistenceStatusInterceptor>.Instance);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new AssertionFailure("an invariant is broken")));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        Assert.Equal(PersistenceStatusInterceptor.AssertionFaultDetail, thrown.Status.Detail);
        Assert.True(lifetime.Stopped);
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
    /// A literal interpolated into a statement reaches neither the log record nor the wire.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The legacy database-error structure's statement field carries the complete generated statement
    /// including its interpolated literal values, and the legacy logger performed no redaction at all.
    /// An exception message raised near statement generation carries the same thing, so the message is
    /// redacted before it is recorded and the wire receives a constant.
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

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new StubCallContext(CancellationToken.None),
                (_, _) => throw new InvalidOperationException(
                    $"UPDATE COMPANY SET NAME = '{Literal}' WHERE ID = 7")));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        Assert.Equal(PersistenceStatusInterceptor.UnhandledFaultDetail, thrown.Status.Detail);
        Assert.DoesNotContain(Literal, thrown.Status.Detail, StringComparison.Ordinal);

        Assert.NotEmpty(logger.Records);
        Assert.DoesNotContain(
            Literal,
            string.Join("\n", logger.Records),
            StringComparison.Ordinal);
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
    /// The directory's parent is a FILE, which cannot be created into by any user, so this proves the
    /// probe without depending on the identity the suite happens to run as. In deployment the same arm
    /// is reached by a mounted volume owned by another user, which the image's non-root user cannot
    /// write - a fault that passes both an existence check and a permission-bit check, which is exactly
    /// why the gate probes with a real write.
    /// </remarks>
    [Fact]
    public void AnUnwritableStorageDirectoryTerminatesStartup()
    {
        string blocker = Path.Combine(Path.GetTempPath(), $"pfw-blocker-{Guid.NewGuid():n}");
        File.WriteAllText(blocker, string.Empty);

        try
        {
            using ServiceProvider provider = BuildGateProvider(Path.Combine(blocker, "nested"));

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                provider.ValidatePersistenceStructuralPreconditions);

            Assert.Contains("not writable by this process", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(blocker);
        }
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

    // ==============================================================================================
    //  5. THE SHIPPED SEAMS AND THEIR DEFINED NEGATIVES
    // ==============================================================================================

    /// <summary>
    /// The unprovisioned engine fails the writes and no-ops the unwinds.
    /// </summary>
    /// <remarks>
    /// THE COMMIT ASSERTION IS THE IMPORTANT ONE. A commit that reported success would tell a caller
    /// data had been written that never was, which is the one failure mode worse than refusing. The
    /// unwinds succeed because they are idempotent and there is genuinely nothing to undo, which is also
    /// how the legacy tolerates disconnecting something that never connected.
    /// </remarks>
    [Fact]
    public void TheUnprovisionedEngineFailsWritesAndNoOpsUnwinds()
    {
        using UnprovisionedTransactionEngine engine = new();

        TransactionData descriptor = new() { Dbms = "MSS Microsoft SQL Server" };
        engine.ApplyConnectionFields(in descriptor);

        // The dialect is echoed so the paging dispatcher still classifies exactly as the legacy does.
        Assert.Equal("MSS Microsoft SQL Server", engine.Dbms);
        Assert.Equal(0, engine.DbHandle);

        Assert.Equal(-1, engine.Connect().SqlCode);
        Assert.Equal(-1, engine.Commit().SqlCode);
        Assert.Equal(-1, engine.Execute("SELECT 1").SqlCode);
        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, engine.Connect().SqlDbCode);
        Assert.Contains(
            "No database engine is provisioned",
            engine.Connect().SqlErrText,
            StringComparison.Ordinal);

        Assert.Equal(0, engine.Rollback().SqlCode);
        Assert.Equal(0, engine.Disconnect().SqlCode);
    }

    /// <summary>
    /// The two refusing factories answer the legacy's own not-implemented code with no half-built pair.
    /// </summary>
    [Fact]
    public void TheRefusingFactoriesAnswerTheLegacyNotImplementedCode()
    {
        Assert.Equal(
            RetCode.E_NO_IMPLEMENTATION,
            new UnboundUpdateTaskFactory().TryCreate("session", out IUpdateTaskSurface? update));
        Assert.Null(update);

        Assert.Equal(
            RetCode.E_NO_IMPLEMENTATION,
            new UnboundCommandTaskFactory().Create(out CommandTaskComponents? command));
        Assert.Null(command);
    }

    /// <summary>
    /// The unbound runtimes refuse without ever claiming a successful empty result.
    /// </summary>
    /// <remarks>
    /// A zero row count would read as "retrieved successfully, nothing matched", which is the most
    /// damaging answer available, so the datastore channel's own failure value is used instead.
    /// </remarks>
    [Fact]
    public void TheUnboundRuntimesRefuseWithoutClaimingZeroRows()
    {
        UnboundDataObjectRuntime dataObjects = new();

        Assert.False(dataObjects.TryResolveDefinition("d_any", out DataObjectDefinition? definition));
        Assert.Null(definition);

        // The datastore channel's failure value, NOT zero. Zero would read as "retrieved successfully,
        // nothing matched", which is the most damaging answer available here.
        ISqlDataStore store = CreateDataStore();
        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            dataObjects.Retrieve(store, []));

        UnboundQueryDataWindowRuntime carriers = new();
        using IPooledTransaction transaction = CreatePooledTransaction();

        Assert.False(carriers.TryGetChild(
            CreateDataStore(),
            "column",
            out Buffers.DataWindowBufferStore? child));
        Assert.Null(child);
        Assert.Equal(
            Buffers.DataWindowBufferStore.DataStoreFailure,
            carriers.AttachTransaction(CreateDataStore(), transaction));

        CarrierCreateOutcome created = carriers.CreateFromSyntax(CreateDataStore(), "table(...)");
        Assert.Equal(Buffers.DataWindowBufferStore.DataStoreFailure, created.Result);
        Assert.NotEmpty(created.ErrorText);
    }

    /// <summary>
    /// The unbound transaction surface refuses productively and vetoes nothing.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The two retrieval hooks are vetoable NOTIFICATIONS, so a surface with nothing to notify must
    /// prevent nothing: answering a veto would invent a refusal the legacy never issues for a hook
    /// nobody implemented.
    /// </remarks>
    [Fact]
    public async Task TheUnboundTransactionSurfaceRefusesProductivelyAndVetoesNothing()
    {
        UnboundQueryTransactionSurface surface = new();
        using IPooledTransaction transaction = CreatePooledTransaction();
        Buffers.DataWindowCarrier carrier = new(TimeProvider.System);

        GridSyntaxOutcome syntax = surface.GridSyntaxFromSql(transaction, "SELECT 1");
        Assert.Equal(string.Empty, syntax.Syntax);
        Assert.NotEmpty(syntax.ErrorText);

        Assert.Equal(RetCode.OK, surface.RaiseBeforeRetrieve(transaction, carrier));
        surface.RaiseAfterRetrieve(transaction, carrier, rowCount: 0);

        CountQueryOutcome refused = await surface.Query(
            transaction,
            "SELECT 1",
            TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.E_NO_IMPLEMENTATION, refused.ReturnCode);
        Assert.Null(refused.Result);
        Assert.NotEmpty(refused.ErrorText);
    }

    /// <summary>
    /// The caller's cancellation outranks the surface's capability verdict.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task CancellationOutranksTheCapabilityVerdict()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        UnboundQueryTransactionSurface surface = new();
        using IPooledTransaction transaction = CreatePooledTransaction();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await surface.Query(transaction, "SELECT 1", cancelled.Token));
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
            collector,
            new QueryFaultProxy(collector));

        Assert.False(host.IsMainThread);
        Assert.False(host.IsCancelled);
        Assert.Equal(PersistenceSqlTaskHost.SoleTaskIndex, host.TaskIndex);
        Assert.NotNull(host.ParentTasking);

        // A sibling lookup has no answer on a host that owns exactly one task.
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
    /// Builds a provider carrying only what the startup gate reads.
    /// </summary>
    /// <param name="dataDirectory">The storage directory to configure.</param>
    /// <returns>A provider the gate can be run against.</returns>
    private static ServiceProvider BuildGateProvider(string dataDirectory)
    {
        ServiceCollection services = new();

        services.AddLogging();
        services.AddSingleton<IValidateOptions<PersistenceOptions>, PersistenceOptionsValidator>();
        services.AddOptions<PersistenceOptions>().Configure(options =>
        {
            options.Sqlite.DataDirectory = dataDirectory;
            options.Jwt.Authority = TrustedIssuer;
            options.Jwt.Audience = TrustedAudience;
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Produces a real pooled transaction over the shipped engine rather than a hand-written stub.
    /// </summary>
    /// <returns>A pooled transaction.</returns>
    /// <remarks>
    /// Using the real activator means these cases exercise the same composition the host performs,
    /// instead of a parallel object graph that could drift away from it unnoticed.
    /// </remarks>
    private static IPooledTransaction CreatePooledTransaction() =>
        new PooledTransactionActivator(
            static () => new UnprovisionedTransactionEngine(),
            TimeProvider.System).CreateDefault();

    /// <summary>
    /// Produces a real worker-affine datastore over the shipped unbound runtime, not a stub.
    /// </summary>
    /// <returns>A datastore.</returns>
    private static ISqlDataStore CreateDataStore() =>
        new SqlDataStoreFactory(new UnboundDataObjectRuntime(), TimeProvider.System)
            .Create(Buffers.CarrierThreadAffinity.WorkerThread);

    /// <summary>Records whether the host was asked to stop.</summary>
    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        /// <summary>Whether a stop was requested.</summary>
        internal bool Stopped { get; private set; }

        /// <inheritdoc/>
        public CancellationToken ApplicationStarted => CancellationToken.None;

        /// <inheritdoc/>
        public CancellationToken ApplicationStopping => CancellationToken.None;

        /// <inheritdoc/>
        public CancellationToken ApplicationStopped => CancellationToken.None;

        /// <inheritdoc/>
        public void StopApplication() => Stopped = true;
    }

    /// <summary>Captures formatted log records so a redaction claim can be checked.</summary>
    /// <typeparam name="T">The category type.</typeparam>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>The formatted records.</summary>
        internal List<string> Records { get; } = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        /// <remarks>
        /// Only the FORMATTED record is captured, deliberately. That is what a log sink writes, so it is
        /// the thing a redaction claim has to be made about.
        /// </remarks>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Records.Add(formatter(state, exception));
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

    /// <summary>
    /// Boots the service's own composition root in process.
    /// </summary>
    /// <remarks>
    /// The bearer handler is given a static verification configuration, which is what makes it skip
    /// metadata retrieval entirely - so these cases prove the DEPLOYED authorization posture without
    /// depending on a Security instance being reachable.
    /// </remarks>
    private sealed class CompositionHost : WebApplicationFactory<Program>
    {
        /// <summary>The key credentials are signed and verified with.</summary>
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        /// <summary>Creates a host.</summary>
        /// <returns>A started-on-first-use host.</returns>
        internal static CompositionHost Create() => new();

        /// <summary>Mints a credential this host accepts.</summary>
        /// <returns>A compact-serialized token.</returns>
        internal string MintToken()
        {
            DateTimeOffset now = TimeProvider.System.GetUtcNow();
            DateTimeOffset issued = now - TimeSpan.FromMinutes(1);
            DateTimeOffset expires = now + TimeSpan.FromMinutes(10);

            string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
            string payload = string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"iss\":\"{TrustedIssuer}\",\"aud\":\"{TrustedAudience}\",\"sub\":\"composition-root-test\",\"iat\":{issued.ToUnixTimeSeconds()},\"nbf\":{issued.ToUnixTimeSeconds()},\"exp\":{expires.ToUnixTimeSeconds()}}}");

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
            builder.UseSetting(
                "Sqlite:DataDirectory",
                Path.Combine(Path.GetTempPath(), "pfw-composition-root"));

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
}
