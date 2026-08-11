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

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Authorization;
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
// --------------------------------------------------------------------------------------------------
// 0. THE LISTENER HANDS A CLIENT CERTIFICATE TO THE APPLICATION AND DECIDES NOTHING ABOUT IT.
//
// appsettings.json sets ClientCertificateMode to AllowCertificate, which makes the handshake REQUEST a
// certificate without demanding one - so /health stays anonymously reachable and the C-02 operations
// stay bearer-authenticated, while the issuance operation can require one per route. What that setting
// does NOT do on its own is decide which certificates are acceptable: with no validation callback the
// listener applies the platform default, which REJECTS THE WHOLE CONNECTION for any certificate that
// does not chain to the container's OS trust store. That default is wrong here in both directions.
//
//   * TOO STRICT, AND IT BREAKS THE INTENDED DEPLOYMENT. A caller certificate issued by the
//     deployment's own local authority - which is exactly what docs/ARCHITECTURE.md's generation recipe
//     produces - is not in any base image's trust store, so the handshake would fail and the
//     application would never see the certificate at all. The published 401 for an untrusted
//     certificate could not be produced, because there would be no request to produce it on.
//   * TOO BROAD, AND IT IS UNSTATEABLE. The store it consults already trusts every public root shipped
//     in the image, so the set of issuers able to present a caller identity would be as wide as the
//     public web PKI - written down nowhere and under nobody's control.
//   * AND IT REFUSES AT THE WRONG GRANULARITY. Rejecting the CONNECTION denies that caller /health, the
//     published key set, the discovery document and every C-02 operation as well, which is three
//     contracts broken to enforce one - the same argument appsettings.json records for not requiring a
//     certificate at the listener.
//
// So the listener accepts the connection and defers, and THAT IS NOT A RELAXATION: a client certificate
// confers nothing by being accepted here. Every route on this service is anonymous by contract or
// bearer-authenticated, with exactly one exception - POST /v1/tokens - and that operation validates the
// certificate against the explicitly configured trust anchor before it reads one byte of identity from
// it, answering the contract's own 401 when it does not establish itself. The decision lives in
// Tokens/ClientCertificateTrust.cs, where a test can drive it and a reader can audit it, rather than in
// a trust store nothing in this repository can observe.
//
// Nothing else about TLS is touched: the protocol set, the server certificate and the revocation
// posture of the SERVER side all stay as configured, and no server-certificate validation anywhere is
// relaxed.
// --------------------------------------------------------------------------------------------------
builder.WebHost.ConfigureKestrel(static kestrel =>
    kestrel.ConfigureHttpsDefaults(static https =>
        https.ClientCertificateValidation = static (_, _, _) => true));

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
// CALLER-CERTIFICATE TRUST - THE HALF OF THE MUTUAL-TLS EDGE THAT WAS MISSING
//
// POST /v1/tokens authenticates its caller by client certificate and by nothing else, and
// Endpoints/TokenEndpoints.cs reconciles that certificate's common name against the claimed subject. A
// NAME PROVES NOTHING ON ITS OWN: unless the certificate's chain is verified against a known authority,
// any caller can present a self-signed certificate whose common name is `powerframework-gateway` and be
// issued a Gateway token for every audience Gateway is allowed. The documented topology issues caller
// certificates from a LOCAL authority that is in no container's operating-system trust store, so the
// platform cannot make that decision either - which left the strongest identity claim in the system
// resting on an unverified string.
//
// The anchor is read from configuration HERE, before Build(), for two reasons. The validation callback
// belongs to the HTTPS defaults, which are configured on the host builder rather than resolved from the
// container; and loading eagerly means a configured-but-unreadable anchor is a refusal to start rather
// than a handshake failure discovered by the first caller.
//
// TRUST IS NARROWED, NEVER RELAXED. There is no AllowAnyClientCertificate call anywhere in this file:
// that method REPLACES the callback below and would accept every certificate presented, which is the
// exact defect this replaces rather than a shortcut to it. With no anchor configured the callback defers
// to the platform's own verdict, which is what Kestrel would have done unaided.
// --------------------------------------------------------------------------------------------------
CallerCertificateTrust callerCertificateTrust = CallerCertificateTrust.Load(
    builder.Configuration[
        $"{SecurityOptions.SectionName}:{nameof(SecurityOptions.MutualTls)}:"
            + $"{nameof(SecurityMutualTlsOptions.ClientCaPath)}"]);

builder.Services.AddSingleton(callerCertificateTrust);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureHttpsDefaults(
    https => https.ClientCertificateValidation = callerCertificateTrust.Validate));

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

// THE ISSUANCE ROSTER, REGISTERED BEFORE THE MINTER THAT DEPENDS ON IT AND SHARED WITH THE ISSUANCE
// EDGE. One instance, deliberately: Endpoints/TokenEndpoints.cs authenticates a presented credential
// against it and Tokens/TokenIssuer.cs decides what the resulting identity may ask for against it, and
// two independently built rosters could disagree - authenticating a caller under one set of permissions
// and authorising it under another. A singleton also means every configured secret is UTF-8 encoded once
// at startup rather than on the hot path of the one operation that cannot be cached.
//
// ITS CONSTRUCTOR IS A GATE. It resolves every secret the roster names and refuses to construct when a
// named configuration key resolves to nothing, so a deployment that meant to supply a credential and did
// not is a startup failure rather than a caller that mysteriously cannot authenticate. Section 8
// resolves the minter during startup, which resolves this type with it.
builder.Services.AddSingleton<IssuanceClientRegistry>();

builder.Services.AddSingleton<TokenIssuer>();

// THE TRANSPORT IDENTITY'S TRUST DECISION, WHICH IS A DIFFERENT KEY WITH A DIFFERENT LIFETIME FROM THE
// SIGNING ONE AND IS THEREFORE A SEPARATE REGISTRATION. It is what makes the published 401 on the
// issuance operation - "no certificate was presented, OR the certificate presented is not trusted" - a
// behaviour rather than a promise: the operation consults it BEFORE reading the certificate's common
// name, because that name IS the caller identity and honouring one from a certificate whose issuer was
// never established would be authentication by assertion.
//
// A SINGLETON THAT LOADS ITS ANCHOR ONCE, and IDisposable, so the container releases the loaded
// certificates at shutdown exactly as it releases the signing key. Section 8 resolves it eagerly, which
// is where a configured-but-unreadable anchor path refuses the host.
//
// AN UNCONFIGURED ANCHOR IS A LEGITIMATE, FAIL-CLOSED STATE and not a startup fault: nothing is
// trusted, issuance refuses every caller with the contract's own 401, and /health, the published key
// set, the discovery document and every C-02 operation stay reachable - so the readiness chain the other
// three services wait on is unaffected. Failing startup instead would make a bring-up of the rest of the
// stack impossible for a service that, without an anchor, could not have honoured a certificate anyway.
builder.Services.AddSingleton<ClientCertificateTrust>();

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

            // RequireHttpsMetadata IS DELIBERATELY NOT ASSIGNED HERE, AND ITS ABSENCE IS THE POINT.
            //
            // The setting governs ONE thing: whether the handler will fetch discovery metadata over
            // plaintext. It is consulted only when a ConfigurationManager exists, which the handler
            // builds only when an Authority or a MetadataAddress is configured. This handler
            // deliberately configures NEITHER - see the block above: Security validates the tokens it
            // minted itself and takes its verification key IN PROCESS from the signing-key layer, so it
            // performs no metadata retrieval of any kind and there is no fetch for the setting to
            // govern.
            //
            // An earlier revision read it from configuration here. That was worse than harmless: an
            // operator reading this block, or the settings file that carried the key, would reasonably
            // conclude Security's metadata retrieval was being held to HTTPS - when Security has no
            // metadata retrieval at all. A setting that appears to be enforced and governs nothing is a
            // false assurance, so the read and its now-dead settings key were both removed rather than
            // left in place for symmetry with the other three services.
            //
            // THE SETTING REMAINS LIVE AND MEANINGFUL ON GATEWAY, DATASERVICES AND PERSISTENCE, which
            // DO fetch this service's key set and discovery document over HTTP and where the property
            // therefore has a fetch to govern. Nothing here relaxes it there. Should this handler ever
            // acquire an Authority, the platform default is already the safe value - true - so the
            // absence of an assignment cannot become a plaintext fetch by omission.
            bearer.MapInboundClaims = inbound.GetValue("MapInboundClaims", false);

            // ALL FOUR ARE ASSIGNED LITERALLY, NOT READ. Each removes an entire class of forgery, so
            // none is a deployment choice: without issuer validation a credential from any issuer is
            // accepted, and THIS service is the issuer, so it would accept forgeries of its own
            // authority; without audience validation a credential minted for Gateway, DataServices or
            // Persistence is replayable here, which is exactly what the one-audience-per-token rule of
            // contract C-01 exists to prevent; without lifetime validation the short lifetimes this
            // service itself mints would bound nothing; without signing-key validation the signature is
            // not verified at all. Reading them with a safe default still left a configuration path that
            // could turn one OFF while this host reported healthy - an unauthenticated boundary wearing
            // the shape of an authenticated one, which constraint C-G forbids. The keys survive in the
            // settings file so a deployment can be audited by reading it, and section 8 refuses to start
            // a host that sets one to false rather than ignoring the value in silence.
            bearer.TokenValidationParameters.ValidateIssuer = true;
            bearer.TokenValidationParameters.ValidateAudience = true;
            bearer.TokenValidationParameters.ValidateLifetime = true;
            bearer.TokenValidationParameters.ValidateIssuerSigningKey = true;

            // INBOUND CLAIM MAPPING IS OFF, AND FOR THIS SERVICE IT IS LOAD BEARING RATHER THAN TIDY.
            // The legacy handler rewrites standard claim names into WS-Federation URIs, so `scope` and
            // `sub` would arrive under names the scope policy below does not look for and every scope
            // check would silently pass nothing. It is hardcoded for the same reason as the four above:
            // the issuer writes its claims through a dictionary precisely so they are NOT mapped on the
            // way out, and mapping on the way back would break the round trip this service's own tests
            // assert.
            bearer.MapInboundClaims = false;

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

            // ZERO, UNCONDITIONALLY, DEPARTING FROM THE HANDLER'S OWN FIVE MINUTES.
            //
            // A tolerance IS a relaxation of lifetime validation: it extends every token's usable life
            // past its own `exp`, and the configured lifetime here is measured in minutes, so the
            // framework default would silently turn a five-minute token into a ten-minute one. Zero is
            // additionally the only correct value for THIS service specifically: the tokens it validates
            // are tokens it minted itself, moments earlier, on the same host and from the same clock -
            // there is no second clock for a tolerance to accommodate.
            //
            // An earlier form read this from configuration with zero as the default, which left the
            // window WIDENABLE from a settings file, an environment variable or a container definition
            // to any value at all - including one exceeding the token lifetime, at which point the
            // expiry check does not expire. It is a constant for the same reason the four checks above
            // are: a bound nothing can move is a bound.
            bearer.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
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
// AND THE CRYPTOGRAPHIC SURFACE GETS A NAMED POLICY OF ITS OWN, BECAUSE AUTHENTICATION IS NOT
// AUTHORIZATION. The fallback below closes the door on a route that declares nothing; that policy decides
// WHO may open the C-02 surface and for WHAT. Requiring only an authenticated principal meant any holder of
// any token minted for this service's audience could drive all 17 operations - keyed HMAC, symmetric
// encryption and decryption, RSA signing and RSA key generation among them - whatever the credential was
// obtained for and whichever caller it was minted for (CWE-862, CWE-863). Contract C-02 says who the
// surface is for: Security serves DataServices, and DataServices requests exactly `security.crypto`.
builder.Services.AddAuthorization(static options =>
{
    options.AddPolicy(CryptoCallerAuthorization.PolicyName, CryptoCallerAuthorization.Configure);

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// AUTHENTICATION IS NOT AUTHORIZATION, AND THE FALLBACK ABOVE ONLY DELIVERS THE FIRST. Every token this
// service mints carries a `scope` claim, and security.v1.yaml publishes a 403 on thirteen operations
// whose description says the token is valid but does not carry the scope the operation requires. Without
// the policies registered here nothing read that claim: any token addressed to this service reached
// every authenticated route, whatever it was scoped to, so the claim was decorative and a caller granted
// the cryptographic surface for one purpose held all of it.
//
// The mechanism, the reason the framework's own claim requirement cannot express it (the claim is ONE
// value carrying a SPACE-DELIMITED set), and the reason it is duplicated per service rather than shared
// (the contracts project is the only permitted cross-service coupling and carries no behaviour) are all
// recorded in Authorization/ScopeAuthorization.cs. The routes that name a policy do so at their own
// declaration in Endpoints/, following the same explicitness rule as the anonymous exemptions.
builder.Services.AddScopeAuthorization();

// --------------------------------------------------------------------------------------------------
// 7. THE PUBLISHED SURFACE - READINESS, THE SINGLE ERROR SHAPE, THE JSON READER, AND THE DOCUMENT
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

// --------------------------------------------------------------------------------------------------
// THE CLOSED REQUEST SCHEMAS, ENFORCED AT RUN TIME RATHER THAN ONLY PUBLISHED.
//
// Every request schema in shared/PowerFramework.Contracts/OpenApi/security.v1.yaml sets
// `additionalProperties: false`. That is a statement about what this service accepts, and until this
// line it was a statement only the DOCUMENT made: System.Text.Json's default UnmappedMemberHandling is
// Skip, so an undeclared member was read, silently discarded, and the request processed as though the
// caller had not sent it.
//
// WHY SILENTLY DISCARDING IT IS THE WRONG ANSWER ON THIS SERVICE IN PARTICULAR. The members a caller
// might plausibly add are the ones that change a cryptographic decision: a `mode`, a `padding`, a
// `keyRef`, a `bits`. A caller that misspells one - `keyReference` for `keyRef`, `padding` on an
// operation that takes none - gets a 200 computed under this service's DEFAULTS instead of under the
// parameters it believes it supplied, and those defaults are the preserved legacy weak ones: ECB for a
// symmetric mode, PKCS#1 for RSA padding. The failure is therefore not "a field was ignored", it is
// "the operation succeeded with weaker parameters than the caller asked for, and said so nowhere".
// Refusing the request is the only answer that cannot be mistaken for the one the caller wanted.
//
// Disallow makes the binder throw on an undeclared member. What the caller then sees is NOT the
// framework's bodiless 400: the refusal registered on the request pipeline in section 9
// (Endpoints/MalformedRequestBody.cs) converts that exception into the published problem body carrying
// a FIXED detail and E_INVALID_ARGUMENT. The distinction matters and is deliberate - the body names no
// member and echoes no part of the request, because on the one service whose request text may itself be
// key material a body describing WHICH member was refused would echo caller-supplied text. The caller
// learns that the body was unreadable and nothing further; the published schema already says which
// members exist.
//
// PropertyNameCaseInsensitive is left at its default of false for the same reason: the schema declares
// exact member names, and accepting `KeyRef` for `keyRef` would be a second undeclared spelling.
//
// THE SETTING ITSELF IS APPLIED EXACTLY ONCE, at the single reader configuration further down this file
// ("ONE JSON READER, CONFIGURED ONCE"). It is stated there rather than here because that block also pins
// the case sensitivity, and the two are one decision about one reader. Registering it twice would be
// harmless - Configure callbacks are additive - but it would leave two places to change and two places
// to disagree.
// --------------------------------------------------------------------------------------------------

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

// THE GENERATED DOCUMENT IS BROUGHT BACK TO THE AUTHORED ONE. The generator drops schema closure and
// types every required member as admitting null, both of which the authored document states otherwise;
// PublishedSchemaFidelity restores them and explains why each matters. It edits the document only - no
// binding, no validation and no response changes.
builder.Services.AddOpenApi(PublishedSchemaFidelity.Configure);

// UNKNOWN JSON MEMBERS ARE REFUSED, WHICH IS THE OTHER HALF OF `additionalProperties: false`.
//
// A schema that forbids a member is a statement about what the SERVICE accepts, and a document-validating
// client is only one of the two parties that has to honour it. Left at the serializer default an unknown
// member is silently discarded, so a caller that misspells `payloadForm` as `payloadform` gets the
// absent-member refusal for a member it believes it sent, and a caller that sends a member this service
// removed in a later version is told nothing at all. Disallow turns both into a refusal that names the
// member.
//
// THIS DOES NOT DISPLACE THE HANDLERS' OWN VALIDATION AND MUST NOT. The crypto and issuance request shapes
// declare nullable members deliberately, so that an ABSENT member reaches the handler and is refused with
// E_INVALID_ARGUMENT naming its wire spelling. Disallow fires on a member that is PRESENT AND UNKNOWN,
// which is a disjoint condition: nothing that reached a handler arm before reaches the serializer's
// refusal now. The mapping registered on the request pipeline converts the serializer's exception into
// the same problem body, so the two refusals are indistinguishable in shape to a caller.
//
// Applied once, at the single reader configuration below.

// THE SETTING IS PINNED RATHER THAN LEFT TO THE ENVIRONMENT, WHICH IS THE ONLY WAY ONE CONTRACT CAN HOLD
// EVERYWHERE. Left alone, ThrowOnBadRequest is true in Development and false elsewhere, so one malformed
// body produces an exception in one deployment and a bodiless 400 in another - two different answers to the
// same request, only one of which carries the published error shape. Pinned true, the refusal registered on
// the pipeline answers every one of them identically. It is not a diagnostic setting here: nothing about the
// exception reaches the caller or the log, only the fixed refusal does.
builder.Services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = true);

// --------------------------------------------------------------------------------------------------
// ONE JSON READER, CONFIGURED ONCE, MATCHING THE PUBLISHED REQUEST SCHEMAS EXACTLY.
//
// Every request schema in shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is CLOSED with
// additionalProperties: false, and every member spelling in it is pinned on the corresponding record
// with an explicit JsonPropertyName attribute rather than left to a naming policy. The serializer's
// web defaults are more permissive than that document on two independent axes, and both are corrected
// here rather than per record, because a per-record attribute is a decision a later record can be
// added without.
//
//   * UNDECLARED MEMBERS ARE REFUSED. The default is to skip them silently, which means this service
//     would accept a body that the document it publishes forbids, and a client validating against that
//     document would refuse a request this service had already honoured. Refusing here is what makes
//     the two agree. It also closes the shape of smuggling the closed schemas exist to prevent: an
//     undeclared member named for key material is now a refusal rather than a member that binds
//     nowhere and is quietly dropped - see the raw-key-material rule on contract C-02.
//   * MEMBER NAMES ARE MATCHED EXACTLY. The web defaults match case-insensitively, so "Subject" would
//     bind to the member the document spells "subject". A document-validating client refuses that
//     spelling as an undeclared member, so accepting it is the same divergence as the point above
//     wearing a different hat.
//
// NEITHER IS A LEGACY BEHAVIOUR CHANGE, AND THERE IS NO LEGACY BEHAVIOUR HERE TO CHANGE (constraint
// C-B): PowerFramework is a library with no listener, no route and no wire format of any kind, so this
// service's request bodies are net-new and the authored contract document is the only authority over
// what they accept. Nothing about response WRITING is touched by either setting - the response member
// spellings, their casing and their order are unchanged, and the RFC 6749 spellings on the issuance
// result stay exactly as that record pins them.
//
// The payload-form enumeration is NOT governed from here. Its converter is declared on the type in
// Endpoints/CryptoEndpoints.cs, so it applies to every path that reads that value - the generated
// document, a hand-built serializer in a test, and this pipeline - rather than only to bodies that
// happen to arrive through the configured reader.
// --------------------------------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(static options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
});

// --------------------------------------------------------------------------------------------------
// A BODY THE READER ABOVE REFUSES IS THE PUBLISHED 400, IN EVERY ENVIRONMENT - AND THE PIN THAT MAKES
// THAT TRUE IS `ThrowOnBadRequest = true`, ABOVE. THIS BLOCK RECORDS WHY IT IS NOT `false`.
//
// The framework's route-handler option that decides this DEFAULTS TO THROWING IN DEVELOPMENT and to
// writing a 400 everywhere else, so a malformed body is answered as the documented bad request in a
// container and as a 500 on a developer's machine. Measured on this toolchain, both a truncated body and
// an undeclared member produced a 500 carrying the internal-failure return code under the development
// default - a status security.v1.yaml does not declare for either case, on the one service that holds
// the system's signing input, and the status that pages an operator. So the setting must be pinned
// rather than inherited. That much is settled; the only question is WHICH value.
//
// TWO MECHANISMS CAN PRODUCE THE PUBLISHED 400, AND THEY ARE MUTUALLY EXCLUSIVE.
//   * SUPPRESS THE THROW (`false`). The framework writes a bodiless 400 and the status-code middleware
//     in section 9 fills in a problem body through the problem-details service. Cheap, but the body is
//     a GENERIC status-code rendering: it carries no fixed detail sentence, so a caller cannot tell a
//     body this service could not READ from any other 400 the service might answer.
//   * PIN THE THROW (`true`) AND MAP THE EXCEPTION. Endpoints/MalformedRequestBody.cs recognises the
//     binder's own exception shapes and writes ONE fixed refusal - `MalformedRequestBody.RefusalDetail`
//     plus E_INVALID_ARGUMENT - for every unreadable body, whatever made it unreadable. That is a
//     stronger contract than the generic rendering, and it is the one this service publishes.
//
// THE SECOND IS WHAT SHIPS, so the pin is `true` and NOT `false`. Setting it false here would not merely
// choose the weaker body: `MalformedRequestBody.Use(app)` would then never see an exception at all, so
// the shipped mapping would become dead code while still appearing wired, and the refusal detail the
// published document promises would silently stop being written. Both values are individually
// defensible; holding both at once is not, and the later registration would win.
//
// NOTHING IS LOST DIAGNOSTICALLY UNDER THE PINNED THROW. The framework still records the binding failure
// on its own logger with the parameter and the underlying exception; only the propagation to the client
// is replaced, by the response the contract declares. The refusal carries the same single error shape and
// the same retCode member as every other non-2xx response of this service.
// --------------------------------------------------------------------------------------------------

// The schema transformer restores ONE schema the generator cannot describe on its own: the
// payload-form selector. That member carries a hand-written converter, because the stock string-enum
// converter matches its token names case-insensitively and this contract declares exactly two tokens -
// and the schema exporter can describe the stock converter but not a custom one, so without this the
// generated document would say "anything" where the authored document says enum: [STRING, BLOB]. The
// transformer lives beside the type it describes, in Endpoints/CryptoEndpoints.cs, and restates the
// authored document and nothing else.
builder.Services.AddOpenApi(static options =>
    options.AddSchemaTransformer<PayloadFormSchemaTransformer>());

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

// Resolved eagerly for the same reason, and it is the half of the mutual-TLS story that a lazy
// registration would get wrong. A CONFIGURED-BUT-UNREADABLE trust anchor is a structural fault: the
// deployment declared which authority may mint caller identities and this service cannot read it, so
// every issuance request it will ever receive is already doomed. Discovering that at startup is a failure
// to launch; discovering it on the first token request is an outage that looks like a caller problem. An
// UNSET anchor resolves successfully, records one warning, and trusts nothing - the fail-closed state,
// which is a legitimate posture rather than a fault.
_ = app.Services.GetRequiredService<ClientCertificateTrust>();

// Read from the BUILT host's configuration for the same reason section 5 reads from the injected one:
// this is the only vantage point from which the final, fully-composed configuration is visible.
RequireIssuableInboundAudience(
    issuance,
    ReadInboundAudience(app.Configuration, inboundAuthenticationSection),
    inboundAuthenticationSection);

// AND THE FOUR TOKEN-VALIDATION SWITCHES ARE INVARIANT, WHICH THIS LINE MAKES ENFORCEABLE. Section 5
// assigns all four literally, so a configured false takes no effect - and a setting that is silently
// ignored is worse than one that is honoured, because an operator would believe it applied. Refusing to
// start says plainly that the value is neither honoured nor honourable. Read from the BUILT host's
// configuration for the same reason the two lines above are: this is the only vantage point from which
// the final, fully-composed configuration is visible, so a value contributed by a later source is seen.
RequireInvariantTokenValidation(app.Configuration, inboundAuthenticationSection);

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
// A STATUS SELECTOR RATHER THAN THE PARAMETERLESS FORM, AND IT IS A CONTRACT-FIDELITY REQUIREMENT.
//
// The parameterless form answers 500 for EVERY exception, including the ones the framework raises to
// report a CALLER error. The one that matters here is BadHttpRequestException, which body binding raises
// when the payload cannot be deserialised - and which carries its own intended status, 400, that the
// parameterless form discards.
//
// WHY THIS BECAME REACHABLE. Section 7 configures the JSON layer to REFUSE an undeclared member, which
// is what makes `additionalProperties: false` in the published schema an enforced rule rather than a
// documented wish. A caller that misspells `keyRef` therefore hits body binding, which raises
// BadHttpRequestException(400) - and without this selector the caller would be told the SERVICE failed
// when in fact its own request was malformed. The published contract declares 400 for a malformed body
// and 500 for a fault of this service, so answering 500 there would be this service reporting the wrong
// party at fault and inviting an operator to investigate a service that behaved correctly.
//
// NOTHING ELSE CHANGES SHAPE. Every other exception still answers 500, the problem-details service
// configured in section 7 still writes the single published error shape with its legacy return code, and
// the body of a refusal still carries no caller-supplied text - the framework's own message names the
// parameter and its type and never a value, and this service's error classifier derives the return code
// from the status alone.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = static exception => exception is BadHttpRequestException malformed
        ? malformed.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

// A BODY THIS SERVICE CANNOT READ IS A CLIENT ERROR, AND IS ANSWERED INSIDE THE HANDLER ABOVE RATHER THAN
// BY IT. The serializer refuses a member the published schema does not declare - that is what
// `additionalProperties: false` means on the receiving side - and a truncated body fails the same way. Both
// are 400s, and both must read identically in every environment. Placing the refusal INSIDE the exception
// handler is what keeps the serializer's exception, whose message quotes the offending payload fragment,
// out of the unhandled-fault log record on a surface whose payloads are plaintext, ciphertext and key
// references. A fault it does not recognise is rethrown, so the 500 path is unchanged.
MalformedRequestBody.Use(app);

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
/// Refuses to start a host that has turned off any of the four inbound token-validation checks.
/// </summary>
/// <param name="configuration">The built host's configuration.</param>
/// <param name="sectionName">The inbound-authentication section the switches are read from.</param>
/// <exception cref="ArgumentNullException">
/// <paramref name="configuration"/> is <see langword="null"/>.
/// </exception>
/// <exception cref="InvalidOperationException">Any of the four switches is configured false.</exception>
/// <remarks>
/// <para>
/// EACH OF THE FOUR REMOVES A WHOLE CLASS OF FORGERY, AND THIS SERVICE HAS THE MOST TO LOSE FROM IT.
/// Without issuer validation a credential from any issuer is accepted, and this service IS the issuer,
/// so it would honour forgeries of its own authority. Without audience validation a credential minted for
/// Gateway, DataServices or Persistence is replayable at the cryptographic surface, which is precisely
/// what the one-audience-per-token rule of contract C-01 exists to prevent. Without lifetime validation
/// the short lifetimes this service itself mints bound nothing. Without signing-key validation the
/// signature is not verified at all and any well-formed token is accepted. None of the four is a
/// deployment choice, so section 5 assigns all four literally.
/// </para>
/// <para>
/// AND A CONFIGURED FALSE IS REFUSED RATHER THAN IGNORED. Because the assignment is literal, a false
/// value would take no effect - which is the more dangerous of the two failures, since an operator would
/// believe the switch applied. Refusing to start is how the host says the value is neither honoured nor
/// honourable. Every message names the exact key and states what the check protects; no configured value
/// other than the offending boolean is echoed. ALL FOUR are reported together rather than one at a time,
/// so a deployment with several disabled is fixed in one pass rather than in four restarts.
/// </para>
/// </remarks>
static void RequireInvariantTokenValidation(IConfiguration configuration, string sectionName)
{
    ArgumentNullException.ThrowIfNull(configuration);

    IConfigurationSection inbound = configuration.GetSection(sectionName);

    string[] switches =
    [
        "ValidateIssuer",
        "ValidateAudience",
        "ValidateLifetime",
        "ValidateIssuerSigningKey",
    ];

    List<string> disabled = [];

    foreach (string name in switches)
    {
        // The default is the safe value, so an ABSENT key is not a fault - it is the ordinary case, and
        // the shipped settings file states all four explicitly only so a reviewer can see them.
        if (!inbound.GetValue(name, true))
        {
            disabled.Add(string.Concat("'", sectionName, ":", name, "'"));
        }
    }

    if (disabled.Count == 0)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Configuration key(s) {string.Join(", ", disabled)} are set to false. Each of the four inbound "
        + "token-validation checks removes an entire class of forgery, so none of them is a deployment "
        + "choice: without issuer validation a credential from any issuer is accepted - and this service "
        + "is the issuer; without audience validation a credential minted for another service is "
        + "replayable here; without lifetime validation the short lifetimes this service mints bound "
        + "nothing; without signing-key validation the signature is not verified at all. The bearer "
        + "handler is configured with all four enabled regardless of these values, so the settings would "
        + "not take effect - and a setting that is silently ignored is worse than one that is honoured. "
        + "Remove the key(s) or set them to true, and restart.");
}

/// <summary>
/// Decides whether a client certificate presented on the mutual-TLS issuance edge chains to an authority
/// this deployment accepts.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS TYPE EXISTS. <c>POST /v1/tokens</c> derives the caller's identity from the presented
/// certificate's common name, and a name is only as good as the chain behind it. Without this, a caller
/// could present a self-signed certificate naming any service in the system and be minted that service's
/// token. The documented topology issues caller certificates from a local authority absent from every
/// container's trust store, so the anchor has to be supplied explicitly for the chain to be checkable at
/// all.
/// </para>
/// <para>
/// IT IS A NARROWING, NOT A CALLBACK-SHAPED BYPASS. <see cref="Validate"/> never returns
/// <see langword="true"/> for a certificate that fails to chain: with an anchor configured the chain must
/// build to that anchor under <see cref="X509ChainTrustMode.CustomRootTrust"/>, and with none configured
/// the platform's own verdict is returned unmodified. There is no arm that accepts a certificate because
/// validation was inconvenient, and <c>AllowAnyClientCertificate</c> is deliberately not called anywhere
/// in this service.
/// </para>
/// <para>
/// REVOCATION IS NOT CHECKED, AS A CONSEQUENCE OF THE TOPOLOGY RATHER THAN AS A RELAXATION. A local
/// authority generated by two <c>openssl</c> invocations publishes no revocation list and runs no
/// responder, so an online check has nothing to ask and would make every handshake wait for a lookup that
/// must fail. The recipe's own 30-day certificate lifetime is the control that substitutes for it, and a
/// deployment whose authority does publish revocation information leaves the anchor unset and uses
/// platform trust, where the platform's default revocation behaviour applies.
/// </para>
/// <para>
/// NOTHING ABOUT A REFUSED CERTIFICATE IS RECORDED HERE. A false result becomes a handshake failure, and
/// the endpoint answers an absent and an untrusted certificate with the same status and the same sentence
/// so that a response cannot be used to probe which condition was hit. Logging a subject or a thumbprint
/// here would reintroduce that distinction in the operator record for an unauthenticated caller.
/// </para>
/// </remarks>
internal sealed class CallerCertificateTrust
{
    private readonly X509Certificate2Collection _anchors;

    /// <summary>
    /// Creates a validator over an already-loaded anchor set.
    /// </summary>
    /// <param name="anchors">
    /// The acceptable roots. An EMPTY collection means platform default trust, which is a legitimate
    /// posture rather than a missing one.
    /// </param>
    private CallerCertificateTrust(X509Certificate2Collection anchors) => _anchors = anchors;

    /// <summary>
    /// Whether caller-certificate trust is pinned to a mounted anchor rather than left to the platform.
    /// </summary>
    public bool IsPinned => _anchors.Count > 0;

    /// <summary>
    /// Loads the anchor bundle named by <c>Security:MutualTls:ClientCaPath</c>.
    /// </summary>
    /// <param name="clientCaPath">
    /// The configured path, or <see langword="null"/> or blank for platform default trust.
    /// </param>
    /// <returns>A validator over that anchor, or one that defers to the platform.</returns>
    /// <exception cref="InvalidOperationException">
    /// A path is configured but the bundle cannot be read or does not parse. Structural, and therefore
    /// fatal: a deployment that meant to pin caller trust and cannot would otherwise start and accept
    /// callers on an unverified name.
    /// </exception>
    /// <remarks>
    /// The path is NOT echoed into the failure message. A trust anchor is public material, but a
    /// container's secret mount layout is not something a startup record should publish, so the message
    /// names the configuration key exactly as <see cref="SecurityOptionsValidator"/> does.
    /// </remarks>
    public static CallerCertificateTrust Load(string? clientCaPath)
    {
        if (string.IsNullOrWhiteSpace(clientCaPath))
        {
            return new CallerCertificateTrust([]);
        }

        try
        {
            X509Certificate2Collection loaded = [];

            loaded.ImportFromPemFile(clientCaPath.Trim());

            if (loaded.Count == 0)
            {
                throw new CryptographicException("The file carried no PEM-encoded certificate.");
            }

            return new CallerCertificateTrust(loaded);
        }
        catch (Exception failure) when (failure
            is CryptographicException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            throw new InvalidOperationException(
                "The caller-certificate authority named by "
                    + $"'{SecurityOptions.SectionName}:{nameof(SecurityOptions.MutualTls)}:"
                    + $"{nameof(SecurityMutualTlsOptions.ClientCaPath)}' could not be loaded, so this "
                    + "service cannot verify which authority issued a caller's certificate and the host "
                    + "will not start. Starting without it would leave token issuance resting on an "
                    + "unverified common name. Check that the file exists, that the process can read it, "
                    + "and that it is a PEM-encoded certificate or chain of them. The path is not "
                    + "reproduced here, because a startup record must not publish a container's secret "
                    + "mount layout.",
                failure);
        }
    }

    /// <summary>
    /// The Kestrel client-certificate validation callback.
    /// </summary>
    /// <param name="certificate">The certificate the caller presented.</param>
    /// <param name="chain">The chain the platform built, which may be <see langword="null"/>.</param>
    /// <param name="errors">The platform's own verdict on that chain.</param>
    /// <returns>
    /// <see langword="true"/> when the certificate is acceptable; otherwise <see langword="false"/>,
    /// which fails the handshake.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE PLATFORM'S CHAIN IS NOT REUSED WHEN AN ANCHOR IS CONFIGURED, and that is the point rather
    /// than duplicated work. The chain the platform built was built against the MACHINE's trust store,
    /// under which a certificate from a local authority is untrusted - so its verdict answers a different
    /// question from the one this deployment asked. A fresh chain is built against the configured anchor,
    /// which is the only authority whose certificates this edge accepts.
    /// </para>
    /// <para>
    /// <c>NoFlag</c> is deliberate and is the strict value: no error class is ignored, so an expired
    /// certificate, a broken signature, a wrong key usage or a chain that does not reach the anchor all
    /// fail. Only the TRUST DECISION is redirected.
    /// </para>
    /// </remarks>
    public bool Validate(X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            return false;
        }

        if (!IsPinned)
        {
            // No anchor configured: the platform already decided, and its decision stands. This is the
            // same verdict Kestrel reaches with no callback installed at all.
            return errors == SslPolicyErrors.None;
        }

        using X509Chain verification = new();

        verification.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        verification.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        verification.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        verification.ChainPolicy.CustomTrustStore.AddRange(_anchors);

        return verification.Build(certificate);
    }
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
