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

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
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

// --------------------------------------------------------------------------------------------------
// THE REQUEST PIPELINE
//
// Exactly two middleware components, in the only order that works: authentication establishes the
// principal, authorization then decides. Nothing else is added. There is no CORS policy (no browser
// reaches this service - Gateway is the sole ingress), no HTTPS redirection (the endpoint scheme is
// the deployment's to choose and forcing a redirect here would break the cleartext HTTP/2 gRPC edge),
// no response compression, no rate limiter and no exception-page middleware. Each of those would be a
// feature this refactor was not asked for (constraint C-B).
// --------------------------------------------------------------------------------------------------
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
    /// NO SECRET AND NO KEY MATERIAL PASSES THROUGH HERE. Both groups carry addresses, audiences,
    /// lifetimes, thresholds and preserved legacy defaults; neither declares a signing key, a
    /// password, a token or a certificate, and the only credential-shaped members are the two mutual
    /// TLS PATH settings, which are paths and not material (constraint C-F).
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
            .Configure<IOptions<JwtAuthenticationOptions>>(static (bearer, authentication) =>
            {
                JwtAuthenticationOptions configured = authentication.Value;

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

                bearer.TokenValidationParameters.ValidateIssuer = configured.ValidateIssuer;
                bearer.TokenValidationParameters.ValidateAudience = configured.ValidateAudience;
                bearer.TokenValidationParameters.ValidateLifetime = configured.ValidateLifetime;
                bearer.TokenValidationParameters.ValidateIssuerSigningKey =
                    configured.ValidateIssuerSigningKey;
            });

        return services.AddAuthorization(static options =>
        {
            AuthorizationPolicy authenticated = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.DefaultPolicy = authenticated;
            options.FallbackPolicy = authenticated;
        });
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
    /// THE TYPED CLIENT IS BUILT BY AN EXPLICIT FACTORY, WHICH IS LOAD BEARING AND NOT STYLE.
    /// <see cref="SecurityClient"/> declares two constructors that BOTH accept an
    /// <see cref="HttpClient"/> plus container-resolvable parameters. The typed-client factory calls
    /// the activator with the <see cref="HttpClient"/> supplied, and the activator then demands
    /// exactly one best match; two equal matches make it throw "Multiple constructors accepting all
    /// given argument types have been found" on the first resolve - which is the first token request,
    /// i.e. the first authenticated call this service makes. Naming the constructor removes the
    /// ambiguity, and the one named is the four-argument overload so that the determinism seam
    /// registered by <see cref="AddDataServicesDeterminismSeam"/> is genuinely engaged rather than
    /// silently defaulted to the system clock.
    /// </para>
    /// <para>
    /// THE TWO INTERFACE REGISTRATIONS ARE THE SAME OBJECT, NOT TWO. <see cref="SecurityClient"/>
    /// implements both <see cref="IServiceTokenProvider"/> (C-01) and
    /// <see cref="ICryptoServiceClient"/> (C-02), and both resolve THROUGH the typed client so that the
    /// held credential is shared rather than re-minted per interface. Under C-02 raw key material never
    /// crosses the wire from a caller - it passes an opaque reference that Security resolves on its own
    /// side - and nothing here holds, forwards or logs any.
    /// </para>
    /// </remarks>
    internal static IServiceCollection AddDataServicesClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _ = services
            // WHERE a Security request goes is stated HERE and read nowhere else, which keeps this
            // composition root the single authority over the transport. The client itself reads the
            // bound setting for one purpose only - telling "not configured at all" apart from
            // "configured but registered without it" in a fail-fast diagnostic - and never to compose
            // an address.
            .AddHttpClient<SecurityClient>(static (serviceProvider, httpClient) => httpClient
                .BaseAddress = new Uri(
                    serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>()
                        .Value
                        .Security
                        .BaseAddress,
                    UriKind.Absolute))
            // HOW the client is constructed is stated separately, and by an explicit factory for the
            // constructor-ambiguity reason in this method's remarks.
            .AddTypedClient(static (httpClient, serviceProvider) => new SecurityClient(
                httpClient,
                serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>(),
                serviceProvider.GetRequiredService<ILogger<SecurityClient>>(),
                serviceProvider.GetRequiredService<TimeProvider>()))
            .AddStandardResilienceHandler()
            .Configure(static (resilience, serviceProvider) => ApplyResilience(
                resilience,
                serviceProvider.GetRequiredService<IOptions<DataServicesOptions>>()
                    .Value
                    .Resilience
                    .Security));

        services.TryAddTransient<IServiceTokenProvider>(static serviceProvider =>
            serviceProvider.GetRequiredService<SecurityClient>());

        services.TryAddTransient<ICryptoServiceClient>(static serviceProvider =>
            serviceProvider.GetRequiredService<SecurityClient>());

        // C-05 QueryService, C-06 UpdateService, C-07 CommandService, C-08 TransactionService. Four
        // separate clients rather than one, because the contract is four services so that the update
        // surface - the one carrying optimistic-concurrency semantics - can version independently of
        // the retrieval surface (AAP 0.3.4).
        _ = services
            .AddGrpcClient<PersistenceQueryClient>(ConfigurePersistenceChannel)
            .AddStandardResilienceHandler()
            .Configure(ConfigurePersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceUpdateClient>(ConfigurePersistenceChannel)
            .AddStandardResilienceHandler()
            .Configure(ConfigurePersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceCommandClient>(ConfigurePersistenceChannel)
            .AddStandardResilienceHandler()
            .Configure(ConfigurePersistenceResilience);

        _ = services
            .AddGrpcClient<PersistenceTransactionClient>(ConfigurePersistenceChannel)
            .AddStandardResilienceHandler()
            .Configure(ConfigurePersistenceResilience);

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
    /// Applies the configured Persistence resilience settings to a standard handler.
    /// </summary>
    /// <param name="resilience">The handler options being configured.</param>
    /// <param name="serviceProvider">The provider the bound options are read from.</param>
    private static void ConfigurePersistenceResilience(
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
    /// THE ATTEMPT TIMEOUT IS LEFT AT THE LIBRARY DEFAULT ON PURPOSE. The handler validates that the
    /// circuit-breaker sampling duration is at least double the attempt timeout and that the total
    /// request timeout is not shorter than a single attempt; overriding one of the three from
    /// configuration while inventing values for the others is how a deployment ends up failing that
    /// validation at startup for a reason nobody stated. The configured group names the four values the
    /// AAP calls for and leaves the rest of the pipeline as published.
    /// </para>
    /// </remarks>
    private static void ApplyResilience(
        HttpStandardResilienceOptions resilience,
        ClientResilienceOptions configured)
    {
        resilience.Retry.MaxRetryAttempts = configured.MaxRetryAttempts;
        resilience.Retry.Delay = configured.RetryBaseDelay;
        resilience.CircuitBreaker.FailureRatio = configured.CircuitBreakerFailureRatio;
        resilience.CircuitBreaker.MinimumThroughput = configured.CircuitBreakerMinimumThroughput;
        resilience.CircuitBreaker.SamplingDuration = configured.CircuitBreakerSamplingDuration;
        resilience.CircuitBreaker.BreakDuration = configured.CircuitBreakerBreakDuration;
        resilience.TotalRequestTimeout.Timeout = configured.RequestTimeout;
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
        // THE THREE HOST-BINDING SEAMS
        //
        // All three answer the same question - "which concrete DataWindow does this handle name?" - and
        // all three are REQUIRED dependencies of the published gRPC services, so they are registered
        // rather than left out: a service composed without them is structurally faulty, and the AAP
        // requires that stay fail-fast rather than soften. Registering them is what makes the two
        // MapGrpcService calls real rather than decorative.
        //
        // WHY THE SHIPPED IMPLEMENTATIONS BIND NO HANDLE, STATED AS A FINDING AND NOT AN OVERSIGHT.
        // Binding a handle needs a concrete `DataWindowServiceHost`, and `se_cst_dw` DERIVES FROM
        // `se_cst_datawindow` [se_cst_dw.sru:L4, :L10], which lives in `pfw.ui.controls.ext` - a
        // DEFERRED DesignSystem library. That is a STRUCTURAL INHERITANCE EDGE, not a call, so no
        // refactoring at a call site removes it, and AAP 0.2.1.3 Correction 3 resolves it by having
        // DataServices declare its OWN abstract host contract and record the legacy parent as
        // REFERENCE-only. Materialising a DataWindow is therefore DesignSystem's work, reserved behind
        // the `/v1/design/**` extension point (AAP 0.4.4), and constraint C-D forbids implementing a
        // deferred service even partially and even to stub it out. AAP 0.8.1 settles the choice: where
        // it lies between a partial implementation and a documented gap, THE DOCUMENTED GAP WINS.
        //
        // WHAT THE SHIPPED IMPLEMENTATIONS DO INSTEAD IS ANSWER EACH CONTRACT'S OWN DEFINED NEGATIVE.
        // Every one of the three interfaces documents a null answer as its "this provider cannot serve
        // the handle" result, and the services turn that into `RetCode.E_INVALID_HANDLE` for a model or
        // chain request and into a failed open for an expression session. So a caller that names a
        // handle learns that the handle names nothing - which is the contract - rather than receiving a
        // second empty DataWindow, an exception, or a blanket `E_NO_IMPLEMENTATION`.
        //
        // AND THE GAP IS NARROW, WHICH IS WHY THE SURFACE IS STILL WORTH PUBLISHING. The seams are
        // reached by the eight headless-model read/apply operations, by C-03's bidirectional
        // `EventChain` and by C-04's session open. Retrieve, Update - including the
        // `updatewhereclause` conflict projected as Aborted and then 409 - the paired validation
        // session, and the whole event-gate surface reach NONE of them, so the retrieval / validation /
        // update triple works end to end today.
        //
        // REGISTERED WITH TryAdd SO SUBSTITUTION NEEDS NO EDIT HERE. A host that CAN materialise a
        // DataWindow registers its own factory before this method runs, or replaces the descriptor in a
        // test host, and the published surface then serves handles with no change to this file.
        // ------------------------------------------------------------------------------------------
        services.TryAddSingleton<IDataWindowHostFactory, UnboundDataWindowHostFactory>();
        services.TryAddSingleton<IDataWindowModelSetProvider, UnboundDataWindowModelSetProvider>();
        services.TryAddSingleton<IDataWindowEventChainFactory, UnboundDataWindowEventChainFactory>();

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

        _ = services.AddGrpc();
        _ = services.AddHealthChecks();
        _ = services.AddProblemDetails();
        _ = services.AddOpenApi();

        return services;
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

        // The published contract document, anonymous because a description of the surface is not part
        // of the surface it describes, and every operation in it still states its own security
        // requirement. Explicit rather than implicit, so it survives the fallback policy.
        _ = app.MapOpenApi().AllowAnonymous();

        _ = app.MapHealthEndpoints();
        _ = app.MapPingEndpoints();
        _ = app.MapRestProjectionEndpoints();

        // C-03: retrieval, the paired validation session, the update with its conflict status, the
        // event gate, the bidirectional event chain and the eight headless-model operations.
        _ = app.MapGrpcService<DataWindowService>().RequireAuthorization();

        // C-04: kept a SEPARATE service from C-03 so the expansion engine can version independently of
        // the event chain (AAP 0.4.3), and carrying the two inverted streams - the macro channel and
        // the trace channel - that a single-direction contract could not express.
        _ = app.MapGrpcService<ColumnExpressionService>().RequireAuthorization();

        return app;
    }
}


// ==================================================================================================
//  THE THREE HOST-BINDING SEAM IMPLEMENTATIONS
//  ------------------------------------------------------------------------------------------------
//  These are COMPOSITION types, which is why they live in the composition root: each answers "which
//  concrete DataWindow does this handle name?", and that is a wiring question rather than a
//  behavioural one. Not one of them decides a veto, resolves an ordering discipline, classifies an
//  item-change result, computes a gate or evaluates an expression - all of which stay in Domain/,
//  Expressions/ and Services/ next to the `ws_objects/**` locators that authorise them.
//
//  WHY EACH BINDS NO HANDLE IN THIS REFACTOR, ONCE, RATHER THAN THREE TIMES BELOW.
//  A binding needs a concrete `DataWindowServiceHost`. `se_cst_dw` derives from `se_cst_datawindow`
//  [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L4, :L10], which lives in
//  `pfw.ui.controls.ext` - a DEFERRED DesignSystem library. AAP 0.2.1.3 Correction 3 resolves that
//  structural inheritance edge by having DataServices declare its own ABSTRACT host contract and
//  record the legacy parent as REFERENCE-only, which is exactly what Domain/DataWindowServiceHost.cs
//  is. Materialising a DataWindow behind that contract is DesignSystem's work, reserved behind the
//  `/v1/design/**` extension point (AAP 0.4.4), and constraint C-D forbids implementing a deferred
//  service even partially and even to stub it out. AAP 0.8.1 settles the choice: between a partial
//  implementation and a documented gap, THE DOCUMENTED GAP WINS.
//
//  WHAT THEY DO INSTEAD IS ANSWER THE CONTRACT'S OWN DEFINED NEGATIVE, WHICH IS NOT THE SAME AS A
//  STUB. Each of the three interfaces documents its null answer as "this provider cannot serve the
//  handle", and each consumer already turns that into a specific, published result:
//  `RetCode.E_INVALID_HANDLE` for a model-set or chain request, and a failed session open for the
//  expression host. So a caller learns that the handle names nothing - which is a true statement -
//  rather than receiving a second empty DataWindow, an exception, or a blanket
//  `E_NO_IMPLEMENTATION`. None of the three throws `NotImplementedException`, and none is reachable
//  only from a test.
//
//  SUBSTITUTION NEEDS NO EDIT TO THIS FILE. All three are registered with `TryAdd`, so a host that
//  can materialise a DataWindow registers its own implementation first, or replaces the descriptor,
//  and the whole published surface then serves handles unchanged.
// ==================================================================================================

/// <summary>
/// The shipped <see cref="IDataWindowHostFactory"/>: it binds no DataWindow name, because binding one
/// requires the deferred DesignSystem ancestry recorded above.
/// </summary>
/// <remarks>
/// C-04's <c>OpenExpressionSession</c> is the sole consumer. A null answer FAILS THE WHOLE OPEN rather
/// than yielding a session with a hole in it - a partial open would BLOCK a foreign variable the caller
/// had every reason to expect to resolve - so the refusal is total and immediate, which is the
/// fail-fast posture rather than a softening of it. Every other C-04 operation, including
/// <c>GetServiceState</c> and <c>CloseExpressionSession</c>, is unaffected.
/// </remarks>
internal sealed class UnboundDataWindowHostFactory : IDataWindowHostFactory
{
    /// <inheritdoc/>
    public DataWindowServiceHost? Create(string dataWindowName)
    {
        // The argument is validated even though the answer does not depend on it: a caller that
        // supplied nothing has made a different mistake from one that supplied an unbound name, and
        // collapsing the two would send a reader of the resulting diagnostic to the wrong place.
        ArgumentNullException.ThrowIfNull(dataWindowName);

        return null;
    }
}

/// <summary>
/// The shipped <see cref="IDataWindowModelSetProvider"/>: it binds no DataWindow handle, because a
/// model set is four services ATTACHED TO A HOST and no host can be materialised here.
/// </summary>
/// <remarks>
/// <para>
/// The eight headless-model read and apply operations of contract C-03 are the consumers, and each
/// turns a null answer into <c>RetCode.E_INVALID_HANDLE</c>. That is the contract's own answer for a
/// handle it cannot bind, and it is emphatically NOT a silently created set: a caller that mistyped a
/// handle must learn that rather than receive a second empty DataWindow whose applied state nobody
/// will ever read back.
/// </para>
/// <para>
/// Retention is the other half of the interface's contract - an <c>Apply</c> followed by a <c>Get</c>
/// MUST observe what was applied, because the legacy services live as long as their control does
/// [<c>se_cst_dw.sru:L80-L84</c>]. There is nothing to retain while there is nothing to create, so
/// this implementation holds no cache; the moment a host factory is substituted, retention belongs
/// with the implementation that creates the sets.
/// </para>
/// </remarks>
internal sealed class UnboundDataWindowModelSetProvider : IDataWindowModelSetProvider
{
    /// <inheritdoc/>
    public DataWindowModelSet? GetOrCreate(string dataWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(dataWindowHandle);

        return null;
    }
}

/// <summary>
/// The shipped <see cref="IDataWindowEventChainFactory"/>: it binds no DataWindow handle, because a
/// chain IS a <see cref="DataWindowServiceHost"/> and no host can be materialised here.
/// </summary>
/// <remarks>
/// <para>
/// C-03's bidirectional <c>EventChain</c> is the sole consumer, and it turns a null answer into
/// <c>RetCode.E_INVALID_HANDLE</c> rather than opening a chain over a DataWindow that does not exist.
/// Everything the paired validation session itself owns - the open, the correlation, the event-gate
/// read and the two gate mutators - is reached without this seam and is unaffected, as are
/// <c>Retrieve</c> and <c>Update</c>.
/// </para>
/// <para>
/// THE CHAIN OWNS THE ORDER AND THIS FILE NEVER WILL. A chain built by any implementation of this
/// interface brings with it the creation sequence at <c>se_cst_dw.sru:L570-L574</c>, the DIFFERENT
/// initialisation sequence at <c>:L576-L580</c> with positions three and four swapped, the broker
/// created before any service [<c>:L562</c>], the gate bit-tests, the tri-valued veto and the
/// {0,1,2,3} item-change micro-protocol - and the teardown at <c>:L583-L589</c> that unsubscribes
/// before it destroys. None of that is re-implemented, re-ordered or second-guessed on this boundary,
/// and a substituted factory must preserve all of it because
/// <c>Domain/DataWindowEventChain.cs</c> already does.
/// </para>
/// </remarks>
internal sealed class UnboundDataWindowEventChainFactory : IDataWindowEventChainFactory
{
    /// <inheritdoc/>
    public DataWindowEventChain? Create(
        ValidationSession session,
        string dataWindowHandle,
        IDataWindowEventObserver observer,
        IDataWindowSemanticResponder responder)
    {
        // All four are validated because all four are required by the interface, and a null here is a
        // caller defect rather than an unbound handle. Conflating the two would report a missing
        // DataWindow when the real fault was a missing observer.
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(dataWindowHandle);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(responder);

        return null;
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

