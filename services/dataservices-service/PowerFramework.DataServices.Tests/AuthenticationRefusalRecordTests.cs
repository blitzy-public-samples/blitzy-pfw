// ==================================================================================================
//  AuthenticationRefusalRecordTests - a refusal this service makes is a refusal an operator can read
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//  A measured probe of a running deployment issued refusable requests against this service - no
//  credential, a forged credential, and an authenticated caller without the entitlement - and the
//  shipped logging profile produced ZERO operator records for any of them. The boundary enforced
//  correctly and invisibly, so a credential-stuffing run against this ingress was indistinguishable in
//  the log from no traffic at all.
//
//  The framework does write such records, at Information under `Microsoft.AspNetCore.*`, and every
//  profile this repository ships caps that category at Warning - so those records are filtered out in
//  Production AND in Development. That is why the assertions below are about THIS
//  SERVICE'S OWN records under its own category: raising the framework cap would be a settings change a
//  deployment can undo, and it would buy a handful of records at the price of admitting the whole
//  framework's Information traffic.
//
//  WHY THE 401 IS DRIVEN THROUGH THE HOST AND THE 403 THROUGH THE EVENT
//  An anonymous request reaches the STOCK bearer handler in this service's test host, so the 401 half is
//  asserted end to end against the deployed pipeline. An entitlement refusal needs a caller who
//  authenticated through that same handler AND lacks a scope, which this service's fixture cannot mint
//  without holding verification material it deliberately does not hold - so the 403 half is asserted by
//  invoking the handler's own forbid event directly, with the deployed 403 confirmed against a running
//  container instead. Both halves are proven; neither is assumed from the other.
//
//  NO TOKEN OR SECRET IN THIS FILE IS REAL.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Authorization;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The refusal-record suite.
/// </summary>
public sealed class AuthenticationRefusalRecordTests
{
    /// <summary>The category this service's own refusal records are written under.</summary>
    private const string RefusalCategory =
        "PowerFramework.DataServices.Authorization.AuthenticationRefusal";

    /// <summary>The authenticated route used to provoke an authentication refusal.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>The subject the forbid-event cases authenticate as.</summary>
    private const string VerifiedSubject = "powerframework-gateway";

    /// <summary>The scope-named policy the forbid-event cases are refused by.</summary>
    private const string DemandedPolicy = "dataservices.datawindow";

    /// <summary>
    /// An anonymous request is refused 401 by the deployed pipeline AND leaves exactly one own-code
    /// record naming the absence.
    /// </summary>
    /// <remarks>
    /// END TO END THROUGH THE STOCK BEARER HANDLER, which is what makes this evidence about the deployed
    /// boundary rather than about a substitute. A missing credential is usually a probe rather than an
    /// attack, which is exactly why the record must SAY it was missing: without that classifier a reader
    /// cannot separate it from a presented-and-refused credential, and the interesting case is then
    /// buried in the volume of the ordinary one.
    /// </remarks>
    [Fact]
    public async Task AnAnonymousRequestIsRefusedAndRecorded()
    {
        await using DataServicesTestHostFactory host = new();

        RecordingProvider provider = new();

        host.AdditionalServiceConfiguration.Add(services =>
            services.AddLogging(logging => logging.AddProvider(provider)));

        using HttpClient client = host.CreateAnonymousClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        CapturedRecord record = Assert.Single(provider.RecordsFor(RefusalCategory));

        // EVERY SHIPPED PROFILE ADMITS WARNING. Information would have reproduced the finding.
        Assert.Equal(LogLevel.Warning, record.Level);

        Assert.Contains("Authentication was refused", record.Message, StringComparison.Ordinal);
        Assert.Contains("no credential was presented", record.Message, StringComparison.Ordinal);
        Assert.Contains("NoCredentialPresented", record.Message, StringComparison.Ordinal);
        Assert.Contains("401", record.Message, StringComparison.Ordinal);
        Assert.Contains(PingRoute, record.Message, StringComparison.Ordinal);
        Assert.Contains("Correlation ", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A forged credential is refused 401 by the deployed pipeline and recorded EXACTLY ONCE.
    /// </summary>
    /// <remarks>
    /// <b>THE RECORD THAT MATTERS, AND THE ONE MOST EASILY DOUBLED.</b> A token that fails validation
    /// raises the failure event AND THEN the challenge event, so recording on both yields two records for
    /// one refusal - and every rate a reader computes from the log is then wrong by a factor that varies
    /// with the failure mode. <c>Assert.Single</c> is the point of this test, not incidental to it. The
    /// reason CLASS is asserted present and the validation MESSAGE asserted absent, because those
    /// messages quote the values they rejected.
    /// </remarks>
    [Fact]
    public async Task AForgedCredentialIsRefusedAndRecordedExactlyOnce()
    {
        await using DataServicesTestHostFactory host = new();

        RecordingProvider provider = new();

        host.AdditionalServiceConfiguration.Add(services =>
            services.AddLogging(logging => logging.AddProvider(provider)));

        using HttpClient client = host.CreateAnonymousClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "this-is-not-a-compact-serialization");

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(PingRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        CapturedRecord record = Assert.Single(provider.RecordsFor(RefusalCategory));

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("a presented credential was refused", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no credential was presented", record.Message, StringComparison.Ordinal);

        // NO CREDENTIAL MATERIAL AND NO VALIDATION MESSAGE.
        Assert.DoesNotContain(
            "this-is-not-a-compact-serialization",
            record.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain("IDX", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An authenticated caller lacking the entitlement leaves one record naming the VERIFIED subject and
    /// the policy that was demanded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A 403 IS A DIFFERENT STATEMENT FROM A 401 and must not read like one: the signature, issuer,
    /// audience and lifetime all checked out, so the subject here is established identity rather than a
    /// caller's assertion, and a reader may act on it.
    /// </para>
    /// <para>
    /// <b>THE SUBJECT ASSERTION IS THE REGRESSION GUARD FOR A MEASURED TRAP.</b>
    /// <see cref="ForbiddenContext"/> exposes a <c>Principal</c> property that is NULL on this event, so
    /// the natural implementation reports an absent subject on every 403 - losing the one field that
    /// makes the record worth writing, while still emitting a record that looks correct. The principal
    /// below is set ONLY on <see cref="HttpContext.User"/>, so a reversion to the context property fails
    /// this test.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedCallerLackingTheEntitlementIsRecordedWithItsVerifiedSubject()
    {
        RecordingProvider provider = new();

        JwtBearerOptions bearer = new();

        AuthenticationRefusalRecord.Attach(bearer);

        HttpContext context = NewForbiddenRequest(provider);

        await bearer.Events!.OnForbidden(new ForbiddenContext(
            context,
            NewScheme(),
            bearer));

        CapturedRecord record = Assert.Single(provider.RecordsFor(RefusalCategory));

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("Authorization was refused", record.Message, StringComparison.Ordinal);
        Assert.Contains("AUTHENTICATED caller", record.Message, StringComparison.Ordinal);
        Assert.Contains("403", record.Message, StringComparison.Ordinal);
        Assert.Contains(PingRoute, record.Message, StringComparison.Ordinal);

        // The policy that refused, which in this estate is spelled identically to the scope.
        Assert.Contains(DemandedPolicy, record.Message, StringComparison.Ordinal);

        // The VERIFIED subject - see the null-Principal trap in the remarks.
        Assert.Contains(VerifiedSubject, record.Message, StringComparison.Ordinal);

        // The held scopes are COUNTED, never listed.
        Assert.Contains("2 scope value(s)", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("held-scope-one", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A subject arriving under the framework's REMAPPED claim spelling still reaches the record.
    /// </summary>
    /// <remarks>
    /// 🔴 THE THREE SERVICES SHARING THIS RECORD DO NOT AGREE ON THE SPELLING. Gateway, DataServices and
    /// Security set <c>MapInboundClaims</c> to false, so a subject arrives as <c>sub</c>; Persistence
    /// assigns it nowhere and takes the framework default, which remaps <c>sub</c> onto a long SOAP-era
    /// URI. A record reading only the raw spelling reports an absent subject on the remapping service -
    /// silently, because an absent claim is a legitimate outcome elsewhere and so reads as correct.
    /// </remarks>
    [Fact]
    public async Task ARemappedSubjectClaimStillReachesTheRecord()
    {
        RecordingProvider provider = new();

        JwtBearerOptions bearer = new();

        AuthenticationRefusalRecord.Attach(bearer);

        HttpContext context = NewForbiddenRequest(provider, useMappedClaimSpelling: true);

        await bearer.Events!.OnForbidden(new ForbiddenContext(context, NewScheme(), bearer));

        CapturedRecord record = Assert.Single(provider.RecordsFor(RefusalCategory));

        Assert.Contains(VerifiedSubject, record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Verified subject (absent)", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The console formatter renders log scopes, which is what puts the trace context into a record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE OTHER HALF OF CORRELATION, AND THE HALF THAT WAS CONFIGURED AND THEN DISCARDED.</b> The
    /// composition root asks the logging factory to track TraceId, SpanId and ParentId, which places them
    /// in a log SCOPE - and the console formatter's default is to render no scopes at all. So the
    /// identifiers were assembled for every record and dropped before an operator could read them: a
    /// sweep of a running deployment found the caller's identifier in 39 Gateway records, all of which
    /// name it in their own message template, and in ZERO records on this service.
    /// </para>
    /// <para>
    /// 🔴 READ FROM THE UNNAMED INSTANCE, AND THE NAMED ONE IS A TRAP. <c>AddSimpleConsole(configure)</c>
    /// applies its delegate to the DEFAULT unnamed options instance, and the formatter reads
    /// <c>CurrentValue</c> - that same instance. Measured directly: with the fix in place
    /// <c>CurrentValue</c> reports true while <c>Get("simple")</c> reports FALSE. An assertion on the
    /// named instance fails against correct code, and would PASS against a fix that configured the named
    /// instance and rendered nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheConsoleFormatterRendersScopesSoTheTraceContextReachesEveryRecord()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAnonymousClient();

        SimpleConsoleFormatterOptions formatter = host.Services
            .GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>()
            .CurrentValue;

        Assert.True(
            formatter.IncludeScopes,
            "The console formatter must render scopes, or the tracked trace context never reaches a "
                + "record and a caller's traceId cannot be joined to this service's log.");
    }

    /// <summary>
    /// The trace context tracked is exactly the identifying triple, and not caller-controlled key-value
    /// sets.
    /// </summary>
    /// <remarks>
    /// THE COMPANION CONSTRAINT TO RENDERING SCOPES, and it becomes load bearing BECAUSE they now render.
    /// Baggage and Tags are caller-controlled, so tracking either would copy attacker-influenced content
    /// into every log record - turning a correlation fix into a log-injection vector.
    /// </remarks>
    [Fact]
    public async Task OnlyTheIdentifyingTraceContextIsTrackedAndNotCallerControlledSets()
    {
        await using DataServicesTestHostFactory host = new();

        using HttpClient client = host.CreateAnonymousClient();

        LoggerFactoryOptions tracking = host.Services
            .GetRequiredService<IOptions<LoggerFactoryOptions>>()
            .Value;

        Assert.Equal(
            ActivityTrackingOptions.TraceId
                | ActivityTrackingOptions.SpanId
                | ActivityTrackingOptions.ParentId,
            tracking.ActivityTrackingOptions);
    }

    /// <summary>Builds a request that has authenticated but is about to be forbidden.</summary>
    /// <param name="provider">The provider the record is captured onto.</param>
    /// <param name="useMappedClaimSpelling">
    /// When true the subject is carried under the framework's remapped claim type instead of the raw JWT
    /// name, reproducing a service that leaves claim mapping on.
    /// </param>
    /// <returns>The request.</returns>
    /// <remarks>
    /// A REAL ROUTED ENDPOINT CARRYING REAL AUTHORIZATION METADATA, because the record reads the route
    /// PATTERN and the demanded POLICY from endpoint metadata - without them this would assert the
    /// unrouted placeholder and an absent policy, which is to say it would assert nothing.
    /// </remarks>
    private static HttpContext NewForbiddenRequest(
        RecordingProvider provider,
        bool useMappedClaimSpelling = false)
    {
        ServiceCollection services = new();

        _ = services.AddLogging(logging => logging.AddProvider(provider));

        DefaultHttpContext context = new() { RequestServices = services.BuildServiceProvider() };

        context.Request.Method = HttpMethods.Get;
        context.Request.Path = PingRoute;

        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    useMappedClaimSpelling ? ClaimTypes.NameIdentifier : "sub",
                    VerifiedSubject),
                new Claim("aud", "an-audience"),
                new Claim("scope", "held-scope-one held-scope-two"),
            ],
            authenticationType: "Bearer"));

        context.SetEndpoint(new RouteEndpoint(
            static _ => Task.CompletedTask,
            RoutePatternFactory.Parse(PingRoute),
            order: 0,
            new EndpointMetadataCollection(new AuthorizeAttribute(DemandedPolicy)),
            PingRoute));

        return context;
    }

    /// <summary>Builds the scheme descriptor the event contexts require.</summary>
    /// <returns>The scheme.</returns>
    private static AuthenticationScheme NewScheme() => new(
        JwtBearerDefaults.AuthenticationScheme,
        displayName: null,
        handlerType: typeof(JwtBearerHandler));
}

/// <summary>One captured record: the level, the rendered message and the category it was written under.</summary>
/// <param name="Level">The level.</param>
/// <param name="Category">The logger category.</param>
/// <param name="Message">The rendered message.</param>
internal readonly record struct CapturedRecord(LogLevel Level, string Category, string Message);

/// <summary>
/// A logger provider that captures every record with its category, so a test can assert on ONE category's
/// records without the rest of the host's traffic.
/// </summary>
/// <remarks>
/// THE CATEGORY IS CAPTURED, and that is what makes <c>Assert.Single</c> meaningful: a deployed host
/// writes hundreds of records per request, so a recorder that could not select by category could only
/// assert "a record exists somewhere".
/// </remarks>
internal sealed class RecordingProvider : ILoggerProvider
{
    private readonly List<CapturedRecord> _records = [];

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    /// <summary>The records written under one category, in order.</summary>
    /// <param name="category">The category to select.</param>
    /// <returns>The matching records.</returns>
    internal IReadOnlyList<CapturedRecord> RecordsFor(string category)
    {
        lock (_records)
        {
            return [.. _records.Where(record =>
                string.Equals(record.Category, category, StringComparison.Ordinal))];
        }
    }

    /// <summary>Captures one record.</summary>
    /// <param name="record">The record.</param>
    private void Capture(CapturedRecord record)
    {
        lock (_records)
        {
            _records.Add(record);
        }
    }

    /// <summary>The logger every category resolves to.</summary>
    /// <param name="owner">The provider to capture onto.</param>
    /// <param name="category">The category this logger writes under.</param>
    private sealed class CapturingLogger(RecordingProvider owner, string category) : ILogger
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            owner.Capture(new CapturedRecord(logLevel, category, formatter(state, exception)));
        }
    }
}
