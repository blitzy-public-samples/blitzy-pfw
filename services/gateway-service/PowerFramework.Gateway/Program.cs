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
//  THE ONE MUTUAL-TLS EDGE IN THE SYSTEM, AND WHY IT IS WIRED HERE (constraints C-G and C-K)
//  POST /v1/tokens on the Security service is protected by the mutualTLS scheme and by NOTHING ELSE,
//  because a caller cannot present a bearer token in order to obtain its first bearer token
//  [shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, the mutualTls scheme and that
//  operation's own security list; docs/ARCHITECTURE.md 9.3]. Gateway is one of the two services that
//  request tokens, so the client identity it presents on that handshake is a TRANSPORT concern rather
//  than a payload one - which is exactly why Clients/SecurityClient.cs sends no credential of its own
//  and states that the certificate is configured on the message handler in the composition root. This
//  file is that composition root, so this file attaches it, and it is the ONLY place in the project
//  that can: without it Gateway could never obtain a token and every authenticated call it makes
//  would be unreachable.
//
//  The material is named by two PATHS under Gateway:MutualTls and mounted from the orchestration
//  secret layer; no certificate, no private key and no passphrase is committed to this repository or
//  embedded in an image, and the options type has no member that could hold one (constraint C-F).
//  Leaving the pair entirely unset is a LEGITIMATE state - it means this run presents no client
//  certificate and therefore does not reach the issuance edge, which a local bring-up without a
//  generated certificate set genuinely is - and is deliberately NOT a startup failure. A pair that is
//  set but cannot be read IS a structural fault and stops the host, which is the same fail-fast
//  posture the rest of this file applies. Half a pair never reaches here at all: GatewayOptions
//  refuses it during the startup validation above.
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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
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
//
// Expressed through AddAuthorizationBuilder rather than AddAuthorization(options => ...) because the
// ASP.NET Core analyzers direct the builder form for exactly this shape (ASP0025); the registration
// and the resulting policy are identical, so this is a spelling decision and not a behavioural one.
builder.Services
    .AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
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
//
// THE CLIENT IDENTITY IS LOADED ONCE, HERE, AND SHARED BY EVERY ROTATION OF THE HANDLER. The factory
// below recycles its primary handler on a schedule, so reading the PEM pair inside that factory would
// open a fresh key handle on every rotation and never close one. Loading it as a singleton reads the
// material once and hands the same collection to each handler, which is also what lets the eager
// resolve after Build() turn an unreadable pair into a startup failure rather than a first-request
// one. Rotation of the material itself is a file replacement and a restart, per
// orchestration/.env.example.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton(static serviceProvider => LoadMutualTlsClientIdentity(
    serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value.MutualTls));

builder.Services
    .AddHttpClient<SecurityClient>(static (serviceProvider, httpClient) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        httpClient.BaseAddress = new Uri(options.Upstreams.Security, UriKind.Absolute);
    })
    .ConfigurePrimaryHttpMessageHandler(static serviceProvider =>
    {
        // NOTHING ABOUT SERVER-CERTIFICATE VALIDATION IS TOUCHED HERE, IN ANY ENVIRONMENT. The
        // handler keeps the platform's default trust decision: a forged Security service would be a
        // forged token issuer for the whole system, so trust is an orchestration concern - the CA a
        // deployment mounts - and never a validation callback in code. There is no
        // RemoteCertificateValidationCallback, no ServerCertificateCustomValidationCallback and no
        // environment-conditional relaxation anywhere in this file (constraint C-G).
        SocketsHttpHandler handler = new();

        X509Certificate2Collection identity =
            serviceProvider.GetRequiredService<X509Certificate2Collection>();

        // An empty collection is the unset pair, which is a legitimate state: this deployment
        // presents no client certificate and therefore cannot reach the issuance edge. Assigning an
        // empty collection would be harmless but says less than leaving the property alone, so the
        // property is only written when there is genuinely an identity to present.
        if (identity.Count > 0)
        {
            handler.SslOptions.ClientCertificates = identity;
        }

        return handler;
    })
    // THE CONSTRUCTOR IS PINNED HERE, AND IT HAS TO BE. SecurityClient publishes two public
    // constructors - one taking the clock and one defaulting it - so that it resolves whether or not a
    // TimeProvider has been registered. That arrangement relies on the activator picking the widest
    // constructor it can satisfy, which is how ActivatorUtilities behaves when it is given no argument
    // types, and is NOT how the typed-client factory behaves: that factory calls the activator with
    // HttpClient supplied, and the activator then demands EXACTLY ONE constructor matching the supplied
    // types. Both of SecurityClient's constructors match, so resolving the typed client without this
    // line throws "Multiple constructors accepting all given argument types have been found" on the
    // first resolve - which is the first token request, i.e. the first authenticated call Gateway
    // makes. An explicit factory removes the ambiguity by naming the constructor, and names the one
    // that takes the clock so the determinism seam registered above is genuinely engaged rather than
    // silently defaulted.
    .AddTypedClient<SecurityClient>(static (httpClient, serviceProvider) => new SecurityClient(
        httpClient,
        serviceProvider.GetRequiredService<ILogger<SecurityClient>>(),
        serviceProvider.GetRequiredService<TimeProvider>()))
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
// needs no package reference. It is registered for HealthCheckService, which Endpoints/HealthEndpoints
// .cs resolves OPTIONALLY for Gateway's own component contribution to the C-10 aggregate; the route
// itself is that file's, not this one's. Problem details are registered so that the framework's own
// challenge and the fault path below both answer application/problem+json, which is the error shape
// shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml publishes - and it is also the shape the
// readiness endpoint's own not-ready response uses.
//
// SystemErrorHandler is registered through an explicit factory rather than by type, so that its
// optional problem-details collaborator is supplied deliberately and the process-termination seam
// keeps its default. It reproduces the assert-unpacking protocol of pfw.sra:L111-L144 and, for a
// structural fault, the HALT CLOSE that ends it.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

// The generated document's PATHS are produced from the endpoints the five files below map, and they
// already match the authoritative contract exactly. Its IDENTITY is not: the generator names a
// document after the assembly and the document format, which would publish this surface under a name
// no consumer of the contract recognises. The transformer restates the identity the contract declares
// - and only the identity, because everything else is derived from the endpoints and deriving it is
// the point. shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml is authoritative for all four
// values; if it and this disagree, that file wins and this one is the defect.
builder.Services.AddOpenApi(static options => options.AddDocumentTransformer(
    static (document, context, cancellationToken) =>
    {
        document.Info ??= new OpenApiInfo();

        document.Info.Title = "PowerFramework Gateway API";
        document.Info.Version = "1.0.0";
        document.Info.Summary =
            "The composition root and sole ingress of the PowerFramework .NET decomposition. Contract "
                + "C-09 (REST ingress), the ingress half of C-10 (health and readiness), and the four "
                + "reserved extension points that name the deferred Phase-2 services.";

        // NAME ONLY, exactly as the contract carries it. The full BSD 2-Clause text, its
        // four-condition Chinese restatement and the eleven upstream attributions live in the
        // repository-root NOTICE and LICENSE files and are not restated in a served document.
        document.Info.License = new OpenApiLicense { Name = "BSD-2-Clause" };

        return Task.CompletedTask;
    }));

builder.Services.AddSingleton<IExceptionHandler>(static serviceProvider => new SystemErrorHandler(
    serviceProvider.GetRequiredService<ILogger<SystemErrorHandler>>(),
    serviceProvider.GetRequiredService<IHostApplicationLifetime>(),
    serviceProvider.GetService<IProblemDetailsService>()));

WebApplication app = builder.Build();

// Resolved eagerly, so that a localization provider that cannot be installed stops the host here
// rather than at the first request that needed a translated string. Startup is the only place a
// structural fault can still be reported cheaply.
_ = app.Services.GetRequiredService<I18n>();

// Resolved eagerly for the same reason, and it is the half of the mutual-TLS story that a lazy
// registration would get wrong. A configured-but-unreadable certificate pair is a structural fault: it
// means this deployment intended to reach the token-issuance edge and cannot, so every authenticated
// call it makes is already doomed. Discovering that at startup is a failure to launch; discovering it
// at the first token request is an outage that looks like an upstream problem. An UNSET pair resolves
// to an empty collection and is not a fault, so this line neither fails nor warns for the local
// bring-up posture that presents no client certificate.
_ = app.Services.GetRequiredService<X509Certificate2Collection>();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

// The published contract document, anonymous because a description of the surface is not part of the
// surface it describes and every operation in it still states its own security requirement.
app.MapOpenApi().AllowAnonymous();

// One call per endpoint file. The route patterns, the metadata and the authorization requirements all
// live in those files, next to the contract they implement.
//
// /health is NO EXCEPTION and is mapped by Endpoints/HealthEndpoints.cs like every other route, NOT by
// MapHealthChecks here. Two reasons, and either alone would decide it. First, C-10 requires Gateway's
// readiness to be an AGGREGATE that names Persistence, DataServices and Security with their individual
// states [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml AggregateHealthReport], and the
// framework's own health-check endpoint answers a bare status word that carries none of it. Second, two
// registrations of the same path and method are an ambiguous match, so mapping both would fault the one
// endpoint the orchestration readiness gate probes. AddHealthChecks above stays: HealthEndpoints
// consumes HealthCheckService for Gateway's OWN component contribution to the aggregate.
app.MapHealthEndpoints();
app.MapPingEndpoints();
app.MapCapabilityEndpoints();

// The /v1/datawindow projection of C-03 and C-04. Declared BEFORE the four reserved families so that a
// reader meets the surface that exists before the surface that deliberately does not; routing itself is
// order-independent here, because the reserved templates are catch-alls under four other prefixes and no
// template in either file can match a request the other's could.
app.MapDataServicesProxyEndpoints();

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
/// Loads the client identity Gateway presents on the system's single mutual-TLS edge, or an empty
/// collection when this deployment presents none.
/// </summary>
/// <param name="mutualTls">
/// The validated <c>Gateway:MutualTls</c> group. Both members are filesystem paths naming material
/// mounted from the orchestration secret layer; the type has no member that could carry a certificate
/// body, a private key body or a passphrase, so there is nowhere for one to be placed.
/// </param>
/// <returns>
/// A collection holding the one client certificate when the pair is configured, and an EMPTY
/// collection when it is not. Empty is a legitimate result and not an error: it means this run does
/// not reach the token-issuance edge, which a local bring-up without a generated certificate set
/// genuinely is.
/// </returns>
/// <exception cref="InvalidOperationException">
/// The pair is configured but the material cannot be read or does not parse. That is a structural
/// fault and it stops the host, matching the fail-fast posture described in the file header - a
/// deployment that meant to authenticate to the issuer and cannot has already lost every authenticated
/// call it would make, so continuing would only defer the failure to first use.
/// </exception>
/// <remarks>
/// <para>
/// NO PATH IS EVER ECHOED INTO A MESSAGE, and the exception below names the two CONFIGURATION KEYS
/// instead. A path is not itself a credential, but it names the location of one, and a startup log is
/// exactly the wrong place to publish where a private key is mounted. The configuration key is
/// sufficient for an operator to find the setting, which is the same rule
/// <c>Configuration/GatewayOptions.cs</c> applies to its own validation messages.
/// </para>
/// <para>
/// HALF A PAIR CANNOT REACH HERE. <c>GatewayOptions</c> validates the group as both-or-neither and the
/// registration above validates on start, so by the time this runs the pair is either wholly present
/// or wholly absent. The second check below is therefore a guard against a future caller rather than a
/// reachable configuration state, and it is written as one rather than as an assumption.
/// </para>
/// <para>
/// The material is read from a PEM certificate and a separate PEM key, which is the shape the
/// generation recipe in <c>docs/ARCHITECTURE.md</c> produces and the shape the four
/// <c>*_MTLS_CERT_PATH</c> / <c>*_MTLS_KEY_PATH</c> variables name. The resulting key is ephemeral,
/// which is directly usable for TLS client authentication on Linux - the target operating system for
/// every container in this refactor.
/// </para>
/// </remarks>
static X509Certificate2Collection LoadMutualTlsClientIdentity(
    GatewayOptions.MutualTlsClientOptions mutualTls)
{
    ArgumentNullException.ThrowIfNull(mutualTls);

    if (!mutualTls.IsConfigured)
    {
        return [];
    }

    string certificatePath = mutualTls.CertificatePath.Trim();
    string certificateKeyPath = mutualTls.CertificateKeyPath.Trim();

    if (certificatePath.Length == 0 || certificateKeyPath.Length == 0)
    {
        throw new InvalidOperationException(
            $"'{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}' is half configured. A "
                + "certificate cannot complete a handshake without its key and a key has nothing to "
                + "present without its certificate, so set both "
                + $"'{nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)}' and "
                + $"'{nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)}' or neither.");
    }

    try
    {
        // Constructed directly into the collection so the certificate has no owning local: its
        // lifetime is the returned collection's, which the container holds as a singleton for the
        // life of the process.
        return new X509Certificate2Collection(
            X509Certificate2.CreateFromPemFile(certificatePath, certificateKeyPath));
    }
    catch (Exception failure) when (failure
        is CryptographicException
        or IOException
        or UnauthorizedAccessException
        or ArgumentException)
    {
        throw new InvalidOperationException(
            "The client certificate named by "
                + $"'{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}' could not be "
                + "loaded, so this deployment cannot authenticate to the token-issuance edge and the "
                + "host will not start. Check that both files exist, that the process can read them, "
                + "and that each is PEM encoded - the certificate in "
                + $"'{nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)}' and its private "
                + $"key in '{nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)}'. "
                + "Neither path is reproduced here, because a startup log is the wrong place to "
                + "publish where a private key is mounted.",
            failure);
    }
}

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
