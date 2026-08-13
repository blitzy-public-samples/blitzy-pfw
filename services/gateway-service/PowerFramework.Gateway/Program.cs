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

using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Polly;
using PowerFramework.Gateway.Authorization;
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
//  CORRELATION: THE W3C TRACE CONTEXT IS STAMPED ONTO EVERY LOG RECORD THIS SERVICE WRITES.
//
//  ASP.NET Core already starts an Activity per request and already continues an inbound `traceparent`,
//  so the identifier a caller upstream of this service holds is present here on every request - it was
//  simply never written anywhere an operator can read. An operator holding a Gateway `traceId` from a
//  502 therefore could not join it to the record on this side that explains the fault, which is the
//  whole point of having a correlation identifier at all.
//
//  ActivityTrackingOptions is shared-framework code and adds NO package: the deliberately-excluded list
//  in Directory.Packages.props rules out Serilog and the OpenTelemetry family, and this is the built-in
//  mechanism that remains. TraceId and SpanId identify the operation and the step within it; ParentId
//  is what makes a record attributable to the CALLER's span rather than only to the trace. Baggage and
//  Tags are deliberately NOT tracked: both are caller-controlled key-value sets, so tracking them would
//  copy attacker-influenced content into log records - the opposite of the redaction posture
//  docs/ARCHITECTURE.md 9.9 records.
//
//  This changes no response and no behaviour. It changes what a record CONTAINS, which is why it is
//  paired with the `traceId` member the problem-details customization below adds to every problem body:
//  one identifier, published on the wire and written in the log, so the two can be joined.
// --------------------------------------------------------------------------------------------------
builder.Logging.Configure(static options => options.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

// 🔴 AND THE SCOPE IS ACTUALLY RENDERED, WITHOUT WHICH THE LINE ABOVE CHANGES NOTHING OBSERVABLE.
//
// ActivityTrackingOptions puts the trace context into a log SCOPE. The console formatter's default is
// `IncludeScopes = false`, so every one of those identifiers was assembled per record and then dropped
// before it reached an operator - the mechanism was configured and its output was discarded. A measured
// sweep of a running deployment found the caller's `traceId` in 39 Gateway records, because Gateway
// alone also names it in its own message templates, and in ZERO records on the other two services: the
// correlation identifier the ingress hands a caller in a problem body could not be joined to the
// records on the service that actually failed, which is the entire purpose of publishing it.
//
// AddSimpleConsole IS THE MECHANISM, AND IT ADDS NO PACKAGE. Directory.Packages.props deliberately
// excludes Serilog and the OpenTelemetry family (AAP 0.5.3), so the shared framework's own formatter is
// what remains. It does NOT add a second console provider: the console registration uses
// TryAddEnumerable, so this configures the one already present rather than duplicating it - verified by
// counting ILoggerProvider registrations before and after, which stayed at three, and by confirming one
// record per event rather than two.
//
// SET IN CODE RATHER THAN IN appsettings.json, deliberately. A settings key can be silently dropped by
// a deployment's own configuration layer, and this estate's settings files are asserted key-for-key by
// their own coherence tests - so the guarantee belongs where it cannot be overridden by omission.
builder.Logging.AddSimpleConsole(static options => options.IncludeScopes = true);

// --------------------------------------------------------------------------------------------------
// 0. FILE-BACKED SECRET MATERIAL, RESOLVED BEFORE ANYTHING READS A SECRET
//
// Gateway holds no signing key and mints nothing, but it does hold one secret: the client credential it presents to Security
// in exchange for a token. It is read through a flat configuration key whose NAME is declared on the
// options type and whose VALUE the deployment supplies. Supplied as an environment variable, that
// credential is exposed to `docker compose config`, `docker inspect`, `/proc/<pid>/environ` and every
// child process; supplied as a projected file, to none of those.
//
// SO THE KEY ACCEPTS A `<KEY>_FILE` COMPANION, resolved here. It runs FIRST because the options graph
// and the outbound client both read that key, and a later registration would leave one reader on the
// environment value and the other on the file. The environment-variable form still works, so the
// documented bring-up is unchanged. Configuration/FileBackedSecrets.cs carries the refusal rules.
_ = builder.AddFileBackedSecrets();

// --------------------------------------------------------------------------------------------------
// 0. THE BOUNDS THIS SERVICE PLACES ON ITS OWN INGRESS, APPLIED BEFORE ANYTHING ELSE IS REGISTERED
//
// Decomposition creates this system's first-ever listening socket [Agent Action Plan 0.1.4]: the legacy
// was a library that received no unsolicited request, so no legacy path could be flooded and no legacy
// limit existed to port. The bounds registered here answer a failure mode the TRANSITION introduced,
// which is the same reason outbound resilience is present (0.5.3) rather than a behaviour improvement
// layered on top of it (constraint C-B). No package is added: the limiter is shared-framework code.
//
// It is FIRST because its transport half configures the listener, and a listener bound must be in place
// before the host is built rather than after the first request has already been accepted without one.
// Configuration/IngressOptions.cs states every value and argues each; Composition/IngressHardening.cs
// is the wiring and explains the two chained limiters and why the request-layer one runs after
// authentication. The pipeline half is installed further down, immediately after UseAuthentication.
// --------------------------------------------------------------------------------------------------
builder.AddIngressHardening();

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

    // THE ONE VALUE BINDING CANNOT REACH, APPLIED BETWEEN BINDING AND VALIDATION - AND THAT ORDER IS
    // THE ONLY ORDER THAT WORKS. The client credential Gateway presents on the token-issuance edge
    // arrives as a FLAT environment variable, because the environment-variable provider maps only a
    // double underscore onto the section separator and therefore cannot land a value named
    // SECURITY_CLIENT_SECRET_GATEWAY on a property inside 'Gateway:'. Flat is also what keeps the
    // credential out of every committed settings file (C-F).
    //
    // Post-configure runs AFTER Bind and BEFORE the start-time validation below, so the validator sees
    // the material the deployment actually supplied. Applied the other way round, a deployment that
    // supplied the secret correctly would be refused at startup for presenting nothing.
    //
    // GUARDED ON PRESENCE, WHICH IS THE OTHER HALF AND THE HALF MOST EASILY OMITTED. Assigning
    // unconditionally with `?? string.Empty` does not leave the property alone when the flat key is ABSENT -
    // it OVERWRITES whatever binding put there with empty. `Gateway:SecurityClientSecret` is a
    // bindable leaf (the environment provider folds `Gateway__SecurityClientSecret` onto it), so a
    // deployment can supply the credential by that route, watch the binder accept it, and be refused at
    // startup for presenting nothing - with a message naming the flat key it had deliberately not used.
    // A silently discarded input is worse than a rejected one: there is nothing to read that says the
    // value was dropped.
    //
    // THE SEMANTICS ARE THE SIBLINGS' SEMANTICS RATHER THAN A THIRD SET. Security's signing-key step
    // states the rule normatively - "when the key is ABSENT this step assigns nothing at all, so material
    // that reached the options instance through another legitimate ingress survives rather than being
    // overwritten with nothing" - and DataServices' ApplyIssuanceSecret is the same shape. All three
    // therefore express one idea once, rather than one idea with three different behaviours.
    //
    // PRECEDENCE IS EXPLICIT: the flat key WINS WHEN PRESENT. It is the documented route, it is the name
    // Security's issuance roster and orchestration/.env.example declare for this caller, and a deployment
    // that sets both has stated its intent through the channel both sides of the edge agree on. Blank and
    // whitespace count as absent, because neither can authenticate and accepting one would produce a 401
    // whose cause is invisible.
    //
    // NEITHER VALUE IS EVER LOGGED HERE. This step writes one property and returns; C-F's committed-file
    // prohibition is unaffected and is enforced separately by the estate-wide configuration coherence
    // test, which forbids a credential-named leaf in any settings file.
    .PostConfigure(options =>
    {
        string? configured =
            builder.Configuration[GatewayOptions.SecurityClientSecretConfigurationKey];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            options.SecurityClientSecret = configured;
        }
    })
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
// =================================================================================================
//  DATA PROTECTION IS EPHEMERAL BY DELIBERATE CHOICE, AND THE CHOICE IS ABOUT KEY MATERIAL AT REST.
//
//  AddAuthentication REGISTERS THE DATA-PROTECTION STACK WHETHER OR NOT ANYTHING PROTECTS A PAYLOAD -
//  Microsoft.AspNetCore.Authentication calls AddDataProtection for the ticket formats its remote
//  handlers use, and this service registers no remote handler. DataProtection's own eager initialiser
//  then materialises a key ring during host start. That was MEASURED on all four services rather than
//  inferred: each wrote a key file into its user profile at startup, and one that afterwards failed to
//  bind its port had ALREADY written it. Left at the default the ring is an unencrypted private key
//  under the process's user profile - observed at '/root/.aspnet/DataProtection-Keys' - created per
//  container and shared with nothing, which the framework itself warns about for a container.
//
//  NOTHING IN THIS SERVICE PROTECTS A PAYLOAD. Inbound authentication is bearer-token validation
//  against Security's published verification material, which is stateless and uses no protector; there
//  is no cookie, no session, no antiforgery token and no protected payload that outlives a request. The
//  default therefore writes key material to disk for NO CONSUMER - a secret at rest with no purpose,
//  and a secret at rest with no purpose is the one shape the secrets mandate has no tolerance for.
//
//  EPHEMERAL IS THE HONEST POSTURE, AND ITS FAILURE MODE IS WHY. Keys live in this process and die with
//  it, nothing reaches the filesystem, and a future capability that DOES need a durable protector
//  fails immediately and visibly on the first restart - instead of working on one replica and failing
//  on the next, which is the strictly worse of the two failures the default offers. Persisting the ring
//  instead would not remove the hazard: at-rest encryption of a persisted ring needs an X.509
//  certificate this deployment does not provision, DPAPI is Windows-only, and the target is Linux
//  containers - so persisting would relocate unencrypted key material rather than protect it.
//  docs/SECRETS.md section 5 records the posture and what a later phase must put in its place.
//
//  THE PROVIDER SWAP ALONE WAS NOT ENOUGH, AND THAT WAS MEASURED. Replacing IDataProtectionProvider with
//  the ephemeral one leaves the KEY-MANAGEMENT stack untouched, and data protection's eager initialiser
//  warms THAT rather than whichever provider is registered - so a host wired that way still wrote a key
//  file to the user profile on every start. The repository is therefore what is redirected: with an
//  in-memory IXmlRepository there is no file-system repository to construct, so the ring is created in
//  this process and NOTHING reaches the disk. One mechanism, at the layer that decides where bytes go.
//
//  THIS IS NOT A BEHAVIOUR CHANGE UNDER C-B. There is no legacy analogue to preserve or to break: the
//  key ring is an artifact of the ASP.NET Core hosting choice this refactor introduced, and the legacy
//  framework - a library with no process of its own - has nothing that corresponds to it.
// =================================================================================================
builder.Services
    .AddDataProtection();

// The key ring lives in memory, so the eager initialiser's key is created HERE rather than in a file.
builder.Services.Configure<KeyManagementOptions>(static options =>
    options.XmlRepository = new InMemoryDataProtectionKeyRepository());

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtBearerVerificationOptions>, InternalTlsTrust>(
        static (bearer, verification, trust) =>
    {
        JwtBearerVerificationOptions configured = verification.Value;

        // THE KEY-SET BACKCHANNEL IS AN INTERNAL CHANNEL TOO, AND IT IS THE MOST CONSEQUENTIAL ONE.
        // The handler fetches Security's discovery document and published key set over its own
        // HttpClient, which is built from this handler rather than from any registration above - so an
        // anchor applied everywhere else and not here would leave the one channel that decides WHICH
        // KEYS SIGN A VALID TOKEN unable to connect. It is supplied only when an anchor is configured,
        // so an unset anchor leaves the handler's own default backchannel untouched.
        if (trust.IsPinned)
        {
            SocketsHttpHandler backchannel = new();

            trust.Apply(backchannel);

            bearer.BackchannelHttpHandler = backchannel;
        }

        bearer.Authority = configured.Authority;
        bearer.RequireHttpsMetadata = configured.RequireHttpsMetadata;
        bearer.MapInboundClaims = configured.MapInboundClaims;

        // 🔴 BOTH KEY-SET REFRESH INTERVALS ARE ASSIGNED, BECAUSE LEAVING EITHER UNASSIGNED IS A
        // ROTATION DECISION TAKEN BY OMISSION.
        //
        // Saying nothing here does not mean "refresh promptly" - it means the library's defaults, and the
        // two defaults fail in OPPOSITE directions at once. RefreshInterval defaults to five minutes, so a
        // token minted after Security rotates its key is refused 401 (IDX10503, no key matched the
        // identifier) for up to that long even though the handler asked to refresh the moment it saw the
        // unknown key identifier. AutomaticRefreshInterval defaults to TWELVE HOURS, and it is the only
        // thing that ever drops a RETIRED key, because a successful validation provokes no refresh - so a
        // token signed by the superseded key stays acceptable here for half a day. Rotation therefore
        // inverted this boundary's verdicts: the old credential worked and the new one did not.
        //
        // Both values come from configuration and both are validated at startup against the library's own
        // published minimums, so a deployment can tune the trade - a shorter floor converges faster but is
        // also the only rate limit on the fetch a forged key identifier can provoke - without being able
        // to pick a value the configuration manager would reject on the first authenticated request.
        // docs/ARCHITECTURE.md section 9.6 tabulates the two intervals and docs/SECRETS.md section 4.2.1
        // carries the rotation runbook, including the reason an overlapping key set is not the answer here
        // (AAP 0.6.6.3 fixes exactly one signing secret in the estate).
        bearer.RefreshInterval = configured.MetadataRefreshInterval;
        bearer.AutomaticRefreshInterval = configured.MetadataAutomaticRefreshInterval;

        // 🔴 THE REFUSAL RECORDS, WITHOUT WHICH THIS BOUNDARY ENFORCED CORRECTLY AND SILENTLY.
        //
        // Every 401 and every 403 this service answered produced NO operator record in any shipped
        // logging profile: the framework's own records sit at Information under `Microsoft.AspNetCore.*`
        // and every profile here caps that category at Warning, so a measured probe - no credential, a
        // forged signature, and an authenticated caller without the entitlement - produced not one line
        // for any of the three. Credential stuffing against this ingress would have looked exactly like
        // no traffic at all. Authorization/AuthenticationRefusalRecord.cs carries what a record may
        // contain and why the failure event deliberately writes nothing of its own.
        AuthenticationRefusalRecord.Attach(bearer);

        TokenValidationParameters parameters = bearer.TokenValidationParameters;

        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidateLifetime = true;
        parameters.ValidateIssuerSigningKey = true;

        // CLOCK SKEW IS BOUNDED AND NOT CONFIGURABLE, AND ITS ABSENCE HERE WAS THE WORST PLACE IN THE
        // SYSTEM FOR IT.
        //
        // Leaving this unassigned does not mean "no tolerance" - it means the library's default of FIVE
        // MINUTES, which silently extends every token's usable life by that much. Security issues with a
        // five-minute lifetime, so the default made a token accepted here usable for twice as long as it
        // claims to be, and `ValidateLifetime = true` two lines above was enforcing a bound five times
        // looser than the one it appears to enforce.
        //
        // THE INCONSISTENCY WAS THE FINDING RATHER THAN THE VALUE. Four boundaries validate tokens from
        // one issuer and, before this assignment, all four disagreed: Security refused an expired token
        // at the instant it lapsed (`TimeSpan.Zero`, correct for a service validating only what it just
        // minted on its own clock), DataServices allowed thirty seconds, and Gateway and Persistence
        // inherited five minutes by saying nothing. GATEWAY IS THE SOLE INGRESS - the only boundary an
        // external client can reach - so the loosest tolerance in the system sat on the one edge facing
        // the untrusted network, and a token Security itself would refuse was still admitted here and
        // then forwarded inward. That is the same expired credential being accepted or refused depending
        // only on which door it arrives at.
        //
        // THIRTY SECONDS, MATCHING DataServices AND Persistence VERBATIM. It absorbs ordinary clock
        // drift between containers on one host - the topology the frozen environment describes - without
        // meaningfully widening the window, and Security mints with a truncated whole-second timestamp
        // so no sub-second allowance is required either. The value is a CONSTANT for the same reason the
        // four checks above are: a configurable tolerance is lifetime validation switched off by another
        // name, since nothing would stop a deployment setting it past the token lifetime, at which point
        // the expiry check does not expire. docs/ARCHITECTURE.md states all four boundaries in one
        // place, so the next reader compares them without opening four files.
        parameters.ClockSkew = TimeSpan.FromSeconds(30);

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

// 🔴 AND THE THIRD DURATION, WHICH THE TWO ABOVE DO NOT BOUND: HOW LONG A SUPERSEDED KEY SET STAYS
// ACCEPTABLE AS A LAST-KNOWN-GOOD FALLBACK.
//
// MEASURED, NOT ASSUMED. With both intervals above configured, a token signed by a RETIRED key was still
// accepted at this boundary NINE AND A HALF MINUTES after the rotation - well past the five-minute
// background refresh that had already replaced the current configuration. The cause is
// BaseConfigurationManager's last-known-good CACHE: when validation fails against the current
// configuration the token handler retries against recently-good ones, and those entries live for
// LastKnownGoodLifetime, which defaults to ONE HOUR. Two separately retired identities were both still
// honoured, so it is a cache of several rather than a single previous configuration.
//
// WHY THIS IS BOUNDED RATHER THAN TURNED OFF. UseLastKnownGoodConfiguration is left at its default of
// true on purpose: it is what keeps this boundary validating tokens through a transient inability to
// FETCH the key set, which is an availability property worth having. What is not worth having is a
// retired credential honoured for an hour, which is exactly the window rotation exists to close. So the
// lifetime is bounded to the background refresh interval - after one refresh cycle a superseded set is
// gone - and the value is DERIVED from that interval rather than made a third knob, so the two cannot
// drift into an incoherent pair.
//
// SET ON THE MANAGER THE FRAMEWORK BUILT, IN A POST-CONFIGURE THAT RUNS AFTER IT. The handler's own
// post-configure step is what constructs the configuration manager from the authority, so the manager
// does not exist yet while the Configure delegate above runs. Reaching it here keeps metadata retrieval
// entirely framework code - nothing in this repository fetches a key set by hand - which is the property
// AAP 0.6.6.3's sole-issuer topology depends on.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .PostConfigure<IOptions<JwtBearerVerificationOptions>>(static (bearer, verification) =>
    {
        if (bearer.ConfigurationManager is BaseConfigurationManager manager)
        {
            manager.LastKnownGoodLifetime = verification.Value.MetadataAutomaticRefreshInterval;
        }
    });

// A FALLBACK POLICY, so that no route is ever authenticated by omission (constraint C-G). Every
// endpoint file already declares RequireAuthorization explicitly, and this makes the absence of such
// a declaration a closed door rather than an open one. The two deliberate exceptions below opt out
// in the one place a reader looks for them.
//
// AN AUTHENTICATED CALLER IS THE FALLBACK'S WHOLE REQUIREMENT, AND IT IS NOT THIS SERVICE'S WHOLE
// REQUIREMENT. Every protected route additionally names a SCOPE policy, registered just below by
// AddScopeAuthorization() - so the fallback is the floor for a route that declared nothing, not the
// ceiling for the routes that did.
//
// THE ARGUMENT AGAINST A SCOPE CHECK IS RECORDED RATHER THAN OMITTED, BECAUSE IT IS WRONG IN A WAY
// THAT COSTS SOMETHING. It runs: Gateway is the INGRESS, nothing inside the system calls it, so it has
// no internal caller roster to check against; the published document applies `bearerAuth` with an EMPTY
// scope array throughout and declares no per-operation scope; and the 403 it declares reads as a RELAY
// of a downstream PermissionDenied. Two of those three premises are true and the conclusion still does
// not follow:
//
//   * AN EMPTY ARRAY UNDER A BEARER SCHEME CARRIES NO SCOPE INFORMATION. OpenAPI defines the
//     security-requirement array as a scope list for `oauth2` and `openIdConnect` schemes only, so for
//     an `http`/`bearer` scheme an empty array is the sole meaningful value. Reading it as "no scope
//     required" is an inference from a field that cannot say otherwise.
//   * WITHOUT A SCOPE CHECK, ONE TOKEN REACHES EVERYTHING. Any token minted for this service's
//     audience would open /v1/ping, /v1/capabilities and all thirty-nine /v1/datawindow projections
//     alike, the issuance roster's per-identity least privilege would be enforced nowhere in this
//     service, and the 403 the contract declares would be unreachable at the one boundary external
//     clients can reach.
//
// The document states the requirement machine-readably as `x-required-scope` on every operation -
// ping, capabilities, datawindow, and `none` for the anonymous probe and the eight reserved routes -
// so the contract and this composition root are checkable against each other rather than merely
// consistent-sounding. Audience validation above still does its own containment work, and Security
// still pre-grants no caller the gateway audience; both remain true, and neither is load-bearing on its
// own once the scope policies below are in force.
//
// Expressed through AddAuthorizationBuilder rather than AddAuthorization(options => ...) because the
// ASP.NET Core analyzers direct the builder form for exactly this shape (ASP0025); the registration
// and the resulting policy are identical, so this is a spelling decision and not a behavioural one.
//
// AND THREE NAMED SCOPE POLICIES, BECAUSE A FALLBACK POLICY IS NOT AN ENTITLEMENT CHECK. A fallback
// alone requires only that the caller be AUTHENTICATED, which every token this system mints for
// Gateway's audience is - so one token would reach /v1/ping, /v1/capabilities and all thirty-nine
// /v1/datawindow operations alike. The issuance roster states least privilege per calling identity, and
// without these policies no surface in this service would enforce it and the 403 the contract declares
// would be unreachable.
//
// EACH POLICY NAME AND ITS REQUIRED SCOPE ARE DECLARED BY THE ROUTE THAT NEEDS THEM. This file reads
// those constants, so a route and its requirement keep ONE spelling. The direction matters: a route
// requiring a policy nobody registered fails closed and loudly on the first request, while a policy
// registered under a name no route requires enforces nothing at all and is the half that looks correct
// in review.
//
// THE FOUR RESERVED /v1/{design,documents,integration,scripting} FAMILIES DELIBERATELY KEEP THE
// PARAMETERLESS FORM (decision D5, Endpoints/DeferredCapabilityEndpoints.cs). Requiring a capability
// scope for a route that reaches no capability would invent an entitlement for a service this phase
// must not implement even in metadata (constraint C-D), and the roster grants no such scope to anyone.
// Authenticated-only is exactly the posture those routes need: the reserved roster is not anonymously
// enumerable, and every caller who is authenticated receives the same 501.
//
// EVERY POLICY REQUIRES AN AUTHENTICATED PRINCIPAL AS WELL AS THE SCOPE, so each is correct in
// isolation rather than correct by virtue of the fallback above.
// THE SCOPE HANDLER AND ITS PER-SCOPE POLICIES, REGISTERED FROM Authorization/ScopeAuthorization.cs.
// ONE registration path, and the policy names are the SCOPE NAMES themselves - "ping", "capabilities",
// "datawindow" - because that is what each endpoint passes to RequireAuthorization, reading it from
// GatewayScopes so a route and its policy keep one spelling.
//
// 🔴 A SECOND, PARALLEL FAMILY MUST NOT BE REGISTERED HERE, AND NOTHING REQUIRES ONE. The shape that
// arrives when two independent remediations of the same finding reach this composition root is this
// declarative requirement plus handler ALONGSIDE inline assertion policies registered under
// `gateway:scope:<name>` names taken from per-endpoint ScopePolicyName constants. Such a merge is
// half-done by construction - the endpoints name the bare scope policies, so the `gateway:scope:*`
// policies are required by NO route and enforce nothing, which is precisely the failure mode this file
// warns about two paragraphs down and the half that looks correct in review. They also carry a SECOND
// implementation of "is this scope granted", duplicating ScopeHandler's clause for clause; two copies of
// an authorization predicate can diverge, and only one of them is reachable. Exactly one family is
// registered here: the one the routes actually require.
//
// AUTHENTICATION IS NOT AUTHORIZATION, AND THE FALLBACK BELOW ONLY DELIVERS THE FIRST. This service's
// published contract declares a 403 on forty-one operations whose shared description says the token is
// valid but does not carry the scope the operation requires, "deliberately distinguished from 401 so a
// caller can tell a missing credential from an insufficient one". Until these policies existed nothing
// here read the scope claim, so every authenticated route was reachable by any token addressed to this
// service and the published 403 was unreachable - at the one boundary in the whole system that external
// clients can reach.
//
// The vocabulary, the reason the ingress names capabilities while every internal service namespaces its
// scopes by service, the reason the framework's own claim requirement cannot express the check (the claim
// is ONE value carrying a SPACE-DELIMITED set), and the two surfaces that deliberately carry NO scope
// requirement are all recorded in Authorization/ScopeAuthorization.cs.
//
// CALLED EXACTLY ONCE, AND THAT IS A CORRECTNESS PROPERTY RATHER THAN TIDINESS. The helper registers its
// handler with AddSingleton, not TryAddSingleton, so a second call adds a SECOND ScopeHandler to the
// container: the framework resolves every registered handler for a requirement and invokes each, so every
// scope decision in the service would be evaluated twice - two log records for one decision, and two
// chances for a future handler edit to diverge from its own duplicate. The two convergent remediations
// described above each added this line, and this is the merged single registration they intended.
builder.Services.AddScopeAuthorization();

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

// THE TRUST HALF OF THE SAME STORY, AND IT IS THE HALF THAT WAS MISSING. The client identity above
// says which certificate Gateway PRESENTS; this says which authority Gateway ACCEPTS. Both upstreams
// terminate TLS with certificates issued by the local authority docs/ARCHITECTURE.md 9.3.1 generates,
// and that authority is in no container's OS trust store - so without this every outbound channel
// rejects the certificate the documented topology presents. Registered as a singleton for the same
// reason as the identity: the handler factories recycle handlers on a schedule, and the anchor must be
// read once rather than per rotation.
builder.Services.AddSingleton(static serviceProvider => new InternalTlsTrust(
    serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value.InternalTls));

// THE OUTBOUND DEADLINES, DERIVED ONCE FROM THE SAME SETTING THE PIPELINES BELOW ARE CONFIGURED FROM.
// A gRPC call carrying no deadline lets the upstream keep working - and keep the session or task behind
// that work alive - for as long as the transport looks open, which includes every case where the caller
// has already gone and the transport has not noticed. Registering the pair here rather than letting each
// client invent one is what makes the deadline the composition root's policy, which is precisely where
// DataServicesClient's own remark said such a policy belongs. A singleton because it holds two durations
// and a clock and nothing else.
builder.Services.AddSingleton(static serviceProvider =>
{
    GatewayOptions.OutboundCallOptions outbound =
        serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value.Outbound;

    return new OutboundDeadlines(
        outbound.RequestTimeout,
        outbound.StreamDeadline,
        serviceProvider.GetRequiredService<TimeProvider>());
});

// A NAMED CLIENT RATHER THAN A TYPED ONE, AND THE LIFETIME IS THE WHOLE REASON. The generic overload
// registers the client type TRANSIENT, so every resolve produced a fresh client with a fresh credential
// store - which meant the "reuse a held token until it lapses" path was never reached twice in a running
// host and Gateway asked Security to mint a token for every request that needed one. Naming the client
// leaves this registration owning the address, the trust anchor, the client identity and the resilience
// pipeline, while the scoped registration below owns the object's lifetime so that one request reaches
// one client. The name is an explicit constant rather than the factory's derived type name, because a
// resolution under an unregistered name silently yields a default-configured client with none of the
// above - the same class of fault that left the health probe on platform trust.
builder.Services
    .AddHttpClient(SecurityClient.HttpClientName, static (serviceProvider, httpClient) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        httpClient.BaseAddress = new Uri(options.Upstreams.Security, UriKind.Absolute);
    })
    .ConfigurePrimaryHttpMessageHandler(static serviceProvider =>
    {
        // SERVER-CERTIFICATE VALIDATION IS NARROWED HERE, NEVER RELAXED, AND NEVER BY A CALLBACK. A
        // forged Security service would be a forged token issuer for the whole system, so this handler
        // pins the acceptable root to the anchor the deployment mounts and rejects every other root for
        // internal traffic. Chain building, name validation and validity dates remain the platform's.
        // There is no RemoteCertificateValidationCallback, no
        // ServerCertificateCustomValidationCallback, no DangerousAcceptAnyServerCertificate and no
        // environment-conditional relaxation anywhere in this file (constraint C-G). With no anchor
        // configured the platform's default trust decision stands unmodified.
        SocketsHttpHandler handler = new();

        serviceProvider.GetRequiredService<InternalTlsTrust>().Apply(handler);

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
        serviceProvider.GetRequiredService<TimeProvider>(),
        serviceProvider.GetRequiredService<IOptions<GatewayOptions>>(),
        serviceProvider.GetRequiredService<ServiceTokenCache>()))
    .AddStandardResilienceHandler()
    // SECURITY IS THE ONE EDGE WITH A GENUINE READ/WRITE SPLIT TO MAKE, because it is reached over REST
    // and its methods are therefore distinguishable: the verification-material fetch is a GET and is
    // safely retryable, while token issuance is a POST and is not replayed. A transport failure does not
    // reveal whether the server processed the request, and "minting a second token is probably harmless"
    // is not a basis for replaying a credential-issuing call whose outcome is unknown.
    //
    // THAT SPLIT IS MADE INSIDE THE ONE PREDICATE RATHER THAN BY A SECOND, VERB-ONLY GATE, and the
    // difference is not stylistic. `Retry.DisableForUnsafeHttpMethods()` REPLACES `Retry.ShouldHandle`
    // with a wrapper of its own, so registering it after ConfigureOutboundResilience would leave every
    // pipeline carrying the wrapper instead of the policy - and on the two gRPC channels, where gRPC
    // transports every call as a POST, that wrapper is an all-or-nothing disable that additionally loses
    // the gRPC-status reading the policy exists for. OutboundCallPolicy.IsReplaySafe therefore admits on
    // the RFC 9110 safe methods as well as on the classified operation paths, which reproduces the verb
    // gate's decision exactly on this REST channel and reproduces nothing at all on the gRPC channels,
    // where it is unreachable.
    .Configure(ConfigureOutboundResilience);

// THE CREDENTIAL STORE IS A SINGLETON, AND NAMING IT HERE IS WHAT MAKES THE CACHING REAL. The store holds
// no connection and no handler - only short-lived tokens keyed by audience and scope - so unlike the
// client itself it is safe to keep for the life of the process, and it has to be kept for that long or
// the reuse path is never reached twice.
builder.Services.AddSingleton<ServiceTokenCache>();

// THE CLIENT IS SCOPED, which is what makes one request reach one client and one credential rather than
// re-minting per resolve. Scoped rather than singleton because the HttpClient the factory hands it is
// meant to be short lived and its handler is recycled on a schedule; scoped rather than transient because
// the request is the unit the credential is used within, and it matches DataServicesClient's own lifetime
// below so the client that obtains the token and the client that uses it belong to the same request.
//
// THE CONSTRUCTOR IS NAMED RATHER THAN ACTIVATED, AND IT HAS TO BE. SecurityClient publishes two public
// constructors - one taking the clock and one defaulting it - so that it resolves whether or not a
// TimeProvider has been registered. Handing the activator an HttpClient makes both constructors equally
// good matches and it then demands exactly one, so an activated resolve throws "Multiple constructors
// accepting all given argument types have been found" on the first token request. Naming the constructor
// removes the ambiguity, and names the widest one so the determinism seam and the shared credential store
// are genuinely engaged rather than silently defaulted.
builder.Services.AddScoped(static serviceProvider => new SecurityClient(
    serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(SecurityClient.HttpClientName),
    serviceProvider.GetRequiredService<ILogger<SecurityClient>>(),
    serviceProvider.GetRequiredService<TimeProvider>(),
    serviceProvider.GetRequiredService<IOptions<GatewayOptions>>(),
    serviceProvider.GetRequiredService<ServiceTokenCache>()));

builder.Services.AddScoped<IServiceTokenProvider>(static serviceProvider =>
    serviceProvider.GetRequiredService<SecurityClient>());

builder.Services
    .AddGrpcClient<DataWindowService.DataWindowServiceClient>((serviceProvider, grpcOptions) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
        ApplyGrpcRetry(grpcOptions.ChannelOptionsActions, options.Outbound);
    })
    .ConfigurePrimaryHttpMessageHandler(CreateInternalChannelHandler)
    .AddStandardResilienceHandler()
    .Configure(ConfigureGrpcOutboundResilience);

builder.Services
    .AddGrpcClient<ColumnExpressionService.ColumnExpressionServiceClient>(
        (serviceProvider, grpcOptions) =>
        {
            GatewayOptions options =
                serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

            grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
            ApplyGrpcRetry(grpcOptions.ChannelOptionsActions, options.Outbound);
        })
    .ConfigurePrimaryHttpMessageHandler(CreateInternalChannelHandler)
    .AddStandardResilienceHandler()
    .Configure(ConfigureGrpcOutboundResilience);

builder.Services.AddScoped<DataServicesClient>();

// THE READINESS-PROBE CHANNEL, REGISTERED RATHER THAN IMPLIED. Endpoints/HealthEndpoints.cs asks the
// factory for a client under this name; an unregistered name yields a default-configured client, which
// is how the probe silently ended up on platform default trust while the two functional channels above
// were being discussed. Registering it by name puts the probe on the same anchor as everything else,
// which matters because a probe that cannot complete a handshake reports Unreachable and Gateway then
// never opens its own readiness gate.
builder.Services
    .AddHttpClient(HealthEndpoints.ProbeHttpClientName)
    .ConfigurePrimaryHttpMessageHandler(CreateInternalChannelHandler);

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

// THE CUSTOMIZATION IS WHAT MAKES A FRAMEWORK-GENERATED BODY A CONTRACT-SHAPED ONE. The problem
// responses this service writes itself - the proxy projection's, the readiness endpoint's, the system
// error handler's - carry `retCode` because the code that writes them sets it. The bodies the FRAMEWORK
// writes carry none: the bearer challenge, the authorization refusal, the unmatched route and the
// rejected method are all produced beneath any of this service's own code, and gateway.v1.yaml declares
// that every 4xx and every 5xx response uses the one problem shape so that a consumer writes one error
// handler. Filling the member here is what makes that true of all of them rather than of the subset this
// codebase happens to write by hand.
//
// ONLY `retCode` IS FILLED, AND `traceId` IS DELIBERATELY LEFT TO THE FRAMEWORK. The problem-details
// writer supplies the correlation identifier itself, from the ambient activity and falling back to the
// request identifier, for every body it writes - hand-written and framework-generated alike. Stamping it
// here as well was measured to change nothing: removing the stamp left every assertion about the member
// passing, because the writer had already put it there. Code whose removal is undetectable is not
// defence in depth, it is a second implementation of a rule with no way to tell which one is in force.
//
// The guard on `retCode` is not an optimization. A response this service composed itself has already
// resolved its own originating return code - the projection forwards the UPSTREAM's code, which is more
// specific than anything derivable from a status - so overwriting it would replace a precise value with a
// derived one.
builder.Services.AddProblemDetails(static options =>
    options.CustomizeProblemDetails = static context =>
    {
        // THE CORRELATION IDENTIFIER IS ADDED TO EVERY PROBLEM BODY, INCLUDING ONE AN ENDPOINT COMPOSED
        // ITSELF - which is why it sits ABOVE the retCode guard rather than below it. The guard returns
        // early for a body that already carries retCode, so anything written after it would be skipped
        // for exactly the bodies this service authored by hand.
        //
        // The value is resolved the same way in every service that publishes it: the current Activity's
        // id when there is one - which there is on every request, because the host starts an Activity and
        // continues an inbound W3C `traceparent` - and the host's own request identifier otherwise. The
        // presence guard keeps a hand-written path that already set the member authoritative, and keeps an
        // empty member out of the body: a `traceId` with no value advertises a bridge with no far side.
        //
        // The published problem schema sets `additionalProperties: true` and states that a consumer must
        // ignore members it does not recognise, so this adds a member without widening any contract.
        if (!context.ProblemDetails.Extensions.ContainsKey(ProblemContractMembers.TraceId))
        {
            string correlationId = Activity.Current?.Id
                ?? context.HttpContext.TraceIdentifier
                ?? string.Empty;

            if (!string.IsNullOrEmpty(correlationId))
            {
                context.ProblemDetails.Extensions[ProblemContractMembers.TraceId] = correlationId;
            }
        }

        if (context.ProblemDetails.Extensions.ContainsKey(ProblemContractMembers.RetCode))
        {
            return;
        }

        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

        context.ProblemDetails.Extensions[ProblemContractMembers.RetCode] = ClassifyFailure(status);
    });

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

// ==================================================================================================
//  CONTENT NEGOTIATION ON THE RESPONSE - THE ONE HTTP MECHANISM THIS INGRESS WAS NOT HONOURING
// ==================================================================================================
//
//  WHY THIS IS NOT THE BEHAVIOUR IMPROVEMENT CONSTRAINT C-B FORBIDS, stated first because the previous
//  reading of that constraint had it the other way round and this file said so. C-B freezes LEGACY
//  behaviour. There is no legacy behaviour here to freeze: the legacy is an in-process library that hands
//  a DataWindow carrier over BY POINTER, opens no listening socket and composes no response at all
//  [AAP 0.1.5]. The HTTP response representation is surface the decomposition created from nothing, in
//  exactly the same way the JWT bearer requirement on every internal edge is - and C-B cannot be read to
//  freeze net-new surface at its first draft, or the resilience handlers the AAP itself justifies would be
//  forbidden too. What C-B does forbid, and what is not done here, is changing a ported behaviour: not one
//  byte of any decoded body differs, because a caller that advertises no encoding receives exactly the
//  bytes it received before.
//
//  MEASURED RATHER THAN ASSUMED. A full retrieval projection of 50,008 rows answered 73,239,527 bytes with
//  `content-encoding` NULL and no `Vary`, for every one of six accept-encoding combinations including
//  `gzip` and `gzip, br, deflate, zstd`; at 100,009 rows the body was 146,680,012 bytes. The client had
//  ASKED and was ignored. Transfer is chunked, so this was never a memory exposure on either side - it is
//  a client's stated capability being discarded.
//
//  NO PERFORMANCE OBJECTIVE IS ASSERTED HERE, AND NONE MAY BE (AAP 0.8.5). No target ratio, no latency
//  budget and no throughput claim appears in this file, in the documentation or in a test. Nothing is
//  tuned either: both providers keep their framework compression levels, because choosing one would be
//  making exactly the tuning claim the AAP forbids. What is asserted is only that a stated capability is
//  honoured.
//
//  NO PACKAGE IS ADDED (AAP 0.5.3). Response compression ships in the Microsoft.AspNetCore.App shared
//  framework, so the deliberately-excluded-package list is untouched.
//
//  THE THREE NARROWINGS, EACH LOAD BEARING:
//
//  1. THE MIME ALLOWLIST IS REPLACED, NOT EXTENDED. The framework's default set covers text/plain,
//     text/css, text/html, application/javascript, text/xml and more - none of which this service serves,
//     and text/html in particular is the shape a compression side-channel is classically demonstrated on.
//     Only the two media types this ingress actually produces are listed. The generated OpenAPI document
//     is served as application/json and is therefore included, which is correct: it is a description of a
//     public contract that already lives in the repository.
//
//  2. HTTPS IS OPTED INTO DELIBERATELY, AND THE BREACH QUESTION IS ANSWERED RATHER THAN WAVED AWAY. The
//     framework defaults this OFF because compressing a TLS body that mixes attacker-influenced input with
//     a SECRET leaks the secret through response length. Neither half holds here: no response this service
//     composes carries a session cookie, a bearer token or key material - Gateway sets no Set-Cookie
//     anywhere and never echoes an Authorization value - and the bodies at issue carry the caller's OWN
//     rows, which it already has. Security is the service whose bodies DO carry token and key material,
//     and it is deliberately left uncompressed for precisely this reason; that asymmetry is the point of
//     answering the question per service instead of once.
//
//  3. BROTLI AND GZIP ONLY. Both are in the shared framework. Deflate is not offered separately because
//     gzip subsumes it for every client that asks for either, and zstd has no framework provider - a
//     caller advertising it simply negotiates one of the two below, which is what content negotiation is
//     for.
builder.Services.AddResponseCompression(static options =>
{
    options.EnableForHttps = true;

    options.MimeTypes =
    [
        "application/json",
        "application/problem+json",
    ];
});

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

// Resolved eagerly for exactly the same reason as the client identity above, and it is the other half
// of the same fault. A configured-but-unreadable trust anchor means this deployment intended to pin
// internal trust and cannot, so every outbound channel it opens will refuse the certificate it is
// handed - discovering that at startup is a failure to launch, whereas discovering it at the first
// token request is an outage that looks like an upstream problem. An UNSET anchor resolves to platform
// default trust and is not a fault.
_ = app.Services.GetRequiredService<InternalTlsTrust>();

// Resolved eagerly so that a deadline pair which cannot be constructed - a non-positive duration that
// slipped past validation - is a failure to launch rather than an exception on the first outbound call.
_ = app.Services.GetRequiredService<OutboundDeadlines>();

// FORCED HERE FOR THE SAME REASON, and this one matters more than it looks. The retry classification is
// built from the contract descriptors in a static initializer, so a method name that no longer resolves
// would otherwise throw at the first outbound FAILURE - the one moment when a second, unrelated fault is
// hardest to diagnose. Touching it now turns a contract-versus-classification mismatch into a refusal to
// start.
_ = OutboundCallPolicy.Verify();

// THE PROTECTIVE RESPONSE HEADERS, INSTALLED FIRST SO THEY REACH EVERY RESPONSE. It is registered ahead of
// the exception handler and of authentication deliberately: it works by registering a response-starting
// callback rather than by writing headers itself, so being outermost is what lets it cover a problem
// document the exception handler writes and a bodiless challenge the authentication middleware writes, as
// well as a handler's own response. It overrides nothing a route set for itself - see the file's own banner
// for the three directives and the reason for each.
SecurityResponseHeaders.Use(app);

// IMMEDIATELY INSIDE THE HEADER MIDDLEWARE AND OUTSIDE EVERYTHING ELSE, which is the only position that
// works. Compression must wrap every middleware that can write a body - the exception handler's problem
// document, the status-code pages' problem document, the bearer challenge and every mapped endpoint - and a
// middleware placed after any of them cannot compress what they wrote. It stays INSIDE the response-header
// middleware because that one works by registering a response-starting callback rather than by writing
// bytes, so the two do not contend, and the protective headers must remain the outermost thing on the
// response. See the registration above for why this is honoured content negotiation rather than the
// behaviour improvement C-B forbids.
app.UseResponseCompression();

app.UseExceptionHandler();

// STATUS-CODE PAGES, AND THE REASON IS CONTRACT FIDELITY RATHER THAN HARDENING. Without it the framework
// answers a bare status with NO BODY on every path that never reaches this service's own code: the bearer
// challenge, the authorization refusal, an unmatched route and a rejected method. gateway.v1.yaml declares
// reusable Unauthorized, Forbidden and NotFound responses whose bodies are ProblemDetails, and the routes
// themselves declare ProducesProblem for 401, 403 and 404 - so a bodyless response is a published promise
// this service was not keeping. This middleware writes the missing body through the problem-details
// service configured above, which is why the members that customization fills reach these responses too.
//
// It is a narrow exception to the no-unrequested-middleware rule (constraint C-B): the requirement that
// creates it is the published contract, and no CORS and no output caching are added here. Response
// compression IS registered, and the long note on its registration above is why: it honours a capability
// the client states on the request rather than adding one to a ported behaviour, and no legacy behaviour
// exists on this net-new surface for it to change.
// It is ordered with UseExceptionHandler and BEFORE authentication for the reason both diagnostics
// middlewares share: each works by observing what the middlewares beneath it produced, and the response
// they most need to observe is the challenge the authentication middleware writes.
app.UseStatusCodePages();

app.UseAuthentication();

// THE REQUEST-LAYER INGRESS BOUND, AND ITS POSITION IS THE WHOLE REASON IT IS HERE RATHER THAN OUTERMOST.
// Its per-caller partition is the AUTHENTICATED PRINCIPAL, which does not exist until the line above has
// run - and partitioning on the source address instead would put an entire upstream service in one bucket,
// because inside a container network every request from one peer shares one address. What that ordering
// leaves unbounded is the work done BEFORE a caller is known, and the transport bounds registered in
// section 0 are what answer it: a connection ceiling, a header ceiling and a header-completion deadline
// all apply beneath every middleware, which is the only place a bound on unauthenticated work can live.
// It sits before UseAuthorization so that an authenticated caller flooding routes it is not granted is
// counted rather than being refused for free. /health is exempt - it is the readiness gate three
// dependents are held behind, so rate-limiting it would make a busy service a permanently unready one.
app.UseIngressHardening();

app.UseAuthorization();

// The published contract document, AUTHENTICATED like every other route that has not been granted an
// explicit exemption. Serving it anonymously is defensible on the argument that a description of a surface
// is not part of the surface, and that argument does not survive the AAP: the anonymous exceptions are
// enumerated and this is not among them - `/health` on all four services (C-10), and Security's key set and
// discovery document (C-01), and nothing else. The circularity worry behind it is not real either. A
// consumer learns how to authenticate from the AUTHORED contract at
// shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml, which is a file in the repository and needs no
// credential to read; this route serves a GENERATED projection of that same document, so putting it behind
// the boundary withholds nothing a consumer needs in order to obtain a credential. Security - the token
// issuer itself, where the circularity argument would be strongest of all - already publishes its document
// behind its own boundary, and this brings Gateway into line with it. No AllowAnonymous call: the fallback
// policy from section 5 applies, so an anonymous request is answered 401 (constraint C-G).
app.MapOpenApi();

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

// Classifies a framework-generated failure status as a legacy return code, so that a body this
// service did not compose still carries the member the published contract declares.
// statusCode: The status the framework is answering with.
// The legacy code for that status, and RetCode.UNKNOWN for anything unclassifiable.
// EVERY ARM IS WRITTEN OUT RATHER THAN DERIVED FROM A TRUTHINESS TEST. The codes are taken from the
// published contract's own response catalogue: a malformed request is E_INVALID_ARGUMENT, a
// refused caller is E_ACCESS_DENIED whether the refusal was authentication or authorization, an
// unmatched route is E_OBJECT_NOT_FOUND, a rejected method is E_NO_SUPPORT, and a reserved
// extension point is E_NO_IMPLEMENTATION - the same value
// Endpoints/DeferredCapabilityEndpoints.cs writes by hand, so the two agree.
// <b>429 IS CLASSIFIED BECAUSE THIS SERVICE PRODUCES IT, AND IT WAS THE ONE PUBLISHED STATUS MISSING
// HERE.</b> Endpoints/DataServicesProxyEndpoints.cs declares it on every projected route and
// reaches it twice - from an upstream ResourceExhausted, and from an in-band E_BUSY outcome -
// so it is a status a caller genuinely receives. Without an arm it fell to UNKNOWN, which reports
// "unclassifiable" for a refusal this service classifies precisely everywhere else, and it broke the
// round trip: the in-band direction maps E_BUSY ONTO 429, so the reverse must map 429 back onto
// E_BUSY or the two directions disagree about the same event. It shares the code with 503 for the
// reason the two refusals share a nature - the request was declined because capacity was not available -
// and the statuses stay distinct so a caller can still tell a shed request from an unavailable upstream.
// THE REFUSAL AND THE FORBIDDEN CASE SHARE ONE CODE, AND THAT ASYMMETRY IS PRESERVED RATHER THAN PAPERED
// OVER. The legacy algebra declares exactly one access code and draws no distinction between "no
// credential" and "credential without permission". The HTTP statuses stay distinct, so a caller can still
// tell the two apart; the code simply does not gain a member the oracle never declared.
// THE GUARD IS THE POINT OF THE FINAL CHECK. The algebra is tri-state and has a documented hole:
// PREVENT is 1 and IsSucceeded tests greater-than-or-equal-to zero, so a prevention reads
// as a SUCCESS [ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13, retcode.sru:L42],
// and CANCELLED is excluded from IsFailed, so a cancellation is NEITHER
// [isfailed.srf:L11-L13]. An error body must never carry a code from either class, and the kernel
// predicate is CONSUMED to enforce that rather than the comparison being re-derived here. Every arm below
// already satisfies it; the check exists so a future edit introducing one that did not would degrade to
// UNKNOWN instead of publishing a failure a consumer's own predicate would read as a success.
static long ClassifyFailure(int statusCode)
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

// Selects the localization provider for a locale token, reproducing the three-way selection at
// ws_objects/pfw.pbl.src/pfw.sra:L95-L102.
// The configured locale token. GatewayOptions restricts it to en, chs or
// cht and validates that on start, so an unrecognised value cannot reach here through
// configuration.
// Returns: The provider for that locale.
// The token is not one of the three the legacy declares. Unreachable through configuration, and a
// hard failure rather than a silent default because a Gateway that quietly fell back to another
// locale would return text no caller asked for.
// Compared with StringComparer.Ordinal: these are legacy symbols rather than
// human-readable text, and no culture may participate in matching them. The Simplified Chinese
// provider is a genuine no-op because Simplified Chinese is the base locale, and it is registered
// anyway so that the three-provider shape of the legacy survives intact.
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

// Builds the primary handler for an outbound internal channel - the two gRPC clients and the
// readiness-probe client - with internal trust applied.
// serviceProvider: The provider the shared trust anchor is resolved from.
// Returns: A handler that verifies its peer against the mounted anchor when one is configured.
// ONE FACTORY FOR THREE CHANNELS, so all three demonstrably share the same trust decision. Three
// lambdas would let one drift during a later edit, and a channel that quietly keeps platform default
// trust is exactly the defect this prevents - a readiness probe with no registered client of its own
// silently uses a default-configured one.
// EnableMultipleHttp2Connections is set explicitly because supplying a primary handler replaces
// the one the gRPC client factory would otherwise build, and that one sets this property. Leaving it
// at its default would silently cap concurrent streams per connection at the peer's advertised limit
// and queue calls behind it - a behavioural change to the transport that has nothing to do with trust.
// The Security typed client does NOT come through here, and that is deliberate: it needs the client
// certificate as well as the anchor, so its handler is built at its own registration where both halves
// are in view.
static HttpMessageHandler CreateInternalChannelHandler(IServiceProvider serviceProvider)
{
    ArgumentNullException.ThrowIfNull(serviceProvider);

    SocketsHttpHandler handler = new() { EnableMultipleHttp2Connections = true };

    serviceProvider.GetRequiredService<InternalTlsTrust>().Apply(handler);

    return handler;
}

// Configures the HTTP-level resilience pipeline for a gRPC channel, WITH ITS RETRY DISABLED.
// resilience: The standard resilience options for this client.
// serviceProvider: The provider the bound options are read from.
// 🔴 EXACTLY ONE LAYER MAY RETRY A gRPC CALL, AND IT IS THE gRPC ONE.
// Both layers retrying the same failure MULTIPLIES rather than adds: with four attempts configured at
// each, one call against a failing upstream made SIXTEEN. That was measured, not predicted - the
// deployed-pipeline test counted them - and it is precisely the amplification the base-delay validation
// message warns about, aimed at an upstream that is by definition already struggling.
// THE gRPC LAYER IS THE RIGHT ONE TO KEEP because it is strictly better informed. It sees a connect
// failure, which the HTTP handler never does - the balancer establishes the connection outside the
// handler pipeline - AND it sees a status delivered in trailers, which is the only thing the HTTP layer
// could ever have acted on. Retrying at the HTTP layer as well adds no reachable failure mode.
// EVERYTHING ELSE IN THE PIPELINE IS KEPT: the circuit breaker with its own predicate, the per-attempt
// timeout and the total request timeout all still apply, and the breaker still protects the upstream
// from a caller that keeps trying. Only the retry strategy is stood down, and only on these two
// channels - the Security REST client is not a gRPC channel, has no service config, and keeps HTTP-level
// retry as its only mechanism, which is why OutboundCallPolicy.ShouldRetryAsync and its crypto
// path classifications remain live.
static void ConfigureGrpcOutboundResilience(
    HttpStandardResilienceOptions resilience,
    IServiceProvider serviceProvider)
{
    ConfigureOutboundResilience(resilience, serviceProvider);

    // A PREDICATE, NOT A COUNT: the package's validator requires MaxRetryAttempts to be at least one, so
    // "no retries" cannot be expressed as zero. The gRPC service config configured by ApplyGrpcRetry is
    // the single retry authority for these channels.
    resilience.Retry.ShouldHandle = OutboundCallPolicy.NeverRetryAtTheHttpLayerAsync;
}

// Attaches the gRPC-level retry configuration to a channel.
// channelActions: The channel-configuration actions the client factory will run.
// outbound: The bound outbound-call options.
// A SECOND RETRY LAYER, AND IT IS NOT REDUNDANT. The Polly pipeline configured by
// ConfigureOutboundResilience is attached to the HttpClient's message handler, and
// Grpc.Net establishes its connection in the balancer's subchannel transport - outside that handler. So
// the one failure the policy was taken for, "the upstream is down", never reached it. This layer runs
// inside the gRPC client, where the connect failure happens.
// The two layers are kept in agreement by construction rather than by review: the method roster and the
// retryable status set both come from
// OutboundCallPolicy.BuildRetryServiceConfig(int, TimeSpan), which reads the same
// definitions the HTTP-level predicate reads. Only replay-safe methods are named, so every
// state-advancing call stays single-attempt.
// ATTEMPTS ARE COUNTED INCLUSIVELY HERE. The Polly setting is a number of RETRIES; a gRPC retry policy
// takes a number of ATTEMPTS, so it is one greater. Getting that wrong would silently change the number
// of calls a replay-safe read makes. The increment is CHECKED - see
// GatewayOptions.OutboundCallOptions.ResolveGrpcAttemptCount for the negative count the
// unchecked form used to produce and every layer used to accept.
// 🔴 <b>A CONFIGURED ZERO INSTALLS NOTHING AT ALL, WHICH IS WHAT MAKES THE DISABLE REAL.</b> The setting
// was documented as disabling retry while this method consumed it unconditionally: zero produced
// MaxAttempts = 1, which the gRPC retry policy rejects, so the one value an operator would reach
// for to turn retry off could not be deployed. Returning early leaves ServiceConfig and the
// channel's own ceiling unset, which is the absence of a retry policy rather than a policy configured to
// do nothing - and the HTTP layer is disabled by predicate in the sibling method.
static void ApplyGrpcRetry(
    IList<Action<GrpcChannelOptions>> channelActions,
    GatewayOptions.OutboundCallOptions outbound)
{
    ArgumentNullException.ThrowIfNull(channelActions);
    ArgumentNullException.ThrowIfNull(outbound);

    if (!outbound.RetriesEnabled)
    {
        return;
    }

    int attempts = outbound.ResolveGrpcAttemptCount();
    TimeSpan initialBackoff = outbound.RetryBaseDelay;

    channelActions.Add(channelOptions =>
    {
        // The channel's own ceiling must admit the policy's attempt count, or the channel silently
        // clamps it and the configured number stops being the number performed.
        channelOptions.MaxRetryAttempts = attempts;
        channelOptions.ServiceConfig =
            OutboundCallPolicy.BuildRetryServiceConfig(attempts, initialBackoff);
    });
}

// Applies Gateway's outbound resilience policy to one client's standard pipeline.
// resilience: The pipeline's options, mutated in place.
// serviceProvider: The provider the outbound bounds are read from.
// ONE FUNCTION FOR ALL THREE OUTBOUND CLIENTS, so the token channel and the two gRPC channels cannot
// drift into three different postures. Before this existed, all three called
// AddStandardResilienceHandler() with no configuration at all, which meant a stock HTTP retry
// policy sat on top of gRPC channels it could not read and REST calls it should not replay.
// WHAT EACH LINE FIXES, in the order they appear:
// <list type="number">
// <item>
// The TOTAL REQUEST TIMEOUT is taken from the same setting that produces the gRPC deadline, so the
// local budget and the bound the upstream is told about are one number. Left at its default, the two
// were independent and a change to either would have silently desynchronised them.
// </item>
// <item>
// The BACKOFF TYPE and JITTER are set explicitly rather than inherited. Both happen to match the
// package's defaults today, and that is exactly why they are written down: "bounded backoff with
// jitter" is a requirement of this policy, and a requirement that holds only because a dependency's
// default happens to satisfy it is not actually being enforced.
// </item>
// <item>
// The RETRY PREDICATE is replaced, which is the substantive change. The stock predicate reads the HTTP
// status, and a gRPC call the server refused carries HTTP 200 - so it never retried a server-declared
// Unavailable, while it happily replayed a transport fault on an update, a session open or a
// transaction commit. The replacement decides from the operation first and the gRPC status second.
// </item>
// <item>
// The CIRCUIT-BREAKER PREDICATE is replaced for the first half of the same reason: an upstream that
// answers Unavailable to every call is unhealthy, and a breaker that cannot see the status never
// notices. It deliberately does NOT count the deliberate refusals - admission limits, concurrency
// conflicts, authorization decisions - because those are correct answers and breaking on them would
// deny the reads that were still working.
// </item>
// </list>
// THE RETRY COUNT AND ITS BASE DELAY ARE CONFIGURED HERE TOO, and the reason is worth stating because it
// is NOT the obvious one. Leaving them at the package's defaults is the defensible position, on the
// ground that choosing them would assert an availability posture the repository publishes nothing to
// derive from (AAP 0.8.5). They are set for a CORRECTNESS reason instead: the gRPC channels carry a
// second retry layer whose attempt
// count has to be the same number as this one, and a number that exists in two places under two
// defaults is a number that will disagree. So one setting feeds both - see ApplyGrpcRetry - and
// no latency or throughput target is claimed by either. The CIRCUIT-BREAKER THRESHOLDS stay at the
// package's defaults, for exactly the AAP 0.8.5 reason above.
static void ConfigureOutboundResilience(
    HttpStandardResilienceOptions resilience,
    IServiceProvider serviceProvider)
{
    ArgumentNullException.ThrowIfNull(resilience);
    ArgumentNullException.ThrowIfNull(serviceProvider);

    GatewayOptions.OutboundCallOptions outbound = serviceProvider
        .GetRequiredService<IOptions<GatewayOptions>>()
        .Value
        .Outbound;

    resilience.TotalRequestTimeout.Timeout = outbound.RequestTimeout;

    // SET RATHER THAN INHERITED, and that is a fix rather than tidying. The per-attempt timeout kept the
    // package's ten-second default while only the total was configured, so for any operation that is
    // never retried the ten seconds was the bound that actually applied - see
    // GatewayOptions.OutboundCallOptions.AttemptTimeout. Unset, this equals the total, so the documented
    // number is the observed one.
    resilience.AttemptTimeout.Timeout =
        outbound.ResolveAttemptTimeout(resilience.CircuitBreaker.SamplingDuration);

    // ASSIGNED FROM THE SAME TWO SETTINGS THE gRPC LAYER READS, so the two layers cannot disagree about
    // how many times a replay-safe call is attempted. Both matched the package defaults before; they are
    // written down because a requirement satisfied only by a dependency's default is not being enforced.
    //
    // 🔴 A CONFIGURED ZERO IS EXPRESSED AS A PREDICATE, NOT AS A COUNT, and it has to be: the resilience
    // package declares Retry.MaxRetryAttempts in the range 1..int.MaxValue, so assigning zero made the
    // SERVICE FAIL TO START - "The field <client>-standard.Retry.MaxRetryAttempts must be between 1 and
    // 2147483647", observed on all three named pipelines - while the setting was documented as a disable.
    // The strategy therefore names the smallest legal count and its ShouldHandle answers false for
    // everything, so nothing is ever retried. See OutboundCallOptions.DisabledRetryPlaceholderAttempts.
    resilience.Retry.MaxRetryAttempts = outbound.RetriesEnabled
        ? outbound.MaxRetryAttempts
        : GatewayOptions.OutboundCallOptions.DisabledRetryPlaceholderAttempts;

    resilience.Retry.Delay = outbound.RetryBaseDelay;

    resilience.Retry.BackoffType = DelayBackoffType.Exponential;
    resilience.Retry.UseJitter = true;

    resilience.Retry.ShouldHandle = outbound.RetriesEnabled
        ? OutboundCallPolicy.ShouldRetryAsync
        : OutboundCallPolicy.NeverRetryAtTheHttpLayerAsync;

    resilience.CircuitBreaker.ShouldHandle = OutboundCallPolicy.ShouldBreakAsync;
}

// Loads the client identity Gateway presents on the system's single mutual-TLS edge, or an empty
// collection when this deployment presents none.
// The validated Gateway:MutualTls group. Both members are filesystem paths naming material
// mounted from the orchestration secret layer; the type has no member that could carry a certificate
// body, a private key body or a passphrase, so there is nowhere for one to be placed.
// A collection holding the one client certificate when the pair is configured, and an EMPTY
// collection when it is not. Empty is a legitimate result and not an error: it means this run does
// not reach the token-issuance edge, which a local bring-up without a generated certificate set
// genuinely is.
// The pair is configured but the material cannot be read or does not parse. That is a structural
// fault and it stops the host, matching the fail-fast posture described in the file header - a
// deployment that meant to authenticate to the issuer and cannot has already lost every authenticated
// call it would make, so continuing would only defer the failure to first use.
// NO PATH IS EVER ECHOED INTO A MESSAGE, and the exception below names the two CONFIGURATION KEYS
// instead. A path is not itself a credential, but it names the location of one, and a startup log is
// exactly the wrong place to publish where a private key is mounted. The configuration key is
// sufficient for an operator to find the setting, which is the same rule
// Configuration/GatewayOptions.cs applies to its own validation messages.
// HALF A PAIR CANNOT REACH HERE. GatewayOptions validates the group as both-or-neither and the
// registration above validates on start, so by the time this runs the pair is either wholly present
// or wholly absent. The second check below is therefore a guard against a future caller rather than a
// reachable configuration state, and it is written as one rather than as an assumption.
// The material is read from a PEM certificate and a separate PEM key, which is the shape the
// generation recipe in docs/ARCHITECTURE.md produces and the shape the four
// *_MTLS_CERT_PATH / *_MTLS_KEY_PATH variables name. The resulting key is ephemeral,
// which is directly usable for TLS client authentication on Linux - the target operating system for
// every container in this refactor.
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
        // ⚠ THE ORIGINAL EXCEPTION IS DELIBERATELY NOT ATTACHED, AND OMITTING IT IS THE WHOLE POINT.
        // The message below names no path - but an InnerException would, because every one of the four
        // caught types puts the file name in its own Message: IOException and
        // UnauthorizedAccessException are constructed from the path by the runtime, and
        // CryptographicException can carry it too. Startup logging renders an exception CHAIN, not just
        // the outermost message, so attaching the cause would publish where a private key is mounted in
        // the one log record an operator is most likely to paste somewhere (constraint C-F). Redacting
        // the outer message while wrapping the inner one is not redaction at all.
        //
        // THE TYPE NAME IS RETAINED BECAUSE IT IS DIAGNOSTIC AND CARRIES NO PATH. It is what separates
        // the three failure modes an operator would otherwise have to guess between: a missing or
        // unreadable file, a file that is not PEM, and a key the platform will not accept. That is
        // enough to act on, and it is all that can be published safely.
        throw new InvalidOperationException(
            "The client certificate named by "
                + $"'{GatewayOptions.SectionName}:{nameof(GatewayOptions.MutualTls)}' could not be "
                + "loaded, so this deployment cannot authenticate to the token-issuance edge and the "
                + "host will not start. Check that both files exist, that the process can read them, "
                + "and that each is PEM encoded - the certificate in "
                + $"'{nameof(GatewayOptions.MutualTlsClientOptions.CertificatePath)}' and its private "
                + $"key in '{nameof(GatewayOptions.MutualTlsClientOptions.CertificateKeyPath)}'. The "
                + $"underlying failure was a {failure.GetType().Name}. Neither path is reproduced here, "
                + "and the underlying exception is deliberately not attached, because a startup log is "
                + "the wrong place to publish where a private key is mounted.");
    }
}

/// <summary>
/// The extension-member names the published problem body declares.
/// </summary>
/// <remarks>
/// SPELLED ONCE HERE BECAUSE THE COMPOSITION ROOT AND THE ENDPOINT FILES BOTH WRITE THEM, and the
/// customization above exists precisely to fill the member an endpoint file did not. Two independent
/// spellings would let a rename go half-applied, at which point a body would carry both the old member and
/// the new one and a consumer would read whichever it happened to look for
/// [shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml ProblemDetails].
/// </remarks>
internal static class ProblemContractMembers
{
    /// <summary>The legacy return code carried by every problem body.</summary>
    internal const string RetCode = "retCode";

    /// <summary>The correlation identifier carried by every problem body this service writes.</summary>
    /// <remarks>
    /// <para>
    /// Spelled <c>traceId</c>, which is the spelling every other service in the estate publishes and the
    /// one the authored OpenAPI documents describe. A second spelling anywhere would leave an operator
    /// joining two halves of one request by two different member names.
    /// </para>
    /// <para>
    /// The value is the current <c>Activity</c> identifier - a W3C trace context id, continued from an
    /// inbound <c>traceparent</c> when the caller sent one - falling back to the host's request identifier.
    /// The composition root's logging configuration stamps the same trace and span identifiers onto every
    /// log record, which is what makes a body and a record joinable rather than merely both timestamped.
    /// </para>
    /// </remarks>
    internal const string TraceId = "traceId";
}

/// <summary>
/// The trust anchor every outbound internal channel verifies its peer against, loaded once from the
/// path <c>Gateway:InternalTls:TrustedCaPath</c> names.
/// </summary>
/// <remarks>
/// <para>
/// WHAT THIS FIXES, STATED PLAINLY. Security and DataServices both terminate TLS with certificates
/// issued by the LOCAL certificate authority the generation recipe in <c>docs/ARCHITECTURE.md</c>
/// §9.3.1 creates, and that authority is in no container's operating-system trust store. Left on
/// platform default trust, every outbound channel this service opens - token issuance, the two gRPC
/// channels, the bearer handler's key-set backchannel and the readiness probes - rejects the
/// certificate it is presented and the documented topology cannot connect at all.
/// </para>
/// <para>
/// IT NARROWS TRUST; IT DOES NOT RELAX IT. The policy built here sets
/// <see cref="X509ChainTrustMode.CustomRootTrust"/>, so the mounted anchor becomes the ONLY acceptable
/// root for internal traffic and the machine's public roots stop being acceptable for it. Chain
/// building, name validation and validity dates are still performed by the platform, unchanged. There
/// is no <c>RemoteCertificateValidationCallback</c>, no
/// <c>ServerCertificateCustomValidationCallback</c>, no <c>DangerousAcceptAnyServerCertificate</c> and
/// no environment-conditional bypass anywhere in this service (constraint C-G).
/// </para>
/// <para>
/// REVOCATION IS A SETTING, AND ITS DEFAULT IS A CONSEQUENCE OF THE TOPOLOGY RATHER THAN A RELAXATION.
/// <c>Gateway:InternalTls:RevocationMode</c> selects the posture, so a deployment whose authority DOES
/// publish revocation information can ask for a real check - which a hardcoded value denied it, leaving a
/// stolen peer certificate acceptable until it expired. The shipped default is the only value the
/// DOCUMENTED topology can answer, and that was measured rather than assumed: a local authority generated
/// by two <c>openssl</c> invocations publishes no distribution point and runs no responder, so chain
/// building for a leaf it issued succeeds with no check and fails under both stricter modes with an
/// indeterminate revocation status. An indeterminate status is a REFUSAL under those modes and never a
/// pass, because no verification flag ignores it. While the default stands, the substituting control is
/// certificate lifetime - the recipe's own <c>-days 30</c> - and the operational surfaces carry both the
/// production recommendation and the emergency procedure for a compromise.
/// </para>
/// <para>
/// LOADED ONCE AND SHARED. The handler factories recycle their primary handlers on a schedule, so
/// reading the anchor inside a factory would re-read the file on every rotation. It is loaded here as
/// a singleton, which is also what lets the eager resolve after <c>Build()</c> turn an unreadable
/// anchor into a startup failure rather than a first-request one.
/// </para>
/// </remarks>
internal sealed class InternalTlsTrust
{
    /// <summary>The configuration path of the group this type is built from.</summary>
    /// <remarks>
    /// Composed once so the resolver's failure message and the loader's failure message name the group
    /// the same way, and so neither can drift from the property names it quotes.
    /// </remarks>
    private static readonly string ConfigurationKeyPrefix =
        $"{GatewayOptions.SectionName}:{nameof(GatewayOptions.InternalTls)}";

    private readonly X509Certificate2Collection _anchors;

    /// <summary>
    /// The revocation posture the configured group selected, resolved once at construction.
    /// </summary>
    /// <remarks>
    /// RESOLVED HERE RATHER THAN PER POLICY, so an unrecognised value fails the host's start instead of
    /// failing the first outbound handshake - the fail-fast posture the rest of this file keeps.
    /// </remarks>
    private readonly X509RevocationMode _revocationMode;

    /// <summary>
    /// Loads the anchor bundle, or records that this deployment uses platform default trust.
    /// </summary>
    /// <param name="options">The validated <c>Gateway:InternalTls</c> group. A path, never material.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A path is configured but the bundle cannot be read or does not parse. Structural, and therefore
    /// fatal: a deployment that meant to pin internal trust and cannot has already lost every
    /// authenticated call it would make.
    /// </exception>
    /// <remarks>
    /// The path is NOT echoed into the failure message. A trust anchor is public material, but a
    /// container's secret mount layout is not something a startup record should publish, so the message
    /// names the configuration key exactly as
    /// <c>Configuration/GatewayOptions.cs</c> and <c>LoadMutualTlsClientIdentity</c> do.
    /// </remarks>
    public InternalTlsTrust(GatewayOptions.InternalTlsTrustOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // RESOLVED BEFORE THE EARLY RETURN, DELIBERATELY. An unrecognised mode is a misconfiguration
        // whether or not this deployment pins an anchor, and a deployment that later sets a path would
        // otherwise discover the typo only once it had.
        _revocationMode = options.ResolveRevocationMode(ConfigurationKeyPrefix);

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
                throw new CryptographicException(
                    "The file carried no PEM-encoded certificate.");
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
                    + $"'{GatewayOptions.SectionName}:{nameof(GatewayOptions.InternalTls)}:"
                    + $"{nameof(GatewayOptions.InternalTlsTrustOptions.TrustedCaPath)}' could not be "
                    + "loaded, so this deployment cannot verify its upstreams' certificates and the "
                    + "host will not start. Check that the file exists, that the process can read it, "
                    + "and that it is a PEM-encoded certificate or chain of them. The path is not "
                    + "reproduced here, because a startup record must not publish a container's secret "
                    + "mount layout.",
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

        handler.SslOptions.CertificateChainPolicy = CreateChainPolicy();
    }

    /// <summary>
    /// Builds the chain policy internal peers are verified against.
    /// </summary>
    /// <returns>A policy trusting the mounted anchor and nothing else.</returns>
    /// <remarks>
    /// <see cref="X509ChainPolicy.CustomTrustStore"/> is only consulted under
    /// <see cref="X509ChainTrustMode.CustomRootTrust"/>, so the two are set together and neither is
    /// meaningful without the other.
    /// </remarks>
    private X509ChainPolicy CreateChainPolicy()
    {
        X509ChainPolicy policy = new()
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,

            // THE CONFIGURED POSTURE, not a constant. See the option's own remarks for why the shipped
            // default cannot be the strict value on the documented topology, and why an indeterminate
            // status under the stricter two is a refusal rather than a pass.
            RevocationMode = _revocationMode,
        };

        // ONLY MEANINGFUL WHEN A CHECK IS ACTUALLY PERFORMED, and it excludes the root because a locally
        // generated authority does not revoke itself - asking about it would turn every check into an
        // indeterminate answer and therefore into a refusal, which is the failure the mode's own remarks
        // describe. It matches the flag Security's issuance-credential check already uses.
        policy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

        policy.CustomTrustStore.AddRange(_anchors);

        return policy;
    }
}

/// <summary>
/// The data-protection key repository, held in this process's memory and never written to storage.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS AT ALL. Data protection is registered by the authentication stack whether or not
/// anything protects a payload, and its eager initialiser materialises a key ring during host start - which,
/// left at the default, writes an UNENCRYPTED private key into the process's user profile for no consumer.
/// Nothing in this service protects a payload: inbound authentication is bearer-token validation against
/// published verification material, and there is no cookie, no session, no antiforgery token and no
/// protected payload that outlives a request.
/// </para>
/// <para>
/// 🔴 <b>AND THE OBVIOUS FIX IS THE ONE THAT DOES NOT WORK.</b> Swapping
/// <c>IDataProtectionProvider</c> for the framework's ephemeral provider was tried and MEASURED: a key file
/// was still written on every start, because the eager initialiser warms the key-management stack rather
/// than the registered provider. Redirecting the REPOSITORY is what removes the write, because it removes
/// the file-system repository from the graph entirely.
/// </para>
/// <para>
/// The consequence is deliberate and is the reason this posture was chosen: a capability that later needs a
/// DURABLE protector fails immediately and visibly at the first restart, rather than working on one replica
/// and failing on the next. <c>docs/SECRETS.md</c> section 5.4 records the decision, the rejected
/// alternative and what a later phase must put in its place.
/// </para>
/// <para>
/// THREAD SAFETY IS REQUIRED, NOT OPTIONAL. The key ring is read on request threads and written by the
/// initialiser, so every access is taken under one lock. The returned collection is a snapshot, so a caller
/// enumerating it cannot observe a concurrent store.
/// </para>
/// </remarks>
internal sealed class InMemoryDataProtectionKeyRepository : IXmlRepository
{
    /// <summary>The stored elements, guarded by <see cref="_gate"/>.</summary>
    private readonly List<XElement> _elements = [];

    /// <summary>Serialises every read and write of <see cref="_elements"/>.</summary>
    private readonly object _gate = new();

    /// <inheritdoc />
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_gate)
        {
            // A COPY, and each element cloned: the key manager is free to mutate what it is handed, and a
            // shared instance would let one caller's edit reach another's read.
            return [.. _elements.Select(static element => new XElement(element))];
        }
    }

    /// <inheritdoc />
    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        lock (_gate)
        {
            _elements.Add(new XElement(element));
        }
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
