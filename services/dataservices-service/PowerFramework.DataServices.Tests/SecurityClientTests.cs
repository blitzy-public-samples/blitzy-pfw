// ==================================================================================================
//  SecurityClientTests.cs
//  Conformance tests for Clients/SecurityClient.cs - contracts C-01 (TokenService) and C-02
//  (CryptoService).
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE TESTS ASSERT, AND WHY THAT IS THE RIGHT BAR
//    The C-01 half has a caller inside this service: the Persistence client attaches the credential
//    this provider yields. Its bar is therefore behavioural - reuse, refresh, typed failure,
//    cancellation.
//
//    The C-02 half has NO caller inside this service, and that is a discovery rather than a gap. A
//    repository-wide search establishes that the legacy n_crypto is not used anywhere in the three
//    libraries this service and its downstream are ported from. Its correctness bar is therefore
//    CONFORMANCE TO THE PUBLISHED SCHEMA - path, member spelling, member presence and absence, enum
//    encoding, status handling - and NOT call-site parity, because there is no legacy call site in
//    this service to be in parity with. Fabricating one would be a new feature.
//
//  NO NETWORK IS REACHED BY ANY TEST HERE. Every one runs against a recording message handler, so the
//  suite is hermetic and no Security instance is required. The clock is substituted too, so expiry and
//  reuse are exercised without waiting for real time to pass.
//
//  NO SECRET, CREDENTIAL, KEY, CERTIFICATE OR REFERENCE VALUE IN THIS FILE IS REAL. The token and
//  reference values below are obviously-fake fixed markers chosen so that they cannot match any
//  provider's credential pattern; none is copied from anywhere in the repository, and in particular
//  nothing is taken from the read-only legacy browser asset tests/blink/test_jws.htm, which is the
//  hardcoded-private-key anti-pattern the client under test replaces.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// A recording message handler. It reaches no network, captures every request it is given together
/// with the body text and the cancellation token it observed, and answers from a queue of canned
/// responses.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>The requests this handler was given, in order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The request body text this handler observed, in order.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Whether the last observed cancellation token could be cancelled.</summary>
    public bool ObservedCancellableToken { get; private set; }

    /// <summary>Queues a JSON response.</summary>
    /// <param name="statusCode">The status to answer with.</param>
    /// <param name="json">The body, or <see langword="null"/> for no content.</param>
    /// <param name="mediaType">The media type to report.</param>
    /// <returns>This handler, so queueing can be chained.</returns>
    public RecordingHandler Enqueue(
        HttpStatusCode statusCode,
        string? json,
        string mediaType = "application/json")
    {
        HttpResponseMessage response = new(statusCode);
        if (json is not null)
        {
            response.Content = new StringContent(json, Encoding.UTF8, mediaType);
        }

        _responses.Enqueue(response);
        return this;
    }

    /// <summary>Queues a response whose content the caller builds itself.</summary>
    /// <param name="statusCode">The status to answer with.</param>
    /// <param name="content">The content to answer with.</param>
    /// <returns>This handler, so queueing can be chained.</returns>
    public RecordingHandler EnqueueRaw(HttpStatusCode statusCode, HttpContent content)
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode) { Content = content });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObservedCancellableToken = cancellationToken.CanBeCanceled;

        Requests.Add(request);
        Bodies.Add(request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult());

        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}

/// <summary>
/// A clock a test can move. The production client reads no other clock, which is what makes expiry and
/// reuse reproducible rather than schedule-dependent.
/// </summary>
internal sealed class MutableClock : TimeProvider
{
    /// <summary>The instant this clock reports.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

public sealed class SecurityClientTests
{
    // Obviously-fake fixed markers. None is a credential, none resembles one, and none is copied from
    // anywhere in the repository.
    private const string FakeToken = "fake.not-a-real-token.value";
    private const string TestBaseAddress = "https://security.invalid/";

    private static (SecurityClient Client, RecordingHandler Handler, MutableClock Clock) CreateClient(
        RecordingHandler handler,
        string? baseAddress = TestBaseAddress,
        string configuredAddress = TestBaseAddress)
    {
        HttpClient httpClient = new(handler, disposeHandler: false);
        if (baseAddress is not null)
        {
            httpClient.BaseAddress = new Uri(baseAddress, UriKind.Absolute);
        }

        DataServicesOptions options = new();
        options.Security.BaseAddress = configuredAddress;

        MutableClock clock = new();
        SecurityClient client = new(
            httpClient,
            Options.Create(options),
            NullLogger<SecurityClient>.Instance,
            clock);

        return (client, handler, clock);
    }

    private static string TokenResponseJson(
        long expiresIn = 300,
        string scope = "persistence.read persistence.write",
        long? issuedAt = null,
        string accessToken = FakeToken,
        string tokenType = "Bearer")
    {
        string issued = issuedAt is long value
            ? string.Create(CultureInfo.InvariantCulture, $",\"issued_at\":{value}")
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"access_token\":\"{accessToken}\",\"token_type\":\"{tokenType}\","
            + $"\"expires_in\":{expiresIn},\"scope\":\"{scope}\"{issued}}}");
    }

    private static string ProblemJson(long? retCode, string title = "Access denied") => retCode is long code
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"type\":\"about:blank\",\"title\":\"{title}\",\"status\":403,"
            + $"\"detail\":\"The caller is not permitted this reference.\",\"retCode\":{code}}}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"type\":\"about:blank\",\"title\":\"{title}\",\"status\":403}}");

    private static ServiceTokenRequest SampleRequest() =>
        new("powerframework-dataservices", "powerframework-persistence", ["persistence.read"]);

    private static JsonElement Body(RecordingHandler handler, int index = 0) =>
        JsonDocument.Parse(handler.Bodies[index]).RootElement;

    // ==============================================================================================
    //  C-01 - token issuance, reuse, refresh and failure
    // ==============================================================================================

    [Fact]
    public async Task GetTokenAsync_SendsTheThreePublishedMembersAndNoCredential()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);

        ServiceToken token = await client.GetTokenAsync(
            new ServiceTokenRequest("subject-a", "audience-b", ["scope.one", "scope.two"]),
            TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://security.invalid/v1/tokens", request.RequestUri?.ToString());

        // Authenticated by the transport, so no Authorization header is attached: a caller cannot
        // present a bearer token in order to obtain its first bearer token.
        Assert.Null(request.Headers.Authorization);

        JsonElement body = Body(handler);
        Assert.Equal(3, body.EnumerateObject().Count());
        Assert.Equal("subject-a", body.GetProperty("subject").GetString());
        Assert.Equal("audience-b", body.GetProperty("audience").GetString());
        Assert.Equal(
            ["scope.one", "scope.two"],
            body.GetProperty("scopes").EnumerateArray().Select(element => element.GetString()));

        Assert.Equal(FakeToken, token.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
    }

    [Fact]
    public async Task GetTokenAsync_ReadsTheGrantedScopeSetRatherThanTheRequestedOne()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(scope: "persistence.read"));
        (SecurityClient client, _, _) = CreateClient(handler);

        ServiceToken token = await client.GetTokenAsync(
            new ServiceTokenRequest("s", "a", ["persistence.read", "persistence.write"]),
            TestContext.Current.CancellationToken);

        // A narrowing is a normal successful outcome under this contract, not a failure.
        Assert.Equal(["persistence.read"], token.GrantedScopes);
    }

    [Fact]
    public async Task GetTokenAsync_TreatsAnEmptyGrantedScopeSetAsSuccess()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(scope: string.Empty));
        (SecurityClient client, _, _) = CreateClient(handler);

        ServiceToken token = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Empty(token.GrantedScopes);
    }

    [Fact]
    public async Task GetTokenAsync_AnchorsExpiryToTheReportedIssuanceInstantWhenSupplied()
    {
        long issuedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 60, issuedAt: issuedAt));
        (SecurityClient client, _, _) = CreateClient(handler);

        ServiceToken token = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(issuedAt).AddSeconds(60),
            token.ExpiresAt);
    }

    [Fact]
    public async Task GetTokenAsync_AnchorsExpiryToTheInjectedClockWhenNoInstantIsSupplied()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 120));
        (SecurityClient client, _, MutableClock clock) = CreateClient(handler);
        DateTimeOffset start = clock.UtcNow;

        ServiceToken token = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(start.AddSeconds(120), token.ExpiresAt);
    }

    [Fact]
    public async Task GetTokenAsync_ReusesTheHeldCredentialWhileItRemainsValid()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 300));
        (SecurityClient client, _, MutableClock clock) = CreateClient(handler);

        ServiceToken first = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        clock.UtcNow = clock.UtcNow.AddSeconds(299);

        ServiceToken second = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetTokenAsync_RequestsAgainOnceTheHeldCredentialHasLapsed()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 300))
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 300, accessToken: "fake.second"));
        (SecurityClient client, _, MutableClock clock) = CreateClient(handler);

        ServiceToken first = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        // Exactly at expiry, because the comparison is strict and no skew margin is subtracted.
        clock.UtcNow = first.ExpiresAt;

        ServiceToken second = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal("fake.second", second.AccessToken);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTokenAsync_HoldsACredentialPerSubjectAudienceAndScopeSet()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson())
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);

        await client.GetTokenAsync(
            new ServiceTokenRequest("s", "audience-one", ["scope"]),
            TestContext.Current.CancellationToken);
        await client.GetTokenAsync(
            new ServiceTokenRequest("s", "audience-two", ["scope"]),
            TestContext.Current.CancellationToken);

        // Keying on the audience matters: reusing one audience's token for another would hand a caller a
        // credential minted for somewhere else.
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTokenAsync_ReusesACredentialWhenTheSameScopeSetArrivesInAnotherOrder()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);

        await client.GetTokenAsync(
            new ServiceTokenRequest("s", "a", ["one", "two"]),
            TestContext.Current.CancellationToken);
        await client.GetTokenAsync(
            new ServiceTokenRequest("s", "a", ["two", "one"]),
            TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetTokenAsync_SendsTheRequestedScopeOrderVerbatim()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);

        await client.GetTokenAsync(
            new ServiceTokenRequest("s", "a", ["zeta", "alpha"]),
            TestContext.Current.CancellationToken);

        // Ordering happens only inside the cache key; what is sent is what the caller asked for.
        Assert.Equal(
            ["zeta", "alpha"],
            Body(handler).GetProperty("scopes").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task GetTokenAsync_SurfacesARefusalAsATypedFailureAndNeverAnEmptyToken()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.Forbidden,
            ProblemJson(RetCode.E_ACCESS_DENIED),
            "application/problem+json");
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("issueToken", failure.OperationId);
        Assert.Equal((int)HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Equal("about:blank", failure.ProblemType);
        Assert.Equal("Access denied", failure.Title);
        Assert.Equal("The caller is not permitted this reference.", failure.Detail);
        Assert.Equal(RetCode.E_ACCESS_DENIED, failure.RetCode);
        Assert.DoesNotContain(FakeToken, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTokenAsync_DoesNotCacheAFailedIssuance()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.InternalServerError, ProblemJson(RetCode.E_INTERNAL_ERROR))
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);

        await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        ServiceToken token = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(FakeToken, token.AccessToken);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("{\"access_token\":\"\",\"token_type\":\"Bearer\",\"expires_in\":60,\"scope\":\"\"}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"Basic\",\"expires_in\":60,\"scope\":\"\"}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"Bearer\",\"expires_in\":0,\"scope\":\"\"}")]
    [InlineData("{\"token_type\":\"Bearer\",\"expires_in\":60,\"scope\":\"\"}")]
    [InlineData("{\"access_token\":\"t\",\"token_type\":\"Bearer\",\"expires_in\":60}")]
    public async Task GetTokenAsync_RefusesAnOffContractIssuanceResponse(string json)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, json);
        (SecurityClient client, _, _) = CreateClient(handler);

        await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task GetTokenAsync_RefusesAnEmptySuccessBody()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, json: null);
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("issueToken", failure.OperationId);
    }

    // ==============================================================================================
    //  The problem+json to return-code projection, INCLUDING the preserved tri-state hole
    // ==============================================================================================

    [Fact]
    public async Task RetCodeProjection_TreatsPreventAsSucceeded()
    {
        SecurityClientException failure = await RefuseWithAsync(RetCode.PREVENT);

        // Preserved legacy behaviour: the success predicate is >= 0, so a prevention reads as a success.
        Assert.True(failure.RetCodeIsSucceeded);
        Assert.False(failure.RetCodeIsFailed);
        Assert.False(failure.RetCodeIsCancelled);
    }

    [Fact]
    public async Task RetCodeProjection_TreatsCancelledAsNeitherSucceededNorFailed()
    {
        SecurityClientException failure = await RefuseWithAsync(RetCode.CANCELLED);

        Assert.False(failure.RetCodeIsSucceeded);
        Assert.False(failure.RetCodeIsFailed);
        Assert.True(failure.RetCodeIsCancelled);
    }

    [Fact]
    public async Task RetCodeProjection_TreatsAMissingCodeAsNeither()
    {
        SecurityClientException failure = await RefuseWithAsync(retCode: null);

        // Null is never coerced to zero. Coercing it would turn "the response did not tell us" into
        // "succeeded".
        Assert.Null(failure.RetCode);
        Assert.False(failure.RetCodeIsSucceeded);
        Assert.False(failure.RetCodeIsFailed);
        Assert.False(failure.RetCodeIsCancelled);
    }

    [Theory]
    [InlineData(RetCode.E_INVALID_ARGUMENT)]
    [InlineData(RetCode.E_ACCESS_DENIED)]
    [InlineData(RetCode.E_OBJECT_NOT_FOUND)]
    [InlineData(RetCode.E_INTERNAL_ERROR)]
    [InlineData(RetCode.UNKNOWN)]
    public async Task RetCodeProjection_CarriesTheDeclaredFailureCodesThrough(long retCode)
    {
        SecurityClientException failure = await RefuseWithAsync(retCode);

        Assert.Equal(retCode, failure.RetCode);
        Assert.True(failure.RetCodeIsFailed);
    }

    [Fact]
    public async Task RetCodeProjection_IgnoresANonIntegerExtensionMember()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.BadRequest,
            "{\"title\":\"Bad request\",\"retCode\":\"not-a-number\"}",
            "application/problem+json");
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Null(failure.RetCode);
    }

    [Fact]
    public async Task RetCodeProjection_SurvivesAResponseCarryingNoProblemBodyAtAll()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.ServiceUnavailable, "plain text", "text/plain");
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        // The status is the substantive answer and is never lost behind a deserialization failure.
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Null(failure.Title);
        Assert.Null(failure.RetCode);
    }

    private static async Task<SecurityClientException> RefuseWithAsync(long? retCode)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.Forbidden,
            ProblemJson(retCode),
            "application/problem+json");
        (SecurityClient client, _, _) = CreateClient(handler);

        return await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());
    }

    // ==============================================================================================
    //  Cancellation and fail-fast configuration
    // ==============================================================================================

    [Fact]
    public async Task GetTokenAsync_ObservesAnAlreadyCancelledToken()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson());
        (SecurityClient client, _, _) = CreateClient(handler);
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetTokenAsync(SampleRequest(), source.Token).AsTask());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HashAsync_PropagatesCancellationIntoTheTransport()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, "{\"digest\":\"d\"}");
        (SecurityClient client, _, _) = CreateClient(handler);
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.HashAsync(
                CryptoPayload.FromString("payload"),
                Enums.CRYPTO_HASH_SHA256,
                source.Token));
    }

    [Fact]
    public async Task EveryOperation_ForwardsACancellableTokenToTheTransport()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, "{\"digest\":\"d\"}");
        (SecurityClient client, _, _) = CreateClient(handler);
        using CancellationTokenSource source = new();

        await client.HashAsync(
            CryptoPayload.FromString("payload"),
            Enums.CRYPTO_HASH_SHA256,
            source.Token);

        Assert.True(handler.ObservedCancellableToken);
    }

    [Fact]
    public async Task AnyOperation_FailsFastAndNamesTheSettingWhenNoAddressIsConfigured()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _, _) = CreateClient(
            handler,
            baseAddress: null,
            configuredAddress: string.Empty);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GenerateGuidAsync(flags: null, TestContext.Current.CancellationToken));

        Assert.Contains("DataServices:Security:BaseAddress", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the setting itself is missing", failure.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnyOperation_DistinguishesAnUnappliedSettingFromAMissingOne()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _, _) = CreateClient(handler, baseAddress: null);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GenerateGuidAsync(flags: null, TestContext.Current.CancellationToken));

        Assert.Contains(
            "registered without applying it",
            failure.Message,
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  The three-argument constructor, and the boundary conditions of the expiry conversion
    // ==============================================================================================

    [Fact]
    public async Task TheThreeArgumentConstructorResolvesAgainstTheSystemClock()
    {
        // This overload exists so the type resolves whether or not a clock has been registered in the
        // container. It performs no network input or output, exactly as the seamed overload does not.
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, TokenResponseJson(expiresIn: 600));
        using HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(TestBaseAddress, UriKind.Absolute),
        };

        SecurityClient client = new(
            httpClient,
            Options.Create(new DataServicesOptions()),
            NullLogger<SecurityClient>.Instance);

        ServiceToken token = await client.GetTokenAsync(
            SampleRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(FakeToken, token.AccessToken);
        Assert.True(token.ExpiresAt > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task GetTokenAsync_RefusesAnIssuanceInstantOutsideTheRepresentableRange()
    {
        long beyondMaximum = DateTimeOffset.MaxValue.ToUnixTimeSeconds() + 1;
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            TokenResponseJson(expiresIn: 60, issuedAt: beyondMaximum));
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("issuance timestamp", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTokenAsync_RefusesALifetimeThatOverflowsTheRepresentableRange()
    {
        // At the very end of the representable range, so the lifetime cannot be added to it.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            TokenResponseJson(
                expiresIn: 60,
                issuedAt: DateTimeOffset.MaxValue.ToUnixTimeSeconds()));
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("outside the representable range", failure.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
    }

    [Fact]
    public async Task GetTokenAsync_RefusesAJsonNullSuccessBody()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, "null");
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("empty body", failure.Message, StringComparison.Ordinal);
        Assert.Equal("issueToken", failure.OperationId);
    }

    [Fact]
    public async Task ARefusalWhoseBodyCannotBeReadAtAllStillReportsItsStatus()
    {
        // A JSON media type this runtime cannot decode. The problem body degrades to nothing while the
        // status - the substantive answer - survives.
        StringContent content = new("{}", Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/problem+json")
        {
            CharSet = "unsupported-charset-name",
        };

        RecordingHandler handler = new RecordingHandler()
            .EnqueueRaw(HttpStatusCode.BadGateway, content);
        (SecurityClient client, _, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(SampleRequest(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal((int)HttpStatusCode.BadGateway, failure.StatusCode);
        Assert.Null(failure.Title);
        Assert.Null(failure.RetCode);
    }

    // ==============================================================================================
    //  The return-code extension member, read directly
    //
    //  The two integral arms are unreachable through the transport, because deserialization always parks
    //  an unmatched member as a JsonElement. They exist for a body constructed in process, and the
    //  sibling-assembly visibility the project grants is how that is exercised.
    // ==============================================================================================

    [Fact]
    public void ReadRetCode_AcceptsEitherRepresentationAndRefusesToInventAValue()
    {
        Microsoft.AspNetCore.Mvc.ProblemDetails fromLong = new();
        fromLong.Extensions["retCode"] = RetCode.E_DB_ERROR;
        Assert.Equal(RetCode.E_DB_ERROR, SecurityClient.ReadRetCode(fromLong));

        Microsoft.AspNetCore.Mvc.ProblemDetails fromInt = new();
        fromInt.Extensions["retCode"] = -3;
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, SecurityClient.ReadRetCode(fromInt));

        Microsoft.AspNetCore.Mvc.ProblemDetails fromElement = new();
        fromElement.Extensions["retCode"] = JsonDocument.Parse("1").RootElement;
        Assert.Equal(RetCode.PREVENT, SecurityClient.ReadRetCode(fromElement));

        Microsoft.AspNetCore.Mvc.ProblemDetails fromText = new();
        fromText.Extensions["retCode"] = "not a number";
        Assert.Null(SecurityClient.ReadRetCode(fromText));

        Microsoft.AspNetCore.Mvc.ProblemDetails fromNull = new();
        fromNull.Extensions["retCode"] = null;
        Assert.Null(SecurityClient.ReadRetCode(fromNull));

        Assert.Null(SecurityClient.ReadRetCode(new Microsoft.AspNetCore.Mvc.ProblemDetails()));
        Assert.Null(SecurityClient.ReadRetCode(null));
    }
}
