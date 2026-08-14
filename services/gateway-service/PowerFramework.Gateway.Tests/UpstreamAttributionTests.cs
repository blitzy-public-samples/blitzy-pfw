// ==================================================================================================
//  UpstreamAttributionTests - a forwarded failure names the upstream that actually failed
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS, AND WHY IT EXISTS
//
//  `OpenApi/gateway.v1.yaml` publishes `upstream` as a closed enum - persistence, dataservices,
//  security - and its description states the member exists so an operator can attribute a failure
//  WITHOUT correlating logs. Two defects made that untrue at the same time:
//
//    1. `BuildProblem` stamped the literal `dataservices` on EVERY forwarded failure. `security` was
//       therefore an enum member no response could ever carry, and a consumer branching on it had a
//       branch that could not be reached.
//
//    2. `SecurityClient` let a transport failure escape as a bare `HttpRequestException`. Every
//       projected call obtains a service credential from Security BEFORE it calls DataServices, so the
//       projection's single transport arm caught SECURITY's outage and answered
//       `502 { upstream: "dataservices" }` with DataServices-specific prose - while DataServices was
//       perfectly healthy. The operator record said the same wrong thing, three lines below Security's
//       own `Name or service not known`.
//
//  So the two halves are one property and are asserted as one: the credential edge must be
//  distinguishable from the data edge in BOTH channels a reader has - the problem body and the
//  operator record - and every pre-existing projection must still attribute itself to DataServices
//  exactly as it did before.
//
//  WHY THE FAULTS ARE INJECTED RATHER THAN PROVOKED
//  A genuinely unreachable host makes the outcome depend on the platform resolver's timing and on
//  whichever exception class it happens to raise. The doubles below raise the exact classes the client
//  is required to convert, which is what makes each arm's coverage legible rather than incidental. The
//  end-to-end reproduction against a really-stopped security-service is the runtime re-verification
//  step, not this file's job.
//
//  NO CREDENTIAL OR KEY IN THIS FILE IS REAL, and none is copied from anywhere in the repository.
// ==================================================================================================

using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Endpoints;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// A transport that raises a supplied fault instead of answering, so the client's conversion of a
/// transport failure can be driven one exception class at a time.
/// </summary>
/// <param name="fault">The fault to raise on every send.</param>
internal sealed class FaultingSecurityTransport(Exception fault) : HttpMessageHandler
{
    /// <summary>How many sends were attempted, so "attempted exactly once" is assertable.</summary>
    internal int Attempts { get; private set; }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Attempts++;

        return Task.FromException<HttpResponseMessage>(fault);
    }
}

/// <summary>
/// A token provider that reproduces a Security outage: the credential cannot be obtained because the
/// issuer could not be reached.
/// </summary>
/// <remarks>
/// This is the double that makes the deployed-route assertion possible without stopping a real service.
/// It raises exactly what <see cref="SecurityClient"/> raises, so the route sees what it would see in
/// production rather than a stand-in.
/// </remarks>
internal sealed class UnreachableIssuerTokenProvider : IServiceTokenProvider
{
    /// <inheritdoc/>
    public Task<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        throw new ServiceTokenUnavailableException(
            ServiceTokenUnavailableException.DefaultMessage,
            new HttpRequestException("Name or service not known (security-service:8443)"));
    }
}

/// <summary>
/// A transport for the DataServices edge that records whether it was ever reached and refuses if it was.
/// </summary>
/// <remarks>
/// THE COUNTER IS THE POINT. A credential that could not be obtained must stop the request BEFORE the
/// data edge is touched, so "DataServices was never called" is the positive evidence that the outage
/// being attributed to Security really is Security's - rather than a DataServices call that happened to
/// fail for its own reasons.
/// </remarks>
internal sealed class NeverCalledCallInvoker : CallInvoker
{
    /// <summary>How many calls reached this transport. Must stay zero on the credential path.</summary>
    internal int Calls { get; private set; }

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw Reached(method);

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw Reached(method);

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw Reached(method);

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw Reached(method);

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw Reached(method);

    /// <summary>Records the breach and describes it.</summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="method">The method that was reached.</param>
    /// <returns>The exception to raise.</returns>
    private InvalidOperationException Reached<TRequest, TResponse>(Method<TRequest, TResponse> method)
        where TRequest : class
        where TResponse : class
    {
        Calls++;

        return new InvalidOperationException(
            $"The DataServices edge was called ({method.FullName}) on a request whose service credential "
                + "could not be obtained. The credential is acquired first, so reaching the data edge "
                + "means the credential failure was swallowed rather than projected.");
    }
}

/// <summary>
/// The attribution suite.
/// </summary>
public sealed class UpstreamAttributionTests
{
    /// <summary>The problem-details extension member naming the upstream, spelled as the contract does.</summary>
    private const string UpstreamMember = "upstream";

    /// <summary>The enum member standing for the credential edge.</summary>
    private const string SecurityUpstream = "security";

    /// <summary>The enum member standing for the data edge.</summary>
    private const string DataServicesUpstream = "dataservices";

    /// <summary>The projected route the reproduction used.</summary>
    private const string RetrieveRoute = "/v1/datawindow/retrieve";

    // ==============================================================================================
    //  HALF ONE - THE CLIENT RAISES A TYPED FAILURE, SO THE EDGE IS IDENTIFIABLE AT ALL
    // ==============================================================================================

    /// <summary>
    /// Every transport class meaning "the issuance call produced no answer" becomes the typed failure.
    /// </summary>
    /// <param name="fault">The fault the transport raises.</param>
    /// <remarks>
    /// <para>
    /// EACH ROW IS A CLASS THIS CHANNEL REALLY PRODUCES, not a hypothetical: a name-resolution or connect
    /// failure arrives as <see cref="HttpRequestException"/>, a socket fault as <see cref="IOException"/>,
    /// the resilience pipeline's total-request timeout as a cancellation whose token is not the caller's,
    /// and a handler reporting a timeout directly as <see cref="TimeoutException"/>.
    /// </para>
    /// <para>
    /// THE INNER EXCEPTION IS ASSERTED because the operator channel still needs the underlying fault even
    /// though the caller's body must not carry it - preserving it is what makes the conversion a
    /// classification rather than a loss of evidence.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TransportFaultRows))]
    public async Task ATransportFailureOnIssuanceBecomesTheTypedCredentialFailure(Exception fault)
    {
        using FaultingSecurityTransport transport = new(fault);
        using HttpClient httpClient = NewSecurityHttpClient(transport);

        SecurityClient client = NewSecurityClient(httpClient);

        ServiceTokenUnavailableException raised =
            await Assert.ThrowsAsync<ServiceTokenUnavailableException>(() => client.GetTokenAsync(
                new ServiceTokenRequest("powerframework-gateway", "dataservices", ["dataservices.read"]),
                TestContext.Current.CancellationToken));

        Assert.Same(fault, raised.InnerException);

        // ATTEMPTED EXACTLY ONCE. Issuance is a POST whose replay is not safe, so a second attempt here
        // would contradict what the client documents about it.
        Assert.Equal(1, transport.Attempts);

        // THE MESSAGE NAMES THE EDGE AND CARRIES NOTHING ELSE - no address, no port, no credential.
        Assert.Contains("Security", raised.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("security.invalid", raised.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(IssuanceSecret, raised.Message, StringComparison.Ordinal);
    }

    /// <summary>The transport classes the client is required to convert.</summary>
    /// <returns>One row per class.</returns>
    public static TheoryData<Exception> TransportFaultRows() =>
    [
        new HttpRequestException("Name or service not known"),
        new IOException("The connection was reset."),
        new TimeoutException("The operation timed out."),

        // A cancellation that is NOT the caller's - the shape an outbound timeout takes. No token in this
        // row is ever signalled, so it is unambiguously not a caller hanging up.
        new TaskCanceledException("The request was canceled due to the configured HttpClient timeout."),
    ];

    /// <summary>
    /// A cancellation the CALLER caused is rethrown rather than converted.
    /// </summary>
    /// <remarks>
    /// THE DISTINCTION IS LOAD BEARING RATHER THAN TIDY. A caller that hung up is neither this gateway's
    /// fault nor Security's, and the projection answers it with no body at all because there is no longer
    /// a connection to answer on. Converting it would fabricate a Security outage out of every navigation
    /// away and would put a 502 in the operator record for each one.
    /// </remarks>
    [Fact]
    public async Task ACallerCancellationIsNotConvertedIntoACredentialFailure()
    {
        using FaultingSecurityTransport transport =
            new(new TaskCanceledException("cancelled with the caller's own token"));

        using HttpClient httpClient = NewSecurityHttpClient(transport);

        SecurityClient client = NewSecurityClient(httpClient);

        using CancellationTokenSource callerHungUp = new();
        await callerHungUp.CancelAsync();

        OperationCanceledException raised =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetTokenAsync(
                new ServiceTokenRequest("powerframework-gateway", "dataservices", ["dataservices.read"]),
                callerHungUp.Token));

        // NOT the typed credential failure - that is the whole assertion.
        Assert.IsNotType<ServiceTokenUnavailableException>(raised, exactMatch: false);
    }

    /// <summary>
    /// The typed failure is NOT an <see cref="HttpRequestException"/>, which is what keeps the
    /// projection's arm ordering meaningful.
    /// </summary>
    /// <remarks>
    /// If it derived, the arm that exists to catch it FIRST would be shadowed by the very arm it precedes,
    /// and the fix would read as made while changing nothing. The compiler cannot state this, so it is
    /// asserted.
    /// </remarks>
    [Fact]
    public void TheTypedCredentialFailureDoesNotDeriveFromTheTransportException() =>
        Assert.IsNotType<HttpRequestException>(new ServiceTokenUnavailableException(), exactMatch: false);

    // ==============================================================================================
    //  HALF TWO - THE PROBLEM DOCUMENT STAMPS THE UPSTREAM IT WAS GIVEN
    // ==============================================================================================

    /// <summary>
    /// A projection naming Security stamps <c>security</c>; one naming nothing still stamps
    /// <c>dataservices</c>.
    /// </summary>
    /// <param name="upstream">The upstream the projection names, or <see langword="null"/> for the default.</param>
    /// <param name="expected">The member value the body must carry.</param>
    /// <remarks>
    /// THE NULL ROW IS THE REGRESSION GUARD, AND IT IS THE MORE IMPORTANT OF THE THREE. Every
    /// pre-existing projection site passes no upstream and every one of them means DataServices, so a fix
    /// that made <c>security</c> reachable by changing what THOSE produce would have traded one
    /// misattribution for another.
    /// </remarks>
    [Theory]
    [InlineData(null, DataServicesUpstream)]
    [InlineData(SecurityUpstream, SecurityUpstream)]
    [InlineData(DataServicesUpstream, DataServicesUpstream)]
    public void AForwardedFailureNamesTheUpstreamTheProjectionCarries(string? upstream, string expected)
    {
        DataServicesProxyEndpoints.StatusProjection projection = new(
            StatusCodes.Status502BadGateway,
            RetCode.E_RETRY,
            "An upstream failure, for the attribution assertion.",
            FromUpstream: true,
            Upstream: upstream);

        ProblemDetails problem = DataServicesProxyEndpoints.BuildProblem(NewContext(), projection);

        Assert.True(problem.Extensions.TryGetValue(UpstreamMember, out object? named));
        Assert.Equal(expected, named);
    }

    /// <summary>
    /// A rejection this gateway produced itself carries NO <c>upstream</c> member at all.
    /// </summary>
    /// <remarks>
    /// The contract states the member is absent when Gateway composed the response, so publishing a name -
    /// any name - would attribute this service's own refusal of a malformed body to a service that never
    /// saw it. Asserted beside the rows above because one line decides both.
    /// </remarks>
    [Fact]
    public void AGatewayOwnRejectionCarriesNoUpstreamMember()
    {
        DataServicesProxyEndpoints.StatusProjection projection = new(
            StatusCodes.Status400BadRequest,
            RetCode.E_INVALID_ARGUMENT,
            "A rejection this gateway composed itself.",
            FromUpstream: false,

            // Supplied and deliberately ignored: FromUpstream is the gate, so a name reaching a
            // Gateway-own rejection must not leak into the body merely because it was passed.
            Upstream: SecurityUpstream);

        ProblemDetails problem = DataServicesProxyEndpoints.BuildProblem(NewContext(), projection);

        Assert.DoesNotContain(UpstreamMember, problem.Extensions);
    }

    // ==============================================================================================
    //  HALF THREE - THE DEPLOYED ROUTE, WHICH IS THE MEASURED REPRODUCTION
    // ==============================================================================================

    /// <summary>
    /// On the deployed route, a Security outage answers <c>502</c> attributed to <c>security</c>, with
    /// prose that names Security and clears DataServices - and the data edge is never touched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE FINDING, DRIVEN THROUGH THE REAL HOST AND THE REAL ROUTE. The reproduction stopped
    /// security-service alone and received <c>502 { upstream: "dataservices" }</c> naming a healthy
    /// service; here the same condition is produced by the credential provider raising what
    /// <see cref="SecurityClient"/> raises, and the answer must name Security instead.
    /// </para>
    /// <para>
    /// THE STATUS IS DELIBERATELY UNCHANGED AT 502. The contract's own 502 description already reads that
    /// an upstream could not be reached and that the body names which one - so 502 was always right and
    /// only the attribution was wrong. Asserting the status too is what keeps this a fix to the body
    /// rather than a silent change of the status surface.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ASecurityOutageOnTheDeployedRouteIsAttributedToSecurityAndNotToDataServices()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        NeverCalledCallInvoker dataEdge = new();
        RecordingLoggerProvider records = new();

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();

            _ = services.AddSingleton(new DataServicesClient(
                new DataWindowService.DataWindowServiceClient(dataEdge),
                new ColumnExpressionService.ColumnExpressionServiceClient(dataEdge),
                new UnreachableIssuerTokenProvider(),
                NullLogger<DataServicesClient>.Instance));

            _ = services.AddLogging(logging => logging.AddProvider(records));
        });

        using HttpClient client = host.CreateAuthenticatedClient();
        using StringContent body = new("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        Assert.Equal(
            MediaTypeNames.Application.ProblemJson,
            response.Content.Headers.ContentType?.MediaType);

        JsonNode problem = Assert.IsType<JsonObject>(JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

        // THE CALLER'S CHANNEL.
        Assert.Equal(SecurityUpstream, problem[UpstreamMember]?.GetValue<string>());
        Assert.Equal(StatusCodes.Status502BadGateway, problem["status"]?.GetValue<int>());
        Assert.Equal(RetCode.E_RETRY, problem["retCode"]?.GetValue<long>());

        string detail = problem["detail"]?.GetValue<string>() ?? string.Empty;

        Assert.Contains("Security", detail, StringComparison.Ordinal);

        // THE PROSE MUST ALSO CLEAR DATASERVICES, because a reader who has just been told 502 will go and
        // look at whichever service the detail mentions. The old prose sent them to the healthy one.
        Assert.Contains("DataServices is not implicated", detail, StringComparison.Ordinal);

        // NOTHING SENSITIVE CROSSES: no host, no port, no credential.
        Assert.DoesNotContain("security-service:8443", detail, StringComparison.Ordinal);

        // THE DATA EDGE WAS NEVER TOUCHED - the credential is obtained first, so this is the positive
        // evidence that the outage really was Security's.
        Assert.Equal(0, dataEdge.Calls);

        // THE OPERATOR'S CHANNEL, which is the second half of the finding: the record named DataServices
        // too, so an operator reading the log was sent to the same wrong service as the caller.
        (LogLevel Level, string Message) record = Assert.Single(
            records.Entries,
            entry => entry.Message.Contains("reported gRPC status", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains($"upstream {SecurityUpstream}", record.Message, StringComparison.Ordinal);
        Assert.Contains(RetrieveRoute, record.Message, StringComparison.Ordinal);

        // The exception class reaches the record, so the reader can tell a credential outage from a data
        // outage even before reading the attribution.
        Assert.Contains(
            nameof(ServiceTokenUnavailableException),
            record.Message,
            StringComparison.Ordinal);

        // AND THE RECORD DOES NOT CLAIM SECURITY REPORTED A STATUS IT NEVER SENT. With the upstream named
        // correctly the sentence became "upstream security reported gRPC status (unrouted)", reusing the
        // ROUTE marker to mean "no status arrived" - so the record asserted that a service which answered
        // nothing had reported something. The absence is now named as an absence.
        Assert.Contains("(none - no gRPC response arrived)", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("(unrouted)", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// On the same deployed route, a DataServices transport failure is STILL attributed to
    /// <c>dataservices</c>.
    /// </summary>
    /// <remarks>
    /// THE REGRESSION GUARD FOR THE FIX ITSELF, and the reason both arms live in one file. The credential
    /// arm was inserted AHEAD of the transport arm; if it were too broad - catching by base type, or
    /// matching on transport prose - it would swallow this case as well and every DataServices outage
    /// would start blaming Security. Same route, same status, opposite attribution.
    /// </remarks>
    [Fact]
    public async Task ADataServicesTransportFailureOnTheDeployedRouteIsStillAttributedToDataServices()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        RecordingLoggerProvider records = new();

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();

            // The credential is obtained successfully; the DATA edge is what fails, with the bare
            // transport exception the surviving arm exists for.
            _ = services.AddSingleton(new DataServicesClient(
                new DataWindowService.DataWindowServiceClient(
                    new TransportFaultingCallInvoker(
                        new HttpRequestException("Name or service not known (dataservices-service:8443)"))),
                new ColumnExpressionService.ColumnExpressionServiceClient(
                    new TransportFaultingCallInvoker(
                        new HttpRequestException("Name or service not known (dataservices-service:8443)"))),
                new StubServiceTokenProvider(),
                NullLogger<DataServicesClient>.Instance));

            _ = services.AddLogging(logging => logging.AddProvider(records));
        });

        using HttpClient client = host.CreateAuthenticatedClient();
        using StringContent body = new("{}", Encoding.UTF8, MediaTypeNames.Application.Json);

        using HttpResponseMessage response = await client.PostAsync(
            new Uri(RetrieveRoute, UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        JsonNode problem = Assert.IsType<JsonObject>(JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(DataServicesUpstream, problem[UpstreamMember]?.GetValue<string>());

        (LogLevel Level, string Message) record = Assert.Single(
            records.Entries,
            entry => entry.Message.Contains("reported gRPC status", StringComparison.Ordinal));

        Assert.Contains($"upstream {DataServicesUpstream}", record.Message, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// A credential-shaped but meaningless value, so an assertion that it never appears in a message has
    /// something to look for.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT token-shaped and not copied from anywhere: a realistic-looking secret in a test
    /// file invites being mistaken for real material.
    /// </remarks>
    private const string IssuanceSecret = "not-a-real-issuance-secret";

    /// <summary>Builds the HTTP client the security client sends over.</summary>
    /// <param name="transport">The transport double.</param>
    /// <returns>The client.</returns>
    private static HttpClient NewSecurityHttpClient(HttpMessageHandler transport) =>
        new(transport, disposeHandler: false)
        {
            BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute),
        };

    /// <summary>Builds a security client carrying a credential, so issuance is actually attempted.</summary>
    /// <param name="httpClient">The transport.</param>
    /// <returns>The client.</returns>
    /// <remarks>
    /// The system clock is used rather than a frozen one because nothing here reads a timestamp: every
    /// call fails in transit before a token exists to cache or to date.
    /// </remarks>
    private static SecurityClient NewSecurityClient(HttpClient httpClient) =>
        new(
            httpClient,
            NullLogger<SecurityClient>.Instance,
            TimeProvider.System,
            Options.Create(new GatewayOptions { SecurityClientSecret = IssuanceSecret }));

    /// <summary>
    /// A context carrying only what the problem builder reads: a method, a path and a service provider.
    /// </summary>
    /// <returns>The context.</returns>
    /// <remarks>
    /// The options are registered because the builder's retry-after step resolves them on every call; it
    /// applies nothing below 429, so no header is written on any status this file builds.
    /// </remarks>
    private static DefaultHttpContext NewContext()
    {
        ServiceCollection services = new();

        _ = services.AddLogging();
        _ = services.Configure<GatewayOptions>(static _ => { });

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = RetrieveRoute;

        return context;
    }
}

/// <summary>
/// A transport that fails every call with a bare transport exception, reproducing a gRPC edge whose host
/// cannot be reached at all.
/// </summary>
/// <param name="fault">The fault to raise.</param>
/// <remarks>
/// Distinct from the faulting invoker in the streaming suite, which raises a gRPC <see cref="RpcException"/>
/// - a status that ARRIVED. This one reproduces the case where no gRPC response arrives at all, which is
/// the case the surviving transport arm of the projection exists to answer.
/// </remarks>
internal sealed class TransportFaultingCallInvoker(Exception fault) : CallInvoker
{
    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        new(
            Task.FromException<TResponse>(fault),
            Task.FromException<Metadata>(fault),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        new(
            new FaultingReader<TResponse>(fault),
            Task.FromException<Metadata>(fault),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw fault;

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) => throw fault;

    /// <inheritdoc/>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) => throw fault;

    /// <summary>A reader whose first move fails, so the failure precedes the status line.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="fault">The fault to raise.</param>
    private sealed class FaultingReader<T>(Exception fault) : IAsyncStreamReader<T>
    {
        /// <inheritdoc/>
        public T Current => throw fault;

        /// <inheritdoc/>
        public Task<bool> MoveNext(CancellationToken cancellationToken) => Task.FromException<bool>(fault);
    }
}
