// ==================================================================================================
//  SystemErrorObservabilityTests - the two OPERATOR-CHANNEL properties of the system-error protocol
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS   Two properties of PowerFramework.Gateway.Diagnostics.SystemErrorHandler
//                         that no other suite can observe, because both are properties of what the
//                         handler WRITES rather than of what it computes:
//
//                           1. THE ALLOWLIST (DECISION 2). An ordinary request fault produces a
//                              record built only from constants, integers, this codebase's own
//                              identifiers, and values the caller itself sent. The exception object
//                              is not handed to the logger, and no exception message - inner or
//                              outer - no `Exception.Source` and no formatted block appears anywhere
//                              in it. The record for a STRUCTURAL fault is unchanged and still
//                              carries every one of the seven decoded fields plus the block, because
//                              that record IS the legacy dialog and C-B requires it be preserved.
//
//                           2. THE CORRELATION BRIDGE (DECISION 7). One identifier is resolved
//                              before anything is written, and the value in the operator record is
//                              provably the same value the caller receives in the response body. A
//                              caller quoting the identifier from its body can therefore find the
//                              record; that is the entire justification for the response carrying so
//                              little else.
//
//  WHY THESE NEED A SUITE OF THEIR OWN
//  ------------------------------------------------------------------------------------------------
//  Both are NEGATIVE properties, and a negative property is invisible to any test written against a
//  return value: the handler returns a boolean, and it returns the identical boolean whether or not
//  it just wrote a caller's bearer token into a retained log record. Only an observer placed on the
//  logging abstraction itself can tell the difference, which is what RecordedLogEntry exists for.
//
//  The hostile fixtures below are deliberately specific rather than generic. A fault message reading
//  "something went wrong" would pass an allowlist test that a real one fails, so the messages here
//  carry the exact shapes F5 names - a connection string with a password, a bearer token, a URL with
//  embedded credentials, and an absolute internal path - at three different depths of the exception
//  chain and in `Exception.Source` as well. Every assertion is that NONE of them reaches the record.
//
//  WHAT THIS SUITE DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  It does not re-test the decoder, the formatter or the return-code narrowing. Those are pure
//  functions over a payload string and are covered as such; this suite constructs a real assert
//  payload only where it needs a structural fault to exist at all.
//
//  It starts no host and opens no socket. A DefaultHttpContext with a memory-backed response body is
//  sufficient and is deterministic, which matters because two of these tests assert on an ambient
//  Activity and a test that shares a host with another test cannot control that.
// ==================================================================================================

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// One entry captured from the logging abstraction, holding everything an assertion needs to
/// distinguish a redacted record from a leaking one.
/// </summary>
/// <param name="Level">The level the handler chose.</param>
/// <param name="Message">
/// The rendered message - the string a plain text sink would write, which is where a leak would
/// surface in the most consequential way because such a sink stores nothing else.
/// </param>
/// <param name="Exception">
/// The exception the handler passed to the logger, or <see langword="null"/>. This member is the
/// crux of the allowlist: a passed exception is rendered by every sink through its own string
/// conversion, which includes every message in the chain, so passing one defeats any redaction
/// performed on the template.
/// </param>
/// <param name="Properties">
/// The structured properties, which is what a structured sink stores and indexes. A leak can hide
/// here while the rendered message looks clean, so both are asserted on separately.
/// </param>
internal sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> Properties)
{
    /// <summary>
    /// Reads one structured property by name, or <see langword="null"/> when the record does not
    /// carry it.
    /// </summary>
    /// <param name="name">The property name, as it appears in the message template.</param>
    /// <returns>The property value, or <see langword="null"/>.</returns>
    internal object? Property(string name)
    {
        foreach (KeyValuePair<string, object?> property in Properties)
        {
            if (string.Equals(property.Key, name, StringComparison.Ordinal))
            {
                return property.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Every property name the record carries, excluding the synthetic original-format entry the
    /// logging abstraction appends.
    /// </summary>
    /// <returns>The names, in template order.</returns>
    internal IReadOnlyList<string> PropertyNames()
    {
        List<string> names = [];

        foreach (KeyValuePair<string, object?> property in Properties)
        {
            if (!string.Equals(property.Key, OriginalFormatPropertyName, StringComparison.Ordinal))
            {
                names.Add(property.Key);
            }
        }

        return names;
    }

    /// <summary>
    /// Everything a sink could persist from this entry, concatenated: the rendered message, every
    /// structured value, and the string conversion of a passed exception. An allowlist assertion runs
    /// against this rather than against the message alone, because a leak in any one of the three is
    /// equally a leak.
    /// </summary>
    /// <returns>The concatenation.</returns>
    internal string EverythingASinkCouldPersist()
    {
        System.Text.StringBuilder everything = new();
        everything.Append(Message);

        foreach (KeyValuePair<string, object?> property in Properties)
        {
            everything.Append('\u0000').Append(property.Key).Append('\u0000');

            if (property.Value is not null)
            {
                everything.Append(Convert.ToString(property.Value, CultureInfo.InvariantCulture));
            }
        }

        if (Exception is not null)
        {
            everything.Append('\u0000').Append(Exception.ToString());
        }

        return everything.ToString();
    }

    /// <summary>
    /// The name the logging abstraction gives the entry it appends carrying the raw template.
    /// </summary>
    internal const string OriginalFormatPropertyName = "{OriginalFormat}";
}

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that records rather than writes, so a test can assert on
/// what the handler put on the operator channel.
/// </summary>
/// <remarks>
/// Enabled at every level deliberately. A recorder that respected a minimum level could silently
/// drop the very entry under test and the test would then pass by observing nothing.
/// </remarks>
internal sealed class RecordingLogger : ILogger<SystemErrorHandler>
{
    private readonly List<RecordedLogEntry> _entries = [];

    /// <summary>The entries recorded so far, in the order the handler wrote them.</summary>
    internal IReadOnlyList<RecordedLogEntry> Entries => _entries;

    /// <summary>The single entry recorded, failing the test when there is not exactly one.</summary>
    /// <returns>The entry.</returns>
    internal RecordedLogEntry Single()
    {
        Assert.Single(_entries);
        return _entries[0];
    }

    /// <inheritdoc/>
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

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

        IReadOnlyList<KeyValuePair<string, object?>> properties =
            state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];

        _entries.Add(new RecordedLogEntry(
            logLevel,
            formatter(state, exception),
            exception,
            properties));
    }

    /// <summary>The scope a recorder needs but never uses.</summary>
    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// An <see cref="IHostApplicationLifetime"/> that records a stop request instead of performing one.
/// </summary>
/// <remarks>
/// The handler's default termination effect calls <see cref="StopApplication"/>, but every test here
/// supplies its own termination callback instead, so this type exists to satisfy the constructor's
/// required dependency without any test being able to stop the test host by accident.
/// </remarks>
internal sealed class StubHostApplicationLifetime : IHostApplicationLifetime
{
    /// <inheritdoc/>
    public CancellationToken ApplicationStarted => CancellationToken.None;

    /// <inheritdoc/>
    public CancellationToken ApplicationStopping => CancellationToken.None;

    /// <inheritdoc/>
    public CancellationToken ApplicationStopped => CancellationToken.None;

    /// <summary>Whether a stop was requested.</summary>
    internal bool StopRequested { get; private set; }

    /// <inheritdoc/>
    public void StopApplication() => StopRequested = true;
}

/// <summary>
/// The operator-channel suite: the allowlist and the correlation bridge.
/// </summary>
public sealed class SystemErrorObservabilityTests
{
    // ----------------------------------------------------------------------------------------------
    //  THE HOSTILE FIXTURE. Each constant is one of the shapes an ordinary fault's message really
    //  does carry in a decomposed system, placed at a different depth of the chain so that a fix
    //  which only handles the outermost exception is caught.
    // ----------------------------------------------------------------------------------------------

    /// <summary>A credential-bearing connection string, on the OUTERMOST exception's message.</summary>
    private const string OuterSecret =
        "Host=primary.db.internal;Port=5432;Username=svc_gateway;Password=hunter2";

    /// <summary>A bearer token, on the FIRST INNER exception's message.</summary>
    private const string InnerSecret =
        "Authorization: Bearer eyJhbGciOiJSUzI1NiJ9.payload.signature";

    /// <summary>A URL with embedded credentials, on the SECOND INNER exception's message.</summary>
    private const string DeepestSecret = "https://operator:letmein@security-service:5104/v1/tokens";

    /// <summary>An absolute internal path, placed on <see cref="Exception.Source"/>.</summary>
    private const string SourceSecret = "/var/lib/powerframework/secrets/security.key";

    /// <summary>The route pattern a test registers, whose placeholder stands where a value would.</summary>
    private const string RoutePatternText = "/v1/datawindow/{sessionId}/events";

    /// <summary>The concrete path bound to that pattern, carrying a value in the placeholder.</summary>
    private const string ConcretePathWithValue = "/v1/datawindow/tok-9f3c-secret-session/events";

    /// <summary>The allowlisted property names the ordinary-fault record must carry, exactly.</summary>
    private static readonly string[] ExpectedOrdinaryFaultProperties =
    [
        "ReportTitle",
        "Severity",
        "TraceId",
        "RequestMethod",
        "RoutePattern",
        "ResponseStatus",
        "ErrorNumber",
        "WireRetCode",
        "FaultTypes",
        "ErrorObject",
        "ObjectEvent",
        "ErrorLine",
    ];

    // ----------------------------------------------------------------------------------------------
    //  F5 - THE ALLOWLIST.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The core F5 assertion: not one of the four hostile values reaches anything a sink could
    /// persist - not the rendered message, not a structured value, and not a passed exception.
    /// </summary>
    [Fact]
    public async Task NoExceptionMessageAtAnyChainDepthReachesTheOrdinaryFaultRecord()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext();

        bool handled = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        Assert.True(handled);

        RecordedLogEntry entry = logger.Single();
        string persisted = entry.EverythingASinkCouldPersist();

        // The exception object itself is the single most consequential leak, because every sink
        // renders it through its own string conversion regardless of what the template says.
        Assert.Null(entry.Exception);

        Assert.DoesNotContain(OuterSecret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(InnerSecret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(DeepestSecret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(SourceSecret, persisted, StringComparison.Ordinal);

        // The distinctive fragments too, so a fix that merely truncates a message cannot pass.
        Assert.DoesNotContain("hunter2", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJSUzI1NiJ9", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("letmein", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets/security.key", persisted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The allowlist stated positively: exactly these property names, in this order, and no others.
    /// </summary>
    /// <remarks>
    /// An exact-set assertion is the point. A test that only checked for the ABSENCE of known-bad
    /// names would pass the day someone adds a placeholder for a value nobody thought to forbid,
    /// which is precisely how an allowlist decays into a denylist.
    /// </remarks>
    [Fact]
    public async Task TheOrdinaryFaultRecordCarriesExactlyTheAllowlistedProperties()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext();

        _ = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        Assert.Equal(ExpectedOrdinaryFaultProperties, logger.Single().PropertyNames());
    }

    /// <summary>
    /// The allowlisted record still identifies the fault: the type chain names every link from the
    /// outermost type to the innermost cause, so an operator learns WHAT failed without learning what
    /// it said.
    /// </summary>
    [Fact]
    public async Task TheOrdinaryFaultRecordNamesTheWholeExceptionTypeChain()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);

        _ = await handler
            .TryHandleAsync(
                CreateHttpContext(),
                CreateHostileException(),
                TestContext.Current.CancellationToken);

        string chain = Assert.IsType<string>(logger.Single().Property("FaultTypes"));

        Assert.Contains("System.InvalidOperationException", chain, StringComparison.Ordinal);
        Assert.Contains("System.Net.Http.HttpRequestException", chain, StringComparison.Ordinal);
        Assert.Contains("System.TimeoutException", chain, StringComparison.Ordinal);

        // Ordered outermost first, so the reading direction is unambiguous.
        Assert.True(
            chain.IndexOf("System.InvalidOperationException", StringComparison.Ordinal)
                < chain.IndexOf("System.TimeoutException", StringComparison.Ordinal),
            "The chain must read from the outermost type towards the innermost cause.");
    }

    /// <summary>
    /// An ordinary fault is reported at error level and does not terminate the host: the
    /// structural-versus-request line of DECISION 5 is unaffected by the redaction.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryFaultIsReportedAtErrorLevelAndDoesNotTerminate()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out List<int> terminations);

        _ = await handler
            .TryHandleAsync(
                CreateHttpContext(),
                CreateHostileException(),
                TestContext.Current.CancellationToken);

        Assert.Equal(LogLevel.Error, logger.Single().Level);
        Assert.Empty(terminations);
    }

    /// <summary>
    /// The route is identified by its PATTERN and the concrete path - which carries a caller value in
    /// the placeholder - never appears in the record.
    /// </summary>
    [Fact]
    public async Task TheOrdinaryFaultRecordCarriesTheRoutePatternAndNotTheBoundPath()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext(ConcretePathWithValue, RoutePatternText);

        _ = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();

        Assert.Equal(RoutePatternText, entry.Property("RoutePattern"));
        Assert.DoesNotContain(
            "tok-9f3c-secret-session",
            entry.EverythingASinkCouldPersist(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// With no endpoint matched there is no pattern, and the fallback is a fixed literal rather than
    /// the request path - so an unrouted fault cannot smuggle a path value into the record either.
    /// </summary>
    [Fact]
    public async Task AnUnroutedFaultReportsAFixedLiteralRatherThanTheRequestPath()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext(ConcretePathWithValue, routePattern: null);

        _ = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();

        Assert.Equal("(unrouted)", entry.Property("RoutePattern"));
        Assert.DoesNotContain(
            "tok-9f3c-secret-session",
            entry.EverythingASinkCouldPersist(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The counterpart assertion, and the one that keeps the fix honest against C-B: a STRUCTURAL
    /// fault's record is unredacted and still carries all seven decoded fields, the formatted block
    /// and the exception itself.
    /// </summary>
    /// <remarks>
    /// If this test ever fails because the structural record was trimmed too, the redaction has been
    /// applied to the one path the oracle actually specifies, which would be a behavioural
    /// regression rather than a security improvement.
    /// </remarks>
    [Fact]
    public async Task TheStructuralFaultRecordIsUnredactedAndCarriesEverySevenFieldValue()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out List<int> terminations);

        _ = await handler
            .TryHandleAsync(
                CreateHttpContext(),
                CreateSevenFieldAssertion(),
                TestContext.Current.CancellationToken);

        RecordedLogEntry entry = logger.Single();
        string persisted = entry.EverythingASinkCouldPersist();

        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.NotNull(entry.Exception);

        // Fields 2 through 5 and 7 - every decoded string field - reach the operator verbatim.
        Assert.Contains("the assertion text", persisted, StringComparison.Ordinal);
        Assert.Contains("w_demo_selector", persisted, StringComparison.Ordinal);
        Assert.Contains("n_cst_example", persisted, StringComparison.Ordinal);
        Assert.Contains("of_dowork", persisted, StringComparison.Ordinal);
        Assert.Contains("frame one", persisted, StringComparison.Ordinal);

        // Fields 1 and 6 - the two numeric ones.
        Assert.Equal(-10000L, entry.Property("ErrorNumber"));
        Assert.Equal(42L, entry.Property("ErrorLine"));

        // And the halt path still runs, unchanged.
        Assert.Equal([70], terminations);
    }

    // ----------------------------------------------------------------------------------------------
    //  F6 - THE CORRELATION BRIDGE.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The bridge, asserted end to end on the ordinary path: the identifier in the operator record is
    /// the identifier in the response body, not merely a value of the same shape.
    /// </summary>
    [Fact]
    public async Task TheIdentifierInTheOrdinaryFaultRecordIsTheIdentifierInTheResponseBody()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext();

        _ = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        string recorded = Assert.IsType<string>(logger.Single().Property("TraceId"));
        Assert.False(string.IsNullOrEmpty(recorded));
        Assert.Equal(recorded, await ReadTraceIdFromBodyAsync(httpContext));
    }

    /// <summary>
    /// The same bridge on the structural path, because a terminal fault is exactly the occasion on
    /// which a caller most needs an identifier that leads somewhere.
    /// </summary>
    [Fact]
    public async Task TheIdentifierInTheStructuralFaultRecordIsTheIdentifierInTheResponseBody()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext();

        _ = await handler
            .TryHandleAsync(
                httpContext,
                CreateSevenFieldAssertion(),
                TestContext.Current.CancellationToken);

        string recorded = Assert.IsType<string>(logger.Single().Property("TraceId"));
        Assert.False(string.IsNullOrEmpty(recorded));
        Assert.Equal(recorded, await ReadTraceIdFromBodyAsync(httpContext));
    }

    /// <summary>
    /// When a distributed trace is in flight its identifier is the one used, because that is what
    /// correlates this record with every other service's record for the same operation.
    /// </summary>
    [Fact]
    public async Task TheAmbientTraceIdentifierIsPreferredOverTheHostsOwn()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = CreateHttpContext();

        using ActivitySource source = new("PowerFramework.Gateway.Tests.SystemError");
        using ActivityListener listener = new()
        {
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        using Activity? activity = source.StartActivity("fault");
        Assert.NotNull(activity);

        _ = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        Assert.Equal(activity.Id, logger.Single().Property("TraceId"));
        Assert.Equal(
            activity.Id,
            await ReadTraceIdFromBodyAsync(httpContext));
    }

    /// <summary>
    /// The response-already-started record carries the identifier too, and for the reason specific to
    /// it: this is the one path on which the caller receives no body at all, so without the
    /// identifier here the two records for one request could not be tied together.
    /// </summary>
    [Fact]
    public async Task TheResponseAlreadyStartedRecordCarriesTheSameIdentifierAsTheFaultRecord()
    {
        RecordingLogger logger = new();
        SystemErrorHandler handler = CreateHandler(logger, out _);
        HttpContext httpContext = new DefaultHttpContext
        {
            Features = { [typeof(IHttpResponseFeature)] = new StartedResponseFeature() },
        };

        bool handled = await handler
            .TryHandleAsync(httpContext, CreateHostileException(), TestContext.Current.CancellationToken);

        // The body could not be written, which is the one case that reports not-handled.
        Assert.False(handled);
        Assert.Equal(2, logger.Entries.Count);

        object? faultIdentifier = logger.Entries[0].Property("TraceId");
        object? warningIdentifier = logger.Entries[1].Property("TraceId");

        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
        Assert.NotNull(faultIdentifier);
        Assert.Equal(faultIdentifier, warningIdentifier);
    }

    // ----------------------------------------------------------------------------------------------
    //  THE TYPE-CHAIN DESCRIBER, AS A PURE FUNCTION (C-H).
    // ----------------------------------------------------------------------------------------------

    /// <summary>A null exception describes as the empty string rather than as an error.</summary>
    [Fact]
    public void ANullExceptionDescribesAsTheEmptyString()
        => Assert.Equal(string.Empty, SystemErrorHandler.DescribeExceptionTypes(null));

    /// <summary>A single exception describes as its own namespace-qualified name and nothing else.</summary>
    [Fact]
    public void ASingleExceptionDescribesAsItsNamespaceQualifiedName()
    {
        Assert.Equal(
            "System.TimeoutException",
            SystemErrorHandler.DescribeExceptionTypes(new TimeoutException("ignored")));
    }

    /// <summary>A chain is joined outermost first, and the separator points towards the cause.</summary>
    [Fact]
    public void ANestedChainIsJoinedFromTheOutermostTypeTowardsTheCause()
    {
        Exception nested = new InvalidOperationException(
            "ignored",
            new TimeoutException("ignored"));

        Assert.Equal(
            "System.InvalidOperationException <- System.TimeoutException",
            SystemErrorHandler.DescribeExceptionTypes(nested));
    }

    /// <summary>
    /// A chain deeper than the bound is truncated and MARKED, so a truncated chain is never mistaken
    /// for a complete one.
    /// </summary>
    [Fact]
    public void AChainDeeperThanTheBoundIsTruncatedAndMarked()
    {
        Exception chain = new TimeoutException("ignored");

        // Eleven links in total: three past the eight-link bound.
        for (int depth = 0; depth < 10; depth++)
        {
            chain = new InvalidOperationException("ignored", chain);
        }

        string described = SystemErrorHandler.DescribeExceptionTypes(chain);

        Assert.EndsWith(" <- ...", described, StringComparison.Ordinal);

        // Eight named links, so seven separators between them plus the one before the marker.
        Assert.Equal(8, described.Split(" <- ", StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// A message is never part of a description, at any depth. This is the unit-level restatement of
    /// the suite's central property.
    /// </summary>
    [Fact]
    public void NoMessageAppearsInADescriptionAtAnyDepth()
    {
        string described = SystemErrorHandler.DescribeExceptionTypes(CreateHostileException());

        Assert.DoesNotContain("hunter2", described, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", described, StringComparison.Ordinal);
        Assert.DoesNotContain("letmein", described, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------------------------------------
    //  FIXTURE CONSTRUCTION.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the handler with a recording logger and a termination callback that records instead of
    /// stopping the test host (DECISION 6).
    /// </summary>
    /// <param name="logger">The recorder to attach.</param>
    /// <param name="terminations">Receives every exit code termination was requested with.</param>
    /// <returns>The handler.</returns>
    private static SystemErrorHandler CreateHandler(RecordingLogger logger, out List<int> terminations)
    {
        List<int> recorded = [];
        terminations = recorded;

        return new SystemErrorHandler(
            logger,
            new StubHostApplicationLifetime(),
            problemDetailsService: null,
            requestProcessTermination: recorded.Add);
    }

    /// <summary>
    /// A request context with a memory-backed response body, optionally bound to a route pattern.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="routePattern">
    /// The pattern of the endpoint to attach, or <see langword="null"/> for an unrouted request.
    /// </param>
    /// <returns>The context.</returns>
    private static HttpContext CreateHttpContext(
        string path = "/v1/ping",
        string? routePattern = null)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = path;
        httpContext.Response.Body = new MemoryStream();

        if (routePattern is not null)
        {
            httpContext.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(routePattern),
                order: 0,
                new EndpointMetadataCollection(),
                displayName: null));
        }

        return httpContext;
    }

    /// <summary>
    /// The hostile ordinary fault: three chained exceptions, each carrying a different shape of
    /// secret, plus a fourth secret on <see cref="Exception.Source"/>.
    /// </summary>
    /// <returns>The exception.</returns>
    private static Exception CreateHostileException()
    {
        Exception deepest = new TimeoutException(DeepestSecret);
        Exception inner = new HttpRequestException(InnerSecret, deepest);
        Exception outer = new InvalidOperationException(OuterSecret, inner)
        {
            Source = SourceSecret,
        };

        return outer;
    }

    /// <summary>
    /// A complete seven-field assert payload, as the producer emits it: fields joined by CRLF, field
    /// one the fixed sentinel number [<c>ws_objects/pfw.common.pbl.src/assert.srf:L19,L22</c>].
    /// </summary>
    /// <returns>The assertion carrying that payload as its message.</returns>
    private static AssertionFailure CreateSevenFieldAssertion()
    {
        string payload = string.Join(
            "\r\n",
            "-10000",
            "the assertion text",
            "w_demo_selector",
            "n_cst_example",
            "of_dowork",
            "42",
            "frame one");

        return new AssertionFailure(payload);
    }

    /// <summary>
    /// Reads the correlation identifier back out of the problem-details body the handler wrote.
    /// </summary>
    /// <param name="httpContext">The context whose response body to read.</param>
    /// <returns>The identifier the caller receives.</returns>
    private static async Task<string?> ReadTraceIdFromBodyAsync(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;

        using JsonDocument document = await JsonDocument
            .ParseAsync(httpContext.Response.Body, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            RetCode.UNKNOWN,
            document.RootElement.GetProperty("retCode").GetInt64());

        return document.RootElement.TryGetProperty("traceId", out JsonElement traceId)
            ? traceId.GetString()
            : null;
    }

    /// <summary>
    /// A response feature that reports the response as already on the wire, so the one path that
    /// cannot write a body is reachable without a real server.
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        /// <inheritdoc/>
        public Stream Body { get; set; } = Stream.Null;

        /// <inheritdoc/>
        public bool HasStarted => true;

        /// <inheritdoc/>
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        /// <inheritdoc/>
        public string? ReasonPhrase { get; set; }

        /// <inheritdoc/>
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        /// <inheritdoc/>
        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        /// <inheritdoc/>
        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }
}
