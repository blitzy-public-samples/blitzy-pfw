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
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
    // GUARDED ON PRESENCE, WHICH IS THE OTHER HALF AND WAS MISSING. An earlier form assigned
    // unconditionally with `?? string.Empty`, so an ABSENT flat key did not leave the property alone -
    // it OVERWROTE whatever binding had put there with empty. `Gateway:SecurityClientSecret` is a
    // bindable leaf (the environment provider folds `Gateway__SecurityClientSecret` onto it), so a
    // deployment could supply the credential by that route, watch the binder accept it, and be refused at
    // startup for presenting nothing - with a message naming the flat key it had deliberately not used.
    // A silently discarded input is worse than a rejected one: there is nothing to read that says the
    // value was dropped.
    //
    // THE SEMANTICS ARE NOW THE SIBLINGS' SEMANTICS RATHER THAN A THIRD SET. Security's signing-key step
    // states the rule normatively - "when the key is ABSENT this step assigns nothing at all, so material
    // that reached the options instance through another legitimate ingress survives rather than being
    // overwritten with nothing" - and DataServices' ApplyIssuanceSecret is the same shape. Gateway was
    // the outlier, which meant one idea had three different behaviours across three services.
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

// A FALLBACK POLICY, so that no route is ever authenticated by omission (constraint C-G). Every
// endpoint file already declares RequireAuthorization explicitly, and this makes the absence of such
// a declaration a closed door rather than an open one. The two deliberate exceptions below opt out
// in the one place a reader looks for them.
//
// AN AUTHENTICATED CALLER IS THE WHOLE REQUIREMENT HERE, AND UNLIKE THE OTHER THREE SERVICES THAT IS
// NOT AN OVERSIGHT. DataServices, Persistence and Security each require an operation-specific scope
// AND a permitted caller identity on top of authentication, because each of them is an INTERNAL
// receiver whose complete set of callers is enumerated in Security's issuance grant matrix. Gateway is
// the INGRESS: nothing inside the system calls it, so it has no internal caller roster to check
// against, and its callers are external clients that no internal matrix describes.
//
// The authoritative wire document settles what that means, and it is authoritative for anything on the
// wire. shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml applies `bearerAuth` with an EMPTY
// scope array to every operation and states, under that scheme's own Scope heading, that every
// operation requires the scheme except the anonymous health probe - it declares no per-operation scope
// anywhere. It then attributes the 403 response to "The projected gRPC method returned
// PermissionDenied": Gateway's forbidden answer is a RELAY of the downstream refusal, which is exactly
// where the scope decision is made and enforced. Inventing a Gateway-side scope name would therefore
// be a change to the published contract rather than a hardening of it, and it would fabricate an
// external-client scope vocabulary the Agent Action Plan does not define.
//
// Two properties do the containment work instead, and neither depends on a scope here. Audience
// validation above accepts only tokens minted for THIS service, so a token obtained for DataServices,
// Persistence or Security is refused at this boundary with a 401 rather than reaching a projection.
// And Security's grant matrix pre-grants NO caller the gateway audience at all - the shipped roster
// carries exactly the internal call graph - so a service identity cannot mint itself an ingress token.
// An external client is a deployment fact: its certificate identity and its grant are added to
// Security:Callers by the deployment that has one, which is recorded in that settings file.
//
// Expressed through AddAuthorizationBuilder rather than AddAuthorization(options => ...) because the
// ASP.NET Core analyzers direct the builder form for exactly this shape (ASP0025); the registration
// and the resulting policy are identical, so this is a spelling decision and not a behavioural one.
//
// AND THREE NAMED SCOPE POLICIES, BECAUSE A FALLBACK POLICY IS NOT AN ENTITLEMENT CHECK. Every
// protected route used to require only that the caller be AUTHENTICATED, which every token this system
// mints for Gateway's audience is - so one token reached /v1/ping, /v1/capabilities and all
// thirty-nine /v1/datawindow operations alike. The issuance roster states least privilege per calling
// identity and, until these policies existed, no surface in this service enforced it and the 403 the
// contract declares was unreachable.
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
// Two independent remediations of the same finding arrived at this composition root: a declarative
// requirement plus handler, and the three inline assertion policies below that the endpoints name. Both
// read the same `scope` claim with the same ordinal, space-delimited semantics, so they agree by
// construction; the requirement form is registered because ScopeAuthorizationTests drives it directly,
// and the named form is registered because the endpoints reference `<Endpoint>.ScopePolicyName`.
//
// AUTHENTICATION IS NOT AUTHORIZATION, AND THE FALLBACK BELOW ONLY DELIVERS THE FIRST. This service's
// published contract declares a 403 on thirty-nine operations whose shared description says the token is
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
        .Build())
    .AddPolicy(
        PingEndpoints.ScopePolicyName,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(static context =>
                GrantsScope(context.User, PingEndpoints.RequiredScope)))
    .AddPolicy(
        CapabilityEndpoints.ScopePolicyName,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(static context =>
                GrantsScope(context.User, CapabilityEndpoints.RequiredScope)))
    .AddPolicy(
        DataServicesProxyEndpoints.ScopePolicyName,
        policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(static context =>
                GrantsScope(context.User, DataServicesProxyEndpoints.RequiredScope)));

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
    .AddGrpcClient<DataWindowService.DataWindowServiceClient>(static (serviceProvider, grpcOptions) =>
    {
        GatewayOptions options = serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

        grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
    })
    .ConfigurePrimaryHttpMessageHandler(CreateInternalChannelHandler)
    .AddStandardResilienceHandler()
    .Configure(ConfigureOutboundResilience);

builder.Services
    .AddGrpcClient<ColumnExpressionService.ColumnExpressionServiceClient>(
        static (serviceProvider, grpcOptions) =>
        {
            GatewayOptions options =
                serviceProvider.GetRequiredService<IOptions<GatewayOptions>>().Value;

            grpcOptions.Address = new Uri(options.Upstreams.DataServices, UriKind.Absolute);
        })
    .ConfigurePrimaryHttpMessageHandler(CreateInternalChannelHandler)
    .AddStandardResilienceHandler()
    .Configure(ConfigureOutboundResilience);

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

app.UseExceptionHandler();

// STATUS-CODE PAGES, AND THE REASON IS CONTRACT FIDELITY RATHER THAN HARDENING. Without it the framework
// answers a bare status with NO BODY on every path that never reaches this service's own code: the bearer
// challenge, the authorization refusal, an unmatched route and a rejected method. gateway.v1.yaml declares
// reusable Unauthorized, Forbidden and NotFound responses whose bodies are ProblemDetails, and the routes
// themselves declare ProducesProblem for 401, 403 and 404 - so a bodyless response is a published promise
// this service was not keeping. This middleware writes the missing body through the problem-details
// service configured above, which is why the members that customization fills reach these responses too.
//
// It is the narrow exception to the no-unrequested-middleware rule (constraint C-B): the requirement that
// creates it is the published contract, and nothing else is added here - no CORS, no rate limiting, no
// compression, no output caching. It is ordered with UseExceptionHandler and BEFORE authentication for the
// reason both diagnostics middlewares share: each works by observing what the middlewares beneath it
// produced, and the response they most need to observe is the challenge the authentication middleware
// writes.
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// The published contract document, AUTHENTICATED like every other route that has not been granted an
// explicit exemption. It used to be anonymous on the argument that a description of a surface is not part
// of the surface, and that argument does not survive the AAP: the anonymous exceptions are enumerated and
// this is not among them - `/health` on all four services (C-10), and Security's key set and discovery
// document (C-01), and nothing else. The circularity worry it was defending against is not real either. A
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

/// <summary>
/// Classifies a framework-generated failure status as a legacy return code, so that a body this
/// service did not compose still carries the member the published contract declares.
/// </summary>
/// <param name="statusCode">The status the framework is answering with.</param>
/// <returns>
/// The legacy code for that status, and <see cref="RetCode.UNKNOWN"/> for anything unclassifiable.
/// </returns>
/// <remarks>
/// <para>
/// EVERY ARM IS WRITTEN OUT RATHER THAN DERIVED FROM A TRUTHINESS TEST. The codes are taken from the
/// published contract's own response catalogue: a malformed request is <c>E_INVALID_ARGUMENT</c>, a
/// refused caller is <c>E_ACCESS_DENIED</c> whether the refusal was authentication or authorization, an
/// unmatched route is <c>E_OBJECT_NOT_FOUND</c>, a rejected method is <c>E_NO_SUPPORT</c>, and a reserved
/// extension point is <c>E_NO_IMPLEMENTATION</c> - the same value
/// <c>Endpoints/DeferredCapabilityEndpoints.cs</c> writes by hand, so the two agree.
/// </para>
/// <para>
/// <b>429 IS CLASSIFIED BECAUSE THIS SERVICE PRODUCES IT, AND IT WAS THE ONE PUBLISHED STATUS MISSING
/// HERE.</b> <c>Endpoints/DataServicesProxyEndpoints.cs</c> declares it on every projected route and
/// reaches it twice - from an upstream <c>ResourceExhausted</c>, and from an in-band <c>E_BUSY</c> outcome -
/// so it is a status a caller genuinely receives. Without an arm it fell to <c>UNKNOWN</c>, which reports
/// "unclassifiable" for a refusal this service classifies precisely everywhere else, and it broke the
/// round trip: the in-band direction maps <c>E_BUSY</c> ONTO 429, so the reverse must map 429 back onto
/// <c>E_BUSY</c> or the two directions disagree about the same event. It shares the code with 503 for the
/// reason the two refusals share a nature - the request was declined because capacity was not available -
/// and the statuses stay distinct so a caller can still tell a shed request from an unavailable upstream.
/// </para>
/// <para>
/// THE REFUSAL AND THE FORBIDDEN CASE SHARE ONE CODE, AND THAT ASYMMETRY IS PRESERVED RATHER THAN PAPERED
/// OVER. The legacy algebra declares exactly one access code and draws no distinction between "no
/// credential" and "credential without permission". The HTTP statuses stay distinct, so a caller can still
/// tell the two apart; the code simply does not gain a member the oracle never declared.
/// </para>
/// <para>
/// THE GUARD IS THE POINT OF THE FINAL CHECK. The algebra is tri-state and has a documented hole:
/// <c>PREVENT</c> is 1 and <c>IsSucceeded</c> tests greater-than-or-equal-to zero, so a prevention reads
/// as a SUCCESS [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>, <c>retcode.sru:L42</c>],
/// and <c>CANCELLED</c> is excluded from <c>IsFailed</c>, so a cancellation is NEITHER
/// [<c>isfailed.srf:L11-L13</c>]. An error body must never carry a code from either class, and the kernel
/// predicate is CONSUMED to enforce that rather than the comparison being re-derived here. Every arm below
/// already satisfies it; the check exists so a future edit introducing one that did not would degrade to
/// <c>UNKNOWN</c> instead of publishing a failure a consumer's own predicate would read as a success.
/// </para>
/// </remarks>
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
/// <summary>
/// Builds the primary handler for an outbound internal channel - the two gRPC clients and the
/// readiness-probe client - with internal trust applied.
/// </summary>
/// <param name="serviceProvider">The provider the shared trust anchor is resolved from.</param>
/// <returns>A handler that verifies its peer against the mounted anchor when one is configured.</returns>
/// <remarks>
/// <para>
/// ONE FACTORY FOR THREE CHANNELS, so all three demonstrably share the same trust decision. Three
/// lambdas would let one drift during a later edit, and a channel that quietly kept platform default
/// trust is exactly the defect this replaces - the readiness probe had no registered client at all and
/// so was silently using a default-configured one.
/// </para>
/// <para>
/// <c>EnableMultipleHttp2Connections</c> is set explicitly because supplying a primary handler replaces
/// the one the gRPC client factory would otherwise build, and that one sets this property. Leaving it
/// at its default would silently cap concurrent streams per connection at the peer's advertised limit
/// and queue calls behind it - a behavioural change to the transport that has nothing to do with trust.
/// </para>
/// <para>
/// The Security typed client does NOT come through here, and that is deliberate: it needs the client
/// certificate as well as the anchor, so its handler is built at its own registration where both halves
/// are in view.
/// </para>
/// </remarks>
static HttpMessageHandler CreateInternalChannelHandler(IServiceProvider serviceProvider)
{
    ArgumentNullException.ThrowIfNull(serviceProvider);

    SocketsHttpHandler handler = new() { EnableMultipleHttp2Connections = true };

    serviceProvider.GetRequiredService<InternalTlsTrust>().Apply(handler);

    return handler;
}

/// <summary>
/// Applies Gateway's outbound resilience policy to one client's standard pipeline.
/// </summary>
/// <param name="resilience">The pipeline's options, mutated in place.</param>
/// <param name="serviceProvider">The provider the outbound bounds are read from.</param>
/// <remarks>
/// <para>
/// ONE FUNCTION FOR ALL THREE OUTBOUND CLIENTS, so the token channel and the two gRPC channels cannot
/// drift into three different postures. Before this existed, all three called
/// <c>AddStandardResilienceHandler()</c> with no configuration at all, which meant a stock HTTP retry
/// policy sat on top of gRPC channels it could not read and REST calls it should not replay.
/// </para>
/// <para>
/// WHAT EACH LINE FIXES, in the order they appear:
/// </para>
/// <list type="number">
/// <item>
/// The TOTAL REQUEST TIMEOUT is taken from the same setting that produces the gRPC deadline, so the
/// local budget and the bound the upstream is told about are one number. Left at its default, the two
/// were independent and a change to either would have silently desynchronised them.
/// </item>
/// <item>
/// The BACKOFF TYPE and JITTER are set explicitly rather than inherited. Both happen to match the
/// package's defaults today, and that is exactly why they are written down: "bounded backoff with
/// jitter" is a requirement of this policy, and a requirement that holds only because a dependency's
/// default happens to satisfy it is not actually being enforced.
/// </item>
/// <item>
/// The RETRY PREDICATE is replaced, which is the substantive change. The stock predicate reads the HTTP
/// status, and a gRPC call the server refused carries HTTP 200 - so it never retried a server-declared
/// Unavailable, while it happily replayed a transport fault on an update, a session open or a
/// transaction commit. The replacement decides from the operation first and the gRPC status second.
/// </item>
/// <item>
/// The CIRCUIT-BREAKER PREDICATE is replaced for the first half of the same reason: an upstream that
/// answers Unavailable to every call is unhealthy, and a breaker that cannot see the status never
/// notices. It deliberately does NOT count the deliberate refusals - admission limits, concurrency
/// conflicts, authorization decisions - because those are correct answers and breaking on them would
/// deny the reads that were still working.
/// </item>
/// </list>
/// <para>
/// RETRY COUNT AND CIRCUIT-BREAKER THRESHOLDS ARE LEFT AT THE PACKAGE'S DEFAULTS, and that is a
/// decision rather than an oversight. Choosing values for them would be asserting a availability or
/// latency posture this repository publishes nothing to derive from (AAP 0.8.5), whereas every value
/// this function does set has a stated correctness derivation.
/// </para>
/// </remarks>
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

    resilience.Retry.BackoffType = DelayBackoffType.Exponential;
    resilience.Retry.UseJitter = true;
    resilience.Retry.ShouldHandle = OutboundCallPolicy.ShouldRetryAsync;

    resilience.CircuitBreaker.ShouldHandle = OutboundCallPolicy.ShouldBreakAsync;
}

/// <summary>
/// Whether the principal's <c>scope</c> claim set contains the named scope.
/// </summary>
/// <param name="user">The authenticated principal.</param>
/// <param name="required">The scope the operation requires.</param>
/// <returns><see langword="true"/> when the scope is granted.</returns>
/// <remarks>
/// EXACT, ORDINAL AND SPACE-DELIMITED, matching <c>Authorization/ScopeAuthorization.cs</c>'s handler
/// clause for clause: the claim is read under its bare wire spelling because inbound claim mapping is
/// off, entries are compared ordinally with no prefix match and no wildcard, and empty entries are
/// skipped so a doubled or trailing delimiter behaves like a well-formed value. Stated here as well as
/// in the handler because the two forms must not be able to disagree about what "granted" means.
/// </remarks>
static bool GrantsScope(ClaimsPrincipal user, string required)
{
    ArgumentNullException.ThrowIfNull(user);
    ArgumentNullException.ThrowIfNull(required);

    foreach (Claim claim in user.FindAll("scope"))
    {
        foreach (string granted in claim.Value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(granted, required, StringComparison.Ordinal))
            {
                return true;
            }
        }
    }

    return false;
}

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
/// REVOCATION IS NOT CHECKED, AND THAT IS A CONSEQUENCE OF THE TOPOLOGY RATHER THAN A RELAXATION. A
/// local authority generated by two <c>openssl</c> invocations publishes no certificate revocation
/// list and runs no responder, so an online check has nothing to ask and an offline check has nothing
/// to read; requesting one would make every internal handshake wait for a lookup that must fail. The
/// certificates it issues are short-lived by the recipe's own <c>-days 30</c>, which is the control
/// that substitutes for revocation here. A deployment whose authority does publish revocation
/// information leaves this path unset and uses platform trust, where the platform's own default
/// revocation behaviour applies.
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
    private readonly X509Certificate2Collection _anchors;

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
            RevocationMode = X509RevocationMode.NoCheck,
        };

        policy.CustomTrustStore.AddRange(_anchors);

        return policy;
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
