// =====================================================================================================
//  F-31 - THE INGRESS ENFORCES WHAT THE CONTRACT DECLARES, AND CLASSIFIES ITS OWN REFUSALS
// =====================================================================================================
//
//  WHY THIS FILE EXISTS, STATED AS THE DEFECTS IT WOULD HAVE CAUGHT. Three separate faults were found at
//  runtime against a Gateway whose 906 assertions were entirely green, and all three shared one shape: a
//  property the published contract declares, which nothing in the service applied and nothing in the suite
//  asked about. Each test below is anchored to one.
//
//    1. AN OMITTED REQUIRED PARAMETER ANSWERED 500. The projection bound sessionId as a NON-nullable
//       string, so minimal-API binding raised BadHttpRequestException before the handler was entered, and
//       the fault handler discarded the 400 that exception carries and answered 500 with the catalogue's
//       UNKNOWN code. A caller that forgot a query parameter was told this service had failed. The suite
//       never noticed because every existing test supplied the parameter - the omission was untested, so
//       the binding shape that could not express it was invisible.
//
//    2. THE DECLARED LENGTH BOUND WAS DECLARATIVE ONLY. components.parameters.SessionIdQuery publishes
//       minLength 1 and maxLength 128. Nothing enforced either, so a 129-character identifier was
//       forwarded and the upstream answered 404 - reporting a GONE session for a request that had violated
//       a published bound and never named a session at all. 128 and 129 were indistinguishable.
//
//    3. AN IDLE SUBSCRIPTION STREAM FAILED AT THE OUTBOUND DEADLINE. The event stream is a SUBSCRIPTION:
//       quiet is its normal state, not a fault. Collecting it with no bound meant a quiet subscription was
//       held until the 10 s per-attempt outbound timeout and then surfaced as a 504, so "nothing has
//       happened yet" was indistinguishable from "the upstream is broken".
//
//  THE ASYMMETRY WITH THE RETRIEVAL IS DELIBERATE AND IS ASSERTED. A retrieval terminates itself with a
//  final-marked chunk, so a window that cut it short would produce a SHORT ARRAY THAT CLOSES CLEANLY -
//  indistinguishable from a complete one, which is the one failure mode F-15 exists to prevent. Only the
//  subscription opts in. The test at the end of section 3 pins that difference.
//
//  WHERE DEFECT 1 IS PROVEN, AND WHY IT TAKES TWO LAYERS. Under live Kestrel the binding failure reaches
//  the fault handler, which is where the 500 came from. A WebApplicationFactory host short-circuits the
//  same failure earlier, so section 1 pins the property that holds in BOTH: absent and empty are answered
//  identically, and each detail names the parameter - pre-fix the absent case carried NO detail at all.
//  Section 4 then pins the classification itself directly, which is the arm that produced the 500.
//
//  WHAT IS NOT INVENTED HERE. No status outside gateway.v1.yaml's closed sanctioned set is produced or
//  expected - notably not 422, which GatewayContractTests asserts appears nowhere in the document.
// =====================================================================================================

using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Gateway.Endpoints;
using Xunit;

using RetCode = PowerFramework.Shared.Kernel.RetCode;
using WireRetCode = PowerFramework.Contracts.Common.V1.RetCode.Types.Value;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// Conformance for the session-identifier binding, the declared length bound, the bounded collection
/// window on the subscription stream, and the reclassification of a framework binding failure.
/// </summary>
/// <remarks>
/// EVERY HOST HERE IS LOCALLY OWNED rather than a shared class fixture, because each substitutes its own
/// upstream double. Sharing one and mutating it would make these tests order-dependent.
/// </remarks>
public sealed class DataServicesProxySessionBindingTests
{
    /// <summary>The projected event-gate read: the session-scoped shape under test.</summary>
    /// <remarks>
    /// CHOSEN BECAUSE ITS ONLY ARGUMENT IS THE IDENTIFIER. An operation with a body could pass its binding
    /// tests for reasons unrelated to the query parameter; this one cannot.
    /// </remarks>
    private const string EventGateRoute = "/v1/datawindow/event-gate";

    /// <summary>The problem-body member carrying the legacy return code.</summary>
    private const string RetCodeMember = "retCode";

    /// <summary>The problem-body member naming the upstream, present only on an upstream's answer.</summary>
    private const string UpstreamMember = "upstream";

    // ==============================================================================================
    //  SECTION 1 - AN OMITTED PARAMETER AND AN EMPTY ONE ARE TWO SPELLINGS OF ONE MISTAKE
    // ==============================================================================================

    /// <summary>
    /// An omitted session identifier draws the same client refusal an empty one does, and neither reaches
    /// the upstream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO CASES ARE COMPARED TO EACH OTHER, not only to a literal. That is what makes the assertion
    /// about INDISTINGUISHABILITY rather than about two statuses that merely happen to agree today: the
    /// status, the return code and the absence of an upstream marker are all asserted equal across the
    /// pair, so a change that reclassified one without the other fails here.
    /// </para>
    /// <para>
    /// THE UPSTREAM MARKER IS THE LOAD-BEARING NEGATIVE. Its absence proves the refusal was formed at this
    /// ingress and not relayed - which is the difference between "this gateway declined your request" and
    /// "DataServices declined it", and the difference the 500 defect erased.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnOmittedSessionIdentifierIsRefusedExactlyAsAnEmptyOneIs()
    {
        ScriptedDataWindowServiceClient upstream = new(eventGate: (_, _) => Task.FromResult(SucceedingGate()));

        await using GatewayTestHostFixture host = NewHost(upstream);
        using HttpClient client = host.CreateAuthenticatedClient();

        // NO QUERY STRING AT ALL: the shape that used to fail inside framework binding, before the handler.
        (HttpStatusCode Status, long RetCode, bool HasUpstream, string Detail) absent =
            await ReadRefusalAsync(client, EventGateRoute);

        // THE PARAMETER PRESENT AND CARRYING NOTHING: the shape that always reached the handler.
        (HttpStatusCode Status, long RetCode, bool HasUpstream, string Detail) empty =
            await ReadRefusalAsync(client, $"{EventGateRoute}?sessionId=");

        Assert.Equal(HttpStatusCode.BadRequest, absent.Status);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, absent.RetCode);
        Assert.False(absent.HasUpstream);

        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, empty.RetCode);
        Assert.False(empty.HasUpstream);

        // COMPARED TO EACH OTHER, which is the property the defect violated.
        Assert.Equal(empty.Status, absent.Status);
        Assert.Equal(empty.RetCode, absent.RetCode);
        Assert.Equal(empty.HasUpstream, absent.HasUpstream);

        // THE DETAILS MAY DIFFER - each names its own condition so a caller can correct the right thing -
        // but both must name the parameter, and neither may be the framework's own binding message, which
        // reads as an internal failure.
        Assert.Contains("sessionId", absent.Detail, StringComparison.Ordinal);
        Assert.Contains("sessionId", empty.Detail, StringComparison.Ordinal);

        // NEITHER REFUSAL WAS FORWARDED. A refusal that still consumed an upstream call would leave the
        // bound enforced in name only.
        Assert.Empty(upstream.EventGateSessionIds);
    }

    // ==============================================================================================
    //  SECTION 2 - THE DECLARED LENGTH BOUND, AT ITS EXACT BOUNDARY
    // ==============================================================================================

    /// <summary>
    /// An identifier of exactly the declared maximum is forwarded; one character more is refused here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH SIDES OF THE BOUNDARY IN ONE TEST, because either alone proves nothing about where the edge
    /// sits. A test that only refused 129 would still pass if the bound were 12; a test that only accepted
    /// 128 would still pass if nothing were enforced at all. The pair pins the boundary to the declared
    /// value - <c>maxLength: 128</c> - and the accepted case simultaneously proves the new check did not
    /// start refusing legitimate identifiers.
    /// </para>
    /// <para>
    /// THE ACCEPTED CASE IS ASSERTED TO ARRIVE UNCHANGED. A bound applied by truncating would also answer
    /// 200, and would silently address the wrong session.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDeclaredLengthBoundIsEnforcedAtItsExactDeclaredBoundary()
    {
        string atBound = new('s', 128);
        string overBound = new('s', 129);

        ScriptedDataWindowServiceClient upstream = new(eventGate: (_, _) => Task.FromResult(SucceedingGate()));

        await using GatewayTestHostFixture host = NewHost(upstream);
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage accepted = await SendAsync(client, $"{EventGateRoute}?sessionId={atBound}");

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // FORWARDED VERBATIM, which rules out a bound implemented by truncation.
        Assert.Equal(atBound, Assert.Single(upstream.EventGateSessionIds));

        (HttpStatusCode Status, long RetCode, bool HasUpstream, string Detail) refused =
            await ReadRefusalAsync(client, $"{EventGateRoute}?sessionId={overBound}");

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, refused.RetCode);
        Assert.False(refused.HasUpstream);

        // THE LIMIT IS REPORTED AND THE VALUE IS NOT. A caller needs the bound to correct its request; it
        // already has its own identifier, so echoing 129 characters of caller content back would add
        // nothing and would put caller data in a problem body.
        Assert.Contains("128", refused.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(overBound, refused.Detail, StringComparison.Ordinal);

        // THE REFUSAL DID NOT REACH THE UPSTREAM: still the one call the accepted case made.
        Assert.Single(upstream.EventGateSessionIds);
    }

    /// <summary>
    /// A single space satisfies the declared minimum and is forwarded exactly as supplied.
    /// </summary>
    /// <remarks>
    /// THE DECLARED CONSTRAINT IS A LENGTH AND NOT A CHARACTER CLASS. <c>minLength: 1</c> is satisfied by
    /// one space, so refusing it would enforce a rule the contract does not publish. Trimming it would be
    /// worse still: it would REPAIR a caller's value into a different one, and the caller would then be
    /// told about a session it never asked for. This test exists to keep a future tightening honest - if a
    /// character class is ever wanted, it belongs in the contract first.
    /// </remarks>
    [Fact]
    public async Task WhitespaceSatisfiesTheDeclaredMinimumAndIsNeverTrimmed()
    {
        const string singleSpace = " ";

        ScriptedDataWindowServiceClient upstream = new(eventGate: (_, _) => Task.FromResult(SucceedingGate()));

        await using GatewayTestHostFixture host = NewHost(upstream);
        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await SendAsync(client, $"{EventGateRoute}?sessionId=%20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(singleSpace, Assert.Single(upstream.EventGateSessionIds));
    }

    // ==============================================================================================
    //  SECTION 3 - THE BOUNDED COLLECTION WINDOW ON A SUBSCRIPTION
    // ==============================================================================================

    /// <summary>
    /// A subscription that has produced nothing answers an empty success inside its window rather than
    /// failing at the outbound deadline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TIMING ASSERTION IS THE POINT AND IT IS ONE-SIDED. It asserts only that the answer arrived well
    /// inside the 10 s per-attempt outbound timeout - the deadline the defect surfaced as a 504 - which is
    /// a CORRECTNESS statement about which of two code paths ran, not a latency claim (AAP 0.8.5 forbids
    /// one, since the repository publishes no budget). A generous ceiling is used deliberately so the test
    /// cannot fail for being run on a loaded machine; the pre-fix path exceeded it by a factor of five.
    /// </para>
    /// <para>
    /// THE BODY MUST CLOSE. An empty array is a complete, well-formed answer meaning "nothing yet"; a
    /// truncated body would be a malformed one. This is the only place a cleanly closed short array is
    /// correct, and it is correct because zero elements is not a truncation of anything.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnIdleSubscriptionAnswersAnEmptySuccessInsideItsWindow()
    {
        TimeSpan window = TimeSpan.FromMilliseconds(250);

        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        long startedAt = Stopwatch.GetTimestamp();

        // A SUBSCRIPTION THAT NEVER SPEAKS, which is a quiet subscription's normal state and not a fault.
        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<EventChainResponse>
            .PrefetchAsync(
                NeverProduces<EventChainResponse>(TestContext.Current.CancellationToken),
                maximumElements: 64,
                window,
                TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("[]", Encoding.UTF8.GetString(body.ToArray()));

        // WELL INSIDE THE DEADLINE THE DEFECT WAITED FOR. Not a latency budget - a proof of which path ran.
        Assert.True(
            elapsed < GatewayOptions.OutboundCallOptions.MinimumRequestTimeout,
            $"An idle subscription took {elapsed}, which is not inside the "
                + $"{GatewayOptions.OutboundCallOptions.MinimumRequestTimeout} outbound deadline the "
                + "bounded window exists to answer ahead of.");
    }

    /// <summary>
    /// A subscription that is producing is never truncated by its window.
    /// </summary>
    /// <remarks>
    /// THE OTHER HALF OF THE WINDOW'S CONTRACT. A window that closed a stream that had more to say would
    /// turn the defect around: instead of a false failure it would produce a false success, silently short.
    /// The window is generous relative to the producer here precisely so that any truncation observed is
    /// the projection's and not the schedule's.
    /// </remarks>
    [Fact]
    public async Task AProducingSubscriptionIsNotTruncatedByItsWindow()
    {
        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<EventChainResponse>
            .PrefetchAsync(
                PromptEvents(4, TestContext.Current.CancellationToken),
                maximumElements: 64,
                TimeSpan.FromSeconds(9),
                TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);

        string written = Encoding.UTF8.GetString(body.ToArray());

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(written);

        Assert.Equal(4, document.RootElement.GetArrayLength());
    }

    /// <summary>
    /// The retrieval projection does not opt into the window, so a retrieval can never be cut short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ASYMMETRY, ASSERTED RATHER THAN DESCRIBED IN A COMMENT. A retrieval terminates itself with a
    /// final-marked chunk, so a window that ended one early would produce a short array THAT CLOSES
    /// CLEANLY - indistinguishable from a complete result, which is the single failure mode F-15 exists to
    /// prevent. Passing no window is what keeps that impossible, and this test fails if a future change
    /// applies the subscription's window to data retrieval.
    /// </para>
    /// <para>
    /// Driven through the same prefetch with the window omitted, over a producer that would certainly have
    /// been cut short had one applied: it stalls far longer than the subscription window before completing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ARetrievalWithNoWindowIsNeverCutShortByOne()
    {
        DefaultHttpContext context = NewContext();

        using MemoryStream body = new();
        context.Response.Body = body;

        IResult result = await DataServicesProxyEndpoints.StreamedSequenceResult<RetrieveChunk>
            .PrefetchAsync(
                StalledThenComplete(TestContext.Current.CancellationToken),
                maximumElements: 64,
                collectionWindow: null,
                TestContext.Current.CancellationToken);

        await result.ExecuteAsync(context);

        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(body.ToArray()));

        // BOTH CHUNKS, INCLUDING THE FINAL MARKER. A window would have delivered only the first.
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.True(document.RootElement[1].GetProperty("final").GetBoolean());
    }

    // ==============================================================================================
    //  SECTION 4 - A FRAMEWORK BINDING FAILURE IS THE CALLER'S ERROR, AND CARRIES ITS OWN STATUS
    // ==============================================================================================

    /// <summary>
    /// The status a framework binding failure carries is honoured, and a structural fault overrides it.
    /// </summary>
    /// <param name="carriedStatus">The status the framework attached to the request it refused.</param>
    /// <param name="structuralFault">Whether the fault is a decoded assertion failure.</param>
    /// <param name="expected">The status the handler must answer with.</param>
    /// <remarks>
    /// <para>
    /// DRIVEN DIRECTLY RATHER THAN THROUGH A HOST, because the arms that matter cannot all be produced by a
    /// real request: a carried <c>5xx</c> and a carried value below the client range do not arise from
    /// minimal-API binding at all, and the structural-fault arm requires a decoded assertion payload. The
    /// omitted-parameter case IS exercised end to end in section 1; this pins the classification itself.
    /// </para>
    /// <para>
    /// THE TWO NARROWING CONDITIONS ARE BOTH ASSERTED, and each is load bearing. A structural fault is this
    /// service's own and is terminal (DECISION 5), so nothing an exception carries may reclassify it - the
    /// last row proves a carried 400 does not. And only a client-range status is honoured, so a carried
    /// <c>500</c> or <c>399</c> falls back rather than being trusted as a classification.
    /// </para>
    /// </remarks>
    [Theory]

    // HONOURED: the client range, at both ends and in the middle.
    [InlineData(StatusCodes.Status400BadRequest, false, StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status413PayloadTooLarge, false, StatusCodes.Status413PayloadTooLarge)]
    [InlineData(499, false, 499)]

    // NOT HONOURED: outside the client range in either direction, so the fallback stands.
    [InlineData(StatusCodes.Status500InternalServerError, false, StatusCodes.Status500InternalServerError)]
    [InlineData(399, false, StatusCodes.Status500InternalServerError)]

    // NOT HONOURED: a structural fault is terminal and is never reclassified by a carried status.
    [InlineData(StatusCodes.Status400BadRequest, true, StatusCodes.Status500InternalServerError)]
    public void ACarriedClientStatusIsHonouredUnlessTheFaultIsStructural(
        int carriedStatus,
        bool structuralFault,
        int expected)
    {
        BadHttpRequestException refused = new("A request this service could not accept.", carriedStatus);

        Assert.Equal(expected, SystemErrorHandler.ResolveResponseStatus(refused, structuralFault));
    }

    /// <summary>
    /// A fault carrying no status at all is this service's own.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT MUST STAY 500, and it is asserted separately from the theory above because it is a
    /// different claim: the theory narrows WHICH carried statuses are honoured, and this fixes what happens
    /// when there is nothing to honour. An ordinary exception escaping the pipeline is a fault of this
    /// service, and answering anything in the client range for one would blame the caller for it.
    /// </remarks>
    [Fact]
    public void AFaultCarryingNoStatusIsThisServicesOwn()
    {
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            SystemErrorHandler.ResolveResponseStatus(new InvalidOperationException("Nothing carried."), false));

        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            SystemErrorHandler.ResolveResponseStatus(new InvalidOperationException("Nothing carried."), true));
    }

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>A plain successful event-gate answer, which is not what any test here is about.</summary>
    /// <returns>The response.</returns>
    /// <remarks>
    /// SUCCEEDING ON PURPOSE. Every upstream answer in this file is uninteresting, because what is under
    /// test is whether the request reached an upstream at all and with what identifier. A scripted failure
    /// would confuse a refusal formed here with one relayed from there.
    /// </remarks>
    private static GetEventGateResponse SucceedingGate()
        => new() { RetCode = WireRetCode.Ok, Gate = new EventGate { Mask = 0 } };

    /// <summary>Builds a locally owned host with the supplied upstream substituted.</summary>
    /// <param name="upstream">The scripted DataWindow upstream.</param>
    /// <returns>The host, which the caller disposes.</returns>
    /// <remarks>
    /// PRODUCTION ENVIRONMENT, so no development-only leniency is in play and the projection under test is
    /// the one a deployment runs. The substitution goes through the fixture's own extension point, applied
    /// last, so token validation and the unreachable-network handler stay exactly as the fixture set them.
    /// </remarks>
    private static GatewayTestHostFixture NewHost(ScriptedDataWindowServiceClient upstream)
    {
        GatewayTestHostFixture host = GatewayTestHostFixture.ForEnvironment(Environments.Production);

        DataServicesClient substituted = new(
            upstream,
            new InertColumnExpressionServiceClient(),
            new StubServiceTokenProvider(),
            NullLogger<DataServicesClient>.Instance);

        host.AdditionalServiceConfiguration.Add(services =>
        {
            services.RemoveAll<DataServicesClient>();
            services.AddSingleton(substituted);
        });

        return host;
    }

    /// <summary>Sends one GET to a relative route.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="path">The relative route, query string included when one is intended.</param>
    /// <returns>The response, which the caller disposes.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Sends one GET and decomposes the problem body it answers with.</summary>
    /// <param name="client">The client to send with.</param>
    /// <param name="path">The relative route, query string included when one is intended.</param>
    /// <returns>The status, the carried return code, whether an upstream was named, and the detail.</returns>
    /// <remarks>
    /// DECOMPOSED RATHER THAN RETURNING THE DOCUMENT, so the two refusals in section 1 can be compared to
    /// each other member by member after both responses have been disposed.
    /// </remarks>
    private static async Task<(HttpStatusCode Status, long RetCode, bool HasUpstream, string Detail)>
        ReadRefusalAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await SendAsync(client, path);

        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // ASSERTED BEFORE THE BODY IS READ, so that a request which was FORWARDED rather than refused fails
        // here with a legible message. A success body carries retCode as the protobuf enum's NAME, so
        // reading it as a number would otherwise fail with a JSON type error that says nothing about the
        // actual defect - which is that the request reached an upstream at all.
        Assert.True(
            (int)response.StatusCode >= StatusCodes.Status400BadRequest
                && (int)response.StatusCode < StatusCodes.Status500InternalServerError,
            $"'{path}' answered {(int)response.StatusCode} rather than a client refusal, so it was not "
                + $"refused at this ingress. Body: {text}");

        using JsonDocument document = JsonDocument.Parse(text);

        return (
            response.StatusCode,
            document.RootElement.GetProperty(RetCodeMember).GetInt64(),
            document.RootElement.TryGetProperty(UpstreamMember, out _),
            document.RootElement.TryGetProperty("detail", out JsonElement detail)
                ? detail.GetString() ?? string.Empty
                : string.Empty);
    }

    /// <summary>Builds a context with the services the projection resolves for its diagnostics.</summary>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewContext()
    {
        ServiceCollection services = new();
        _ = services.AddLogging();

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/v1/datawindow/sessions/probe/events";

        return context;
    }

    /// <summary>A stream that opens and then never produces, as a quiet subscription does.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="cancellationToken">Observed, as a real stream observes it.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// <para>
    /// IT WAITS ON THE TOKEN RATHER THAN SLEEPING, which is what makes the test prove something. The window
    /// cancels a token linked to this one, so a correct projection ends this wait; a projection that
    /// ignored its window would wait here until the test's own token cancelled. The delay is unbounded
    /// precisely so nothing but a cancellation can end it.
    /// </para>
    /// <para>
    /// The <c>yield</c> after the wait is unreachable and exists only to make this an iterator.
    /// </para>
    /// </remarks>
    private static async IAsyncEnumerable<T> NeverProduces<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        yield break;
    }

    /// <summary>A subscription that produces promptly.</summary>
    /// <param name="count">How many notices it delivers.</param>
    /// <param name="cancellationToken">Observed on every read.</param>
    /// <returns>The sequence.</returns>
    private static async IAsyncEnumerable<EventChainResponse> PromptEvents(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Yield();

            // NAMED BY ITS SESSION, which is the member every notice on this stream carries. The payload
            // oneof is deliberately left unset: what is counted here is ELEMENTS DELIVERED, and giving
            // each a payload would add a shape to assert that has nothing to do with the window.
            yield return new EventChainResponse { SessionId = $"probe-{index + 1}" };
        }
    }

    /// <summary>
    /// A retrieval that stalls longer than the subscription window would allow and then completes.
    /// </summary>
    /// <param name="cancellationToken">Observed on every read.</param>
    /// <returns>The sequence.</returns>
    /// <remarks>
    /// THE STALL IS BETWEEN THE FIRST AND SECOND CHUNK ON PURPOSE. That is exactly where a window would cut
    /// a retrieval to one element and close the array cleanly - the indistinguishable-short-array failure -
    /// so a stall placed anywhere else would not test the thing that matters.
    /// </remarks>
    private static async IAsyncEnumerable<RetrieveChunk> StalledThenComplete(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new RetrieveChunk { ChunkIndex = 1, Final = false };

        await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);

        yield return new RetrieveChunk { ChunkIndex = 2, Final = true };
    }
}
