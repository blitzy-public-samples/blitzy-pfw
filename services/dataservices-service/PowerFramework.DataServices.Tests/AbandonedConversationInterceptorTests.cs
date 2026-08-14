// ==================================================================================================
//  THE UNARY-BOUNDARY TRANSLATION FOR A RACED CONVERSATION TEARDOWN
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE CASES ARE FOR. The root-cause fix for the reported defect is the durable-host rebind at
//  conversation teardown - see DataWindowCompositionTests, which asserts it directly. What is LEFT after
//  that fix is a race: a unary call resolves its retained model set, and the conversation those models
//  were re-hosted onto is torn down before the call reaches them. The model then forwards a semantic ask
//  to a disposed conversation and `ObjectDisposedException` escapes the handler, which gRPC reports as
//  UNKNOWN with a generic detail - the same symptom the reported defect produced, arriving by a narrower
//  route.
//
//  SO THE ASSERTIONS HERE ARE ABOUT THE STATUS AND THE DETAIL, NOT ABOUT AN ANSWER. Nothing is
//  fabricated: no empty filter, no default menu, no success for work that did not happen. A caller is
//  told the models were between hosts and that the call can be issued again.
// ==================================================================================================

using Grpc.AspNetCore.Server;
using Grpc.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using PowerFramework.DataServices.Grpc;

using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// <see cref="AbandonedConversationInterceptor"/> - the unary-only translation of a raced teardown.
/// </summary>
public sealed class AbandonedConversationInterceptorTests
{
    /// <summary>
    /// A disposed conversation surfaces as FAILED_PRECONDITION rather than as an unhandled exception.
    /// </summary>
    /// <remarks>
    /// FAILED_PRECONDITION AND NOT ABORTED, and the distinction is load bearing rather than cosmetic:
    /// ABORTED is what this service forwards when Persistence reports an update conflict, and Gateway
    /// projects that to HTTP 409. Reusing it here would make a raced teardown indistinguishable from an
    /// optimistic-concurrency conflict, which is a caller-visible confusion of two unrelated conditions.
    /// RESOURCE_EXHAUSTED is likewise already spoken for, by the ingress bound.
    /// </remarks>
    [Fact]
    public async Task ADisposedConversationBecomesFailedPreconditionRatherThanAnUnhandledFault()
    {
        RecordingLogger log = new();
        AbandonedConversationInterceptor interceptor = new(log);

        RpcException raised = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new C03CallContext(TestContext.Current.CancellationToken),
                (_, _) => throw new ObjectDisposedException("DataWindowEventConversation")));

        Assert.Equal(StatusCode.FailedPrecondition, raised.StatusCode);
        Assert.Equal(AbandonedConversationInterceptor.Detail, raised.Status.Detail);
    }

    /// <summary>
    /// The wire detail names no internal type and interpolates nothing from the fault.
    /// </summary>
    /// <remarks>
    /// The exception's own <c>ObjectName</c> is a .NET type name: it tells a caller nothing they can act
    /// on, and this service does not put internal type names on the wire. The detail is a fixed string,
    /// which is also what makes it assertable by identity above rather than by fuzzy matching.
    /// </remarks>
    [Fact]
    public async Task TheWireDetailCarriesNoInternalTypeName()
    {
        AbandonedConversationInterceptor interceptor = new(new RecordingLogger());

        RpcException raised = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new C03CallContext(TestContext.Current.CancellationToken),
                (_, _) => throw new ObjectDisposedException("DataWindowEventConversation")));

        Assert.DoesNotContain("DataWindowEventConversation", raised.Status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerFramework.", raised.Status.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectDisposed", raised.Status.Detail, StringComparison.Ordinal);

        // It DOES tell the caller the call can be issued again, which is the actionable half.
        Assert.Contains("again", raised.Status.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The raced call is recorded, with the method named and the exception attached.
    /// </summary>
    /// <remarks>
    /// AT WARNING, NOT ERROR. The condition is recoverable by the caller and needs no operator action, but
    /// a rise in its rate would mean callers are interleaving unary work with a conversation on the same
    /// DataWindow far more than expected - so it is not routine either. The exception is ATTACHED rather
    /// than interpolated, which is what keeps the type and the site in the log record while the wire
    /// detail carries neither.
    /// </remarks>
    [Fact]
    public async Task TheRacedCallIsRecordedAtWarningWithTheMethodAndTheException()
    {
        RecordingLogger log = new();
        AbandonedConversationInterceptor interceptor = new(log);
        ObjectDisposedException thrown = new("DataWindowEventConversation");

        _ = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new C03CallContext(TestContext.Current.CancellationToken),
                (_, _) => throw thrown));

        RecordedEntry entry = Assert.Single(log.Entries);

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(thrown, entry.Exception);
        Assert.Contains("/dataservices.v1.DataWindowService/Adhoc", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Any OTHER exception is left exactly as it was - the translation is one condition wide.
    /// </summary>
    /// <remarks>
    /// A broad catch here would mask genuine faults behind a status that says "retry", which is the
    /// opposite of the fail-fast posture AAP 0.6.7 requires for a structural fault. The two rows are an
    /// ordinary fault and a status this service raises deliberately; neither may be rewritten.
    /// </remarks>
    [Fact]
    public async Task AnyOtherFaultIsNotTranslated()
    {
        AbandonedConversationInterceptor interceptor = new(new RecordingLogger());

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new C03CallContext(TestContext.Current.CancellationToken),
                (_, _) => throw new InvalidOperationException("a genuine fault")));

        // And a status the handler raised on purpose keeps its own code, so the ingress bound's
        // RESOURCE_EXHAUSTED and the update path's ABORTED both travel unchanged.
        RpcException deliberate = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                new C03CallContext(TestContext.Current.CancellationToken),
                (_, _) => throw new RpcException(new Status(StatusCode.Aborted, "conflict"))));

        Assert.Equal(StatusCode.Aborted, deliberate.StatusCode);
        Assert.Equal("conflict", deliberate.Status.Detail);
    }

    /// <summary>
    /// A handler that completes is passed through untouched, and nothing is logged.
    /// </summary>
    [Fact]
    public async Task ASuccessfulCallIsPassedThroughAndNothingIsRecorded()
    {
        RecordingLogger log = new();
        AbandonedConversationInterceptor interceptor = new(log);

        string answered = await interceptor.UnaryServerHandler<string, string>(
            "request",
            new C03CallContext(TestContext.Current.CancellationToken),
            (payload, _) => Task.FromResult(payload + "-answered"));

        Assert.Equal("request-answered", answered);
        Assert.Empty(log.Entries);
    }

    /// <summary>
    /// The server registers it, INSIDE the ingress bound.
    /// </summary>
    /// <remarks>
    /// COMPOSED THROUGH THE SERVICE'S OWN REGISTRATION, so this cannot describe a wiring the host does not
    /// actually perform - an interceptor that exists and is never registered translates nothing. The ORDER
    /// is asserted as well as the presence: the bound is outermost so work is shed before it is done, and
    /// the translation sits inside it so a translated fault has still been accounted against ingress.
    /// </remarks>
    [Fact]
    public void TheServerRegistersTheInterceptorInsideTheIngressBound()
    {
        ServiceCollection services = new();

        services.AddLogging();
        _ = services.AddDataServicesPublishedSurface();

        GrpcServiceOptions options = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<GrpcServiceOptions>>()
            .Value;

        Assert.Equal(2, options.Interceptors.Count);
        Assert.Equal(typeof(GrpcIngressLimitInterceptor), options.Interceptors[0].Type);
        Assert.Equal(typeof(AbandonedConversationInterceptor), options.Interceptors[1].Type);
    }

    /// <summary>One recorded log entry.</summary>
    /// <param name="Level">The level it was recorded at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The attached exception, if any.</param>
    private sealed record RecordedEntry(LogLevel Level, string Message, Exception? Exception);

    /// <summary>
    /// The smallest logger that can prove what reached the sink. Deliberately not a mocking framework: the
    /// assertion is about the record, and a hand-written sink states that directly.
    /// </summary>
    private sealed class RecordingLogger : ILogger<AbandonedConversationInterceptor>
    {
        private readonly List<RecordedEntry> _entries = [];

        /// <summary>Every entry, in order.</summary>
        internal IReadOnlyList<RecordedEntry> Entries => _entries;

        /// <inheritdoc/>
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

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

            _entries.Add(new RecordedEntry(logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            internal static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
