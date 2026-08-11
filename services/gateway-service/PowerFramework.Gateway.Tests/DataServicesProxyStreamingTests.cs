// =====================================================================================================
//  F-15 - INCREMENTAL STREAMING AND THE ELEMENT BOUND AT THE INGRESS
// =====================================================================================================
//
//  WHY THE GATEWAY STREAMS WHERE THE DATASERVICES PROJECTION COLLECTS. Both answer a server-streaming gRPC
//  method as one JSON document, but they trade differently and the asymmetry is deliberate. The
//  DataServices projection collects first so that a mid-stream upstream failure can still produce a clean
//  problem body. Here the buffering would cost far more, because the gateway is the process EVERY request
//  in the system passes through - so each element is formatted and written as it arrives and one element is
//  live at a time.
//
//  THE PRICE, RECORDED HONESTLY RATHER THAN GLOSSED. Once the status line and headers are sent they cannot
//  be changed, so a fault after the first element ends the body WITHOUT its closing bracket. A caller
//  detects that as malformed JSON, which is a DETECTABLE failure. What this deliberately never produces is
//  a short array that closes cleanly, because that is indistinguishable from a complete one.
//
//  THE BOUND IS A RESOURCE BOUND AND NOT A PERFORMANCE CLAIM (AAP 0.8.5).
// =====================================================================================================

using System.Net;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Endpoints;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// A <see cref="CallInvoker"/> whose server-streaming calls fault instead of producing elements, so that a
/// pre-first-item upstream failure can be exercised through the REAL generated stubs.
/// </summary>
/// <param name="fault">The failure every opened stream raises.</param>
/// <param name="afterElements">How many elements are produced before the failure.</param>
/// <remarks>
/// <para>
/// Only the server-streaming member is implemented, because it is the only shape this file's subject uses.
/// The other three refuse loudly rather than answering: reaching one of them would mean the projection
/// under test had stopped being a server stream, which is a wiring change worth failing over.
/// </para>
/// <para>
/// THE STUBS ABOVE IT ARE THE REAL ONES. Substituting the transport rather than the client means the
/// failure travels the same path a genuine gRPC fault does - through the generated stub, through the typed
/// client's <c>await foreach</c>, and out of the projection - which is the only way to prove where it is
/// caught.
/// </para>
/// </remarks>
internal sealed class FaultingServerStreamCallInvoker(RpcException fault, int afterElements = 0) : CallInvoker
{
    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        new(
            new FaultingStreamReader<TResponse>(fault, afterElements),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) =>
        throw new InvalidOperationException(
            $"This double answers server streams only. Method: {method.FullName}.");

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) =>
        throw new InvalidOperationException(
            $"This double answers server streams only. Method: {method.FullName}.");

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        throw new InvalidOperationException(
            $"This double answers server streams only. Method: {method.FullName}.");

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        throw new InvalidOperationException(
            $"This double answers server streams only. Method: {method.FullName}.");
}

/// <summary>
/// A response-stream reader that produces a fixed number of default messages and then faults.
/// </summary>
/// <typeparam name="TResponse">The streamed message type.</typeparam>
/// <param name="fault">The failure raised once the produced count is reached.</param>
/// <param name="afterElements">How many messages are produced before the failure.</param>
/// <remarks>
/// The failure is raised from the AWAITED read rather than synchronously, because that is how a real
/// transport surfaces one and because a synchronous throw would exercise a path the deployed code never
/// takes.
/// </remarks>
internal sealed class FaultingStreamReader<TResponse>(RpcException fault, int afterElements)
    : IAsyncStreamReader<TResponse>
    where TResponse : class
{
    private int _produced;

    /// <inheritdoc/>
    public TResponse Current { get; private set; } = default!;

    /// <inheritdoc/>
    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        await Task.Yield();

        cancellationToken.ThrowIfCancellationRequested();

        if (_produced >= afterElements)
        {
            throw fault;
        }

        _produced++;

        Current = Activator.CreateInstance<TResponse>();

        return true;
    }
}

/// <summary>
/// The incremental streaming and element bound of the proxied server-stream projection.
/// </summary>
public sealed class DataServicesProxyStreamingTests
{
    /// <summary>A field the JSON mapping emits exactly once per projected element.</summary>
    private const string ElementMarker = "chunkIndex";

    /// <summary>The projected retrieval, which is the only server-streaming route in the contract.</summary>
    private const string RetrieveRoute = "/v1/datawindow/retrieve";

    /// <summary>
    /// A sequence within the bound is written as a single well-formed JSON array.
    /// </summary>
    /// <remarks>
    /// The shape is asserted from the actual response bytes rather than from an intermediate collection,
    /// because what a caller receives is the bytes: a separator written in the wrong place or a missing
    /// bracket is invisible to any assertion made before serialization.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ASequenceWithinTheBoundIsWrittenAsOneWellFormedArray(int elements)
    {
        (string body, int pulled) = await StreamAsync(elements, maximumElements: 10);

        Assert.StartsWith("[", body, StringComparison.Ordinal);
        Assert.EndsWith("]", body, StringComparison.Ordinal);
        Assert.Equal(elements, CountElements(body));
        Assert.Equal(elements, pulled);
    }

    /// <summary>
    /// The sequence is never drained whole: the producer is stopped at the bound.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE INCREMENTALITY CLAIM, AND IT IS THE ONE THAT WOULD OTHERWISE BE PROSE.</b> An
    /// implementation that materialized the sequence and then trimmed it would satisfy every byte-level
    /// assertion in this file while still holding an unbounded response in the gateway's memory. Counting
    /// how many elements the producer was ASKED for is what separates the two: an upstream that keeps
    /// producing must be stopped, not merely ignored.
    /// </remarks>
    [Fact]
    public async Task TheProducerIsStoppedAtTheBoundRatherThanDrainedAndTrimmed()
    {
        (string body, int pulled) = await StreamAsync(elements: 50, maximumElements: 3);

        // Pulled at most one past the bound - the element that revealed the crossing. Never all fifty.
        Assert.InRange(pulled, 3, 4);
        Assert.Equal(3, CountElements(body));
    }

    /// <summary>
    /// Exceeding the bound abandons the document WITHOUT its closing bracket.
    /// </summary>
    /// <remarks>
    /// <b>THE ABSENT BRACKET IS THE POINT, NOT AN OVERSIGHT.</b> The status line was already sent, so the
    /// refusal cannot become a problem response; leaving the array unterminated is what makes the caller
    /// detect an incomplete answer. Closing it would hand back a short array that reads as complete, which
    /// is the one outcome worse than an error.
    /// </remarks>
    [Fact]
    public async Task ExceedingTheBoundAbandonsTheDocumentWithoutItsClosingBracket()
    {
        (string body, _) = await StreamAsync(elements: 10, maximumElements: 2);

        Assert.StartsWith("[", body, StringComparison.Ordinal);

        // Tested as "does not END with a bracket" rather than "contains none": an element's own payload
        // carries brackets of its own - a chunk's row list is an array - so a containment test would pass
        // for the wrong reason and fail once an element's shape changed.
        Assert.False(
            body.TrimEnd().EndsWith(']'),
            "The abandoned document must not be terminated, or a short array reads as a complete one.");

        Assert.Equal(2, CountElements(body));
    }

    /// <summary>
    /// A caller that has gone away stops the forwarding rather than draining the upstream for nobody.
    /// </summary>
    /// <remarks>
    /// An abandoned response is the ordinary way a streamed request ends early, and continuing to pull from
    /// the upstream after it would spend the whole system's most contended process on a body nobody will
    /// read. The abort arrives on the RESPONSE side here - the stream itself opened cleanly and its first
    /// element is already in hand - which is the mid-stream case, distinct from the pre-first-item case
    /// below.
    /// </remarks>
    [Fact]
    public async Task AnAbortedRequestStopsTheForwarding()
    {
        using CancellationTokenSource aborted = new();

        // Cancelled synchronously: the async overload accepts a token the analyzer requires threading, and
        // the token this test needs cancelled is the one being cancelled - so the synchronous form is the
        // honest expression of the intent rather than a token laundered through an unrelated one.
        aborted.Cancel();

        int pulled = 0;

        DefaultHttpContext context = NewContext();
        context.RequestAborted = aborted.Token;

        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>
            .PrefetchAsync(
                Produce(50, () => pulled++, TestContext.Current.CancellationToken),
                maximumElements: 10,
                TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.ExecuteAsync(context));

        Assert.InRange(pulled, 0, 1);
    }

    // ==============================================================================================
    //  F-18 - A FAILURE BEFORE THE FIRST ELEMENT MUST STILL REACH THE SHARED FAILURE PATH
    //  --------------------------------------------------------------------------------------------
    //  The result writes 200 and `[` as its FIRST act, and the framework executes it only after the
    //  projection has returned. So while the first element was pulled from inside ExecuteAsync, a stream
    //  that faulted before producing anything faulted after the status line was already on the wire: a
    //  caller retrieving a DataWindow that does not exist received 200 and a truncated array instead of
    //  404, and the streaming type's own remarks claimed the opposite.
    //
    //  The three tests below pin the corrected placement at both levels - the factory in isolation, and
    //  the deployed route end to end.
    // ==============================================================================================

    /// <summary>
    /// A stream that faults BEFORE its first element propagates the fault out of the prefetch, where the
    /// projection's shared translation can still see it.
    /// </summary>
    /// <remarks>
    /// <b>THE FAULT ESCAPING IS THE BEHAVIOUR UNDER TEST, NOT AN INCIDENT.</b> The prefetch is awaited
    /// inside the projection's try, so a fault raised here is translated exactly as it is on a unary route.
    /// Returning a result instead - which is what the previous shape did, because the pull happened later -
    /// puts the same fault after the headers, where nothing can translate it. Asserted as "no result is
    /// produced" as well as "the fault is the upstream's", because a result would be the defect.
    /// </remarks>
    [Fact]
    public async Task APreFirstItemFaultEscapesThePrefetchRatherThanBecomingAResult()
    {
        RpcException upstream = new(new Status(StatusCode.NotFound, "no such DataWindow"));

        RpcException observed = await Assert.ThrowsAsync<RpcException>(() =>
            DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>.PrefetchAsync(
                Fault(upstream, afterElements: 0, TestContext.Current.CancellationToken),
                maximumElements: 10,
                TestContext.Current.CancellationToken));

        Assert.Same(upstream, observed);
    }

    /// <summary>
    /// The enumerator is disposed when the prefetch faults, so a failed retrieval leaves no upstream call
    /// open.
    /// </summary>
    /// <remarks>
    /// A gRPC call enumerator HOLDS THE CALL. Leaving it undisposed after answering the request with a
    /// problem document would hold an upstream call open for a request that has already ended, and the
    /// gateway is the process every request passes through - so the leak would accumulate there first.
    /// </remarks>
    [Fact]
    public async Task TheEnumeratorIsDisposedWhenThePrefetchFaults()
    {
        bool disposed = false;

        _ = await Assert.ThrowsAsync<RpcException>(() =>
            DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>.PrefetchAsync(
                Fault(
                    new RpcException(new Status(StatusCode.Internal, "upstream fault")),
                    afterElements: 0,
                    TestContext.Current.CancellationToken,
                    () => disposed = true),
                maximumElements: 10,
                TestContext.Current.CancellationToken));

        Assert.True(disposed, "A faulted prefetch must not leave the upstream enumerator undisposed.");
    }

    /// <summary>
    /// A fault AFTER the first element still ends the body unterminated, because by then the headers have
    /// gone - the documented trade, asserted so the fix above did not quietly change it.
    /// </summary>
    /// <remarks>
    /// This is the boundary the prefetch draws. Before the first element, a fault becomes a problem
    /// response; from the first element onward it cannot, and the response ends WITHOUT its closing bracket
    /// so the caller detects an incomplete answer. Both halves are asserted, because a change that made
    /// either behave like the other would be a silent regression in opposite directions.
    /// </remarks>
    [Fact]
    public async Task AFaultAfterTheFirstElementEndsTheBodyUnterminated()
    {
        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>
            .PrefetchAsync(
                Fault(
                    new RpcException(new Status(StatusCode.Internal, "upstream fault")),
                    afterElements: 2,
                    TestContext.Current.CancellationToken),
                maximumElements: 10,
                TestContext.Current.CancellationToken);

        _ = await Assert.ThrowsAsync<RpcException>(() => result.ExecuteAsync(context));

        await context.Response.BodyWriter.FlushAsync(TestContext.Current.CancellationToken);

        string written = Encoding.UTF8.GetString(body.ToArray());

        Assert.StartsWith("[", written, StringComparison.Ordinal);
        Assert.Equal(2, CountElements(written));
        Assert.False(
            written.TrimEnd().EndsWith(']'),
            "A mid-stream fault must leave the array unterminated so the caller detects it.");
    }

    /// <summary>
    /// On the DEPLOYED route, an upstream that faults before its first chunk answers the mapped problem
    /// status rather than a truncated success.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE ROW THAT WOULD HAVE FAILED BEFORE THE FIX.</b> The upstream refuses with
    /// <see cref="StatusCode.NotFound"/> - the retrieval of a DataWindow that does not exist - and the
    /// published map assigns that 404. Before the first element was pulled inside the projection, this same
    /// request answered <c>200</c> with the two bytes <c>[</c> and nothing else, because the status line was
    /// written before the fault was raised.
    /// </para>
    /// <para>
    /// Driven through the deployed host and the real generated stubs, with only the TRANSPORT substituted,
    /// so what is asserted is the route's behaviour rather than a helper's. The problem document's own
    /// status member is checked as well as the response status: a translation that set one and not the other
    /// would leave a consumer reading two different answers to the same question.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APreFirstItemUpstreamFaultAnswersTheMappedProblemStatusOnTheDeployedRoute()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        DataServicesClient faulting = BuildClient(
            new FaultingServerStreamCallInvoker(
                new RpcException(new Status(StatusCode.NotFound, "no such DataWindow"))));

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();
            services.AddSingleton(faulting);
        });

        using HttpClient client = host.CreateAuthenticatedClient();

        using StringContent body = new("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(JsonValueKind.Object, problem.RootElement.ValueKind);
        Assert.Equal(
            (int)HttpStatusCode.NotFound,
            problem.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>
    /// On the DEPLOYED route, an upstream that produces a chunk and THEN faults has already committed
    /// <c>200</c> - the documented trade, asserted so the fix above did not turn streaming back into
    /// buffering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE OTHER HALF OF THE BOUNDARY, AND IT IS WHAT SEPARATES A PREFETCH FROM A COLLECTION.</b>
    /// A projection that drained the stream before answering would see this same fault BEFORE any header and
    /// would answer a problem document - so a committed <c>200</c> is positive evidence that exactly one
    /// element was pulled and the rest were forwarded as they arrived.
    /// </para>
    /// <para>
    /// The headers are read WITHOUT buffering the body, deliberately. In process the abandoned response
    /// surfaces to the client as a faulted read rather than as a short byte array - the test host propagates
    /// the exception through its response pipe instead of truncating it - so the status line is the part of
    /// this that is observable here, and the unterminated document itself is asserted directly against the
    /// written bytes by <see cref="AFaultAfterTheFirstElementEndsTheBodyUnterminated"/>. Both are asserted;
    /// neither is assumed from the other.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFaultAfterTheFirstChunkHasAlreadyCommittedTheStatusOnTheDeployedRoute()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        DataServicesClient faulting = BuildClient(
            new FaultingServerStreamCallInvoker(
                new RpcException(new Status(StatusCode.NotFound, "no such DataWindow")),
                afterElements: 1));

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();
            services.AddSingleton(faulting);
        });

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri(RetrieveRoute, UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, MediaTypeNames.Application.Json),
        };

        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MediaTypeNames.Application.Json, response.Content.Headers.ContentType?.MediaType);

        // The body cannot complete, because the upstream failed after the status line had gone. The
        // ANSWERED-THEN-FAILED shape is the point: a caller sees an answer it cannot finish reading rather
        // than a short array that reads as complete.
        Exception? interrupted = await Record.ExceptionAsync(() =>
            response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(interrupted);
    }

    /// <summary>Builds the typed client over a substituted transport and the REAL generated stubs.</summary>
    /// <param name="upstream">The transport double.</param>
    /// <returns>The client.</returns>
    /// <remarks>
    /// No socket is opened and no credential is real - the token provider answers a placeholder.
    /// </remarks>
    private static DataServicesClient BuildClient(CallInvoker upstream) =>
        new(
            new DataWindowService.DataWindowServiceClient(upstream),
            new ColumnExpressionService.ColumnExpressionServiceClient(upstream),
            new StubServiceTokenProvider(),
            NullLogger<DataServicesClient>.Instance);

    /// <summary>Runs the projection and returns the response body with the producer's pull count.</summary>
    /// <param name="elements">How many elements the upstream offers.</param>
    /// <param name="maximumElements">The configured bound.</param>
    /// <returns>The body text and the number of elements the producer was asked for.</returns>
    private static async Task<(string Body, int Pulled)> StreamAsync(int elements, int maximumElements)
    {
        int pulled = 0;

        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        // CONSTRUCTED THROUGH THE PREFETCH, WHICH IS THE ONLY WAY IN. The result cannot be built without
        // its first element in hand, so every test in this file exercises the same two-step the projection
        // performs: pull one element inside the shared failure path, then forward from there.
        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>
            .PrefetchAsync(
                Produce(elements, () => pulled++, TestContext.Current.CancellationToken),
                maximumElements,
                TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);

        await context.Response.BodyWriter.FlushAsync(TestContext.Current.CancellationToken);

        return (Encoding.UTF8.GetString(body.ToArray()), pulled);
    }

    /// <summary>Builds a context with the services the projection resolves for its diagnostics.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewContext()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/datawindow/retrieve";

        return context;
    }

    /// <summary>A lazy producer that records how many elements it was asked for.</summary>
    /// <param name="count">How many it will offer.</param>
    /// <param name="onPull">Called once per element produced.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// LAZY BY CONSTRUCTION, WHICH IS WHAT MAKES THE PULL COUNT MEANINGFUL. An eager collection would report
    /// every element as pulled the moment it was built and could prove nothing about incrementality.
    /// </remarks>
    private static async IAsyncEnumerable<RetrieveChunk> Produce(
        int count,
        Action onPull,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            onPull();

            await Task.Yield();

            yield return new RetrieveChunk { ChunkIndex = index + 1 };
        }
    }

    /// <summary>A sequence that produces a fixed number of chunks and then faults.</summary>
    /// <param name="fault">The failure raised once the produced count is reached.</param>
    /// <param name="afterElements">How many chunks are produced first; zero faults before any.</param>
    /// <param name="cancellationToken">Observed on every read, as a real stream observes it.</param>
    /// <param name="onDispose">Called when the enumerator is disposed, so disposal is observable.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// HAND-WRITTEN RATHER THAN AN ITERATOR, and the reason is the disposal assertion. A C# iterator runs its
    /// <c>finally</c> blocks while the exception unwinds THROUGH the iterator body, so a disposal flag set in
    /// one would already be true whether or not the enumerator was ever disposed - making the assertion pass
    /// for the wrong reason. An explicit enumerator sets the flag only from <c>DisposeAsync</c>.
    /// </remarks>
    private static IAsyncEnumerable<RetrieveChunk> Fault(
        RpcException fault,
        int afterElements,
        CancellationToken cancellationToken,
        Action? onDispose = null)
        => new FaultingChunkSequence(fault, afterElements, cancellationToken, onDispose);

    /// <summary>Counts the encoded elements in a written body.</summary>
    /// <param name="body">The body text.</param>
    /// <returns>The element count.</returns>
    /// <remarks>
    /// Counted by a field the protobuf JSON mapping emits EXACTLY ONCE PER ELEMENT rather than by splitting
    /// on the separator or counting braces: an element's own payload contains both commas and braces, so
    /// either of those would count the wrong thing and would drift the moment an element's shape changed.
    /// The chunk ordinal is always present because the projection sets it on every chunk from one.
    /// </remarks>
    private static int CountElements(string body)
    {
        int elements = 0;

        for (int index = body.IndexOf(ElementMarker, StringComparison.Ordinal);
            index >= 0;
            index = body.IndexOf(ElementMarker, index + ElementMarker.Length, StringComparison.Ordinal))
        {
            elements++;
        }

        return elements;
    }
}

/// <summary>
/// A chunk sequence that produces a fixed number of elements and then raises a gRPC failure, reporting its
/// own disposal.
/// </summary>
/// <param name="fault">The failure raised once the produced count is reached.</param>
/// <param name="afterElements">How many chunks are produced before the failure.</param>
/// <param name="cancellationToken">Observed on every read.</param>
/// <param name="onDispose">Invoked from <see cref="DisposeAsync"/> and from nowhere else.</param>
/// <remarks>
/// <para>
/// ONE ENUMERATION ONLY, which is all the projection performs and all the tests need. Handing back
/// <see langword="this"/> keeps the produced count and the disposal flag on one object, so an assertion
/// reads the same instance the projection consumed.
/// </para>
/// <para>
/// The failure is raised from the AWAITED read rather than synchronously, because that is how a real gRPC
/// stream surfaces one - a synchronous throw would travel a path the deployed code never takes.
/// </para>
/// </remarks>
internal sealed class FaultingChunkSequence(
    RpcException fault,
    int afterElements,
    CancellationToken cancellationToken,
    Action? onDispose) : IAsyncEnumerable<RetrieveChunk>, IAsyncEnumerator<RetrieveChunk>
{
    private int _produced;

    /// <inheritdoc/>
    public RetrieveChunk Current { get; private set; } = new();

    /// <inheritdoc/>
    public IAsyncEnumerator<RetrieveChunk> GetAsyncEnumerator(CancellationToken enumerationToken = default)
        => this;

    /// <inheritdoc/>
    public async ValueTask<bool> MoveNextAsync()
    {
        await Task.Yield();

        cancellationToken.ThrowIfCancellationRequested();

        if (_produced >= afterElements)
        {
            throw fault;
        }

        _produced++;

        // ONE-BASED, so the ordinal is a non-default value and the JSON mapping therefore EMITS it. A
        // zero-based ordinal would be omitted from the encoded element as a default, and the element count
        // this file asserts is taken from that field.
        Current = new RetrieveChunk { ChunkIndex = _produced };

        return true;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        onDispose?.Invoke();

        return ValueTask.CompletedTask;
    }
}
