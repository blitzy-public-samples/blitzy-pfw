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
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        // PROVISIONED, because readiness now requires the schema. Kept end-to-end on the REAL storage
        // check rather than switched to a stub: what this case is about is that an anonymous request gets
        // a 200 from the deployed composition, and a stub would move the assertion off that composition.
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        await using PersistenceHost host = PersistenceHost.Create(dataDirectory: directory.Path);
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
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        await using PersistenceHost host = PersistenceHost.Create(dataDirectory: directory.Path);
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
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        await using PersistenceHost host = PersistenceHost.Create(dataDirectory: directory.Path);
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

        // Exactly the three entries the composed service produces, in this order: the endpoint's own
        // statement, the storage verdict, and whether the runtime seams the four published contracts are
        // served through are bound. No fourth entry, because no other check is registered - and the
        // projection folds any that were into ONE aggregated entry rather than naming them.
        //
        // THE RUNTIME ENTRY IS NAMED RATHER THAN FOLDED, AND THAT IS THE POINT OF ITS EXISTENCE. A
        // composition missing one of those seams produces an instance that starts, answers 200 here,
        // satisfies the Compose dependency condition and then fails every call on the affected contract.
        // Folding it into the aggregated entry would report the not-ready state without saying which
        // capability was affected; naming it is what an operator can act on.
        Assert.Equal(["self", "sqlite", "runtime"], names);

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

        // AND THE VERDICT IS MACHINE-READABLE, WHICH IS THE HALF THAT ACTUALLY REACHES GATEWAY. The detail
        // above is prose for a human, and retCode is identical for Degraded and Unhealthy alike, so neither
        // separates the two verdicts contract C-10 promises to keep apart. The token cannot live in
        // `status` - RFC 9457 uses that name here for the integer HTTP status, asserted above - so it has a
        // member of its own, and Gateway's aggregator reads exactly this one.
        Assert.Equal("Unhealthy", problem.GetProperty("serviceStatus").GetString());

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
    /// The not-ready body carries the storage check's OWN reason, and the remedy with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>NAMING THE COMPONENT AND NOTHING ELSE IN THE BODY</b> leaves an operator reading
    /// <c>/health</c> with a symptom and no action: "sqlite" is not ready says nothing about whether the
    /// volume is missing or the migrations simply have not been run, and those two call for opposite
    /// responses. The seam distinguishes them internally and records the remedy, so the
    /// only party not told was the one that has to act.
    /// </para>
    /// <para>
    /// Both published values are constants of this codebase - a schema identifier and a documented
    /// command - so this asserts the disclosure rule too: no filesystem path appears, which is what
    /// separates a publishable reason from an unpublishable one on an anonymous route.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Not_ready_body_names_the_storage_reason_and_its_remedy()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        // A real, openable, EMPTY database: reachable engine, unprovisioned schema.
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(directory.Path, "test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };

        await using (SqliteConnection seed = new(builder.ConnectionString))
        {
            await seed.OpenAsync(TestContext.Current.CancellationToken);
        }

        await using PersistenceHost host = PersistenceHost.Create(dataDirectory: directory.Path);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(body);

        string detail = document.RootElement.GetProperty("detail").GetString() ?? string.Empty;

        Assert.Contains("sqlite", detail, StringComparison.Ordinal);
        Assert.Contains("COMPANY", detail, StringComparison.Ordinal);
        Assert.Contains("dotnet ef database update", detail, StringComparison.Ordinal);

        // The check entry carries the same reason, so a consumer reading the checks array rather than the
        // prose gets it too.
        Assert.Contains("COMPANY", body, StringComparison.Ordinal);

        // AND STILL NO PATH. The reason is publishable BECAUSE it is a vetted constant; the directory this
        // deployment mounts is not, and asserting its absence is what keeps the two apart.
        Assert.DoesNotContain(directory.Path, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A description this codebase did not author is NOT echoed, whatever supplied it.
    /// </summary>
    /// <remarks>
    /// The other half of the same change, and the half that keeps it safe. The projection echoes a check's
    /// description only when it is exact-matched against the two checks' published sets, so a substituted
    /// check - or a future registration reusing the name, or a description built from an exception - falls
    /// back to this file's binary prose. The stub reports the description <c>substituted</c>, which is in
    /// neither set.
    /// </remarks>
    [Fact]
    public async Task An_unvetted_check_description_is_not_published()
    {
        await using PersistenceHost host = PersistenceHost.Create(
            storage: StubHealthCheck.Reporting(HealthStatus.Unhealthy));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("substituted", body, StringComparison.Ordinal);
        Assert.Contains(
            "The storage engine did not answer a read-only reachability probe.",
            body,
            StringComparison.Ordinal);
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

        // NAMED RATHER THAN MERELY PRESENT SOMEWHERE IN THE BODY. A substring scan would pass on the
        // prose alone, and prose is not what Gateway's aggregator reads: Degraded and Unhealthy are both
        // answered 503, so the ONLY thing that keeps them apart on the wire is this member.
        using JsonDocument document = JsonDocument.Parse(body);

        Assert.Equal("Degraded", document.RootElement.GetProperty("serviceStatus").GetString());
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
    public async Task A_faulting_evaluator_answers_503_and_keeps_the_exception_off_both_channels()
    {
        RecordingLoggerProvider recorder = new();

        await using PersistenceHost host = PersistenceHost.Create(
            faultEvaluator: true,
            recorder: recorder);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            HealthPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains("self", body, StringComparison.Ordinal);
        Assert.DoesNotContain("evaluator faulted", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);

        // AND OFF THE OPERATOR CHANNEL TOO (constraint C-F). "It only goes to the operator log" is not a
        // sufficient answer on an ANONYMOUS route: an unauthenticated caller decides how often the line
        // runs, so a fault message naming the configured mount path would be published on demand. The
        // exception object is therefore not passed at all - no message, no stack trace - and what remains
        // is a fixed sentence plus the exception's type name, which is a constant of this codebase.
        string record = Assert.Single(
            recorder.Records,
            candidate => candidate.Contains("Readiness evaluation failed", StringComparison.Ordinal));

        Assert.DoesNotContain(
            FaultingHealthCheckService.FaultMessagePathToken,
            record,
            StringComparison.Ordinal);
        Assert.DoesNotContain("evaluator faulted", record, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at PowerFramework", record, StringComparison.Ordinal);

        // ...while the class of fault, the service and the verdict all survive.
        Assert.Contains(nameof(InvalidOperationException), record, StringComparison.Ordinal);
        Assert.Contains("persistence", record, StringComparison.Ordinal);
        Assert.Contains("Unhealthy", record, StringComparison.Ordinal);

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
    public async Task The_probe_reports_healthy_against_a_reachable_and_provisioned_engine()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        // THE REAL MIGRATIONS, because readiness now requires the schema as well as the engine. This is
        // the case that proves the two halves agree: what `dotnet ef database update` produces is exactly
        // what the probe accepts.
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

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
    /// Opening the database records which database was opened and not where this deployment keeps it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE RESOLVED PATH IS THE ONE VALUE IN THAT RECORD THAT IS ABOUT THE DEPLOYMENT RATHER THAN THE
    /// DATABASE, and putting it in three records plus the close record is the easy default. A path is not itself a credential,
    /// but it publishes where storage - and in a deployment that mounts one, where a secret volume - lives, to
    /// every reader of a log that needed none of it in order to read the log.
    /// </para>
    /// <para>
    /// WHAT REPLACES IT LOSES NOTHING AN OPERATOR USED. The bare file name answers "which database", and the
    /// PARITY form of the legacy URI - the form a characterization recording carries - answers "what was the
    /// engine asked", including the journal mode and the integrity-check state. The deployment form of the
    /// URI, which does embed the directory, remains available on the factory for a caller that needs it and
    /// is not written to a log.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Opening_records_the_database_name_and_not_the_resolved_path()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        RecordingLoggerProvider records = new();

        await using SqliteConnectionFactory storage = CreateStorage(
            directory.Path,
            logger: (ILogger<SqliteConnectionFactory>)new TypedRecorder<SqliteConnectionFactory>(records));

        long opened = await storage.OpenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RetCode.OK, opened);

        string written = string.Join("\n", records.Records);

        Assert.Contains("test.db", written, StringComparison.Ordinal);
        Assert.Contains(storage.LegacyUri, written, StringComparison.Ordinal);
        Assert.DoesNotContain(directory.Path, written, StringComparison.Ordinal);
        Assert.DoesNotContain(storage.DatabasePath, written, StringComparison.Ordinal);
        Assert.DoesNotContain(storage.LegacyResolvedUri, written, StringComparison.Ordinal);

        // And the same rule holds for the close record, which is the one that is easiest to forget because it
        // is written at debug.
        await storage.DisposeAsync();

        Assert.DoesNotContain(
            directory.Path,
            string.Join("\n", records.Records),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A data directory that cannot be created is reported as not ready rather than allowed to fault.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO HALVES OF THE READINESS-VERSUS-RUNTIME SPLIT, ASSERTED SIDE BY SIDE ON ONE INPUT, which is the only way to
    /// show they diverge deliberately rather than by accident. The same uncreatable location produces two
    /// quite different behaviours depending on which path asks:
    /// </para>
    /// <para>
    /// READINESS reports not-ready, quietly, creating nothing and naming nothing - because the caller is
    /// anonymous. That half is asserted by the unusable-location case above.
    /// </para>
    /// <para>
    /// THE RUNTIME OPEN throws, immediately - because the caller is the startup sequence and the fault is
    /// structural. Fail-fast is the ported posture and softening it into a warning-and-continue would be
    /// a behavioural change dressed as robustness (AAP 0.1.4). This half is what this case pins, and it is
    /// the assertion that would fail if a future change routed the runtime open through the read-only
    /// probe to make a test go green.
    /// </para>
    /// <para>
    /// <b>⚠ WHAT THIS CASE DELIBERATELY DOES NOT ASSERT, AND WHY.</b> Requiring the CONFIGURED PATH
    /// to appear in the thrown message is the tempting reading, on the grounds that an operator cannot fix a
    /// mount they are not told about. The rest of this estate settles that question the other way, and
    /// asserting it here would make this service internally inconsistent: <c>Program.cs</c>'s internal-trust anchor states
    /// that "the path is not reproduced here, because a startup record must not publish a container's
    /// secret mount layout ... the message names the configuration key instead - the same rule
    /// Configuration/PersistenceOptions.cs applies to its own validation messages", and both sibling
    /// services say the same of their own mounted paths. Measured on a running host, the old form put the
    /// path into the startup output FOUR times - the message once and the ATTACHED file-system exception's
    /// own message the rest - and named the configuration key ZERO times, so the operator was handed the
    /// value they already knew and not the setting to change. The terminal record simultaneously appended
    /// "Configured values are deliberately not quoted", which was false in the one record that said it.
    /// </para>
    /// <para>
    /// So the disclosure half of this case is inverted and the FAIL-FAST half is unchanged and
    /// strengthened: the open still throws, still creates nothing, and now names the key while withholding
    /// the path. The operator's need is met by the key plus an established failure class - a file occupies
    /// the path, the parent is missing, the directory is unwritable, or its creation was refused - which is
    /// more actionable than a single "not writable" sentence covering all four.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_runtime_open_still_fails_fast_where_readiness_merely_reports_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        string blocker = Path.Combine(directory.Path, "blocker");
        await File.WriteAllTextAsync(
            blocker,
            "not a directory",
            TestContext.Current.CancellationToken);

        string unusable = Path.Combine(blocker, "data");

        await using SqliteConnectionFactory storage = CreateStorage(unusable);

        InvalidOperationException fault = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await storage.OpenAsync(TestContext.Current.CancellationToken));

        // THE KEY IS NAMED, so the record identifies the setting to change.
        Assert.Contains(
            DataDirectoryFault.ConfigurationKey,
            fault.Message,
            StringComparison.Ordinal);

        // THE ESTABLISHED FAILURE CLASS IS STATED. A file stands where the parent directory should be, so
        // the record must say that rather than "not writable" - which would send an operator to check
        // permissions on a path whose problem is not permissions.
        Assert.Contains("CONTAINING DIRECTORY", fault.Message, StringComparison.Ordinal);

        // THE CAUSE IS NAMED BY TYPE, so a log pipeline can still group by it. The TYPE NAME ITSELF IS NOT
        // SPELLED HERE, deliberately: which file-system exception a rooted-path-into-a-file produces is a
        // platform detail - an earlier form of this row asserted `IOException` and failed against the
        // derived type this runtime actually raises - and pinning it would make the row about the runtime
        // rather than about the record. What matters is that SOME type is named and that it is not the
        // absent-cause placeholder.
        const string causePreamble = "The file system reported ";

        int causeAt = fault.Message.IndexOf(causePreamble, StringComparison.Ordinal);

        Assert.True(causeAt >= 0, "The record does not name the reported cause at all.");

        string named = fault.Message[(causeAt + causePreamble.Length)..];
        named = named[..named.IndexOf('.', StringComparison.Ordinal)];

        Assert.EndsWith("Exception", named, StringComparison.Ordinal);
        Assert.NotEqual("no exception", named);

        // AND THE PATH IS WITHHELD - message and inner exception alike. Asserted as booleans rather than
        // with Assert.DoesNotContain, because that overload renders both operands and would print the
        // mount layout at exactly the moment the defect it guards against is present.
        Assert.False(
            fault.Message.Contains(unusable, StringComparison.Ordinal),
            "The thrown message reproduces the configured storage path.");
        Assert.False(
            fault.Message.Contains(blocker, StringComparison.Ordinal),
            "The thrown message reproduces the blocking path.");
        Assert.False(
            fault.Message.Contains(directory.Path, StringComparison.Ordinal),
            "The thrown message reproduces the configured storage directory.");

        // The inner exception is deliberately NOT chained: a file-system exception quotes the path in its
        // own Message, so chaining one would republish exactly what the sentence withholds.
        Assert.Null(fault.InnerException);

        // And it still created nothing on the way to failing.
        Assert.True(File.Exists(blocker));
        Assert.False(Directory.Exists(unusable));
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
        // THE REMEDY IS NAMED HERE TOO, because the commonest way to reach this arm is a database file
        // that does not exist yet: the probe opens READ-ONLY by design and so cannot create it, which is
        // why an unprovisioned deployment reports unreachable rather than schema-incomplete.
        Assert.Equal(
            "The storage engine did not answer a read-only reachability probe. The database file may not "
                + "exist yet: provision it with `dotnet ef database update`.",
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

        // Provisioned by the real migrations FIRST - readiness requires the migration history table, so a
        // hand-written CREATE TABLE alone would now be a mis-provisioned database - and only then seeded,
        // so the row count below is a real invariant rather than a constant zero.
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

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

        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

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

    /// <summary>
    /// A wall-clock correction backward cannot extend the positive cache.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CASE THE MONOTONIC SWITCH EXISTS FOR, and the one a wall-clock implementation gets wrong.
    /// Computing the window as the difference of two WALL-CLOCK reads fails when the wall clock steps
    /// backward - an NTP correction, a container resuming on a host whose clock moved, a manual change:
    /// that difference goes NEGATIVE, a negative age is trivially within any window, and the cached
    /// positive answer is served from then on. A bounded staleness silently becomes an unbounded one: this
    /// service would keep reporting READY, and Gateway's gate would keep standing open, for as long as the
    /// clock stayed behind - with storage already gone.
    /// </para>
    /// <para>
    /// The double steps the two timelines INDEPENDENTLY here, which is what a real correction does: the
    /// wall clock jumps back an hour while a couple of minutes of real time pass. Elapsed time therefore
    /// says the window expired and wall-clock arithmetic says it has not, so the two answers differ and
    /// the case can only pass if the seam reads the elapsed one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_wall_clock_correction_backward_does_not_extend_the_positive_cache()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        FrozenClock clock = new(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path, clock);

        SqliteReachabilityHealthCheck check = new(
            storage,
            NullLogger<SqliteReachabilityHealthCheck>.Instance);

        Assert.Equal(
            HealthStatus.Healthy,
            (await check.CheckHealthAsync(
                RegistrationContext(HealthStatus.Unhealthy),
                TestContext.Current.CancellationToken)).Status);

        DateTimeOffset? firstMeasurement = storage.LastReachabilityProbedAt;
        Assert.NotNull(firstMeasurement);

        // The wall clock jumps back an hour; two minutes of real time pass while it does. Two minutes is
        // comfortably past the window, so a fresh measurement is required - and an hour backward is what
        // would have made the old arithmetic serve the cache indefinitely.
        clock.CorrectWallClockBackward(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2));

        Assert.Equal(
            HealthStatus.Healthy,
            (await check.CheckHealthAsync(
                RegistrationContext(HealthStatus.Unhealthy),
                TestContext.Current.CancellationToken)).Status);

        // RE-MEASURED: the stamp moved, and it moved to the corrected wall-clock instant - which is now
        // EARLIER than the first one, proving the window decision did not come from comparing these two.
        Assert.NotEqual(firstMeasurement, storage.LastReachabilityProbedAt);
        Assert.Equal(clock.GetUtcNow(), storage.LastReachabilityProbedAt);
        Assert.True(
            storage.LastReachabilityProbedAt < firstMeasurement,
            "The corrected instant must be earlier, or this case is not exercising a rollback at all.");
    }

    /// <summary>
    /// The window serves the cache at exactly its boundary, and re-measures one tick past it.
    /// </summary>
    /// <remarks>
    /// The comparison is <c>age &lt;= window</c>, so an age exactly equal to the window is INSIDE it. That
    /// boundary is preserved from before the monotonic switch: only the quantity being compared changed,
    /// not the comparison, and pinning it here is what would catch a change from <c>&lt;=</c> to
    /// <c>&lt;</c> made incidentally while editing the arithmetic.
    /// </remarks>
    [Fact]
    public async Task The_window_boundary_is_inclusive()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        FrozenClock clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path, clock);

        TimeSpan window = TimeSpan.FromSeconds(5);

        Assert.True(await storage.IsReachableAsync(window, TestContext.Current.CancellationToken));

        DateTimeOffset? measured = storage.LastReachabilityProbedAt;

        // EXACTLY the window: still inside, so the cached answer is served and the stamp does not move.
        clock.Advance(window);

        Assert.True(await storage.IsReachableAsync(window, TestContext.Current.CancellationToken));
        Assert.Equal(measured, storage.LastReachabilityProbedAt);

        // One tick past: re-measured.
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.True(await storage.IsReachableAsync(window, TestContext.Current.CancellationToken));
        Assert.NotEqual(measured, storage.LastReachabilityProbedAt);
    }

    /// <summary>
    /// A failure is never cached, so a recovery is reported at the very next ask.
    /// </summary>
    /// <remarks>
    /// The asymmetry is deliberate and is what bounds the cost of the window to one edge of a transition
    /// only. This case drives the transition for real: an unprovisioned database, then the migrations, then
    /// the same seam on a frozen clock - so if a failure WERE cached the second answer would still be
    /// not-ready and this case would fail.
    /// </remarks>
    [Fact]
    public async Task A_failure_is_not_cached_so_a_recovery_is_reported_immediately()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        FrozenClock clock = new(new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero));
        await using SqliteConnectionFactory storage = CreateStorage(directory.Path, clock);

        TimeSpan window = TimeSpan.FromMinutes(10);

        // No database at all yet.
        Assert.False(await storage.IsReachableAsync(window, TestContext.Current.CancellationToken));
        Assert.Equal(StorageReadiness.Unreachable, storage.LastReadiness);

        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        // THE CLOCK HAS NOT MOVED, and the window is ten minutes - so a cached failure would still be
        // serving. It is not, because failures are never cached.
        Assert.True(await storage.IsReachableAsync(window, TestContext.Current.CancellationToken));
        Assert.Equal(StorageReadiness.Ready, storage.LastReadiness);
    }

    /// <summary>
    /// When the runtime connection is already open, the probe BORROWS it and leaves it open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER BRANCH OF THE READINESS PROBE, and the one where a mistake would be most damaging.
    /// Borrowing is deliberate: the ambient connection is the handle requests actually run on, so proving
    /// IT answers is a truer readiness statement than proving some other handle does, and it spends no
    /// extra file descriptor per poll on a route an orchestrator polls continuously.
    /// </para>
    /// <para>
    /// WHAT MUST NOT HAPPEN IS THE PROBE DISPOSING WHAT IT BORROWED. The disposal in that member is
    /// scoped to the handle it opened ITSELF, and if it were not, an anonymous request would close the
    /// connection every credentialled request depends on - a denial of service driven from an
    /// unauthenticated surface, which is a worse outcome than the mutation the split was closing. The
    /// catalogue call at the end is the assertion that actually proves it: that member throws outright if
    /// the ambient connection is not open, so it cannot pass against a closed one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_probe_borrows_the_runtime_connection_when_one_is_open_and_leaves_it_open()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        await ProvisionSchemaAsync(directory.Path, TestContext.Current.CancellationToken);

        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        // The RUNTIME open - credentialled, creative, and the thing readiness must never perform itself.
        Assert.Equal(RetCode.OK, await storage.OpenAsync(TestContext.Current.CancellationToken));
        Assert.True(storage.IsOpened);

        for (int poll = 0; poll < 4; poll++)
        {
            Assert.True(
                await storage.IsReachableAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        }

        Assert.Equal(StorageReadiness.Ready, storage.LastReadiness);

        // STILL OPEN after four probes.
        Assert.True(storage.IsOpened);

        // And genuinely usable, not merely flagged as open: this member throws if the ambient connection
        // is closed, so it can only answer here if the borrow left it intact.
        Assert.True(await storage.IsTableExistsAsync("COMPANY", TestContext.Current.CancellationToken));
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
    //  ANONYMOUS READINESS DISCLOSES NOTHING  (constraint C-F)
    // ==============================================================================================
    //
    //  THE THREAT IS THE CALLER, NOT THE SENSITIVITY OF ANY ONE VALUE. /health is anonymous on all four
    //  services because the orchestrator's own probe has no credential, so an unauthenticated caller
    //  decides how often the readiness path runs and therefore how often whatever it writes is emitted.
    //  "It only goes to the operator channel" is not an answer for a line an anonymous caller can drive.
    //
    //  AND THE MESSAGES THAT ARRIVE HERE ARE PRECISELY THE ONES THAT NAME THINGS. The data-directory
    //  fault embeds the CONFIGURED MOUNT PATH in its own message - deliberately, so an operator can fix
    //  the volume - and a provider fault for a failed open names the database file almost every time.
    //  A passed exception would also carry a stack trace naming internal types.
    //
    //  WHAT REPLACES IT IS STILL ACTIONABLE: a fixed sentence, the fault's TYPE NAME - a compile-time
    //  constant of this codebase, never a configured value - and the numeric codes. The path is still
    //  logged once on the SUCCESSFUL open, and the fail-fast startup path still surfaces the whole
    //  exception, where no anonymous caller can reach it.

    /// <summary>
    /// A readiness probe against an unusable location reports not ready and discloses no path.
    /// </summary>
    /// <remarks>
    /// TWO PROPERTIES AT ONCE, AND THE FIRST ONE IS WHY THE SECOND IS EASY. The readiness path does not
    /// attempt to create the data directory at all, so a location whose parent is an ordinary FILE - a
    /// location no filesystem will turn into a directory - is simply unreachable rather than a structural
    /// fault to be caught. That means the verdict comes from the seam's own recorded failure instead of
    /// from an exception whose message embeds the configured path, which is the disclosure this case
    /// exists to rule out either way.
    /// </remarks>
    [Fact]
    public async Task An_unusable_location_reports_not_ready_and_logs_no_path()
    {
        using TemporaryDataDirectory parent = TemporaryDataDirectory.Create();
        string blocker = Path.Combine(parent.Path, "blocker");
        await File.WriteAllTextAsync(blocker, "not a directory", TestContext.Current.CancellationToken);

        string unusable = Path.Combine(blocker, "pfw-secret-mount-name");

        await using SqliteConnectionFactory storage = CreateStorage(unusable);

        RecordingLogger<SqliteReachabilityHealthCheck> logger = new();
        SqliteReachabilityHealthCheck check = new(storage, logger);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        // A structured not-ready rather than a thrown fault, so the gate can observe it.
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(StorageReadiness.Unreachable, storage.LastReadiness);

        // NOTHING WAS CREATED, which is the whole property of the split: the blocker is still a file and no
        // directory appeared beside or beneath it.
        Assert.True(File.Exists(blocker), "The readiness path must not have replaced the blocking file.");
        Assert.False(Directory.Exists(unusable), "The readiness path must not create the data directory.");

        // THE ANONYMOUS BODY carries no path, and no exception travels with the verdict.
        Assert.DoesNotContain(
            "pfw-secret-mount-name",
            result.Description ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Null(result.Exception);

        // THE OPERATOR RECORD carries no path, no directory component and no provider prose...
        string record = Assert.Single(logger.Records);
        Assert.DoesNotContain("pfw-secret-mount-name", record, StringComparison.Ordinal);
        Assert.DoesNotContain(unusable, record, StringComparison.Ordinal);
        Assert.DoesNotContain(parent.Path, record, StringComparison.Ordinal);

        // ...and still says what happened, in which state, and with which provider code.
        Assert.Contains("did not satisfy the read-only readiness probe", record, StringComparison.Ordinal);
        Assert.Contains(nameof(StorageReadiness.Unreachable), record, StringComparison.Ordinal);
        Assert.Contains("provider result code", record, StringComparison.Ordinal);
    }

    /// <summary>
    /// An anonymous probe against an EMPTY but usable location creates absolutely nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CENTRAL CASE FOR THE SPLIT. A readiness path that ran the RUNTIME open would bring the data
    /// directory into being, open under the legacy <c>mode=rwc</c> grammar so the database FILE is
    /// created, and set the journal mode - so an unauthenticated request to <c>/health</c> would perform
    /// three filesystem mutations and then report READY against a database it had just invented. That is
    /// both a mutation driven from an unauthenticated surface and a false positive: the gate Gateway hangs
    /// on would open for a service with no data.
    /// </para>
    /// <para>
    /// Asserting the directory listing is EMPTY rather than just checking for the database file is
    /// deliberate - a journal, a write-ahead log or a shared-memory file would each be a mutation too,
    /// and each has a different name.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_anonymous_probe_against_an_empty_location_creates_nothing_and_reports_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        string nested = Path.Combine(directory.Path, "not-yet-created");

        await using SqliteConnectionFactory storage = CreateStorage(nested);

        SqliteReachabilityHealthCheck check = new(
            storage,
            NullLogger<SqliteReachabilityHealthCheck>.Instance);

        for (int poll = 0; poll < 3; poll++)
        {
            HealthCheckResult result = await check.CheckHealthAsync(
                RegistrationContext(HealthStatus.Unhealthy),
                TestContext.Current.CancellationToken);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        // NO DIRECTORY, therefore no database file, no journal and no write-ahead log.
        Assert.False(
            Directory.Exists(nested),
            "An anonymous readiness probe must not create the configured data directory.");

        // And the parent is untouched: nothing was written beside the intended location either.
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));

        Assert.Equal(StorageReadiness.Unreachable, storage.LastReadiness);
    }

    /// <summary>
    /// Storage that exists but has no schema is reported not ready, and distinctly so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CASE FOR PROBING THE SCHEMA AND NOT ONLY THE ENGINE. A constant scalar passes just as happily
    /// against an empty database, so without a schema check a service whose volume mounted correctly and
    /// whose migrations never ran reports READY - and then fails every request it receives. That is the
    /// worst shape of readiness bug: the gate opens, traffic arrives, and nothing works.
    /// </para>
    /// <para>
    /// The verdict is DISTINCT from unreachable rather than merely negative, because the two call for
    /// different actions: this one means the storage is fine and the migrations have not been applied.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_engine_that_answers_without_a_schema_is_reported_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        // A real, openable, EMPTY database - exactly what a fresh volume with unrun migrations looks like.
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(directory.Path, "test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        await using (SqliteConnection seed = new(builder.ConnectionString))
        {
            await seed.OpenAsync(TestContext.Current.CancellationToken);
        }

        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(
            storage,
            NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(StorageReadiness.SchemaIncomplete, storage.LastReadiness);

        // A DISTINCT description, so an operator is not sent to check a mount that is already correct -
        // and an ACTIONABLE one: it names the missing table and the command that provisions it. Both are
        // constants of this codebase, not configured values, so publishing them on the anonymous route
        // discloses nothing while withholding them left the response with no remedy in it at all.
        Assert.Equal(
            "The storage engine answered but the required COMPANY schema is not provisioned. Apply the "
            + "migrations with `dotnet ef database update`.",
            result.Description);

        // The seam's in-process diagnostic DOES name the table it looked for, which is a constant of this
        // codebase rather than a configured value - and it is the authenticated half of the answer.
        Assert.Contains("COMPANY", storage.SqlErrText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A schema created WITHOUT the migration tool is reported not ready.
    /// </summary>
    /// <remarks>
    /// The application table alone is not a provisioned database. Migrations are the only provisioning
    /// path this service has, and a database holding the table but no migration history is one the
    /// migration tool would try to re-apply its first migration against, and fail. This case pins that
    /// the probe rejects it rather than reporting ready on a half-provisioned schema - and it is the case
    /// that would fail if a future change swapped <c>Migrate</c> for <c>EnsureCreated</c> anywhere.
    /// </remarks>
    [Fact]
    public async Task A_schema_without_the_migration_history_is_reported_not_ready()
    {
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();

        // The legacy DDL shape, applied by hand: COMPANY exists, the history table does not.
        _ = await SeedCompanyTableAsync(Path.Combine(directory.Path, "test.db"));

        await using SqliteConnectionFactory storage = CreateStorage(directory.Path);

        SqliteReachabilityHealthCheck check = new(
            storage,
            NullLogger<SqliteReachabilityHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            RegistrationContext(HealthStatus.Unhealthy),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(StorageReadiness.SchemaIncomplete, storage.LastReadiness);
        Assert.Contains("migration history", storage.SqlErrText, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed RUNTIME open logs the preserved provider code and nothing else.
    /// </summary>
    /// <remarks>
    /// The runtime open is not an anonymous path - it runs at startup and on a credentialled request - but
    /// its log arm is narrowed all the same, because a record naming the database file is of no more use
    /// to an operator than the provider code and is of considerably more use to anyone else. The full
    /// text is still available in process on the seam's own members.
    /// </remarks>
    [Fact]
    public async Task A_failed_open_logs_the_provider_code_without_the_path_or_the_provider_message()
    {
        // The database FILE NAME is occupied by a directory, so the provider refuses the open with
        // SQLITE_CANTOPEN and a message that names the file. The seam's own arm is what is under test.
        using TemporaryDataDirectory directory = TemporaryDataDirectory.Create();
        _ = Directory.CreateDirectory(Path.Combine(directory.Path, "test.db"));

        RecordingLogger<SqliteConnectionFactory> logger = new();

        await using SqliteConnectionFactory storage = new(
            Options.Create(new PersistenceOptions
            {
                Sqlite = new SqliteOptions
                {
                    DataDirectory = directory.Path,
                    DatabaseFileName = "test.db",
                    Mode = "rwc",
                    Journal = "DELETE",
                },
            }),
            logger,
            TimeProvider.System);

        // THROUGH THE RUNTIME OPEN, not the readiness probe. The readiness path goes nowhere
        // near this arm - readiness and runtime opening are separate paths - so exercising it means calling
        // the member that owns it. The arm is worth pinning: it is the one an operator reads when a real
        // deployment cannot open its database at startup.
        Assert.NotEqual(
            RetCode.OK,
            await storage.OpenAsync(TestContext.Current.CancellationToken));

        string record = Assert.Single(logger.Records);

        // No path, no file name, no provider prose.
        Assert.DoesNotContain(directory.Path, record, StringComparison.Ordinal);
        Assert.DoesNotContain("test.db", record, StringComparison.Ordinal);
        Assert.DoesNotContain("unable to open", record, StringComparison.OrdinalIgnoreCase);

        // The PRESERVED SQLITE_* constant is what locates the cause, and it distinguishes a missing file
        // from a permission refusal from a corrupt header without naming any of them.
        Assert.Contains("could not be opened", record, StringComparison.Ordinal);
        Assert.Contains(
            RetCode.SQLITE_CANTOPEN.ToString(System.Globalization.CultureInfo.InvariantCulture),
            record,
            StringComparison.Ordinal);

        // AND THE TEXT IS NOT LOST, only unlogged: the in-process readers still answer with it, which is
        // what keeps an authenticated surface as diagnosable as it was.
        Assert.NotEqual(string.Empty, storage.SqlErrText);
        Assert.Equal(RetCode.SQLITE_CANTOPEN, storage.SqlDbCode);
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
    /// <param name="logger">The logger, or <see langword="null"/> to record nothing.</param>
    /// <returns>A seam that has not yet opened anything.</returns>
    private static SqliteConnectionFactory CreateStorage(
        string dataDirectory,
        TimeProvider? clock = null,
        ILogger<SqliteConnectionFactory>? logger = null)
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
            logger ?? NullLogger<SqliteConnectionFactory>.Instance,
            clock ?? TimeProvider.System);
    }

    /// <summary>
    /// Provisions the real schema in a data directory by applying the real migrations.
    /// </summary>
    /// <param name="dataDirectory">The directory the database file should live in.</param>
    /// <param name="cancellationToken">The test's token.</param>
    /// <returns>A task that completes when the schema is present.</returns>
    /// <remarks>
    /// <para>
    /// THE REAL MIGRATIONS RATHER THAN HAND-WRITTEN DDL, deliberately. The readiness probe now requires
    /// both the application table and the migration history table, and the only provisioning path this
    /// service has is <c>dotnet ef database update</c> - so applying the migrations here is what proves
    /// the DEPLOYED provisioning path satisfies the DEPLOYED readiness check. Hand-written
    /// <c>CREATE TABLE</c> statements would satisfy the probe while proving nothing about that pairing,
    /// and would silently keep passing if a future migration and the probe drifted apart.
    /// </para>
    /// <para>
    /// <c>Migrate</c> and not <c>EnsureCreated</c>: the latter creates the schema WITHOUT writing the
    /// history table, which is precisely the mis-provisioned state the probe exists to reject, so using
    /// it here would make every readiness case fail for the right reason at the wrong time.
    /// </para>
    /// </remarks>
    private static async Task ProvisionSchemaAsync(
        string dataDirectory,
        CancellationToken cancellationToken)
    {
        SqliteConnectionStringBuilder builder = new()
        {
            DataSource = Path.Combine(dataDirectory, "test.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,

            // POOLING OFF, a TEST-HARNESS necessity and not a product concern.
            // Microsoft.Data.Sqlite pools by connection string, so disposing the context returns its
            // connection to the pool WITHOUT closing the underlying handle - and a later RUNTIME open
            // would then block for its whole busy timeout trying to set the journal mode, which needs an
            // exclusive lock. In production the migration tool is a separate PROCESS that exits, so no
            // handle survives it and nothing needs disabling; here the two share one process.
            Pooling = false,
        };

        DbContextOptions<PowerFrameworkDbContext> options =
            new DbContextOptionsBuilder<PowerFrameworkDbContext>()
                .UseSqlite(builder.ConnectionString)
                .Options;

        await using PowerFrameworkDbContext context = new(options);

        await context.Database.MigrateAsync(cancellationToken);
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
        /// <summary>The wall-clock instant.</summary>
        private DateTimeOffset _now;

        /// <summary>
        /// The MONOTONIC tick count, held INDEPENDENTLY of the wall-clock instant.
        /// </summary>
        /// <remarks>
        /// <b>TWO SEPARATE TIMELINES, AND THE SEPARATION IS THE WHOLE VALUE OF THIS DOUBLE.</b> On a real
        /// machine the two are independent in exactly one direction that matters: an NTP correction, a
        /// resumed host or a manual change steps the WALL clock, in either direction, while the monotonic
        /// clock keeps counting forward regardless. A double that derived one from the other could not
        /// reproduce that, and so could not tell a window measured on elapsed time from one measured on
        /// the difference of two wall-clock reads - which is the only distinction worth testing here.
        /// </remarks>
        private long _ticks;

        /// <summary>Initializes the clock with both timelines at the same origin.</summary>
        /// <param name="start">The instant the wall clock starts at.</param>
        internal FrozenClock(DateTimeOffset start)
        {
            _now = start;
            _ticks = start.UtcTicks;
        }

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow() => _now;

        /// <inheritdoc/>
        /// <remarks>One tick per tick, so a caller can reason in <see cref="TimeSpan"/> directly.</remarks>
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc/>
        /// <remarks>
        /// <b>OVERRIDING THIS PAIR IS MANDATORY, NOT OPTIONAL, AND FORGETTING IT FAILS SILENTLY IN THE
        /// WRONG DIRECTION.</b> The seam decides its cache window with
        /// <see cref="TimeProvider.GetElapsedTime(long, long)"/>, which is computed from these two
        /// members. A subclass overriding only <see cref="GetUtcNow"/> inherits the base implementation,
        /// which reads the REAL machine timestamp - so the window would be measured on time the test does
        /// not control while the test advanced only the instant, and a case asserting the window would
        /// pass or fail on how long the test host happened to take.
        /// </remarks>
        public override long GetTimestamp() => _ticks;

        /// <summary>Moves BOTH timelines forward, which is what ordinary time passing looks like.</summary>
        /// <param name="by">How far to advance.</param>
        internal void Advance(TimeSpan by)
        {
            _now = _now.Add(by);
            _ticks += by.Ticks;
        }

        /// <summary>
        /// Steps the WALL clock backward while the monotonic timeline continues forward - an NTP
        /// correction, precisely.
        /// </summary>
        /// <param name="wallClockStep">How far the wall clock jumps back.</param>
        /// <param name="monotonicStep">How much real time passes while it does.</param>
        internal void CorrectWallClockBackward(TimeSpan wallClockStep, TimeSpan monotonicStep)
        {
            _now = _now.Add(-wallClockStep);
            _ticks += monotonicStep.Ticks;
        }
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
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>The formatted records, in order.</summary>
        internal List<string> Records { get; } = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        /// <remarks>
        /// Only the FORMATTED record is kept. That is what a sink writes, so it is the thing a disclosure
        /// claim has to be made about - asserting on the template alone would pass while the arguments
        /// leaked.
        /// </remarks>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Records.Add(formatter(state, exception));
        }
    }

    /// <summary>A provider whose loggers record every formatted record.</summary>
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
    /// Presents an existing recording provider as a typed logger.
    /// </summary>
    /// <typeparam name="TCategory">The category the consumer asks for.</typeparam>
    /// <param name="records">The provider every record is appended to.</param>
    /// <remarks>
    /// The provider already records everything; this exists only because a constructor asks for
    /// <c>ILogger&lt;T&gt;</c> rather than <c>ILogger</c>, and adding a second recorder would give two places
    /// for the capture rule to drift.
    /// </remarks>
    private sealed class TypedRecorder<TCategory>(RecordingLoggerProvider records) : ILogger<TCategory>
    {
        /// <summary>The untyped logger every call is forwarded to.</summary>
        private readonly ILogger _inner = records.CreateLogger(typeof(TCategory).FullName!);

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => _inner.BeginScope(state);

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _inner.Log(logLevel, eventId, state, exception, formatter);
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
        /// <summary>
        /// A path-shaped fragment planted in the fault message. It stands for the configured mount path a
        /// real options-binding fault would carry, and it is distinctive enough that a leak on either
        /// channel is unmistakable.
        /// </summary>
        internal const string FaultMessagePathToken = "/mnt/pfw-secret-volume-name/test.db";

        /// <inheritdoc/>
        public override Task<HealthReport> CheckHealthAsync(
            Func<HealthCheckRegistration, bool>? predicate,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "The readiness evaluator faulted deliberately at "
                + FaultMessagePathToken
                + ". Nothing about this message may reach the anonymous response body OR the operator "
                + "log, because an unauthenticated caller decides how often both are written.");
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

                // STARTUP PROVISIONING IS SWITCHED OFF FOR EVERY HOST IN THIS FILE, AND THAT IS WHAT MAKES
                // THIS FILE'S SUBJECT REACHABLE AT ALL. Switched ON, the startup sequence applies pending
                // migrations before the pipeline is built, so a host started against an empty or absent
                // database repairs it and then reports READY - which is correct for an orchestrated
                // deployment and fatal for a suite whose whole subject is what the probe says about an
                // unprovisioned or unreachable engine. OFF is the shipped default, so this line pins the
                // posture rather than changing it; both states also remain genuinely reachable in
                // production - under this setting, which is the documented posture for a characterization
                // capture run, and under a volume replaced beneath an already-running container. The
                // provisioning behaviour itself is asserted by CompositionRootTests against the real graph
                // and by SchemaProvisionerTests against the step.
                ["Schema:ApplyMigrationsOnStartup"] = "false",
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
