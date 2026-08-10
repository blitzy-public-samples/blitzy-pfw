// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK SECURITY SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the keyed cryptographic surface and for the system's SOLE TOKEN ISSUER.
//  Port 5104. Gateway, DataServices and Persistence call it; it calls none of them.
//
//  This file WIRES: the configuration contract and its startup gate, the cryptographic dependency
//  graph, the two determinism seams, inbound token validation, the default-deny authorization posture,
//  the single published error shape and the five route groups. Route declarations and handler logic
//  live in Endpoints/, next to the contract they implement - this file wires, it does not implement.
//
//  WHY REST AND NOT gRPC (constraint C-K)
//  Token issuance and key publication must be plain HTTP so that consumers' stock
//  Microsoft.AspNetCore.Authentication.JwtBearer handlers fetch the JSON Web Key Set and the OpenID
//  discovery metadata WITH ZERO BESPOKE CODE. Choosing gRPC here would force hand-written key-set
//  retrieval into three services - a net INCREASE in hand-written security code, which is the opposite
//  of the requirement. The n_crypto surface itself argues nothing for gRPC either: at
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru it is roughly sixty STATELESS request/response overloads
//  with no ordering requirement, no cross-call state and nothing to stream, so not one property of it
//  needs a channel, a stream or a compile-time schema. Section 5 records the one deliberate asymmetry
//  that this choice buys: the three consumers read verification material over HTTP, this service reads
//  its own IN PROCESS.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for a token issuer, because PowerFramework is a library with no process
//  of its own, no listener and no server tier - so JWT issuance, key publication and the health and
//  ping contract are net-new, built only on the legacy signing primitives RSASign and VerifyRSASign at
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73. What IS ported is the lifecycle posture of the
//  framework application object at ws_objects/pfw.pbl.src/pfw.sra: initialization before anything else
//  [:L91], finalization paired with it [:L108], and a structural fault ENDING THE PROCESS rather than
//  degrading past it [:L111-L144, ending in HALT CLOSE at :L143]. Sections 1 and 8 are where that
//  posture lands: a misconfigured host refuses to start instead of reporting healthy and then refusing
//  every request.
//
//  THE ASYMMETRIC KEY DECISION IS STRUCTURAL RATHER THAN STYLISTIC
//  The published key set carries PUBLIC verification material only, and the other three services hold
//  verification material only and are not independent signing authorities. A symmetric key would
//  either expose the signing material through that document or require distributing it, and either
//  outcome breaks the sole-issuer topology. The signature scheme therefore comes from
//  Security:SigningAlgorithm, whose accepted set Configuration/SecurityOptions.cs closes to the three
//  RSA-PKCS#1 identifiers the legacy signing primitive's hash argument corresponds to. The scheme used
//  by the JavaScript library on the anti-pattern page under tests/blink/ is that library's choice and
//  is not a contract; nothing from that page - no value, no fragment, no claim and no algorithm
//  selection - is reproduced here or anywhere in this service.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No HttpClient, no gRPC channel and no client of any kind. Security is called BY the other three
//      and calls none of them, so it has no outbound edge and therefore no transient network fault to
//      handle - which is why Microsoft.Extensions.Http.Resilience is absent from this project
//      (constraint C-A).
//    * No Authority and no MetadataAddress pointing at this service's own base address. See section 5:
//      a self-referential discovery fetch would add a startup network dependency on itself, would fail
//      in a cold container, and would leave this service unable to satisfy the readiness gate its own
//      dependents wait on (constraint C-I).
//    * No AllowAnonymous. This service has EXACTLY THREE anonymous routes - GET /health, and the two
//      /.well-known/ publications - and all three opt out at their own declaration, inside
//      Endpoints/HealthEndpoints.cs and Endpoints/JwksEndpoints.cs, where a reader looks for them. A
//      fourth exemption granted here would be an authentication decision taken away from the route it
//      applies to, so the published contract document served by MapOpenApi below is governed by the
//      default-deny fallback policy like every other route (constraint C-G).
//    * No key, no credential, no connection string and no configuration VALUE in any form, including
//      in a comment. The system's single signing input is supplied through configuration and appears
//      in no source file, no settings file and no container definition; every diagnostic below names a
//      configuration KEY and never echoes what was configured under it (constraint C-F).
//    * No registration, route, handler, options type, health check or client for DesignSystem,
//      Documents, Integration or ScriptBridge. Those four capability areas are out of this refactor
//      entirely; their reserved routes belong to GATEWAY's routing table, and declaring one here would
//      put a deferred capability's surface in the wrong service (constraint C-D).
//    * No DbContext, no EF Core, no Microsoft.Data.Sqlite and no connection string. Persistence is the
//      only service that holds a storage provider (constraint C-E).
//    * No rate limiting, CORS, response compression, output caching or request-body-size tuning. Each
//      would be an unrequested behavioural change on a security-critical path (constraint C-B). The
//      two diagnostics middlewares in section 9 are the exception that proves the rule: they are
//      present because the PUBLISHED CONTRACT requires every non-2xx to carry one problem shape, not
//      as hardening.
//    * No development bypass, no dev-token mode and no relaxed-validation branch under any environment
//      condition. Every validation in section 5 is on, and the clock-skew allowance defaults to zero
//      rather than to the handler's five minutes (constraint C-G).
//    * No mutual-TLS scaffolding. Mutual TLS is a per-pair fallback, and the ONE operation that uses a
//      transport credential applies it as its own route-level policy inside Endpoints/TokenEndpoints.cs
//      rather than at the listener; nothing is scaffolded for any deferred service.
//    * No SCREAMING_SNAKE identifier is DECLARED here. The repository .editorconfig scopes its naming
//      suppressions to a fixed list of files carrying preserved legacy identifiers, of which the only
//      one in this project is Crypto/LegacyDefaults.cs; with warnings as errors, a preserved-spelling
//      constant declared in this file would be a BUILD ERROR rather than a style nit. The preserved
//      identifiers are CONSUMED from there and from PowerFramework.Shared.Kernel instead.
//    * No listening port in code. https://+:5104 comes from appsettings.json and is overridden by the
//      container's environment, which is what makes independent deployability hold (constraint C-J).
//    * No presentation surface: no static files, no SPA proxy, no Razor or Blazor, no component
//      library and no theming layer. Phase 1 is API and service level only, and the legacy application
//      disables even its own theming [ws_objects/pfw.pbl.src/pfw.sra:L25].
// ==================================================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// 1. THE CONFIGURATION CONTRACT, AND THE STARTUP GATE THAT MAKES IT FAIL FAST
//
// THE HOST REFUSES TO START ON A MISCONFIGURATION. That is the legacy posture, not a preference: the
// framework application object decodes a structural fault and then ENDS THE PROCESS
// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE at :L143]. Softening any check below
// into a warning-and-continue would produce a service that answers its readiness probe, gates three
// dependents behind that answer, and then refuses every request - which looks correct from the outside
// for exactly as long as it takes to page someone.
//
// THREE MECHANISMS, EACH DOING WORK THE OTHER TWO CANNOT.
//   * ValidateDataAnnotations enforces the presence rules declared where each property is declared, so
//     the requirement and its enforcement cannot drift apart.
//   * SecurityOptionsValidator is a DISCRETE registered validator rather than a lambda, because every
//     one of its nine rule groups has to be drivable from a test by constructing an options instance
//     and calling it - no host, no request - which is what makes the per-service coverage gate
//     reachable on this logic. It also AGGREGATES: an operator fixing a bring-up sees three faults as
//     three faults rather than one restart at a time.
//   * ValidateOnStart moves both passes from first-use to host start. Without it the first failing
//     request, not the deployment, is what reports the misconfiguration.
//
// NO KEY MATERIAL AND NO CONFIGURED VALUE APPEARS HERE, and none can appear in a failure either: every
// message the validator produces names a configuration KEY, and the signing-material failure is one
// fixed sentence that describes the accepted encodings and says nothing about what was supplied - not
// the value, not a substring, not even its length (constraint C-F).
// --------------------------------------------------------------------------------------------------
builder.Services
    .AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))

    // ------------------------------------------------------------------------------------------
    // THE FLAT-KEY RESOLUTION STEP. This is the one place the system's single signing input enters
    // the process, and it exists because SECTION BINDING CANNOT REACH IT.
    //
    // The environment-variable configuration provider folds a DOUBLE UNDERSCORE, and only a double
    // underscore, into the ':' section separator. SECURITY_JWT_SIGNING_KEY contains none, so it
    // lands as a TOP-LEVEL configuration key of exactly that literal spelling rather than as a path
    // into the Security section - and the Bind above, however it is written, will therefore never
    // populate SigningKey. The name is fixed by orchestration/.env.example and the Compose manifest
    // and must not be renamed, and no Security__SigningKey alias is accepted alongside it: two
    // spellings for one input is two ways for a deployment to be half-configured. The spelling is
    // read from the constant on the options type so that this file, the validator and the tests that
    // drive configuration all use one.
    //
    // POST-CONFIGURE RATHER THAN CONFIGURE, AND GUARDED ON PRESENCE. Post-configure actions run
    // after every configure action, so a value supplied by the deployment is authoritative and
    // cannot be displaced by an earlier or later registration. The presence guard is the other half:
    // when the key is ABSENT this step assigns nothing at all, so material that reached the options
    // instance through another legitimate ingress survives rather than being overwritten with
    // nothing. An absent key is not a fallback path - it is a validation failure, reported by the
    // validator above with the key named.
    //
    // WHAT IS NOT DONE HERE, AND WOULD BE THE MOST TEMPTING THING TO DO. No key is generated when
    // none is configured. An ephemeral key would trade a loud startup failure for a silent
    // fleet-wide authentication outage in which every token the other three services hold stops
    // verifying, and each restart would invalidate the previous generation's tokens as well.
    // ------------------------------------------------------------------------------------------
    .PostConfigure<IConfiguration>(static (options, configuration) =>
    {
        string? configured = configuration[SecurityOptions.SigningKeyEnvironmentVariableName];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            options.SigningKey = configured;
        }
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();

// --------------------------------------------------------------------------------------------------
// 2. THE CRYPTOGRAPHIC SURFACE, AND ITS TWO DETERMINISM SEAMS
//
// Every provider is a stateless singleton over System.Security.Cryptography. NO THIRD-PARTY
// CRYPTOGRAPHY PACKAGE IS REFERENCED and none is needed: the base class library covers every operation
// the legacy surface declares - RSA, keyed hashing, the MD5 and SHA family, AES, 3DES and DES across
// ECB, CBC and CFB with the PKCS-family padding, cryptographic random generation and GUID generation.
//
// The graph is shallow and explicit. EncodingProvider is the leaf every other provider composes over,
// so the base64, hex, blob and string conversions have exactly one implementation rather than four
// copies that could drift, and the legacy's own encoding behaviour is characterized in one place.
//
// THE TWO SEAMS ARE REGISTERED HERE ON PURPOSE. The random blob, random string and identifier
// generators reached through IEntropySource are the primary non-determinism sources in this whole
// service, and the clock is the other; the characterization model requires every such value to be
// maskable from BOTH the master and the candidate recording. Registering the real implementations here
// is what lets the sibling test project substitute deterministic doubles for the whole host rather than
// reaching inside a provider - and it is why no business logic in this service reads an ambient clock
// or draws entropy directly.
//
// THE LEGACY WEAK DEFAULTS ARE PRESERVED AND ANNOTATED, NEVER CORRECTED (constraint C-B). ECB is the
// default symmetric mode, PKCS#1 the default RSA padding with no-padding explicitly refused, PKCS#5
// padding is the only choice, no key-derivation function is reachable at all so a passphrase is used as
// raw key bytes, there is no authenticated encryption so ciphertext carries no integrity tag, and
// 1024-bit RSA remains a legal key size. Each is catalogued and annotated in Crypto/LegacyDefaults.cs,
// which is a STATIC catalogue and therefore has nothing to register: there is no instance of it to
// resolve, which is exactly why it appears nowhere below.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<EncodingProvider>();
builder.Services.AddSingleton<HashProvider>();
builder.Services.AddSingleton<HmacProvider>();
builder.Services.AddSingleton<SymmetricCipherProvider>();
builder.Services.AddSingleton<RsaProvider>();
builder.Services.AddSingleton<IEntropySource, CryptographicEntropySource>();
builder.Services.AddSingleton<RandomProvider>();
builder.Services.AddSingleton(TimeProvider.System);

// --------------------------------------------------------------------------------------------------
// 3. THE REFERENCE RESOLUTION BOUNDARY - THE OTHER HALF OF THE CONTRACT-LEVEL KEY-MATERIAL RULE
//
// The seven providers above were authored to take ALREADY-RESOLVED material and to read no
// configuration of their own. That is the structural half of the rule that RAW KEY MATERIAL NEVER
// CROSSES THE WIRE INBOUND. CryptoReferenceResolver is the other half: it is the single place an opaque
// reference becomes material, it consults the permitted set before it reads anything, and it is the
// only type in this service that touches the key store.
//
// Its policy comes from the same bound options instance section 1 validated, so there is one
// configuration contract in this service rather than two. The store's own CLOSED DEFAULT does the
// safety work at run time: an unpopulated permitted set authorises nothing, so a service brought up
// without a reviewed key store refuses every keyed operation rather than reading whatever
// configuration key a caller happens to name.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<CryptoReferenceResolver>();

// --------------------------------------------------------------------------------------------------
// 4. THE SIGNING-KEY LAYER AND THE SOLE MINTER
//
// A SINGLETON EACH, BECAUSE THE KEY IS FIXED FOR THE LIFETIME OF THE PROCESS. This phase adds no
// rotation, so a per-request instance would re-import the same material on every call and would let two
// concurrent requests publish key sets built from different imports. The signing-key layer imports the
// one configured input, owns the resulting key, and is therefore IDisposable - the container disposes
// it at shutdown, which is this host's structural equivalent of the finalization the legacy pairs with
// its initialization [ws_objects/pfw.pbl.src/pfw.sra:L91 against :L108].
//
// TWO CONSUMERS OF THE KEY LAYER, WITH DELIBERATELY UNEQUAL ACCESS. Endpoints/JwksEndpoints.cs reads
// ONLY the public-only projection and never the credential member; Tokens/TokenIssuer.cs is the sole
// legitimate reader of the credential; and section 5 below takes the public-only verification key.
// Registering the type once here is what keeps that asymmetry a property of the three call sites rather
// than of three separately-configured instances.
//
// THE MINTER'S CONSTRUCTOR IS ITSELF A GATE. It resolves the issuer identity, the audience roster, the
// lifetime and the signing credential EAGERLY and refuses to construct when any of them cannot support
// issuance - a blank issuer, an empty or blank-entried roster, a lifetime that does not carry a whole
// second, a symmetric key, an algorithm this issuer does not produce, or a key identifier that
// disagrees with the one the key set publishes. Section 8 resolves it during startup precisely so that
// those refusals happen before the first request rather than during it.
//
// Security mints; Gateway, DataServices and Persistence hold verification material only and are not
// independent signing authorities. Registering the minter here, once, makes that topology a property of
// the composition root rather than of a convention.
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<SigningKeyProvider>();
builder.Services.AddSingleton<TokenIssuer>();

// --------------------------------------------------------------------------------------------------
// 5. INBOUND TOKEN VALIDATION - THE STOCK HANDLER, AND THE ONE ASYMMETRY THAT IS DELIBERATE
//
// Security is subject to the same rule as every other service on its own surface: /v1/ping requires a
// valid token and the issuer grants itself no exemption. The handler is the stock bearer handler, so
// the security-critical path is FRAMEWORK code rather than hand-written code - the same reasoning that
// leaves the published error shape to the framework's problem-details service in section 7.
//
// NO AUTHORITY AND NO METADATA ADDRESS IS CONFIGURED, AND THAT IS THE DELIBERATE ASYMMETRY. Security
// validates tokens it minted ITSELF, so pointing the handler at its own discovery document would be
// self-referential: it would make startup fetch metadata from the very service that is starting, fail
// in a cold container, and leave this service unable to satisfy the readiness gate its dependents are
// waiting on (constraint C-I). Its verification material is therefore supplied IN PROCESS from the
// signing-key layer registered above. The other three services DO use the HTTP key-set and discovery
// path, and that difference is exactly why this service speaks REST.
//
// ALL FOUR VALIDATIONS ARE ON AND NONE IS RELAXED. Each is read from configuration so a deployment can
// tighten but never silently loosen one by omission - a missing key leaves the safe value in place -
// and there is no environment-conditional arm anywhere below.
//
// THE CLOCK-SKEW ALLOWANCE DEFAULTS TO ZERO RATHER THAN TO THE HANDLER'S FIVE MINUTES, because a
// tolerance IS a relaxation of lifetime validation and the configured token lifetime is measured in
// minutes: accepting the framework default would silently extend a five-minute token to ten. It stays
// configurable for a deployment with genuine clock drift, and it is the only value in this block whose
// default departs from the handler's own.
//
// THE ACCEPTED AUDIENCE IS THIS SERVICE'S OWN IDENTITY, NOT THE WHOLE ISSUANCE ROSTER. Both come from
// the one bound options contract - see the resolver at the foot of this file - and the distinction is
// the point of audience validation: a token minted for Gateway must not be replayable at Security. The
// roster is used as the MEMBERSHIP AUTHORITY for that identity and as the fallback when a deployment
// declares no inbound identity at all, and section 8 refuses to start a host whose declared inbound
// identity is absent from the roster, because /v1/ping could then never be reached by any token this
// issuer is able to mint.
// --------------------------------------------------------------------------------------------------
const string inboundAuthenticationSection = "Authentication:Jwt";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// ------------------------------------------------------------------------------------------------
// CONFIGURED THROUGH THE OPTIONS PIPELINE RATHER THAN IN THE AddJwtBearer CALLBACK, FOR TWO REASONS,
// AND THE SECOND ONE WAS MEASURED RATHER THAN ASSUMED.
//
// First, the verification key has to come from a RESOLVED signing-key layer, which only a
// dependency-injected configuration step can reach. The resolution is lazy - the handler materializes
// its options on first use - which keeps the cryptographic import off the path of the anonymous
// readiness probe, and it is section 8 rather than this registration that turns unusable material
// into a startup failure.
//
// Second, EVERY SETTING BELOW IS READ FROM THE INJECTED CONFIGURATION rather than from the builder's
// configuration at composition time, so that this block sees the FINAL configuration of the host.
// Reading the section eagerly instead looks equivalent and is not: a configuration source contributed
// later - which is exactly how the in-process service tests supply settings, and how a hosting
// arrangement may add its own - is applied while the host is being built, after any composition-time
// read has already happened. An eager read would therefore silently ignore it and leave the handler
// configured from the settings file alone, which is both wrong and untestable (constraint C-H).
// ------------------------------------------------------------------------------------------------
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<SigningKeyProvider, IOptions<SecurityOptions>, IConfiguration>(
        (bearer, signingKeys, security, configuration) =>
        {
            SecurityOptions configured = security.Value;
            IConfigurationSection inbound = configuration.GetSection(inboundAuthenticationSection);

            bearer.RequireHttpsMetadata = inbound.GetValue("RequireHttpsMetadata", true);
            bearer.MapInboundClaims = inbound.GetValue("MapInboundClaims", false);

            bearer.TokenValidationParameters.ValidateIssuer =
                inbound.GetValue("ValidateIssuer", true);
            bearer.TokenValidationParameters.ValidateAudience =
                inbound.GetValue("ValidateAudience", true);
            bearer.TokenValidationParameters.ValidateLifetime =
                inbound.GetValue("ValidateLifetime", true);
            bearer.TokenValidationParameters.ValidateIssuerSigningKey =
                inbound.GetValue("ValidateIssuerSigningKey", true);

            bearer.TokenValidationParameters.ValidIssuer = configured.Issuer;

            // Only the plural member is populated. Setting JwtBearerOptions.Audience as well would
            // have the handler's own post-configure step derive a SECOND accepted value from it, so
            // the set this service accepts would have two sources able to disagree.
            bearer.TokenValidationParameters.ValidAudiences = ResolveInboundAudiences(
                configured,
                ReadInboundAudience(configuration, inboundAuthenticationSection));

            // The PUBLIC half of the signing key, taken from the layer that owns the private half. The
            // provider builds it from parameters exported with the private components EXCLUDED, so
            // there is nothing private in the object handed here rather than merely nothing exposed.
            bearer.TokenValidationParameters.IssuerSigningKey = signingKeys.PublicVerificationKey;

            // ZERO BY DEFAULT, DELIBERATELY DEPARTING FROM THE HANDLER'S OWN FIVE MINUTES. A tolerance
            // IS a relaxation of lifetime validation, and the configured token lifetime is measured in
            // minutes, so accepting the framework default would silently extend a five-minute token to
            // ten. It stays configurable for a deployment with genuine clock drift.
            bearer.TokenValidationParameters.ClockSkew =
                inbound.GetValue("ClockSkew", TimeSpan.Zero);
        });

// --------------------------------------------------------------------------------------------------
// 6. DEFAULT DENY
//
// A FALLBACK POLICY REQUIRING AN AUTHENTICATED USER, so that NO route on the service holding the
// system's single signing input is ever authenticated by omission (constraint C-G). Adding a route and
// forgetting its requirement CLOSES it here rather than opening it, which is the only ordering of that
// mistake that is safe.
//
// The three anonymous routes and every authenticated one state their requirement explicitly at their
// own declaration, inside Endpoints/. That is deliberate duplication: a requirement satisfied by
// omission is invisible at the thing it guards and would evaporate silently if this policy were ever
// relaxed, whereas an explicit call fails loudly if the route's intent ever changes.
// --------------------------------------------------------------------------------------------------
builder.Services.AddAuthorization(static options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --------------------------------------------------------------------------------------------------
// 7. THE PUBLISHED SURFACE - READINESS, THE SINGLE ERROR SHAPE, AND THE DOCUMENT
//
// Health-check registration ships INSIDE the Microsoft.AspNetCore.App shared framework, so /health
// needs no package reference and none is declared. The probe itself is in Endpoints/HealthEndpoints.cs,
// resolves the registry optionally, and performs no cryptographic work of any kind - which matters
// because the orchestration gates three dependents on its answer.
//
// ONE ERROR SHAPE, CONFIGURED ONCE. shared/PowerFramework.Contracts/OpenApi/security.v1.yaml declares
// exactly one error body for every 4xx and every 5xx in this service, carrying the legacy return code
// in the retCode extension member, and that document is AUTHORITATIVE for anything on the wire - where
// it and docs/CONTRACTS.md could ever disagree, the document wins. The customization below is what
// makes the framework's OWN responses honour it: the endpoint files already build their bodies through
// the shared problem factory, but the bearer challenge and an unhandled fault are produced by the
// framework with a status and no body at all, and a 401 with no retCode is a documented response this
// service would simply not be producing.
//
// IT IS ADDITIVE AND IDEMPOTENT. A body that already carries the member is left exactly as its author
// built it, so this step can never overwrite an endpoint's deliberate classification with a status-code
// approximation of it. The member name is read from the shared factory so that one spelling exists.
//
// Microsoft.OpenApi is pinned to 2.11.0 as a MANDATORY constraint held centrally at the repository root.
// 2.0.0 raises the NU1903 advisory, and moving to the 3.x line breaks the build with two CS0200
// "cannot assign, read only" errors inside the SDK's own generated OpenAPI support file, because the
// 10.0.x source generator is compiled against the 2.x object model. 2.11.0, the highest published 2.x,
// is the only value that is simultaneously non-vulnerable and compatible. Neither Swashbuckle nor
// Scalar is referenced: an interactive UI is not required, and the generator already produces the
// document.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();

builder.Services.AddProblemDetails(static options =>
    options.CustomizeProblemDetails = static context =>
    {
        if (context.ProblemDetails.Extensions.ContainsKey(ProblemResults.RetCodeExtensionMember))
        {
            return;
        }

        int status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;

        context.ProblemDetails.Extensions[ProblemResults.RetCodeExtensionMember] =
            ClassifyFailure(status);
    });

builder.Services.AddOpenApi();

WebApplication app = builder.Build();

// --------------------------------------------------------------------------------------------------
// 8. STARTUP ACQUISITION - THE INITIALIZE HALF OF THE LEGACY'S MANDATORY PAIRING
//
// The legacy framework requires initialization and finalization to be paired [pfwInitialize at
// ws_objects/pfw.pbl.src/pfw.sra:L91 against pfwFinalize at :L108, documented as required to be paired
// in docs/README.md]. This block is that pairing expressed structurally rather than as a bespoke
// lifecycle framework: the whole signing chain is ACQUIRED here, once, at startup, and the container
// releases it at graceful shutdown because the layer that owns the key is IDisposable and is a
// singleton of the root provider.
//
// EACH LINE CONVERTS A LATENT MISCONFIGURATION INTO A REFUSAL TO START.
//   * Resolving the options contract runs both validation passes now. Absent signing material, material
//     that is present but cannot be imported as an asymmetric private key, a blank issuer, an empty
//     audience roster, a missing key identifier, a non-positive lifetime, an algorithm outside the
//     accepted set, a metadata path outside the anonymous namespace or a token endpoint inside it - all
//     of them stop the host here, with the offending configuration key named and no value echoed.
//   * Resolving the minter performs the import and the credential checks its constructor owns, so a key
//     whose identifier disagrees with the one the key set publishes, or a symmetric key smuggled in,
//     fails now rather than on the first issuance request.
//   * The inbound audience check closes the one gap neither of the above can see: an inbound identity
//     that no token this issuer can mint would ever carry.
//
// It runs before the pipeline and the routes are wired so that the earliest possible failure is also
// the clearest one. Nothing here performs I/O, opens a connection or reaches another service, so an
// independent bring-up with no other service running is unaffected (constraints C-A and C-I).
// --------------------------------------------------------------------------------------------------
SecurityOptions issuance = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

_ = app.Services.GetRequiredService<TokenIssuer>();

// Read from the BUILT host's configuration for the same reason section 5 reads from the injected one:
// this is the only vantage point from which the final, fully-composed configuration is visible.
RequireIssuableInboundAudience(
    issuance,
    ReadInboundAudience(app.Configuration, inboundAuthenticationSection),
    inboundAuthenticationSection);

// --------------------------------------------------------------------------------------------------
// 9. THE PIPELINE
//
// ORDER IS THE WHOLE CONTENT OF THIS BLOCK, so it is stated rather than left to be inferred.
//
// The two diagnostics middlewares come FIRST because each works by observing what the middlewares
// beneath it produced, and the response they most need to observe is the bearer challenge written by
// the authentication middleware below. They are present for CONTRACT FIDELITY and not as hardening:
// security.v1.yaml declares that every non-2xx response of this service carries the single problem
// shape, and the framework's challenge and its unhandled-fault response both carry a status with NO
// body until one of these two writes one through the problem-details service configured in section 7.
// This is the narrow exception to the no-unrequested-middleware rule (constraint C-B) - the requirement
// that creates it is the published contract, and nothing else is added: no rate limiting, no CORS, no
// compression and no output caching.
//
// Authentication precedes authorization because a policy cannot evaluate a principal that has not been
// established yet. With the fallback policy from section 6 in force, that ordering is what turns an
// absent token into the documented 401 on every route that has not explicitly opted out.
// --------------------------------------------------------------------------------------------------
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// --------------------------------------------------------------------------------------------------
// 10. THE ROUTES - ONE CALL PER ENDPOINT FILE, IN THE ORDER A READER MEETS THEM
//
// Every requirement, every exemption and every declared response lives in the file that owns the route,
// which is what makes the handler logic unit-testable without a host and keeps this file a wiring
// manifest. NOT ONE AllowAnonymous CALL APPEARS BELOW: this service has exactly three anonymous routes
// and all three opt out at their own declaration (constraint C-G).
// --------------------------------------------------------------------------------------------------

// The generated contract document. It is NOT one of the three exemptions and is therefore governed by
// the default-deny fallback policy like every other route - a description of the surface is not itself
// a published operation, so it earns no exemption, and the authored specification under
// shared/PowerFramework.Contracts/OpenApi/ remains the artifact a consumer is handed.
app.MapOpenApi();

// The anonymous readiness probe, and the authenticated liveness route that is the standing proof this
// boundary is authenticated. /health must be anonymous on the sole issuer for a structural reason: a
// probe requiring a token could not answer until issuance was live, and issuance going live is the very
// thing the probe gates. /v1/ping requires a token and answers 401 without one.
app.MapHealthEndpoints();
app.MapPingEndpoints();

// Contract C-01's publication half: the key set and the discovery metadata this service's three
// consumers point their stock bearer handlers at. BOTH ROUTES ARE ANONYMOUS BY NECESSITY - a handler
// reads them in order to learn how to authenticate, so it cannot already hold a token - and each says
// so explicitly inside the file, because under the default-deny policy above omission would close the
// route instead of opening it. That registration also verifies that both configured addresses sit
// inside the well-known namespace and differ from one another, and fails the host when either condition
// does not hold.
app.MapJwksEndpoints();

// Contract C-01's issuance half, and the only route in the system that reaches a signing key. It is
// authenticated by a CLIENT CERTIFICATE rather than by a token, applied explicitly inside that file as
// a route-level authorization policy, because a caller cannot present a bearer token in order to obtain
// its first bearer token - which is why the published document applies its mutual-TLS scheme to that
// one operation as an override of the document-level bearer requirement. The requirement is enforced
// per operation rather than at the listener so that the anonymous routes above and the
// bearer-authenticated ones below stay reachable on this service's single listener; that registration
// also validates the configured issuance address and fails the host when it is blank, unrooted, or
// inside the anonymous metadata namespace.
app.MapTokenEndpoints();

// Contract C-02, the cryptographic surface. Every one of its operations requires a token and answers
// 401 without one, applied once on the route group inside the file rather than relied upon from the
// default-deny fallback above, so a route added later cannot become anonymous by omission.
app.MapCryptoEndpoints();

app.Run();

// ==================================================================================================
//  LOCAL FUNCTIONS
//
//  Declared at the foot of the composition so that the wiring above reads top to bottom. Each is
//  deliberately small, total over its input, and free of any dependency on the host - which is what
//  lets the sibling test project reason about the same decisions by asserting the behaviour they
//  produce on a booted host.
// ==================================================================================================

/// <summary>
/// Chooses the legacy return code that classifies a framework-produced failure response.
/// </summary>
/// <param name="statusCode">The HTTP status the framework is about to write.</param>
/// <returns>
/// A return code that is a genuine failure under the legacy algebra, never one that reads as a success
/// and never one the algebra treats as neither.
/// </returns>
/// <remarks>
/// <para>
/// EVERY ARM IS WRITTEN OUT DELIBERATELY RATHER THAN DERIVED FROM A TRUTHINESS TEST, which is the whole
/// requirement here. The codes and the statuses are taken from the published contract's own response
/// catalogue - a malformed request is <c>E_INVALID_ARGUMENT</c>, a refused caller is
/// <c>E_ACCESS_DENIED</c> whether the refusal was authentication or authorization, an absent resource is
/// <c>E_OBJECT_NOT_FOUND</c>, and an internal fault is <c>E_INTERNAL_ERROR</c> with <c>UNKNOWN</c> for a
/// condition that cannot be classified at all.
/// </para>
/// <para>
/// THE REFUSAL AND THE FORBIDDEN CASE SHARE ONE CODE, AND THAT ASYMMETRY IS PRESERVED RATHER THAN
/// PAPERED OVER. The legacy algebra declares exactly one access code and draws no distinction between
/// "no credential" and "credential without permission"; the published contract records the same
/// correspondence. The HTTP statuses stay distinct, so a caller can still tell the two apart - the code
/// simply does not gain a member the oracle never declared.
/// </para>
/// <para>
/// THE GUARD IS THE POINT OF THE FINAL CHECK. The return-code algebra is tri-state and has a documented
/// hole: <c>PREVENT</c> is 1 and <c>IsSucceeded</c> tests greater-than-or-equal-to zero, so a prevention
/// reads as a SUCCESS [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>,
/// <c>retcode.sru:L42</c>]; <c>CANCELLED</c> is -2 and is explicitly excluded from <c>IsFailed</c>, so a
/// cancellation is NEITHER succeeded nor failed [<c>isfailed.srf:L11-L13</c>,
/// <c>retcode.sru:L44-L45</c>]; and both predicates answer false on null. An error body must therefore
/// never carry a code from either of those two classes, and the kernel predicate is CONSUMED to enforce
/// that rather than the comparison being re-derived here. Every arm below already satisfies it; the
/// check exists so that a future edit which introduced one that did not would degrade to
/// <c>UNKNOWN</c> instead of publishing a failure that a consumer's own predicate would read as a
/// success.
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
        StatusCodes.Status503ServiceUnavailable => RetCode.E_BUSY,
        >= StatusCodes.Status500InternalServerError => RetCode.E_INTERNAL_ERROR,
        _ => RetCode.UNKNOWN,
    };

    return Predicates.IsFailed(classified) ? classified : RetCode.UNKNOWN;
}

/// <summary>
/// Reads this service's own declared inbound audience identity from a configuration root.
/// </summary>
/// <param name="configuration">The configuration to read, which must be a fully-composed one.</param>
/// <param name="sectionName">The inbound-validation section's name.</param>
/// <returns>The declared identity, trimmed, or an empty string when none is declared.</returns>
/// <remarks>
/// ONE READER SO THERE IS ONE SPELLING OF THE KEY. Section 5 configures the handler from it and section
/// 8 gates startup on it; two independent reads of the same key would be two places for a rename to go
/// half-applied. Trimming is applied because a settings value that acquired surrounding whitespace would
/// otherwise become an audience no token could ever carry, and an empty result is a legitimate answer
/// meaning "not declared" rather than a failure - the companion resolver treats it as such.
/// </remarks>
static string ReadInboundAudience(IConfiguration configuration, string sectionName)
{
    ArgumentNullException.ThrowIfNull(configuration);

    return (configuration.GetSection(sectionName)["Audience"] ?? string.Empty).Trim();
}

/// <summary>
/// Resolves the audience identities this service accepts on an inbound token.
/// </summary>
/// <param name="configured">The validated issuance settings.</param>
/// <param name="declaredAudience">
/// This service's own identity as declared for inbound validation, already trimmed, or an empty string
/// when a deployment declares none.
/// </param>
/// <returns>The accepted audience set, never empty.</returns>
/// <remarks>
/// <para>
/// THE NARROW ANSWER IS THE PREFERRED ONE. When a deployment declares this service's own identity, that
/// single identity is the accepted set, because the entire purpose of audience validation is that a
/// token addressed to one service is not replayable at another - and every identity in the issuance
/// roster belongs to a DIFFERENT service in the same trust domain. Accepting the whole roster would
/// defeat the check while appearing to perform it.
/// </para>
/// <para>
/// THE ROSTER IS THE FALLBACK, NOT THE DEFAULT. A deployment that declares no inbound identity gets the
/// roster, which keeps the authenticated ping route reachable rather than silently unreachable; the
/// roster is validated non-empty before this is called, so the returned set can never be empty and
/// audience validation can never degenerate into accepting everything. A deployment that declares an
/// identity the roster does not contain is refused at startup instead - see the companion check - since
/// no token this issuer can mint would ever carry it.
/// </para>
/// <para>
/// Ordinal comparison, because an audience is an opaque protocol identifier: two spellings differing
/// only by case are two different audiences, and folding them together would accept one the deployment
/// never declared. The materialized array is a snapshot, so a later mutation of the roster cannot widen
/// the set the handler is already validating against.
/// </para>
/// </remarks>
static string[] ResolveInboundAudiences(SecurityOptions configured, string declaredAudience)
{
    ArgumentNullException.ThrowIfNull(configured);

    return string.IsNullOrEmpty(declaredAudience)
        ? [.. configured.Audiences]
        : [declaredAudience];
}

/// <summary>
/// Refuses to start when the declared inbound audience is not an identity this issuer can mint for.
/// </summary>
/// <param name="configured">The validated issuance settings.</param>
/// <param name="declaredAudience">
/// This service's own identity as declared for inbound validation, already trimmed, or an empty string
/// when a deployment declares none.
/// </param>
/// <param name="sectionName">
/// The configuration section the identity was read from, so the diagnostic names the exact key an
/// operator has to change.
/// </param>
/// <exception cref="InvalidOperationException">
/// The declared identity is absent from the issuance roster.
/// </exception>
/// <remarks>
/// <para>
/// THE FAULT THIS CATCHES IS INVISIBLE EVERY OTHER WAY. Both halves of the configuration would be
/// individually valid, the service would report healthy, every consumer would obtain tokens
/// successfully, and only <c>/v1/ping</c> on THIS service would refuse every token forever - a
/// contract-declared response failing while nothing in the deployment looked wrong. Converting it into
/// a refusal to start is the same posture the legacy takes on a decoded structural fault, which ends the
/// process rather than continuing past it [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, ending in
/// <c>HALT CLOSE</c> at <c>:L143</c>].
/// </para>
/// <para>
/// NO VALUE IS ECHOED. The message names the two configuration keys and states the relationship that
/// must hold between them; neither the declared identity nor any roster entry appears in it, which keeps
/// this diagnostic consistent with every other startup failure in this service (constraint C-F).
/// </para>
/// <para>
/// An undeclared identity is not a fault: the companion resolver answers it with the roster, which is a
/// wider but still closed set. Only a declared identity that the roster cannot produce is unrecoverable.
/// </para>
/// </remarks>
static void RequireIssuableInboundAudience(
    SecurityOptions configured,
    string declaredAudience,
    string sectionName)
{
    ArgumentNullException.ThrowIfNull(configured);

    if (string.IsNullOrEmpty(declaredAudience))
    {
        return;
    }

    foreach (string audience in configured.Audiences)
    {
        if (string.Equals(audience, declaredAudience, StringComparison.Ordinal))
        {
            return;
        }
    }

    throw new InvalidOperationException(
        $"Configuration key '{sectionName}:Audience' declares an inbound audience that is not a "
        + $"member of '{SecurityOptions.SectionName}:{nameof(SecurityOptions.Audiences)}'. This "
        + "service validates inbound tokens against its own identity, and that identity must be one "
        + "this issuer is permitted to mint for, or the authenticated ping route would refuse every "
        + "token while the service reported healthy. Add the identity to the issuance roster, or "
        + "correct the declared audience, and restart. This message never echoes either configured "
        + "value.");
}

/// <summary>
/// The reachable entry-point type for the in-process service tests.
/// </summary>
/// <remarks>
/// LOAD BEARING, NOT CEREMONIAL. Top-level statements compile to an internal <c>Program</c> class, so
/// without this declaration <c>WebApplicationFactory&lt;Program&gt;</c> in the sibling
/// <c>PowerFramework.Security.Tests</c> project cannot name the entry point, the service-level tests
/// cannot boot this host at all, and the per-service coverage gate becomes unreachable for every line in
/// this file.
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
