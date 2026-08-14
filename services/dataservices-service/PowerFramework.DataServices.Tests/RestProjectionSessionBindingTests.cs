// ==================================================================================================
//  RestProjectionSessionBindingTests - AN OMITTED PARAMETER IS A CLIENT ERROR, NEVER A SERVER FAULT
//  ------------------------------------------------------------------------------------------------
//  WHY THIS FILE EXISTS, STATED AS THE DEFECT IT WOULD HAVE CAUGHT
//
//  A runtime probe of the deployed stack drove 580 requests across the four services and produced
//  exactly ONE 5xx: `GET /v1/datawindow/event-gate` with NO query string answered
//
//      500  {"status":500,"retCode":-27,...}
//
//  and wrote `Microsoft.AspNetCore.Http.BadHttpRequestException: Required parameter "string sessionId"
//  was not provided from query string` to the operator channel - the only unhandled-exception cause in
//  27 MB of captured logs. The cause was the SHAPE OF THE HANDLER SIGNATURE, not the handler's body:
//  `MapSessionScoped` bound the identifier as a NON-nullable `string`, so minimal-API parameter binding
//  faulted BEFORE the route was entered and nothing in the projection had run yet to answer. The same
//  request with the parameter PRESENT AND EMPTY answered a clean 400 with E_INVALID_ARGUMENT, so one
//  client mistake had two answers: a client error for one spelling and a server fault for the other.
//
//  WHAT THIS SUITE PINS
//
//    1. ABSENT AND EMPTY ARE ONE ANSWER. Compared to EACH OTHER rather than each to a literal, so a
//       change that reclassified one without the other fails here. Both name the parameter; neither is
//       the framework's own binding message, which reads as an internal failure.
//
//    2. ONLY THOSE TWO ARE REFUSED HERE. A whitespace identifier is FORWARDED and answered by the
//       session registry's own in-band E_INVALID_ARGUMENT with its own prose. That asymmetry is the
//       proof that the acceptance gate refuses two shapes rather than pre-empting the registry - and it
//       is what keeps the 404 for an unresolvable identifier truthful, since this service IS the
//       session owner and an identifier it cannot resolve genuinely names no session.
//
//    3. NO FRAMEWORK FAULT REACHES THE OPERATOR CHANNEL. The record is the projection's own binding
//       refusal, at warning, once - and no record anywhere mentions BadHttpRequestException.
//
//    4. THE RULE IS THE SHARED DECLARATION'S, NOT ONE ROUTE'S. Every query-bound session-identifier
//       operation the GENERATED DOCUMENT declares is driven with its identifier omitted, so a fourth
//       session-scoped operation added without a path template is covered by this suite on the day it
//       is added rather than on the day it is probed.
//
//    5. THE DOCUMENT STILL SAYS THE PARAMETER IS REQUIRED, AND NOW DECLARES THE 400 THAT ANSWERS ITS
//       VIOLATION. Nullable binding is how the route becomes able to REFUSE the omission; the generator
//       would otherwise infer optionality from the signature. And the three session-scoped operations
//       used to suppress their 400 on the reasoning that an operation with no body has nothing to
//       reject - which overlooked the parameter, and published a required parameter with no declared
//       response for violating it.
//
//  ORACLE
//  ------------------------------------------------------------------------------------------------
//  None. The legacy is an in-process library with no ingress, no route table and no parameter binding,
//  so it has no behaviour here to preserve. The boundary created both the surface and the defect
//  (AAP 0.1.4), which is why the reference implementation is Gateway's own fixed twin -
//  `DataServicesProxySessionBindingTests` - rather than a PowerBuilder locator.
// ==================================================================================================

using System.Net;
using System.Net.Mime;
using System.Text.Json;

using PowerFramework.DataServices.Domain;

using Xunit;

using KernelRetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// Conformance for the session-identifier binding on the projected session-scoped operations, and for
/// what the generated document says about it.
/// </summary>
/// <remarks>
/// EVERY CASE OWNS ITS HOST rather than sharing a class fixture, because two of them raise the logging
/// floor or read the generated document and a shared, mutated host would make them order-dependent.
/// </remarks>
public sealed class RestProjectionSessionBindingTests
{
    /// <summary>The projected event-gate read: the only session-scoped operation bound from the query.</summary>
    /// <remarks>
    /// CHOSEN BECAUSE ITS ONLY ARGUMENT IS THE IDENTIFIER. An operation carrying a body could pass a
    /// binding assertion for reasons that have nothing to do with the parameter under test.
    /// </remarks>
    private const string EventGateRoute = "/v1/datawindow/event-gate";

    /// <summary>Where a validation session is opened, so a positive arm has a real identifier.</summary>
    private const string SessionsRoute = "/v1/datawindow/sessions";

    /// <summary>An identifier of the right shape that this service never issued.</summary>
    private const string UnknownSessionId = "0123456789abcdef0123456789abcdef";

    /// <summary>The parameter's published name, which every refusal detail must name.</summary>
    private const string SessionIdParameterName = "sessionId";

    /// <summary>The framework's own binding message, which must never reach a caller or an operator.</summary>
    private const string FrameworkBindingFaultType = "BadHttpRequestException";

    // ==============================================================================================
    //  SECTION 1 - ONE CLIENT MISTAKE, ONE ANSWER
    // ==============================================================================================

    /// <summary>
    /// An omitted session identifier draws the same refusal an empty one does, and both name the
    /// parameter.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE TWO CASES ARE COMPARED TO EACH OTHER, which is what makes this an assertion about
    /// INDISTINGUISHABILITY rather than about two statuses that happen to agree today. Status, return
    /// code, media type and the absence of an upstream marker are all asserted equal across the pair.
    /// </para>
    /// <para>
    /// THE UPSTREAM MARKER IS THE LOAD-BEARING NEGATIVE. The problem shape carries one only when the
    /// failure demonstrably came off this service's single outbound edge; a refusal formed in the route
    /// must not name anything else as its cause, and the pre-fix 500 named nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnOmittedSessionIdentifierIsRefusedExactlyAsAnEmptyOneIsAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        // NO QUERY STRING AT ALL: the shape that used to fault inside framework binding, before the route.
        Refusal absent = await ReadRefusalAsync(client, EventGateRoute);

        // THE PARAMETER PRESENT AND CARRYING NOTHING: the shape that always reached the route.
        Refusal empty = await ReadRefusalAsync(client, $"{EventGateRoute}?{SessionIdParameterName}=");

        Assert.Equal(HttpStatusCode.BadRequest, absent.Status);
        Assert.Equal(KernelRetCode.E_INVALID_ARGUMENT, absent.RetCode);
        Assert.False(absent.HasUpstream);

        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Equal(KernelRetCode.E_INVALID_ARGUMENT, empty.RetCode);
        Assert.False(empty.HasUpstream);

        // COMPARED TO EACH OTHER, which is the property the defect violated.
        Assert.Equal(empty.Status, absent.Status);
        Assert.Equal(empty.RetCode, absent.RetCode);
        Assert.Equal(empty.HasUpstream, absent.HasUpstream);
        Assert.Equal(empty.MediaType, absent.MediaType);

        // A PROBLEM BODY, NOT A BARE STATUS. Pre-fix the deployed answer carried no detail whatsoever.
        Assert.Equal(MediaTypeNames.Application.ProblemJson, absent.MediaType);

        // THE DETAILS MAY DIFFER - each names its own condition so a caller can correct the right thing -
        // but both must name the parameter, and neither may be the framework's own binding message.
        Assert.Contains(SessionIdParameterName, absent.Detail, StringComparison.Ordinal);
        Assert.Contains(SessionIdParameterName, empty.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(FrameworkBindingFaultType, absent.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(FrameworkBindingFaultType, empty.Detail, StringComparison.Ordinal);

        // THE CORRELATION IS PUBLISHED ON BOTH, because the detail is deliberately fixed prose and the
        // trace identifier is the only thing that leads an operator to this occurrence.
        Assert.False(string.IsNullOrWhiteSpace(absent.TraceId));
        Assert.False(string.IsNullOrWhiteSpace(empty.TraceId));
    }

    /// <summary>
    /// The route refuses absence and emptiness itself, and forwards everything else to the registry.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE THIRD SHAPE IS WHAT MAKES THE FIRST TWO MEANINGFUL. A gate that refused every questionable
    /// identifier would satisfy the test above and would have pre-empted the session registry - moving a
    /// rule that lives in one place into two, and putting this projection in the position of deciding
    /// what its own service already decides. A single space is therefore asserted to be FORWARDED: it
    /// draws the same 400 and the same return code, and it draws the REGISTRY'S prose rather than the
    /// route's, which is only possible if it was actually forwarded.
    /// </para>
    /// <para>
    /// NO LENGTH BOUND IS ASSERTED HERE BECAUSE NONE IS PUBLISHED. Gateway enforces
    /// <c>minLength</c>/<c>maxLength</c> because <c>gateway.v1.yaml</c> declares them and because a
    /// forwarded over-long identifier would make an upstream answer for a violation of Gateway's own
    /// published bound. This document declares no length, and this service owns the sessions, so an
    /// identifier it cannot resolve is a truthful 404 rather than a misattribution.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheRouteRefusesOnlyAbsenceAndEmptinessAndForwardsEverythingElseAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        Refusal absent = await ReadRefusalAsync(client, EventGateRoute);
        Refusal empty = await ReadRefusalAsync(client, $"{EventGateRoute}?{SessionIdParameterName}=");

        // A SINGLE SPACE, PERCENT-ENCODED: present, non-empty, and blank - so the route forwards it and
        // ValidationSessionRegistry.Resolve judges it, exactly as it judged a blank identifier before
        // this projection had an acceptance gate at all.
        Refusal blank = await ReadRefusalAsync(client, $"{EventGateRoute}?{SessionIdParameterName}=%20");

        // The same status and the same code: a caller cannot tell which layer refused, and should not
        // need to.
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);
        Assert.Equal(KernelRetCode.E_INVALID_ARGUMENT, blank.RetCode);
        Assert.False(blank.HasUpstream);

        // DIFFERENT PROSE, WHICH IS THE EVIDENCE OF THE ROUTE IT TOOK. The route's own details name the
        // parameter; the forwarded one is the projection's in-band argument-rejection prose, which does
        // not - so a gate that had started refusing whitespace would fail here.
        Assert.NotEqual(absent.Detail, blank.Detail);
        Assert.NotEqual(empty.Detail, blank.Detail);
        Assert.DoesNotContain(SessionIdParameterName, blank.Detail, StringComparison.Ordinal);

        // AND THE VALUE IS NOWHERE IN ANY OF THE THREE BODIES. A caller already holds what it sent, and
        // a problem body is read by whoever holds the response rather than only by that caller.
        foreach (Refusal refusal in (Refusal[])[absent, empty, blank])
        {
            Assert.DoesNotContain("\"sessionId\":", refusal.Body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The omission is recorded as this projection's own binding refusal, and no framework fault is
    /// recorded at all.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE OPERATOR RECORD IS HALF OF WHAT THE DEFECT PRODUCED. The 500 was one symptom; the other was
    /// an unhandled-exception record naming <c>BadHttpRequestException</c> and quoting the framework's
    /// binding message - which tells an operator this service failed. Asserting the absence of that
    /// string across EVERY record, at the most verbose level the factory will emit, is what pins the
    /// second symptom.
    /// </para>
    /// <para>
    /// THE POSITIVE HALF IS ASSERTED TOO: the refusal is recorded once, at warning, through the same
    /// record every body-binding refusal uses - so the occurrence is diagnosable rather than silent.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheOmissionIsRecordedAsABindingRefusalAndNoFrameworkFaultIsRecordedAsync()
    {
        await using DataServicesTestHostFactory host = new();

        ProjectionLogRecorder recorder = RestProjection.RecordOperatorLog(host);

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.GetAsync(
            RestProjection.Relative(EventGateRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string[] refusals =
        [
            .. recorder.Records.Where(static record =>
                record.Contains("rejected a request body or a required parameter", StringComparison.Ordinal)),
        ];

        // ONE RECORD, AT WARNING, FROM THE PROJECTION'S OWN CATEGORY. An operator filtering on this
        // category sees the refusal and its correlation, and nothing that suggests a fault.
        string refusal = Assert.Single(refusals);

        Assert.Contains("[Warning]", refusal, StringComparison.Ordinal);
        Assert.Contains(RestProjection.ProjectionLoggerCategory, refusal, StringComparison.Ordinal);

        // NOT ONE RECORD MENTIONS THE FRAMEWORK'S BINDING FAULT, and none carries the parameter value.
        Assert.DoesNotContain(
            recorder.Records,
            record => record.Contains(FrameworkBindingFaultType, StringComparison.Ordinal));

        Assert.DoesNotContain(
            recorder.Records,
            record => record.Contains("was not provided from query string", StringComparison.Ordinal));
    }

    // ==============================================================================================
    //  SECTION 2 - THE OTHER OUTCOMES STILL HAPPEN, WHICH IS WHAT KEEPS THE REFUSAL NARROW
    // ==============================================================================================

    /// <summary>
    /// The session-scoped read still answers 200 for a live session, 404 for an identifier that names
    /// none, 401 without a credential, and 400 only for the omission.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// THE WHOLE BOUNDARY MATRIX IN ONE ROW, taken in the order the runtime probe took it. A gate placed
    /// too early - or one that answered 400 for anything it could not resolve - would pass every
    /// assertion in section 1 and collapse the 404 into a 400 here, which would tell a caller its
    /// request was malformed when in fact its session had expired. The 401 row is included because the
    /// refusal must not be reachable ahead of authentication: an anonymous caller learns nothing about
    /// which parameters an operation takes.
    /// </remarks>
    [Fact]
    public async Task TheSessionScopedReadStillAnswersEveryOtherDeclaredOutcomeAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();
        using HttpClient anonymous = host.CreateAnonymousClient();

        string sessionId;

        using (HttpResponseMessage opened = await RestProjection.PostAsync(
            client,
            SessionsRoute,
            new { datawindowHandle = DataWindowCatalogue.SqliteFixtureName }))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(opened);

            sessionId = body.RootElement.GetProperty(SessionIdParameterName).GetString() ?? string.Empty;
        }

        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // 200 - a live session this caller owns.
        using (HttpResponseMessage live = await client.GetAsync(
            RestProjection.Relative($"{EventGateRoute}?{SessionIdParameterName}={sessionId}"),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        // 404 - present, non-empty, and naming no session. NOT 400: the request was well formed.
        using (HttpResponseMessage unknown = await client.GetAsync(
            RestProjection.Relative($"{EventGateRoute}?{SessionIdParameterName}={UnknownSessionId}"),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // 401 - no credential at all, and with the parameter omitted, so the refusal cannot be reached
        // ahead of authentication.
        using (HttpResponseMessage unauthenticated = await anonymous.GetAsync(
            RestProjection.Relative(EventGateRoute),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        }

        // 400 - the omission, and only the omission.
        using (HttpResponseMessage omitted = await client.GetAsync(
            RestProjection.Relative(EventGateRoute),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        }
    }

    /// <summary>
    /// Every query-bound session-identifier operation the generated document declares refuses its own
    /// omission, and none of them faults.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// THE OPERATION LIST IS READ FROM THE DOCUMENT RATHER THAN WRITTEN HERE, and that is the whole
    /// point of the case. The defect was fixed in the SHARED declaration every session-scoped operation
    /// goes through, so the assertion has to be about the class of operations and not about the one
    /// route the probe happened to hit. A fourth session-scoped operation declared without a path
    /// template is covered here the moment it appears, which is the difference between a fix and a
    /// patch.
    /// </para>
    /// <para>
    /// PATH-BOUND IDENTIFIERS ARE EXCLUDED BY CONSTRUCTION, because omitting a path segment produces a
    /// different address rather than an operation missing an argument - the router simply does not match
    /// it, and there is no binding to fail.
    /// </para>
    /// <para>
    /// THE LOOP REFUSES TO BE EMPTY. A document that published no query-bound identifier would otherwise
    /// satisfy every assertion by finding nothing to check.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryQueryBoundSessionIdentifierOperationRefusesItsOwnOmissionAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        List<(string Method, string Path)> queryBound = [];

        using (HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);

            using JsonDocument document = await RestProjection.DocumentAsync(served);

            foreach ((string method, string path, JsonElement operation) in ProjectedOperations(document))
            {
                if (DeclaresSessionIdIn(operation, "query"))
                {
                    queryBound.Add((method, path));
                }
            }
        }

        Assert.NotEmpty(queryBound);

        foreach ((string method, string path) in queryBound)
        {
            using HttpRequestMessage request = new(
                new HttpMethod(method.ToUpperInvariant()),
                RestProjection.Relative(path));

            using HttpResponseMessage response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            using JsonDocument body = await RestProjection.DocumentAsync(response);

            Assert.Equal(
                KernelRetCode.E_INVALID_ARGUMENT,
                body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64());

            Assert.False(
                body.RootElement.TryGetProperty(RestProjection.UpstreamMember, out _),
                $"{method} {path} named an upstream for a refusal formed in its own route.");
        }
    }

    // ==============================================================================================
    //  SECTION 3 - WHAT THE PUBLISHED DOCUMENT NOW SAYS
    // ==============================================================================================

    /// <summary>
    /// Every operation publishing a session-identifier parameter publishes it as required and declares
    /// the <c>400</c> that answers its violation.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// <para>
    /// TWO DEFECTS OF ONE KIND, BOTH ASSERTED. Binding the parameter nullable is what lets the route
    /// answer its omission, and it makes the generator infer OPTIONALITY from the signature - a document
    /// that says a required argument may be left out is worse than the 500 it replaced, because a
    /// generated client would build the request that fails. Separately, the three session-scoped
    /// operations declared no <c>400</c> at all, on the reasoning that an operation binding no body has
    /// nothing to reject; that overlooked the parameter, and an empty identifier had ALWAYS drawn a
    /// <c>400</c>. A declared constraint with no declared response for violating it is a promise no
    /// generated client can branch on.
    /// </para>
    /// <para>
    /// ASSERTED OVER EVERY OPERATION THAT PUBLISHES THE PARAMETER, in either location, so neither
    /// property can be restored on one operation and lost on the next.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task EveryPublishedSessionIdentifierIsRequiredAndCarriesItsBadRequestAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = await RestProjection.DocumentAsync(served);

        int carriers = 0;

        foreach ((string method, string path, JsonElement operation) in ProjectedOperations(document))
        {
            if (!DeclaresSessionIdIn(operation, "query") && !DeclaresSessionIdIn(operation, "path"))
            {
                continue;
            }

            carriers++;

            JsonElement parameter = operation
                .GetProperty("parameters")
                .EnumerateArray()
                .Single(candidate => string.Equals(
                    candidate.GetProperty("name").GetString(),
                    SessionIdParameterName,
                    StringComparison.Ordinal));

            Assert.True(
                parameter.TryGetProperty("required", out JsonElement required)
                    && required.GetBoolean(),
                $"{method.ToUpperInvariant()} {path} publishes sessionId as optional. The handler binds "
                    + "it nullable so the route can refuse an omission; the parameter is not optional, "
                    + "and a client generated from this document would build a request that cannot "
                    + "succeed.");

            Assert.True(
                operation.GetProperty("responses").TryGetProperty("400", out JsonElement badRequest),
                $"{method.ToUpperInvariant()} {path} declares sessionId required and declares no 400 for "
                    + "violating it.");

            // THE PROSE IS THE SESSION-IDENTIFIER PROSE ON THE BODILESS OPERATIONS, so a consumer is not
            // told to look for a body this operation does not accept. The two named conditions are the
            // two the route refuses, and no length is claimed - because none is published.
            string description = badRequest.GetProperty("description").GetString() ?? string.Empty;

            if (OperationAcceptsNoBody(operation))
            {
                Assert.Contains("it was omitted", description, StringComparison.Ordinal);
                Assert.Contains("or it is empty", description, StringComparison.Ordinal);
                Assert.Contains("takes no request body", description, StringComparison.Ordinal);
                Assert.DoesNotContain("128", description, StringComparison.Ordinal);
            }
        }

        // Three today - the two session closes and the gate read - and the count is asserted so a
        // document that published the parameter nowhere cannot satisfy the loop by skipping it.
        Assert.Equal(3, carriers);
    }

    /// <summary>
    /// A body-bound operation keeps the shared <c>400</c> prose.
    /// </summary>
    /// <returns>A task representing the assertions.</returns>
    /// <remarks>
    /// THE COMPLEMENT THAT KEEPS THE CASE ABOVE HONEST. The session-identifier wording is selected per
    /// operation, so a selector that returned it for everything would satisfy every assertion in this
    /// file while telling the consumer of a body-bound operation that the operation accepts no body.
    /// </remarks>
    [Fact]
    public async Task ABodyBoundOperationKeepsTheSharedBadRequestProseAsync()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAuthenticatedClient();

        using HttpResponseMessage served = await client.GetAsync(
            RestProjection.Relative(RestProjection.DocumentRoute),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);

        using JsonDocument document = await RestProjection.DocumentAsync(served);

        int bodyBound = 0;

        foreach ((string method, string path, JsonElement operation) in ProjectedOperations(document))
        {
            if (OperationAcceptsNoBody(operation))
            {
                continue;
            }

            bodyBound++;

            string description = operation
                .GetProperty("responses")
                .GetProperty("400")
                .GetProperty("description")
                .GetString() ?? string.Empty;

            Assert.DoesNotContain("takes no request body", description, StringComparison.Ordinal);

            Assert.True(
                description.Contains("own binding", StringComparison.Ordinal)
                    || description.Contains("cross-DataWindow variable reference", StringComparison.Ordinal),
                $"{method.ToUpperInvariant()} {path} publishes a 400 description that is neither the "
                    + "shared wording nor the cross-session narrowing: '{description}'.");
        }

        // Thirty-six of the thirty-nine projected operations bind a body.
        Assert.Equal(36, bodyBound);
    }

    // ==============================================================================================
    //  READING HELPERS - EACH READS, NONE ASSERTS ON BEHALF OF A CASE
    // ==============================================================================================

    /// <summary>Enumerates the projected operations of a generated document.</summary>
    /// <param name="document">The generated document.</param>
    /// <returns>The method, the path and the operation object, for each projected operation.</returns>
    /// <remarks>
    /// FILTERED ON THE <c>x-grpc-method</c> EXTENSION rather than on the path prefix alone, because the
    /// prefix also carries this service's own non-projected routes and an operation without the extension
    /// is not one of the thirty-nine.
    /// </remarks>
    private static IEnumerable<(string Method, string Path, JsonElement Operation)> ProjectedOperations(
        JsonDocument document)
    {
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (!path.Name.StartsWith(RestProjectionContract.ProjectedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (JsonProperty operation in path.Value.EnumerateObject())
            {
                if (operation.Value.TryGetProperty("x-grpc-method", out _))
                {
                    yield return (operation.Name, path.Name, operation.Value);
                }
            }
        }
    }

    /// <summary>Reports whether an operation publishes the session identifier in a given location.</summary>
    /// <param name="operation">The operation object.</param>
    /// <param name="location">The OpenAPI parameter location, <c>query</c> or <c>path</c>.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool DeclaresSessionIdIn(JsonElement operation, string location)
    {
        if (!operation.TryGetProperty("parameters", out JsonElement parameters))
        {
            return false;
        }

        foreach (JsonElement parameter in parameters.EnumerateArray())
        {
            if (string.Equals(
                    parameter.GetProperty("name").GetString(),
                    SessionIdParameterName,
                    StringComparison.Ordinal)
                && string.Equals(
                    parameter.GetProperty("in").GetString(),
                    location,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reports whether an operation accepts no request body.</summary>
    /// <param name="operation">The operation object.</param>
    /// <returns><see langword="true"/> when the operation declares no request body.</returns>
    private static bool OperationAcceptsNoBody(JsonElement operation) =>
        !operation.TryGetProperty("requestBody", out _);

    /// <summary>Issues a GET and reads the refusal it answered.</summary>
    /// <param name="client">The client, in whichever authentication posture the case chose.</param>
    /// <param name="address">The address, relative.</param>
    /// <returns>The refusal, read whole so a case can compare two of them.</returns>
    private static async Task<Refusal> ReadRefusalAsync(HttpClient client, string address)
    {
        using HttpResponseMessage response = await client.GetAsync(
            RestProjection.Relative(address),
            TestContext.Current.CancellationToken);

        string text = await RestProjection.BodyAsync(response).ConfigureAwait(false);

        using JsonDocument body = JsonDocument.Parse(text);

        return new Refusal(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            body.RootElement.GetProperty(RestProjection.RetCodeMember).GetInt64(),
            body.RootElement.TryGetProperty(RestProjection.UpstreamMember, out _),
            body.RootElement.TryGetProperty("detail", out JsonElement detail)
                ? detail.GetString() ?? string.Empty
                : string.Empty,
            body.RootElement.TryGetProperty(RestProjection.TraceIdMember, out JsonElement traceId)
                ? traceId.GetString() ?? string.Empty
                : string.Empty,
            text);
    }

    /// <summary>One refusal, read whole.</summary>
    /// <param name="Status">The HTTP status.</param>
    /// <param name="MediaType">The response media type.</param>
    /// <param name="RetCode">The legacy return code the problem body carries.</param>
    /// <param name="HasUpstream">Whether the body named an upstream as the cause.</param>
    /// <param name="Detail">The problem detail.</param>
    /// <param name="TraceId">The correlation identifier.</param>
    /// <param name="Body">The body as text, for the value-absence assertions.</param>
    /// <remarks>
    /// A record so two refusals can be compared field by field, which is what makes the
    /// indistinguishability assertion an assertion about the pair rather than about two literals.
    /// </remarks>
    private sealed record Refusal(
        HttpStatusCode Status,
        string MediaType,
        long RetCode,
        bool HasUpstream,
        string Detail,
        string TraceId,
        string Body);
}
