// ==================================================================================================
//  DataServicesStreamLifetimeTests - the LIFETIME properties of the Gateway DataServices client
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   Four properties of PowerFramework.Gateway.Clients.DataServicesClient and the
//                         two lifetime types it hands out. All four are about WHEN AN OPERATION ENDS
//                         and WHO IS TOLD, which is why none of them is observable from a return value:
//
//                           1. THE OUTBOUND ORDERING GUARD. A message whose token declares no ordering
//                              discipline is refused BEFORE it is written. The far end fails the whole
//                              session on that value, so writing it would answer a caller's omission by
//                              destroying a conversation that cannot be resumed.
//
//                           2. THE HALF-CLOSE HAS A CANCELLATION PATH. gRPC's stream writer offers no
//                              token overload on its completion method, so the token is registered
//                              against the CALL. Without it this was the one member in the client where
//                              a request could hang with no way out.
//
//                           3. SESSION CLEANUP IS BOUNDED AND ITS FAILURE IS OBSERVABLE. An explicit
//                              close propagates; disposal does not rethrow but records. Neither is
//                              unbounded, because an unbounded close inside `await using` stalls the
//                              request that was trying to unwind.
//
//                           4. THE TRACE SUBSCRIPTION IS ONE-SHOT AND SAYS SO ON THE WIRE. The
//                              signature hands back no writer, so a half-close is the truth; leaving
//                              the outbound half open leaves the far end waiting for a message that
//                              cannot arrive.
//
//  HOW IT TESTS WITHOUT A SERVER
//  ------------------------------------------------------------------------------------------------
//  Through the seam the client documents in its own banner: both generated stubs declare every method
//  `virtual` and expose a protected parameterless constructor expressly so a double can be derived. So
//  the doubles below derive from the generated clients and return hand-built gRPC call objects over
//  hand-written stream halves. Nothing here opens a socket, starts a host, or needs DataServices to
//  exist - which is the point, because two of these tests are about what happens when the far end never
//  answers, a state a real upstream cannot be asked to reproduce on demand.
//
//  ONE TEST DELIBERATELY WAITS
//  ------------------------------------------------------------------------------------------------
//  `DisposalTerminatesEvenWhenTheCloseNeverAnswers` waits out ValidationSessionScope.CloseTimeout,
//  because the property under test IS that the bound fires. Asserting the shape of the token instead
//  would prove the bound was configured, not that it works, and "disposal can hang" is precisely the
//  defect. Its cost is one wait of that duration, once.
// ==================================================================================================

using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Contracts.Common.V1;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// A token provider that answers immediately with a non-secret placeholder credential.
/// </summary>
/// <remarks>
/// The value is deliberately not token-shaped. Nothing in these tests inspects the credential, and a
/// realistic-looking one in a test file invites being mistaken for real material.
/// </remarks>
internal sealed class StubServiceTokenProvider : IServiceTokenProvider
{
    /// <inheritdoc/>
    public Task<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ServiceToken(
            "not-a-real-credential",
            "Bearer",
            DateTimeOffset.UtcNow.AddMinutes(5),
            request.Scopes));
    }
}

/// <summary>
/// A client stream writer that records what was written and whether the stream was half-closed, and
/// which can be made to leave the half-close pending forever.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
{
    private readonly TaskCompletionSource _completionGate = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly bool _completionHangs;

    /// <summary>Creates the writer.</summary>
    /// <param name="completionHangs">
    /// When true, <see cref="CompleteAsync"/> returns a task that never completes, reproducing a far end
    /// that never reads and a connection whose flow control never opens.
    /// </param>
    internal RecordingClientStreamWriter(bool completionHangs = false)
        => _completionHangs = completionHangs;

    /// <summary>Every message written, in order.</summary>
    internal List<T> Written { get; } = [];

    /// <summary>Whether the half-close was ATTEMPTED.</summary>
    internal bool CompletionAttempted { get; private set; }

    /// <summary>
    /// Faults a pending half-close, reproducing what a real transport does when the call underneath it
    /// is disposed.
    /// </summary>
    /// <remarks>
    /// This is not test convenience, it is fidelity. Disposing a Grpc.Net call cancels it, and a write
    /// or completion that was pending on that call then FAULTS - it does not stay pending forever. A
    /// double whose pending completion survived disposal would model a transport that does not exist,
    /// and a test written against it would hang rather than observe the cancellation path.
    /// </remarks>
    internal void FaultPendingCompletion()
        => _completionGate.TrySetException(
            new RpcException(new Status(StatusCode.Cancelled, "The call was disposed.")));

    /// <inheritdoc/>
    public WriteOptions? WriteOptions { get; set; }

    /// <inheritdoc/>
    public Task CompleteAsync()
    {
        CompletionAttempted = true;
        return _completionHangs ? _completionGate.Task : Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteAsync(T message)
    {
        Written.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The cancellable write, implemented rather than inherited.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the message has been recorded.</returns>
    /// <remarks>
    /// REQUIRED, NOT OPTIONAL. gRPC declares this overload as a DEFAULT interface method that throws
    /// "cancellation of stream writes is not supported by this gRPC implementation" - Grpc.Net's own
    /// writer overrides it, and any double that does not will fail every test the moment production code
    /// uses the cancellable form. Which it does, deliberately: passing the token into the write is the
    /// end-to-end cancellation the client's banner commits to, and the contrast with the COMPLETION
    /// method - which has no such overload at all - is exactly why the half-close needs its own
    /// registration-based cancellation path.
    /// </remarks>
    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteAsync(message);
    }
}

/// <summary>
/// An async stream reader over a fixed list of messages.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class ListAsyncStreamReader<T> : IAsyncStreamReader<T>
    where T : class
{
    private readonly IReadOnlyList<T> _messages;
    private int _index = -1;

    /// <summary>Creates the reader.</summary>
    /// <param name="messages">The messages to yield, in order.</param>
    internal ListAsyncStreamReader(IReadOnlyList<T> messages) => _messages = messages;

    /// <inheritdoc/>
    public T Current => _messages[_index];

    /// <inheritdoc/>
    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _index++;
        return Task.FromResult(_index < _messages.Count);
    }
}

/// <summary>
/// A double for C-03's generated client, overriding only the three operations these tests reach.
/// </summary>
internal sealed class FakeDataWindowServiceClient : DataWindowService.DataWindowServiceClient
{
    private readonly Func<CloseValidationSessionRequest, CallOptions, Task<CloseValidationSessionResponse>>?
        _close;

    /// <summary>Creates the double.</summary>
    /// <param name="close">
    /// How a close should behave, or <see langword="null"/> for a plain success. Receives the call
    /// options so a test can assert on the token the client supplied.
    /// </param>
    internal FakeDataWindowServiceClient(
        Func<CloseValidationSessionRequest, CallOptions, Task<CloseValidationSessionResponse>>? close = null)
        => _close = close;

    /// <summary>The writer of the last event chain opened, for assertions about what was written.</summary>
    internal RecordingClientStreamWriter<EventChainRequest>? EventChainWriter { get; private set; }

    /// <summary>Whether the last event chain's call object has been disposed.</summary>
    internal bool EventChainCallDisposed { get; private set; }

    /// <summary>Makes the next event chain's half-close hang.</summary>
    internal bool EventChainCompletionHangs { get; set; }

    /// <summary>The call options the last close was invoked with.</summary>
    internal CallOptions? LastCloseOptions { get; private set; }

    /// <summary>How many closes were invoked.</summary>
    internal int CloseCallCount { get; private set; }

    /// <inheritdoc/>
    public override AsyncUnaryCall<OpenValidationSessionResponse> OpenValidationSessionAsync(
        OpenValidationSessionRequest request,
        CallOptions options)
    {
        OpenValidationSessionResponse response = new()
        {
            SessionId = "session-under-test",
            RetCode = RetCode.Types.Value.Ok,
        };

        return UnaryCall(Task.FromResult(response));
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<CloseValidationSessionResponse> CloseValidationSessionAsync(
        CloseValidationSessionRequest request,
        CallOptions options)
    {
        CloseCallCount++;
        LastCloseOptions = options;

        Task<CloseValidationSessionResponse> answer = _close is null
            ? Task.FromResult(new CloseValidationSessionResponse
            {
                RetCode = RetCode.Types.Value.Ok,
                WasOpen = true,
            })
            : _close(request, options);

        return UnaryCall(answer);
    }

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse> EventChain(
        CallOptions options)
    {
        RecordingClientStreamWriter<EventChainRequest> writer = new(EventChainCompletionHangs);
        EventChainWriter = writer;

        return new AsyncDuplexStreamingCall<EventChainRequest, EventChainResponse>(
            writer,
            new ListAsyncStreamReader<EventChainResponse>([]),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            () =>
            {
                EventChainCallDisposed = true;

                // A real transport faults whatever was pending on a disposed call; the double must too,
                // or the cancellation path under test would have nothing to observe.
                writer.FaultPendingCompletion();
            });
    }

    /// <summary>Wraps an answer as a unary call with the metadata a caller may read.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="answer">The answer.</param>
    /// <returns>The call.</returns>
    private static AsyncUnaryCall<TResponse> UnaryCall<TResponse>(Task<TResponse> answer)
        => new(
            answer,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
}

/// <summary>
/// A double for C-04's generated client, overriding only the trace channel.
/// </summary>
internal sealed class FakeColumnExpressionServiceClient
    : ColumnExpressionService.ColumnExpressionServiceClient
{
    private readonly IReadOnlyList<TraceRecord> _records;

    /// <summary>Creates the double.</summary>
    /// <param name="records">The records the trace channel should yield.</param>
    internal FakeColumnExpressionServiceClient(IReadOnlyList<TraceRecord> records)
        => _records = records;

    /// <summary>The writer of the last trace channel opened.</summary>
    internal RecordingClientStreamWriter<TraceChannelRequest>? TraceWriter { get; private set; }

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord> TraceChannel(
        CallOptions options)
    {
        RecordingClientStreamWriter<TraceChannelRequest> writer = new();
        TraceWriter = writer;

        return new AsyncDuplexStreamingCall<TraceChannelRequest, TraceRecord>(
            writer,
            new ListAsyncStreamReader<TraceRecord>(_records),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }
}

/// <summary>
/// The lifetime suite.
/// </summary>
public sealed class DataServicesStreamLifetimeTests
{
    // ----------------------------------------------------------------------------------------------
    //  F18 - THE OUTBOUND ORDERING GUARD.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A token declaring no discipline is refused, AND NOTHING IS WRITTEN. The second half is the
    /// substantive assertion: the far end fails the session on that value, so a write would cost the
    /// conversation rather than merely be rejected.
    /// </summary>
    [Fact]
    public async Task AnUnspecifiedOrderingDisciplineIsRefusedBeforeAnythingIsWritten()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        EventChainRequest request = new()
        {
            SessionId = "session-under-test",
            Token = new SequencingToken
            {
                Sequence = 1,
                Discipline = OrderingDiscipline.Unspecified,
            },
        };

        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(
            () => channel.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal("request", failure.ParamName);
        Assert.Contains("unspecified", failure.Message, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(dataWindow.EventChainWriter);
        Assert.Empty(dataWindow.EventChainWriter.Written);
    }

    /// <summary>
    /// Both stated disciplines are accepted and the message travels EXACTLY as supplied, so the guard
    /// rejects only the unusable value rather than narrowing the contract.
    /// </summary>
    /// <param name="discipline">The discipline to send.</param>
    [Theory]
    [InlineData(OrderingDiscipline.Synchronous)]
    [InlineData(OrderingDiscipline.Sequenced)]
    public async Task AStatedOrderingDisciplineIsWrittenThrough(OrderingDiscipline discipline)
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        EventChainRequest request = new()
        {
            SessionId = "session-under-test",
            Token = new SequencingToken { Sequence = 7, Discipline = discipline },
        };

        await channel.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(dataWindow.EventChainWriter);
        EventChainRequest written = Assert.Single(dataWindow.EventChainWriter.Written);

        Assert.Same(request, written);
        Assert.Equal(7, written.Token.Sequence);
        Assert.Equal(discipline, written.Token.Discipline);
    }

    // ----------------------------------------------------------------------------------------------
    //  F10 - THE HALF-CLOSE CANCELLATION PATH.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A half-close of a healthy stream completes, so the cancellation machinery is not in the way of
    /// the ordinary path.
    /// </summary>
    [Fact]
    public async Task AHealthyHalfCloseCompletes()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        await channel.CompleteSendingAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(dataWindow.EventChainWriter);
        Assert.True(dataWindow.EventChainWriter.CompletionAttempted);
    }

    /// <summary>
    /// A caller that cannot cancel is served WITHOUT a registration, so opting out of cancellation is
    /// not the same as opting out of the half-close.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.None"/> reports <c>CanBeCanceled</c> as false, and registering
    /// against it would allocate a registration that could never fire. The member therefore awaits the
    /// completion directly on that path - a separate arm from the one every other row here exercises,
    /// and the arm a caller that passes no token takes.
    /// </remarks>
    [Fact]
    public async Task ANonCancellableTokenStillHalfClosesWithoutRegistering()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        // Deliberately the non-cancellable token, not the test's own. Guard the premise so the row
        // cannot silently start exercising the registered arm instead.
        Assert.False(CancellationToken.None.CanBeCanceled);

        await channel.CompleteSendingAsync(CancellationToken.None);

        Assert.NotNull(dataWindow.EventChainWriter);
        Assert.True(dataWindow.EventChainWriter.CompletionAttempted);

        // The call is NOT torn down by an ordinary half-close, whichever arm served it.
        Assert.False(dataWindow.EventChainCallDisposed);
    }

    /// <summary>
    /// A token cancelled BEFORE the attempt refuses without touching the stream, so an already-abandoned
    /// request does not half-close a conversation on its way out.
    /// </summary>
    [Fact]
    public async Task AnAlreadyCancelledTokenRefusesTheHalfCloseWithoutAttemptingIt()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => channel.CompleteSendingAsync(cancelled.Token));

        Assert.NotNull(dataWindow.EventChainWriter);
        Assert.False(dataWindow.EventChainWriter.CompletionAttempted);
    }

    /// <summary>
    /// THE CENTRAL F10 ASSERTION: a half-close that never completes is cancellable, the cancellation
    /// carries the caller's own token, and the call is torn down so the conversation cannot be left in
    /// an unknown state.
    /// </summary>
    /// <remarks>
    /// A member that accepted no token at all would leave this scenario with no exit: the returned
    /// task simply never completed and the request holding it hung indefinitely.
    /// </remarks>
    [Fact]
    public async Task APendingHalfCloseIsCancellableAndTearsTheCallDown()
    {
        FakeDataWindowServiceClient dataWindow = new() { EventChainCompletionHangs = true };
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        await using DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        using CancellationTokenSource caller = new();

        Task halfClose = channel.CompleteSendingAsync(caller.Token);

        // The half-close is genuinely pending: nothing has completed it and nothing will.
        Assert.False(halfClose.IsCompleted);

        await caller.CancelAsync();

        OperationCanceledException cancelled =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => halfClose);

        // The caller's OWN token, so a `catch` filtered on it matches.
        Assert.Equal(caller.Token, cancelled.CancellationToken);

        // And the conversation was torn down rather than left half-closed-in-progress.
        Assert.True(dataWindow.EventChainCallDisposed);
    }

    /// <summary>
    /// A completed half-close leaves nothing attached to the caller's token, so cancelling that token
    /// afterwards - which a request scope routinely does - cannot retroactively tear the call down.
    /// </summary>
    [Fact]
    public async Task CancellingAfterASuccessfulHalfCloseDoesNotTearTheCallDown()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        DataWindowEventChannel channel = await client
            .OpenEventChainAsync(TestContext.Current.CancellationToken);

        using CancellationTokenSource caller = new();
        await channel.CompleteSendingAsync(caller.Token);

        await caller.CancelAsync();

        Assert.False(dataWindow.EventChainCallDisposed);

        // Disposal is what disposes the call, and it still does.
        await channel.DisposeAsync();
        Assert.True(dataWindow.EventChainCallDisposed);
    }

    // ----------------------------------------------------------------------------------------------
    //  F9 - BOUNDED, OBSERVABLE SESSION CLEANUP.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The close a caller asks for BOUNDS itself: the token it hands the upstream can be cancelled,
    /// which is precisely what <c>CancellationToken.None</c> could not be.
    /// </summary>
    [Fact]
    public async Task AnExplicitCloseSucceedsAndIsBounded()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            TestContext.Current.CancellationToken);

        CloseValidationSessionResponse closed =
            await scope.CloseAsync(TestContext.Current.CancellationToken);

        Assert.True(closed.WasOpen);
        Assert.Same(closed, scope.CloseResult);
        Assert.Null(scope.CloseFailure);

        Assert.NotNull(dataWindow.LastCloseOptions);
        Assert.True(
            dataWindow.LastCloseOptions.Value.CancellationToken.CanBeCanceled,
            "The close must run on a cancellable token; CancellationToken.None cannot be bounded.");

        // Disposal afterwards is a no-op: it must not close a second time.
        await scope.DisposeAsync();
        Assert.Equal(1, dataWindow.CloseCallCount);
    }

    /// <summary>
    /// An explicit close PROPAGATES its failure, because on a normal path there is no primary exception
    /// for it to mask and a caller is entitled to learn that an upstream session was not released.
    /// </summary>
    [Fact]
    public async Task AnExplicitCloseFailurePropagatesAndIsRecorded()
    {
        RpcException upstreamFailure = new(new Status(StatusCode.Unavailable, "upstream is down"));

        FakeDataWindowServiceClient dataWindow = new(
            (_, _) => Task.FromException<CloseValidationSessionResponse>(upstreamFailure));

        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            TestContext.Current.CancellationToken);

        RpcException thrown = await Assert.ThrowsAsync<RpcException>(
            () => scope.CloseAsync(TestContext.Current.CancellationToken));

        Assert.Same(upstreamFailure, thrown);
        Assert.Null(scope.CloseResult);
        Assert.Same(upstreamFailure, scope.CloseFailure);
    }

    /// <summary>
    /// A second explicit close after a failure refuses rather than silently re-attempting, so a success
    /// can never overwrite a failure the caller has already acted on. The original fault is retained.
    /// </summary>
    [Fact]
    public async Task ASecondExplicitCloseAfterAFailureRefusesAndKeepsTheOriginalFault()
    {
        RpcException upstreamFailure = new(new Status(StatusCode.Unavailable, "upstream is down"));

        FakeDataWindowServiceClient dataWindow = new(
            (_, _) => Task.FromException<CloseValidationSessionResponse>(upstreamFailure));

        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<RpcException>(
            () => scope.CloseAsync(TestContext.Current.CancellationToken));

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.CloseAsync(TestContext.Current.CancellationToken));

        Assert.Same(upstreamFailure, refusal.InnerException);
        Assert.Equal(1, dataWindow.CloseCallCount);
    }

    /// <summary>
    /// Disposal does NOT rethrow - it must not displace the fault unwinding through the enclosing block
    /// - but the failure is RECORDED, so a leaked upstream session is observable in code and not only in
    /// a log nobody reads.
    /// </summary>
    [Fact]
    public async Task DisposalSwallowsACloseFailureButRecordsIt()
    {
        RpcException upstreamFailure = new(new Status(StatusCode.Internal, "close rejected"));

        FakeDataWindowServiceClient dataWindow = new(
            (_, _) => Task.FromException<CloseValidationSessionResponse>(upstreamFailure));

        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            TestContext.Current.CancellationToken);

        // No exception escapes disposal.
        await scope.DisposeAsync();

        Assert.Null(scope.CloseResult);
        Assert.Same(upstreamFailure, scope.CloseFailure);
    }

    /// <summary>
    /// Disposal runs on a token of ITS OWN, which is both cancellable - so the attempt is bounded - and
    /// unaffected by the caller's token having been cancelled, because that is exactly the case in which
    /// the session most needs releasing.
    /// </summary>
    [Fact]
    public async Task DisposalUsesItsOwnBoundedTokenAndIgnoresACancelledCallerToken()
    {
        FakeDataWindowServiceClient dataWindow = new();
        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        using CancellationTokenSource caller = new();

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            caller.Token);

        await caller.CancelAsync();

        await scope.DisposeAsync();

        // The close still happened, despite the caller's token being cancelled.
        Assert.Equal(1, dataWindow.CloseCallCount);
        Assert.NotNull(scope.CloseResult);
        Assert.Null(scope.CloseFailure);

        Assert.NotNull(dataWindow.LastCloseOptions);
        CancellationToken supplied = dataWindow.LastCloseOptions.Value.CancellationToken;

        Assert.True(
            supplied.CanBeCanceled,
            "Disposal must supply a bounded token, not CancellationToken.None.");
        Assert.False(
            supplied.IsCancellationRequested,
            "Disposal's token must not inherit the caller's cancellation.");
    }

    /// <summary>
    /// THE CENTRAL F9 ASSERTION: disposal TERMINATES even when the upstream never answers the close, and
    /// records the timeout rather than reporting a clean release.
    /// </summary>
    /// <remarks>
    /// This test waits out <see cref="ValidationSessionScope.CloseTimeout"/> on purpose. The property is
    /// that the bound fires; asserting the token's shape instead would prove only that a bound was
    /// configured, and "disposal can hang" is the defect itself. A close issued on
    /// <c>CancellationToken.None</c> never returns in this scenario.
    /// </remarks>
    [Fact]
    public async Task DisposalTerminatesEvenWhenTheCloseNeverAnswers()
    {
        FakeDataWindowServiceClient dataWindow = new(
            static (_, options) =>
            {
                // Answers only when the client's own token is cancelled - which only the disposal bound
                // can do here, because no caller token reaches this path.
                TaskCompletionSource<CloseValidationSessionResponse> pending = new(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                options.CancellationToken.Register(
                    () => pending.TrySetCanceled(options.CancellationToken));

                return pending.Task;
            });

        DataServicesClient client = CreateClient(dataWindow, new FakeColumnExpressionServiceClient([]));

        ValidationSessionScope scope = await client.OpenValidationSessionScopeAsync(
            new OpenValidationSessionRequest(),
            TestContext.Current.CancellationToken);

        long startedAt = Stopwatch.GetTimestamp();
        await scope.DisposeAsync();
        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.Null(scope.CloseResult);
        Assert.IsAssignableFrom<OperationCanceledException>(scope.CloseFailure);

        // Bounded, and bounded by roughly the declared value rather than by chance. The upper factor is
        // generous because this asserts termination, not latency - no latency target exists to assert.
        Assert.True(
            elapsed < ValidationSessionScope.CloseTimeout * 4,
            $"Disposal took {elapsed} against a declared bound of {ValidationSessionScope.CloseTimeout}.");
    }

    // ----------------------------------------------------------------------------------------------
    //  F17 - THE ONE-SHOT TRACE SUBSCRIPTION.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The trace subscription writes its ONE control message and then half-closes, because the signature
    /// hands back no writer and so no second message can ever be sent. Leaving the outbound half open
    /// would leave the far end waiting for a message that cannot arrive.
    /// </summary>
    [Fact]
    public async Task TheTraceSubscriptionHalfClosesAfterItsSingleControlMessage()
    {
        TraceRecord record = new()
        {
            Expr = "sum(salary for page)",
            Value = "1000",
            Stack = "salary",
        };

        FakeColumnExpressionServiceClient columnExpression = new([record]);
        DataServicesClient client = CreateClient(new FakeDataWindowServiceClient(), columnExpression);

        TraceChannelRequest request = new()
        {
            SessionId = "session-under-test",
            DatawindowHandle = "dw_sqlite",
            Subscribe = true,
        };

        List<TraceRecord> received = [];

        await foreach (TraceRecord streamed in client.StreamExpressionTraceAsync(
            request,
            TestContext.Current.CancellationToken))
        {
            received.Add(streamed);
        }

        Assert.Same(record, Assert.Single(received));

        Assert.NotNull(columnExpression.TraceWriter);
        Assert.Same(request, Assert.Single(columnExpression.TraceWriter.Written));
        Assert.True(
            columnExpression.TraceWriter.CompletionAttempted,
            "The trace subscription must half-close: the API exposes no writer, so no further control "
                + "message can be sent and the far end must be told so.");
    }

    // ----------------------------------------------------------------------------------------------
    //  FIXTURE CONSTRUCTION.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the client over the two doubles, with a stub token provider and a null logger.
    /// </summary>
    /// <param name="dataWindow">The C-03 double.</param>
    /// <param name="columnExpression">The C-04 double.</param>
    /// <returns>The client.</returns>
    private static DataServicesClient CreateClient(
        FakeDataWindowServiceClient dataWindow,
        FakeColumnExpressionServiceClient columnExpression)
        => new(
            dataWindow,
            columnExpression,
            new StubServiceTokenProvider(),
            NullLogger<DataServicesClient>.Instance);
}
