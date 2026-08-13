// ======================================================================================================
//  THE C-03 / C-04 PROJECTION CENSUS
//  ----------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS, STATED AS THE GAP IT CLOSES.
//
//  Gateway individually wires THIRTY-NINE routes onto the two DataServices contracts - fifteen for C-03's
//  DataWindowService and twenty-four for C-04's ColumnExpressionService - and its typed client exposes
//  forty-two public operations over the same two generated stubs. Every one of those wirings is a
//  four-part assertion made in source and nowhere verified: a path, an HTTP method, a request/response
//  message pair, and the ONE typed-client member the route delegates to.
//
//  The sibling suites exercise three of the thirty-nine, and they were chosen for good reasons - the
//  update carries the 409, the retrieval carries the stream, the event-gate read carries the session
//  identifier. What they cannot see is a route wired to the WRONG NEIGHBOUR. Every projection has the same
//  shape, so `/calc-all` delegating to `Calc`, `/expressions/set` delegating to `AddExpression`, or
//  `/variable-expressions/get` bound to `SetVariableExpression` all compile, all answer 200, all render a
//  well-formed body of the right family, and all pass every test in this folder. The mis-wiring is
//  invisible precisely because the surface is uniform and hand-written thirty-nine times.
//
//  So this file asserts the census rather than the sample, in four independent directions:
//
//    1. AGAINST THE GENERATED DESCRIPTORS. The expected table's RPC names must equal the two service
//       descriptors' method sets minus exactly the two INVERTED channels. A method added to a contract
//       therefore fails here until it is projected or deliberately excluded - and the exclusion of the
//       inverted pair is asserted by name rather than assumed.
//    2. AGAINST THE PUBLISHED DOCUMENT. Every route the production host declares under /v1/datawindow must
//       appear in the table with the same method, path and operation id, and vice versa. Read from the
//       host's own OpenAPI document, so it is the DEPLOYED registration being measured.
//    3. AGAINST THE RUNNING HOST, ROUTE BY ROUTE. All thirty-nine are driven through the real composition
//       root against a recording transport, and each must reach EXACTLY ONE upstream RPC and it must be
//       the expected one, by fully-qualified gRPC method name.
//    4. AGAINST THE TYPED CLIENT DIRECTLY, for the members no route projects: the session scope, the two
//       inverted channels and the bidirectional event chain. A route census cannot reach those, and they
//       are the members whose mis-wiring would be hardest to notice.
//
//  HOW THE UPSTREAM IS SUBSTITUTED, AND WHY IT IS DONE AT THE CALL INVOKER.
//  The sibling suites derive from the generated stub and override the operations they need. That does not
//  scale to a census: it would mean thirty-nine overrides written by hand, each of which could itself be wired
//  to the wrong method - a double that repeats the very mistake it is meant to catch. Instead the
//  GENERATED stubs are constructed over a recording CallInvoker, which is the seam gRPC itself provides:
//  the stub's own generated code decides which Method<,> descriptor each member passes, so the recording
//  is of the STUB'S choice rather than of a test's restatement of it. Nothing here opens a socket.
//
//  IT REACHES NOTHING DEFERRED. Every route asserted here is a Phase-1 projection of C-03 or C-04. The
//  four reserved 501 families are the sibling deferred-route suite's subject and are not touched, and no
//  project, container or stub of a deferred service is introduced (C-D).
// ======================================================================================================

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using Xunit;

// Google.Protobuf.Reflection and Microsoft.Extensions.DependencyInjection both publish a
// `ServiceDescriptor`, and this file needs both namespaces - the generated descriptors on one side, the
// container registration on the other. Naming the one that is meant keeps the collision surface at zero
// (CS0104) rather than relying on which using happens to be nearer.
using ContractServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// One projected route, as the production host is expected to declare it.
/// </summary>
/// <param name="Rpc">The unqualified RPC name on the owning contract service.</param>
/// <param name="Surface">Which contract the RPC belongs to.</param>
/// <param name="HttpMethod">The single HTTP method the route declares.</param>
/// <param name="Path">The route template, absolute and including the group prefixes.</param>
/// <param name="OperationId">The operation id the published document carries.</param>
/// <remarks>
/// The RPC name is the JOIN KEY of this whole file: it is what ties a row to a generated descriptor method,
/// to a published operation and to an observed upstream call. Everything else is the assertion.
/// </remarks>
public sealed record ProjectedRoute(
    string Rpc,
    ContractService Surface,
    string HttpMethod,
    string Path,
    string OperationId)
{
    /// <summary>The fully-qualified gRPC method name the recording invoker reports.</summary>
    public string GrpcMethodName => Surface switch
    {
        ContractService.DataWindow => $"/{DataWindowService.Descriptor.FullName}/{Rpc}",
        _ => $"/{ColumnExpressionService.Descriptor.FullName}/{Rpc}",
    };

    /// <summary>A stable label for a theory row, so a failure names the route rather than an index.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{HttpMethod} {Path} -> {Rpc}");
}

/// <summary>Which of the two DataServices contracts owns an RPC.</summary>
public enum ContractService
{
    /// <summary>Contract C-03, <c>dataservices.v1.DataWindowService</c>.</summary>
    DataWindow,

    /// <summary>Contract C-04, <c>dataservices.v1.ColumnExpressionService</c>.</summary>
    ColumnExpression,
}

/// <summary>
/// A <see cref="CallInvoker"/> that records which RPC a generated stub chose, and answers with a
/// default-constructed response.
/// </summary>
/// <remarks>
/// <para>
/// THE SEAM IS THE POINT. Every generated client member funnels through one of the four members below,
/// passing the <see cref="Method{TRequest, TResponse}"/> descriptor its own generated code declares. So
/// what is recorded here is the STUB'S routing decision, not a restatement of it written in a test - which
/// is exactly the property a census needs, and exactly what a hand-written per-method override cannot
/// offer.
/// </para>
/// <para>
/// It answers with an EMPTY response of the right type rather than a scripted payload. That is deliberate
/// and it is the whole scope of this file: the subject is which RPC a route reaches, and a payload
/// assertion here would duplicate the sibling suites' work while making a route census depend on
/// thirty-nine message shapes. An empty message is still a well-formed one, so the projection renders it and the
/// route answers 200.
/// </para>
/// <para>
/// Client streaming throws. C-03 and C-04 declare no client-streaming RPC, so reaching that member would
/// mean a contract had grown one and this double had silently accepted it.
/// </para>
/// </remarks>
internal sealed class RecordingCallInvoker : CallInvoker
{
    private readonly List<string> _calls = [];
    private readonly List<object> _requests = [];

    /// <summary>Every RPC reached, by fully-qualified gRPC method name, in order.</summary>
    public ImmutableArray<string> Calls => [.. _calls];

    /// <summary>Every request message received, in the same order as <see cref="Calls"/>.</summary>
    public ImmutableArray<object> Requests => [.. _requests];

    /// <summary>The single RPC reached, or <see langword="null"/> when none or several were.</summary>
    /// <remarks>
    /// EXACTLY ONE, or nothing. A route that fanned out to two upstream calls is as wrong as one that
    /// reached the wrong RPC, and a property that returned the first of several would hide it.
    /// </remarks>
    public string? SingleCall => _calls.Count == 1 ? _calls[0] : null;

    /// <summary>The call options of the most recent call, for a test that asserts on the token.</summary>
    public CallOptions? LastOptions { get; private set; }

    /// <summary>Forgets every recorded call, so one fixture can serve several assertions.</summary>
    public void Clear()
    {
        _calls.Clear();
        _requests.Clear();
        LastOptions = null;
    }

    /// <inheritdoc/>
    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) =>
        throw new InvalidOperationException(
            "Neither C-03 nor C-04 declares a client-streaming RPC, so reaching this member means a "
                + $"contract has grown one and this double accepted it silently. Method: {method.FullName}.");

    /// <inheritdoc/>
    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        Record(method, request: null, options);

        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            new RecordingClientStreamWriter<TRequest>(),
            new ListAsyncStreamReader<TResponse>([]),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    /// <inheritdoc/>
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        Record(method, request, options);

        return new AsyncServerStreamingCall<TResponse>(
            new ListAsyncStreamReader<TResponse>([]),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    /// <inheritdoc/>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        Record(method, request, options);

        return new AsyncUnaryCall<TResponse>(
            Task.FromResult(EmptyResponse<TResponse>()),
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Blocking calls are not used anywhere in this service - every client member is asynchronous - so
    /// reaching this member is a wiring change worth failing over rather than answering.
    /// </remarks>
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        throw new InvalidOperationException(
            "The Gateway client makes no blocking gRPC call, so reaching this member means a member was "
                + $"rewritten synchronously. Method: {method.FullName}.");

    /// <summary>Builds an empty response of the type the stub declared.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <returns>A newly constructed, all-defaults message.</returns>
    /// <remarks>
    /// Constructed REFLECTIVELY because <see cref="CallInvoker"/>'s own type parameters carry no
    /// <c>new()</c> constraint - it is declared over `class` alone, so a generic `new TResponse()` will not
    /// compile here. Every generated protobuf message has a public parameterless constructor, so the
    /// reflection cannot fail for a contract type; the refusal below states that rather than returning null
    /// and failing later somewhere less obvious.
    /// </remarks>
    private static TResponse EmptyResponse<TResponse>()
        where TResponse : class =>
        Activator.CreateInstance<TResponse>()
            ?? throw new InvalidOperationException(
                $"{typeof(TResponse).Name} could not be constructed. Every generated protobuf message has "
                    + "a public parameterless constructor, so this means the recording invoker was handed a "
                    + "response type that is not one.");

    /// <summary>Records one call.</summary>
    /// <param name="method">The descriptor the generated stub supplied.</param>
    /// <param name="request">The request, or <see langword="null"/> for a streaming send half.</param>
    /// <param name="options">The call options the client composed.</param>
    private void Record<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        object? request,
        CallOptions options)
        where TRequest : class
        where TResponse : class
    {
        _calls.Add(method.FullName);

        if (request is not null)
        {
            _requests.Add(request);
        }

        LastOptions = options;
    }
}

/// <summary>
/// The census: all thirty-nine projected routes and the typed-client members no route can reach.
/// </summary>
/// <remarks>
/// Each test owns its host, because each substitutes the DataServices client and the shared class fixture
/// deliberately registers one that refuses on resolution. Owning the host keeps every row
/// order-independent.
/// </remarks>
public sealed class DataServicesRouteCensusTests
{
    /// <summary>The group prefix every projected route sits under.</summary>
    private const string DataWindowPrefix = "/v1/datawindow";

    /// <summary>The sub-group prefix the twenty-four C-04 projections sit under.</summary>
    private const string ExpressionPrefix = DataWindowPrefix + "/expression";

    /// <summary>The published contract document, which is anonymous.</summary>
    private const string DocumentRoute = "/openapi/v1.json";

    /// <summary>The schema name the 409 response references in the published document.</summary>
    /// <remarks>
    /// The generator names a schema after its CLR type, so this is the name of the Gateway-local problem
    /// type. Written once here rather than at each use so a rename shows up as one failure.
    /// </remarks>
    private const string ConflictSchemaName = "ConflictProblemDetails";

    /// <summary>The member of that schema carrying the storage-current row.</summary>
    private const string ConflictMemberName = "conflict";

    /// <summary>The session identifier every session-scoped row supplies.</summary>
    /// <remarks>
    /// An opaque, obviously non-secret token. Gateway holds no state keyed by it - the session lives inside
    /// DataServices - so any well-formed value serves, and the value is asserted to arrive rather than
    /// interpreted.
    /// </remarks>
    private const string SessionId = "census-session";

    /// <summary>
    /// The two RPCs that are deliberately NOT projected, because they are INVERTED bidirectional streams.
    /// </summary>
    /// <remarks>
    /// DataServices calls back into its client on both, because the legacy expects the APPLICATION to
    /// implement the macro switch and to consume the expression trace. An inverted stream has no
    /// request/response direction to project onto a REST route, so their absence is a contract property
    /// rather than an omission - and it is asserted by name here so that projecting one later fails loudly
    /// instead of quietly adding a fortieth route.
    /// </remarks>
    private static readonly ImmutableArray<string> InvertedChannels =
        ["InvokeMethodChannel", "TraceChannel"];

    /// <summary>
    /// THE CENSUS TABLE. Thirty-nine rows, and the count is asserted rather than trusted.
    /// </summary>
    /// <remarks>
    /// Written out by hand ON PURPOSE, because a table derived from the production registration would
    /// assert only that the code equals itself. These rows come from contract C-04's and C-03's own method
    /// lists and from <c>docs/CONTRACTS.md</c>, so a divergence between the contract and the wiring shows
    /// up as a failure here rather than as a passing test over a wrong route.
    /// </remarks>
    private static readonly ImmutableArray<ProjectedRoute> Census =
    [
        // ---- C-03, the DataWindow surface: the triple, the session, the gate, the four headless models --
        new("Retrieve", ContractService.DataWindow, "POST", DataWindowPrefix + "/retrieve", "retrieve"),
        new("OpenValidationSession", ContractService.DataWindow, "POST", DataWindowPrefix + "/sessions",
            "openValidationSession"),
        new("CloseValidationSession", ContractService.DataWindow, "DELETE",
            DataWindowPrefix + "/sessions/{sessionId}", "closeValidationSession"),
        new("Update", ContractService.DataWindow, "POST", DataWindowPrefix + "/update", "updateDataWindow"),
        new("GetEventGate", ContractService.DataWindow, "GET", DataWindowPrefix + "/event-gate",
            "getEventGate"),
        new("DisableEvent", ContractService.DataWindow, "POST", DataWindowPrefix + "/event-gate/disable",
            "disableEvent"),
        new("EnableEvent", ContractService.DataWindow, "POST", DataWindowPrefix + "/event-gate/enable",
            "enableEvent"),
        new("GetDropDownSearchState", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/drop-down-search/state", "getDropDownSearchState"),
        new("ApplyDropDownSearch", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/drop-down-search/apply", "applyDropDownSearch"),
        new("GetColumnSortState", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/column-sort/state", "getColumnSortState"),
        new("ApplyColumnSort", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/column-sort/apply", "applyColumnSort"),
        new("GetContextMenuModel", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/context-menu/state", "getContextMenuModel"),
        new("ApplyContextMenuModel", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/context-menu/apply", "applyContextMenuModel"),
        new("GetRowSelectState", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/row-select/state", "getRowSelectState"),
        new("ApplyRowSelectStyle", ContractService.DataWindow, "POST",
            DataWindowPrefix + "/row-select/style", "applyRowSelectStyle"),

        // ---- C-04, the column-expression surface ------------------------------------------------------
        new("OpenExpressionSession", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/sessions", "openExpressionSession"),
        new("CloseExpressionSession", ContractService.ColumnExpression, "DELETE",
            ExpressionPrefix + "/sessions/{sessionId}", "closeExpressionSession"),
        new("AddExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/expressions/add", "addExpression"),
        new("SetExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/expressions/set", "setExpression"),
        new("GetExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/expressions/get", "getExpression"),
        new("RemoveExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/expressions/remove", "removeExpression"),
        new("RemoveAllExpressions", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/expressions/remove-all", "removeAllExpressions"),
        new("AddVariable", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/variables/add", "addVariable"),
        new("SetVariable", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/variables/set", "setVariable"),
        new("AddVariableExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/variable-expressions/add", "addVariableExpression"),
        new("SetVariableExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/variable-expressions/set", "setVariableExpression"),
        new("GetVariableExpression", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/variable-expressions/get", "getVariableExpression"),
        new("AddForeignVariable", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/foreign-variables/add", "addForeignVariable"),
        new("SetRelativeColumns", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/relative-columns/set", "setRelativeColumns"),
        new("SetExpressionFlag", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/flags/set", "setExpressionFlag"),
        new("Calc", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/calc", "calc"),
        new("CalcAll", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/calc-all", "calcAll"),
        new("CalcEmpty", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/calc-empty",
            "calcEmpty"),
        new("CalcItem", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/calc-item",
            "calcItem"),
        new("SetEnabled", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/enabled",
            "setExpressionServiceEnabled"),
        new("SetTrace", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/trace",
            "setExpressionTrace"),
        new("GetServiceState", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/state",
            "getExpressionServiceState"),
        new("GetExpressionState", ContractService.ColumnExpression, "POST",
            ExpressionPrefix + "/engine-state", "getExpressionState"),
        new("EventStream", ContractService.ColumnExpression, "POST", ExpressionPrefix + "/event-stream",
            "getExpressionEventStream"),
    ];

    /// <summary>The census as theory rows.</summary>
    public static TheoryData<ProjectedRoute> AllRoutes()
    {
        TheoryData<ProjectedRoute> rows = [];

        foreach (ProjectedRoute route in Census)
        {
            rows.Add(route);
        }

        return rows;
    }

    /// <summary>
    /// The census covers every RPC the two contracts declare, less exactly the two inverted channels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// READ FROM THE GENERATED DESCRIPTORS, so the contract itself is the authority. This is the row that
    /// makes the other three trustworthy: without it the census could be internally consistent and still
    /// omit an RPC entirely, and an omitted RPC is the one failure a per-route test can never detect.
    /// </para>
    /// <para>
    /// The exclusions are asserted BY NAME. A test that merely counted "thirty-nine of forty-two" would
    /// pass if the wrong three were missing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCensusIsExactlyTheProjectableSurfaceOfBothContracts()
    {
        Assert.Equal(39, Census.Length);

        // No duplicate anywhere: not a path, not an operation id, not an RPC.
        Assert.Equal(39, Census.Select(route => route.Path + route.HttpMethod).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(39, Census.Select(route => route.OperationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(39, Census.Select(route => route.GrpcMethodName).Distinct(StringComparer.Ordinal).Count());

        AssertSurfaceCovered(ContractService.DataWindow, DataWindowService.Descriptor, expectedProjected: 15);
        AssertSurfaceCovered(
            ContractService.ColumnExpression,
            ColumnExpressionService.Descriptor,
            expectedProjected: 24);

        // The inverted pair belongs to C-04 and is projected by nothing.
        foreach (string inverted in InvertedChannels)
        {
            Assert.Contains(
                inverted,
                ColumnExpressionService.Descriptor.Methods.Select(method => method.Name),
                StringComparer.Ordinal);

            Assert.DoesNotContain(
                inverted,
                Census.Select(route => route.Rpc),
                StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The production host declares exactly the census, and the published document agrees member for
    /// member.
    /// </summary>
    /// <remarks>
    /// Read from the DEPLOYED host's own OpenAPI document rather than from a hand-assembled pipeline, so
    /// what is measured is the registration that ships. Both directions are checked: a route in the
    /// document that is not in the census, and a census row the document does not declare.
    /// </remarks>
    [Fact]
    public async Task TheProductionHostDeclaresExactlyTheCensusWithMatchingOperationIds()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        // AUTHENTICATED, BECAUSE THE DOCUMENT ROUTE IS. Gateway's fallback authorization policy applies
        // to /openapi/v1.json - it carries no AllowAnonymous exemption, and the enumerated anonymous
        // exceptions in this system are /health on all four services and Security's key set and discovery
        // document, and nothing else. An anonymous read here would be measuring a 401, which is a fact
        // about the boundary rather than about the census this suite exists to check.
        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument document = await ReadDocumentAsync(client);

        Dictionary<string, string> declared = new(StringComparer.Ordinal);

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith(DataWindowPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                string key = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{operation.Name.ToUpperInvariant()} {path.Name}");

                declared[key] = operation.Value.TryGetProperty("operationId", out JsonElement id)
                    ? id.GetString() ?? string.Empty
                    : string.Empty;
            }
        }

        // Neither direction may have a member the other lacks.
        Assert.Equal(
            Census.Select(route => $"{route.HttpMethod} {route.Path}").OrderBy(key => key, StringComparer.Ordinal),
            declared.Keys.OrderBy(key => key, StringComparer.Ordinal));

        foreach (ProjectedRoute route in Census)
        {
            string key = $"{route.HttpMethod} {route.Path}";

            Assert.True(
                declared.TryGetValue(key, out string? operationId),
                $"The published document declares no {key}.");

            Assert.Equal(route.OperationId, operationId);
        }
    }

    /// <summary>
    /// Every projected route publishes the generated descriptor's own gRPC method and message types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TYPE HALF OF THE CONTRACT, MEASURED AGAINST THE PROTOCOL DEFINITION RATHER THAN A LIST. The route
    /// census above proves each path reaches the right RPC; that says nothing about what the document TELLS a
    /// consumer to send and expect. <c>x-grpc-method</c>, <c>x-proto-request</c> and <c>x-proto-response</c>
    /// are independent strings in the published document, so a wrong request or response message name there
    /// compiles, ships, and misleads every generated client built from it - exactly the class of defect that
    /// an unexercised surface hides.
    /// </para>
    /// <para>
    /// The oracle is <see cref="ContractServiceDescriptor"/>, so this cannot be satisfied by transcribing the
    /// same mistake twice: the expected values are read out of the generated descriptor's input and output
    /// message types at assertion time.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProjectedRoutePublishesTheDescriptorsOwnMethodAndMessageTypes()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        // AUTHENTICATED, BECAUSE THE DOCUMENT ROUTE IS. Gateway's fallback authorization policy applies
        // to /openapi/v1.json - it carries no AllowAnonymous exemption, and the enumerated anonymous
        // exceptions in this system are /health on all four services and Security's key set and discovery
        // document, and nothing else. An anonymous read here would be measuring a 401, which is a fact
        // about the boundary rather than about the census this suite exists to check.
        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument document = await ReadDocumentAsync(client);

        int checkedRoutes = 0;

        foreach (ProjectedRoute route in Census)
        {
            JsonElement operation = FindOperation(document, route);

            ContractServiceDescriptor descriptor = route.Surface == ContractService.DataWindow
                ? DataWindowService.Descriptor
                : ColumnExpressionService.Descriptor;

            Google.Protobuf.Reflection.MethodDescriptor method =
                descriptor.FindMethodByName(route.Rpc)
                    ?? throw new InvalidOperationException(
                        $"The generated descriptor for {descriptor.FullName} declares no {route.Rpc}, so the "
                            + "census names an RPC that does not exist.");

            Assert.Equal(
                $"{descriptor.FullName}/{route.Rpc}",
                ReadExtension(operation, "x-grpc-method", route));

            Assert.Equal(
                method.InputType.FullName,
                ReadExtension(operation, "x-proto-request", route));

            Assert.Equal(
                method.OutputType.FullName,
                ReadExtension(operation, "x-proto-response", route));

            checkedRoutes++;
        }

        // Guards against a silently empty loop: the count is the census, not a number this test chose.
        Assert.Equal(Census.Length, checkedRoutes);
    }

    /// <summary>
    /// The update route's 409 publishes the CONFLICT schema, so a consumer of the document can see the
    /// current row state the response actually carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE 409 IS THE ONE RESPONSE IN THIS CONTRACT THAT CARRIES A PAYLOAD BEYOND ITS STATUS.</b> On an
    /// optimistic-concurrency mismatch the projection answers a problem document with a <c>conflict</c>
    /// member holding the storage-current row, which is what makes the published retry-or-surface policy
    /// actionable: a caller that cannot see what it lost has nothing to decide with. Declaring the response
    /// as a BARE problem document - which is what a plain problem declaration produces - published a shape
    /// the route does not answer, and the authored <c>gateway.v1.yaml</c> declares a
    /// <c>ConflictProblemDetails</c> schema precisely so that shape is visible.
    /// </para>
    /// <para>
    /// Asserted against the GENERATED document rather than the authored one, because the authored contract is
    /// already checked by the contracts suite and the divergence being guarded here is between the two. Both
    /// the reference and the referenced schema are checked: a <c>$ref</c> to a schema that declared no
    /// conflict member would satisfy the first assertion while publishing nothing more than a status.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheUpdateRoutePublishesTheConflictSchemaRatherThanABareProblemDocument()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        using HttpClient client = host.CreateAuthenticatedClient();

        using JsonDocument document = await ReadDocumentAsync(client);

        ProjectedRoute update = Census.Single(row =>
            string.Equals(row.Rpc, "Update", StringComparison.Ordinal)
            && row.Surface == ContractService.DataWindow);

        JsonElement operation = FindOperation(document, update);

        Assert.True(
            operation.GetProperty("responses").TryGetProperty("409", out JsonElement conflict),
            "The update operation publishes no 409, so the conflict the contract rests on is invisible.");

        Assert.True(
            conflict.GetProperty("content")
                .TryGetProperty(MediaTypeNames.Application.ProblemJson, out JsonElement problemJson),
            $"The update operation's 409 declares no {MediaTypeNames.Application.ProblemJson} content.");

        string? reference = problemJson.GetProperty("schema").GetProperty("$ref").GetString();

        Assert.False(
            string.IsNullOrEmpty(reference),
            "The update operation's 409 references no schema, so its payload is undescribed.");

        Assert.EndsWith(ConflictSchemaName, reference!, StringComparison.Ordinal);

        // THE REFERENCED SCHEMA MUST ACTUALLY CARRY THE MEMBER. A reference alone proves only that a name
        // was published; the conflict member is what a caller reads the storage-current row from.
        Assert.True(
            document.RootElement.GetProperty("components")
                .GetProperty("schemas")
                .TryGetProperty(ConflictSchemaName, out JsonElement schema),
            $"The document references {ConflictSchemaName} but declares no such schema.");

        Assert.True(
            schema.GetProperty("properties").TryGetProperty(ConflictMemberName, out _),
            $"{ConflictSchemaName} declares no {ConflictMemberName} member, so the 409 publishes nothing "
                + "beyond its status.");
    }

    /// <summary>Finds one census row's operation object in the published document.</summary>
    /// <param name="document">The published document.</param>
    /// <param name="route">The census row.</param>
    /// <returns>The operation object.</returns>
    private static JsonElement FindOperation(JsonDocument document, ProjectedRoute route)
    {
        Assert.True(
            document.RootElement.GetProperty("paths").TryGetProperty(route.Path, out JsonElement path),
            $"The published document declares no path {route.Path}.");

        string verb = route.HttpMethod.ToLowerInvariant();

        Assert.True(
            path.TryGetProperty(verb, out JsonElement operation),
            $"The published document declares {route.Path} but not its {route.HttpMethod} operation.");

        return operation;
    }

    /// <summary>Reads one required specification extension off a published operation.</summary>
    /// <param name="operation">The operation object.</param>
    /// <param name="extension">The extension name.</param>
    /// <param name="route">The census row, named in the failure message.</param>
    /// <returns>The extension's string value.</returns>
    private static string ReadExtension(JsonElement operation, string extension, ProjectedRoute route)
    {
        Assert.True(
            operation.TryGetProperty(extension, out JsonElement value),
            $"{route.HttpMethod} {route.Path} publishes no {extension}, so a consumer of the document cannot "
                + "tell which protobuf message the operation carries.");

        string? text = value.GetString();

        Assert.False(
            string.IsNullOrEmpty(text),
            $"{route.HttpMethod} {route.Path} publishes an empty {extension}.");

        return text!;
    }

    /// <summary>
    /// Each projected route reaches exactly one upstream RPC, and it is the expected one.
    /// </summary>
    /// <param name="route">The census row under test.</param>
    /// <remarks>
    /// <para>
    /// THE ROW THAT CATCHES A WRONG NEIGHBOUR. Every projection in this contract has the same shape, so a
    /// route delegating to the adjacent typed-client member compiles, answers 200 and renders a
    /// well-formed body. Only the observed gRPC method name distinguishes them.
    /// </para>
    /// <para>
    /// EXACTLY ONE CALL, asserted as well as which one. A projection that fanned out - re-opening a session
    /// on the way past, say - would be as wrong as one bound to the wrong RPC.
    /// </para>
    /// <para>
    /// The request body is the empty JSON object for every body-bound row. That is sufficient because the
    /// subject is routing: the upstream is scripted to answer an empty message of the right type, so a 200
    /// proves the projection bound, invoked and rendered. Payload semantics belong to the sibling suites
    /// and to the contract tests.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EachProjectedRouteReachesExactlyItsOwnUpstreamRpc(ProjectedRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        RecordingCallInvoker upstream = new();

        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteRecordingClient(host, upstream);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, route);

        // 200, and specifically not a 404 from a mistyped path, a 405 from the wrong verb, a 400 from a
        // request type the body cannot bind onto, or a 500 from a projection that faulted.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(route.GrpcMethodName, upstream.SingleCall);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The verb is exact: a projected route refuses every method it does not declare.
    /// </summary>
    /// <param name="route">The census row under test.</param>
    /// <remarks>
    /// A route registered with an extra verb would let a caller reach it in a way the contract does not
    /// publish, and the projection helpers each declare exactly one method - so this asserts the helper was
    /// used as intended. The upstream must not be reached at all on a refused verb.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EachProjectedRouteDeclaresOnlyItsOwnHttpMethod(ProjectedRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);

        RecordingCallInvoker upstream = new();

        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        SubstituteRecordingClient(host, upstream);

        using HttpClient client = host.CreateAuthenticatedClient();

        // PUT is declared by no projected route in this contract, so it is the honest probe: it is neither
        // the method under test nor a method any neighbour uses.
        using HttpResponseMessage response = await SendAsync(client, route, overrideMethod: HttpMethod.Put);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(upstream.Calls);
    }

    /// <summary>
    /// The typed-client members no route can project are wired to their own RPCs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FOUR MEMBERS A ROUTE CENSUS CANNOT REACH, and they are the ones whose mis-wiring would be hardest to
    /// notice: the validation-session SCOPE, which opens and later closes; the bidirectional event chain;
    /// and the two INVERTED channels, on which DataServices calls back into its client.
    /// </para>
    /// <para>
    /// Each is asserted by the RPC it opens, in order. The scope additionally proves the pairing - open then
    /// close, two calls and not one - which is the property that makes an abandoned session impossible to
    /// mistake for a closed one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheUnprojectedTypedClientMembersEachReachTheirOwnRpc()
    {
        RecordingCallInvoker upstream = new();
        DataServicesClient client = BuildClient(upstream);

        // 1. The session scope: opens on creation, closes on disposal, in that order.
        await using (ValidationSessionScope scope =
            await client.OpenValidationSessionScopeAsync(
                new OpenValidationSessionRequest(),
                TestContext.Current.CancellationToken))
        {
            Assert.Equal(
                $"/{DataWindowService.Descriptor.FullName}/OpenValidationSession",
                upstream.SingleCall);

            Assert.NotNull(scope);
        }

        Assert.Equal(
            [
                $"/{DataWindowService.Descriptor.FullName}/OpenValidationSession",
                $"/{DataWindowService.Descriptor.FullName}/CloseValidationSession",
            ],
            upstream.Calls);

        // 2. The bidirectional event chain, which carries the 22-event ordered surface.
        upstream.Clear();

        await using (DataWindowEventChannel channel =
            await client.OpenEventChainAsync(TestContext.Current.CancellationToken))
        {
            Assert.Equal($"/{DataWindowService.Descriptor.FullName}/EventChain", upstream.SingleCall);
            Assert.NotNull(channel);
        }

        // 3. The expression event stream: a server stream, enumerated to open the call.
        upstream.Clear();

        await foreach (EventStreamResponse _ in client.StreamExpressionEventsAsync(
            new EventStreamRequest(),
            TestContext.Current.CancellationToken))
        {
            // The recording upstream yields nothing, so the body is unreachable. Opening the call is the
            // assertion, and it is made below.
        }

        Assert.Equal(
            $"/{ColumnExpressionService.Descriptor.FullName}/EventStream",
            upstream.SingleCall);

        // 4. The two INVERTED channels. Both are bidirectional and both are opened by the client, so the
        //    assertion is that each opens ITS OWN channel - the one pairing no route could ever check.
        upstream.Clear();

        await client.ServeMacroInvocationsAsync(
            static (_, _) => ValueTask.FromResult(new InvokeMethodResponse()),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            $"/{ColumnExpressionService.Descriptor.FullName}/InvokeMethodChannel",
            upstream.SingleCall);

        upstream.Clear();

        await foreach (TraceRecord _ in client.StreamExpressionTraceAsync(
            new TraceChannelRequest(),
            TestContext.Current.CancellationToken))
        {
            // As above: the recording upstream yields nothing.
        }

        Assert.Equal(
            $"/{ColumnExpressionService.Descriptor.FullName}/TraceChannel",
            upstream.SingleCall);
    }

    /// <summary>
    /// Every projected route's typed-client member is reachable directly and reaches the same RPC.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CLIENT MEASURED WITHOUT THE ROUTE. The route census proves the HTTP surface is wired correctly;
    /// this proves the CLIENT is, independently of it. The two can disagree in one direction that matters:
    /// a client member wired to the wrong stub method would be caught by the route census only for the
    /// routes that project it, and the typed client is public surface that a future in-process consumer
    /// could use without any route at all.
    /// </para>
    /// <para>
    /// Driven by reflection over the census rather than by thirty-nine hand-written calls, because a hand-written
    /// call list is a second place for the same mistake to be made. The member name is derived from the RPC
    /// name by the one convention the client follows without exception - <c>&lt;Rpc&gt;Async</c> - with the
    /// single documented departure noted below.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryProjectedRpcHasATypedClientMemberThatReachesIt()
    {
        RecordingCallInvoker upstream = new();
        DataServicesClient client = BuildClient(upstream);

        // THE DEVIATION MAP IS ASSERTED IN BOTH DIRECTIONS BELOW, so it cannot become a place a rename
        // hides. An entry that is no longer needed fails just as loudly as a missing one.
        AssertDeviationMapIsExactlyWhatTheClientNeeds();

        foreach (ProjectedRoute route in Census)
        {
            upstream.Clear();

            string memberName = ClientMemberName(route.Rpc);

            System.Reflection.MethodInfo? member = FindClientMember(memberName);

            Assert.True(
                member is not null,
                $"DataServicesClient declares no two-parameter {memberName} for RPC {route.Rpc}.");

            System.Reflection.ParameterInfo[] parameters = member!.GetParameters();

            object request = Activator.CreateInstance(parameters[0].ParameterType)
                ?? throw new InvalidOperationException(
                    $"The request type for {memberName} could not be constructed.");

            object? invoked = member.Invoke(
                client,
                [request, TestContext.Current.CancellationToken]);

            await ConsumeAsync(invoked, member.ReturnType);

            Assert.Equal(route.GrpcMethodName, upstream.SingleCall);
        }
    }

    /// <summary>
    /// The typed-client operations that legitimately project to no route, each exercised directly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THESE FOUR ARE WHY THE CLIENT SURFACE IS LARGER THAN THE ROUTE SURFACE, and each is named rather than
    /// merely tolerated. <c>OpenValidationSessionScopeAsync</c> and <c>OpenEventChainAsync</c> return
    /// disposable conversations rather than a response message, so there is nothing for one request and one
    /// response to project; <c>ServeMacroInvocationsAsync</c> and <c>StreamExpressionTraceAsync</c> are the
    /// two INVERTED channels of AAP Section 0.4.3 C-04, where DataServices calls back into its client, and an
    /// inbound HTTP route is the wrong shape for a callback by construction.
    /// </para>
    /// <para>
    /// Naming them is what lets the surface census below be an equality rather than a subset check, so a
    /// newly added client operation cannot slip in unexercised - which is precisely the gap this file closes.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<string> NonProjectedClientOperations =
    [
        "OpenValidationSessionScopeAsync",
        "OpenEventChainAsync",
        "ServeMacroInvocationsAsync",
        "StreamExpressionTraceAsync",
    ];

    /// <summary>
    /// The client declares no asynchronous operation this file leaves unexercised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ONE ASSERTION THAT KEEPS THIS FILE HONEST AS THE CLIENT GROWS. Every other test here measures the
    /// operations it already knows about, so every one of them would still pass if a forty-third operation
    /// were added and never touched. This test is an equality between the client's whole asynchronous public
    /// surface and the union of the census and the four documented non-projected operations, so a new
    /// operation fails it until it is either projected as a route or named as a deliberate exception.
    /// </para>
    /// <para>
    /// Asynchronous return shape is the filter because that is what an operation is on this client: a
    /// property getter, <c>Equals</c>, <c>GetHashCode</c>, <c>GetType</c> and <c>ToString</c> are not
    /// operations and are excluded by returning nothing awaitable.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheClientDeclaresNoAsynchronousOperationThisCensusLeavesUnexercised()
    {
        ImmutableArray<string> declared =
        [
            .. typeof(DataServicesClient)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(candidate => candidate.DeclaringType == typeof(DataServicesClient))
                .Where(candidate => !candidate.IsSpecialName)
                .Where(candidate => IsAsynchronous(candidate.ReturnType))
                .Select(candidate => candidate.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        ImmutableArray<string> exercised =
        [
            .. Census.Select(route => ClientMemberName(route.Rpc))
                .Concat(NonProjectedClientOperations)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        // Reported as two set differences rather than one sequence comparison, because "which operation" is
        // the only useful thing to know when this fails and a sequence diff buries it.
        ImmutableArray<string> unexercised = [.. declared.Except(exercised, StringComparer.Ordinal)];
        ImmutableArray<string> vanished = [.. exercised.Except(declared, StringComparer.Ordinal)];

        Assert.True(
            unexercised.IsEmpty,
            "DataServicesClient declares asynchronous operations that no test in this file drives: "
                + string.Join(", ", unexercised)
                + ". Project each as a route or name it in NonProjectedClientOperations with the reason.");

        Assert.True(
            vanished.IsEmpty,
            "This census expects client operations that DataServicesClient no longer declares: "
                + string.Join(", ", vanished)
                + ". The census or the deviation map is stale.");

        Assert.Equal(declared, exercised);
        Assert.Equal(Census.Length + NonProjectedClientOperations.Length, declared.Length);
    }

    /// <summary>Reports whether one return type makes its method an asynchronous operation.</summary>
    /// <param name="returnType">The return type.</param>
    /// <returns><see langword="true"/> for a task, a value task, or an async sequence.</returns>
    private static bool IsAsynchronous(Type returnType)
    {
        if (returnType == typeof(Task) || returnType == typeof(ValueTask))
        {
            return true;
        }

        if (!returnType.IsGenericType)
        {
            return false;
        }

        Type definition = returnType.GetGenericTypeDefinition();

        return definition == typeof(Task<>)
            || definition == typeof(ValueTask<>)
            || definition == typeof(IAsyncEnumerable<>);
    }

    /// <summary>
    /// The RPC names whose typed-client member is deliberately not spelled <c>&lt;Rpc&gt;Async</c>.
    /// </summary>
    /// <remarks>
    /// ONE ENTRY, AND IT EARNS ITS PLACE. <c>EventStream</c> is a bare, contract-shaped RPC name that says
    /// nothing about which of the two contracts it belongs to; the client member says both what it streams
    /// and that it is the expression surface's. The census records the departure rather than relaxing the
    /// convention to a fuzzy match, because a fuzzy match would also accept a member wired to the wrong RPC.
    /// </remarks>
    private static readonly ImmutableDictionary<string, string> ClientMemberDeviations =
        ImmutableDictionary<string, string>.Empty
            .Add("EventStream", "StreamExpressionEventsAsync");

    /// <summary>Resolves the typed-client member name for one RPC.</summary>
    /// <param name="rpc">The RPC name as the generated descriptor spells it.</param>
    /// <returns>The member name to reflect for.</returns>
    private static string ClientMemberName(string rpc) =>
        ClientMemberDeviations.TryGetValue(rpc, out string? deviation) ? deviation : $"{rpc}Async";

    /// <summary>Finds one public two-parameter client member by exact name.</summary>
    /// <param name="memberName">The member name.</param>
    /// <returns>The member, or <see langword="null"/> when the client declares none.</returns>
    /// <remarks>
    /// The arity filter is what keeps <c>OpenValidationSessionAsync</c> distinct from the scope-returning
    /// overload family, and an exact ordinal name match is what keeps this from silently accepting a
    /// similarly named member.
    /// </remarks>
    private static System.Reflection.MethodInfo? FindClientMember(string memberName) =>
        typeof(DataServicesClient)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Name, memberName, StringComparison.Ordinal)
                && candidate.GetParameters().Length == 2);

    /// <summary>Asserts the deviation map is neither short nor stale.</summary>
    /// <remarks>
    /// A deviation map is only trustworthy if it is checked against the thing it describes. Every mapped RPC
    /// must genuinely lack a conventionally named member - otherwise the map is masking one - and no mapped
    /// RPC may have gained one, which is how a later rename back to the convention gets noticed.
    /// </remarks>
    private static void AssertDeviationMapIsExactlyWhatTheClientNeeds()
    {
        foreach (KeyValuePair<string, string> deviation in ClientMemberDeviations)
        {
            Assert.Contains(deviation.Key, Census.Select(route => route.Rpc), StringComparer.Ordinal);

            Assert.True(
                FindClientMember($"{deviation.Key}Async") is null,
                $"The deviation map still routes RPC {deviation.Key} to {deviation.Value}, but the client "
                    + $"now also declares {deviation.Key}Async. The map entry is stale and the census would "
                    + "stop measuring the conventionally named member.");

            Assert.True(
                FindClientMember(deviation.Value) is not null,
                $"The deviation map routes RPC {deviation.Key} to {deviation.Value}, which the client does "
                    + "not declare with two parameters.");
        }

        ImmutableArray<string> unnecessary =
        [
            .. ClientMemberDeviations.Keys
                .Where(rpc => FindClientMember($"{rpc}Async") is not null)
                .OrderBy(rpc => rpc, StringComparer.Ordinal),
        ];

        Assert.Empty(unnecessary);
    }

    /// <summary>Asserts one contract's projected method set equals the census rows for that contract.</summary>
    /// <param name="surface">The contract being checked.</param>
    /// <param name="descriptor">Its generated service descriptor.</param>
    /// <param name="expectedProjected">How many of its RPCs the census must project.</param>
    private static void AssertSurfaceCovered(
        ContractService surface,
        ContractServiceDescriptor descriptor,
        int expectedProjected)
    {
        ImmutableArray<string> projected =
        [
            .. Census.Where(route => route.Surface == surface)
                .Select(route => route.Rpc)
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        ImmutableArray<string> projectable =
        [
            .. descriptor.Methods
                .Select(method => method.Name)
                .Where(name => !InvertedChannels.Contains(name, StringComparer.Ordinal))
                .Where(name => !string.Equals(name, "EventChain", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal),
        ];

        Assert.Equal(expectedProjected, projected.Length);
        Assert.Equal(projectable, projected);
    }

    /// <summary>Builds a typed client over the recording invoker and nothing else.</summary>
    /// <param name="upstream">The recording invoker both generated stubs are constructed over.</param>
    /// <returns>The client.</returns>
    /// <remarks>
    /// THE GENERATED STUBS ARE THE REAL ONES. Only the transport beneath them is substituted, which is what
    /// makes the recorded method name the stub's own routing decision rather than a test's restatement of
    /// it. No socket is opened and no credential is real - the token provider answers a placeholder.
    /// </remarks>
    private static DataServicesClient BuildClient(RecordingCallInvoker upstream) =>
        new(
            new DataWindowService.DataWindowServiceClient(upstream),
            new ColumnExpressionService.ColumnExpressionServiceClient(upstream),
            new StubServiceTokenProvider(),
            NullLogger<DataServicesClient>.Instance);

    /// <summary>Registers a client over the recording invoker on a locally owned host.</summary>
    /// <param name="fixture">The host to configure.</param>
    /// <param name="upstream">The recording invoker.</param>
    /// <remarks>
    /// A LOCALLY OWNED HOST EVERY TIME. The shared class fixture registers a DataWindow client that refuses
    /// on resolution - correct for a suite whose subject is authorization - and mutating it would couple
    /// every row in this file to execution order. The registration goes through the fixture's own extension
    /// point, applied last, so token validation, the frozen clock and the unreachable-network handler stay
    /// exactly as the fixture established them.
    /// </remarks>
    private static void SubstituteRecordingClient(
        GatewayTestHostFixture fixture,
        RecordingCallInvoker upstream)
    {
        DataServicesClient substituted = BuildClient(upstream);

        fixture.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();
            services.AddSingleton(substituted);
        });
    }

    /// <summary>Sends the request one census row describes.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="route">The row.</param>
    /// <param name="overrideMethod">A method to use instead of the row's own, for the verb probe.</param>
    /// <returns>The response, which the caller disposes.</returns>
    /// <remarks>
    /// The three request shapes the projection helpers declare: a JSON body for a body-bound route, a path
    /// segment for the session-scoped DELETE, and a query-string parameter for the session-scoped GET. The
    /// session identifier is supplied in whichever position the route template implies, which is itself a
    /// small assertion - a route that moved the parameter would fail binding rather than answer 200.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        ProjectedRoute route,
        HttpMethod? overrideMethod = null)
    {
        HttpMethod method = overrideMethod ?? new HttpMethod(route.HttpMethod);

        bool templated = route.Path.Contains("{sessionId}", StringComparison.Ordinal);

        string path = templated
            ? route.Path.Replace("{sessionId}", SessionId, StringComparison.Ordinal)
            : string.Equals(route.HttpMethod, "GET", StringComparison.Ordinal)
                ? $"{route.Path}?sessionId={SessionId}"
                : route.Path;

        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative));

        // A body is sent for exactly the routes that declare one. The empty JSON object binds onto every
        // projected request message, because protobuf JSON treats every field as optional.
        if (!templated && !string.Equals(route.HttpMethod, "GET", StringComparison.Ordinal))
        {
            request.Content = new StringContent("{}", Encoding.UTF8, MediaTypeNames.Application.Json);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Reads the published OpenAPI document.</summary>
    /// <param name="client">An authenticated client; the document route is behind the boundary.</param>
    /// <returns>The parsed document, which the caller disposes.</returns>
    private static async Task<JsonDocument> ReadDocumentAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(DocumentRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The published document was empty.");
    }

    /// <summary>Awaits or enumerates whatever a reflected client member returned.</summary>
    /// <param name="invoked">The returned value.</param>
    /// <param name="returnType">Its declared type.</param>
    /// <returns>A task that completes once the call has been opened and drained.</returns>
    /// <remarks>
    /// A projected member returns either a <see cref="Task{TResult}"/> or an
    /// <see cref="IAsyncEnumerable{T}"/>. The second must be ENUMERATED rather than merely obtained,
    /// because an iterator method opens nothing until it is first moved - so a test that only called it
    /// would record no upstream call and would pass for the wrong reason.
    /// </remarks>
    private static async Task ConsumeAsync(object? invoked, Type returnType)
    {
        Assert.NotNull(invoked);

        if (typeof(Task).IsAssignableFrom(returnType))
        {
            await (Task)invoked;

            return;
        }

        Type? sequence = returnType.IsGenericType
            && returnType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)
                ? returnType
                : Array.Find(
                    returnType.GetInterfaces(),
                    candidate => candidate.IsGenericType
                        && candidate.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

        Assert.True(
            sequence is not null,
            $"A projected client member returned {returnType.Name}, which is neither awaitable nor an "
                + "async sequence. The census cannot drive it, so the convention it departs from has to be "
                + "recorded rather than skipped.");

        // DISPATCHED THROUGH A GENERIC HELPER RATHER THAN BY REFLECTING ON THE ENUMERATOR. A compiler-
        // generated async iterator implements IAsyncEnumerator<T> EXPLICITLY, so `MoveNextAsync` is not a
        // public member of the concrete state-machine type and looking it up by name finds nothing. Closing
        // the helper over the element type puts the interface dispatch back in the compiler's hands.
        System.Reflection.MethodInfo drain =
            typeof(DataServicesRouteCensusTests)
                .GetMethod(nameof(DrainAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(sequence!.GetGenericArguments()[0]);

        await (Task)drain.Invoke(null, [invoked])!;
    }

    /// <summary>Opens and drains one async sequence.</summary>
    /// <typeparam name="TElement">The element type.</typeparam>
    /// <param name="sequence">The sequence.</param>
    /// <returns>A task that completes once the sequence is exhausted.</returns>
    /// <remarks>
    /// ENUMERATION IS WHAT OPENS THE CALL. An iterator method returns without doing anything until it is
    /// first moved, so a census that merely obtained the sequence would record no upstream call and pass for
    /// the wrong reason. The recording upstream yields nothing, so this completes on the first move.
    /// </remarks>
    private static async Task DrainAsync<TElement>(IAsyncEnumerable<TElement> sequence)
    {
        await foreach (TElement _ in sequence.ConfigureAwait(false))
        {
            // Deliberately empty: the recording upstream yields no elements, and the assertion is that the
            // call was opened rather than that anything arrived.
        }
    }
}
