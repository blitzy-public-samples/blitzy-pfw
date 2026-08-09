// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK DATASERVICES SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the DataWindow retrieval / validation / update triple, the 22-event
//  chain and the column-expression engine. Port 5102. It is reached by Gateway and by nothing else,
//  and it reaches Persistence and Security.
//
//  This file WIRES: configuration binding with startup validation, the localization surface the
//  validation path calls into, inbound token validation, the typed client for Security, the readiness
//  and liveness routes, and the determinism seams. Route declarations live in Endpoints/, next to the
//  contract they implement.
//
//  TRANSPORT, AND WHY IT IS NOT REST-ONLY (constraint C-K)
//  DataServices' legacy interface is a 22-event ordered chain with veto semantics, a `ref string`
//  out-parameter [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L13] and an `any` return
//  over a `string[]` argument [:L14]. That shape demands compile-time contract enforcement,
//  bidirectional streaming to carry the ordered chain, and a status model rich enough for a
//  four-value alphabet and a tri-valued veto, so gRPC is the primary transport and the published
//  protocol definitions live in PowerFramework.Contracts. A thin REST projection exists for Gateway
//  alone. The two Kestrel endpoints this host listens on - HTTP/1.1 for REST and HTTP/2 for gRPC -
//  come from the Kestrel section of appsettings.json and are never restated in code.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for a host: PowerFramework is a library with no process of its own, no
//  listener and no server tier, so this file's job did not exist. What IS ported is the lifecycle
//  posture of the framework application object at ws_objects/pfw.pbl.src/pfw.sra - initialization
//  before anything else, finalization paired with it, and a structural fault ending the process
//  rather than degrading past it [:L111-L144].
//
//  FAIL FAST, PRESERVED AS FAIL FAST (constraint C-B)
//  Both option groups are validated ON START, so a deployment that misconfigures an upstream address,
//  a session lifetime or the token authority fails to come up instead of failing the first request
//  that touched the bad value. Worker-session creation failure is fatal in the legacy, and a decoded
//  assertion failure terminates the application; softening either into warn-and-continue would be a
//  behavioural change dressed up as robustness.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No DbContext, no EF Core, no Microsoft.Data.Sqlite, no connection string, no migration call and
//      no storage setting of any kind. Persistence is the only service that holds a storage provider,
//      and SQL generation and execution belong to it alone (constraints C-A and C-E).
//    * No registration, route, handler or options type for DesignSystem, Documents, Integration or
//      ScriptBridge. The presentational halves of the column-sort, context-menu and drop-down-search
//      services are a documented capability gap surfaced as Gateway extension points, not something
//      stubbed here (constraint C-D).
//    * No token minting and no signing key. Security is the sole issuer; this service holds
//      verification material only (constraints C-F and C-G).
//    * No listening port in code, and no SCREAMING_SNAKE constant DECLARED here - the preserved legacy
//      identifiers are consumed from PowerFramework.Shared.Kernel, whose files are the ones the
//      repository .editorconfig scopes its naming suppressions to.
// ==================================================================================================

using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Endpoints;
using PowerFramework.DataServices.Expressions;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// 1. CONFIGURATION, VALIDATED AT STARTUP RATHER THAN AT FIRST REQUEST
//
// Each group has a dedicated validator that reports every broken rule at once, so an operator fixes
// one deployment rather than discovering the next problem on the next attempt. The validators are
// registered as IValidateOptions rather than run through data annotations, because they express
// cross-property rules - an address shape, a positive lifetime, a ratio inside its open interval -
// that annotations cannot state.
//
// DataServices:* carries the five PRESERVED-LEGACY-DEFAULT groups, every value of which was read at a
// named line of ws_objects/** and is therefore a behaviour-preservation claim rather than a tuning
// knob. Nothing here overrides one; the configuration document is their single authority.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddOptions<DataServicesOptions>()
    .Bind(builder.Configuration.GetSection(DataServicesOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<DataServicesOptions>, DataServicesOptionsValidator>();

builder.Services
    .AddOptions<JwtAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(JwtAuthenticationOptions.SectionName))
    .ValidateOnStart();

builder.Services
    .AddSingleton<IValidateOptions<JwtAuthenticationOptions>, JwtAuthenticationOptionsValidator>();

// --------------------------------------------------------------------------------------------------
// 2. THE DETERMINISM SEAM
//
// Registered rather than read ambiently. The characterization model requires every clock read to be
// maskable from BOTH the master and the candidate recording, and the transaction-pool idle expiry,
// the session lifetimes and the ping timestamp all read a clock. Endpoints resolve it optionally and
// fall back to the system clock, so registering it here is what lets a test substitute a
// deterministic double for the whole host.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);

// --------------------------------------------------------------------------------------------------
// 2b. THE `for page` PAGE RESOLVER - WIRED HERE BECAUSE A SEAM IS NOT WIRING
//
// The expression evaluator's CLASS default is UnresolvedPageResolver, which REFUSES `for page` rather
// than widening it to every row - a deliberate narrowing (AAP 0.1.5) and the right default for an
// evaluator built by code that has stated nothing about pagination. It is the wrong thing for a
// CONFIGURED, RUNNING SERVICE to be silently using, and that is exactly what it was: the sole
// `for page` expression in the repository, dw_sqlite.srd:L27's `sum(salary for page)`, answered the
// malformed sentinel in the deployed path unless a wiring site remembered to inject a resolver.
//
// The pagination is therefore STATED ONCE, in DataServices:ColumnExpression:PageResolution, and turned
// into the one resolver that expresses it by ExpressionPageResolverFactory - which lives beside the
// three resolver implementations so the mapping cannot drift from them. The deployed default is
// WholeBuffer, an explicit statement that the sole evidenced surface is unpaginated: dw_sqlite.srd
// declares no page-break band. A paginated deployment states FixedRowsPerPage with a row count, or
// Unresolved to get the refusal back; the options validator rejects every other combination at
// startup, so this factory call cannot silently pick a resolver from a mistyped setting.
//
// A SINGLETON because all three resolvers are stateless value-like objects and one instance serves
// every evaluator - the two parameterless ones are shared instances already. Evaluators take it
// through their three-argument constructor rather than assigning the property afterwards, so an
// evaluator is never briefly running on the refusing default.
//
// NO EVALUATOR IS REGISTERED HERE, and that is not an omission: DataWindowExpressionEvaluator binds to
// a DataWindowServiceHost, which is per-session state that the C-04 service layer creates and owns.
// Registering the resolver is what makes that layer's construction correct by default whenever it is
// written; registering an evaluator would mean inventing a host lifetime the contract has not defined.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<IExpressionPageResolver>(static serviceProvider =>
{
    ColumnExpressionOptions columnExpression = serviceProvider
        .GetRequiredService<IOptions<DataServicesOptions>>()
        .Value
        .ColumnExpression;

    return ExpressionPageResolverFactory.Create(
        columnExpression.PageResolution,
        columnExpression.PageRowsPerPage);
});

// --------------------------------------------------------------------------------------------------
// 3. LOCALIZATION - A CONFIRMED REQUIREMENT OF THIS SERVICE, NOT A CONDITIONAL ONE
//
// I18N is called from INSIDE the DataWindow validation path - se_cst_dw.sru:L357 and :L368 pass the
// CAT_DWSVC category - so this service needs the localization surface in its own right rather than
// inheriting it from the ingress. The provider is selected from DataServices:Localization:Locale,
// whose default reproduces the hardcoded "en" of pfw.sra:L94; the hardcoding is the defect, so it is
// preserved as the default value and made overridable.
//
// The installer's return code is CHECKED through Predicates, because the tri-state algebra is
// published contract and a hand-rolled comparison would re-derive it. The SILENT PASSTHROUGH FALLBACK
// is preserved exactly: with no matching entry the text comes back unchanged, never throwing, never
// logging and never marked untranslated. Nothing here probes for, copies or embeds pfw.i18n.xml - it
// is read-only legacy data, and its absence producing unchanged text IS the preserved behaviour.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<II18nProvider>(static serviceProvider =>
{
    DataServicesOptions options =
        serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value;

    return CreateLocaleProvider(options.Localization.Locale);
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
                + "preserved return-code algebra, so the host will not start. The validation path "
                + "routes its diagnostics through this surface "
                + "[ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L357, :L368], so a host "
                + "with no installed provider would emit untranslated validation messages instead of "
                + "failing visibly.",
            installed));
    }

    return localization;
});

// --------------------------------------------------------------------------------------------------
// 4. INBOUND TOKEN VALIDATION - THE STOCK HANDLER, CONFIGURED FROM THE VALIDATED SECTION
//
// An internal edge is a created boundary too: the legacy opened no listening socket at all, so this
// service's inbound surface was created by the decomposition and is authenticated from the outset
// (constraint C-G). The stock bearer handler resolves Security's discovery document and published key
// set beneath the configured authority with zero bespoke retrieval code, which is the property that
// made Security REST rather than gRPC.
//
// All four validations stay on and none is relaxed. There is no environment-conditional bypass in this
// file: a bypass that exists only in Development is still a bypass, and it would make the local build
// disagree with the published contract. Metadata is fetched lazily by the handler on first use rather
// than at startup, so this host starts while Security is still coming up, and an anonymous request is
// refused before any metadata is needed - which is what keeps the mandated 401 on /v1/ping true even
// with Security unreachable.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtAuthenticationOptions>>(static (bearer, authentication) =>
    {
        JwtAuthenticationOptions configured = authentication.Value;

        bearer.Authority = configured.Authority;
        bearer.Audience = configured.Audience;
        bearer.RequireHttpsMetadata = configured.RequireHttpsMetadata;

        // An explicit metadata address overrides the authority-relative default, which is what lets a
        // deployment point the handler at a key set published somewhere other than the conventional
        // path beneath the authority.
        if (!string.IsNullOrWhiteSpace(configured.MetadataAddress))
        {
            bearer.MetadataAddress = configured.MetadataAddress;
        }

        bearer.TokenValidationParameters.ValidateIssuer = configured.ValidateIssuer;
        bearer.TokenValidationParameters.ValidateAudience = configured.ValidateAudience;
        bearer.TokenValidationParameters.ValidateLifetime = configured.ValidateLifetime;
        bearer.TokenValidationParameters.ValidateIssuerSigningKey = configured.ValidateIssuerSigningKey;
    });

// A FALLBACK POLICY, so no route is ever authenticated by omission (constraint C-G). The endpoint
// files already declare their requirements explicitly; this makes the absence of a declaration a
// closed door rather than an open one. The two deliberate exceptions opt out where a reader looks for
// them: /health is anonymous, and so is the published contract document.
builder.Services.AddAuthorization(static options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --------------------------------------------------------------------------------------------------
// 5. THE TYPED CLIENT FOR SECURITY
//
// Security is REST, so this is a typed HttpClient rather than a generated stub: the contracts project
// publishes exactly three protocol definitions and none of them is security. The client carries both
// contract C-01 (token issuance, for the credential this service presents downstream) and contract
// C-02 (the cryptographic surface), and under C-02 raw key material never crosses the wire from a
// caller - it passes an opaque reference that Security resolves on its own side.
//
// The resilience handler exists because AN IN-PROCESS CALL CANNOT FAIL IN TRANSIT AND A NETWORK CALL
// CAN. This edge was an in-process method call in the legacy library, so the failure mode did not
// exist and there was nothing to configure; handling a failure mode the decomposition itself creates
// is required BY the transition rather than layered on top of it. Its values come from configuration
// and restate the library's own standard-handler defaults, so they are visible and overridable per
// environment. NO LATENCY, THROUGHPUT OR AVAILABILITY CLAIM is made or implied by any of them: the
// repository publishes no such budget, so none may be asserted.
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddHttpClient<SecurityClient>(static (serviceProvider, httpClient) =>
    {
        DataServicesOptions options =
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value;

        httpClient.BaseAddress = new Uri(options.Security.BaseAddress, UriKind.Absolute);
    })
    .AddStandardResilienceHandler()
    .Configure(static (resilience, serviceProvider) =>
    {
        ClientResilienceOptions configured = serviceProvider
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Resilience
            .Security;

        resilience.Retry.MaxRetryAttempts = configured.MaxRetryAttempts;
        resilience.Retry.Delay = configured.RetryBaseDelay;
        resilience.CircuitBreaker.FailureRatio = configured.CircuitBreakerFailureRatio;
        resilience.CircuitBreaker.MinimumThroughput = configured.CircuitBreakerMinimumThroughput;
        resilience.CircuitBreaker.SamplingDuration = configured.CircuitBreakerSamplingDuration;
        resilience.CircuitBreaker.BreakDuration = configured.CircuitBreakerBreakDuration;
        resilience.TotalRequestTimeout.Timeout = configured.RequestTimeout;
    });

builder.Services.AddTransient<IServiceTokenProvider>(static serviceProvider =>
    serviceProvider.GetRequiredService<SecurityClient>());

builder.Services.AddTransient<ICryptoServiceClient>(static serviceProvider =>
    serviceProvider.GetRequiredService<SecurityClient>());

// --------------------------------------------------------------------------------------------------
// 6. THE PUBLISHED SURFACE
//
// Health-check registration ships inside the Microsoft.AspNetCore.App shared framework, so /health
// needs no package reference and none is declared. Problem details are registered so the framework's
// own challenge and every error response answer application/problem+json, which is the error shape
// the authored contracts publish.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// Resolved eagerly, so a localization provider that cannot be installed stops the host here rather
// than at the first validation that needed a translated message.
_ = app.Services.GetRequiredService<I18n>();

app.UseAuthentication();
app.UseAuthorization();

// The published contract document, anonymous because a description of the surface is not part of the
// surface it describes and every operation in it still states its own security requirement.
app.MapOpenApi().AllowAnonymous();

// One call per endpoint file. /health is anonymous and /v1/ping requires a token and answers 401
// without one; both properties are declared inside those files, where they are exercised.
app.MapHealthEndpoints();
app.MapPingEndpoints();

app.Run();

/// <summary>
/// Selects the localization provider for a locale token, reproducing the three-way selection at
/// <c>ws_objects/pfw.pbl.src/pfw.sra:L95-L102</c>.
/// </summary>
/// <param name="locale">
/// The configured locale token. <see cref="DataServicesOptions"/> restricts it to <c>en</c>,
/// <c>chs</c> or <c>cht</c> and validates that on start, so an unrecognised value cannot reach here
/// through configuration.
/// </param>
/// <returns>The provider for that locale.</returns>
/// <exception cref="InvalidOperationException">
/// The token is not one of the three the legacy declares. Unreachable through configuration, and a
/// hard failure rather than a silent default because a service that quietly fell back to another
/// locale would emit validation messages in a language no caller asked for.
/// </exception>
/// <remarks>
/// Compared with ordinal semantics: these are legacy symbols rather than human-readable text, and no
/// culture may participate in matching them. The Simplified Chinese provider is a genuine no-op
/// because Simplified Chinese is the base locale, and it is selectable anyway so that the
/// three-provider shape of the legacy survives intact.
/// </remarks>
static II18nProvider CreateLocaleProvider(string locale) => locale switch
{
    "en" => new EnglishProvider(),
    "chs" => new SimplifiedChineseProvider(),
    "cht" => new TraditionalChineseProvider(),
    _ => throw new InvalidOperationException(
        $"'{DataServicesOptions.SectionName}:Localization:Locale' is '{locale}', which is not one of "
            + "the three locales the legacy framework declares at "
            + "ws_objects/pfw.pbl.src/pfw.sra:L95-L102 - en, chs or cht."),
};

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile to an internal <c>Program</c> class, so
/// without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the sibling
/// <c>PowerFramework.DataServices.Tests</c> project cannot name the entry point, the service-level
/// tests cannot boot this host at all, and the per-service coverage gate becomes unreachable for every
/// line in this file.
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
