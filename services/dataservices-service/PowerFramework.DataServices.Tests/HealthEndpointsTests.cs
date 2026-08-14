// =================================================================================================
//  HealthEndpointsTests - the anonymous readiness probe, exercised over a real HTTP pipeline.
//
//  WHY A TEST HOST AND NOT A DIRECT CALL. Endpoints/HealthEndpoints.cs exposes exactly one public
//  member, MapHealthEndpoints, and everything this suite needs to observe is a property of the
//  RESPONSE rather than of a method's return value: the STATUS CODE the orchestration readiness gate
//  reads, the anonymity the gate depends on, and the exact member set an anonymous body is allowed to
//  carry. A direct invocation of the handler could observe none of the three, because the handler is
//  private by design and its IResult has to be executed to become a status and a body.
//
//  Microsoft.AspNetCore.Mvc.Testing brings Microsoft.AspNetCore.TestHost with it, so a host is built
//  here in the test rather than through the service's own composition root - which does not exist at
//  this checkpoint and is owned elsewhere. That is not a workaround: the endpoint file's own decision
//  record item 5 states that Program.cs owns registration and that this probe must answer whether or
//  not any health check was registered, and building the host locally is what lets both halves of that
//  be exercised - the registered case and the unregistered one - from one suite.
//
//  WHAT EACH ROW EXISTS TO PROTECT.
//    * ONLY Healthy IS 200. Degraded means NOT READY, and what an orchestrator observes is the status
//      code, so a 200 for Degraded would satisfy `depends_on: condition: service_healthy` on a service
//      that had just said it was not usable - and Gateway would start behind an upstream that was not
//      ready, while every document still claimed the gate held. That is the single most important
//      readiness property in the orchestration, and it is a status-code property.
//    * THE ANONYMOUS BODY DISCLOSES NOTHING A REGISTRATION SUPPLIED. This endpoint is world-readable
//      by anything that can reach the port. A check name or description authored by whatever
//      registered the check - a provider name, a host, a URL, a database identifier, an exception
//      summary - would be published to all of it. The vocabulary is closed in the endpoint file, and
//      these rows are what keep it closed.
//    * THE COUNT OF REGISTERED CHECKS IS ITSELF DISCLOSURE. One entry per check, even anonymised,
//      publishes the shape of this service's dependency graph, so the aggregation is asserted rather
//      than assumed.
// =================================================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerFramework.DataServices.Endpoints;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class HealthEndpointsTests
{
    /// <summary>The two component identifiers the anonymous body is permitted to name.</summary>
    private static readonly string[] PermittedComponentNames = ["self", "components"];

    /// <summary>
    /// Builds a host that maps the probe and registers the supplied component checks, then returns a
    /// client for it.
    /// </summary>
    /// <param name="checks">
    /// One entry per component check to register: its name, its verdict, and the description it
    /// reports. Both the name and the description are deliberately hostile - they carry the kind of
    /// internal detail a real registration routinely leaks - so that a projection which echoed either
    /// would fail these rows rather than pass them.
    /// </param>
    /// <returns>The host and a client bound to it. The caller disposes both.</returns>
    private static async Task<(IHost Host, HttpClient Client)> StartAsync(
        params (string Name, HealthStatus Status, string Description)[] checks)
        => await StartCoreAsync(registerHealthCheckService: checks.Length > 0, channel: null, checks);

    /// <summary>
    /// Builds the host, with the two axes the rows below need to vary independently.
    /// </summary>
    /// <param name="registerHealthCheckService">
    /// Whether <c>AddHealthChecks</c> is called at all. This is a SEPARATE axis from the check list,
    /// because registering the service and registering a check are separate acts and the endpoint
    /// distinguishes them: with no service it reports its own liveness, and with a service but no
    /// check it aggregates an empty report. Collapsing the two would leave the empty-report arm
    /// unreachable from any test.
    /// </param>
    /// <param name="channel">
    /// When supplied, receives every record written to the operator channel together with the level
    /// each was written at, and forces the level filter low enough that the trace-level healthy record
    /// is actually emitted. Both halves are needed: the endpoint guards that record behind
    /// <c>IsEnabled</c>, and this project's copied <c>appsettings.json</c> pins the default category to
    /// Information - a configuration RULE, which beats <c>SetMinimumLevel</c>, so the rule set is
    /// cleared rather than merely lowered.
    /// </param>
    /// <param name="checks">The component checks to register, if any.</param>
    /// <returns>The host and a client bound to it. The caller disposes both.</returns>
    private static async Task<(IHost Host, HttpClient Client)> StartCoreAsync(
        bool registerHealthCheckService,
        OperatorChannel? channel,
        params (string Name, HealthStatus Status, string Description)[] checks)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        if (channel is not null)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new RecordingLoggerProvider(channel));
            builder.Logging.SetMinimumLevel(LogLevel.Trace);

            // The configuration-bound rules are cleared rather than overridden. A rule sourced from
            // appsettings wins over SetMinimumLevel, and this project's copy pins the default category
            // to Information - which would suppress the very record these rows exist to observe and
            // would make them pass by seeing nothing.
            builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
            {
                options.Rules.Clear();
                options.MinLevel = LogLevel.Trace;
            });
        }

        if (registerHealthCheckService)
        {
            IHealthChecksBuilder health = builder.Services.AddHealthChecks();

            foreach ((string name, HealthStatus status, string description) in checks)
            {
                health.AddCheck(name, () => new HealthCheckResult(status, description));
            }
        }

        WebApplication app = builder.Build();
        app.MapHealthEndpoints();

        await app.StartAsync(TestContext.Current.CancellationToken);

        return (app, app.GetTestClient());
    }

    private static async Task<(HttpStatusCode Status, JsonDocument Body)> ProbeAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (response.StatusCode, body);
    }

    // ==============================================================================================
    //  THE STATUS-CODE MAPPING - the readiness gate's whole contract with this endpoint
    // ==============================================================================================

    [Fact]
    public async Task AFullyReadyServiceAnswersTwoHundred()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("primary-store", HealthStatus.Healthy, "connected"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
                Assert.Equal("dataservices", body.RootElement.GetProperty("service").GetString());
            }
        }
    }

    [Theory]
    [InlineData(HealthStatus.Degraded, "Degraded")]
    [InlineData(HealthStatus.Unhealthy, "Unhealthy")]
    public async Task ANotReadyServiceAnswersFiveOhThreeForBothNotReadyVerdicts(
        HealthStatus registered,
        string expectedWireStatus)
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("primary-store", registered, "the connection pool is still warming"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                // THE ROW THAT MATTERS MOST IN THIS FILE. Degraded answering 200 would let
                // `depends_on: condition: service_healthy` open on a service that had just reported
                // itself not ready, so Gateway would start behind an unusable upstream while the
                // readiness gate appeared to be working. The verdict is still distinguishable - it is
                // carried in the body, which is asserted immediately below.
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                // AND THE THREE-TOKEN VOCABULARY SURVIVES, carried in the problem detail on this path.
                // Collapsing Degraded into Unhealthy would lose the distinction contract C-10 requires
                // between a service still completing startup validation and one whose dependency has
                // failed.
                string? detail = body.RootElement.GetProperty("detail").GetString();

                Assert.NotNull(detail);
                Assert.Contains(expectedWireStatus, detail!, StringComparison.Ordinal);

                // The legacy return code the authored contract requires in the single permitted
                // extension member. E_RETRY is "the operation should be retried", which is the legacy
                // vocabulary's match for Service Unavailable.
                Assert.Equal(
                    PowerFramework.Shared.Kernel.RetCode.E_RETRY,
                    body.RootElement.GetProperty("retCode").GetInt64());

                // AND THE VERDICT IS MACHINE-READABLE, WHICH IS THE HALF THAT ACTUALLY REACHES GATEWAY.
                // The detail above is prose for a human, and prose is not something an aggregator can
                // branch on; retCode is identical for both verdicts, so it separates nothing. Without a
                // member of its own the distinction contract C-10 promises would exist only in a sentence,
                // and Gateway would have to flatten every not-ready upstream into a failure. The token
                // cannot live in `status` - RFC 9457 uses that name here for the integer HTTP status,
                // asserted alongside so the two are visibly different members.
                Assert.Equal(
                    expectedWireStatus,
                    body.RootElement.GetProperty("serviceStatus").GetString());
                Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
            }
        }
    }

    [Fact]
    public async Task AServiceWithNoRegisteredCheckIsReadyOnTheStrengthOfAnsweringAtAll()
    {
        // Decision record item 5: the probe must answer whether or not Program.cs registered anything,
        // because binding this endpoint's liveness to a registration it does not own would turn a probe
        // into a startup gate. With nothing registered, the whole of a leaf service's readiness is that
        // its process is running and answering - which executing this request has just demonstrated.
        (IHost host, HttpClient client) = await StartAsync();

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

                JsonElement checks = body.RootElement.GetProperty("checks");
                JsonElement single = Assert.Single(checks.EnumerateArray().ToArray());

                Assert.Equal("self", single.GetProperty("name").GetString());
            }
        }
    }

    // ==============================================================================================
    //  THE DISCLOSURE BOUNDARY - what an anonymous body may and may not carry
    // ==============================================================================================

    [Fact]
    public async Task NoRegistrationSuppliedNameOrDescriptionReachesTheAnonymousBody()
    {
        // BOTH STRINGS BELOW ARE THE FAILURE MODE, WRITTEN OUT. A registration names its check and
        // describes it for its own operator, not for an anonymous reader, and the contract's request
        // that authors keep those free of internal detail is a rule on THEM - not a control this
        // service can enforce. So the check name carries a host and a region, and the description
        // carries a connection string, an address and a file path. If either is echoed, this row fails.
        const string LeakyName = "npgsql-primary.db.internal.eu-west-1";
        const string LeakyDescription =
            "Host=primary.db.internal;Port=5432;Username=svc_dataservices;Password=hunter2 "
            + "(see /var/lib/powerframework/secrets/db.conf)";

        (IHost host, HttpClient client) = await StartAsync(
            (LeakyName, HealthStatus.Unhealthy, LeakyDescription));

        using (host)
        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/health", UriKind.Relative),
                TestContext.Current.CancellationToken);

            string payload = await response.Content.ReadAsStringAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            foreach (string forbidden in (string[])
                [LeakyName, LeakyDescription, "npgsql", "primary.db.internal", "5432", "hunter2", "/var/lib"])
            {
                Assert.DoesNotContain(forbidden, payload, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task TheBodyNamesOnlyTheClosedPublicVocabularyHoweverManyChecksAreRegistered()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("store-a", HealthStatus.Healthy, "ok"),
            ("store-b", HealthStatus.Healthy, "ok"),
            ("queue-c", HealthStatus.Degraded, "catching up"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, status);

                // A problem document on the not-ready path, so the checks are read from the detail's
                // component names rather than from a report body. What matters is that the only names
                // anywhere in the payload are the two permitted ones.
                string payload = body.RootElement.ToString();

                foreach (string leaked in (string[])["store-a", "store-b", "queue-c"])
                {
                    Assert.DoesNotContain(leaked, payload, StringComparison.OrdinalIgnoreCase);
                }

                Assert.Contains("components", payload, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ThreeRegisteredChecksBecomeOneAggregatedEntryRatherThanThree()
    {
        // THE COUNT IS ITSELF DISCLOSURE, which is why aggregation is asserted rather than left to
        // read as a formatting choice. One anonymised entry per registered check would publish how
        // many components this service depends on to any unauthenticated caller - the same disclosure
        // as a name, expressed as a number.
        (IHost host, HttpClient client) = await StartAsync(
            ("store-a", HealthStatus.Healthy, "ok"),
            ("store-b", HealthStatus.Healthy, "ok"),
            ("queue-c", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);

                JsonElement[] checks = [.. body.RootElement.GetProperty("checks").EnumerateArray()];

                Assert.Equal(2, checks.Length);

                foreach (JsonElement check in checks)
                {
                    Assert.Contains(check.GetProperty("name").GetString(), PermittedComponentNames);
                }
            }
        }
    }

    // ==============================================================================================
    //  ANONYMITY - the property the whole readiness chain rests on
    // ==============================================================================================

    [Fact]
    public async Task TheProbeAnswersWithoutACredential()
    {
        // C-10 makes /health anonymous on all four services for a mechanical reason: the caller that
        // probes it holds no token, and it probes precisely while the service is starting. A 401 here
        // would make readiness depend on the service being probed and on token issuance being already
        // live - a circular dependency that cannot resolve during a cold start.
        (IHost host, HttpClient client) = await StartAsync();

        using (host)
        using (client)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/health", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(client.DefaultRequestHeaders.Authorization);
        }
    }

    [Fact]
    public async Task TheReportCarriesExactlyTheFourMembersTheContractDeclares()
    {
        (IHost host, HttpClient client) = await StartAsync(
            ("store-a", HealthStatus.Healthy, "ok"));

        using (host)
        using (client)
        {
            ServiceHealthReport? report = await client.GetFromJsonAsync<ServiceHealthReport>(
                new Uri("/health", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.NotNull(report);
            Assert.Equal("Healthy", report!.Status);
            Assert.Equal("dataservices", report.Service);
            Assert.NotEqual(default, report.CheckedAt);
            Assert.NotEmpty(report.Checks);

            foreach (ServiceHealthCheck check in report.Checks)
            {
                Assert.Contains(check.Name, PermittedComponentNames);
            }
        }
    }

    // ==============================================================================================
    //  THE TWO REGISTRATION STATES THE ENDPOINT DISTINGUISHES, AND THE OPERATOR CHANNEL'S LEVEL
    // ==============================================================================================

    [Fact]
    public async Task ARegisteredServiceWithNoRegisteredCheckReportsLivenessAlone()
    {
        // The third registration state, and the one a composition root produces by calling
        // AddHealthChecks() before any component check exists: the service resolves, so the endpoint
        // evaluates a report, and the report is EMPTY. The aggregate must then be this service's own
        // liveness rather than an empty component summary - an aggregated `components` entry over zero
        // checks would claim a verdict about a dependency graph that has no members.
        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel: null);

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);

            using (body)
            {
                Assert.Equal(HttpStatusCode.OK, status);
                Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

                JsonElement checks = body.RootElement.GetProperty("checks");

                JsonElement only = Assert.Single(checks.EnumerateArray().ToArray());

                Assert.Equal("self", only.GetProperty("name").GetString());
            }
        }
    }

    [Fact]
    public async Task TheHealthyOperatorRecordIsTraceLevelAndNamesEveryCheckWithoutItsDescription()
    {
        OperatorChannel channel = new();

        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel,
            ("primary-store", HealthStatus.Healthy, "Server=db;Password=MUST-NOT-BE-LOGGED"),
            ("cache", HealthStatus.Healthy, "redis://cache:6379"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);
            body.Dispose();

            Assert.Equal(HttpStatusCode.OK, status);
        }

        (LogLevel level, string record) = Assert.Single(
            channel.Records,
            static r => r.Message.Contains("primary-store", StringComparison.Ordinal));

        // BOTH names travel, with their verdicts, because WHICH component is which is the one thing
        // the anonymous response deliberately cannot say.
        Assert.Contains("primary-store=Healthy", record, StringComparison.Ordinal);
        Assert.Contains("cache=Healthy", record, StringComparison.Ordinal);

        // NEITHER DESCRIPTION travels. A description is free text a registration authored, and these
        // two carry exactly what such text routinely carries (constraint C-F).
        Assert.DoesNotContain("MUST-NOT-BE-LOGGED", record, StringComparison.Ordinal);
        Assert.DoesNotContain("redis://cache:6379", record, StringComparison.Ordinal);

        // The level is chosen from the verdict, so continuous probing of a healthy service does not
        // fill a log with records that say nothing happened.
        Assert.Equal(LogLevel.Trace, level);
    }

    [Fact]
    public async Task TheNotReadyOperatorRecordIsWarningLevel()
    {
        // The refutation for the row above: the level is not a constant.
        OperatorChannel channel = new();

        (IHost host, HttpClient client) = await StartCoreAsync(
            registerHealthCheckService: true,
            channel,
            ("primary-store", HealthStatus.Degraded, "slow"));

        using (host)
        using (client)
        {
            (HttpStatusCode status, JsonDocument body) = await ProbeAsync(client);
            body.Dispose();

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        }

        (LogLevel level, _) = Assert.Single(
            channel.Records,
            static r => r.Message.Contains("primary-store=Degraded", StringComparison.Ordinal));

        Assert.Equal(LogLevel.Warning, level);
    }
}

/// <summary>
/// The operator-channel records one host wrote, with the level each was written at.
/// </summary>
/// <remarks>
/// The level is captured because the endpoint chooses it from the verdict, and a record written at the
/// wrong level is a real defect that inspecting the message alone cannot detect: a healthy probe polled
/// once a second at warning level would bury the warnings that matter. Held per host rather than in
/// static state, so two rows can never observe each other's records.
/// </remarks>
internal sealed class OperatorChannel
{
    /// <summary>Every readiness record this host wrote, in order.</summary>
    public List<(LogLevel Level, string Message)> Records { get; } = [];
}

/// <summary>
/// A logging provider that appends every readiness record to an <see cref="OperatorChannel"/>.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly OperatorChannel _channel;

    /// <summary>Creates the provider over the caller's channel.</summary>
    /// <param name="channel">The channel every record is appended to.</param>
    public RecordingLoggerProvider(OperatorChannel channel) => _channel = channel;

    /// <summary>
    /// The endpoint's own logger category. Selecting on it is what separates the endpoint's operator
    /// record from the framework's own health-check record, which names a check and its verdict too and
    /// therefore cannot be excluded by inspecting message text.
    /// </summary>
    private const string ReadinessCategory = "PowerFramework.DataServices.Endpoints.HealthEndpoints";

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) =>
        string.Equals(categoryName, ReadinessCategory, StringComparison.Ordinal)
            ? new RecordingLogger(_channel)
            : NullLogger.Instance;

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly OperatorChannel _channel;

        public RecordingLogger(OperatorChannel channel) => _channel = channel;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _channel.Records.Add((logLevel, formatter(state, exception)));
        }
    }
}
