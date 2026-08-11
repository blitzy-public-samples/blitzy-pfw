// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT AND ENTRY POINT OF THE POWERFRAMEWORK DATASERVICES SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the DataWindow retrieval / validation / update triple, the 22-event
//  chain and the column-expression engine. Port 5102. It is reached by Gateway and by nothing else,
//  and it reaches Persistence (contracts C-05..C-08) and Security (contracts C-01/C-02).
//
//  This file WIRES and NOTHING ELSE. Every behaviour it touches lives in another file, next to the
//  `ws_objects/**` locator that authorises it: the event chain in Domain/, the expansion engine in
//  Expressions/, the four headless models in Services/, the two published gRPC surfaces in Grpc/,
//  and the three route groups in Endpoints/. Composition is the one thing that has NO legacy
//  analogue, because PowerFramework is a library with no process of its own, no listener and no
//  server tier - so this file's job did not exist before the decomposition.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//      ws_objects/pfw.pbl.src/pfw.sra:L88-L108   The framework application lifecycle. `open`
//          becomes host startup and `close` becomes host shutdown. `pfwInitialize(...)` [:L91] and
//          `pfwFinalize()` [:L108] are the pairing docs/README.md's initialisation section states
//          MUST be paired; Gateway owns the initializer analogue, and this host reproduces the
//          PAIRING for what it owns - see section 9 below.
//      ws_objects/pfw.pbl.src/pfw.sra:L94-L102   `lang = "en"` and the three-way provider switch.
//          The DEFECT is the hardcoding, not the value, so the value is preserved as the CONFIGURED
//          DEFAULT while the un-configurability is removed (constraint C-F).
//      ws_objects/pfw.pbl.src/pfw.sra:L111-L144  `systemerror`: split the assert payload on `~r~n`
//          into up to seven fields, format, show, then `HALT CLOSE` [:L143]. Gateway owns the
//          DECODING half (Diagnostics/SystemErrorHandler.cs). What THIS file inherits is the
//          POSTURE: a structural fault ends the process. Softening that into warn-and-continue
//          would be a behavioural change dressed up as robustness (AAP 0.1.4).
//
//  TRANSPORT, AND WHY IT IS NOT REST-ONLY (constraint C-K)
//  DataServices' legacy interface is a 22-event ordered chain with veto semantics, a `ref string`
//  out-parameter [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L13] and an `any` return
//  over a `string[]` argument [:L14]. That shape demands compile-time contract enforcement,
//  bidirectional streaming to carry the ordered chain, and a status model rich enough for a
//  four-value alphabet and a tri-valued veto, so gRPC is the PRIMARY transport and the published
//  protocol definitions live in PowerFramework.Contracts. A thin REST projection exists for Gateway
//  alone. Both Kestrel endpoints - HTTP/1.1 for REST and HTTP/2 for gRPC - come from the Kestrel
//  section of appsettings.json and are NEVER restated in code: there is no UseUrls, no Listen call,
//  no port literal and no protocol literal anywhere below. Environment variables win over
//  appsettings.json by design, and this file does not fight them.
//
//  FAIL FAST, PRESERVED AS FAIL FAST (AAP 0.1.4, 0.6.7)
//  Both option groups are validated ON START and again eagerly immediately after Build, so a
//  deployment that misconfigures the Persistence address, the Security authority or the token
//  audience fails to come up instead of failing the first request that touched the bad value. The
//  localization provider is installed and its return code CHECKED at startup for the same reason.
//  Worker-session creation failure is fatal in the legacy and a decoded assertion failure ends the
//  application; the managed equivalent is startup validation that terminates.
//
//  A STRUCTURAL FAULT AND A TRANSIENT NETWORK FAULT ARE OPPOSITE CASES, AND MUST NEVER BE
//  CONFLATED. A structural fault - a missing or malformed mandatory setting, a provider that will
//  not install, a container that cannot compose a required collaborator - terminates the process
//  here, at startup, before a listener exists. A transient network fault on an edge the
//  decomposition itself created is handled by the resilience pipeline of section 5 and is NEVER a
//  reason to terminate: an in-process call could not fail in transit, a network call can, and
//  handling that new failure mode is required BY the transition. One is a deployment defect, the
//  other is normal operation of a distributed system.
//
//  THE FOUR ALPHABETS THAT SHARE THE NUMERALS 1 AND 2, RECORDED HERE SO NO WIRING EVER MAPS ONE
//  ONTO ANOTHER: Eventful's tri-valued VetoResult (continue / prevent-once / prevent-deep); the
//  broker's OnException hook (1 = prevent, 2 = continue, anything else = rethrow); the
//  RetCode.PREVENT convention read through Predicates.IsPrevented; and this service's own
//  {0,1,2,3} item-change alphabet [se_cst_dw.sru:L182-L253]. Nothing in this file translates
//  between them, and nothing in this file may start to.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No DbContext, no EF Core, no Microsoft.Data.Sqlite, no connection factory, no connection
//      string and no migration call. Persistence is the ONLY service in the system that holds a
//      storage provider, and SQL generation and execution belong to it alone; this service reaches
//      data exclusively through the persistence.v1 gRPC clients (constraints C-A and C-E).
//    * No registration, route, handler or options type for DesignSystem, Documents, Integration or
//      ScriptBridge, and no NotImplementedException-throwing placeholder for any of them. The four
//      reserved 501 routes are GATEWAY's routing metadata, not this service's. In particular there
//      is no window-positioning, DPI-conversion, font-measurement, IME or popup-menu-rendering
//      registration: those are the deferred RENDERING halves of ColumnSort, ContextMenu and
//      DropDownSearch, surfaced as data over the contract and named as the `/v1/design/**`
//      extension point (constraint C-D, AAP 0.3.5 and 0.4.4).
//    * No token minting, no signing key, no SymmetricSecurityKey, no signing credential and no
//      reference to any signing secret. Security is the SOLE issuer; this service holds
//      VERIFICATION material only (constraints C-F and C-G).
//    * No package and no middleware the AAP does not call for. The AAP 0.5.3 exclusions are binding:
//      no Serilog, no OpenTelemetry, no FluentValidation, no Scalar or Swashbuckle UI, no gRPC
//      server reflection, and no health-check package - health-check registration ships inside the
//      Microsoft.AspNetCore.App shared framework.
//    * No SCREAMING_SNAKE constant DECLARED here. The preserved legacy identifiers are CONSUMED
//      from PowerFramework.Shared.Kernel, whose files are the ones the repository .editorconfig
//      scopes its CA1707 suppressions to (AAP 0.4.5.3).
//
//  THE ONLY VALUE LITERALS BELOW ARE THE THREE LEGACY LOCALE TOKENS - "en", "chs" and "cht" - in
//  the provider-selection switch, and they are legacy SYMBOLS rather than configuration, matched
//  ordinally, carried verbatim from pfw.sra:L94-L102. Everything else that looks like a literal is
//  either a configuration KEY name or a diagnostic message.
// ==================================================================================================

using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Authorization;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Domain;
using PowerFramework.DataServices.Endpoints;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Grpc;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;

// The four generated persistence.v1 clients, reached through aliases rather than a namespace import.
// PowerFramework.Contracts.Persistence.V1 publishes generated messages whose bare names collide with
// this project's own domain vocabulary, so importing it wholesale would make several names ambiguous
// (CS0104). Naming exactly the four client types keeps the collision surface at zero.
using PersistenceCommandClient =
    PowerFramework.Contracts.Persistence.V1.CommandService.CommandServiceClient;
using PersistenceQueryClient =
    PowerFramework.Contracts.Persistence.V1.QueryService.QueryServiceClient;
using PersistenceTransactionClient =
    PowerFramework.Contracts.Persistence.V1.TransactionService.TransactionServiceClient;
using PersistenceUpdateClient =
    PowerFramework.Contracts.Persistence.V1.UpdateService.UpdateServiceClient;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// THE COMPOSITION, IN SEVEN NAMED GROUPS
//
// Each group is an INTERNAL EXTENSION METHOD on IServiceCollection rather than a run of inline calls.
// Two reasons, and either alone would decide it. First legibility: a reader looking for "how is the
// token handler configured" reads one method instead of scanning a flat script. Second testability
// (constraint C-H): the sibling PowerFramework.DataServices.Tests project sees internals through the
// InternalsVisibleTo item in this project's .csproj, so a test can call a single group directly and
// assert what it registered without booting a host - and, when it does boot a host, top-level
// statements compile into the `Program` type declared at the foot of this file, which
// WebApplicationFactory<Program> needs by name.
//
// ORDER IS NOT ARBITRARY, BUT NEITHER IS IT BEHAVIOURAL. Registration order in a container does not
// determine construction order; the groups are sequenced so that a reader meets configuration before
// the things configured by it. The ONE place order genuinely is contract - the creation and
// initialisation sequence of the five attached DataWindow services - is NOT owned here at all, and
// section 7 records exactly where it is owned.
// --------------------------------------------------------------------------------------------------
builder.Services.AddDataServicesOptions(builder.Configuration);
builder.Services.AddDataServicesDeterminismSeam();
builder.Services.AddDataServicesLocalization();
builder.Services.AddDataServicesAuthentication();
builder.Services.AddDataServicesClients();
builder.Services.AddDataServicesDomain();
builder.Services.AddDataServicesPublishedSurface();

WebApplication app = builder.Build();

// --------------------------------------------------------------------------------------------------
// STARTUP VALIDATION - THE MANAGED FORM OF `HALT CLOSE` [pfw.sra:L143]
//
// ValidateOnStart already fails the host during StartAsync, and these three lines fail it EARLIER,
// before Kestrel binds a socket. That ordering matters operationally: a process that binds 5102 and
// then dies has, for a moment, satisfied a readiness probe that reads a connection rather than a
// status, and compose's `depends_on: condition: service_healthy` chain would let Gateway start behind
// it. Failing before the listener exists removes that window entirely.
//
// Each guard uses PowerFramework.Shared.Diagnostics' `Assertions` - note the name: the static class
// is `Assertions`, NOT `Assert`, because a static class cannot contain a member of its own name
// (CS0542), even though the file is Assert.cs. `Assertions.Assert` throws `AssertionFailure`, which
// propagates out of this file uncaught and ends the process, which IS the reproduced posture.
//
// THE TRI-STATE HOLE IS ACCOUNTED FOR RATHER THAN STUMBLED OVER. `Assertions.Assert(long?)` fires
// when `Predicates.IsSucceeded` answers false, and `IsSucceeded` tests `>= 0` [issucceeded.srf:L11-
// L13] - so `RetCode.PREVENT` (1) would NOT fire while `RetCode.CANCELLED` (-2) and null both DO.
// The one return code guarded below is the localization installer's, whose codomain is
// `RetCode.OK` or `RetCode.E_INVALID_OBJECT` (-5); the second is a failure under this predicate and
// the value PREVENT is not reachable, so the guard is exact for its subject rather than merely
// plausible.
// --------------------------------------------------------------------------------------------------
Assertions.Assert(
    app.Services.GetRequiredService<IOptions<DataServicesOptions>>().Value,
    $"'{DataServicesOptions.SectionName}' did not bind to an options instance, so this host has no "
        + "configured upstream addresses, session lifetimes or preserved legacy defaults. Startup is "
        + "terminated rather than continued.");

Assertions.Assert(
    app.Services.GetRequiredService<IOptions<JwtAuthenticationOptions>>().Value,
    $"'{JwtAuthenticationOptions.SectionName}' did not bind to an options instance, so inbound tokens "
        + "could not be validated against Security's published verification material. An "
        + "unauthenticated boundary is a structural fault under constraint C-G, so startup is "
        + "terminated rather than continued.");

// Resolved EAGERLY on purpose. The localization singleton's factory installs the provider and checks
// the installer's return code, so resolving it here is what turns a provider that will not install
// into a startup failure instead of an untranslated validation message discovered in production.
Assertions.Assert(
    app.Services.GetRequiredService<I18n>(),
    "The localization facade did not compose. The DataWindow validation path routes its diagnostics "
        + "through it [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L355, :L357], so a "
        + "host without it is structurally faulty rather than partly working.");

// Resolved eagerly so that a configured-but-unreadable trust anchor stops the host here rather than at
// the first outbound call. A deployment that meant to pin internal trust and cannot has already lost
// every authenticated call it would make: every one of the five internal channels would refuse the
// certificate it is handed, so discovering it at startup is a failure to launch whereas discovering it
// at the first token request is an outage that looks like an upstream problem. An UNSET anchor resolves
// to platform default trust and is not a fault.
_ = app.Services.GetRequiredService<InternalTlsTrust>();

// THE OTHER HALF OF THE SAME HANDSHAKE, AND FOR THE SAME REASON. The anchor above says which authority
// this service ACCEPTS; this says which certificate it PRESENTS. `POST /v1/tokens` is protected by
// mutual TLS and by nothing else - a caller cannot present a bearer token in order to obtain its first
// bearer token - so the identity is what makes every subsequent authenticated call reachable at all.
// Resolved eagerly so a configured-but-unreadable PEM pair is a failure to launch rather than a 401
// from the issuance edge that looks like an upstream problem. An UNCONFIGURED pair resolves to an empty
// collection, which is not a fault here: it is a deployment that presents no identity, and
// `SecurityClient` says so by name at the first token request rather than sending a request that can
// only be refused.
X509Certificate2Collection securityClientIdentity =
    app.Services.GetRequiredService<X509Certificate2Collection>();

if (securityClientIdentity.Count == 0)
{
    app.Logger.LogWarning(
        "No client identity is configured at '{CertificateKey}' and '{KeyKey}', so this host presents "
            + "no certificate on the token-issuance edge and cannot obtain a credential. Every "
            + "authenticated outbound call - the four Persistence contracts and the crypto contract - "
            + "will fail with a named configuration diagnostic on first use. Readiness reports the "
            + "bootstrap as unavailable. Neither path is recorded here, because a startup log must not "
            + "publish where key material is mounted.",
        $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}"
            + $":{nameof(SecurityClientOptions.MutualTls)}"
            + $":{nameof(MutualTlsClientOptions.CertificatePath)}",
        $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Security)}"
            + $":{nameof(SecurityClientOptions.MutualTls)}"
            + $":{nameof(MutualTlsClientOptions.CertificateKeyPath)}");
}

// Resolved eagerly so a deadline pair that cannot be constructed - a non-positive duration that slipped
// past validation - is a failure to launch rather than an exception on the first outbound call.
_ = app.Services.GetRequiredService<OutboundDeadlines>();

// FORCED HERE FOR THE SAME REASON, and this one matters more than it looks. The retry classification is
// built from the contract descriptors in a static initializer, so a method name that no longer resolves
// would otherwise throw at the first outbound FAILURE - the one moment when a second, unrelated fault is
// hardest to diagnose. Touching it now turns a contract-versus-classification mismatch into a refusal to
// start, which is the posture the legacy had when a structural fault terminated the application outright.
_ = OutboundCallPolicy.Verify();

// --------------------------------------------------------------------------------------------------
// THE REQUEST PIPELINE
//
// Four middleware components, in the only order that works, and the two diagnostics ones come FIRST
// because each works by observing what the middlewares beneath it produced - and the response they most
// need to observe is the bearer challenge the authentication middleware writes. Authentication then
// establishes the principal and authorization decides.
//
// THE TWO DIAGNOSTICS MIDDLEWARES ARE HERE FOR CONTRACT FIDELITY, NOT AS HARDENING, and that is what makes
// them the narrow exception to the no-unrequested-middleware rule (constraint C-B). Without them the
// framework answers a bare status with NO BODY on every path that never reaches this service's own code,
// while this service's own routes declare ProducesProblem for 401, 403 and 404 and register the
// problem-details service to write them - a promise the pipeline was not keeping. UseExceptionHandler
// converts an unhandled REST fault into the same problem shape rather than an empty 500.
//
// NEITHER TOUCHES THE gRPC EDGE. A gRPC response always carries its own content type, and status-code pages
// write only where a response has neither a body nor a content type; a gRPC handler's own faults are
// converted to trailers by the hosting layer before they could reach an exception handler. An
// UNAUTHENTICATED gRPC call does gain a problem body on its 401, which changes nothing a gRPC client
// observes: the client maps the HTTP status before it ever looks at the content type.
//
// Nothing else is added. There is still no CORS policy (no browser reaches this service - Gateway is the
// sole ingress), no HTTPS redirection (both shipped endpoints are already https, so a redirection
// middleware would have nothing to redirect and would only add a hop that could rewrite an HTTP/2 gRPC
// call into an HTTP/1.1 one), no response compression and no rate limiter.
// --------------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapDataServicesEndpoints();

// --------------------------------------------------------------------------------------------------
// SHUTDOWN, PAIRED WITH STARTUP AND ORDERED AFTER THE ORACLE
//
// docs/README.md's initialisation section states that initialize and finalize MUST be paired, and
// pfw.sra pairs them across `open` [:L91] and `close` [:L108]. Gateway owns the framework-initializer
// analogue; what THIS host owns is its two session stores, and they are released deterministically
// here rather than left to finalizers.
//
// THE ORDER MIRRORS ORDERING FACT 2 [se_cst_dw.sru:L583-L589], whose principle is UNSUBSCRIBE BEFORE
// DESTROY: `Eventful.of_Off()` runs FIRST and only then are the five attached services destroyed, so
// that no dispatch can reach a service that no longer exists. Validation sessions are therefore
// closed FIRST, because a validation session is what owns an event chain and a chain's own Teardown
// is what reproduces those nine lines; closing the sessions is what causes each chain to unsubscribe
// before anything it might dispatch into goes away. Expression sessions are closed SECOND: they are
// C-04's independent scope, and each close disposes its engines, whose legacy destructor body is
// `Destroy _vecCalcStack` [n_cst_dwsvc_columnexp.sru:L2425].
//
// REGISTERED ON ApplicationStopping RATHER THAN ApplicationStopped, so the stores are released while
// the host is still draining in-flight calls and before the container disposes anything underneath
// them. The two counts are recorded because a non-zero count at shutdown is the only evidence that
// sessions were still open, and a session left open is a leak this ordering exists to prevent.
// --------------------------------------------------------------------------------------------------
app.Lifetime.ApplicationStopping.Register(() =>
{
    ILogger logger = app.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("PowerFramework.DataServices.Shutdown");

    int validationSessions = app.Services.GetRequiredService<ValidationSessionRegistry>().CloseAll();
    int expressionSessions = app.Services.GetRequiredService<ExpressionSessionRegistry>().CloseAll();

    // Counts only. Never a session identifier, never a DataWindow handle, never a buffer value and
    // never a statement: the redaction obligation of AAP 0.6.3.8 is a property of every log site in
    // the system, not only of the one that relays a DbError.
    logger.LogInformation(
        "DataServices shutdown released {ValidationSessions} validation session(s) and then "
            + "{ExpressionSessions} expression session(s), in that order.",
        validationSessions,
        expressionSessions);
});

app.Run();

/// <summary>
/// The seven registration groups and the one mapping group this service's composition root is built
/// from.
/// </summary>
/// <remarks>
/// <para>
/// INTERNAL, AND VISIBLE TO THE TEST PROJECT ALONE through the <c>InternalsVisibleTo</c> item in
/// <c>PowerFramework.DataServices.csproj</c>. Nothing here is public API: composition is this
/// service's own concern and no other assembly may reach into it (constraint C-A).
/// </para>
/// <para>
/// EVERY METHOD IS PURE REGISTRATION. Not one of them opens a socket, reads a file, resolves a
/// service, contacts an upstream or reads a clock, so calling all seven is free and a test may call
/// any one in isolation. The single deliberate exception is stated where it occurs: the localization
/// singleton's FACTORY installs a provider, and factories run on first resolve rather than on
/// registration.
/// </para>
/// </remarks>
internal static class DataServicesComposition
{
    /// <summary>
    /// Binds and validates the two option groups, failing the host on start rather than on the first
    /// request that touched a broken value.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The host configuration to bind from.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// THE VALIDATORS ARE <c>IValidateOptions</c> IMPLEMENTATIONS RATHER THAN DATA ANNOTATIONS, and
    /// that is a requirement rather than a preference: they express CROSS-PROPERTY rules that
    /// annotations cannot state - that a paged column-expression resolution must carry a positive row
    /// count while every other resolution must not, that a circuit-breaker failure ratio sits inside
    /// an open interval, that an upstream address is absolute and well formed, and that a locale is
    /// one of exactly three legacy tokens. Each reports EVERY broken rule at once, so an operator
    /// fixes one deployment instead of discovering the next fault on the next attempt.
    /// </para>
    /// <para>
    /// <c>DataServices:DropDownSearch:ShowFilteredRows</c> IS BOUND AS A NULLABLE BOOLEAN AND NULL IS
    /// NEVER COERCED TO FALSE. <c>n_cst_dwsvc_dropdownsearch.sru</c> declares
    /// <c>privatewrite boolean #ShowFilteredRows</c> with NO initializer and an inline note that NULL
    /// means auto-determine, and auto-determine is the default. Collapsing null to false would replace
    /// auto-determination with an explicit "no" - a behaviour change (constraint C-B). The same
    /// discipline is general: PowerBuilder has null for value types and the tri-state predicates
    /// depend on it, so collapsing null to zero anywhere converts "neither succeeded nor failed" into
    /// "succeeded". The options type declares the nullable and this method does not flatten it.
    /// </para>
    /// <para>
    /// EXACTLY ONE SECRET PASSES THROUGH HERE, AND IT ARRIVES BY A DIFFERENT ROUTE FROM EVERY OTHER
    /// SETTING. Both groups otherwise carry addresses, audiences, lifetimes, thresholds and preserved
    /// legacy defaults; neither declares a signing key, a token or a certificate, and the only other
    /// credential-shaped members are the two mutual-TLS PATH settings, which are paths and not material
    /// (constraint C-F). The exception is this service's issuance secret, which
    /// <see cref="ApplyIssuanceSecret"/> reads from the FLAT configuration key
    /// <see cref="SecurityClientOptions.ClientSecretConfigurationKey"/> - not from the bound section,
    /// because the environment-variable provider translates only a double underscore into the section
    /// separator and that key contains none. It therefore appears in no settings file and in no
    /// container definition; the settings document names the KEY and never the value.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services
            .AddOptions<DataServicesOptions>()
            .Bind(configuration.GetSection(DataServicesOptions.SectionName))
            // APPLIED AFTER BINDING AND BEFORE VALIDATION, WHICH IS THE ONLY ORDER THAT WORKS. The
            // validator's "this service can present no issuance credential at all" rule reads the
            // secret, so applying it afterwards would fail every deployment that configured one; and
            // binding cannot reach it, because the key is flat rather than sectioned. PostConfigure runs
            // between the two.
            .PostConfigure(options => ApplyIssuanceSecret(options, configuration))
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<DataServicesOptions>, DataServicesOptionsValidator>();

        _ = services
            .AddOptions<JwtAuthenticationOptions>()
            .Bind(configuration.GetSection(JwtAuthenticationOptions.SectionName))
            .ValidateOnStart();

        services
            .TryAddSingleton<IValidateOptions<JwtAuthenticationOptions>,
                JwtAuthenticationOptionsValidator>();

        return services;
    }

    /// <summary>
    /// Copies this service's issuance secret from its flat configuration key onto the bound options.
    /// </summary>
    /// <param name="options">The bound options instance to complete.</param>
    /// <param name="configuration">The host configuration to read the flat key from.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS IS NOT ORDINARY SECTION BINDING. The secret's configuration key is
    /// <c>SECURITY_CLIENT_SECRET_DATASERVICES</c>, a flat top-level key of exactly that spelling. The
    /// environment-variable configuration provider maps a DOUBLE underscore, and only a double
    /// underscore, onto the <c>:</c> section separator, so that name is not a path into the
    /// <c>DataServices</c> section and binding the section will never populate the property however it
    /// is bound. The name is fixed by Security's issuance roster and by
    /// <c>orchestration/.env.example</c>, which declare it once for both sides of the edge, so it cannot
    /// be renamed to a sectioned spelling and no alias may be added: two accepted spellings for one
    /// input is two ways for a deployment to be half configured.
    /// </para>
    /// <para>
    /// ABSENCE IS LEFT ALONE RATHER THAN DEFAULTED. A missing key leaves the property at its empty
    /// initial value, and the validator then decides whether that is legal - it is, but only on a
    /// deployment that configured the client-certificate pair instead. Substituting anything here would
    /// pre-empt that decision and turn a startup refusal into an authentication failure on the first
    /// call. Whitespace is treated as absent for the same reason a blank secret is: it cannot
    /// authenticate, and accepting it would produce a <c>401</c> whose cause is invisible.
    /// </para>
    /// <para>
    /// THE VALUE IS NEVER LOGGED HERE OR ANYWHERE. This method writes it to one property and returns;
    /// no diagnostic records its presence, its length or its absence, because the validator's message
    /// already names the KEY an operator must set and a log line about a secret is a log line
    /// containing a secret's shape.
    /// </para>
    /// </remarks>
    private static void ApplyIssuanceSecret(DataServicesOptions options, IConfiguration configuration)
    {
        string? configured = configuration[SecurityClientOptions.ClientSecretConfigurationKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        // Null-conditional because binding a section that omits `Security` entirely leaves the group
        // null, and the validator - not this method - is what reports that.
        if (options.Security is not null)
        {
            options.Security.ClientSecret = configured;
        }
    }

    /// <summary>
    /// Registers the clock every component in this service reads, as an injected seam rather than an
    /// ambient static.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A REQUIREMENT OF THE PARITY MODEL, NOT A CONVENIENCE. The characterization technique's one hard
    /// prerequisite is repeatability, with every non-deterministic value masked from BOTH the master
    /// and the candidate recording (AAP 0.6.7). Session idle expiry, the ping timestamp and the
    /// expression trace all read a clock, so the clock is the seam - registering it here is what lets
    /// one substitution in a test host make the whole process deterministic.
    /// </para>
    /// <para>
    /// Registered as the CONCRETE <see cref="TimeProvider"/> because that is the abstraction: every
    /// consumer in this service declares a <c>TimeProvider?</c> parameter and falls back to
    /// <see cref="TimeProvider.System"/> when none is supplied, so an interface wrapper would add a
    /// type without adding a capability.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesDeterminismSeam(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Registers the localization surface and installs the provider the configured locale selects.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// A CONFIRMED REQUIREMENT OF THIS SERVICE, NOT A CONDITIONAL ONE. <c>I18N(CAT_DWSVC, ...)</c> is
    /// called from INSIDE the DataWindow validation path - <c>se_cst_dw.sru:L355</c> supplies the
    /// fallback text for an invalid value and <c>:L357</c> supplies the dialog title - and also at
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c> and at roughly twenty-one sites in
    /// <c>n_cst_dwsvc_contextmenu.sru</c>. So this service needs the surface in its own right rather
    /// than inheriting it from the ingress. (Locator note for a future reader: the plan cites
    /// <c>:L357, :L368</c>; the VERIFIED sites are <c>:L355</c> and <c>:L357</c> - line 368 is a
    /// comment.)
    /// </para>
    /// <para>
    /// AN INJECTED INSTANCE, NEVER A STATIC MUTABLE SLOT. The legacy installs into a global
    /// [<c>pfw.sra:L103</c>], which is exactly what a multi-tenant server process must not do: a
    /// static slot would be shared across every concurrent call and rewritable by any of them. The
    /// facade is a container singleton and every consumer takes it as a constructor parameter.
    /// </para>
    /// <para>
    /// THE INSTALLER'S RETURN CODE IS CHECKED, THROUGH THE PUBLISHED PREDICATE. <c>I18n</c>'s
    /// single-argument overload is the INSTALLER and it answers <c>RetCode.E_INVALID_OBJECT</c> for a
    /// provider it will not accept. The check goes through <see cref="Predicates.IsSucceeded(long?)"/>
    /// rather than a hand-rolled comparison, because the tri-state algebra is published contract and
    /// re-deriving it at a call site is how the two drift apart.
    /// </para>
    /// <para>
    /// THE SILENT-PASSTHROUGH FALLBACK IS PRESERVED EXACTLY [<c>i18n.srf</c>]. With no matching entry
    /// the text comes back UNCHANGED - never throwing, never logging, never marked untranslated - and
    /// nothing here adds a missing-translation warning. Nothing here probes for, copies or embeds
    /// <c>pfw.i18n.xml</c> either: it is read-only legacy data, its reader is deliberately tolerant of
    /// its absence, and unchanged text IS the preserved behaviour of a missing table.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<II18nProvider>(static serviceProvider => CreateLocaleProvider(
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value.Localization
                .Locale));

        services.TryAddSingleton<I18n>(static serviceProvider =>
        {
            I18n localization = new();

            long installed = localization.I18N(serviceProvider.GetRequiredService<II18nProvider>());

            if (!Predicates.IsSucceeded(installed))
            {
                // A STRUCTURAL FAULT, so it terminates. Resolved eagerly at startup by the guard in
                // the top-level statements, which is what makes this a startup failure rather than a
                // first-request failure.
                Assertions.AssertFailed(
                    $"Installing the localization provider answered {installed}, which is not a "
                    + "success under the preserved return-code algebra. The DataWindow validation "
                    + "path routes its diagnostics through this surface "
                    + "[ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L355, :L357], so a "
                    + "host with no installed provider would emit untranslated validation messages "
                    + "instead of failing visibly.");
            }

            return localization;
        });

        return services;
    }

    /// <summary>
    /// Selects the localization provider for a locale token, reproducing the three-way selection at
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L95-L102</c>.
    /// </summary>
    /// <param name="locale">
    /// The configured locale token. <see cref="DataServicesOptions"/> restricts it to the three legacy
    /// values and validates that on start, so an unrecognised value cannot reach here through
    /// configuration.
    /// </param>
    /// <returns>The provider for that locale.</returns>
    /// <remarks>
    /// <para>
    /// THE THREE TOKENS ARE THE ONLY VALUE LITERALS IN THIS FILE, and they are legacy SYMBOLS rather
    /// than configuration: <c>pfw.sra:L94</c> hardcodes <c>lang = "en"</c> and <c>:L95-L102</c>
    /// switches on it to <c>n_cst_i18n_en</c>, <c>n_cst_i18n_chs</c> or <c>n_cst_i18n_cht</c>, then
    /// installs at <c>:L103</c>. The DEFECT is the hardcoding, so <c>"en"</c> survives as the
    /// CONFIGURED DEFAULT declared by the options type while the un-configurability is removed.
    /// Comparison is ordinal because these are symbols and no culture may participate in matching
    /// them.
    /// </para>
    /// <para>
    /// THE SIMPLIFIED CHINESE PROVIDER IS SELECTABLE AND IS NOT A NO-OP. Its legacy translate body is
    /// commented out because Simplified Chinese is the BASE locale, but the ported provider answers
    /// HANDLED (1) for framework-sourced text while leaving the text untouched - which is
    /// behaviourally distinct from answering 0, because a handled answer stops the lookup chain. The
    /// three-provider shape of the legacy therefore survives intact rather than being collapsed to
    /// two.
    /// </para>
    /// <para>
    /// THE UNRECOGNISED ARM FAILS RATHER THAN FALLING BACK. A service that quietly defaulted to
    /// another locale would emit validation messages in a language no caller asked for, and the
    /// characterization comparison for every localized message would silently diverge.
    /// </para>
    /// </remarks>
    private static II18nProvider CreateLocaleProvider(string locale) => locale switch
    {
        "en" => new EnglishProvider(),
        "chs" => new SimplifiedChineseProvider(),
        "cht" => new TraditionalChineseProvider(),
        _ => throw new InvalidOperationException(
            $"'{DataServicesOptions.SectionName}:Localization:Locale' is '{locale}', which is not one "
            + "of the three locales the legacy framework declares at "
            + "ws_objects/pfw.pbl.src/pfw.sra:L95-L102 - en, chs or cht."),
    };


    /// <summary>
    /// Registers inbound token validation and the authorization policy that closes every route by
    /// default.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// AN INTERNAL EDGE IS A CREATED BOUNDARY TOO (constraint C-G). The legacy opened NO listening
    /// socket, registered no route and received no unsolicited request, so this service's inbound
    /// surface was brought into existence by the decomposition itself. "No new attack surface"
    /// therefore has to be read as "every newly created surface is authenticated from the outset",
    /// which is what these registrations deliver - and it applies to the gRPC edge Gateway calls
    /// exactly as it applies to a public one.
    /// </para>
    /// <para>
    /// THE STOCK HANDLER, WITH NO BESPOKE RETRIEVAL CODE. Configured entirely from the validated
    /// <c>Authentication:Jwt</c> section, the bearer handler fetches Security's OIDC discovery document
    /// and its published key set from beneath the configured authority by itself. That is precisely why
    /// Security is REST rather than gRPC (AAP 0.1.5): choosing gRPC there would have forced
    /// hand-written key-set retrieval into three services, a net INCREASE in hand-written security
    /// code. Nothing below fetches, caches, parses or pins a key.
    /// </para>
    /// <para>
    /// ALL FOUR VALIDATIONS STAY ON and none is relaxed. The shipped <c>appsettings.json</c> sets
    /// issuer, audience, lifetime and issuer-signing-key validation to true, and
    /// <c>appsettings.Development.json</c> does not override any of them - so there is no
    /// environment-conditional bypass anywhere: a bypass that exists only in Development is still a
    /// bypass, and it would make the local build disagree with the published contract. The four
    /// switches are BOUND rather than hardcoded so a deployment can be audited for them by reading its
    /// settings, and this file never narrows, defaults or conditionally overrides one. Note precisely
    /// what that does and does not mean: the options validator enforces that the authority and the
    /// audience are present and that the authority is an absolute address whose scheme agrees with
    /// <c>RequireHttpsMetadata</c>, but it does NOT reject a deployment that turns a validation switch
    /// off. Turning one off is therefore a deployment defect visible in the settings file, not a code
    /// path, and the correct place to catch it is deployment review.
    /// </para>
    /// <para>
    /// NO SIGNING MATERIAL OF ANY KIND. No signing key, no symmetric key, no signing credential, no
    /// issuer signing key supplied as a value and no token generator. Security is the SOLE issuer;
    /// this service holds verification material only, and the only appearances of the word "signing"
    /// below are the names of VALIDATION switches (constraints C-F and C-G).
    /// </para>
    /// <para>
    /// METADATA IS FETCHED LAZILY by the handler on first use rather than at startup, which is what
    /// lets this host come up while Security is still starting. An anonymous request is refused before
    /// any metadata is needed, so the mandated 401 on <c>/v1/ping</c> holds even with Security
    /// unreachable.
    /// </para>
    /// <para>
    /// A FALLBACK POLICY RATHER THAN ONLY A DEFAULT ONE. Every endpoint file already declares its own
    /// requirement explicitly, and the fallback is what makes the ABSENCE of a declaration a closed
    /// door instead of an open one. A default policy alone would not do that: it applies where
    /// authorization is requested, whereas the fallback applies where none was requested at all. The
    /// two deliberate exceptions opt out where a reader looks for them - <c>/health</c> declares
    /// <c>AllowAnonymous</c> inside Endpoints/HealthEndpoints.cs, and so does the published contract
    /// document at its mapping site.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        _ = services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtAuthenticationOptions>, InternalTlsTrust>(
                static (bearer, authentication, trust) =>
            {
                JwtAuthenticationOptions configured = authentication.Value;

                // THE KEY-SET BACKCHANNEL IS AN INTERNAL CHANNEL TOO, AND IT IS THE MOST CONSEQUENTIAL
                // ONE. The handler fetches Security's discovery document and published key set over its
                // own HttpClient, built from this handler rather than from any client registration -
                // so an anchor applied to every other channel and not here would leave the one channel
                // that decides WHICH KEYS SIGN A VALID TOKEN unable to connect. Supplied only when an
                // anchor is configured, so an unset anchor leaves the handler's default untouched.
                if (trust.IsPinned)
                {
                    SocketsHttpHandler backchannel = new();

                    trust.Apply(backchannel);

                    bearer.BackchannelHttpHandler = backchannel;
                }

                bearer.Authority = configured.Authority;
                bearer.Audience = configured.Audience;
                bearer.RequireHttpsMetadata = configured.RequireHttpsMetadata;

                // An explicit metadata address overrides the authority-relative default, which is what
                // lets a deployment point the handler at a key set published somewhere other than the
                // conventional path beneath the authority. Left unset, the handler derives it.
                if (!string.IsNullOrWhiteSpace(configured.MetadataAddress))
                {
                    bearer.MetadataAddress = configured.MetadataAddress;
                }

                // ALL FOUR ARE ASSIGNED LITERALLY, NOT READ. Each removes an entire class of forgery,
                // so none is a deployment choice: without issuer validation a token from any issuer is
                // accepted; without audience validation a token minted for another service is replayable
                // here; without lifetime validation Security's short lifetimes bound nothing; without
                // signing-key validation the signature is not checked at all. Reading them left a
                // configuration path that could disable a check while the host still reported healthy,
                // which is an unauthenticated boundary wearing the shape of an authenticated one
                // (constraint C-G). The bound properties SURVIVE so a deployment can still be audited by
                // reading its settings file, and JwtAuthenticationOptionsValidator refuses a configured
                // false outright - because ignoring one in silence would let an operator believe a switch
                // took effect when it did not.
                bearer.TokenValidationParameters.ValidateIssuer = true;
                bearer.TokenValidationParameters.ValidateAudience = true;
                bearer.TokenValidationParameters.ValidateLifetime = true;
                bearer.TokenValidationParameters.ValidateIssuerSigningKey = true;

                // CLOCK SKEW IS BOUNDED AND NOT CONFIGURABLE. The library's default is five minutes,
                // which silently extends every token's usable life by that much; and an unbounded
                // setting would let a deployment extend it without limit, which is lifetime validation
                // switched off by another name. Thirty seconds absorbs ordinary clock drift between
                // containers on one host - the topology the frozen environment describes - without
                // meaningfully widening the window. Security mints with a truncated whole-second
                // timestamp, so no sub-second allowance is required either.
                bearer.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);

                // INBOUND CLAIM NAMES ARE NOT REMAPPED. The handler's default rewrites short JWT claim
                // names onto long SOAP-era URIs, so `sub` arrives as a URI and a scope check written
                // against the name the token actually carries finds nothing. Turning it off means every
                // claim this service reads is spelled exactly as Security minted it and as the contract
                // publishes it - one spelling on the wire, in a log record and in a characterization
                // recording.
                bearer.MapInboundClaims = false;
            });

        services.AddAuthorization(static options =>
        {
            AuthorizationPolicy authenticated = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;

            // AND AUTHENTICATION IS NOT AUTHORIZATION. The default and the fallback close the door on a
            // route that declares nothing; deciding WHO may open it and for WHAT is the job of the two
            // named policies, and those are registered in ONE place - Authorization/ScopeAuthorization.cs,
            // reached through AddScopeAuthorization() below. Without them, any holder of any token minted
            // for this audience could call both contracts and all thirty-nine projected routes: a
            // credential obtained to read a DataWindow could drive the expression engine, and a credential
            // minted for a caller with no business here could do either (CWE-862, CWE-863).
            //
            // ⚠ THEY ARE NOT REGISTERED HERE AS WELL, AND THAT IS THE POINT ⚠
            //
            // Two AddPolicy calls for one capability is not belt-and-braces: AddPolicy REPLACES a policy of
            // the same name, and configuration callbacks run in registration order, so whichever ran last
            // silently became the whole policy. Registering the pair here AND in ScopeAuthorization.cs -
            // once with the caller roster and once without - meant the surviving policy depended on call
            // order rather than on either author's intent. One registrar, carrying both requirements.
        });

        // AUTHENTICATION IS NOT AUTHORIZATION, AND THE POLICIES ABOVE ONLY DELIVER THE FIRST. Gateway's
        // own published contract declares a 403 on all thirty-nine of its /v1/datawindow/** operations
        // whose description reads "The projected gRPC method returned PermissionDenied. The token is
        // valid but does not carry the scope this operation requires" - a promise about THIS service,
        // because Gateway's 403 there is a PROJECTION of a status this service was never producing.
        // Until these policies existed nothing here read the scope claim, so C-03 and C-04 were both
        // reachable by any token addressed to this service and the published 403 was unreachable through
        // the whole chain.
        //
        // The mechanism, the reason the framework's own claim requirement cannot express it (the claim is
        // ONE value carrying a SPACE-DELIMITED set, which is exactly what Gateway sends), the reason the
        // two contracts get two independent scopes, and the reason it is duplicated per service rather
        // than shared are all recorded in Authorization/ScopeAuthorization.cs.
        return services.AddScopeAuthorization();
    }

    /// <summary>
    /// Registers the two outbound edges: the four persistence.v1 gRPC clients behind
    /// <see cref="PersistenceClient"/>, and the typed REST client for Security.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// PERSISTENCE IS gRPC AND SECURITY IS REST, AND THE ASYMMETRY IS THE CONTRACT'S. Contracts C-05
    /// through C-08 are action-oriented RPC surfaces with a structured error payload and a streamed
    /// retrieval, so the four generated clients are registered against
    /// <c>DataServices:Persistence:Address</c> through the gRPC client factory. Contracts C-01 and
    /// C-02 are plain HTTP so that stock bearer handlers self-configure, so Security is a TYPED
    /// HTTPCLIENT against <c>DataServices:Security:BaseAddress</c> and there is no generated Security
    /// stub to look for - its contract is <c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>.
    /// </para>
    /// <para>
    /// THE RESILIENCE HANDLER EXISTS BECAUSE AN IN-PROCESS CALL CANNOT FAIL IN TRANSIT AND A NETWORK
    /// CALL CAN. Both edges were in-process method calls in the legacy library, so the failure mode did
    /// not exist and there was nothing to configure; handling a failure mode the decomposition ITSELF
    /// creates is required BY the transition rather than layered on top of it. Without it the first
    /// transient fault would surface as a defect the legacy could not have had, which is a regression
    /// introduced by the refactor rather than a preserved behaviour.
    /// </para>
    /// <para>
    /// AND IT IS NOT A PERFORMANCE MEASURE. No latency, throughput or availability claim is made or
    /// implied by any value below: the repository publishes no service-level agreement, no latency
    /// budget, no throughput target and no availability commitment anywhere, so no performance
    /// objective may be asserted (AAP 0.1.2, 0.8.5). The settings come from
    /// <c>DataServices:Resilience</c> so they are visible and overridable per environment rather than
    /// buried in code, and each restates the library's own standard-handler default.
    /// </para>
    /// <para>
    /// OUTBOUND CALLS CARRY A TOKEN AND PROPAGATE THEIR CANCELLATION TOKEN, so the downstream edges are
    /// authenticated too and a caller that disconnects stops work rather than leaving it running.
    /// <see cref="PersistenceClient"/> obtains the credential through
    /// <see cref="IServiceTokenProvider"/>, which resolves to <see cref="SecurityClient"/> and contract
    /// C-01 - so no service other than Security mints anything. The legacy proxy pair
    /// <c>n_cst_threading*</c> / <c>n_cst_thread*</c> encoded thread affinity as a CONTRACT rather than
    /// as commentary; in .NET that becomes async request/response with a <c>CancellationToken</c>, and
    /// the client's members carry one on every operation rather than flattening the affinity away.
    /// </para>
    /// <para>
    /// THE SECURITY CHANNEL IS A NAMED CLIENT AND THE CLIENT OBJECT IS SCOPED, WHICH IS LOAD BEARING AND
    /// NOT STYLE. The generic typed-client overload registers the client type TRANSIENT, and that made
    /// two things false at once: the credential store the client owned as a field was EMPTY on every
    /// resolve, so the held-credential path was never reached across calls and this service asked Security
    /// to mint a fresh token for every outbound call; and the two interface registrations below each
    /// resolved their own client, so the claim that they are one object held for a single resolve and not
    /// for a request. Splitting the registration - the name owns the address, handler and resilience
    /// pipeline, a scoped registration owns the object, and a singleton owns the store - is what makes both
    /// statements true.
    /// </para>
    /// <para>
    /// THE CONSTRUCTOR IS NAMED RATHER THAN ACTIVATED, for a reason that survives that change.
    /// <see cref="SecurityClient"/> declares two constructors that BOTH accept an
    /// <see cref="HttpClient"/> plus container-resolvable parameters. An activator handed the
    /// <see cref="HttpClient"/> demands exactly one best match, and two equal matches make it throw
    /// "Multiple constructors accepting all given argument types have been found" on the first resolve -
    /// which is the first token request, i.e. the first authenticated call this service makes. Naming the
    /// constructor removes the ambiguity, and the one named is the widest overload so that both the
    /// determinism seam registered by <see cref="AddDataServicesDeterminismSeam"/> and the shared
    /// credential store are genuinely engaged rather than silently defaulted.
    /// </para>
    /// <para>
    /// THE TWO INTERFACE REGISTRATIONS ARE THE SAME OBJECT, NOT TWO. <see cref="SecurityClient"/>
    /// implements both <see cref="IServiceTokenProvider"/> (C-01) and
    /// <see cref="ICryptoServiceClient"/> (C-02), and both resolve THROUGH the one scoped client so that
    /// the held credential is shared rather than re-minted per interface -
    /// <c>SecurityCredentialCompositionTests</c> asserts it against this composition rather than leaving
    /// it as a claim. Under C-02 raw key material never crosses the wire from a caller - it passes an
    /// opaque reference that Security resolves on its own side - and nothing here holds, forwards or logs
    /// any.
    /// </para>
    /// <para>
    /// AND THE CHANNEL PRESENTS A CLIENT CERTIFICATE, WHICH IS WHAT MAKES ANY OF THE ABOVE REACHABLE.
    /// Contract C-01 authenticates <c>POST /v1/tokens</c> with mutual TLS and with nothing else, because
    /// a caller cannot present a bearer token in order to obtain its first bearer token. The identity is
    /// loaded once from <c>DataServices:Security:MutualTls</c> and attached by
    /// <see cref="CreateSecurityChannelHandler"/>; an unreadable or half-configured pair ends the host,
    /// and an unset pair is the documented "presents nothing" posture that the client reports by name at
    /// the first token request.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // THE TRUST ANCHOR EVERY CHANNEL BELOW VERIFIES ITS PEER AGAINST. Both upstreams terminate TLS
        // with certificates issued by the local authority docs/ARCHITECTURE.md 9.3.1 generates, which is
        // in no container's OS trust store - so without this, none of the five channels registered here
        // can complete a handshake. A singleton because the handler factories recycle handlers on a
        // schedule and the anchor must be read once rather than per rotation.
        services.TryAddSingleton(static serviceProvider => new InternalTlsTrust(
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value.InternalTls));

        // THE CLIENT IDENTITY THIS SERVICE PRESENTS, LOADED ONCE AND SHARED BY EVERY HANDLER ROTATION.
        // The options group existed before this line did, and nothing consumed it: the Security channel
        // was built with the trust anchor and no certificate, so `POST /v1/tokens` - which is protected
        // by mutual TLS and by nothing else - refused every request this service made, and with no
        // credential neither the four Persistence contracts nor the crypto contract were reachable. A
        // SINGLETON rather than a per-handler load because the client factory recycles its primary
        // handler on a schedule: reading the PEM pair inside that factory would open a fresh private-key
        // handle per rotation and never close one, and it is the single load that lets the eager resolve
        // after `Build()` turn an unreadable pair into a startup failure rather than a first-request one.
        services.TryAddSingleton(static serviceProvider => LoadSecurityClientIdentity(
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value.Security.MutualTls));

        // ONE CREDENTIAL STORE FOR THE WHOLE PROCESS, WHICH IS WHAT MAKES THE HELD CREDENTIAL REAL. A
        // typed HTTP client is registered TRANSIENT by the framework, so the store `SecurityClient` used
        // to own as a field was empty on every resolve - the "reuse a held token until it lapses" path
        // documented on its token accessor was never reached across calls in a running host, and this
        // service asked Security to mint a fresh token for every single outbound call. The store holds no
        // connection and no handler, only short-lived tokens keyed by audience and scope, so unlike the
        // client itself it is safe to keep for the life of the process.
        services.TryAddSingleton<ServiceTokenCache>();

        _ = services
            // WHERE a Security request goes is stated HERE and read nowhere else, which keeps this
            // composition root the single authority over the transport. The client itself reads the
            // bound setting for one purpose only - telling "not configured at all" apart from
            // "configured but registered without it" in a fail-fast diagnostic - and never to compose
            // an address.
            //
            // A NAMED CLIENT RATHER THAN A TYPED ONE, AND THE LIFETIME IS THE WHOLE REASON. The generic
            // overload registers the client type TRANSIENT, and with two interfaces forwarding to it
            // each forwarder resolved its own instance - so the remark below about "the same object, not
            // two" was false however the forwarders themselves were registered. Naming the client leaves
            // this registration owning the address, the handler and the resilience pipeline, and lets the
            // scoped registration further down own the object's lifetime, which is the only arrangement
            // in which both interfaces genuinely reach one client. The name is an explicit constant
            // rather than the framework's derived type name so nothing here depends on that convention.
            .AddHttpClient(SecurityClient.HttpClientName, static (serviceProvider, httpClient) => httpClient
                .BaseAddress = new Uri(
                    serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>()
                        .Value
                        .Security
                        .BaseAddress,
                    UriKind.Absolute))
            // HOW the peer is verified. Server-certificate validation is NARROWED here, never relaxed,
            // and never by a callback: a forged Security service would be a forged token issuer for the
            // whole system, so this handler pins the acceptable root to the anchor the deployment mounts
            // and rejects every other root for internal traffic. Chain building, name validation and
            // validity dates remain the platform's. With no anchor configured the platform's default
            // trust decision stands unmodified (constraint C-G).
            .ConfigurePrimaryHttpMessageHandler(CreateSecurityChannelHandler)
            .AddStandardResilienceHandler()
            // THE ONE EDGE WITH A GENUINE READ/WRITE SPLIT TO MAKE. Security is reached over REST, so its
            // methods are distinguishable: the verification-material fetch is a GET and is safely
            // retryable, while token issuance is a POST and is not retried - minting a second token is
            // not obviously harmful, but "not obviously harmful" is not a basis for replaying a
            // credential-issuing request whose outcome is unknown. The predicate expresses exactly that
            // distinction, so the configured attempt count still governs the GET.
            .Configure(static (resilience, serviceProvider) => ApplyResilience(
                resilience,
                serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>()
                    .Value
                    .Resilience
                    .Security));

        // THE CLIENT ITSELF IS SCOPED, AND THAT IS WHAT MAKES THE REMARK ABOVE TRUE. One registration of
        // the concrete type means the two interface forwarders below reach ONE object within a request
        // rather than each resolving its own - which is what "the same object, not two" has always
        // claimed and what a transient typed client made false however the forwarders were registered.
        // Scoped rather than singleton because the HttpClient handed to it is factory-managed and meant
        // to be short lived, and scoped rather than transient because the request is the unit the
        // credential is used within; it matches PersistenceClient's own lifetime for the same reason.
        //
        // THE CONSTRUCTOR IS NAMED RATHER THAN ACTIVATED. SecurityClient publishes two public
        // constructors that both accept an HttpClient plus container-resolvable parameters, so the
        // activator cannot choose between them; naming the widest one here also guarantees the
        // determinism seam and the shared credential store are genuinely engaged rather than silently
        // defaulted.
        services.TryAddScoped(static serviceProvider => new SecurityClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(SecurityClient.HttpClientName),
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>(),
            serviceProvider.GetRequiredService<ILogger<SecurityClient>>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<ServiceTokenCache>()));

        services.TryAddScoped<IServiceTokenProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<SecurityClient>());

        services.TryAddScoped<ICryptoServiceClient>(static serviceProvider =>
            serviceProvider.GetRequiredService<SecurityClient>());

        // C-05 QueryService, C-06 UpdateService, C-07 CommandService, C-08 TransactionService. Four
        // separate clients rather than one, because the contract is four services so that the update
        // surface - the one carrying optimistic-concurrency semantics - can version independently of
        // the retrieval surface (AAP 0.3.4).
        //
        // ============ ALL FOUR GET THE NON-RETRYING PIPELINE, AND THAT IS A CORRECTNESS DECISION ======
        // A transport failure does not tell a caller whether the server processed the request. Retrying
        // is therefore only safe for an operation that can be applied twice with the same result, and
        // NOT ONE RPC ON THESE FOUR CONTRACTS QUALIFIES - not even the ones that read:
        //
        //   * C-06 Update APPLIES ROWS. A retry after a failure that actually succeeded double-applies
        //     an insert, and the second attempt's updatewhereclause then finds the row already changed
        //     and reports a conflict for a change this caller itself made.
        //   * C-07 Exec runs an arbitrary statement, which is the same argument with nothing bounding it.
        //   * C-08 BeginSession takes a POOL REFERENCE. A retried begin leaks the first one, and the pool
        //     is reference-counted, so the leak is permanent for the process. Commit and Rollback are
        //     each once-only by definition.
        //   * C-05 LOOKS retryable and is not. Alongside the streaming read it carries the task
        //     lifecycle and the clause setters, and CreateQueryTask leaks a server-held task while
        //     SetWhereClause under SQL_MS_APPEND appends the clause A SECOND TIME - which produces a
        //     different statement, not a duplicate one.
        //
        // WHY AN OPERATION-SCOPED PREDICATE RATHER THAN MaxRetryAttempts = 0. Zeroing the attempt count
        // records the CONSEQUENCE and loses the reason: it cannot distinguish a read from an Update, it
        // cannot be relaxed for the one edge that does have a safe method, and it leaves nothing in the
        // code saying why. Clients/OutboundCallPolicy.cs classifies by operation and reads the
        // `grpc-status` a server-declared refusal arrives with - which an HTTP-level predicate cannot see
        // at all, because such a refusal arrives as HTTP 200 - so the four contracts here retry the
        // classified reads and idempotent teardowns and nothing else, and the configured attempt count
        // stays meaningful on the Security edge below. The rest of the pipeline - the total request
        // timeout and the circuit breaker - stays on all four, and that is the part the AAP added this
        // package for: an in-process call could not fail in transit and a network call can (AAP 0.5.3).
        // ==========================================================================================
        _ = services
            .AddGrpcClient<PersistenceQueryClient>(ConfigurePersistenceChannel)
            .ConfigurePrimaryHttpMessageHandler(CreateInternalGrpcHandler)
            .AddStandardResilienceHandler()
            .Configure(ConfigureNonRetryingPersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceUpdateClient>(ConfigurePersistenceChannel)
            .ConfigurePrimaryHttpMessageHandler(CreateInternalGrpcHandler)
            .AddStandardResilienceHandler()
            .Configure(ConfigureNonRetryingPersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceCommandClient>(ConfigurePersistenceChannel)
            .ConfigurePrimaryHttpMessageHandler(CreateInternalGrpcHandler)
            .AddStandardResilienceHandler()
            .Configure(ConfigureNonRetryingPersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceTransactionClient>(ConfigurePersistenceChannel)
            .ConfigurePrimaryHttpMessageHandler(CreateInternalGrpcHandler)
            .AddStandardResilienceHandler()
            .Configure(ConfigureNonRetryingPersistenceResilience);

        // THE OUTBOUND DEADLINES, DERIVED FROM THE SAME GROUP THE FOUR PIPELINES ABOVE ARE CONFIGURED
        // FROM. A gRPC call carrying no deadline lets Persistence keep working - and keep the query,
        // update, command or transaction handle behind that work alive - for as long as the transport
        // looks open, which includes every case where this caller has gone and the transport has not
        // noticed. Those handles are bounded per principal and globally, so an abandoned call that is
        // never told to stop consumes admission capacity a live caller then cannot get. A singleton
        // because it holds two durations and a clock and nothing else.
        services.TryAddSingleton(static serviceProvider =>
        {
            ClientResilienceOptions persistence = serviceProvider
                .GetRequiredService<IOptions<DataServicesOptions>>()
                .Value
                .Resilience
                .Persistence;

            return new OutboundDeadlines(
                persistence.RequestTimeout,
                persistence.StreamDeadline,
                serviceProvider.GetRequiredService<TimeProvider>());
        });

        // SCOPED, deliberately. The client holds no cross-request state of its own, and a scoped
        // lifetime is what keeps its logger scope and its resolved gRPC clients aligned with the call
        // that is using them.
        services.TryAddScoped<PersistenceClient>();

        return services;
    }

    /// <summary>
    /// Points a generated persistence.v1 client at the configured Persistence address.
    /// </summary>
    /// <param name="serviceProvider">The provider the bound options are read from.</param>
    /// <param name="grpcOptions">The client options being configured.</param>
    /// <remarks>
    /// A NAMED METHOD RATHER THAN FOUR COPIES OF A LAMBDA, so all four clients demonstrably read the
    /// SAME setting. Four separate lambdas would let one drift to a different key during a later edit,
    /// and the four are required to address one service.
    /// </remarks>
    private static void ConfigurePersistenceChannel(
        IServiceProvider serviceProvider,
        Grpc.Net.ClientFactory.GrpcClientFactoryOptions grpcOptions)
    {
        DataServicesOptions options =
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>().Value;

        grpcOptions.Address = new Uri(options.Persistence.Address, UriKind.Absolute);
    }

    /// <summary>
    /// Builds the primary handler for the four Persistence gRPC channels, with internal trust applied.
    /// </summary>
    /// <param name="serviceProvider">The provider the shared trust anchor is resolved from.</param>
    /// <returns>A handler that verifies its peer against the mounted anchor when one is configured.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ONE FACTORY FOR FOUR CHANNELS, for the same reason <see cref="ConfigurePersistenceChannel"/> is
    /// one method rather than four lambdas: all four demonstrably share the same trust decision, and a
    /// channel that quietly kept platform default trust could not connect to the documented topology at
    /// all.
    /// </para>
    /// <para>
    /// <c>EnableMultipleHttp2Connections</c> is set explicitly because supplying a primary handler
    /// replaces the one the gRPC client factory would otherwise build, and that one sets this property.
    /// Leaving it at its default would cap concurrent streams at the peer's advertised limit and queue
    /// calls behind it - a change to the transport that has nothing to do with trust.
    /// </para>
    /// </remarks>
    private static HttpMessageHandler CreateInternalGrpcHandler(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        SocketsHttpHandler handler = new() { EnableMultipleHttp2Connections = true };

        serviceProvider.GetRequiredService<InternalTlsTrust>().Apply(handler);

        return handler;
    }

    /// <summary>
    /// Builds the primary handler for the Security REST channel, with internal trust applied.
    /// </summary>
    /// <param name="serviceProvider">The provider the shared trust anchor is resolved from.</param>
    /// <returns>A handler that verifies Security against the mounted anchor when one is configured.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="serviceProvider"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SEPARATE FROM THE gRPC FACTORY, because this channel carries the client certificate as well as
    /// the anchor and the gRPC channels carry neither. Keeping them apart is what stops a later edit
    /// from attaching this service's client identity to a channel that has no use for it.
    /// </para>
    /// <para>
    /// AND THE CERTIFICATE IS THE HALF THAT WAS MISSING. Contract C-01 protects <c>POST /v1/tokens</c>
    /// with mutual TLS and with nothing else, because a caller cannot present a bearer token in order to
    /// obtain its first bearer token - so a channel carrying the anchor but no identity completes the
    /// handshake, is refused at the endpoint, and leaves this service with no credential for any of the
    /// four Persistence contracts or the crypto contract. The identity is resolved from the container
    /// rather than loaded here for the reason recorded on its registration: the factory recycles this
    /// handler on a schedule and the private key must be read once, not per rotation.
    /// </para>
    /// <para>
    /// An EMPTY collection is the unconfigured pair, which is a legitimate deployment state rather than a
    /// fault - it presents no identity and requests no token. The property is left alone in that case
    /// rather than assigned an empty collection, because writing it would say less than not writing it.
    /// </para>
    /// </remarks>
    private static HttpMessageHandler CreateSecurityChannelHandler(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        SocketsHttpHandler handler = new();

        serviceProvider.GetRequiredService<InternalTlsTrust>().Apply(handler);

        X509Certificate2Collection identity =
            serviceProvider.GetRequiredService<X509Certificate2Collection>();

        if (identity.Count > 0)
        {
            handler.SslOptions.ClientCertificates = identity;
        }

        return handler;
    }

    /// <summary>
    /// Loads the PEM client-certificate pair this service presents on the token-issuance edge, or an
    /// empty collection when the deployment configures none.
    /// </summary>
    /// <param name="mutualTls">The bound <c>DataServices:Security:MutualTls</c> group.</param>
    /// <returns>
    /// The loaded identity, or an empty collection when neither path is configured.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="mutualTls"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The group is half configured, or the material it names cannot be read as a PEM certificate and
    /// key.
    /// </exception>
    /// <remarks>
    /// <para>
    /// FAIL CLOSED ON HALF-CONFIGURED AND ON UNREADABLE, AND ONLY THOSE TWO. A certificate cannot
    /// complete a handshake without its key and a key has nothing to present without its certificate, so
    /// half a pair is unusable rather than merely weaker; and material that names a file the process
    /// cannot read is a deployment that intended to authenticate and cannot. Both end the host, which is
    /// the managed form of the legacy's own posture [ws_objects/pfw.pbl.src/pfw.sra:L143] - a structural
    /// fault terminates rather than degrading. Neither path is echoed into either message: a path is not
    /// itself a credential, but it names where one is mounted, and a startup log is exactly the wrong
    /// place to publish that. The configuration key is what an operator needs and all they are given.
    /// </para>
    /// <para>
    /// NEITHER IS AN EMPTY PAIR. That is the documented "this deployment presents no client certificate
    /// and requests no token" state carried by the options group itself, and refusing to start on it
    /// would make the shipped defaults unrunnable. The consequence is instead surfaced twice where it is
    /// actionable: a named startup warning, and a named diagnostic from <see cref="SecurityClient"/> at
    /// the first token request instead of a request that could only ever be refused.
    /// </para>
    /// </remarks>
    private static X509Certificate2Collection LoadSecurityClientIdentity(
        MutualTlsClientOptions mutualTls)
    {
        ArgumentNullException.ThrowIfNull(mutualTls);

        if (!mutualTls.IsConfigured)
        {
            return [];
        }

        string certificatePath = mutualTls.CertificatePath.Trim();
        string certificateKeyPath = mutualTls.CertificateKeyPath.Trim();

        string certificateKey = string.Concat(
            DataServicesOptions.SectionName,
            ":",
            nameof(DataServicesOptions.Security),
            ":",
            nameof(SecurityClientOptions.MutualTls),
            ":",
            nameof(MutualTlsClientOptions.CertificatePath));

        string privateKeyKey = string.Concat(
            DataServicesOptions.SectionName,
            ":",
            nameof(DataServicesOptions.Security),
            ":",
            nameof(SecurityClientOptions.MutualTls),
            ":",
            nameof(MutualTlsClientOptions.CertificateKeyPath));

        if (certificatePath.Length == 0 || certificateKeyPath.Length == 0)
        {
            throw new InvalidOperationException(
                $"'{certificateKey}' and '{privateKeyKey}' must be configured together or not at all. This "
                    + "deployment is half configured: a certificate cannot complete a TLS handshake without "
                    + "its key and a key has nothing to present without its certificate, so half a client "
                    + "identity is unusable rather than merely weaker. Neither path is quoted here, because "
                    + "a startup log must not record where key material is mounted.");
        }

        try
        {
            // Constructed straight into the collection so the certificate has no owning local: its
            // lifetime is the returned collection's, which the container holds for the life of the
            // process and hands to every rotation of the primary handler.
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
                $"The client certificate named by '{certificateKey}' and '{privateKeyKey}' could not be "
                    + "loaded, so this host cannot authenticate to the token-issuance edge and will not "
                    + "start. Check that both files exist, that the process can read them, and that each "
                    + "is PEM encoded - the certificate in the first key and its private key in the "
                    + "second. Neither path is reproduced here, because a startup log is the wrong place "
                    + "to publish where a private key is mounted.",
                failure);
        }
    }

    /// <summary>
    /// Applies the configured Persistence resilience settings to a standard handler.
    /// </summary>
    /// <param name="resilience">The handler options being configured.</param>
    /// <param name="serviceProvider">The provider the bound options are read from.</param>
    private static void ConfigureNonRetryingPersistenceResilience(
        HttpStandardResilienceOptions resilience,
        IServiceProvider serviceProvider) => ApplyResilience(
            resilience,
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>()
                .Value
                .Resilience
                .Persistence);

    /// <summary>
    /// Copies one configured resilience group onto a standard handler.
    /// </summary>
    /// <param name="resilience">The handler options being configured.</param>
    /// <param name="configured">The configured group.</param>
    /// <remarks>
    /// <para>
    /// ONE MAPPING, USED BY BOTH EDGES. The two edges are configured from two SEPARATE option groups -
    /// <c>DataServices:Resilience:Persistence</c> and <c>DataServices:Resilience:Security</c> - so they
    /// can be tuned independently, but the projection from a group onto the handler is identical and is
    /// written once so the two cannot diverge in shape.
    /// </para>
    /// <para>
    /// THE RETRY PREDICATE IS THE CALLER'S DECISION AND IS NOT DEFAULTED. Both edges share the timeout
    /// and circuit-breaker shape, and they differ on exactly one axis: whether an unsafe method may be
    /// replayed. Making that a required argument means a future edge cannot acquire retries on a mutating
    /// contract by omission - it has to say so.
    /// </para>
    /// <para>
    /// THE ATTEMPT TIMEOUT IS LEFT AT THE LIBRARY DEFAULT ON PURPOSE. The handler validates that the
    /// circuit-breaker sampling duration is at least double the attempt timeout and that the total
    /// request timeout is not shorter than a single attempt; overriding one of the three from
    /// configuration while inventing values for the others is how a deployment ends up failing that
    /// validation at startup for a reason nobody stated. The configured group names the four values the
    /// AAP calls for and leaves the rest of the pipeline as published.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Builds the message handler the Security typed client sends through, attaching this service's
    /// client certificate when the deployment configured one.
    /// </summary>
    /// <param name="serviceProvider">The provider the typed client was resolved from.</param>
    /// <returns>The primary handler for the Security typed client.</returns>
    /// <exception cref="InvalidOperationException">
    /// The configured certificate pair cannot be loaded or is unusable as a client identity.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS EXISTS BECAUSE A DECLARED SETTING THAT NOTHING READS IS WORSE THAN NO SETTING AT ALL.
    /// <c>DataServices:Security:MutualTls</c> is bound and validated as a pair, and C-01 publishes
    /// <c>mutualTls</c> as one of the two credentials it accepts on <c>POST /v1/tokens</c>. Without this
    /// registration the typed client carried only a base address and a resilience pipeline, so a
    /// deployment that mounted a certificate pair and read the settings document would believe it had
    /// configured a credential that was never presented - and the refusal would arrive as a
    /// <c>401</c> from Security with nothing in it to suggest the certificate had been dropped on this
    /// side. Consuming the setting is what makes the published alternative reachable.
    /// </para>
    /// <para>
    /// NORMAL SERVER-CERTIFICATE VALIDATION IS RETAINED, and no callback is installed. The one thing a
    /// client-certificate configuration must not do is quietly become a trust-everything switch: no
    /// <c>RemoteCertificateValidationCallback</c>, no <c>DangerousAcceptAnyServerCertificate</c>, and no
    /// revocation relaxation appears here or anywhere in this file, so the platform's own chain
    /// validation applies exactly as it would without a client identity. The legacy tree contains the
    /// opposite pattern - a test window that disables server- and host-certificate validation outright
    /// with an inline note saying so - and reproducing that here would import a weakness from a surface
    /// this refactor does not carry.
    /// </para>
    /// <para>
    /// FAIL FAST, AND ON THE FIRST RESOLVE RATHER THAN ON THE FIRST HANDSHAKE. A handler factory runs
    /// when the typed client is first resolved, which for this client is the first token request. A pair
    /// of paths that names a missing file, an unreadable file, a mismatched key or material the platform
    /// cannot use as a client identity is therefore reported as a configuration fault with the
    /// configuration KEYS named, rather than as a TLS handshake failure whose message names neither. The
    /// paths themselves are never quoted: a path is not a credential, but it names where one is mounted,
    /// and a startup log is the wrong place to publish that.
    /// </para>
    /// <para>
    /// WHEN NO PAIR IS CONFIGURED THIS RETURNS THE ORDINARY HANDLER UNCHANGED - it is not an error, and
    /// it is a supported topology: wherever TLS is terminated ahead of Security by a reverse proxy or a
    /// mesh sidecar, the client certificate never reaches the application, so the credential is the
    /// <c>Basic</c> one this client writes onto the request instead. A deployment configuring NEITHER is
    /// refused at startup by the options validator, so this method never has to represent that case.
    /// </para>
    /// </remarks>
    private static HttpMessageHandler ConfigureSecurityTransport(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        MutualTlsClientOptions mutualTls = serviceProvider
            .GetRequiredService<IOptions<DataServicesOptions>>()
            .Value
            .Security
            .MutualTls;

        SocketsHttpHandler handler = new();

        if (!mutualTls.IsConfigured)
        {
            return handler;
        }

        handler.SslOptions.ClientCertificates =
        [
            LoadClientCertificate(mutualTls.CertificatePath, mutualTls.CertificateKeyPath),
        ];

        return handler;
    }

    /// <summary>
    /// Loads a PEM certificate and its PEM private key into one certificate usable as a client identity.
    /// </summary>
    /// <param name="certificatePath">Path to the PEM certificate.</param>
    /// <param name="keyPath">Path to the PEM private key.</param>
    /// <returns>The loaded certificate, carrying its private key.</returns>
    /// <exception cref="InvalidOperationException">The pair cannot be loaded or is unusable.</exception>
    /// <remarks>
    /// <para>
    /// THE PKCS#12 ROUND TRIP IS NOT REDUNDANT. A certificate created from PEM files carries an
    /// ephemeral key, and on Windows the TLS stack cannot use such a key for client authentication - the
    /// handshake fails with an error that names neither the certificate nor the cause. Exporting to
    /// PKCS#12 and re-importing produces a certificate whose key the platform will use, and it is
    /// harmless where it was not required. It is done unconditionally rather than under an
    /// operating-system test so that a deployment behaves the same way everywhere and this path is
    /// exercised by every run rather than only by the platform that needs it.
    /// </para>
    /// <para>
    /// EVERY FAILURE MODE IS TRANSLATED, AND NONE OF THEM QUOTES A PATH. A missing or unreadable file,
    /// material that is not PEM, a key that does not match the certificate and a key algorithm the
    /// platform will not accept all arrive as different exception types from three different APIs; each
    /// becomes one <see cref="InvalidOperationException"/> naming the two configuration keys and
    /// carrying the original as its inner exception for a diagnostic log. The inner exception is safe to
    /// carry here precisely because it is the platform's own message about material, not a message this
    /// code composed from a path.
    /// </para>
    /// </remarks>
    private static X509Certificate2 LoadClientCertificate(string certificatePath, string keyPath)
    {
        try
        {
            using X509Certificate2 fromPem =
                X509Certificate2.CreateFromPemFile(certificatePath, keyPath);

            return X509CertificateLoader.LoadPkcs12(
                fromPem.Export(X509ContentType.Pkcs12),
                password: null,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or ArgumentException)
        {
            throw new InvalidOperationException(
                "The client certificate configured for the token-issuance edge could not be loaded as a "
                + "usable client identity. Check that 'DataServices:Security:MutualTls:CertificatePath' "
                + "and 'DataServices:Security:MutualTls:CertificateKeyPath' both name readable "
                + "PEM-encoded files, that the key belongs to the certificate, and that its algorithm is "
                + "one this platform accepts for client authentication. Neither path is quoted here, "
                + "because a startup log must not record where key material is mounted.",
                exception);
        }
    }

    private static void ApplyResilience(
        HttpStandardResilienceOptions resilience,
        ClientResilienceOptions configured)
    {
        resilience.Retry.MaxRetryAttempts = configured.MaxRetryAttempts;
        resilience.Retry.Delay = configured.RetryBaseDelay;

        // THE SAFETY DECISION LIVES IN THE PREDICATE BELOW AND NOWHERE ELSE, WHICH IS A CORRECTION.
        //
        // A transport failure does not reveal whether the server processed the request, so replaying a
        // non-idempotent operation can apply it twice, and there is no end-to-end idempotency key or
        // request deduplication anywhere in this system. That reasoning is unchanged; what changed is
        // where it is enforced. This method used to call `Retry.DisableForUnsafeHttpMethods()` here and
        // then assign `Retry.ShouldHandle` a few lines below - and that assignment REPLACES whatever the
        // verb gate installed, so the gate decided nothing at all while reading as though it were the
        // safety mechanism. Two ways of expressing one decision, with the visible one inert, is worse
        // than either alone.
        //
        // IT IS ALSO THE WEAKER OF THE TWO ON THREE OF THE FOUR EDGES. gRPC transports every call as an
        // HTTP POST, so on the four Persistence contracts a verb gate is an all-or-nothing disable that
        // cannot distinguish a read from an Update - and it cannot read a `grpc-status` either, which is
        // the half of the problem that silently costs availability. Clients/OutboundCallPolicy.cs decides
        // from the operation first and the gRPC status second, and it admits the RFC 9110 safe methods, so
        // it reproduces the verb gate's decision exactly on the Security REST edge - the one edge where a
        // verb genuinely distinguishes a read - while remaining correct on the three where it does not.
        resilience.CircuitBreaker.FailureRatio = configured.CircuitBreakerFailureRatio;
        resilience.CircuitBreaker.MinimumThroughput = configured.CircuitBreakerMinimumThroughput;
        resilience.CircuitBreaker.SamplingDuration = configured.CircuitBreakerSamplingDuration;
        resilience.CircuitBreaker.BreakDuration = configured.CircuitBreakerBreakDuration;
        resilience.TotalRequestTimeout.Timeout = configured.RequestTimeout;

        // THE BACKOFF SHAPE IS WRITTEN DOWN RATHER THAN INHERITED. Both values match the package's
        // defaults today, and that is precisely why they are stated: "bounded backoff with jitter" is a
        // requirement of this policy, and a requirement that holds only because a dependency's default
        // happens to satisfy it is not being enforced by anything.
        resilience.Retry.BackoffType = DelayBackoffType.Exponential;
        resilience.Retry.UseJitter = true;

        // THE TWO PREDICATES ARE THE SUBSTANTIVE PART, because the stock ones cannot see this edge at
        // all. A gRPC call the server refused arrives as HTTP 200 with the refusal in `grpc-status`, so
        // the stock retry predicate never retried a server-declared Unavailable - while it happily
        // REPLAYED a transport fault on an Update, an Exec or a Commit, none of which is idempotent.
        // Clients/OutboundCallPolicy.cs decides from the operation first and the gRPC status second; the
        // circuit-breaker predicate additionally distinguishes an upstream that cannot serve from one
        // that is deliberately refusing, so a contested update or an exhausted quota cannot trip the
        // breaker and take the read path down with it.
        resilience.Retry.ShouldHandle = OutboundCallPolicy.ShouldRetryAsync;
        resilience.CircuitBreaker.ShouldHandle = OutboundCallPolicy.ShouldBreakAsync;
    }


    /// <summary>
    /// Registers the domain, expression and model collaborators the two published gRPC surfaces are
    /// composed from.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// WHAT IS REGISTERED HERE IS EXACTLY WHAT HAS A CONTAINER LIFETIME, AND NOTHING ELSE. The two
    /// session stores, the two duplex-channel routers, the trace broker, the event relay, the page
    /// resolver, the pinyin matcher and the three host-binding seams are process-wide. Everything else
    /// this service runs on is PER-DATAWINDOW or PER-SESSION state that the service layer creates and
    /// owns, and the four paragraphs below record each omission so a future reader can tell a
    /// deliberate absence from a forgotten line.
    /// </para>
    /// <para>
    /// NO EVENT BROKER SINGLETON, AND THAT IS CONTRACT. <c>se_cst_dw.sru:L562</c> creates the broker as
    /// an instance member of ONE CONTROL - <c>this.eventful = create eventful</c> - before it creates
    /// any of the five attached services, and <c>Domain/DataWindowEventChain.cs</c> reproduces that by
    /// owning its own broker per chain. A container singleton would make twelve topics
    /// [<c>:L47-L76</c>] shared across every concurrent DataWindow in the process, so a subscription
    /// made for one would receive another's dispatches. The broker is therefore deliberately absent
    /// from this method.
    /// </para>
    /// <para>
    /// NO POST-DRAIN LOOP HERE EITHER, AND THE OWNERSHIP IS SETTLED RATHER THAN ASSUMED. The legacy
    /// <c>Post _of_PostAcceptText()</c> [<c>se_cst_dw.sru:L387-L393</c>, body at <c>:L537-L558</c>]
    /// relies on the WIN32 MESSAGE PUMP, which a headless Linux container does not have, so the pump
    /// becomes an explicit drain. <c>Domain/ValidationSession.cs</c> OWNS BOTH HALVES -
    /// <c>TryQueueDeferredAccept</c> queues and <c>DrainDeferredAccept</c> runs - and
    /// <c>DataWindowEventChain.DrainDeferredAccept</c> delegates straight to the session, whose own
    /// decision record states that it introduces NO SECOND MECHANISM so that the continuation and the
    /// four cross-event fields it reads [<c>:L89-L96</c>] stay in one place. The broker's separate
    /// queue of posted dispatches is drained by <c>EventBroker.DrainPostedContinuations</c> on the
    /// per-chain broker above. A host-level drain loop would be a THIRD mechanism racing both.
    /// </para>
    /// <para>
    /// NO EXPRESSION EVALUATOR AND NO EXPRESSION ENGINE. <c>DataWindowExpressionEvaluator</c> binds to
    /// a <c>DataWindowServiceHost</c>, and a host is per-session state the C-04 service layer creates;
    /// <c>ColumnExpressionEngine</c> is per-DataWindow for the same reason. Registering either would
    /// mean inventing a host lifetime the contract has not defined. What IS registered is the PAGE
    /// RESOLVER they are constructed with, which is what makes that construction correct by default.
    /// </para>
    /// <para>
    /// NO FOUR HEADLESS MODELS AS SINGLETONS. <c>ContextMenuModel</c>, <c>RowSelectService</c>,
    /// <c>ColumnSortModel</c> and <c>DropDownSearchModel</c> all derive from
    /// <c>DataWindowServiceBase</c> and are USELESS UNTIL ATTACHED to a host - the legacy has them as
    /// five instance members of one control [<c>se_cst_dw.sru:L80-L84</c>] - so four container
    /// singletons would be four permanently unattached objects, and a state written by one caller's
    /// apply would be read by another caller's get. They arrive per DataWindow through
    /// <see cref="IDataWindowModelSetProvider"/> instead, and the retention is the point: an apply
    /// followed by a get MUST observe what was applied.
    /// </para>
    /// <para>
    /// AND NOTHING AT ALL FOR THE DEFERRED RENDERING HALVES (constraint C-D). No window positioning, no
    /// DPI conversion, no font measurement, no input-method service and no popup-menu renderer is
    /// registered, because those are DesignSystem's and DesignSystem is deferred. The three services
    /// that were split - ColumnSort, ContextMenu and DropDownSearch - ship their headless halves and
    /// surface the rendering halves as data over the contract, named as the <c>/v1/design/**</c>
    /// extension point (AAP 0.3.5, 0.4.4).
    /// </para>
    /// <para>
    /// ORDERING FACT 1 IS NOT OWNED BY THIS CONTAINER AND MUST NOT BE. <c>se_cst_dw.sru:L570-L574</c>
    /// CREATES the five attached services in the order ContextMenu, RowSelect, ColumnSort,
    /// DropDownSearch, ColumnExp, and <c>:L576-L580</c> then calls <c>OnInit</c> in a DIFFERENT order -
    /// ContextMenu, RowSelect, <b>DropDownSearch, ColumnSort</b>, ColumnExp, with positions three and
    /// four swapped. BOTH SEQUENCES ARE OBSERVABLE and neither is a tidy-up candidate. A DI container
    /// resolves in dependency order and would silently impose a THIRD order, so the sequence is owned
    /// by <c>Domain/DataWindowEventChain.cs</c>, which reproduces both from its constructor through
    /// <c>IDataWindowAttachedServiceFactory</c>'s five members. This method registers no attached
    /// service, so there is no order here for a container to get wrong.
    /// </para>
    /// <para>
    /// ORDERING FACT 3 LIKEWISE BELONGS ELSEWHERE. The event gate's <c>EID_ROWFOCUSCHANGE</c> = 1,
    /// <c>EID_ITEMFOCUSCHANGE</c> = 2 and <c>EID_ITEMCHANGE</c> = 4 mask [<c>:L41-L43</c>] is tested
    /// through Kernel's <c>Bits.BitTest</c>, whose fixed-width <c>uint</c> and <c>ushort</c> overloads
    /// exist because PowerBuilder's <c>ulong</c> is 32-bit and its <c>uint</c> is 16-bit. Widening to
    /// C#'s same-named but wider types would change which bits survive a test. <c>Domain/EventGate.cs</c>
    /// owns that, and nothing here converts, widens or re-declares a gate value.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesDomain(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The four cross-event state fields [se_cst_dw.sru:L89-L96] have nowhere to live on a stateless
        // boundary, so the store that holds them is process-wide and every session is reached by
        // correlation identifier. Registered through an EXPLICIT FACTORY because its constructors are
        // internal, and the container's activator only considers PUBLIC constructors - a type
        // registration would fail at first resolve with "a suitable constructor could not be found".
        // The localization facade is passed because the validation-error path builds a localized
        // message [:L355, :L357], and the clock because idle expiry is a determinism seam.
        services.TryAddSingleton(static serviceProvider => new ValidationSessionRegistry(
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>(),
            serviceProvider.GetRequiredService<I18n>(),
            serviceProvider.GetRequiredService<TimeProvider>()));

        // The trace broker is registered ONCE and reached under two names, so C-04's TraceChannel and
        // the expression sessions that EMIT into it share one instance. Registering the interface with
        // its own factory would create a second broker, and half the trace records would then be
        // delivered to subscribers of the other one.
        services.TryAddSingleton<ExpressionTraceBroker>();
        services.TryAddSingleton<IExpressionTraceSink>(static serviceProvider =>
            serviceProvider.GetRequiredService<ExpressionTraceBroker>());

        // The session scope that REPLACES `foreignvardata.expsvc` [n_cst_dwsvc_columnexp.sru:L80-L83].
        // That field is a LIVE OBJECT POINTER to another DataWindow's expression service and cannot be
        // serialized, so cross-DataWindow variables resolve only while both DataWindows are co-resident
        // in one session here, and a reference spanning sessions is BLOCKED with a defined error rather
        // than silently answered wrongly (AAP 0.6.2.3). An explicit factory names the four-argument
        // public constructor so the trace sink is genuinely supplied rather than defaulted away.
        services.TryAddSingleton(static serviceProvider => new ExpressionSessionRegistry(
            serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>(),
            serviceProvider.GetRequiredService<TimeProvider>(),
            serviceProvider.GetRequiredService<IExpressionTraceSink>(),
            serviceProvider.GetRequiredService<ILogger<ExpressionSessionRegistry>>()));

        // C-04's INVERTED macro channel. The legacy expects the APPLICATION to implement the macro
        // switch [se_cst_dw.sru:L14, docs/n_cst_dwsvc_columnexp.md], so across a boundary DataServices
        // must call BACK into its client while the inbound calculation is still open - and the router is
        // what holds the per-session channel that call travels down. Process-wide because a channel
        // opened by one call is used by another.
        services.TryAddSingleton<MacroInvocationRouter>();

        // The relay for the three column-expression notifications, and its typed logger wrapper. The
        // wrapper exists so the relay can take a logger without taking a dependency on a generic
        // logger type it does not otherwise need; both are process-wide for the same reason the router
        // is.
        services.TryAddSingleton<ColumnExpressionEventRelayLogger>();
        services.TryAddSingleton<ColumnExpressionEventRelay>();

        // THE SINGLE GENUINE PARITY RISK IN THE IN-SCOPE SET, REGISTERED IN ITS HONEST STATE.
        // `pfwPinyinFirstLetterLike`'s lookup table exists ONLY inside the closed pfw.dll and its flag
        // semantics are undocumented - the sole call site passes a flag value whose meaning is recorded
        // nowhere in the repository [n_cst_dwsvc_dropdownsearch.sru:L323]. Bit-exact parity therefore
        // requires characterizing both from the behavioural oracle, which has not been exercised, so
        // the shipped configuration is the one that REPORTS BLOCKED: every match attempt answers
        // Unavailable with a reason naming the seam. That is the reportable outcome the parity mandate
        // asks for in place of an approximation, because an approximation would return subtly different
        // result sets - a regression that looks like correct behaviour (AAP 0.6.5, R1). Substituting a
        // characterized matcher is a registration change here and nothing else.
        services.TryAddSingleton(PinyinFirstLetterMatcher.Blocked);

        // The `for page` resolver, STATED ONCE and turned into the one resolver that expresses it by a
        // factory that lives beside the three implementations. The CLASS default refuses `for page`
        // rather than widening it to every row, which is the right default for an evaluator built by
        // code that has stated nothing about pagination - and the wrong thing for a configured, running
        // service to be silently using. The deployed default is WholeBuffer, an explicit statement that
        // the sole evidenced surface is unpaginated: the only `for page` expression in the repository is
        // `sum(salary for page)` [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L27] and that DataWindow
        // declares no page-break band. The options validator rejects every other combination on start,
        // so this call cannot pick a resolver from a mistyped setting. A SINGLETON because all three
        // resolvers are stateless.
        services.TryAddSingleton<IExpressionPageResolver>(static serviceProvider =>
        {
            ColumnExpressionOptions columnExpression = serviceProvider
                .GetRequiredService<IOptions<DataServicesOptions>>()
                .Value
                .ColumnExpression;

            return ExpressionPageResolverFactory.Create(
                columnExpression.PageResolution,
                columnExpression.PageRowsPerPage);
        });

        // ------------------------------------------------------------------------------------------
        // THE THREE HOST-BINDING SEAMS - PROVISIONED
        //
        // All three answer the same question - "which concrete DataWindow does this handle name?" - and
        // all three are REQUIRED dependencies of the published gRPC services, so a service composed
        // without them is structurally faulty and the startup gate keeps that fail-fast.
        //
        // WHAT THESE REPLACED, AND WHY THE REPLACEMENT WAS REQUIRED. An earlier revision registered three
        // `Unbound*` implementations that bound no handle and returned null, defended on the ground that
        // materialising a DataWindow needs the DesignSystem ancestry `se_cst_dw` inherits from
        // `se_cst_datawindow` [se_cst_dw.sru:L4, :L10] and that constraint C-D forbids implementing a
        // deferred service even partially. The inheritance fact is true of the LEGACY graph; the
        // conclusion is not, and the AAP contradicts it in two places:
        //
        //   * AAP 0.2.1.3 Correction 3 resolves that exact edge by instructing DataServices to "define
        //     its own abstract host contract carrying only the members se_cst_dw actually consumes from
        //     its parent, IMPLEMENT AGAINST THAT, and record se_cst_datawindow as REFERENCE-only". The
        //     contract is Domain/DataWindowServiceHost.cs; the implementation half was what was missing.
        //   * AAP 0.3.5 splits every UI capability into "a headless half that SHIPS IN DATASERVICES and a
        //     rendering half" that is deferred. A row and column model with buffers, item statuses,
        //     selection and sort is the headless half by definition.
        //
        // The cost of the previous shape was not narrow: EIGHT of C-03's headless-model operations and
        // the WHOLE of C-04 - session open, both inverted streams - were permanently unreachable, and
        // the published surface answered a valid handle as though it named nothing.
        //
        // THE DEFERRED BOUNDARY IS STILL RESPECTED, AND OBSERVABLY SO. Nothing under Domain/ computes,
        // stores or answers a coordinate, a size, a colour, a font, a DPI conversion or a redraw; see
        // the header of Domain/HeadlessDataWindowHost.cs, which states how a reader checks that by
        // reading rather than by auditing call sites. Rendering stays behind `/v1/design/**`.
        //
        // THE NEGATIVE IS STILL REACHABLE. A handle no definition in Domain/DataWindowCatalogue.cs
        // carries still resolves to null, and the services still turn that into
        // `RetCode.E_INVALID_HANDLE` for a model or chain request and a failed open for an expression
        // session. That is published contract, and it is now reached by the inputs that earn it - an
        // unknown name - rather than by every input.
        //
        // REGISTERED WITH TryAdd SO SUBSTITUTION STILL NEEDS NO EDIT HERE. A deployment that materialises
        // DataWindows another way registers its own factory before this method runs, or replaces the
        // descriptor in a test host, and the published surface serves handles with no change to this file.
        // ------------------------------------------------------------------------------------------

        // The catalogue and the host factory are SINGLETONS because both RETAIN: a definition registered
        // once must be visible to every later resolve, and a host retained per name is what keeps an
        // expression session's variables alive across two resolves of the same handle.
        services.TryAddSingleton<DataWindowCatalogue>();
        services.TryAddSingleton<HeadlessDataWindowHostFactory>();
        services.TryAddSingleton<HeadlessDataWindowModelSetProvider>();

        services.TryAddSingleton<IDataWindowHostFactory>(static serviceProvider =>
            serviceProvider.GetRequiredService<HeadlessDataWindowHostFactory>());

        services.TryAddSingleton<IDataWindowModelSetProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<HeadlessDataWindowModelSetProvider>());

        services.TryAddSingleton<IDataWindowEventChainFactory>(static serviceProvider =>
            new HeadlessDataWindowEventChainFactory(
                serviceProvider.GetRequiredService<HeadlessDataWindowModelSetProvider>()));

        // THE UPDATE DESCRIPTOR SEAM, AND ITS ABSENCE IS NOT A HARMLESS DEFAULT. C-03's update path
        // treats a null descriptor as the single-table fallback - name the data object and let the
        // carrier's own definition govern - which is CORRECT for a definition that declares no update
        // table and INDISTINGUISHABLE from a deployment that never bound the seam. Unbound, therefore,
        // every update runs with whatever predicate the carrier defaulted to rather than the six-column
        // one the evidenced definition declares [dw_sqlite.srd:L8-L14], reports success, and no test of
        // the update path would notice.
        services.TryAddSingleton<IDataWindowUpdateContractProvider>(static serviceProvider =>
            new HeadlessDataWindowUpdateContractProvider(
                serviceProvider.GetRequiredService<DataWindowCatalogue>()));

        return services;
    }

    /// <summary>
    /// Registers the published surface: the gRPC server, the health-check service, the problem-details
    /// writer and the OpenAPI document.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>AddGrpc</c> IS WHAT MAKES C-03 AND C-04 SERVABLE. gRPC is this service's PRIMARY transport
    /// because its legacy interface is a 22-event ordered chain with a tri-valued veto, a
    /// <c>ref string</c> out-parameter and an <c>any</c> return over a string array - a shape JSON over
    /// REST carries neither the ordering nor the typed veto of (AAP 0.1.5). No interceptor is added
    /// here: authorization is declared per service at the mapping site, which is where a reader looks
    /// for it, and an interceptor would put the same requirement somewhere less visible.
    /// </para>
    /// <para>
    /// NO gRPC SERVER REFLECTION (AAP 0.5.3). It is a convenience for ad-hoc command-line tooling, and
    /// it would publish this service's whole method surface to anything that can reach the port.
    /// </para>
    /// <para>
    /// NO HEALTH-CHECK PACKAGE. Health-check registration ships inside the
    /// <c>Microsoft.AspNetCore.App</c> shared framework, so <c>/health</c> needs no package reference
    /// and none is declared. <c>Endpoints/HealthEndpoints.cs</c> resolves the service OPTIONALLY and
    /// answers a truthful Healthy when nothing is registered, so this registration adds the aggregation
    /// point without turning a probe into a startup gate. NO COMPONENT CHECK IS ADDED for the two
    /// upstreams, deliberately: C-10 gives the AGGREGATION to Gateway, and a leaf service that
    /// reported itself unready because an upstream was slow would make compose's
    /// <c>depends_on: condition: service_healthy</c> chain oscillate rather than settle.
    /// </para>
    /// <para>
    /// ONE COMPONENT CHECK IS ADDED, AND IT OPENS NO CHANNEL. <c>AddDataServicesHealthChecks</c>
    /// registers the credential precondition of the token bootstrap: every outward call this service
    /// makes carries a bearer token, the only way to obtain one is the issuance edge, and contract C-01
    /// protects that edge with mutual TLS and nothing else. A deployment that mounts no client
    /// certificate is a legitimate startup state - and one in which this service can serve nothing - so
    /// it must not report READY. The check reads bound configuration only: no token is minted, no
    /// handshake is attempted and no upstream is probed, so it is compatible with the paragraph above
    /// rather than an exception to it.
    /// </para>
    /// <para>
    /// PROBLEM DETAILS so that the framework's own challenge and every error response answer
    /// <c>application/problem+json</c>, which is the error shape the authored contracts publish - and
    /// the shape the REST projection's own 409 conflict body extends.
    /// </para>
    /// <para>
    /// NO INTERACTIVE DOCUMENT UI (AAP 0.5.3). <c>AddOpenApi</c> produces the document; Scalar and
    /// Swashbuckle would each add a package and a browsable surface this refactor was not asked for.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesPublishedSurface(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ⚠ LOAD BEARING, AND IT IS ABOUT THE SHARED PORT. Left at its default, the gRPC hosting layer maps
        // a CATCH-ALL route of the shape /{service}/{method} so that a call naming a service this server
        // does not host answers the gRPC UNIMPLEMENTED status instead of falling through. That route is two
        // parameter segments wide, so it matches ANY two-segment path - including this service's own REST
        // routes /v1/ping and /health - and it answers them from the gRPC handler. The observable damage is
        // precise and it is an error-contract defect: a request to a REST route is answered 404 by the gRPC
        // handler with a gRPC content type, so it carries neither the problem body those routes declare nor
        // the 405 that a rejected method on an existing route should produce. Since the port carries BOTH
        // protocols by design, the REST surface's HTTP semantics must not be silently reshaped by a gRPC
        // wildcard, so the catch-all is switched off - the same decision, for the same reason, that
        // services/persistence-service's composition root records.
        //
        // WHAT THIS COSTS IS ALMOST NOTHING. An authenticated caller naming a service this server does not
        // host now falls through to routing and receives HTTP 404, and every conforming gRPC client maps a
        // 404 onto UNIMPLEMENTED, so the status it surfaces is unchanged. An unauthenticated caller receives
        // 401 either way, because the fallback policy applies to a request that matched no endpoint just as
        // it does to one that did. Unknown METHODS on a service that IS hosted are unaffected and keep
        // answering a proper UNIMPLEMENTED status, because their route begins with a literal service name
        // that cannot collide with a REST path.
        _ = services.AddGrpc(static options => options.IgnoreUnknownServices = true);

        _ = services.AddDataServicesHealthChecks();

        // THE CUSTOMIZATION IS WHAT MAKES A FRAMEWORK-GENERATED BODY A CONTRACT-SHAPED ONE. The problem
        // responses this service writes itself - every projection in Endpoints/RestProjectionEndpoints.cs -
        // carry `retCode` and `traceId` because the code that writes them sets both. The bodies the
        // FRAMEWORK writes carry neither: the bearer challenge, the authorization refusal, an unmatched
        // route and a rejected method are all produced beneath any of this service's own code, and those
        // same projection routes declare ProducesProblem for 401, 403 and 404. Filling both members here is
        // what makes the declaration true of all of them rather than of the subset written by hand.
        //
        // The guard is not an optimization: a response this service composed itself has already resolved its
        // own correlation identifier and forwarded the UPSTREAM's return code, which is more specific than
        // anything derivable from a status, so overwriting either would replace a precise value with a
        // derived one.
        _ = services.AddProblemDetails(static options =>
            options.CustomizeProblemDetails = static context =>
            {
                if (context.ProblemDetails.Extensions.ContainsKey(ProblemContractMembers.RetCode))
                {
                    return;
                }

                int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

                context.ProblemDetails.Extensions[ProblemContractMembers.RetCode] = ClassifyFailure(status);
            });

        _ = services.AddOpenApi();

        return services;
    }

    /// <summary>
    /// Classifies a framework-generated failure status as a legacy return code, so that a body this service
    /// did not compose still carries the member its published routes declare.
    /// </summary>
    /// <param name="statusCode">The status the framework is answering with.</param>
    /// <returns>
    /// The legacy code for that status, and <see cref="RetCode.UNKNOWN"/> for anything unclassifiable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// EVERY ARM IS WRITTEN OUT RATHER THAN DERIVED FROM A TRUTHINESS TEST, and each one agrees with the
    /// projection's own vocabulary in <c>Endpoints/RestProjectionEndpoints.cs</c>: a malformed request is
    /// <c>E_INVALID_ARGUMENT</c>, a refused caller is <c>E_ACCESS_DENIED</c> whether the refusal was
    /// authentication or authorization, an unmatched route is <c>E_OBJECT_NOT_FOUND</c> and a rejected
    /// method is <c>E_NO_SUPPORT</c>. The legacy algebra declares exactly one access code and draws no
    /// distinction between "no credential" and "credential without permission"; the HTTP statuses stay
    /// distinct so a caller can still tell them apart, and the code gains no member the oracle never had.
    /// </para>
    /// <para>
    /// THE FINAL CHECK IS THE POINT OF THE GUARD. The algebra is tri-state with a documented hole -
    /// <c>PREVENT</c> is 1 and reads as a SUCCESS through <c>IsSucceeded</c>
    /// [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>], <c>CANCELLED</c> is excluded from
    /// <c>IsFailed</c> and so is NEITHER [<c>isfailed.srf:L11-L13</c>] - so an error body must never carry a
    /// code from either class. The kernel predicate is consumed to enforce that rather than the comparison
    /// being re-derived, so a future arm that violated it degrades to <c>UNKNOWN</c> instead of publishing a
    /// failure a consumer's own predicate would read as a success.
    /// </para>
    /// </remarks>
    private static long ClassifyFailure(int statusCode)
    {
        long classified = statusCode switch
        {
            StatusCodes.Status400BadRequest => RetCode.E_INVALID_ARGUMENT,
            StatusCodes.Status401Unauthorized => RetCode.E_ACCESS_DENIED,
            StatusCodes.Status403Forbidden => RetCode.E_ACCESS_DENIED,
            StatusCodes.Status404NotFound => RetCode.E_OBJECT_NOT_FOUND,
            StatusCodes.Status405MethodNotAllowed => RetCode.E_NO_SUPPORT,
            StatusCodes.Status408RequestTimeout => RetCode.E_TIME_OUT,
            StatusCodes.Status415UnsupportedMediaType => RetCode.E_INVALID_TYPE,
            StatusCodes.Status429TooManyRequests => RetCode.E_BUSY,
            StatusCodes.Status501NotImplemented => RetCode.E_NO_IMPLEMENTATION,
            StatusCodes.Status503ServiceUnavailable => RetCode.E_BUSY,
            StatusCodes.Status504GatewayTimeout => RetCode.E_TIME_OUT,
            >= StatusCodes.Status500InternalServerError => RetCode.E_INTERNAL_ERROR,
            _ => RetCode.UNKNOWN,
        };

        return Predicates.IsFailed(classified) ? classified : RetCode.UNKNOWN;
    }

    /// <summary>
    /// Maps every route and every gRPC service this host publishes.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// ONE CALL PER PUBLISHED SURFACE, AND EACH SURFACE DECLARES ITS OWN SECURITY WHERE IT IS
    /// EXERCISED. <c>/health</c> is the ONE documented anonymous exception in the system and says so
    /// inside <c>Endpoints/HealthEndpoints.cs</c>; <c>/v1/ping</c> requires a token and answers 401
    /// without one, declared inside <c>Endpoints/PingEndpoints.cs</c>; the REST projection applies
    /// <c>RequireAuthorization</c> once at its parent group so no projected route can be anonymous by
    /// omission. The two gRPC services declare it HERE, because a mapped gRPC service has no file of
    /// its own to declare it in.
    /// </para>
    /// <para>
    /// BOTH gRPC SERVICES REQUIRE AUTHORIZATION, NOT JUST THE PUBLIC EDGE. Constraint C-G covers
    /// INTERNAL edges too: Gateway is the only caller, and it presents a Security-issued token exactly
    /// as an external client would. The fallback policy registered by
    /// <see cref="AddDataServicesAuthentication"/> would already close them, and these two calls state
    /// the requirement explicitly so that it survives any later change to that fallback.
    /// </para>
    /// <para>
    /// THE Aborted -> 409 MAPPING IS DECLARED ONCE, AND NOT HERE. It belongs to
    /// <c>Endpoints/RestProjectionEndpoints.cs</c>, which applies ONE shared status projection to every
    /// route it declares and decodes the rich-error trailer both services advertise on their own method
    /// descriptors - so the trailer key, the gRPC status and the payload type are read from the
    /// contract rather than written as literals anywhere. On an <c>updatewhereclause</c> mismatch
    /// Persistence answers <c>Aborted</c>, DataServices relays it, and the projection answers HTTP 409
    /// carrying the same <c>common.v1.ConflictDetail</c> with the current row state. THERE IS NO SILENT
    /// OVERWRITE ANYWHERE IN THE SYSTEM; callers implement an explicit retry-or-surface policy.
    /// </para>
    /// <para>
    /// THE DbError REDACTION OBLIGATION IS HONOURED BY NOT LOGGING ONE. The legacy <c>sqlsyntax</c>
    /// field carries the COMPLETE generated statement including interpolated literal values, and the
    /// legacy logger performed no redaction at all [AAP 0.6.5, 0.6.3.8]. Nothing in this file logs,
    /// echoes, wraps or copies a relayed <c>DbError</c>; the only log statement in the whole file
    /// records two integer session counts at shutdown.
    /// </para>
    /// <para>
    /// AND NO ROUTE UNDER <c>/v1/design/**</c>, <c>/v1/documents/**</c>, <c>/v1/integration/**</c> OR
    /// <c>/v1/scripting/**</c>. Those four reserved 501 declarations live on GATEWAY's routing table
    /// alone (AAP 0.4.4). A routing declaration there is metadata about the eventual system; adding one
    /// here would make this service claim a surface it does not own.
    /// </para>
    /// </remarks>
    internal static WebApplication MapDataServicesEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The published contract document, AUTHENTICATED like every other route here bar /health. It used
        // to be anonymous on the argument that a description of a surface is not part of the surface, and
        // that argument does not survive the AAP: the anonymous exceptions are ENUMERATED - `/health` on
        // all four services (C-10) and Security's key set and discovery document (C-01) - and this is not
        // among them. Nor is anything withheld by protecting it: a consumer learns this service's shape
        // from the AUTHORED contracts in shared/PowerFramework.Contracts, which are files in the
        // repository, and this route serves a generated projection of them. Security, the token issuer
        // itself, already publishes its own document behind its own boundary. No AllowAnonymous call, so
        // the fallback policy applies and an anonymous request is answered 401 (constraint C-G).
        _ = app.MapOpenApi();

        _ = app.MapHealthEndpoints();
        _ = app.MapPingEndpoints();
        _ = app.MapRestProjectionEndpoints();

        // C-03: retrieval, the paired validation session, the update with its conflict status, the
        // event gate, the bidirectional event chain and the eight headless-model operations.
        _ = app.MapGrpcService<DataWindowService>()
            .RequireAuthorization(CallerAuthorization.DataWindowPolicyName);

        // C-04: kept a SEPARATE service from C-03 so the expansion engine can version independently of
        // the event chain (AAP 0.4.3), and carrying the two inverted streams - the macro channel and
        // the trace channel - that a single-direction contract could not express.
        _ = app.MapGrpcService<ColumnExpressionService>()
            .RequireAuthorization(CallerAuthorization.ColumnExpressionPolicyName);

        return app;
    }

    /// <summary>
    /// Reads the configured mutual-TLS client identity, or answers an empty collection when none is set.
    /// </summary>
    /// <param name="mutualTls">The bound pair of paths.</param>
    /// <returns>
    /// A collection holding the one certificate with its private key, or an EMPTY collection when this
    /// deployment presents none. Empty rather than <see langword="null"/>, so the handler registration has one
    /// shape to handle instead of two.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="mutualTls"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The pair is configured but the material cannot be read or does not parse. That is a structural fault and
    /// it stops the host: a deployment that meant to authenticate to the issuer and cannot has already lost
    /// every authenticated call it would make, so continuing would only defer the failure to first use. AAP
    /// 0.1.4 requires the legacy's fail-fast posture survive as fail-fast rather than soften into
    /// warning-and-continue.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>NO PATH IS EVER ECHOED INTO A MESSAGE</b>, and the exceptions below name the two CONFIGURATION KEYS
    /// instead. A path is not itself a credential, but it names the location of one, and a startup log is
    /// exactly the wrong place to publish where a private key is mounted. The configuration key is sufficient
    /// for an operator to find the setting, which is the rule <c>Configuration/DataServicesOptions.cs</c>
    /// already applies to its own validation messages (constraint C-F).
    /// </para>
    /// <para>
    /// HALF A PAIR SHOULD NOT REACH HERE. <see cref="MutualTlsClientOptions"/> validates the group as
    /// both-or-neither and the options registration validates on start, so by the time this runs the pair is
    /// either wholly present or wholly absent. The second check below is a guard against a future caller rather
    /// than a reachable configuration state, and it is written as one rather than as an assumption.
    /// </para>
    /// <para>
    /// The material is read from a PEM certificate and a separate PEM key, which is the shape the generation
    /// recipe in <c>docs/ARCHITECTURE.md</c> produces and the shape the <c>*_MTLS_CERT_PATH</c> /
    /// <c>*_MTLS_KEY_PATH</c> variables name. The resulting key is ephemeral, which is directly usable for TLS
    /// client authentication on Linux - the target operating system for every container in this refactor.
    /// </para>
    /// <para>
    /// DELIBERATELY THE SAME SHAPE AS GATEWAY'S LOADER, down to the caught exception set and the wording of the
    /// refusal. Two services solving one problem two ways is how one of them drifts, and a reader who has
    /// understood either has understood both. It is not SHARED code, because AAP 0.4.3 permits exactly one
    /// cross-service coupling - the published contracts - and a composition-root helper is not a contract.
    /// </para>
    /// </remarks>
    internal static X509Certificate2Collection LoadMutualTlsClientIdentity(
        MutualTlsClientOptions mutualTls)
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
                $"'{DataServicesOptions.SectionName}:Security:MutualTls' is half configured. A certificate "
                    + "cannot complete a handshake without its key and a key has nothing to present without "
                    + "its certificate, so set both "
                    + $"'{nameof(MutualTlsClientOptions.CertificatePath)}' and "
                    + $"'{nameof(MutualTlsClientOptions.CertificateKeyPath)}' or neither.");
        }

        try
        {
            // Constructed directly into the collection so the certificate has no owning local: its lifetime is
            // the returned collection's, which the container holds as a singleton for the life of the process.
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
                    + $"'{DataServicesOptions.SectionName}:Security:MutualTls' could not be loaded, so this "
                    + "deployment cannot authenticate to the token-issuance edge and the host will not start. "
                    + "Check that both files exist, that the process can read them, and that each is PEM "
                    + $"encoded - the certificate in '{nameof(MutualTlsClientOptions.CertificatePath)}' and "
                    + $"its private key in '{nameof(MutualTlsClientOptions.CertificateKeyPath)}'. Neither path "
                    + "is reproduced here, because a startup log is the wrong place to publish where a private "
                    + "key is mounted.",
                failure);
        }
    }
}


/// <summary>
/// The trust anchor every outbound internal channel verifies its peer against, loaded once from the
/// path <c>DataServices:InternalTls:TrustedCaPath</c> names.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS FIXES. Persistence and Security both terminate TLS with certificates issued by the LOCAL
/// certificate authority the generation recipe in <c>docs/ARCHITECTURE.md</c> §9.3.1 creates, and that
/// authority is in no container's operating-system trust store. Left on platform default trust, every
/// outbound channel this service opens - the four Persistence gRPC channels, the Security token and
/// crypto channel, and the bearer handler's key-set backchannel - rejects the certificate it is
/// presented, so the service cannot obtain a token and cannot reach a single Persistence RPC.
/// </para>
/// <para>
/// IT NARROWS TRUST; IT DOES NOT RELAX IT. The policy built here sets
/// <see cref="X509ChainTrustMode.CustomRootTrust"/>, so the mounted anchor becomes the ONLY acceptable
/// root for internal traffic and the machine's public roots stop being acceptable for it. Chain
/// building, name validation and validity dates are still performed by the platform. There is no
/// <c>RemoteCertificateValidationCallback</c>, no <c>ServerCertificateCustomValidationCallback</c>, no
/// <c>DangerousAcceptAnyServerCertificate</c> and no environment-conditional bypass anywhere in this
/// service (constraint C-G).
/// </para>
/// <para>
/// REVOCATION IS NOT CHECKED, AS A CONSEQUENCE OF THE TOPOLOGY RATHER THAN AS A RELAXATION. A local
/// authority generated by two <c>openssl</c> invocations publishes no revocation list and runs no
/// responder, so an online check has nothing to ask; the recipe's own <c>-days 30</c> lifetime is the
/// control that substitutes for revocation. A deployment whose authority does publish revocation
/// information leaves this path unset and uses platform trust, where the platform's default revocation
/// behaviour applies.
/// </para>
/// <para>
/// LOADED ONCE AND SHARED, because the handler factories recycle their primary handlers on a schedule
/// and reading the anchor inside a factory would re-read the file on every rotation. A singleton is
/// also what lets the eager resolve after <c>Build()</c> turn an unreadable anchor into a startup
/// failure rather than a first-request one.
/// </para>
/// </remarks>
/// <summary>
/// The two authorization policies this service's contracts are served under: who may call, and what they
/// may do.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. Both gRPC contracts and all thirty-nine projected REST routes used to be protected by
/// "an authenticated user" and nothing more, so any holder of any token this issuer minted for this
/// audience could call every operation on both - and a credential minted for a caller that has no business
/// here at all could do the same (CWE-862, CWE-863). Both halves of the fix are here because either alone
/// leaves a hole: scope without subject admits any caller the issuer serves as long as it holds the scope,
/// and subject without scope lets the one permitted caller reach both contracts once it is in.
/// </para>
/// <para>
/// THE SCOPE NAMES ARE NOT INVENTED HERE. Gateway requests exactly <c>dataservices.datawindow</c> and
/// <c>dataservices.columnexpression</c> for this edge, and the split matches the contracts: C-03 is the
/// DataWindow service and C-04 is the expression engine, kept separate precisely so the expansion engine
/// can version independently (AAP 0.4.3). Stating them as constants keeps the receiver and the issuance
/// roster spelling one thing.
/// </para>
/// <para>
/// A SCOPE CLAIM IS SPACE-DELIMITED AND MUST BE SPLIT, WHICH IS WHY THIS IS AN ASSERTION AND NOT
/// <c>RequireClaim</c>. RFC 6749 carries the granted set as ONE claim holding a space-delimited list, so
/// <c>RequireClaim("scope", "dataservices.datawindow")</c> would demand a token whose ENTIRE scope claim is
/// that one value - and would refuse the very credential Gateway obtains, which carries both scopes.
/// </para>
/// <para>
/// THE SUBJECT IS READ FROM EITHER SPELLING. A bearer handler with inbound claim mapping on renames
/// <c>sub</c> to the framework's name-identifier claim type, and with it off leaves <c>sub</c> alone; both
/// are legitimate, and this service must not silently stop enforcing identity because of one. Whichever is
/// present is compared ORDINALLY, matching how the issuer compares an identity everywhere else.
/// </para>
/// <para>
/// A REFUSAL IS gRPC <c>PermissionDenied</c> RATHER THAN <c>Unauthenticated</c>, because the principal was
/// established and found insufficient - and the REST projection in this service already publishes that as
/// HTTP 403 against 401, "a valid-but-insufficient credential, distinct from none". Nothing about the wire
/// contract changes; this is the code that makes the published 403 reachable.
/// </para>
/// </remarks>
/// <summary>
/// The extension-member names the published problem body declares.
/// </summary>
/// <remarks>
/// SPELLED ONCE HERE BECAUSE THE COMPOSITION ROOT AND THE PROJECTION FILE BOTH WRITE THEM, and the
/// customization in the composition root exists precisely to fill the member the projection did not. Two
/// independent spellings would let a rename go half-applied, at which point a body would carry both the old
/// member and the new one and a consumer would read whichever it happened to look for.
/// </remarks>
internal static class ProblemContractMembers
{
    /// <summary>The legacy return code carried by every problem body.</summary>
    internal const string RetCode = "retCode";
}

/// <summary>
/// The names the two capability policies are registered and referenced under.
/// </summary>
/// <remarks>
/// <para>
/// ONE NAME PER CAPABILITY, AND IT IS THE PUBLISHED SCOPE NAME ITSELF. These aliases previously carried a
/// second spelling of the same two decisions - <c>dataservices:datawindow</c> beside the published
/// <c>dataservices.datawindow</c> - and each was registered separately, so the service ran with FOUR
/// policies for TWO capabilities: the pair the endpoints named and a parallel pair nothing reached. Two
/// spellings for one authorization decision is the shape in which a route ends up naming a policy that
/// exists but enforces less than the one an author was editing, and the framework answers an unregistered
/// name with an unexplained internal error rather than a refusal.
/// </para>
/// <para>
/// THE SPELLING KEPT IS THE ONE THAT IS ALREADY ON THE WIRE. <c>DataServicesScopes</c> publishes these two
/// values as the scopes Gateway requests and Security's grant matrix authorises, so the policy name, the
/// scope claim and the issuance grant are one string in three files rather than a mapping that has to be
/// maintained. These members remain as the names the endpoint files reference so the call sites read as
/// authorization rather than as string literals.
/// </para>
/// </remarks>
internal static class CallerAuthorization
{
    /// <summary>The policy name C-03 - the DataWindow service - and its projected routes are mapped under.</summary>
    internal const string DataWindowPolicyName = DataServicesScopes.DataWindow;

    /// <summary>The policy name C-04 - the column-expression engine - and its routes are mapped under.</summary>
    internal const string ColumnExpressionPolicyName = DataServicesScopes.ColumnExpression;
}

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile into an implicitly internal
/// <c>Program</c> class, so without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the
/// sibling <c>PowerFramework.DataServices.Tests</c> project cannot name the entry point, the
/// service-level tests cannot boot this host at all, and the per-service coverage gate (constraint
/// C-H) becomes unreachable for every line in this file. The <c>InternalsVisibleTo</c> item in this
/// project's <c>.csproj</c> covers the seven internal registration groups above; the entry point
/// itself is made <c>public</c> because the factory's generic constraint resolves it by name from
/// outside.
/// </remarks>
internal sealed class InternalTlsTrust
{
    private readonly X509Certificate2Collection _anchors;

    /// <summary>
    /// Loads the anchor bundle, or records that this deployment uses platform default trust.
    /// </summary>
    /// <param name="options">
    /// The validated <c>DataServices:InternalTls</c> group. A path, never material.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A path is configured but the bundle cannot be read or does not parse. Structural, and therefore
    /// fatal.
    /// </exception>
    /// <remarks>
    /// The path is NOT echoed into the failure message. A trust anchor is public material, but a
    /// container's secret mount layout is not something a startup record should publish, so the message
    /// names the configuration key instead - the same rule
    /// <c>Configuration/DataServicesOptions.cs</c> applies to its own validation messages.
    /// </remarks>
    public InternalTlsTrust(InternalTlsTrustOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
        {
            _anchors = [];

            return;
        }

        try
        {
            X509Certificate2Collection loaded = [];

            loaded.ImportFromPemFile(options.TrustedCaPath.Trim());

            if (loaded.Count == 0)
            {
                throw new CryptographicException("The file carried no PEM-encoded certificate.");
            }

            _anchors = loaded;
        }
        catch (Exception failure) when (failure
            is CryptographicException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            throw new InvalidOperationException(
                "The internal trust anchor named by "
                    + $"'{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.InternalTls)}:"
                    + $"{nameof(InternalTlsTrustOptions.TrustedCaPath)}' could not be loaded, so this "
                    + "deployment cannot verify its upstreams' certificates and the host will not "
                    + "start. Check that the file exists, that the process can read it, and that it is "
                    + "a PEM-encoded certificate or chain of them. The path is not reproduced here, "
                    + "because a startup record must not publish a container's secret mount layout.",
                failure);
        }
    }

    /// <summary>
    /// Whether internal trust is pinned to a mounted anchor rather than left to the platform.
    /// </summary>
    public bool IsPinned => _anchors.Count > 0;

    /// <summary>
    /// Applies the pinned anchor to one outbound handler, or leaves platform trust in place.
    /// </summary>
    /// <param name="handler">The handler about to be used for internal traffic.</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A FRESH POLICY PER HANDLER, DELIBERATELY. <see cref="X509ChainPolicy"/> is not documented as
    /// thread-safe and a handler may be used concurrently, so each handler receives its own instance
    /// built over the SAME shared anchor collection - one file read, one policy per consumer.
    /// </remarks>
    public void Apply(SocketsHttpHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!IsPinned)
        {
            return;
        }

        X509ChainPolicy policy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
        };

        policy.CustomTrustStore.AddRange(_anchors);

        handler.SslOptions.CertificateChainPolicy = policy;
    }
}

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile into an implicitly internal
/// <c>Program</c> class, so without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the
/// sibling <c>PowerFramework.DataServices.Tests</c> project cannot name the entry point, the
/// service-level tests cannot boot this host at all, and the per-service coverage gate (constraint
/// C-H) becomes unreachable for every line in this file. The <c>InternalsVisibleTo</c> item in this
/// project's <c>.csproj</c> covers the seven internal registration groups above; the entry point
/// itself is made <c>public</c> because the factory's generic constraint resolves it by name from
/// outside.
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
