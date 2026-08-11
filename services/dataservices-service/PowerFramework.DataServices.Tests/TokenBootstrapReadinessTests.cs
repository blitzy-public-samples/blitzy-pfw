// ==================================================================================================
//  TOKEN-BOOTSTRAP READINESS - WHAT THIS FILE ASSERTS THAT NOTHING ELSE DOES
//
//  Every outward call this service makes carries a bearer token: all four of Persistence's contracts
//  require one, and so does each of Security's seventeen cryptographic operations. The only way to get
//  one is POST /v1/tokens, which contract C-01 protects with MUTUAL TLS AND NOTHING ELSE - because a
//  caller cannot present a bearer token in order to obtain its first bearer token.
//
//  A deployment that mounts no client certificate is a LEGITIMATE STARTUP STATE and deliberately not a
//  startup failure: orchestration/.env.example records that a local bring-up without a generated
//  certificate set genuinely is that state. It is also a state in which this service cannot serve a
//  single request. Before this check, such an instance
//
//      started, answered GET /health with 200, satisfied the Compose `depends_on:
//      condition: service_healthy` gate, let Gateway start behind it, and then had every
//      outward call refused for want of a caller identity
//
//  and readiness - the one question whose whole job is to answer "can this instance serve" - said yes.
//  The rows below are what hold that answer honest.
//
//  THE DISTINCTION THIS FILE TURNS ON: "does not refuse to start" and "reports ready" are different
//  statements, and readiness exists precisely to express the gap between them. Nothing here makes an
//  unset certificate pair a startup failure; the documented behaviour is unchanged. What changes is that
//  the instance stops CLAIMING it can serve.
//
//  WHAT IS DELIBERATELY NOT ASSERTED: nothing about whether Security is reachable. That is an upstream
//  question and C-10 gives every upstream question to Gateway - a leaf that reported itself unready
//  because an upstream was still starting would make the dependency chain oscillate instead of settle.
//  Nothing here performs any I/O at all: no token is minted, no handshake is attempted, no metadata is
//  fetched and no file is opened, so no row can pass or fail on network conditions.
// ==================================================================================================

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Endpoints;
using Xunit;

namespace PowerFramework.DataServices.Tests;

/// <summary>
/// The readiness check that reports whether this service can obtain a service token at all.
/// </summary>
public sealed class TokenBootstrapReadinessTests
{
    /// <summary>A certificate path, which is never opened by anything in this file.</summary>
    private const string CertificatePath = "/run/secrets/security-mtls/dataservices-client.crt";

    /// <summary>The matching key path, likewise never opened.</summary>
    private const string CertificateKeyPath = "/run/secrets/security-mtls/dataservices-client.key";

    /// <summary>The issuance address, which is never contacted.</summary>
    private const string IssuanceAddress = "https://security-service:5104";

    /// <summary>The prose a ready verdict carries.</summary>
    private const string ReadyDescription =
        "The credential material this service must present in order to obtain a service token is "
        + "configured.";

    /// <summary>The prose a not-ready verdict carries.</summary>
    private const string NotReadyDescription =
        "The credential material this service must present in order to obtain a service token is not "
        + "configured, so every call that needs a token would be refused.";

    /// <summary>
    /// A fully configured deployment reports ready.
    /// </summary>
    [Fact]
    public async Task A_configured_bootstrap_reports_ready()
    {
        HealthCheckResult result = await EvaluateAsync(
            Configured(),
            HealthStatus.Degraded,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(ReadyDescription, result.Description);
    }

    /// <summary>
    /// Every way the credential material can be absent reports not ready.
    /// </summary>
    /// <param name="shape">A description of the configuration, used to name the case.</param>
    /// <param name="certificate">The configured certificate path.</param>
    /// <param name="key">The configured key path.</param>
    /// <remarks>
    /// <para>
    /// HALF-SET IS COVERED ALONGSIDE UNSET, and the two arrive here for different reasons. Unset is a
    /// legitimate deployment state that simply cannot reach the issuance edge. Half-set is a typo, and
    /// the options validator refuses it at startup - but this check must not depend on that refusal
    /// having happened, because a host that bound the group itself has no validator in the path. Both
    /// answer the same question identically: a certificate cannot complete a handshake without its key,
    /// and a key has nothing to present without its certificate.
    /// </para>
    /// <para>
    /// EVERY ROW HERE ALSO CONFIGURES NO SHARED SECRET, WHICH IS WHAT MAKES THEM NOT-READY. Contract C-01
    /// accepts two caller credentials as alternatives, so an absent certificate pair is only fatal when the
    /// other scheme is absent too - that is the subject of
    /// <see cref="A_configured_shared_secret_is_ready_whatever_the_certificate_pair_looks_like"/>. What
    /// remains true on these rows is that half a certificate pair cannot complete a handshake: a
    /// certificate cannot be presented without its key and a key has nothing to present without its
    /// certificate, so a deployment that chose the certificate scheme and mis-set it is not ready.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("neither path set", "", "")]
    [InlineData("only the certificate set", CertificatePath, "")]
    [InlineData("only the key set", "", CertificateKeyPath)]
    [InlineData("both set to whitespace", "   ", "\t")]
    public async Task An_unmounted_credential_reports_not_ready(
        string shape,
        string certificate,
        string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(shape));

        DataServicesOptions options = Configured();
        options.Security.MutualTls.CertificatePath = certificate;
        options.Security.MutualTls.CertificateKeyPath = key;

        // Stated rather than implied: these rows are about the certificate scheme, and the OTHER scheme is
        // deliberately absent. Configuring it would make every row below ready, which is the point of the
        // sibling theory.
        Assert.True(string.IsNullOrWhiteSpace(options.Security.ClientSecret));

        HealthCheckResult result = await EvaluateAsync(
            options,
            HealthStatus.Degraded,
            TestContext.Current.CancellationToken);

        // DEGRADED RATHER THAN UNHEALTHY: the material has not FAILED, it has not arrived, and the
        // orchestration layer owns the mount. Both verdicts are answered 503, so the readiness gate
        // behaves identically - the distinction is what an operator is told to do about it.
        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(NotReadyDescription, result.Description);
    }

    /// <summary>
    /// A configured shared secret reports READY whatever the certificate pair looks like, because the two
    /// schemes are alternatives.
    /// </summary>
    /// <param name="shape">The certificate-pair shape this row pairs with the secret.</param>
    /// <param name="certificate">The configured certificate path.</param>
    /// <param name="key">The configured key path.</param>
    /// <remarks>
    /// <para>
    /// <b>THE FIRST ROW IS THE DOCUMENTED BRING-UP, AND IT USED TO REPORT NOT READY.</b> This check
    /// required BOTH HALVES OF THE CERTIFICATE PAIR and nothing else, so a deployment supplying
    /// <c>SECURITY_CLIENT_SECRET_DATASERVICES</c> and leaving both paths deliberately empty - which
    /// <c>orchestration/.env.example</c> section 6.3 records as a SUPPORTED state, naming the <c>Basic</c>
    /// scheme the primary one - reported not ready forever. This service's readiness is one of the three
    /// Gateway's own gate waits on, so the consequence was a stack that never came up.
    /// </para>
    /// <para>
    /// THE HALF-SET ROWS ARE INCLUDED ON PURPOSE. A mis-set certificate path alongside a working secret is
    /// still a deployment that can obtain a token, so it is READY: the certificate scheme is the
    /// alternative it is not using. The startup validator is what tells an operator about the typo, and it
    /// does so without pretending the service cannot work.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("no certificate pair at all", "", "")]
    [InlineData("only the certificate set", CertificatePath, "")]
    [InlineData("only the key set", "", CertificateKeyPath)]
    [InlineData("a complete pair as well", CertificatePath, CertificateKeyPath)]
    public async Task A_configured_shared_secret_is_ready_whatever_the_certificate_pair_looks_like(
        string shape,
        string certificate,
        string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(shape));

        DataServicesOptions options = Configured();
        options.Security.MutualTls.CertificatePath = certificate;
        options.Security.MutualTls.CertificateKeyPath = key;

        // NOT MATERIAL AND NOT CREDENTIAL-SHAPED. The check reads only whether a secret is PRESENT; it
        // never authenticates with it, so a marker is the honest value to write here (C-F).
        options.Security.ClientSecret = "readiness-not-a-real-secret";

        HealthCheckResult result = await EvaluateAsync(
            options,
            HealthStatus.Degraded,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(ReadyDescription, result.Description);
    }

    /// <summary>
    /// An unconfigured issuance address reports not ready, because a token cannot be requested from
    /// nowhere.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_issuance_address_reports_not_ready()
    {
        DataServicesOptions options = Configured();
        options.Security.BaseAddress = string.Empty;

        HealthCheckResult result = await EvaluateAsync(
            options,
            HealthStatus.Degraded,
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Equal(NotReadyDescription, result.Description);
    }

    /// <summary>
    /// The published description names no path, no address and no configuration key, on either verdict.
    /// </summary>
    /// <remarks>
    /// A PATH NAMES WHERE PRIVATE KEY MATERIAL IS MOUNTED, and <c>GET /health</c> is anonymous - so
    /// everything this check publishes is world-readable by anything that can reach the port. The setting
    /// NAMES an operator needs go to the operator channel; the response carries authored prose only.
    /// </remarks>
    [Fact]
    public async Task The_published_description_names_no_path_address_or_setting()
    {
        DataServicesOptions unmounted = Configured();
        unmounted.Security.MutualTls.CertificatePath = string.Empty;
        unmounted.Security.MutualTls.CertificateKeyPath = string.Empty;

        foreach (DataServicesOptions options in (DataServicesOptions[])[Configured(), unmounted])
        {
            HealthCheckResult result = await EvaluateAsync(
                options,
                HealthStatus.Degraded,
                TestContext.Current.CancellationToken);

            Assert.NotNull(result.Description);

            Assert.DoesNotContain(CertificatePath, result.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain(CertificateKeyPath, result.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain(IssuanceAddress, result.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain("MutualTls", result.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain("BaseAddress", result.Description!, StringComparison.Ordinal);

            // No data dictionary and no exception either: the framework would carry both onto the report
            // entry, and the entry is what the projection reads.
            Assert.Empty(result.Data);
            Assert.Null(result.Exception);
        }
    }

    /// <summary>
    /// A host that supplies its own bootstrap is not judged against the shipped client's preconditions.
    /// </summary>
    /// <remarks>
    /// THE PRECONDITIONS BELONG TO THE SHIPPED CLIENT, NOT TO THE SERVICE. A host registering its own
    /// <see cref="IServiceTokenProvider"/> is asserting that it obtains credentials by some other
    /// mechanism, whose preconditions this file cannot know - so inventing a verdict about them would be
    /// a guess, and reporting not-ready would make every such host permanently unready. The row asserts
    /// the state that would otherwise be reported not-ready is reported ready here, which is what makes
    /// the discrimination observable rather than incidental.
    /// </remarks>
    [Fact]
    public async Task A_host_supplied_bootstrap_is_reported_ready_and_says_so()
    {
        DataServicesOptions unmounted = Configured();
        unmounted.Security.MutualTls.CertificatePath = string.Empty;
        unmounted.Security.MutualTls.CertificateKeyPath = string.Empty;

        TokenBootstrapHealthCheck check = new(
            Options.Create(unmounted),
            new SubstitutedTokenBootstrap(),
            NullLogger<TokenBootstrapHealthCheck>.Instance);

        HealthCheckResult result = await check.CheckHealthAsync(
            Registration(HealthStatus.Degraded),
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(
            "The service token bootstrap is supplied by the host rather than by this service's own "
                + "client, so this service holds no credential precondition of its own.",
            result.Description);
    }

    /// <summary>
    /// The registration's own failure status is honoured rather than assumed.
    /// </summary>
    /// <param name="failureStatus">The status the registration declares for a failure.</param>
    [Theory]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unhealthy)]
    public async Task The_registrations_own_failure_status_is_honoured(HealthStatus failureStatus)
    {
        DataServicesOptions options = Configured();
        options.Security.MutualTls.CertificatePath = string.Empty;
        options.Security.MutualTls.CertificateKeyPath = string.Empty;

        HealthCheckResult result = await EvaluateAsync(
            options,
            failureStatus,
            TestContext.Current.CancellationToken);

        Assert.Equal(failureStatus, result.Status);
    }

    /// <summary>
    /// A cancelled evaluation stops rather than producing a verdict nobody will read.
    /// </summary>
    [Fact]
    public async Task A_cancelled_evaluation_stops_rather_than_answering()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EvaluateAsync(Configured(), HealthStatus.Degraded, cancelled.Token));
    }

    /// <summary>
    /// The composed registration reaches the published body as a NAMED entry, and the aggregate follows
    /// it: 200 with a ready credential entry, 503 with a degraded one and a machine-readable verdict.
    /// </summary>
    /// <param name="mounted">Whether the credential material is configured.</param>
    /// <param name="expectedStatus">The status the route must answer with.</param>
    /// <param name="expectedEntryStatus">The verdict the credential entry must carry.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THAT PROVES THE WIRING, and it is separate from the rows above on purpose. Those
    /// drive the check directly and establish WHAT it decides; this one drives the registration, the
    /// evaluator, the projection and the response, and establishes that the decision actually reaches a
    /// caller. A check that decided correctly but was never registered would pass every row above.
    /// </para>
    /// <para>
    /// The entry is asserted BY NAME rather than by count, because the name is what tells an operator
    /// which precondition is unmet rather than only that something is - and because folding it into the
    /// aggregated entry would have satisfied the status code while losing exactly that.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, HttpStatusCode.OK, "Healthy")]
    [InlineData(false, HttpStatusCode.ServiceUnavailable, "Degraded")]
    public async Task The_composed_registration_reports_the_credential_entry_by_name(
        bool mounted,
        HttpStatusCode expectedStatus,
        string expectedEntryStatus)
    {
        DataServicesOptions options = Configured();

        if (!mounted)
        {
            options.Security.MutualTls.CertificatePath = string.Empty;
            options.Security.MutualTls.CertificateKeyPath = string.Empty;
        }

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(Options.Create(options));
        builder.Services.TryAddSingleton<IServiceTokenProvider>(_ => ShippedClient(options));
        builder.Services.AddDataServicesHealthChecks();

        await using WebApplication app = builder.Build();

        app.MapHealthEndpoints();

        await app.StartAsync(TestContext.Current.CancellationToken);

        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatus, response.StatusCode);

        using JsonDocument body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        if (mounted)
        {
            JsonElement entry = Assert.Single(
                body.RootElement.GetProperty("checks").EnumerateArray().ToArray(),
                candidate => candidate.GetProperty("name").GetString() == "credentials");

            Assert.Equal(expectedEntryStatus, entry.GetProperty("status").GetString());

            return;
        }

        // The not-ready body is a problem document, so the entry list rides on the extension member the
        // aggregate reads - and the verdict rides on its own member, because RFC 9457 already uses
        // `status` there for the integer HTTP status.
        Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedEntryStatus, body.RootElement.GetProperty("serviceStatus").GetString());

        // The not-ready component is NAMED in the detail, which is what an operator reads.
        Assert.Contains(
            "credentials",
            body.RootElement.GetProperty("detail").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The COMPOSITION ROOT registers the credential check, so the wiring holds in the shipped host and
    /// not only in a hand-assembled one.
    /// </summary>
    /// <remarks>
    /// THE ROW ABOVE DRIVES THE REGISTRATION EXTENSION DIRECTLY, WHICH IS ONE STEP SHORT OF THE TRUTH. A
    /// composition root that stopped calling the extension - reverting to the bare framework registration
    /// it replaced - would leave every other row in this file passing while the shipped service went back
    /// to reporting ready with no credential mounted. This asserts the call itself, against the real
    /// <c>Program</c> the service ships.
    /// </remarks>
    [Fact]
    public void The_composition_root_registers_the_credential_check()
    {
        using DataServicesTestHostFactory host = new();

        HealthCheckServiceOptions registered = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        Assert.Single(
            registered.Registrations,
            registration => string.Equals(
                registration.Name,
                "credentials",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Registering twice leaves one registration, so a host that substituted the verdict keeps it and the
    /// evaluator does not fault while being built.
    /// </summary>
    [Fact]
    public void The_registration_is_idempotent()
    {
        ServiceCollection services = new();

        services.AddDataServicesHealthChecks();
        services.AddDataServicesHealthChecks();

        HealthCheckServiceOptions options = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        Assert.Single(
            options.Registrations,
            registration => string.Equals(
                registration.Name,
                "credentials",
                StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------------------------------------
    //  THE HARNESS
    // ----------------------------------------------------------------------------------------------

    /// <summary>Evaluates the check over the shipped client.</summary>
    /// <param name="options">The bound options the preconditions are read from.</param>
    /// <param name="failureStatus">The status the registration declares for a failure.</param>
    /// <param name="cancellationToken">The evaluation token.</param>
    /// <returns>The verdict.</returns>
    private static async Task<HealthCheckResult> EvaluateAsync(
        DataServicesOptions options,
        HealthStatus failureStatus,
        CancellationToken cancellationToken)
    {
        TokenBootstrapHealthCheck check = new(
            Options.Create(options),
            ShippedClient(options),
            NullLogger<TokenBootstrapHealthCheck>.Instance);

        return await check.CheckHealthAsync(Registration(failureStatus), cancellationToken);
    }

    /// <summary>Builds the SHIPPED token bootstrap, whose preconditions the check asserts.</summary>
    /// <param name="options">The bound options.</param>
    /// <returns>The client.</returns>
    /// <remarks>
    /// The transport is a bare client with no handler activity at all, because the subject performs no
    /// I/O: a client that could send would let a future edit start sending without any row noticing.
    /// </remarks>
    private static SecurityClient ShippedClient(DataServicesOptions options) =>
        new(
            new HttpClient { BaseAddress = new Uri(IssuanceAddress, UriKind.Absolute) },
            Options.Create(options),
            NullLogger<SecurityClient>.Instance);

    /// <summary>Builds a fully configured options instance.</summary>
    /// <returns>The options.</returns>
    private static DataServicesOptions Configured()
    {
        DataServicesOptions options = new();

        options.Security.BaseAddress = IssuanceAddress;
        options.Security.MutualTls.CertificatePath = CertificatePath;
        options.Security.MutualTls.CertificateKeyPath = CertificateKeyPath;

        return options;
    }

    /// <summary>Builds a registration carrying the given failure status.</summary>
    /// <param name="failureStatus">The status a not-ready result must adopt.</param>
    /// <returns>The evaluation context.</returns>
    private static HealthCheckContext Registration(HealthStatus failureStatus) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                "credentials",
                _ => throw new NotSupportedException(
                    "The subject is constructed directly, so the registration's own factory is never "
                    + "invoked; a call here would mean the harness had started driving the framework's "
                    + "evaluator instead of the check."),
                failureStatus,
                ["ready"]),
        };
}

/// <summary>
/// A token bootstrap a host supplied for itself, standing for any mechanism that is not the shipped
/// client.
/// </summary>
/// <remarks>
/// ITS ONE MEMBER THROWS. The check must decide from the provider's IDENTITY alone and must never ask it
/// for a credential: a readiness probe that minted a token would mutate the credential cache and put
/// load on the issuance edge from an anonymous route. A double that answered a token would let that
/// change pass silently.
/// </remarks>
internal sealed class SubstitutedTokenBootstrap : IServiceTokenProvider
{
    /// <inheritdoc/>
    public ValueTask<ServiceToken> GetTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "The token-bootstrap readiness check must decide from the registered provider's identity "
            + "alone. Asking for a credential here would mean the probe had started minting tokens from "
            + "an anonymous route.");
}
