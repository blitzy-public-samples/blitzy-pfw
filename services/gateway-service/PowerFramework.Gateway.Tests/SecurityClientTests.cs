// ==================================================================================================
//  SecurityClientTests - THE ERROR PATH OF GATEWAY'S OUTBOUND EDGE TO SECURITY
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.Gateway.Clients.SecurityClient
//            PowerFramework.Gateway.Clients.SecurityClientException
//
//  WHY THIS FILE EXISTS, AND WHY IT IS THE ERROR PATH RATHER THAN THE HAPPY ONE
//  ------------------------------------------------------------------------------------------------
//  An in-process call cannot fail in transit; a network call can. Handling the failure modes that
//  decomposition itself creates is required BY the transition rather than a robustness improvement
//  layered on top of it - and the failure mode this suite pins is the one that is invisible until it
//  happens, because it turns a clean refusal into an unrelated fault.
//
//  Security answers a refused token request with an RFC 9457 problem document, and this client reads
//  it so that the refusal reaches the caller TYPED: the status, the problem members, and the legacy
//  return code the contract carries as its single extension member. Reading a body can fail, and when
//  it does the client is specified to degrade to "no problem details" and still report the status -
//  because the status is the substantive answer and losing it behind a deserialization failure would
//  be strictly worse than losing the body.
//
//  THE THIRD EXCEPTION IS THE POINT OF THIS SUITE
//  ------------------------------------------------------------------------------------------------
//  Three distinct exceptions all mean "the body cannot be read", and only two of them are the obvious
//  ones. A response declaring `application/problem+json` with a character set the runtime cannot
//  resolve raises InvalidOperationException from the JSON reader - not JsonException, and not
//  NotSupportedException. A catch list carrying only the obvious two lets that escape, and a Security
//  4xx then surfaces to Gateway's caller as an unrelated unhandled 500: a refusal reported as a
//  server fault, with the status and the return code lost. The sibling client in DataServices already
//  absorbs all three and records that a test proved the list incomplete; this suite is that test on
//  this side of the boundary.
//
//  WHAT THIS SUITE DOES NOT DO
//  ------------------------------------------------------------------------------------------------
//  It starts no server and reaches no network. The client takes an HttpClient, so a stub message
//  handler is the whole test double - which is also what makes every branch of the error path
//  reachable without a Security instance running anywhere. No credential, key or certificate appears
//  in this file: the requests it makes are unauthenticated by construction, because the operation's
//  real authentication is a client certificate configured on the message handler in the composition
//  root, and nothing about that is under test here.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.Gateway.Clients;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The typed-refusal contract of <see cref="SecurityClient"/>, including the three distinct ways a
/// response body can be unreadable.
/// </summary>
public class SecurityClientTests
{
    /// <summary>The address the stubbed client is pointed at. Never dialled - the handler answers.</summary>
    private const string SecurityBaseAddress = "https://security.invalid";

    /// <summary>The subject the requests below claim. An identifier, never a credential.</summary>
    private const string Subject = "powerframework-gateway";

    /// <summary>The audience the requests below ask for.</summary>
    private const string Audience = "powerframework-dataservices";

    /// <summary>
    /// The requested scope set. One opaque protocol token, because the scope set is not the subject
    /// here and a longer one would only add rows to a cache key.
    /// </summary>
    private static readonly string[] Scopes = ["datawindow.read"];

    [Fact]
    public async Task AnUnresolvableResponseCharsetStillYieldsATypedRefusalCarryingTheStatus()
    {
        // THE REGRESSION THIS ASSERTION EXISTS FOR.
        //
        // `application/problem+json; charset=x-not-a-real-charset` is a JSON media type the reader
        // accepts and a character set it cannot resolve, so ReadFromJsonAsync raises
        // InvalidOperationException rather than JsonException or NotSupportedException. Absorbed, the
        // refusal arrives as a SecurityClientException carrying HTTP 400. Unabsorbed, the
        // InvalidOperationException escapes the client entirely and Gateway answers its own caller with
        // an unrelated 500 - the status, the problem members and the return code all lost.
        using HttpResponseMessage response = new(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"The requested audience is not permitted.","retCode":-3}""",
                Encoding.UTF8),
        };

        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/problem+json") { CharSet = "x-not-a-real-charset" };

        SecurityClient client = CreateClient(response);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest(Subject, Audience, Scopes),
                TestContext.Current.CancellationToken));

        // THE STATUS SURVIVES, WHICH IS THE SUBSTANTIVE ANSWER.
        Assert.Equal((int)HttpStatusCode.BadRequest, failure.StatusCode);

        // AND THE BODY DEGRADES TO ABSENT RATHER THAN TAKING THE STATUS DOWN WITH IT. The document was
        // well-formed JSON; it was simply undecodable under the declared character set, so the client
        // cannot honestly report its members and does not invent them.
        Assert.Null(failure.Title);
        Assert.Null(failure.Detail);
        Assert.Null(failure.RetCode);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"title": }""")]
    public async Task AMalformedBodyDegradesToNoProblemDetailsAndKeepsTheStatus(string body)
    {
        // THE TWO OBVIOUS MEMBERS OF THE SAME CONDITION, asserted beside the third so the suite records
        // that all three are one behaviour rather than three special cases. A body that is not JSON, and
        // a body that is JSON but truncated, both raise JsonException.
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        };

        SecurityClient client = CreateClient(response);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest(Subject, Audience, Scopes),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Null(failure.RetCode);
    }

    [Fact]
    public async Task AWellFormedProblemDocumentReachesTheCallerWithItsLegacyReturnCode()
    {
        // THE CONTROL, AND IT IS WHAT MAKES THE THREE ABSORPTIONS ABOVE MEANINGFUL. A catch list that
        // swallowed too much would pass every assertion above and fail this one: the members ARE read
        // when the body is readable, including the single extension member that carries the legacy
        // return-code algebra across the boundary.
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""
                      {"type":"about:blank","title":"Audience refused.","detail":"Not permitted.",
                       "retCode":{{RetCode.E_INVALID_ARGUMENT}}}
                      """),
                Encoding.UTF8,
                "application/problem+json"),
        };

        SecurityClient client = CreateClient(response);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest(Subject, Audience, Scopes),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.Forbidden, failure.StatusCode);
        Assert.Equal("Audience refused.", failure.Title);
        Assert.Equal("Not permitted.", failure.Detail);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, failure.RetCode);

        // The title reaches the message too, because an operator reading a log needs the reported
        // problem beside the status rather than in a separate record.
        Assert.Contains("Audience refused.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResponseWithNoBodyAtAllStillReportsItsStatus()
    {
        // A refusal carrying no document is legitimate - the contract does not require one - so the
        // media-type guard short-circuits before any read is attempted and the status is still reported.
        using HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain"),
        };

        SecurityClient client = CreateClient(response);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GetTokenAsync(
                new ServiceTokenRequest(Subject, Audience, Scopes),
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Null(failure.Title);
    }

    /// <summary>
    /// Builds a client whose every request is answered by <paramref name="response"/>.
    /// </summary>
    /// <param name="response">The single response the stub handler returns.</param>
    /// <returns>The client under test.</returns>
    /// <remarks>
    /// The logger is the null implementation and the clock is the system one: neither is the subject
    /// here, and substituting a recording logger would assert on message text that is not contract.
    /// </remarks>
    private static SecurityClient CreateClient(HttpResponseMessage response)
    {
        HttpClient httpClient = new(new SingleResponseHandler(response))
        {
            BaseAddress = new Uri(SecurityBaseAddress, UriKind.Absolute),
        };

        return new SecurityClient(
            httpClient,
            NullLogger<SecurityClient>.Instance,
            TimeProvider.System);
    }

    /// <summary>
    /// A message handler that answers every request with one prepared response.
    /// </summary>
    /// <param name="response">The response to return.</param>
    /// <remarks>
    /// The response is NOT disposed by this handler. Each test owns the response it built and disposes
    /// it through its own <c>using</c>, so disposing here would be a double dispose whose failure mode
    /// is an ObjectDisposedException from an unrelated line.
    /// </remarks>
    private sealed class SingleResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(response);
        }
    }
}
