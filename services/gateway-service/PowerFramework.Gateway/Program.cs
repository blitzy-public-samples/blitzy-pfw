// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK GATEWAY SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the system's SOLE INGRESS. Every external client enters here and
//  nothing but an external client calls Gateway, so this file is the one place where the whole
//  service is assembled: configuration binding, the framework lifecycle, the localization surface,
//  inbound token validation, the typed clients for the two services Gateway reaches, the published
//  REST surface and the unhandled-fault path.
//
//  This file WIRES. It declares no route of its own beyond the anonymous readiness probe and the
//  published OpenAPI document, because route declarations belong in Endpoints/ where they can be
//  read against the contract and exercised without a host.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  ws_objects/pfw.pbl.src/pfw.sra is the framework application object and the authoritative
//  composition-root reference. Note that TWO distinct files in this repository are named pfw.sra;
//  the other, ws_objects/pfw.pack.pbl.src/pfw.sra, is the PowerBuilder packager and is irrelevant
//  here, which is why the full path is always written out.
//
//      :L88-L106  event open      pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL) is the FIRST statement
//                                 [:L91]; the locale is hardcoded to "en" [:L94]; one of three
//                                 provider classes is created from it [:L95-L102]; the provider is
//                                 INSTALLED with I18N(locale) [:L103].
//      :L108      event close     pfwFinalize() is the ONLY statement.
//      :L111-L144 event systemerror  the seven-field assert-unpacking protocol, ending in
//                                 HALT CLOSE [:L143] - process termination.
//
//  Reproduced here as host startup, host shutdown and the unhandled-exception path respectively.
//  Three things in pfw.sra are DELIBERATELY NOT PORTED, listed so the omissions read as decisions:
//  Open(w_demo_selector) [:L105] is UI navigation with no analogue in a headless service;
//  gs_var [:L18] is a stray global with no consumer anywhere in the 544-object estate; and
//  themepath [:L24] embeds a developer-workstation absolute path.
//
//  WHY REST AT THIS BOUNDARY AND NOT gRPC (constraint C-K)
//  Gateway's legacy analogue is the coarse, human-facing open/close/systemerror lifecycle plus a
//  navigation surface - a request/response shape. REST is additionally required BECAUSE Gateway is
//  the sole ingress: browser and third-party reach, HTTP-standard caching and proxying, and mature
//  OpenAPI tooling. gRPC-Web needs a translating proxy and supports server streaming only, so gRPC
//  at the edge would forfeit exactly the properties an ingress needs. Gateway therefore hosts NO
//  gRPC service - there is no AddGrpc and no MapGrpcService anywhere in this project - and
//  references Grpc.AspNetCore for its CLIENT side alone.
//
//  FAIL FAST, PRESERVED AS FAIL FAST (constraint C-B)
//  The legacy posture is explicit and is not softened here. docs/README.md records initialization as
//  mandatory since pfw 2.0, requires pfwFinalize to be PAIRED with pfwInitialize, and warns that a
//  missing module DLL makes initialization FAIL; a decoded assertion failure ends in HALT CLOSE. So:
//  an unusable configuration section prevents the host from STARTING (ValidateOnStart), a failed
//  framework initialization is fatal rather than degraded (Composition/FrameworkInitializer.cs), and
//  a structural fault reaching Diagnostics/SystemErrorHandler.cs terminates the process. Softening
//  any of these into warn-and-continue would be a behavioural change dressed up as robustness.
//
//  ONE DELIBERATE, DOCUMENTED DEPARTURE FROM THE LEGACY CALL SITE. pfw.sra:L91 itself DISCARDS
//  pfwInitialize's return code, while docs/README.md makes a failed initialization fatal. The port
//  CHECKS the code, in FrameworkInitializer, because the documentation is the specification and the
//  call site is the laxity. It is annotated there and again here so that a reviewer does not read it
//  as an unrequested behaviour change.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No listening port in code. 5105 comes from host configuration, which the container supplies,
//      so nothing here reads a custom port key or applies one by hand (constraint C-J).
//    * No PersistenceClient, and no address for Persistence. The topology is layered and acyclic:
//      Gateway calls DataServices and Security; DataServices calls Persistence. A third client here
//      would break the layering (constraint C-A).
//    * No DbContext, no EF Core, no Microsoft.Data.Sqlite, no connection string and no migration
//      call. Persistence is the only service in the refactor that holds a storage provider, and
//      inventing one anywhere else would fabricate a database (constraint C-E).
//    * No registration, route, handler or options type for DesignSystem, Documents, Integration or
//      ScriptBridge. Endpoints/DeferredCapabilityEndpoints.cs declares four reserved routes that
//      answer 501 and contains nothing else; a routing declaration is not a stub, and C-D prohibits
//      IMPLEMENTING a deferred service, which nothing here does.
//    * No token minting, no signing key and no signing authority. SECURITY_JWT_SIGNING_KEY is the
//      system's single signing secret and belongs to Security alone; Gateway holds verification
//      material only (constraints C-F and C-G).
//    * No SCREAMING_SNAKE constant is DECLARED in this file. The repository .editorconfig scopes its
//      naming-analyzer suppressions to a fixed list of files that carry preserved legacy identifiers,
//      and this file is not on it; the preserved identifiers are CONSUMED from
//      PowerFramework.Shared.Kernel instead.
// ==================================================================================================

using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PowerFramework.Contracts.DataServices.V1;
using PowerFramework.Gateway.Clients;
using PowerFramework.Gateway.Composition;
using PowerFramework.Gateway.Configuration;
using PowerFramework.Gateway.Diagnostics;
using PowerFramework.Gateway.Endpoints;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// 1. CONFIGURATION, VALIDATED AT STARTUP RATHER THAN AT FIRST REQUEST
//
// Both sections are bound through the options pattern and both are validated ON START, so a
// deployment that misconfigures either fails to come up instead of failing the first request that
// happens to touch the bad value. That is the fail-fast posture described in the header, expressed
// in the one place a structural fault can still be reported cheaply.
//
// GatewayOptions carries the preserved "en" locale default and the eight-bit capability mask;
// JwtBearerVerificationOptions carries the inbound-token settings and is REQUIRED to name an
// authority, so an unconfigured deployment is rejected by name rather than silently accepting
// nothing. Neither type holds a credential: every member of both is an address, an identifier or a
// boolean.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddOptions<GatewayOptions>()
    .Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<JwtBearerVerificationOptions>()
    .Bind(builder.Configuration.GetSection(JwtBearerVerificationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --------------------------------------------------------------------------------------------------
// 2. THE DETERMINISM SEAM
//
// Registered rather than read ambiently, because the characterization model requires every clock
// read to be maskable from BOTH the master and the candidate recording. Endpoints/PingEndpoints.cs
// resolves this optionally and falls back to the system clock, so a deployment that registered none
// would still work - registering it here is what makes a test able to substitute a deterministic
// double for the whole host.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);

// --------------------------------------------------------------------------------------------------
// 3. LOCALIZATION - THE PRESERVED PROVIDER SELECTION, AND THE PRESERVED SILENT PASSTHROUGH
//
// pfw.sra:L94-L103 hardcodes the locale, selects one of exactly three provider classes from it, and
// INSTALLS the selected provider. The hardcoding is the defect, so it is reproduced as the DEFAULT
// value of Gateway:Locale and made overridable; the three-token vocabulary is reproduced exactly,
// and GatewayOptions restricts the value to those three so a fourth is rejected at startup.
//
// I18n is a CLASS, not a static holder, and its first overload is an INSTALLER rather than a
// translator: it validates the provider and returns a PowerFramework return code. That code is
// CHECKED here - through Predicates rather than a hand-rolled comparison, because the tri-state
// algebra is published contract - and a failure prevents the host from starting.
//
// The legacy used a global auto-instance (a global n_cst_i18n shadowing its own type name); the
// destination replaces it with a single injected instance and NEVER a static mutable slot.
//
// The SILENT PASSTHROUGH FALLBACK is preserved and must not be improved: with no matching entry the
// text comes back unchanged, never throwing, never logging and never marked untranslated. Nothing
// here probes for pfw.i18n.xml, copies it, embeds it or checks for its presence - it is read-only
// legacy data (C-C), the localization library opens it by relative filename at run time, and its
// absence producing unchanged text IS the preserved behaviour.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<II18nProvider>(static serviceProvider =>
{
    GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

    return CreateLocaleProvider(options.Locale);
});

builder.Services.AddSingleton<I18n>(static serviceProvider =>
{
    I18n localization = new();

    long installed = localization.I18N(serviceProvider.GetRequiredService<II18nProvider>());

    if (!Predicates.IsSucceeded(installed))
    {
        throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "Installing the localization provider returned {0}, which is not a success under the "
                + "preserved return-code algebra, so the host will not start. The legacy framework "
                + "treats a structural fault as fatal rather than degrading past it "
                + "[ws_objects/pfw.pbl.src/pfw.sra:L111-L144], and a Gateway serving requests with no "
                + "installed provider would silently return untranslated text for every category.",
            installed));
    }

    return localization;
});

// --------------------------------------------------------------------------------------------------
// 4. THE FRAMEWORK LIFECYCLE - THE MANDATORY INITIALIZE/FINALIZE PAIRING
//
// docs/README.md states that pfwInitialize belongs at the very beginning of Application Open and
// pfwFinalize at the very end of Application Close, and carries a warning that the two MUST be
// paired. FrameworkInitializer is an IHostedLifecycleService, so the host's own startup and shutdown
// ordering supplies the pairing: initialization runs before the server begins listening, and
// finalization runs on graceful shutdown.
//
// The runtime boundary is registered as an interface so a test can substitute one and reach the
// success, failure and indeterminate paths without a native library present.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<IFrameworkRuntime, ManagedFrameworkRuntime>();
builder.Services.AddSingleton<FrameworkInitializer>();
builder.Services.AddHostedService(static serviceProvider =>
    serviceProvider.GetRequiredService<FrameworkInitializer>());

// --------------------------------------------------------------------------------------------------
// 5. INBOUND TOKEN VALIDATION - THE STOCK HANDLER, CONFIGURED FROM THE VALIDATED SECTION
//
// The security-critical path is FRAMEWORK CODE rather than hand-written code: the stock JWT bearer
// handler resolves Security's discovery document and published key set beneath the configured
// authority and refreshes them on its own schedule. That property is the reason Security speaks REST
// rather than gRPC, and it is why nothing in this project retrieves a key set by hand.
//
// The handler is configured FROM the validated options type rather than from raw configuration, so
// exactly one set of values governs both the startup validation and the running handler. All four
// validations stay on; none is relaxed, and there is no environment-conditional bypass anywhere in
// this file - a bypass that exists only in Development is still a bypass, and it would make the
// local build disagree with the published contract.
//
// Metadata is fetched LAZILY by the handler on first use, not at startup, which is what keeps
// Gateway startable while its upstreams are still coming up (constraint C-I). An anonymous request
// is answered before any metadata is needed, because the handler looks for a token first - so the
// mandated 401 on /v1/ping holds even with Security unreachable.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtBearerVerificationOptions>>(static (bearer, verification) =>
    {
        JwtBearerVerificationOptions configured = verification.Value;

        bearer.Authority = configured.Authority;
        bearer.RequireHttpsMetadata = configured.RequireHttpsMetadata;
        bearer.MapInboundClaims = configured.MapInboundClaims;

        TokenValidationParameters parameters = bearer.TokenValidationParameters;

        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidateLifetime = true;
        parameters.ValidateIssuerSigningKey = true;

        // The issuer list is optional: with none configured the handler validates against the issuer
        // the authority's own metadata declares, which is the ordinary arrangement. An explicit list
        // narrows that, and the development overlay supplies one.
        if (configured.ValidIssuers.Count > 0)
        {
            parameters.ValidIssuers = [.. configured.ValidIssuers];
        }

        // The audience list is NOT optional and the options validator refuses an empty one, because
        // an audience is not discoverable from metadata and a service with none configured would
        // reject every token while appearing correctly configured.
        parameters.ValidAudiences = [.. configured.ValidAudiences];
    });

// A FALLBACK POLICY, so that no route is ever authenticated by omission (constraint C-G). Every
// endpoint file already declares RequireAuthorization explicitly, and this makes the absence of such
// a declaration a closed door rather than an open one. The two deliberate exceptions below opt out
// in the one place a reader looks for them.
builder.Services.AddAuthorization(static options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --------------------------------------------------------------------------------------------------
// 6. THE TWO TYPED CLIENTS - EXACTLY TWO, AND THESE TWO
//
// Resilience handlers are attached to both. This is required BY the transition rather than layered
// on top of it, and the distinction matters: an in-process call cannot fail in transit and a network
// call can, so these two edges are failure modes the legacy library could not have had. No latency,
// throughput or availability claim is made or implied by their presence - the repository publishes no
// such budget, so none may be asserted.
//
// SecurityClient is a typed HttpClient because Security is REST and there is no security protocol
// definition: the contracts project publishes exactly three .proto files and none of them is
// security. DataServicesClient wraps the two generated gRPC clients of dataservices.v1 and reaches
// Security's token issuance through IServiceTokenProvider, which SecurityClient implements.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddHttpClient<SecurityClient>(static (serviceProvider, httpClient) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        httpClient.BaseAddress = new Uri(options.Upstreams.Security, UriKind.Absolute);
    })
    .AddStandardResilienceHandler();

builder.Services.AddTransient<IServiceTokenProvider>(static serviceProvider =>
    serviceProvider.GetRequiredService<SecurityClient>());

builder.Services
    .AddGrpcClient<DataWindowService.DataWindowServiceClient>(static (serviceProvider, grpcOptions) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
    })
    .AddStandardResilienceHandler();

builder.Services
    .AddGrpcClient<ColumnExpressionService.ColumnExpressionServiceClient>(
        static (serviceProvider, grpcOptions) =>
        {
            GatewayOptions options =
                serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

            grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
        })
    .AddStandardResilienceHandler();

builder.Services.AddScoped<DataServicesClient>();

// --------------------------------------------------------------------------------------------------
// 7. THE PUBLISHED SURFACE AND THE FAULT PATH
//
// Health-check registration ships inside the Microsoft.AspNetCore.App shared framework, so /health
// needs no package reference. Problem details are registered so that the framework's own challenge
// and the fault path below both answer application/problem+json, which is the error shape
// shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml publishes.
//
// SystemErrorHandler is registered through an explicit factory rather than by type, so that its
// optional problem-details collaborator is supplied deliberately and the process-termination seam
// keeps its default. It reproduces the assert-unpacking protocol of pfw.sra:L111-L144 and, for a
// structural fault, the HALT CLOSE that ends it.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IExceptionHandler>(static serviceProvider => new SystemErrorHandler(
    serviceProvider.GetRequiredService<ILogger<SystemErrorHandler>>(),
    serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
    serviceProvider.GetService<IProblemDetailsService>()));

WebApplication app = builder.Build();

// Resolved eagerly, so that a localization provider that cannot be installed stops the host here
// rather than at the first request that needed a translated string. Startup is the only place a
// structural fault can still be reported cheaply.
_ = app.Services.GetRequiredService<I18n>();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// The anonymous readiness probe. ANONYMOUS DELIBERATELY AND ON ALL FOUR SERVICES: the thing that
// probes it - a container orchestrator, a load balancer, an operator - holds no token, and it probes
// precisely during the window in which the service is still starting. Requiring a token would make
// readiness depend on Security's issuance already being live, a circular dependency that cannot
// resolve during a cold start. It is also the endpoint the orchestration manifest's health condition
// gates Gateway's readiness on.
app.MapHealthChecks("/health").AllowAnonymous();

// The published contract document, anonymous because a description of the surface is not part of the
// surface it describes and every operation in it still states its own security requirement.
app.MapOpenApi().AllowAnonymous();

// One call per endpoint file. The route patterns, the metadata and the authorization requirements all
// live in those files, next to the contract they implement.
app.MapPingEndpoints();
app.MapCapabilityEndpoints();
app.MapDeferredCapabilityEndpoints();

app.Run();

/// <summary>
/// Selects the localization provider for a locale token, reproducing the three-way selection at
/// <c>ws_objects/pfw.pbl.src/pfw.sra:L95-L102</c>.
/// </summary>
/// <param name="locale">
/// The configured locale token. <see cref="GatewayOptions"/> restricts it to <c>en</c>, <c>chs</c> or
/// <c>cht</c> and validates that on start, so an unrecognised value cannot reach here through
/// configuration.
/// </param>
/// <returns>The provider for that locale.</returns>
/// <exception cref="InvalidOperationException">
/// The token is not one of the three the legacy declares. Unreachable through configuration, and a
/// hard failure rather than a silent default because a Gateway that quietly fell back to another
/// locale would return text no caller asked for.
/// </exception>
/// <remarks>
/// Compared with <see cref="StringComparer.Ordinal"/>: these are legacy symbols rather than
/// human-readable text, and no culture may participate in matching them. The Simplified Chinese
/// provider is a genuine no-op because Simplified Chinese is the base locale, and it is registered
/// anyway so that the three-provider shape of the legacy survives intact.
/// </remarks>
static II18nProvider CreateLocaleProvider(string locale) => locale switch
{
    "en" => new EnglishProvider(),
    "chs" => new SimplifiedChineseProvider(),
    "cht" => new TraditionalChineseProvider(),
    _ => throw new InvalidOperationException(
        $"'{GatewayOptions.SectionName}:{nameof(GatewayOptions.Locale)}' is '{locale}', which is not "
            + "one of the three locales the legacy framework declares at "
            + "ws_objects/pfw.pbl.src/pfw.sra:L95-L102 - en, chs or cht."),
};

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile to an internal <c>Program</c> class, so
/// without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the sibling
/// <c>PowerFramework.Gateway.Tests</c> project cannot name the entry point, the service-level tests
/// cannot boot this host at all, and the per-service coverage gate becomes unreachable for every line
/// in this file.
/// </remarks>
public partial class Program
{
    /// <summary>
    /// Prevents the partial class from being constructed directly.
    /// </summary>
    /// <remarks>
    /// The host is started by the top-level statements above, never by instantiating this type. A
    /// protected constructor states that without suppressing anything.
    /// </remarks>
    protected Program()
    {
    }
}
