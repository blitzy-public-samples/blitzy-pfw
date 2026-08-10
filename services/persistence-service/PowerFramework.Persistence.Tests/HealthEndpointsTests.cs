// ==================================================================================================
//  HealthEndpointsTests - THE READINESS CONTRACT OF THE PERSISTENCE SERVICE, EXERCISED END TO END
//  ------------------------------------------------------------------------------------------------
//  WHAT IS UNDER TEST
//  services/persistence-service/PowerFramework.Persistence/Endpoints/HealthEndpoints.cs: the anonymous
//  GET /health route of contract C-10, its registration extension, the read-only SQLite reachability
//  check behind it, and the projection that decides what an unauthenticated caller is allowed to see.
//
//  WHY THE FILE IS SPLIT THE WAY IT IS
//  WebApplicationFactory boots the service's own Program.cs, so what the endpoint-level cases exercise
//  is the DEPLOYED composition - the real fallback authorization policy, the real registration, the real
//  projection - rather than a hand-assembled approximation. The storage half is exercised separately,
//  against a real SQLite database this file creates in its own temporary directory, because that is the
//  only way to assert the two properties that matter most about it: that it genuinely reaches the engine,
//  and that repeated polling changes nothing.
//
//  THE ONE FILESYSTEM RULE THAT APPLIES HERE
//  These tests create and remove their OWN temporary directories under the system temporary path. They
//  never touch the legacy tree, never touch a deployed data directory, and never touch the repository's
//  own test.db. That distinction is the whole point of the non-destructiveness case below: the legacy
//  oracle at ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L450 performs a file delete immediately
//  before opening at :L456, and the service under test must never do the same thing on a probe path,
//  because for one workflow identifier the legacy-side and target-side characterization recordings have
//  to be captured against the same persistence-db volume state.
//
//  RULES POSITION
//  No user rules were provided for this project - the rules document contains exactly one line saying
//  so - and nothing is invented in their place. The relevant named constraints are cited at each case.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Persistence.Configuration;
using PowerFramework.Persistence.Endpoints;

namespace PowerFramework.Persistence.Tests;

/// <summary>
/// The anonymous readiness contract, exercised through the service's own host.
/// </summary>
public sealed class HealthEndpointsTests
{
    /// <summary>The route the attached environment's readiness gate polls (constraint C-L).</summary>
    private const string HealthPath = "/health";

    /// <summary>The authenticated route, used to prove the anonymous exception is not wider than one route.</summary>
    private const string PingPath = "/v1/ping";

    /// <summary>An issuer the host is configured to name. A reserved test host that resolves nowhere.</summary>
    private const string ConfiguredIssuer = "https://security.invalid";

    /// <summary>The audience the host is configured to expect.</summary>
    private const string ConfiguredAudience = "powerframework-persistence-tests";

    // ==============================================================================================
    //  THE ANONYMOUS CONTRACT
    // ==============================================================================================

    /// <summary>
    /// The single property the whole Compose readiness chain rests on: the route answers without a
    /// token.
    /// </summary>
    /// <remarks>
    /// Program.cs installs a fallback authorization policy requiring an authenticated user, so this
    /// case is what proves the explicit anonymous opt-out is present and effective. Without it the
    /// route would answer 401 and <c>depends_on: condition: service_healthy</c> could never satisfy,
    /// which would leave Gateway permanently ungated (constraints C-G and C-J).
    /// </remarks>
    [Fact]
    public async Task Health_answers_200_without_any_authorization_header()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.RequestMessage?.Headers.Authorization);
    }

    /// <summary>
    /// The anonymous exception covers exactly one route and does not leak onto the authenticated one.
    /// </summary>
    [Fact]
    public async Task Ping_still_answers_401_while_health_is_anonymous()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage health = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage ping = await client.GetAsync(
            PingPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
    }

    /// <summary>
    /// A credential the host cannot verify does not close the anonymous route.
    /// </summary>
    /// <remarks>
    /// An orchestrator or proxy that attaches a stale or foreign credential to every outbound request
    /// must not be able to make the readiness gate unsatisfiable. Anonymous means the token is not
    /// REQUIRED, so an unverifiable one is simply not a reason to refuse.
    /// </remarks>
    [Fact]
    public async Task Health_answers_200_even_when_an_unverifiable_credential_is_attached()
    {
        await using PersistenceHost host = PersistenceHost.Create();
        using HttpClient client = host.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Get, HealthPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==============================================================================================
    //  THE HEALTHY BODY, AND WHAT IT MAY NOT CARRY
    // ==============================================================================================

    /// <summary>
    /// The ready body carries the closed vocabulary the projection declares, and Gateway can read it.
    /// </summary>
    /// <remarks>
    /// The <c>status</c> member is asserted as a JSON STRING with one of exactly three tokens, because
    /// Gateway's upstream probe reads that member literally and treats any other value as a failure.
    /// The <c>service</c> member is asserted as <c>persistence</c>, which is one of the three names
    /// Gateway's aggregate closes over.
    /// </remarks>
    [Fact]
    public async Task Healthy_body_reports_the_closed_component_vocabulary()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Healthy));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("persistence", root.GetProperty("service").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("checkedAt").ValueKind);

        List<string> names = [.. root
            .GetProperty("checks")
            .EnumerateArray()
            .Select(check => check.GetProperty("name").GetString() ?? string.Empty)];

        // Exactly the two entries the composed service produces, in this order: the endpoint's own
        // statement and the storage verdict. No third entry, because no other check is registered - and
        // the projection folds any that were into ONE aggregated entry rather than naming them.
        Assert.Equal(["self", "sqlite"], names);

        foreach (JsonElement check in root.GetProperty("checks").EnumerateArray())
        {
            Assert.Equal("Healthy", check.GetProperty("status").GetString());
        }
    }

    /// <summary>
    /// The anonymous body is bounded, so Gateway's probe - which reads only a bounded prefix - can
    /// always parse it.
    /// </summary>
    [Fact]
    public async Task Healthy_body_fits_within_the_bound_the_aggregator_reads()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Healthy));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        byte[] body = await response.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken);

        // Gateway's upstream probe reads at most four kilobytes before giving up on parsing a verdict.
        Assert.InRange(body.Length, 1, 4096);
    }

    /// <summary>
    /// THE DISCLOSURE SWEEP. Nothing on the forbidden list reaches an unauthenticated caller, on either
    /// arm, in the body or in a header.
    /// </summary>
    /// <param name="expectHealthy">
    /// Whether the storage verdict under test is ready. Both arms are swept, because a failure path is
    /// exactly where an implementation is tempted to be helpful.
    /// </param>
    /// <remarks>
    /// The forbidden set is drawn from the legacy structures the service must never echo -
    /// <c>transactiondata</c>'s nine fields at
    /// <c>ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs</c>, of which <c>logpass</c> is a
    /// credential, and <c>dberrordata</c>'s five at <c>.../dberrordata.srs</c>, of which
    /// <c>sqlsyntax</c> carries a complete generated statement including interpolated literals - plus
    /// this deployment's own configuration values and identity (constraints C-F and C-G).
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Neither_arm_discloses_configuration_credentials_or_topology(bool expectHealthy)
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(
                expectHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy),
            dataDirectory: directory.Path);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            expectHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        string headers = response.Headers.ToString() + response.Content.Headers.ToString();
        string observable = body + "\n" + headers;

        // NOTE ON WHAT IS DELIBERATELY *NOT* ON THIS LIST. The component identifier `sqlite` IS present
        // in the body and is meant to be: it is a fixed, non-sensitive identifier authored in the
        // endpoint file, naming a capability the architecture already publishes, and it carries no
        // location, version, credential or provider detail. What must never appear is the PROVIDER's
        // identity or the deployment's own configuration, which is what the entries below assert.
        string[] forbidden =
        [
            // transactiondata's fields at ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs.
            // `lock`, `database` and `autocommit` are excluded from the literal sweep because they are
            // ordinary English words a legitimate description could contain; the six that could only
            // have come from the structure are asserted.
            "logpass", "logid", "servername", "dbparm", "userparm", "dbms",

            // dberrordata's fields at ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs. sqlsyntax is
            // the most consequential of the three: it carries the complete generated statement,
            // interpolated literal values included.
            "sqldbcode", "sqlerrtext", "sqlsyntax",

            // This deployment's own configuration - the composed URI's every part, and the verification
            // settings.
            directory.Path, "test.db", "mode=rwc", "Data Source", "DataDirectory", "journal",
            ConfiguredIssuer, ConfiguredAudience, "jwks", "well-known",

            // Provider, framework and host identity.
            "Microsoft.Data", "SQLitePCLRaw", "e_sqlite3", "EntityFramework", "Kestrel", "net10.0",
            "StackTrace", "   at ", Environment.MachineName,
        ];

        List<string> violations =
        [
            .. forbidden.Where(candidate =>
                observable.Contains(candidate, StringComparison.OrdinalIgnoreCase)),
        ];

        // Asserted as a set rather than one at a time, so a failure names EVERY leak rather than the
        // first, which is what makes this case usable as a regression gate.
        Assert.True(
            violations.Count == 0,
            "The anonymous readiness response disclosed: " + string.Join(", ", violations));
    }

    // ==============================================================================================
    //  THE NOT-READY ARMS - AND THE FAIL-FAST NUANCE
    // ==============================================================================================

    /// <summary>
    /// Unreachable storage answers 503 with a structured body, and the process keeps serving.
    /// </summary>
    /// <remarks>
    /// THIS IS THE FAIL-FAST NUANCE MADE CHECKABLE. The framework's fail-fast posture - a structural
    /// fault ends the process rather than degrading past it, as
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c> does with its terminating system-error handler -
    /// governs startup validation, NOT the health verdict. A probe that terminated on unreachable
    /// storage would leave the readiness gate unable to observe a structured not-ready state at all, and
    /// Gateway's aggregator would see a transport error instead of a verdict. So the second request
    /// below is the assertion that matters: the host is still answering afterwards.
    /// </remarks>
    [Fact]
    public async Task Unreachable_storage_answers_503_and_the_host_keeps_serving()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Unhealthy));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement problem = document.RootElement;

        Assert.Equal("Service Unavailable", problem.GetProperty("title").GetString());
        Assert.Equal(503, problem.GetProperty("status").GetInt32());
        Assert.Contains("sqlite", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        // The legacy return code travels in the single permitted extension member, and it is the ported
        // retry code because the condition is transient and the caller's correct response is to ask
        // again - which is exactly what a readiness gate does.
        Assert.Equal(RetCode.E_RETRY, problem.GetProperty("retCode").GetInt64());

        // The process did not terminate, which is the point of the case.
        using HttpResponseMessage second = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);
        using HttpResponseMessage ping = await client.GetAsync(
            PingPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, ping.StatusCode);
    }

    /// <summary>
    /// A component check that throws is reported as not ready rather than surfacing as a server fault.
    /// </summary>
    /// <remarks>
    /// A 500 would be read by the readiness gate as "the service is broken in an unknown way" and by
    /// Gateway's aggregator as an unreachable upstream. A 503 carrying the published shape is the
    /// contract, and the exception belongs on the operator channel instead.
    /// </remarks>
    [Fact]
    public async Task Throwing_component_check_answers_503_rather_than_500()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Throwing());
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"status\":503", body.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A degraded verdict is answered with 503, and the token stays distinguishable in the body.
    /// </summary>
    /// <remarks>
    /// The published endpoint contract is explicit that <c>Degraded</c> and <c>Unhealthy</c> are BOTH
    /// answered with 503 and only <c>Healthy</c> with 200, because the readiness gate observes the
    /// status CODE: a not-ready service answering 200 would let the dependency condition open on a
    /// service that had just said it was not ready, which is the exact ordering property the gate exists
    /// to enforce. The three tokens nonetheless stay distinct in the body so an operator can tell a
    /// service still completing startup validation from one whose storage engine has failed.
    /// </remarks>
    [Fact]
    public async Task Degraded_storage_answers_503_while_the_body_still_says_degraded()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Degraded));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Degraded", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A check registered under a name outside the closed vocabulary is folded into one aggregated
    /// entry, so neither its name nor the number of registrations reaches the wire.
    /// </summary>
    /// <remarks>
    /// The substituted name here is deliberately the kind of thing a real registration produces - it
    /// names a host and a region - which is precisely why the projection may not echo it.
    /// </remarks>
    [Fact]
    public async Task A_foreign_check_name_never_reaches_the_wire()
    {
        const string leakyName = "npgsql-primary-eu-west-1";

        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Healthy),
            additional: (leakyName, StubHealthCheck.Reporting(HealthStatus.Healthy)));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(leakyName, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("components", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no evaluator registered at all the route still answers, rather than faulting while the
    /// endpoint is built.
    /// </summary>
    /// <remarks>
    /// The framework's own <c>MapHealthChecks</c> resolves the evaluator as REQUIRED, which would turn a
    /// probe into a startup gate: a host that never registered a check would fail to serve the one route
    /// an orchestrator uses to find out whether it is alive. The handler resolves it OPTIONALLY for
    /// exactly this reason, and reports the one thing it can still establish truthfully - that the
    /// process is running and answering, which the execution of the handler has just demonstrated.
    /// </remarks>
    [Fact]
    public async Task Health_answers_200_when_no_evaluator_is_registered_at_all()
    {
        await using PersistenceHost host = PersistenceHost.Create(removeEvaluator: true);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement root = document.RootElement;

        Assert.Equal("Healthy", root.GetProperty("status").GetString());

        JsonElement only = Assert.Single([.. root.GetProperty("checks").EnumerateArray()]);

        Assert.Equal("self", only.GetProperty("name").GetString());
    }

    /// <summary>
    /// A verdict from outside the published vocabulary is republished as <c>Unhealthy</c>, never as
    /// itself.
    /// </summary>
    /// <remarks>
    /// Gateway's upstream probe reads the three tokens literally and treats anything else as a failure,
    /// so a component reporting a status this build does not recognise must not be able to put an
    /// unrecognised token on the wire. The translation is an explicit switch whose default arm fails
    /// closed, and this case is what holds that arm in place.
    /// </remarks>
    [Fact]
    public async Task An_unrecognised_component_status_is_published_as_unhealthy()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting((HealthStatus)99));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        JsonElement storage = document
            .RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(check => string.Equals(
                check.GetProperty("name").GetString(),
                "sqlite",
                StringComparison.Ordinal));

        Assert.Equal("Unhealthy", storage.GetProperty("status").GetString());
        Assert.DoesNotContain("99", storage.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The operator record is written on the READY path too, at trace level, carrying the per-component
    /// detail the anonymous body deliberately withholds.
    /// </summary>
    /// <remarks>
    /// Two properties in one case. The detail an operator needs - which component reported what - exists
    /// on the operator channel and nowhere else, and the ready path is quiet by default: it is only
    /// emitted when trace is enabled, so an orchestrator polling continuously does not fill the log with
    /// records saying nothing happened. Enabling trace for this one category is what makes the first
    /// property observable while the second stays true of the shipped configuration.
    /// </remarks>
    [Fact]
    public async Task The_ready_path_records_the_component_detail_on_the_operator_channel_at_trace()
    {
        RecordingLoggerProvider recorder = new();

        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Healthy),
            traceLogging: true,
            recorder: recorder);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string record = Assert.Single(
            recorder.Records,
            candidate => candidate.Contains("Readiness for the", StringComparison.Ordinal));

        // The component name and its verdict, which the body does not carry per component.
        Assert.Contains("sqlite=Healthy", record, StringComparison.Ordinal);
        Assert.Contains("persistence", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// An evaluator that faults is reported as not ready, and the fault reaches the operator rather than
    /// the caller.
    /// </summary>
    /// <remarks>
    /// Distinct from the throwing-component case: there the shared framework catches the component's
    /// exception and reports it as an entry, whereas here the EVALUATION ITSELF fails, which is the path
    /// the handler's own guard exists for. Both must answer 503 rather than 500, and neither may put the
    /// exception in the body.
    /// </remarks>
    [Fact]
    public async Task A_faulting_evaluator_answers_503_and_keeps_the_exception_out_of_the_body()
    {
        await using PersistenceHost host = PersistenceHost.Create(faultEvaluator: true);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("self", body, StringComparison.Ordinal);
        Assert.DoesNotContain("evaluator faulted", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);

        // Still serving, which is the fail-fast nuance again: a probe reports, it does not terminate.
        using HttpResponseMessage second = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
    }

    // ==============================================================================================
    //  CONCURRENCY
    // ==============================================================================================

    /// <summary>
    /// Simultaneous probes all answer, which is the shape an orchestrator and an operator polling at the
    /// same time actually produce.
    /// </summary>
    [Fact]
    public async Task Simultaneous_probes_all_answer_200()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Healthy));
        using HttpClient client = host.CreateClient();

        Task<HttpResponseMessage>[] inFlight =
        [
            .. Enumerable
                .Range(0, 12)
                .Select(_ => client.GetAsync(HealthPath, TestContext.Current.CancellationToken)),
        ];

        HttpResponseMessage[] responses = await Task.WhenAll(inFlight);

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    // ==============================================================================================
    //  THE STORAGE PROBE ITSELF - REAL ENGINE, REAL DATABASE, READ ONLY
    // ==============================================================================================

    /// <summary>
    /// The probe genuinely reaches the engine and reports ready.
    /// </summary>
    /// <remarks>
    /// A readiness verdict that never touched storage would report ready while the only storage provider
    /// in the estate was unreachable, which is exactly the state the gate Gateway hangs on exists to
    /// detect. This case is what proves the probe is not decorative.
    /// </remarks>
    [Fact]
    public async Task The_probe_reports_healthy_against_a_reachable_engine()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("The storage engine answered a read-only reachability probe.", result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// A data directory that cannot be created is reported as not ready rather than allowed to fault.
    /// </summary>
    /// <remarks>
    /// The seam fails fast on a structural fault, which is correct where it lives - at startup. Reaching
    /// that fault from a probe must not take the container with it, so the check converts it into the
    /// registration's failure status. The directory is made uncreatable by placing an ordinary FILE
    /// where its parent segment must be, which no filesystem will turn into a directory.
    /// </remarks>
    [Fact]
    public async Task An_uncreatable_data_directory_is_reported_as_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        string blocker = Path.Combine(directory.Path, "blocker");
        await File.WriteAllTextAsync(
            blocker,
            "not a directory",
            TestContext.Current.CancellationToken);

        await using SqliteConnectionFactory storage = CreateStorage(Path.Combine(blocker, "data"));

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("The storage reachability probe could not be completed.", result.Description);

        // The reason is on the operator channel and nowhere else: it can name a path.
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// An engine that ANSWERS NEGATIVELY - as opposed to one whose configuration faults - is reported as
    /// not ready, and the provider's own error text stays out of the result.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the arm the seam reports by RETURNING FALSE rather than by throwing, and it is the one an
    /// operational storage failure actually takes. It is provoked by placing a DIRECTORY where the
    /// database file must be, which every SQLite build refuses to open while leaving the directory itself
    /// perfectly valid - a deterministic, cross-platform way to make the engine say no.
    /// </para>
    /// <para>
    /// The assertions on the result are the point: the description is this file's fixed prose, and the
    /// provider's message - which names the path it could not open - is neither in the description, nor
    /// in the data bag, nor on the exception. It is on the operator channel as a pair of integers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_engine_that_answers_negatively_is_reported_as_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        // A directory named exactly as the database file must be. Nothing is deleted and nothing
        // existing is replaced; this directory is created here and removed with the fixture.
        Directory.CreateDirectory(Path.Combine(directory.Path, "test.db"));

        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(
            "The storage engine did not answer a read-only reachability probe.",
            result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);

        // The seam recorded the reason. The check read only the two integers from it and left the text
        // where it was, which is what the assertion above proves from the caller's side.
        Assert.NotEqual(RetCode.OK, storage.SqlCode);
    }

    /// <summary>
    /// The registration's own failure status is honoured rather than assumed.
    /// </summary>
    /// <remarks>
    /// The composed service registers this check as FAILING rather than degrading, because unreachable
    /// storage on the only service that holds any is a failure. Honouring the registration means a host
    /// that decided otherwise gets what it asked for, and it means the check states no policy of its own.
    /// </remarks>
    [Fact]
    public async Task The_probe_honours_the_registrations_failure_status()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        string blocker = Path.Combine(directory.Path, "blocker");
        await File.WriteAllTextAsync(
            blocker,
            "not a directory",
            TestContext.Current.CancellationToken);

        await using SqliteConnectionFactory storage = CreateStorage(Path.Combine(blocker, "data"));

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Degraded),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    /// <summary>
    /// Caller cancellation propagates rather than being converted into a verdict.
    /// </summary>
    /// <remarks>
    /// A cancelled request has no response left to write, so producing a verdict would be work nobody
    /// reads. This is the ONE exception the check deliberately allows out, and it is distinguished from
    /// the expiry of the check's own probe budget - which IS converted into a verdict.
    /// </remarks>
    [Fact]
    public async Task Caller_cancellation_propagates_rather_than_becoming_a_verdict()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(RegistrationContext(HealthStatus.Unhealthy), cancelled.Token));
    }

    /// <summary>
    /// THE SHARED-VOLUME RULE MADE CHECKABLE: repeated polling changes nothing that is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For one workflow identifier the legacy-side and target-side characterization recordings must be
    /// captured against the SAME <c>persistence-db</c> volume state, with the volume neither recreated
    /// nor reseeded between them, or the paired recordings are not comparable at all. Because the
    /// orchestration health condition polls continuously, a probe that mutated stored state would break
    /// that on every single poll rather than in an edge case - so this is the case that has to hold.
    /// </para>
    /// <para>
    /// The first probe is taken BEFORE the snapshot on purpose. It is the one that opens the connection
    /// and applies the journal-mode pragma the seam owns, which is legitimately allowed to touch the
    /// file's metadata; every probe after it must be inert. The row count and the file length are
    /// nonetheless compared across the WHOLE sequence, because those two are the invariants that would
    /// actually be violated by a create, a delete, a migration or a reseed.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Repeated_probes_do_not_alter_the_stored_state()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        string databasePath = Path.Combine(directory.Path, "test.db");
        long seededRows = await SeedCompanyTableAsync(databasePath);
        long lengthBeforeAnyProbe = new FileInfo(databasePath).Length;

        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        // The opening probe, whose side effect on file metadata belongs to the seam rather than to the
        // check, and is therefore excluded from the timestamp comparison below.
        HealthCheckResult opening = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, opening.Status);

        FileInfo snapshot = new(databasePath);
        long lengthAfterOpening = snapshot.Length;
        DateTime writtenAfterOpening = snapshot.LastWriteTimeUtc;

        for (int poll = 0; poll < 6; poll++)
        {
            HealthCheckResult result = await check.CheckHealthAsync(
                RegistrationContext(HealthStatus.Unhealthy),
                TestContext.Current.CancellationToken);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        FileInfo after = new(databasePath);

        Assert.True(after.Exists, "The probe must never delete the database file.");
        Assert.Equal(lengthAfterOpening, after.Length);
        Assert.Equal(writtenAfterOpening, after.LastWriteTimeUtc);

        // The two invariants a create, delete, migration or reseed would break, compared across the
        // whole sequence including the opening probe.
        Assert.Equal(lengthBeforeAnyProbe, after.Length);
        Assert.Equal(seededRows, await CountCompanyRowsAsync(databasePath));
    }

    /// <summary>
    /// The liveness window is measured on the INJECTED clock, so a characterization run can mask it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every clock read is a determinism seam that has to be maskable from both the master and the
    /// candidate recording, which is why the service reads no ambient clock anywhere. Driving a
    /// deterministic double here is what proves the seam is honoured rather than merely declared: with
    /// the clock frozen, repeated probes inside the window reuse the recorded instant; advancing the
    /// clock past the window forces a fresh measurement, which the seam records as a new instant.
    /// </para>
    /// <para>
    /// The double is hand written rather than taken from a testing package, matching the three clocks
    /// this test project already declares: no package reference is added for a fake clock, and
    /// <see cref="TimeProvider"/> is designed to be subclassed for exactly this.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_liveness_window_is_measured_on_the_injected_clock()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        FrozenClock clock = new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path, clock);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        Assert.Null(storage.LastReachabilityProbedAt);

        await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        DateTimeOffset? firstMeasurement = storage.LastReachabilityProbedAt;
        Assert.Equal(clock.GetUtcNow(), firstMeasurement);

        // Inside the window, on a frozen clock: the recorded instant does not move, which is only
        // observable because the age is measured on THIS clock and not on the machine's.
        await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(firstMeasurement, storage.LastReachabilityProbedAt);

        // Past the window: a fresh measurement, stamped with the advanced instant.
        clock.Advance(TimeSpan.FromMinutes(1));

        await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(clock.GetUtcNow(), storage.LastReachabilityProbedAt);
        Assert.NotEqual(firstMeasurement, storage.LastReachabilityProbedAt);
    }

    // ==============================================================================================
    //  THE REGISTRATION EXTENSION
    // ==============================================================================================

    /// <summary>
    /// The one registration call is self-sufficient: it registers the check and the two seams it needs.
    /// </summary>
    /// <remarks>
    /// Driven against a bare service collection deliberately, because that is the property that lets
    /// Program.cs make exactly one readiness call and lets a test compose the probe without a host.
    /// </remarks>
    [Fact]
    public void The_registration_extension_registers_the_check_and_the_seams_it_needs()
    {
        ServiceCollection services = [];
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        IServiceCollection returned = services.AddPersistenceHealthChecks();

        Assert.Same(services, returned);

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<TimeProvider>());
        Assert.NotNull(provider.GetService<SqliteConnectionFactory>());
        Assert.NotNull(provider.GetService<HealthCheckService>());

        HealthCheckServiceOptions options =
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        HealthCheckRegistration registration = Assert.Single(
            options.Registrations,
            candidate => string.Equals(candidate.Name, "sqlite", StringComparison.Ordinal));

        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Contains("ready", registration.Tags);
    }

    /// <summary>
    /// Calling the registration twice does not double-register the check.
    /// </summary>
    [Fact]
    public void The_registration_extension_is_safe_to_call_more_than_once()
    {
        ServiceCollection services = [];
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddPersistenceHealthChecks();
        services.AddPersistenceHealthChecks();

        using ServiceProvider provider = services.BuildServiceProvider();

        HealthCheckServiceOptions options =
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        // Two registrations of the same name would make the shared framework throw on evaluation, so
        // this assertion is what keeps the try-add design honest.
        Assert.Single(
            options.Registrations,
            candidate => string.Equals(candidate.Name, "sqlite", StringComparison.Ordinal));
    }

    /// <summary>
    /// The mapping extension refuses a null route builder rather than deferring the fault to a request.
    /// </summary>
    [Fact]
    public void The_mapping_and_registration_extensions_guard_their_arguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => ((IEndpointRouteBuilder)null!).MapHealthEndpoints());
        Assert.Throws<ArgumentNullException>(
            () => ((IServiceCollection)null!).AddPersistenceHealthChecks());
    }

    /// <summary>
    /// The check refuses null collaborators.
    /// </summary>
    [Fact]
    public void The_check_guards_its_collaborators()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        Assert.Throws<ArgumentNullException>(
            () => new SqliteReachabilityHealthCheck(
                null!,
                NullLogger<SqliteReachabilityHealthCheck>.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new SqliteReachabilityHealthCheck(storage, null!));
    }

    /// <summary>
    /// The check refuses a null evaluation context.
    /// </summary>
    [Fact]
    public async Task The_check_guards_its_context()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(storage, NullLogger<SqliteReachabilityHealthCheck>.Instance);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => check.CheckHealthAsync(null!, TestContext.Current.CancellationToken));
    }

    // ==============================================================================================
    //  FIXTURES
    // ==============================================================================================

    /// <summary>
    /// Builds a storage seam over a temporary directory, bypassing configuration binding so a case
    /// depends on nothing but the path it chose.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file should live in.</param>
    /// <param name="clock">The clock to inject, or <see langword="null"/> for the system clock.</param>
    /// <returns>A seam that has not yet opened anything.</returns>
    private static SqliteConnectionFactory CreateStorage(
        string dataDirectory,
        TimeProvider? clock = null)
    {
        PersistenceOptions options = new()
        {
            Sqlite = new SqliteOptions
            {
                DataDirectory = dataDirectory,
                DatabaseFileName = "test.db",
                Mode = "rwc",
                Journal = "DELETE",
            },
        };

        return new SqliteConnectionFactory(
            Options.Create(options),
            NullLogger<SqliteConnectionFactory>.Instance,
            clock ?? TimeProvider.System);
    }

    /// <summary>
    /// Builds an evaluation context for a registration with the requested failure status.
    /// </summary>
    /// <param name="failureStatus">The status the registration treats a failure as.</param>
    /// <returns>A context the check can read its registration from.</returns>
    private static HealthCheckContext RegistrationContext(HealthStatus failureStatus) => new()
    {
        Registration = new HealthCheckRegistration(
            "sqlite",
            static _ => throw new InvalidOperationException(
                "The registration's own factory is never invoked by these cases; the check under test "
                + "is constructed directly."),
            failureStatus,
            ["ready"]),
    };

    /// <summary>
    /// Creates the evidenced schema and a few rows, so the non-destructiveness case has something that
    /// could be destroyed.
    /// </summary>
    /// <param name="databasePath">Where the database file should be created.</param>
    /// <returns>How many rows were seeded.</returns>
    /// <remarks>
    /// The statement is the one DDL in the repository, at
    /// <c>ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469</c>. It is executed HERE, by a test
    /// fixture preparing its own temporary file - never by the service, and never on a probe path.
    /// </remarks>
    private static async Task<long> SeedCompanyTableAsync(string databasePath)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        await using SqliteConnection connection = new(connectionString.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE IF NOT EXISTS COMPANY("
                + "ID INTEGER PRIMARY KEY NOT NULL,"
                + "NAME TEXT NOT NULL,"
                + "AGE INT NOT NULL,"
                + "ADDRESS CHAR(50),"
                + "SALARY REAL,"
                + "BIRTH TEXT);";

            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        const int rows = 3;

        for (int row = 1; row <= rows; row++)
        {
            await using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO COMPANY (ID, NAME, AGE, ADDRESS, SALARY, BIRTH) "
                + "VALUES ($id, $name, $age, $address, $salary, $birth);";
            insert.Parameters.AddWithValue("$id", row);
            insert.Parameters.AddWithValue("$name", "seeded-" + row.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$age", 30 + row);
            insert.Parameters.AddWithValue("$address", "somewhere");
            insert.Parameters.AddWithValue("$salary", 1000.5d * row);
            insert.Parameters.AddWithValue("$birth", "1990-01-0" + row.ToString(CultureInfo.InvariantCulture));

            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return rows;
    }

    /// <summary>
    /// Counts the seeded rows through a connection of this fixture's own, so the count is independent of
    /// the seam under test.
    /// </summary>
    /// <param name="databasePath">The database file to read.</param>
    /// <returns>The row count.</returns>
    private static async Task<long> CountCompanyRowsAsync(string databasePath)
    {
        SqliteConnectionStringBuilder connectionString = new()
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        };

        await using SqliteConnection connection = new(connectionString.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM COMPANY;";

        object? scalar = await count.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A temporary directory owned by one case, removed when the case ends.
    /// </summary>
    /// <remarks>
    /// A GUID-named directory under the system temporary path, so cases running in parallel and clones
    /// running side by side cannot collide. The removal targets ONLY this directory: it is never a
    /// deployed data directory, never the repository's own <c>test.db</c>, and never anything under the
    /// read-only legacy tree.
    /// </remarks>
    private sealed class TemporaryDataDirectory : IDisposable
    {
        /// <summary>The directory's absolute path.</summary>
        internal string Path { get; }

        /// <summary>Initializes the directory.</summary>
        /// <param name="path">The absolute path, already created.</param>
        private TemporaryDataDirectory(string path) => Path = path;

        /// <summary>Creates and returns a fresh temporary directory.</summary>
        /// <returns>The created directory.</returns>
        internal static TemporaryDataDirectory Create()
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "pfw-health-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

            Directory.CreateDirectory(path);

            return new TemporaryDataDirectory(path);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            // Best effort, and deliberately silent on failure: a case has already made its assertions by
            // the time this runs, and a locked temporary file must not turn a passing case into a
            // failing one.
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// A clock that does not move unless a case moves it.
    /// </summary>
    /// <remarks>
    /// Hand written rather than taken from a testing package, matching the three clocks this project
    /// already declares: the service adds no package reference for a fake clock, and
    /// <see cref="TimeProvider"/> exists to be subclassed for exactly this.
    /// </remarks>
    private sealed class FrozenClock : TimeProvider
    {
        /// <summary>The current instant.</summary>
        private DateTimeOffset _now;

        /// <summary>Initializes the clock.</summary>
        /// <param name="start">The instant the clock starts at.</param>
        internal FrozenClock(DateTimeOffset start) => _now = start;

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>Moves the clock forward.</summary>
        /// <param name="by">How far to advance.</param>
        internal void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    /// <summary>
    /// A component check whose verdict a case chooses, standing in for the storage probe.
    /// </summary>
    /// <remarks>
    /// Substituted UNDER THE SAME REGISTRATION NAME as the real check, so the endpoint-level cases
    /// exercise the real projection path rather than a parallel one. This is what lets the not-ready
    /// arms be asserted without an unreachable database, which no test can create reliably on every
    /// host.
    /// </remarks>
    private sealed class StubHealthCheck : IHealthCheck
    {
        /// <summary>The verdict to report, or <see langword="null"/> to throw instead.</summary>
        private readonly HealthStatus? _status;

        /// <summary>Initializes the stub.</summary>
        /// <param name="status">The verdict, or <see langword="null"/> to throw.</param>
        private StubHealthCheck(HealthStatus? status) => _status = status;

        /// <summary>Creates a stub reporting the supplied verdict.</summary>
        /// <param name="status">The verdict to report.</param>
        /// <returns>The stub.</returns>
        internal static StubHealthCheck Reporting(HealthStatus status) => new(status);

        /// <summary>Creates a stub that throws instead of reporting.</summary>
        /// <returns>The stub.</returns>
        internal static StubHealthCheck Throwing() => new(null);

        /// <inheritdoc/>
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_status is not HealthStatus status)
            {
                throw new InvalidOperationException(
                    "The substituted storage check faulted deliberately. Nothing about this message may "
                    + "reach the anonymous response body.");
            }

            return Task.FromResult(new HealthCheckResult(status, "substituted"));
        }
    }

    /// <summary>
    /// Captures the host's own log records so a case can assert on the operator channel.
    /// </summary>
    /// <remarks>
    /// Hand written rather than taken from a logging-test package: no package is added for this, and the
    /// provider abstraction exists to be implemented. Records are formatted eagerly and stored as strings,
    /// because a log state object is not guaranteed to outlive the call that produced it.
    /// </remarks>
    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /// <summary>Every formatted record, in order.</summary>
        internal List<string> Records { get; } = [];

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new Recorder(this);

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <summary>Appends one record under the provider's lock.</summary>
        /// <param name="record">The formatted record.</param>
        private void Append(string record)
        {
            lock (Records)
            {
                Records.Add(record);
            }
        }

        /// <summary>A logger that records everything it is given.</summary>
        /// <param name="owner">The provider to append to.</param>
        private sealed class Recorder(RecordingLoggerProvider owner) : ILogger
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

                owner.Append(formatter(state, exception));
            }
        }
    }

    /// <summary>
    /// An evaluator that faults, standing in for a readiness evaluation that cannot be completed at all.
    /// </summary>
    /// <remarks>
    /// Distinct from a component check that throws, which the shared framework catches and reports as an
    /// entry. This is the failure the endpoint's own guard exists for, and the message below must never
    /// reach the anonymous body.
    /// </remarks>
    private sealed class FaultingHealthCheckService : HealthCheckService
    {
        /// <inheritdoc/>
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The readiness evaluator faulted deliberately. Nothing about this message may reach the "
                + "anonymous response body.");
    }

    /// <summary>
    /// The service's own host, booted from its own <c>Program.cs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is under test is therefore the DEPLOYED composition: the fallback authorization policy, the
    /// real readiness registration and the real projection. Only two things are supplied from outside -
    /// the verification settings the startup validation requires, and optionally a substituted component
    /// check under the real check's own name.
    /// </para>
    /// <para>
    /// NO TOKEN IS EVER MINTED HERE and no signing key exists in this fixture. Security is the sole
    /// issuer in the system, and this service holds verification material only; the readiness route is
    /// anonymous, so no credential is needed to exercise it at all.
    /// </para>
    /// </remarks>
    private sealed class PersistenceHost : WebApplicationFactory<Program>
    {
        /// <summary>The substituted storage check, or <see langword="null"/> to keep the real one.</summary>
        private readonly IHealthCheck? _storage;

        /// <summary>An extra registration to add, or <see langword="null"/>.</summary>
        private readonly (string Name, IHealthCheck Check)? _additional;

        /// <summary>The data directory to configure, or <see langword="null"/> for the shipped default.</summary>
        private readonly string? _dataDirectory;

        /// <summary>Whether the evaluator is removed from the container entirely.</summary>
        private readonly bool _removeEvaluator;

        /// <summary>Whether the evaluator is replaced by one that faults.</summary>
        private readonly bool _faultEvaluator;

        /// <summary>Whether trace-level logging is enabled for this endpoint's category.</summary>
        private readonly bool _traceLogging;

        /// <summary>A provider capturing the host's records, or <see langword="null"/>.</summary>
        private readonly ILoggerProvider? _recorder;

        /// <summary>Initializes the fixture.</summary>
        /// <param name="storage">The substituted storage check, or <see langword="null"/>.</param>
        /// <param name="additional">An extra registration, or <see langword="null"/>.</param>
        /// <param name="dataDirectory">The data directory to configure, or <see langword="null"/>.</param>
        /// <param name="removeEvaluator">Whether to remove the evaluator entirely.</param>
        /// <param name="faultEvaluator">Whether to replace the evaluator with one that faults.</param>
        /// <param name="traceLogging">Whether to enable trace logging for this endpoint's category.</param>
        /// <param name="recorder">A provider capturing the host's records, or <see langword="null"/>.</param>
        private PersistenceHost(
            IHealthCheck? storage,
            (string Name, IHealthCheck Check)? additional,
            string? dataDirectory,
            bool removeEvaluator,
            bool faultEvaluator,
            bool traceLogging,
            ILoggerProvider? recorder)
        {
            _storage = storage;
            _additional = additional;
            _dataDirectory = dataDirectory;
            _removeEvaluator = removeEvaluator;
            _faultEvaluator = faultEvaluator;
            _traceLogging = traceLogging;
            _recorder = recorder;
        }

        /// <summary>Creates a host.</summary>
        /// <param name="storage">
        /// A substituted storage check registered under the real check's name, or <see langword="null"/>
        /// to leave the real one in place.
        /// </param>
        /// <param name="additional">A second registration under a foreign name, or <see langword="null"/>.</param>
        /// <param name="dataDirectory">A data directory to configure, or <see langword="null"/>.</param>
        /// <param name="removeEvaluator">
        /// When <see langword="true"/>, removes <see cref="HealthCheckService"/> from the container so the
        /// handler's optional resolution is exercised.
        /// </param>
        /// <param name="faultEvaluator">
        /// When <see langword="true"/>, replaces <see cref="HealthCheckService"/> with one that throws, so
        /// the handler's own guard is exercised rather than the framework's per-check guard.
        /// </param>
        /// <param name="traceLogging">
        /// When <see langword="true"/>, enables trace logging for this endpoint's category, which is what
        /// makes the quiet ready-path operator record observable.
        /// </param>
        /// <param name="recorder">A provider capturing the host's records, or <see langword="null"/>.</param>
        /// <returns>A started-on-first-use host.</returns>
        internal static PersistenceHost Create(
            IHealthCheck? storage = null,
            (string Name, IHealthCheck Check)? additional = null,
            string? dataDirectory = null,
            bool removeEvaluator = false,
            bool faultEvaluator = false,
            bool traceLogging = false,
            ILoggerProvider? recorder = null)
            => new(
                storage,
                additional,
                dataDirectory,
                removeEvaluator,
                faultEvaluator,
                traceLogging,
                recorder);

        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.UseEnvironment(Environments.Production);

            // Supplied as configuration so the service's OWN startup validation runs against them. The
            // authority is a reserved test host that resolves nowhere, which is safe because the
            // readiness route is anonymous and no metadata is ever fetched for it.
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(BuildSettings()));

            if (_recorder is ILoggerProvider provider)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(provider));
            }

            builder.ConfigureServices(services =>
            {
                if (_removeEvaluator)
                {
                    services.RemoveAll<HealthCheckService>();

                    // The registration also installs a hosted service that takes the evaluator as a
                    // constructor dependency, so removing only the evaluator would make the HOST fail to
                    // start - which would prove nothing about the endpoint. Matched by type NAME because
                    // the framework declares that hosted service as internal.
                    foreach (ServiceDescriptor descriptor in services
                        .Where(candidate => string.Equals(
                            candidate.ImplementationType?.Name,
                            "HealthCheckPublisherHostedService",
                            StringComparison.Ordinal))
                        .ToList())
                    {
                        services.Remove(descriptor);
                    }
                }
                else if (_faultEvaluator)
                {
                    services.RemoveAll<HealthCheckService>();
                    services.AddSingleton<HealthCheckService, FaultingHealthCheckService>();
                }

                if (_storage is null && _additional is null)
                {
                    return;
                }

                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    if (_storage is IHealthCheck substitute)
                    {
                        // Remove the real registration and put the substitute under the SAME name, so the
                        // projection path under test is the real one.
                        foreach (HealthCheckRegistration existing in options.Registrations
                            .Where(candidate => string.Equals(
                                candidate.Name,
                                "sqlite",
                                StringComparison.OrdinalIgnoreCase))
                            .ToList())
                        {
                            options.Registrations.Remove(existing);
                        }

                        options.Registrations.Add(
                            new HealthCheckRegistration(
                                "sqlite",
                                _ => substitute,
                                HealthStatus.Unhealthy,
                                ["ready"]));
                    }

                    if (_additional is (string name, IHealthCheck extra))
                    {
                        options.Registrations.Add(
                            new HealthCheckRegistration(
                                name,
                                _ => extra,
                                HealthStatus.Unhealthy,
                                ["ready"]));
                    }
                });
            });
        }

        /// <summary>Builds the configuration this host runs on.</summary>
        /// <returns>The settings, applied last so they win over the shipped files.</returns>
        private Dictionary<string, string?> BuildSettings()
        {
            Dictionary<string, string?> settings = new(StringComparer.Ordinal)
            {
                ["Jwt:Authority"] = ConfiguredIssuer,
                ["Jwt:Audience"] = ConfiguredAudience,
                ["Jwt:RequireHttpsMetadata"] = "true",
            };

            if (_dataDirectory is not null)
            {
                settings["Sqlite:DataDirectory"] = _dataDirectory;
            }

            if (_traceLogging)
            {
                settings["Logging:LogLevel:PowerFramework.Persistence.Endpoints.HealthEndpoints"] =
                    "Trace";
            }

            return settings;
        }
    }
}
