// ==================================================================================================
//  Program.cs - THE COMPOSITION ROOT OF THE POWERFRAMEWORK SECURITY SERVICE
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  The ASP.NET Core host for the keyed cryptographic surface and for the system's SOLE TOKEN ISSUER.
//  Port 5104. Gateway, DataServices and Persistence call it; it calls none of them.
//
//  This file WIRES: the cryptographic dependency graph, the determinism seams, inbound token
//  validation, the readiness and liveness routes and the published document. Route declarations live
//  in Endpoints/, next to the contract they implement.
//
//  WHY REST AND NOT gRPC (constraint C-K)
//  Token issuance and key publication must be plain HTTP so that consumers' stock
//  Microsoft.AspNetCore.Authentication.JwtBearer handlers fetch the JSON Web Key Set and the OpenID
//  discovery metadata WITH ZERO BESPOKE CODE. Choosing gRPC here would force hand-written key-set
//  retrieval into three services - a net INCREASE in hand-written security code, which is the opposite
//  of the requirement. The n_crypto surface itself argues nothing for gRPC either: at
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru it is roughly sixty stateless request/response overloads
//  with no ordering requirement and nothing to stream.
//
//  LEGACY REFERENCE (read only - never edited, never built, never shipped: constraint C-C)
//  There is no legacy analogue for a token issuer, because PowerFramework is a library with no process
//  of its own, no listener and no server tier - so JWT issuance, key publication and the health and
//  ping contract are net-new, built only on the legacy signing primitives RSASign and VerifyRSASign at
//  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73. What IS ported is the lifecycle posture of the
//  framework application object at ws_objects/pfw.pbl.src/pfw.sra: initialization before anything else,
//  finalization paired with it [:L91 against :L108], and a structural fault ending the process rather
//  than degrading past it [:L111-L144, ending in HALT CLOSE at :L143].
//
//  THE ASYMMETRIC KEY DECISION IS STRUCTURAL RATHER THAN STYLISTIC
//  The published key set carries PUBLIC verification material only, and the other three services hold
//  verification material only and are not independent signing authorities. A symmetric key would either
//  expose the signing secret through that document or require distributing it, and either outcome
//  breaks the sole-issuer topology.
//
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//    * No HttpClient, no gRPC channel and no client of any kind. Security is called BY the other three
//      and calls none of them, so it has no outbound edge and therefore no transient network fault to
//      handle - which is why Microsoft.Extensions.Http.Resilience is absent from this project
//      (constraint C-A).
//    * No Authority pointing at this service's own base address. See section 2: a self-referential
//      discovery fetch would add a startup network dependency on itself and would break the cold-start
//      path the orchestration health condition depends on (constraint C-I).
//    * No secret, key, PEM, credential or connection string in any form, including in a comment. The
//      system's single signing secret is supplied through configuration and never appears in source, in
//      application settings or in a container definition (constraint C-F).
//    * No registration, route, handler or options type for DesignSystem, Documents, Integration or
//      ScriptBridge. The four reserved 501 routes belong to GATEWAY's routing table, not to this
//      service; declaring one here would put a deferred capability's surface in the wrong service
//      entirely (constraint C-D).
//    * No DbContext, no EF Core, no Microsoft.Data.Sqlite and no connection string. Persistence is the
//      only service that holds a storage provider (constraint C-E).
//    * No rate limiting, CORS, response compression or output caching. Each would be an unrequested
//      behavioural change on a security-critical path (constraint C-B).
//    * No development bypass, no dev-token mode and no relaxed-validation branch under any environment
//      condition (constraint C-G).
//    * No SCREAMING_SNAKE constant is DECLARED here. The repository .editorconfig scopes its naming
//      suppressions to a fixed list of files carrying preserved legacy identifiers, of which the only
//      one in this project is Crypto/LegacyDefaults.cs; the preserved identifiers are CONSUMED from
//      there and from PowerFramework.Shared.Kernel instead.
//    * No listening port in code. http://+:5104 comes from appsettings.json and is overridden by the
//      container's environment, which is what makes independent deployability hold (constraint C-J).
// ==================================================================================================

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------------------------------------------------
// 1. THE CRYPTOGRAPHIC SURFACE, AND ITS TWO DETERMINISM SEAMS
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
// THE TWO SEAMS ARE REGISTERED HERE ON PURPOSE. The random blob, random string and GUID generators are
// the primary non-determinism sources in this whole service, and the clock is the other; the
// characterization model requires every such value to be maskable from BOTH the master and the
// candidate recording. Registering the real implementations here is what lets the sibling test project
// substitute deterministic doubles for the whole host rather than reaching inside a provider.
//
// THE LEGACY WEAK DEFAULTS ARE PRESERVED AND ANNOTATED, NEVER CORRECTED (constraint C-B). ECB is the
// default symmetric mode, PKCS#1 the default RSA padding with no-padding explicitly refused, PKCS#5
// padding is the only choice, no key-derivation function is reachable at all so a passphrase is used as
// raw key bytes, there is no authenticated encryption so ciphertext carries no integrity tag, and
// 1024-bit RSA remains a legal key size. Each is catalogued and annotated in Crypto/LegacyDefaults.cs,
// which is a static catalogue and therefore has nothing to register.
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
// 1b. THE REFERENCE RESOLUTION BOUNDARY - THE OTHER HALF OF THE CONTRACT-LEVEL SECRETS RULE
//
// The seven providers above were authored to take ALREADY-RESOLVED material and to read no
// configuration of their own. That is the structural half of the rule that RAW KEY MATERIAL NEVER
// CROSSES THE WIRE INBOUND. CryptoReferenceResolver is the other half: it is the single place an opaque
// reference becomes material, it consults the permitted set before it reads anything, and it is the only
// type in this service that touches the key store.
//
// The options binding below is what gives it its policy. Only the key-store section is consumed here,
// and DELIBERATELY WITHOUT startup validation: the validator covers the issuer's own signing material,
// which is the token-issuance surface's concern rather than this one's, and enabling it here would
// couple the cryptographic surface's readiness to a setting it never reads. The store's own closed
// default does the safety work instead - an unpopulated permitted set authorises nothing, so a service
// brought up without a reviewed key store refuses every keyed operation rather than reading whatever
// configuration key a caller names.
// --------------------------------------------------------------------------------------------------
builder.Services.Configure<SecurityOptions>(
    builder.Configuration.GetSection(SecurityOptions.SectionName));
builder.Services.AddSingleton<CryptoReferenceResolver>();

// --------------------------------------------------------------------------------------------------
// 1c. THE SIGNING-KEY LAYER - ONE INSTANCE, AND THE ONLY PLACE PRIVATE KEY MATERIAL LIVES
//
// A SINGLETON BECAUSE THE KEY IS FIXED FOR THE LIFETIME OF THE PROCESS. This phase adds no rotation, so
// a per-request instance would re-import the same material on every call and would let two concurrent
// requests publish key sets built from different imports. It imports the one configured signing secret,
// owns the resulting key, and is therefore IDisposable - the container disposes it at shutdown.
//
// TWO CONSUMERS, WITH DELIBERATELY UNEQUAL ACCESS. Endpoints/JwksEndpoints.cs reads ONLY the public-only
// projection and never the credential member; Tokens/TokenIssuer.cs is the sole legitimate reader of the
// credential. Registering the type once here is what keeps that asymmetry a property of the two call
// sites rather than of two separately-configured instances.
//
// NO KEY MATERIAL APPEARS HERE. The secret is bound from configuration into SecurityOptions, which the
// provider takes through IOptions, so nothing in this file, in appsettings.json or in the container
// definition carries a literal (constraint C-F).
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<SigningKeyProvider>();

// --------------------------------------------------------------------------------------------------
// 1d. THE SOLE MINTER - ONE INSTANCE, AND THE ONLY COMPONENT IN THE SYSTEM THAT CREATES A TOKEN
//
// Security mints; Gateway, DataServices and Persistence hold verification material only and are not
// independent signing authorities. Registering this type here, once, is what makes that topology a
// property of the composition root rather than of a convention: there is exactly one issuer instance
// in the process and exactly one route that can reach it.
//
// A SINGLETON, AND ITS CONSTRUCTOR IS THE STARTUP GATE. It resolves the issuer identity, the audience
// roster, the lifetime and the signing credential EAGERLY and refuses to construct when any of them
// cannot support issuance - a blank issuer, an empty or blank-entried roster, a lifetime that does not
// carry a whole second, a symmetric key, an algorithm this issuer does not produce, or a key identifier
// that disagrees with the one the key set publishes. That is the fail-fast posture the legacy framework
// application object sets by ending a structural fault in process termination rather than in a warning
// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE at :L143]: a misconfigured issuer must
// not reach the point of answering a readiness probe, because a service that reports healthy and then
// refuses every request looks correct from the outside for exactly as long as it takes to page someone.
//
// IT TAKES ITS CLOCK AND ITS CREDENTIAL FROM THE TWO REGISTRATIONS ABOVE, and neither is duplicated for
// it. The clock is the determinism seam a characterization run substitutes, so the issuance instant, the
// not-before instant and the expiry are all derived from one substitutable reading; the credential comes
// from the signing-key layer, which is the only place private key material lives. NO KEY MATERIAL AND NO
// CONFIGURATION VALUE APPEARS HERE (constraint C-F).
// --------------------------------------------------------------------------------------------------
builder.Services.AddSingleton<TokenIssuer>();

// --------------------------------------------------------------------------------------------------
// 2. INBOUND TOKEN VALIDATION - THE STOCK HANDLER, AND THE ONE ASYMMETRY THAT IS DELIBERATE
//
// Security is subject to the same rule as every other service on its own surface: /v1/ping requires a
// valid token and the issuer grants itself no exemption. The handler is the stock bearer handler, so
// the security-critical path is framework code rather than hand-written code.
//
// NO AUTHORITY IS CONFIGURED, AND THAT IS THE DELIBERATE ASYMMETRY. Security validates tokens it minted
// ITSELF, so pointing the handler at its own discovery document would be self-referential: it would
// make startup fetch metadata from the very service that is starting, fail in a cold container, and
// leave this service unable to satisfy the readiness gate its own dependents are waiting on. Its
// verification material is therefore supplied IN-PROCESS by the service's signing-key layer rather than
// over HTTP. The other three services DO use the HTTP key-set and discovery path, and that difference
// is exactly why this service speaks REST.
//
// Every validation stays on and each is read from configuration rather than hardcoded, so a deployment
// can tighten but never silently loosen one by omission: a missing key leaves the safe value in place.
// With no signing key resolved, an unverifiable token is REFUSED rather than accepted - the handler
// fails closed, which is the correct posture and is not a bypass.
// --------------------------------------------------------------------------------------------------
IConfigurationSection authentication = builder.Configuration.GetSection("Authentication:Jwt");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(bearer =>
    {
        bearer.Audience = (authentication["Audience"] ?? string.Empty).Trim();
        bearer.RequireHttpsMetadata = authentication.GetValue("RequireHttpsMetadata", true);
        bearer.MapInboundClaims = authentication.GetValue("MapInboundClaims", false);

        bearer.TokenValidationParameters.ValidateIssuer =
            authentication.GetValue("ValidateIssuer", true);
        bearer.TokenValidationParameters.ValidateAudience =
            authentication.GetValue("ValidateAudience", true);
        bearer.TokenValidationParameters.ValidateLifetime =
            authentication.GetValue("ValidateLifetime", true);
        bearer.TokenValidationParameters.ValidateIssuerSigningKey =
            authentication.GetValue("ValidateIssuerSigningKey", true);
    });

// A FALLBACK POLICY REQUIRING AN AUTHENTICATED USER, so that NO route on the service holding the
// system's single signing secret is ever authenticated by omission (constraint C-G). Only the readiness
// probe and the published document opt out, and each says so explicitly at its own declaration.
builder.Services.AddAuthorization(static options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// --------------------------------------------------------------------------------------------------
// 3. THE PUBLISHED SURFACE
//
// Health-check registration ships inside the Microsoft.AspNetCore.App shared framework, so /health
// needs no package reference and none is declared. Problem details are registered so the framework's own
// challenge and the readiness probe's 503 both answer application/problem+json carrying the legacy
// return code, which is the error shape shared/PowerFramework.Contracts/OpenApi/security.v1.yaml
// publishes - and where that document and docs/CONTRACTS.md ever disagree, the document wins for
// anything on the wire.
//
// Microsoft.OpenApi is pinned to 2.11.0 as a MANDATORY constraint held centrally at the repository root.
// 2.0.0 raises the NU1903 advisory, and moving to the 3.x line breaks the build with two CS0200
// "cannot assign, read only" errors inside the SDK's own generated OpenAPI support file, because the
// 10.0.x source generator is compiled against the 2.x object model. Neither Swashbuckle nor Scalar is
// referenced: an interactive UI is not required, and the document generator already produces the
// document.
// --------------------------------------------------------------------------------------------------
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// The published contract document, anonymous because a description of the surface is not part of the
// surface it describes and every operation in it still states its own security requirement.
app.MapOpenApi().AllowAnonymous();

// One call per endpoint file. /health is anonymous - unavoidably so on the sole issuer, since a probe
// requiring a token could not succeed until issuance was already live - and /v1/ping requires a token
// and answers 401 without one. Both properties are declared inside those files, where they are
// exercised.
app.MapHealthEndpoints();
app.MapPingEndpoints();

// Contract C-01's publication half: the key set and the discovery metadata this service's three
// consumers point their stock bearer handlers at. BOTH ROUTES ARE ANONYMOUS BY NECESSITY - a handler
// reads them in order to learn how to authenticate, so it cannot already hold a token - and each says so
// explicitly inside the file rather than relying on omission, because under the default-deny fallback
// above omission would close the route instead of opening it. The registration also validates that both
// configured addresses sit inside the well-known namespace and differ from one another, and fails the
// host when either condition does not hold.
app.MapJwksEndpoints();

// Contract C-01's issuance half, and the only route in the system that reaches a signing key. It is
// authenticated by a CLIENT CERTIFICATE rather than by a token, applied explicitly inside that file as a
// route-level authorization policy, because a caller cannot present a bearer token in order to obtain its
// first bearer token - which is why the published document applies its mutual-TLS scheme to that one
// operation as an override of the document-level bearer requirement. The requirement is enforced per
// operation rather than at the listener so that the anonymous routes above and the bearer-authenticated
// ones below stay reachable on this service's single listener; the registration also validates the
// configured issuance address and fails the host when it is blank, unrooted, or inside the anonymous
// metadata namespace.
app.MapTokenEndpoints();

// Contract C-02, the cryptographic surface. Every one of its 17 operations requires a token and answers
// 401 without one, applied once on the route group inside the file rather than relied upon from the
// default-deny fallback above, so a route added later cannot become anonymous by omission.
app.MapCryptoEndpoints();

app.Run();

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
