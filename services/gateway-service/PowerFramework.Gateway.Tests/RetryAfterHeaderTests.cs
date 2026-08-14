// =====================================================================================================
//  Retry-After ON A CAPACITY REFUSAL
// =====================================================================================================
//
//  WHY THIS FILE EXISTS. A 429 tells a caller that a ceiling was reached; Retry-After is the only part of
//  that answer which tells them WHEN to come back. It is a HEADER, so no amount of prose in the problem
//  document substitutes for it, and a document-level assertion cannot see it - which is precisely why both
//  429 paths shipped without one and no existing test noticed. The subject here is therefore the method
//  that BUILDS the document rather than a body: BuildProblem is the single choke point all four
//  problem-answering paths reach, and the header is applied there.
//
//  THE ABSENCE ROWS CARRY AS MUCH WEIGHT AS THE PRESENCE ROW. RFC 9110 10.2.3 permits the header on a 503
//  as well, and this gateway deliberately withholds it there: a 503 means an upstream is unreachable and
//  nothing in this system knows when it returns, so a delta would be a fabricated availability promise -
//  which AAP 0.8.5 forbids this refactor from asserting anywhere. A test that only checked the 429 would
//  leave a later "helpful" broadening of the condition undetected.
// =====================================================================================================

using System.Globalization;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Endpoints;
using Xunit;
using RetCode = PowerFramework.Shared.Kernel.RetCode;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The <c>Retry-After</c> header the REST projection attaches to a capacity refusal.
/// </summary>
public sealed class RetryAfterHeaderTests
{
    /// <summary>The header name, spelled out rather than taken from the helper under test.</summary>
    /// <remarks>
    /// A test that read the name from the same constant the production code reads would pass if the name
    /// itself were wrong. The wire spelling is the assertion, so it is written here literally.
    /// </remarks>
    private const string HeaderName = "Retry-After";

    /// <summary>
    /// A capacity refusal carries the configured delta, whole seconds, as a header.
    /// </summary>
    [Fact]
    public void ACapacityRefusalCarriesTheConfiguredDelta()
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(5));

        _ = DataServicesProxyEndpoints.BuildProblem(context, Refusal(StatusCodes.Status429TooManyRequests));

        Assert.Equal("5", Assert.Single(context.Response.Headers[HeaderName].ToArray()));
    }

    /// <summary>
    /// A sub-second configured value never renders as zero.
    /// </summary>
    /// <remarks>
    /// <b>THE ROUNDING DIRECTION IS THE ASSERTION.</b> <c>Retry-After: 0</c> reads as "retry immediately",
    /// which is the one answer a capacity refusal must not give - a caller obeying it hammers the ceiling it
    /// was just told about. Truncation produces exactly that from any value under a second, so the delta is
    /// rounded UP and this row is what keeps it that way.
    /// </remarks>
    [Theory]
    [InlineData(1, "1")]
    [InlineData(250, "1")]
    [InlineData(999, "1")]
    [InlineData(1_000, "1")]
    [InlineData(1_001, "2")]
    [InlineData(4_500, "5")]
    [InlineData(30_000, "30")]
    public void ASubSecondConfiguredValueRoundsUpAndNeverRendersZero(int configuredMilliseconds, string expected)
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromMilliseconds(configuredMilliseconds));

        _ = DataServicesProxyEndpoints.BuildProblem(context, Refusal(StatusCodes.Status429TooManyRequests));

        Assert.Equal(expected, Assert.Single(context.Response.Headers[HeaderName].ToArray()));
    }

    /// <summary>
    /// No status other than a capacity refusal carries the header.
    /// </summary>
    /// <param name="httpStatus">The status being answered.</param>
    /// <remarks>
    /// <para>
    /// 503 IS THE ROW THAT MATTERS. It is the status RFC 9110 pairs with 429 as the other legitimate place
    /// for the header, and it is withheld here on purpose: this gateway answers 503 when an upstream is
    /// unreachable, and it has no knowledge of when an unreachable service returns. A delta there would be
    /// an availability commitment, and the plan states plainly that none may be asserted (AAP 0.8.5).
    /// </para>
    /// <para>
    /// The remaining rows are the other statuses the projection produces, so that a future change which
    /// widened the condition to "any 4xx" or "any 5xx" fails here rather than in production. <c>501</c> is
    /// the one row this projection no longer produces at all - it belongs to Gateway's four reserved
    /// deferred-capability routes alone, which build their own body rather than a problem document - and the
    /// row is kept deliberately: it is the assertion that a status arriving here by any future route still
    /// carries no delta, since a reserved route clears on no schedule this service can know.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status501NotImplemented)]
    [InlineData(StatusCodes.Status502BadGateway)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    [InlineData(StatusCodes.Status504GatewayTimeout)]
    public void NoOtherStatusCarriesTheHeader(int httpStatus)
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(5));

        _ = DataServicesProxyEndpoints.BuildProblem(context, Refusal(httpStatus));

        Assert.False(
            context.Response.Headers.ContainsKey(HeaderName),
            $"Status {httpStatus} carried a {HeaderName} header. Only a capacity refusal may, because only "
                + "a ceiling clears on a knowable schedule - see ApplyRetryAfter.");
    }

    /// <summary>
    /// A second pass over the same response replaces the delta rather than adding a second one.
    /// </summary>
    /// <remarks>
    /// Two conflicting deltas on one response is worse than none: a caller has no rule for choosing between
    /// them. The header is therefore SET, and this row proves it - it also covers a value planted by a
    /// middleware ahead of this projection.
    /// </remarks>
    [Fact]
    public void ASecondPassReplacesTheDeltaRatherThanAppending()
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(7));

        context.Response.Headers[HeaderName] = "120";

        _ = DataServicesProxyEndpoints.BuildProblem(context, Refusal(StatusCodes.Status429TooManyRequests));

        Assert.Equal("7", Assert.Single(context.Response.Headers[HeaderName].ToArray()));
    }

    /// <summary>
    /// A response whose headers are already on the wire is left untouched.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS A SAFETY ROW, NOT AN ERGONOMICS ONE.</b> Setting a header after the status line has been
    /// sent throws, and the one route that can fail after committing its status is the streamed retrieval -
    /// so a projection without this guard would replace a legible failure with an exception raised inside a
    /// failure path. The document is still built; only the header is skipped.
    /// </remarks>
    [Fact]
    public void AResponseAlreadyOnTheWireIsLeftUntouched()
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(5));
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        ProblemDetails problem =
            DataServicesProxyEndpoints.BuildProblem(context, Refusal(StatusCodes.Status429TooManyRequests));

        Assert.False(
            context.Response.Headers.ContainsKey(HeaderName),
            "The header was set on a response that had already started, which throws in a real host.");

        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
    }

    /// <summary>
    /// The header the shipped configuration produces is the documented default.
    /// </summary>
    /// <remarks>
    /// The default is what an unconfigured deployment answers with, so it is asserted through the options
    /// type's own default rather than a literal repeated from it.
    /// </remarks>
    [Fact]
    public void TheShippedDefaultProducesTheDocumentedDelta()
    {
        DefaultHttpContext context = NewContext(configured: null);

        _ = DataServicesProxyEndpoints.BuildProblem(context, Refusal(StatusCodes.Status429TooManyRequests));

        string expected = ((long)Math.Ceiling(RestProjectionOptions.DefaultRetryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        Assert.Equal(expected, Assert.Single(context.Response.Headers[HeaderName].ToArray()));
    }

    /// <summary>
    /// An interval the UPSTREAM stated is preferred over this gateway's configured delta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO NUMBERS ARE DELIBERATELY DIFFERENT AND THE UPSTREAM'S IS THE LARGER, which is the case that
    /// matters: substituting the shorter configured delta would send the caller back before the upstream's
    /// window has replenished, into a refusal the upstream had already told it how to avoid (issue INFO-3).
    /// </para>
    /// <para>
    /// Only an upstream INGRESS refusal states an interval. A session or handle ceiling answered in band as
    /// <c>E_BUSY</c> states none - it clears when something is released rather than on a schedule - and the
    /// rows above prove that case still answers with the configured delta.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUpstreamStatedIntervalIsPreferredOverTheConfiguredDelta()
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(5));

        _ = DataServicesProxyEndpoints.BuildProblem(
            context,
            Refusal(StatusCodes.Status429TooManyRequests) with { RetryAfterSeconds = 60L });

        Assert.Equal("60", Assert.Single(context.Response.Headers[HeaderName].ToArray()));
    }

    /// <summary>
    /// A status other than a capacity refusal carries no delta even when an upstream stated one.
    /// </summary>
    /// <remarks>
    /// The condition on the header is the STATUS, not the availability of a number. A 503 carrying an
    /// upstream interval would be a fabricated availability promise - the thing AAP 0.8.5 forbids this
    /// refactor from asserting - so the presence of a stated interval must not widen the condition.
    /// </remarks>
    [Theory]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public void AStatedIntervalDoesNotWidenTheConditionToOtherStatuses(int httpStatus)
    {
        DefaultHttpContext context = NewContext(TimeSpan.FromSeconds(5));

        _ = DataServicesProxyEndpoints.BuildProblem(
            context,
            Refusal(httpStatus) with { RetryAfterSeconds = 30L });

        Assert.False(
            context.Response.Headers.ContainsKey(HeaderName),
            $"Status {httpStatus} carried a {HeaderName} header because an upstream stated an interval. The "
                + "condition is the status, not the availability of a number - see ApplyRetryAfter.");
    }

    /// <summary>
    /// The trailer is read rather than trusted: only a positive whole number is accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>"0"</c> IS THE ROW THAT MATTERS. Relaying it would tell a caller to retry immediately, which is
    /// the one answer a capacity refusal must not give - and it would do so while looking like a considered
    /// value. Every rejected shape falls back to the configured delta rather than to no header at all.
    /// </para>
    /// <para>
    /// The leading-space case is not pedantry: the parse admits no whitespace and no sign, so a producer
    /// that formatted the value loosely is treated as having stated nothing rather than being guessed at.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("60", 60L)]
    [InlineData("1", 1L)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("", null)]
    [InlineData("soon", null)]
    [InlineData(" 60", null)]
    [InlineData("60.5", null)]
    public void OnlyAPositiveWholeNumberOfSecondsIsAcceptedFromAnUpstream(string stated, long? expected)
    {
        Metadata trailers = [];

        trailers.Add(DataServicesProxyEndpoints.UpstreamRetryAfterTrailer, stated);

        RpcException failure = new(
            new Status(StatusCode.ResourceExhausted, "An ingress bound was met."),
            trailers);

        Assert.Equal(expected, DataServicesProxyEndpoints.UpstreamRetryAfterSeconds(failure));
    }

    /// <summary>
    /// A refusal that states nothing reads as nothing, so the configured delta stands.
    /// </summary>
    /// <remarks>
    /// This is the upstream CONCURRENCY-link refusal and every pre-existing producer: no trailer at all.
    /// Reading it must answer null rather than raising, because that path is the common one.
    /// </remarks>
    [Fact]
    public void ARefusalCarryingNoTrailerStatesNoInterval()
    {
        RpcException failure = new(new Status(StatusCode.ResourceExhausted, "An ingress bound was met."));

        Assert.Null(DataServicesProxyEndpoints.UpstreamRetryAfterSeconds(failure));
    }

    /// <summary>
    /// The trailer name is the wire spelling both upstreams use.
    /// </summary>
    /// <remarks>
    /// Written literally rather than read from the upstream projects, which this service cannot reference at
    /// all (C-A): only the published contract crosses a service boundary, so the spelling is the agreement
    /// and a rename on either side has to fail here rather than degrade this header back to the configured
    /// delta in silence.
    /// </remarks>
    [Fact]
    public void TheTrailerNameIsTheWireSpellingBothUpstreamsUse()
    {
        Assert.Equal("retry-after", DataServicesProxyEndpoints.UpstreamRetryAfterTrailer);
    }

    /// <summary>Builds a projection carrying the given status.</summary>
    /// <param name="httpStatus">The status.</param>
    /// <returns>The projection.</returns>
    /// <remarks>
    /// The return code travels with it because the document declares one on every failure; which code is
    /// immaterial to the header, so the busy code is used for legibility.
    /// </remarks>
    private static DataServicesProxyEndpoints.StatusProjection Refusal(int httpStatus) => new(
        httpStatus,
        RetCode.E_BUSY,
        "A refusal, for the header assertion.",
        FromUpstream: false);

    /// <summary>Builds a context whose services carry the configured delta.</summary>
    /// <param name="configured">The delta to configure, or <see langword="null"/> for the shipped default.</param>
    /// <returns>The context.</returns>
    private static DefaultHttpContext NewContext(TimeSpan? configured)
    {
        ServiceCollection services = new();
        _ = services.AddLogging();

        _ = services.Configure<GatewayOptions>(options =>
        {
            if (configured is { } delta)
            {
                options.RestProjection.RetryAfter = delta;
            }
        });

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/v1/datawindow/sessions";

        return context;
    }

    /// <summary>A response feature reporting that the status line has already been sent.</summary>
    /// <remarks>
    /// <see cref="DefaultHttpContext"/>'s own feature always reports not-started, and there is no setter -
    /// so the only way to exercise the guard is to substitute a feature that says otherwise. Everything
    /// else delegates to a plain in-memory implementation.
    /// </remarks>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        /// <inheritdoc/>
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        /// <inheritdoc/>
        public string? ReasonPhrase { get; set; }

        /// <inheritdoc/>
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        /// <inheritdoc/>
        public Stream Body { get; set; } = Stream.Null;

        /// <inheritdoc/>
        public bool HasStarted => true;

        /// <inheritdoc/>
        public void OnStarting(Func<object, Task> callback, object state)
        {
            // Nothing observes the callback in this test, and a real host would already have run it - the
            // response has started. An empty body here is the accurate behaviour, not a stub.
        }

        /// <inheritdoc/>
        public void OnCompleted(Func<object, Task> callback, object state)
        {
            // As above: completion callbacks are not part of what this test observes.
        }
    }
}
