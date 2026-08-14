// ==================================================================================================
//  SecurityClientBoundaryTests - the two boundary properties of Gateway's Security client
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//    1. A REFUSAL'S STATUS SURVIVES A MALFORMED OR UNREADABLE RESPONSE BODY. The problem-details body
//       on a refusal is OPTIONAL enrichment: the status code is the substantive answer, and the caller
//       must receive it whatever the body turns out to be. Three failure shapes are absorbed, and the
//       third - a response declaring a charset that resolves to no encoding - is the one a two-shape
//       catch list omits, which replaces a 401 or a 503 with an unrelated encoding complaint and tells
//       the caller the wrong thing about its own request.
//
//    2. THE CREDENTIAL CACHE KEY IS INJECTIVE FOR ARBITRARY COMPONENT CONTENT. Two distinct token
//       requests must never resolve to one key. A collision is not a cache inefficiency: it hands one
//       caller a credential minted for a different audience or a different scope set, which is exactly
//       what the contract's one-audience-per-request rule exists to prevent.
//
//  WHY THE COLLISION TEST LOOKS ODD
//  ------------------------------------------------------------------------------------------------
//  It sends a subject and an audience carrying U+001F. That is deliberate, and it is the point: joining
//  the key's components with that character rests on the ground that no subject,
//  audience or scope can contain it - a claim that holds for this service's own fixed call sites
//  and is CHECKED nowhere, so it is a convention rather than a control. The two requests below
//  are the minimal pair that collide under such a key, and they do not collide under this one. A test
//  written only against ordinary values would pass against both encodings and prove nothing.
//
//  NO CREDENTIAL, KEY OR SECRET IN THIS FILE IS REAL, and none is copied from anywhere in the
//  repository.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Configuration;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// A message handler that answers from a queue and records what it was asked.
/// </summary>
internal sealed class QueuedResponseHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>The requests this handler was given, in order.</summary>
    internal List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Each request's body, captured AT SEND TIME and in the same order as <see cref="Requests"/>.
    /// </summary>
    /// <remarks>
    /// Captured here rather than read from the recorded request afterwards, because the sender disposes
    /// the request - and with it the content - as soon as the call returns. A test reading the body later
    /// gets ObjectDisposedException, which looks like a test defect and is really just the wrong moment.
    /// </remarks>
    internal List<string> RequestBodies { get; } = [];

    /// <summary>Queues a response built by the caller, so a test can shape its headers exactly.</summary>
    /// <param name="response">The response to answer with.</param>
    /// <returns>This handler, so queueing can be chained.</returns>
    internal QueuedResponseHandler Enqueue(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
        return this;
    }

    /// <summary>Queues a JSON response.</summary>
    /// <param name="statusCode">The status to answer with.</param>
    /// <param name="json">The body.</param>
    /// <returns>This handler, so queueing can be chained.</returns>
    internal QueuedResponseHandler EnqueueJson(HttpStatusCode statusCode, string json)
        => Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);

        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }
}

/// <summary>
/// The boundary suite.
/// </summary>
public sealed class SecurityClientBoundaryTests
{
    /// <summary>An obviously-fake fixed marker. Not a credential and copied from nowhere.</summary>
    private const string FirstCredential = "first.not-a-real-token.value";

    /// <summary>A second obviously-fake fixed marker, distinguishable from the first.</summary>
    private const string SecondCredential = "second.not-a-real-token.value";

    /// <summary>The unit separator the previous cache-key encoding relied on as a delimiter.</summary>
    private const char UnitSeparator = '\u001F';

    // ----------------------------------------------------------------------------------------------
    //  THE ISSUANCE CREDENTIAL - what this client PRESENTS on the one operation a bearer cannot protect
    //
    //  POST /v1/tokens overrides the document-level bearer requirement and declares two ALTERNATIVE
    //  schemes, because a caller cannot present a bearer token in order to obtain its first bearer
    //  token. Until this client presented one of them, Security refused every issuance request it made
    //  and every authenticated call Gateway would go on to make was unreachable.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A configured secret is presented as an HTTP Basic credential whose user-id is the CLAIMED SUBJECT.
    /// </summary>
    /// <remarks>
    /// The user-id is taken from the request rather than configured separately, because the issuance edge
    /// reconciles the subject in the body against the identity the credential establishes and refuses a
    /// mismatch with 403. Decoding the parameter here is what proves the two agree.
    /// </remarks>
    [Fact]
    public async Task AConfiguredSecretIsPresentedAsBasicWithTheClaimedSubjectAsTheUserId()
    {
        const string subject = "powerframework-gateway";
        const string secret = "an-issuance-secret-shaped-value";

        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));

        SecurityClient client = CreateClientWithCredential(handler, secret);

        _ = await client.GetTokenAsync(
            new ServiceTokenRequest(subject, "audience", ["scope"]),
            TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);

        AuthenticationHeaderValue? credential = sent.Headers.Authorization;
        Assert.NotNull(credential);
        Assert.NotNull(credential.Parameter);

        // The canonical scheme spelling, even though the receiver compares it case-insensitively.
        Assert.Equal("Basic", credential.Scheme);

        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credential.Parameter));

        Assert.Equal(subject + ":" + secret, decoded);

        // AND THE BODY CARRIES NO CREDENTIAL. The schema sets additionalProperties false, and a secret in
        // a body is logged by every intermediary that logs bodies.
        string body = Assert.Single(handler.RequestBodies);

        Assert.DoesNotContain(secret, body, StringComparison.Ordinal);

        // The three members the schema declares, and nothing else that could carry material.
        Assert.Contains("\"subject\"", body, StringComparison.Ordinal);
        Assert.Contains("\"audience\"", body, StringComparison.Ordinal);
        Assert.Contains("\"scopes\"", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no secret configured the request carries NO authorization header at all, leaving the client
    /// certificate on the primary handler as the credential.
    /// </summary>
    /// <remarks>
    /// An empty header would be a malformed credential rather than an absent one, and a bearer header
    /// would be meaningless on an operation that does not accept one. A deployment configured with
    /// NEITHER scheme never reaches here: the options validator refuses it at startup.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithNoSecretConfiguredTheIssuanceRequestCarriesNoAuthorizationHeader(string secret)
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));

        SecurityClient client = CreateClientWithCredential(handler, secret);

        _ = await client.GetTokenAsync(
            new ServiceTokenRequest("subject", "audience", ["scope"]),
            TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);

        Assert.Null(sent.Headers.Authorization);
    }

    /// <summary>
    /// The credential is a per-request header and is never left on the client's default headers.
    /// </summary>
    /// <remarks>
    /// Setting it as a default would attach it to every request the client ever makes - including any that
    /// must not carry it - and would leave a credential resident on a container-held singleton between
    /// calls. Two issuance requests are made so the second also proves the first did not mutate the
    /// client.
    /// </remarks>
    [Fact]
    public async Task TheCredentialIsPerRequestAndIsNeverLeftOnTheClientDefaults()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(SecondCredential));

        SecurityClient client = CreateClientWithCredential(handler, "a-secret");

        _ = await client.GetTokenAsync(
            new ServiceTokenRequest("subject-one", "audience", ["scope"]),
            TestContext.Current.CancellationToken);
        _ = await client.GetTokenAsync(
            new ServiceTokenRequest("subject-two", "audience", ["scope"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);

        // EACH REQUEST CARRIES ITS OWN SUBJECT, which is what a default header could not have done.
        foreach ((HttpRequestMessage sent, string expectedSubject) in
            handler.Requests.Zip((string[])["subject-one", "subject-two"]))
        {
            string? parameter = sent.Headers.Authorization?.Parameter;
            Assert.NotNull(parameter);

            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));

            Assert.StartsWith(expectedSubject + ":", decoded, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A non-ASCII secret survives the round trip, because both sides use UTF-8.
    /// </summary>
    /// <remarks>
    /// RFC 7617 leaves the charset to agreement, and the two sides agree on UTF-8. A secret containing a
    /// colon is included too: the receiver splits on the FIRST colon, and the user-id cannot contain one,
    /// so the halves are recovered unambiguously.
    /// </remarks>
    [Theory]
    [InlineData("pässwörd-with-diacritics")]
    [InlineData("secret:with:colons")]
    [InlineData("秘密の値")]
    public async Task ASecretIsEncodedAsUtf8AndSurvivesTheRoundTrip(string secret)
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));

        SecurityClient client = CreateClientWithCredential(handler, secret);

        _ = await client.GetTokenAsync(
            new ServiceTokenRequest("subject", "audience", ["scope"]),
            TestContext.Current.CancellationToken);

        HttpRequestMessage sent = Assert.Single(handler.Requests);

        string? parameter = sent.Headers.Authorization?.Parameter;
        Assert.NotNull(parameter);

        string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parameter));

        Assert.Equal("subject:" + secret, decoded);
    }

    // ----------------------------------------------------------------------------------------------
    //  F15 - THE REFUSAL STATUS SURVIVES AN UNREADABLE BODY.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// THE CENTRAL F15 ASSERTION. A refusal whose body declares an unresolvable charset still surfaces
    /// as the typed failure carrying the original HTTP status, rather than as an encoding complaint.
    /// </summary>
    /// <remarks>
    /// The charset is set through the header's parameter collection rather than through the content
    /// constructor, because the constructor validates the encoding name and would refuse to build the
    /// very response this test needs. A proxy or a misconfigured upstream is under no such constraint,
    /// which is why the shape is worth testing at all.
    /// </remarks>
    [Fact]
    public async Task ARefusalWithAnUnresolvableCharsetStillReportsItsHttpStatus()
    {
        StringContent content = new("{\"title\":\"Access denied\"}", Encoding.UTF8, "application/problem+json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json")
        {
            CharSet = "utf8x",
        };

        QueuedResponseHandler handler = new();
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = content });

        SecurityClient client = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest("subject", "audience", ["scope"]),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.Forbidden, failure.StatusCode);

        // The body could not be read, so no enrichment is claimed from it - and nothing is invented.
        Assert.Null(failure.Title);
        Assert.Null(failure.Detail);
    }

    /// <summary>
    /// The already-absorbed shape still behaves the same way, so the added catch did not change the two
    /// cases that already worked.
    /// </summary>
    [Fact]
    public async Task ARefusalWithAMalformedBodyStillReportsItsHttpStatus()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.ServiceUnavailable, "{ this is not json");

        SecurityClient client = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest("subject", "audience", ["scope"]),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, failure.StatusCode);
    }

    /// <summary>
    /// A readable problem-details body IS still read, so absorbing failures did not turn into ignoring
    /// bodies.
    /// </summary>
    [Fact]
    public async Task ARefusalWithAReadableBodyIsStillEnriched()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(
            HttpStatusCode.Forbidden,
            "{\"type\":\"about:blank\",\"title\":\"Access denied\","
            + "\"detail\":\"The caller is not permitted this audience.\",\"retCode\":-27}");

        SecurityClient client = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest("subject", "audience", ["scope"]),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Equal("Access denied", failure.Title);
        Assert.Equal(-27, failure.RetCode);
    }

    // ----------------------------------------------------------------------------------------------
    //  F19 - THE CACHE KEY IS INJECTIVE FOR ARBITRARY CONTENT.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// THE CENTRAL F19 ASSERTION. Two requests that collided under the delimiter encoding now resolve to
    /// two keys, so each receives the credential minted for IT rather than the other's.
    /// </summary>
    /// <remarks>
    /// Under the previous encoding both requests serialized to the same key, so the second call returned
    /// the FIRST call's cached credential and issued no request at all. Both assertions below would
    /// therefore have failed: one issuance instead of two, and the same credential twice.
    /// </remarks>
    [Fact]
    public async Task TwoRequestsThatWouldHaveCollidedUnderTheDelimiterEncodingDoNotShareACredential()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(SecondCredential));

        SecurityClient client = CreateClient(handler);

        // ("a\u001Fb", "c") and ("a", "b\u001Fc") produce the identical concatenation once the
        // components are joined with U+001F, and different concatenations once each is length-prefixed.
        ServiceToken first = await client.GetTokenAsync(
            new ServiceTokenRequest($"a{UnitSeparator}b", "c", ["scope"]),
            TestContext.Current.CancellationToken);

        ServiceToken second = await client.GetTokenAsync(
            new ServiceTokenRequest("a", $"b{UnitSeparator}c", ["scope"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(FirstCredential, first.AccessToken);
        Assert.Equal(SecondCredential, second.AccessToken);
    }

    /// <summary>
    /// The same holds for the scope set, which is encoded element by element rather than pre-joined with
    /// its RFC 6749 space separator.
    /// </summary>
    [Fact]
    public async Task TwoScopeSetsThatWouldHaveCollidedWhenPreJoinedDoNotShareACredential()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(SecondCredential));

        SecurityClient client = CreateClient(handler);

        ServiceToken first = await client.GetTokenAsync(
            new ServiceTokenRequest("subject", "audience", [$"read{UnitSeparator}write"]),
            TestContext.Current.CancellationToken);

        ServiceToken second = await client.GetTokenAsync(
            new ServiceTokenRequest("subject", "audience", ["read", "write"]),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(FirstCredential, first.AccessToken);
        Assert.Equal(SecondCredential, second.AccessToken);
    }

    /// <summary>
    /// Caching itself still works: the SAME request reuses the held credential and issues nothing
    /// further. Without this, a key that was merely "more unique" could satisfy the tests above by never
    /// matching anything.
    /// </summary>
    [Fact]
    public async Task AnIdenticalRequestReusesTheHeldCredential()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));

        SecurityClient client = CreateClient(handler);
        ServiceTokenRequest request = new("subject", "audience", ["read", "write"]);

        ServiceToken first = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        // A second, equal request built independently - so reuse cannot be an artefact of object
        // identity.
        ServiceToken second = await client.GetTokenAsync(
            new ServiceTokenRequest("subject", "audience", ["write", "read"]),
            TestContext.Current.CancellationToken);

        Assert.Single(handler.Requests);
        Assert.Same(first, second);
    }

    // ----------------------------------------------------------------------------------------------
    //  THE RENEWAL BOUNDARY - when a held credential stops being offered for reuse
    //
    //  The row above decides reuse by the KEY, against a clock that does not move. These three decide
    //  it by ELAPSED TIME, which is the axis the cache used to get wrong: the comparison was exact
    //  (`ExpiresAt > now`), so a credential still valid by a microsecond was handed out and attached to
    //  a new call. It could then lapse IN TRANSIT and reach the verifier expired, and the caller met a
    //  401 indistinguishable from a genuine authorization failure on a call that was correctly
    //  authorized when it was made.
    //
    //  The margin that fixes it is DERIVED rather than invented - it is this client's own configured
    //  outbound request timeout, the longest a call carrying the credential can still be in flight -
    //  and the third row below is the one that pins that derivation on THIS side of the boundary.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// A credential well inside its window is still served from cache, so the margin has not been
    /// widened into "never reuse anything".
    /// </summary>
    [Fact]
    public async Task TheHeldCredentialIsReusedWhileItRemainsComfortablyValid()
    {
        // THE POSITIVE ARM ON THE TIME AXIS, AND IT IS WHAT STOPS THE FIX OVERSHOOTING. A margin too wide
        // would send every call to the issuance edge - a worse failure than the one it prevents, and one a
        // renewal-only assertion would happily pass.
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));

        MovableClock clock = new();
        SecurityClient client = CreateClient(handler, clock);
        ServiceTokenRequest request = new("subject", "audience", ["read"]);

        ServiceToken first = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        // 200s into a 300s credential. The margin is 30s, so the boundary sits at 270s and is not reached.
        clock.UtcNow = clock.UtcNow.AddSeconds(200);

        ServiceToken second = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    /// A credential inside the renewal margin is replaced even though it has not yet expired.
    /// </summary>
    [Fact]
    public async Task TheHeldCredentialIsRenewedBeforeExpiryRatherThanAtIt()
    {
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(SecondCredential));

        MovableClock clock = new();
        SecurityClient client = CreateClient(handler, clock);
        ServiceTokenRequest request = new("subject", "audience", ["read"]);

        ServiceToken first = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        // One second INSIDE the 30s margin: still valid for 29 more seconds, and no longer offered.
        clock.UtcNow = first.ExpiresAt.AddSeconds(-29);

        ServiceToken renewed = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        Assert.NotSame(first, renewed);
        Assert.Equal(SecondCredential, renewed.AccessToken);
        Assert.Equal(2, handler.Requests.Count);

        // AND THE CREDENTIAL IT REPLACED WAS GENUINELY STILL VALID, which is the entire distinction being
        // drawn: this is renewal ahead of expiry, not recovery after it.
        Assert.True(first.ExpiresAt > clock.UtcNow);
    }

    /// <summary>
    /// The renewal boundary moves with Gateway's own configured outbound request timeout, so the margin
    /// is a deployment setting rather than a constant chosen in this client.
    /// </summary>
    [Fact]
    public async Task TheRenewalMarginIsGatewaysOwnConfiguredOutboundRequestTimeout()
    {
        // 🔴 THE ROW THAT PROVES THE DERIVATION RATHER THAN THE NUMBER. Both rows above would pass against
        // a hardcoded 30 seconds. This one configures a DIFFERENT timeout and shows the boundary follows
        // it - which is what makes the margin defensible: no duration originates in this client, and the
        // one it uses is the same published setting that already bounds the resilience pipeline.
        QueuedResponseHandler handler = new();
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(FirstCredential));
        handler.EnqueueJson(HttpStatusCode.OK, TokenJson(SecondCredential));

        MovableClock clock = new();

        // 90s, deliberately three times the default, so a boundary computed from the default would still
        // be reusing the credential at the instant this test asserts it is not.
        SecurityClient client = CreateClient(
            handler,
            clock,
            Options.Create(new GatewayOptions
            {
                Outbound = new GatewayOptions.OutboundCallOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(90),
                },
            }));

        ServiceTokenRequest request = new("subject", "audience", ["read"]);

        ServiceToken first = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        // 80s before expiry of a 300s credential: outside the 30s default, INSIDE the configured 90s.
        clock.UtcNow = first.ExpiresAt.AddSeconds(-80);

        ServiceToken renewed = await client.GetTokenAsync(request, TestContext.Current.CancellationToken);

        Assert.NotSame(first, renewed);
        Assert.Equal(2, handler.Requests.Count);

        // AND THE CAP STILL APPLIES ON TOP OF THE CONFIGURED VALUE. Half of a 300s lifetime is 150s, which
        // exceeds 90s, so the configured margin is the operative one here and the boundary is at 210s.
        Assert.Equal(first.ExpiresAt.AddSeconds(-90), first.RenewAt(TimeSpan.FromSeconds(90)));
    }

    // ----------------------------------------------------------------------------------------------
    //  FIXTURE CONSTRUCTION.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Builds the client over the handler, against a frozen clock.</summary>
    /// <param name="handler">The handler to answer from.</param>
    /// <returns>The client.</returns>
    private static SecurityClient CreateClient(QueuedResponseHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute),
        };

        return new SecurityClient(
            httpClient,
            NullLogger<SecurityClient>.Instance,
            new FrozenClock());
    }

    /// <summary>Builds the client over the handler against a clock the test advances itself.</summary>
    /// <param name="handler">The handler to answer from.</param>
    /// <param name="clock">The clock the test drives.</param>
    /// <param name="options">
    /// The bound options, or <see langword="null"/> to take the declared outbound defaults - which is
    /// also the shape a host takes when the configuration carries no outbound section.
    /// </param>
    /// <returns>The client.</returns>
    private static SecurityClient CreateClient(
        QueuedResponseHandler handler,
        MovableClock clock,
        IOptions<GatewayOptions>? options = null)
    {
        HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute),
        };

        return new SecurityClient(
            httpClient,
            NullLogger<SecurityClient>.Instance,
            clock,
            options);
    }

    /// <summary>
    /// Creates the client with an issuance credential configured, which is the deployed posture.
    /// </summary>
    /// <param name="handler">The scripted transport.</param>
    /// <param name="secret">The configured client secret.</param>
    /// <returns>The client under test.</returns>
    /// <remarks>
    /// The secret is supplied the way the composition root supplies it - on the bound options - rather
    /// than through a constructor parameter of its own, so what is exercised here is the production
    /// resolution path.
    /// </remarks>
    private static SecurityClient CreateClientWithCredential(QueuedResponseHandler handler, string secret)
    {
        HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute),
        };

        return new SecurityClient(
            httpClient,
            NullLogger<SecurityClient>.Instance,
            new FrozenClock(),
            Options.Create(new GatewayOptions { SecurityClientSecret = secret }));
    }

    /// <summary>A valid C-01 token response body carrying the supplied credential.</summary>
    /// <param name="credential">The credential value.</param>
    /// <returns>The JSON body.</returns>
    private static string TokenJson(string credential) =>
        "{\"access_token\":\"" + credential + "\",\"token_type\":\"Bearer\","
        + "\"expires_in\":300,\"scope\":\"scope\"}";

    /// <summary>
    /// A clock that does not move, so a held credential stays valid for the whole of a test and reuse is
    /// decided by the KEY rather than by elapsed time.
    /// </summary>
    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// A clock the test moves, so the renewal boundary can be crossed deliberately rather than waited
    /// for.
    /// </summary>
    /// <remarks>
    /// Starts at the same instant as <see cref="FrozenClock"/> so the two fixtures are directly
    /// comparable, and every advance in a test is expressed relative to a token's own issuance or expiry
    /// rather than to this literal.
    /// </remarks>
    private sealed class MovableClock : TimeProvider
    {
        /// <summary>The current instant, which the test assigns.</summary>
        internal DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
