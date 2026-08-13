// ==================================================================================================
//  AuthenticationRefusalRecordTests - a refusal this service makes is a refusal an operator can read
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PINS
//
//  A measured probe of a running deployment issued three refusable requests against this service - no
//  credential, a forged credential, and an authenticated caller without the entitlement - and the
//  shipped logging profile produced ZERO operator records for all three. The boundary enforced
//  correctly and invisibly, so a credential-stuffing run against this ingress was indistinguishable in
//  the log from no traffic at all.
//
//  The framework does write such records, at Information under `Microsoft.AspNetCore.*`, and every
//  profile this repository ships caps that category at Warning - so those records are filtered out in
//  Production AND in Development. That is why the assertions below are about THIS SERVICE'S OWN records
//  under its own category: raising the framework cap would be a settings change a deployment can undo,
//  and it would buy three records at the price of admitting the whole framework's Information traffic.
//
//  THE SUITE ASSERTS FOUR SEPARABLE THINGS, and each has failed independently in review:
//    1. that a record exists at all, at a level every shipped profile admits
//    2. that there is EXACTLY ONE per refusal - the failure and challenge events both fire for one
//       refused token, so the obvious implementation double-counts every forged credential
//    3. that it carries the classifiers that make it actionable - decision, status, route, subject,
//       reason class, correlation
//    4. that it carries NO credential material, which is the constraint most easily lost when adding
//       fields to make (3) better
//
//  Driven through the deployed host in the PRODUCTION profile, deliberately: the finding is about what
//  the SHIPPED configuration records, so a test that raised a log level to observe the record would be
//  asserting a configuration nobody deploys.
//
//  NO TOKEN OR SECRET IN THIS FILE IS REAL.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Xunit;

namespace PowerFramework.Gateway.Tests;

/// <summary>
/// The refusal-record suite.
/// </summary>
public sealed class AuthenticationRefusalRecordTests
{
    /// <summary>The category this service's own refusal records are written under.</summary>
    private const string RefusalCategory =
        "PowerFramework.Gateway.Authorization.AuthenticationRefusal";

    /// <summary>The authenticated route used to provoke a refusal.</summary>
    private const string PingRoute = "/v1/ping";

    /// <summary>A route gated by a DIFFERENT scope, used to provoke an entitlement refusal.</summary>
    private const string CapabilitiesRoute = "/v1/capabilities";

    /// <summary>
    /// A request carrying no credential is refused 401 AND leaves exactly one own-code record naming the
    /// absence.
    /// </summary>
    /// <remarks>
    /// THE ORDINARY CASE, AND IT IS STILL WORTH A RECORD. A missing credential is usually a probe or a
    /// misconfigured client rather than an attack, which is exactly why the record must SAY it was
    /// missing: without that classifier a reader cannot separate this from the interesting case below,
    /// and the interesting case is buried in the volume of this one.
    /// </remarks>
    [Fact]
    public async Task ARequestWithNoCredentialIsRefusedAndRecorded()
    {
        (HttpStatusCode status, IReadOnlyList<CapturedRecord> records) =
            await ProbeAsync(PingRoute, credential: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);

        CapturedRecord record = Assert.Single(records);

        // EVERY SHIPPED PROFILE ADMITS WARNING. Information would have reproduced the finding.
        Assert.Equal(LogLevel.Warning, record.Level);

        Assert.Contains("Authentication was refused", record.Message, StringComparison.Ordinal);
        Assert.Contains("no credential was presented", record.Message, StringComparison.Ordinal);
        Assert.Contains("NoCredentialPresented", record.Message, StringComparison.Ordinal);
        Assert.Contains("401", record.Message, StringComparison.Ordinal);
        Assert.Contains(PingRoute, record.Message, StringComparison.Ordinal);

        // Nothing was presented, so nothing can be claimed.
        Assert.Contains("(absent)", record.Message, StringComparison.Ordinal);
        Assert.Contains("absent or empty", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A forged credential is refused 401, recorded ONCE, and classified as a presented credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THIS IS THE RECORD THAT MATTERS, AND THE ONE MOST EASILY DOUBLED.</b> A token that fails
    /// validation raises <c>OnAuthenticationFailed</c> AND THEN <c>OnChallenge</c>, so recording on both
    /// yields two records for one refusal - and every rate a reader computes from the log is then wrong
    /// by a factor that varies with the failure mode. <c>Assert.Single</c> is the whole point of this
    /// test, not incidental to it.
    /// </para>
    /// <para>
    /// IT IS ALSO THE RECORD THAT DISTINGUISHES AN ATTACK FROM NOISE: somebody held something they
    /// believed would work. The reason CLASS is asserted and the reason MESSAGE is asserted absent - see
    /// the disclosure test below.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AForgedCredentialIsRefusedAndRecordedExactlyOnce()
    {
        (HttpStatusCode status, IReadOnlyList<CapturedRecord> records) =
            await ProbeAsync(PingRoute, GatewayTestHostFixture.MalformedToken);

        Assert.Equal(HttpStatusCode.Unauthorized, status);

        CapturedRecord record = Assert.Single(records);

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("a presented credential was refused", record.Message, StringComparison.Ordinal);

        // The classifier separates "somebody tried something" from "nobody tried anything".
        Assert.DoesNotContain("no credential was presented", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An authenticated caller without the entitlement is refused 403 and recorded as an AUTHORIZATION
    /// refusal naming the policy that was demanded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE 403 RECORD IS A DIFFERENT STATEMENT FROM THE 401 RECORD and must not read like one. Here the
    /// signature, issuer, audience and lifetime all checked out - the caller IS who they say they are -
    /// so the subject on this record is VERIFIED, and a reader acting on it is acting on established
    /// identity rather than on a caller's assertion.
    /// </para>
    /// <para>
    /// THE SUBJECT ASSERTION IS ALSO THE REGRESSION GUARD FOR A MEASURED TRAP: <c>ForbiddenContext</c>
    /// exposes a <c>Principal</c> property that is NULL on this event, so the natural implementation
    /// reports an absent subject on every 403 - losing the one field that makes the record worth
    /// writing, while still emitting a record that looks correct.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthenticatedCallerLackingTheEntitlementIsRefusedAndRecorded()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        RecordingProvider provider = new();

        host.AdditionalServiceConfiguration.Add(services =>
            services.AddLogging(logging => logging.AddProvider(provider)));

        using HttpClient client = host.CreateClient();

        // Authenticated, but holding ONLY the ping scope - so the capabilities route refuses on
        // entitlement rather than on authentication.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            GatewayTestHostFixture.BearerScheme,
            host.IssueTokenWithScopes(["ping"]));

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(CapabilitiesRoute, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        CapturedRecord record = Assert.Single(provider.RecordsFor(RefusalCategory));

        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("Authorization was refused", record.Message, StringComparison.Ordinal);
        Assert.Contains("AUTHENTICATED caller", record.Message, StringComparison.Ordinal);
        Assert.Contains("403", record.Message, StringComparison.Ordinal);
        Assert.Contains(CapabilitiesRoute, record.Message, StringComparison.Ordinal);

        // THE POLICY THAT REFUSED, which in this estate is spelled identically to the scope - so naming
        // it tells the reader exactly which entitlement was missing.
        Assert.Contains("capabilities", record.Message, StringComparison.Ordinal);

        // THE VERIFIED SUBJECT - see the remarks on the null-Principal trap.
        Assert.Contains(GatewayTestHostFixture.TokenSubject, record.Message, StringComparison.Ordinal);

        // It held one scope, and the count is reported rather than the values.
        Assert.Contains("1 scope value(s)", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// No refusal record carries credential material, a header value, or a validation message.
    /// </summary>
    /// <param name="credential">The credential to present, or <see langword="null"/> to present none.</param>
    /// <remarks>
    /// <para>
    /// <b>THE CONSTRAINT MOST EASILY LOST WHILE MAKING A RECORD MORE USEFUL.</b> A refusal record is the
    /// single most dangerous place in the system to forget the redaction posture (AAP 0.6.6, C-F),
    /// because the thing being refused IS a credential and it is right there in the context object.
    /// </para>
    /// <para>
    /// THE VALIDATION MESSAGE IS ASSERTED ABSENT, NOT JUST THE TOKEN. Those messages quote the values
    /// they rejected - the audience comparison prints the audiences it compared - so an attacker
    /// choosing an audience would be choosing text written into this service's log. Only the exception's
    /// TYPE NAME may travel.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData(GatewayTestHostFixture.MalformedToken)]
    public async Task NoRefusalRecordCarriesCredentialMaterial(string? credential)
    {
        (_, IReadOnlyList<CapturedRecord> records) = await ProbeAsync(PingRoute, credential);

        CapturedRecord record = Assert.Single(records);

        if (credential is not null)
        {
            Assert.DoesNotContain(credential, record.Message, StringComparison.Ordinal);
        }

        // The scheme name would only appear as part of a copied header value.
        Assert.DoesNotContain("Bearer ", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization:", record.Message, StringComparison.Ordinal);

        // Token-validation diagnostics are IDX-prefixed. None may reach a record.
        Assert.DoesNotContain("IDX", record.Message, StringComparison.Ordinal);

        // A compact serialization's segment separator, which no classifier needs.
        Assert.DoesNotContain("eyJ", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal record carries the same correlation identifier the caller's problem body publishes.
    /// </summary>
    /// <remarks>
    /// WITHOUT THIS THE RECORD IS UNJOINABLE. A caller reporting "I got a 401 at 14:03" is only
    /// actionable if the operator can find the record for THAT request rather than for the other refusals
    /// in the same second, and the identifier is the only thing that distinguishes them.
    /// </remarks>
    [Fact]
    public async Task TheRefusalRecordCarriesACorrelationIdentifier()
    {
        (_, IReadOnlyList<CapturedRecord> records) = await ProbeAsync(PingRoute, credential: null);

        CapturedRecord record = Assert.Single(records);

        Assert.Contains("Correlation ", record.Message, StringComparison.Ordinal);

        // The W3C trace-context form the host stamps, so the record joins to a `traceId` rather than to
        // an internal request number.
        Assert.Contains("Correlation 00-", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The console formatter renders log scopes, which is what puts the trace context into a record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>THE OTHER HALF OF CORRELATION, AND THE HALF THAT WAS CONFIGURED AND THEN DISCARDED.</b> The
    /// composition root asks the logging factory to track TraceId, SpanId and ParentId, which places
    /// them in a log SCOPE - and the console formatter's default is to render no scopes at all. So the
    /// identifiers were assembled for every record and dropped before an operator could read them: a
    /// sweep of a running deployment found the caller's identifier in 39 Gateway records, all of which
    /// name it in their own message template, and in ZERO records on the two services behind it.
    /// </para>
    /// <para>
    /// ASSERTED ON THE OPTION RATHER THAN ON CONSOLE OUTPUT, deliberately. The console writer drains on
    /// a background queue, so an output assertion would be timing-dependent; the option is the whole of
    /// what the fix controls, and the end-to-end rendering is confirmed against a running container.
    /// </para>
    /// <para>
    /// 🔴 READ FROM THE UNNAMED INSTANCE, AND THE NAMED ONE IS A TRAP THIS TEST FELL INTO FIRST.
    /// <c>SimpleConsoleFormatterOptions</c> looks like named options - there is a formatter name, and the
    /// formatter is registered under it - but <c>AddSimpleConsole(configure)</c> applies the delegate to
    /// the DEFAULT unnamed instance, and <c>SimpleConsoleFormatter</c> reads <c>CurrentValue</c>, which is
    /// that same unnamed instance. Measured directly: with the fix in place, <c>CurrentValue</c> and
    /// <c>Get("")</c> both report true while <c>Get("simple")</c> reports FALSE. An assertion on the named
    /// instance therefore fails against correct code - and, worse, would have PASSED against a fix that
    /// configured the named instance and rendered nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheConsoleFormatterRendersScopesSoTheTraceContextReachesEveryRecord()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        // Force the host to build before its services are read.
        using HttpClient client = host.CreateClient();

        SimpleConsoleFormatterOptions formatter = host.Services
            .GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>()
            .CurrentValue;

        Assert.True(
            formatter.IncludeScopes,
            "The console formatter must render scopes, or the tracked trace context never reaches a "
                + "record and a caller's traceId cannot be joined to this service's log.");
    }

    /// <summary>
    /// The trace context this service tracks is exactly the identifying triple, and not caller-controlled
    /// key-value sets.
    /// </summary>
    /// <remarks>
    /// THE COMPANION CONSTRAINT TO RENDERING SCOPES, and it becomes load bearing BECAUSE they now
    /// render. Baggage and Tags are caller-controlled, so tracking either would copy attacker-influenced
    /// content into every log record - turning a correlation fix into a log-injection vector. Asserted
    /// so the two decisions cannot drift apart.
    /// </remarks>
    [Fact]
    public async Task OnlyTheIdentifyingTraceContextIsTrackedAndNotCallerControlledSets()
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        using HttpClient client = host.CreateClient();

        LoggerFactoryOptions tracking = host.Services
            .GetRequiredService<IOptions<LoggerFactoryOptions>>()
            .Value;

        Assert.Equal(
            ActivityTrackingOptions.TraceId
                | ActivityTrackingOptions.SpanId
                | ActivityTrackingOptions.ParentId,
            tracking.ActivityTrackingOptions);
    }

    /// <summary>
    /// Issues one request and returns its status with the refusal records it produced.
    /// </summary>
    /// <param name="route">The route to call.</param>
    /// <param name="credential">The bearer credential to present, or <see langword="null"/> for none.</param>
    /// <returns>The status and the records written under the refusal category.</returns>
    /// <remarks>
    /// THE PRODUCTION PROFILE, because the finding is about what the SHIPPED configuration records. A
    /// fixture that raised a log level would be asserting a configuration nobody deploys.
    /// </remarks>
    private static async Task<(HttpStatusCode Status, IReadOnlyList<CapturedRecord> Records)> ProbeAsync(
        string route,
        string? credential)
    {
        await using GatewayTestHostFixture host =
            GatewayTestHostFixture.ForEnvironment(Environments.Production);

        RecordingProvider provider = new();

        host.AdditionalServiceConfiguration.Add(services =>
            services.AddLogging(logging => logging.AddProvider(provider)));

        using HttpClient client = host.CreateClient();

        if (credential is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(GatewayTestHostFixture.BearerScheme, credential);
        }

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(route, UriKind.Relative),
            TestContext.Current.CancellationToken);

        return (response.StatusCode, provider.RecordsFor(RefusalCategory));
    }
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
/// THE CATEGORY IS CAPTURED, unlike the streaming suite's recorder, and that is what makes
/// <c>Assert.Single</c> meaningful here: a deployed host writes hundreds of records per request, so a
/// recorder that could not select by category could only assert "a record exists somewhere".
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
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            owner.Capture(new CapturedRecord(logLevel, category, formatter(state, exception)));
        }
    }
}
