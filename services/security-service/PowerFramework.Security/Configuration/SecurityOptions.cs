// ==================================================================================================
//  SecurityOptions - the whole configuration contract of the Security service, plus the startup
//  validation that makes it fail-fast
//  ------------------------------------------------------------------------------------------------
//  LEGACY PROVENANCE, cited for decisions only. Every path below is READ ONLY: it is the behavioural
//  oracle for parity testing and never an edit target. No legacy file is read at run time, named in a
//  default value, or reachable from any code path in this file.
//
//      ws_objects/pfw.pbl.src/pfw.sra:L111-L144        the fail-fast posture (see FAIL FAST below)
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru      the algorithm and key-shape evidence
//                                                      L19-L20  GenRSAKey, pemformat OPTIONAL
//                                                      L23-L29  keyed Hash, raw key argument
//                                                      L63      RSAEncrypt, HAS a padding argument
//                                                      L70      RSASign, hash type and NO padding
//      ws_objects/pfw.shared.pbl.src/enums.sru:L921-L969   the preserved crypto constant catalogue
//                                                      L927  block label naming RSASign/VerifyRSASign
//                                                      L930  SHA-256 = 2, the RS256 anchor
//                                                      L931  SHA-384 = 3
//                                                      L932  SHA-512 = 4
//                                                      L965  1024-bit RSA, a first-class legal size
//
//  ==================================================================================================
//  WHAT THIS FILE IS, AND WHY IT IS ONE FILE
//  ==================================================================================================
//  Three types, one file, on purpose. The options type, its key-store companion and the validator
//  are a single contract: the validator's rules are the only reason several of the properties are
//  shaped the way they are, and splitting them across files would let one drift from the other with
//  nothing to catch it. This folder contains exactly this file and is intended to stay that way.
//
//  This file declares configuration and validates it. It performs NO I/O of any kind - no file read,
//  no network call, no environment probe - beyond one in-memory key import attempt whose only purpose
//  is to answer "could this material sign?" before the host is allowed to start. That is what makes
//  the whole surface unit-testable without booting a host, which the per-service coverage gate needs.
//
//  ==================================================================================================
//  FAIL FAST, NEVER GRACEFUL DEGRADATION
//  ==================================================================================================
//  The legacy framework's application object handles a decoded assertion by unpacking a seven-field
//  payload split on a carriage-return/line-feed pair, formatting a diagnostic, and then executing
//  HALT CLOSE [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating at :L143]. Read that event
//  looking for a recovery arm and there is none: no retry, no default, no continue path, no downgrade
//  to a warning. A structural fault ends the process.
//
//  The .NET equivalent of HALT CLOSE at startup is a host that refuses to start. That is why this
//  file's validator is registered with ValidateOnStart rather than consulted lazily on first use: a
//  misconfigured Security service that starts is strictly worse than one that does not, because it
//  answers health probes, satisfies the orchestration readiness gate, and then rejects or mis-signs
//  every token in the system while looking healthy from the outside.
//
//  The expected registration, which the service entry point owns and which this file is shaped to
//  make possible:
//
//      builder.Services
//          .AddOptions<SecurityOptions>()
//          .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
//          .ValidateDataAnnotations()
//          .ValidateOnStart();
//
//      builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
//
//      builder.Services.PostConfigure<SecurityOptions>(options =>
//          options.SigningKey =
//              builder.Configuration[SecurityOptions.SigningKeyEnvironmentVariableName]);
//
//  The post-configure step is not a convenience. See THE FLAT KEY below: without it the signing
//  material never reaches the options instance at all, and this file deliberately does not pretend
//  otherwise.
//
//  ==================================================================================================
//  THE FLAT KEY: WHY CONVENTIONAL SECTION BINDING CANNOT CARRY THE SIGNING MATERIAL
//  ==================================================================================================
//  The environment-variable configuration provider maps a DOUBLE UNDERSCORE onto the ':' section
//  separator, and nothing else. Every other setting in this service therefore arrives as
//  Security__Issuer, Security__KeyStore__ConfigurationKeyPrefix and so on, and binds into the
//  Security section by convention.
//
//  SECURITY_JWT_SIGNING_KEY contains no double underscore. It is consequently a FLAT configuration
//  key of exactly that literal name - not Security:Jwt:Signing:Key, not anything inside the Security
//  section - so section binding will never populate SigningKey however the section is bound. The name
//  is fixed by orchestration/.env.example and the Compose manifest and must not be renamed, aliased
//  as Security__SigningKey, or shadowed by a second spelling: the operator-facing contract is that
//  one variable name, and three other services' ability to verify a token depends on it being read.
//
//  Two consequences are load bearing, and both are why the constant below exists:
//    * SigningKey is a settable property with no default, populated by an explicit post-configure
//      step that reads the flat key by name. Nothing here assumes binding did it.
//    * The name is declared once, as a constant, so the entry point and every test read the same
//      spelling and a typo cannot silently produce a service that starts and cannot sign.
//
//  The identical trap applies to the key store: SecurityKeyStoreOptions.ConfigurationKeyPrefix plus a
//  keyRef is a FLAT configuration key, not a path inside Security:KeyStore. See that type.
//
//  ==================================================================================================
//  THE SIGNATURE ALGORITHM: RS256 BY DEFAULT, AND A CLOSED ALLOW-LIST OF THREE
//  ==================================================================================================
//  SigningAlgorithm accepts RS256, RS384 or RS512 and nothing else, and the allow-list is a compiled
//  constant rather than a configurable list. Each exclusion has a specific reason.
//
//  THE HMAC FAMILY IS EXCLUDED STRUCTURALLY, not stylistically. This service publishes verification
//  material anonymously at the JWKS path, and Gateway, DataServices and Persistence hold verification
//  material only - exactly one signing key exists in the whole system and this service holds it. A
//  symmetric JWK in a published key set would be publishing the signing material itself, and handing
//  it to three verifiers would make each of them a co-signer. The sole-issuer property that the whole
//  token topology rests on would be gone. There is therefore no configuration value that selects it.
//
//  THE PSS FAMILY IS EXCLUDED ON LEGACY EVIDENCE. Compare two declarations in the legacy
//  cryptographic class: RSAEncrypt takes a padding argument
//  [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L63], while RSASign takes a hash type and NO padding
//  argument at all [:L70]. The legacy signing primitive has nowhere to express a probabilistic
//  padding scheme, so it is structurally incapable of emitting one. A legacy browser-harness sample
//  does carry a PS256 header [tests/blink/test_jws.htm:L23], and it is NOT a contract: it is the
//  choice of the third-party JavaScript library that produced it, made in a page that also carries a
//  hardcoded private key inline and signs with it. That page is precisely the anti-pattern this
//  configuration surface exists to replace - key material reaching a signer from configuration
//  injection instead of from source - so it is cited here as evidence about provenance and nothing
//  from it is read, reproduced or accepted: PS256 appears in this file only as a rejected value.
//
//  THE ECDSA FAMILY IS EXCLUDED because the legacy cryptographic class exposes no elliptic-curve
//  primitive anywhere in its declarations. There is no legacy behaviour to preserve and no evidence
//  to preserve it from.
//
//  RS384 AND RS512 ARE ADMITTED because the legacy hash-type block is labelled as the argument set of
//  RSASign and VerifyRSASign [ws_objects/pfw.shared.pbl.src/enums.sru:L927] and declares SHA-384 = 3
//  at :L931 and SHA-512 = 4 at :L932 alongside SHA-256 = 2 at :L930. All three are legacy-expressible
//  signatures, so admitting them adds no capability the legacy lacked. SHA-256 is the anchor and the
//  default: RSA plus SHA-256 plus PKCS#1 v1.5 is precisely RS256, and given RSASign's missing padding
//  argument it is the only signature the legacy could produce. Those constants are available in this
//  tree as Enums.CRYPTO_HASH_SHA256 and its siblings in PowerFramework.Shared.Kernel; they are cited
//  here rather than referenced because a JOSE algorithm identifier is not one of them and importing
//  the kernel to name a value this file does not use would be a dependency for a comment.
//
//  ==================================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DECLARE
//  ==================================================================================================
//  Recorded so each omission reads as a decision, and so nobody "completes" this type by adding one.
//
//  NO MINIMUM KEY SIZE, AND NO REJECTION BASED ON KEY SIZE. The legacy constant catalogue keeps
//  1024-bit RSA as a first-class legal size [ws_objects/pfw.shared.pbl.src/enums.sru:L965] and the
//  migration record preserves that allowance rather than correcting it. A floor here would be exactly
//  the silent legacy correction this refactor forbids. If the token-minting library independently
//  refuses a short key at run time, that is an observation for the token-issuing layer to surface,
//  not a policy for this file to invent. Note for a reader diffing against the settings file: the
//  Security section in appsettings.json additionally carries SigningKeyFormat and
//  SigningKeyMinimumSizeBits leaves. They are deliberately NOT bound here for the reason just given -
//  a size floor is a forbidden correction, and a format discriminator is dead by construction because
//  the usability check below accepts BOTH accepted shapes unconditionally and in a fixed order rather
//  than being told which to expect. Unbound configuration keys are ignored by the binder, so their
//  presence costs nothing; they are called out only so their absence here is not read as an oversight.
//
//  NO SWITCH THAT RELAXES VALIDATION. There is no option to disable issuer, audience, lifetime or
//  signature validation, no option to permit unsigned tokens, no option to allow plaintext metadata
//  and no option to make a route anonymous. The values declared here are what three other services
//  validate every token against, so a switch that loosened any of them would loosen them fleet-wide
//  from a single file - and the newly created boundaries of this decomposition are required to be
//  authenticated from the outset rather than optionally so.
//
//  NO EPHEMERAL-KEY FALLBACK, IN ANY ENVIRONMENT. Generating a key when material is absent would turn
//  one loud startup failure into a silent fleet-wide authentication outage: every token the other
//  three services hold would stop verifying, and each restart would invalidate the previous
//  generation's tokens too. There is no development exemption and no generate-if-absent branch.
//
//  NO WEAK LEGACY DEFAULT PROMOTED TO AN OPTION. The preserved electronic-codebook symmetric mode
//  [enums.sru:L946] and PKCS#1 padding default [:L951] belong in the ported cryptographic surface as
//  annotated constants, where they are parity obligations. Exposing either as an operator-selectable
//  setting here would misrepresent a preserved legacy weakness as a deliberate deployment choice.
//
//  NO METADATA BASE ADDRESS. Absolute metadata addresses are composed from Issuer, which is the same
//  value a consumer's bearer handler uses for discovery. A second address setting could disagree with
//  it, and an option nothing reads is dead configuration.
//
//  NO PROVIDER DISCRIMINATOR AND NO keyRef-TO-VALUE MAP on the key store. One resolution mechanism
//  exists, and a map of key values in a settings-bound option is precisely the hardcoded material
//  this refactor's secrets mandate forbids.
//
//  NO required MEMBER ANYWHERE. A required member turns a configuration fault into an activation-time
//  error that names no configuration key. A presence attribute plus the validator produces a startup
//  message that names the offending key, which is what an operator staring at a failed bring-up needs.
//
//  NOT A record, NO custom ToString, NO debugger display attribute and NO serialization helper. A
//  synthesized or hand-written string rendering would print SigningKey into any log, exception or
//  diagnostic that stringified the options instance. The type is deliberately unprintable.
//
//  ==================================================================================================
//  RULES POSITION
//  ==================================================================================================
//  No user rules were provided for this project: the rules document contains exactly one line saying
//  so. Nothing is invented or back-filled from convention in their place. The binding constraints are
//  the refactor's own named constraints together with the enterprise-standard baseline - nullable
//  reference types with warnings as errors, no credential value in source or settings or container
//  definition, names-only diagnostics, and a plainly unit-testable type - and every decision above
//  cites the concern that drives it.
// ==================================================================================================

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace PowerFramework.Security.Configuration;

/// <summary>
/// The complete configuration contract of the Security service: the token claims it stamps, the
/// metadata addresses it publishes, the key store it resolves opaque key references against, and the
/// single signing key it holds.
/// </summary>
/// <remarks>
/// <para>
/// Bound from the <c>Security</c> configuration section named by <see cref="SectionName"/>, with one
/// exception that matters: <see cref="SigningKey"/> arrives from the FLAT configuration key named by
/// <see cref="SigningKeyEnvironmentVariableName"/> and is applied by an explicit post-configure step,
/// because the environment provider's double-underscore section separator means that name can never
/// land inside the section by convention. The file header records the mechanism in full.
/// </para>
/// <para>
/// VALIDATION IS FAIL-FAST AND IS NOT OPTIONAL. Presence rules are declared as annotations on the
/// properties so that they are visible where the property is read, and every rule an annotation
/// cannot express - and, deliberately, the presence rules again - lives in
/// <see cref="SecurityOptionsValidator"/>. Registering the validator with <c>ValidateOnStart</c>
/// makes a misconfiguration refuse the host rather than surface on the first token request.
/// </para>
/// <para>
/// This type is sealed, carries no string rendering of any kind, and never logs. It is a value
/// container that one of its properties makes sensitive, so it is shaped so that stringifying it
/// cannot leak that property.
/// </para>
/// </remarks>
public sealed class SecurityOptions
{
    /// <summary>
    /// The configuration section this type binds from: <c>Security</c>.
    /// </summary>
    /// <remarks>
    /// Declared once so that the service entry point, the settings files and every test agree on one
    /// spelling. A mis-spelled section binds nothing, leaves every property at its default, and is
    /// then caught by <see cref="SecurityOptionsValidator"/> as a pile of missing values rather than
    /// as the one typo it actually is - so the constant is a diagnosability measure as much as a
    /// tidiness one.
    /// </remarks>
    public const string SectionName = "Security";

    /// <summary>
    /// The FLAT configuration key that carries the signing material:
    /// <c>SECURITY_JWT_SIGNING_KEY</c>. This is a NAME, not a value; no key material appears in this
    /// file, in any settings file, or in any container definition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY CONVENTIONAL SECTION BINDING CANNOT WORK. The environment-variable configuration provider
    /// translates a double underscore, and only a double underscore, into the <c>:</c> section
    /// separator. This name contains none, so it is a top-level configuration key of exactly this
    /// literal spelling rather than a path into the <c>Security</c> section. Binding the section -
    /// however it is bound - will therefore never populate <see cref="SigningKey"/>.
    /// </para>
    /// <para>
    /// The resolution is an explicit post-configure step in the service entry point that reads
    /// <c>configuration[SecurityOptions.SigningKeyEnvironmentVariableName]</c> onto the bound
    /// instance. That is the whole reason this constant is public: the entry point and the tests that
    /// drive configuration read one spelling from one place.
    /// </para>
    /// <para>
    /// The name is fixed by <c>orchestration/.env.example</c> and the Compose manifest and must NOT
    /// be renamed, and no <c>Security__SigningKey</c> alias may be added. Two accepted spellings for
    /// one input is two ways for a deployment to be half-configured.
    /// </para>
    /// </remarks>
    public const string SigningKeyEnvironmentVariableName = "SECURITY_JWT_SIGNING_KEY";

    /// <summary>
    /// The issuer identity: the <c>iss</c> claim stamped into every minted token and the
    /// <c>issuer</c> member of the discovery document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the token issuer when minting and by the discovery endpoint when describing this
    /// service. Absolute metadata addresses are composed from this value, which is why there is no
    /// separate metadata base address to disagree with it.
    /// </para>
    /// <para>
    /// This value is what Gateway, DataServices and Persistence each validate the <c>iss</c> claim
    /// against, so changing it invalidates every token in flight and must be treated as a
    /// coordinated change rather than a local one. Its format is left to the deployment: the
    /// discovery document's own shape is the concern of the endpoint that publishes it, and a format
    /// rule invented here could reject an identity a deployment legitimately uses.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The audience identities a minted token may be addressed to - the in-scope service identities
    /// this issuer serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the token issuer to populate the <c>aud</c> claim. The concrete identities are
    /// declared in <c>appsettings.json</c> and deliberately nowhere in code: an audience is a
    /// deployment fact, and a value baked in here would be a second source of truth able to drift
    /// from the settings file. In this phase the roster covers the three services this issuer serves
    /// - the gateway composition root, the DataWindow service layer and the persistence layer -
    /// together with this service's own identity for its authenticated ping route.
    /// </para>
    /// <para>
    /// GET-ONLY BY DESIGN, and it still binds. The configuration binder populates an existing
    /// collection instance when a collection property has no setter, so section binding works exactly
    /// as it would with a setter, while the absence of one keeps the property from being replaced
    /// wholesale or set to null. The declared type is an interface rather than a concrete list or an
    /// array so that no caller can rely on an implementation detail of how it is stored.
    /// </para>
    /// <para>
    /// An empty roster fails validation: an issuer that may address no audience cannot mint a usable
    /// token. No particular COUNT is required, because the roster is a deployment decision.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<string> Audiences { get; } = [];

    /// <summary>
    /// How long a minted service token stays valid. Short-lived by contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the token issuer to compute the <c>exp</c> claim. The default is five minutes, which
    /// matches the value the settings file declares; a service token is obtained on demand from a
    /// service that is always reachable, so a short life costs little and bounds the damage from a
    /// leaked token to the same interval.
    /// </para>
    /// <para>
    /// Clock skew is deliberately NOT modelled here. Skew tolerance belongs to the token issuer and
    /// to each verifier's bearer handler, and expressing it as a second setting alongside this one
    /// would invite the two to be tuned against each other. Validation only requires that this value
    /// be strictly positive - a zero or negative lifetime mints tokens that are already expired,
    /// which is a configuration fault rather than an aggressive policy.
    /// </para>
    /// </remarks>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The key identifier: the <c>kid</c> published in the JWKS document and stamped into the header
    /// of every minted token.
    /// </summary>
    /// <remarks>
    /// Read by the signing-key provider when describing the key, by the token issuer when stamping
    /// the header, and by the JWKS endpoint when publishing it. This is what makes rotation possible:
    /// a new key gets a new identifier, both appear in the published set for as long as tokens minted
    /// under the old one are still in flight, and each verifier selects by <c>kid</c> rather than
    /// guessing. An issuer with no key identifier cannot express that, so a blank value fails
    /// validation. It is an identifier and carries no key material.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string SigningKeyId { get; set; } = string.Empty;

    /// <summary>
    /// The JOSE signature algorithm this issuer signs with. Defaults to <c>RS256</c> and is
    /// restricted to the closed set published by
    /// <see cref="SecurityOptionsValidator.PermittedSigningAlgorithms"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the signing-key provider, by the token issuer when stamping the <c>alg</c> header, and
    /// by the JWKS endpoint when describing the published key. The permitted set is
    /// <c>RS256</c>, <c>RS384</c> and <c>RS512</c>; it is a compiled constant and cannot be extended
    /// by configuration. Comparison is case-sensitive because JOSE algorithm identifiers are.
    /// </para>
    /// <para>
    /// The exclusions are reasoned, not stylistic, and the file header records each in full: the HMAC
    /// family is excluded structurally, because a published key set is anonymous verification
    /// material and a symmetric key there would publish the signing material and make three verifiers
    /// into co-signers; the PSS family is excluded on legacy evidence, because the legacy signing
    /// primitive takes a hash type and no padding argument at all
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70] while its encryption counterpart does take
    /// one [:L63], so it cannot express a probabilistic scheme; and the elliptic-curve family is
    /// excluded because the legacy cryptographic class declares no such primitive.
    /// </para>
    /// <para>
    /// Changing this value changes what every verifier must accept, so it is a coordinated change.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string SigningAlgorithm { get; set; } = "RS256";

    /// <summary>
    /// The request path the JWKS document is published at. Defaults to
    /// <c>/.well-known/jwks.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the JWKS endpoint when mapping the route and by the discovery endpoint when
    /// advertising it as <c>jwks_uri</c>, composed against <see cref="Issuer"/>.
    /// </para>
    /// <para>
    /// CONSTRAINED TO THE <c>/.well-known/</c> NAMESPACE, AND THAT IS AN AUTHENTICATION CONTROL
    /// RATHER THAN TIDINESS. The service entry point applies a default-deny authorization policy and
    /// grants anonymous access to exactly three paths: the health probe, this one and
    /// <see cref="OpenIdConfigurationPath"/>. An unconstrained value here could therefore point the
    /// anonymous exemption at an authenticated route and silently remove its authentication.
    /// Validation requires the prefix and requires something after it.
    /// </para>
    /// <para>
    /// It must stay anonymous: the document contains public verification material only, and every
    /// consumer's stock bearer handler fetches it before it holds any token to present.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string JwksPath { get; set; } = "/.well-known/jwks.json";

    /// <summary>
    /// The request path the OpenID discovery document is published at. Defaults to
    /// <c>/.well-known/openid-configuration</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the discovery endpoint when mapping the route. Publishing it is the entire reason this
    /// service speaks REST rather than gRPC: a consumer's stock bearer handler self-configures from
    /// this document with no bespoke code, so the security-critical retrieval path is framework code
    /// in three services instead of hand-written code in three services.
    /// </para>
    /// <para>
    /// Constrained to the <c>/.well-known/</c> namespace for the same authentication reason as
    /// <see cref="JwksPath"/>: it is one of exactly three anonymous exemptions, and an unconstrained
    /// value could aim that exemption somewhere it does not belong.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string OpenIdConfigurationPath { get; set; } = "/.well-known/openid-configuration";

    /// <summary>
    /// The request path token issuance is served at. Defaults to <c>/v1/tokens</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS PROPERTY IS THE SINGLE DECLARED SOURCE OF TRUTH FOR THAT PATH. The token endpoint maps
    /// its route from it and the discovery endpoint advertises it as <c>token_endpoint</c>; if the two
    /// disagreed, discovery would publish an address that answers nothing, and a consumer that
    /// trusted discovery could not obtain a token at all. Neither may hardcode the path
    /// independently.
    /// </para>
    /// <para>
    /// CONSTRAINED AS THE INVERSE OF THE METADATA PATHS: it must be rooted, and it must NOT fall
    /// inside <c>/.well-known/</c>. That is the anonymous-exemption control applied in the other
    /// direction - the one route that mints tokens can never land inside the namespace the entry point
    /// exempts from authorization.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string TokenEndpointPath { get; set; } = "/v1/tokens";

    /// <summary>
    /// The key store this service resolves an opaque caller-supplied key reference against. Names
    /// only; it never carries key material.
    /// </summary>
    /// <remarks>
    /// Get-only and always present, so the descriptor can never be null and never be replaced
    /// wholesale. The configuration binder populates the existing instance when a complex property
    /// has no setter, so <c>Security:KeyStore</c> binds exactly as it would with a setter. See
    /// <see cref="SecurityKeyStoreOptions"/> for what it holds and, importantly, for what it must
    /// never hold.
    /// </remarks>
    public SecurityKeyStoreOptions KeyStore { get; } = new();

    /// <summary>
    /// The signing material: the asymmetric private key this service - and only this service - signs
    /// with. Populated from the flat configuration key named by
    /// <see cref="SigningKeyEnvironmentVariableName"/>. Never defaulted, never logged, never echoed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SETTABLE, AND WITH NO DEFAULT OF ANY KIND, both deliberately. Settable because the only thing
    /// that can populate it is an explicit post-configure step in the service entry point reading the
    /// flat key by name - the environment provider's double-underscore section separator means the
    /// fixed variable name can never bind into the <c>Security</c> section by convention, so nothing
    /// in this file may assume conventional binding populated it. Undefaulted because there is no
    /// value that would be safe to fall back to: generating one would silently invalidate every token
    /// the other three services hold, and any literal would be exactly the hardcoded credential this
    /// refactor's secrets mandate exists to eliminate.
    /// </para>
    /// <para>
    /// Read by the signing-key provider, which imports it once at startup and hands the resulting key
    /// to the token issuer and to the JWKS endpoint - the latter publishing the PUBLIC half only,
    /// derived from this value. There is no public-key setting and no key to distribute by hand.
    /// </para>
    /// <para>
    /// TWO ENCODINGS ARE ACCEPTED, and the validator tries them in a fixed order: armoured text
    /// first, then base64 of the bare binary encoding. Both exist in practice because the legacy key
    /// generator's armoured output is an OPTIONAL fourth argument
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20], so legacy material genuinely exists in
    /// both shapes and neither may be refused. Material that is neither - random bytes being the case
    /// that actually happens - cannot be imported as an asymmetric key and fails startup.
    /// </para>
    /// <para>
    /// HANDLING RULES, which the surrounding code is shaped to enforce. This value is never written to
    /// a log, a trace, an exception message, a metric or a diagnostic; no failure interpolates it, any
    /// substring of it, or even its length; and the containing type declares no string rendering, no
    /// debugger display and no serialization helper precisely so that stringifying the options
    /// instance cannot expose it. It appears in no source file, no settings file and no container
    /// definition: configuration injection from the orchestration layer is the only path by which it
    /// reaches this process.
    /// </para>
    /// </remarks>
    public string? SigningKey { get; set; }
}

/// <summary>
/// The key-store descriptor: how this service turns an opaque, caller-supplied key reference into
/// key material, and which references it will accept at all. NAMES ONLY - this type carries no key
/// material and no value that could become key material.
/// </summary>
/// <remarks>
/// <para>
/// WHY A REFERENCE AND NOT A KEY. The published cryptographic contract's rule is that raw key
/// material never crosses the wire from a caller: a caller passes an opaque <c>keyRef</c> and this
/// service resolves it against its own configured store. The legacy surface is the shape being
/// removed - its keyed hashing overloads take a raw key argument directly
/// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L29], so every caller of the legacy library had to
/// hold, transport and therefore be trusted with the key. Moving resolution behind a reference means
/// a compromised caller can name a key it may use and can obtain nothing it may not.
/// </para>
/// <para>
/// A top-level type rather than a type nested inside <see cref="SecurityOptions"/>, so that it is
/// bindable, constructible and assertable on its own in a test without reaching through its parent.
/// It lives in the same file as its parent because a descriptor and the contract it belongs to are
/// one decision.
/// </para>
/// </remarks>
public sealed class SecurityKeyStoreOptions
{
    /// <summary>
    /// The configuration-key NAME PREFIX that a permitted key reference is appended to in order to
    /// resolve its material - for example <c>SECURITY_KEYSTORE_</c>. A name, never a value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the cryptographic endpoints when resolving a caller's <c>keyRef</c>. Resolution is
    /// <c>configuration[ConfigurationKeyPrefix + keyRef]</c> and nothing more elaborate.
    /// </para>
    /// <para>
    /// THE DOUBLE-UNDERSCORE TRAP APPLIES HERE TOO, and getting it wrong is the likeliest defect in
    /// the whole key-store path. The composed lookup is a FLAT configuration key of exactly that
    /// concatenated spelling - it is NOT a path inside <c>Security:KeyStore</c>, and the environment
    /// provider will only fold a double underscore into a section separator. A prefix carrying no
    /// double underscore therefore keeps resolution flat, which is the intent: material supplied as an
    /// environment variable resolves by its own literal name, exactly as the signing key does.
    /// </para>
    /// <para>
    /// REQUIRED ONLY WHEN THE STORE IS ACTUALLY USED. Validation demands a non-blank prefix if and
    /// only if <see cref="PermittedKeyRefs"/> lists at least one reference, because a permitted
    /// reference with no prefix to resolve it against is unresolvable configuration and should stop
    /// the host. A deployment that permits no reference uses no prefix, and failing it for a value it
    /// has no use for would be a fabricated requirement.
    /// </para>
    /// </remarks>
    public string ConfigurationKeyPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The closed set of key references callers may name. A reference outside this set is rejected
    /// cleanly rather than resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read by the cryptographic endpoints before any resolution is attempted, which is the point: an
    /// unknown reference becomes a clean, cheap rejection instead of an unbounded lookup into the
    /// configuration tree driven by caller-supplied text. Without the allow-list, a caller chooses
    /// which configuration key this service reads.
    /// </para>
    /// <para>
    /// EVERY ENTRY IS CHARSET-CONSTRAINED. Validation requires each entry to be a conservative safe
    /// identifier - letters, digits, dot, underscore and hyphen only, at least one character and no
    /// more than <see cref="SecurityOptionsValidator.MaximumKeyRefLength"/> - so that a reference can
    /// never traverse out of the intended configuration namespace or be shaped like a path. That
    /// check is on the DECLARED set rather than only on the caller's input, because the declared set
    /// is what the caller's input is matched against: an unsafe entry here would legitimise an unsafe
    /// reference for every caller.
    /// </para>
    /// <para>
    /// ONE OPERATIONAL NOTE ON THE UNDERSCORE, WHICH IS PERMITTED AND STAYS PERMITTED. A single
    /// underscore is an ordinary identifier character, and a DOUBLED one is equally safe here, because
    /// the composed lookup is a literal flat key and no traversal is possible either way. It is,
    /// however, a footgun for material supplied as an environment variable: the environment provider
    /// folds a doubled underscore into a section separator on the way IN, so a variable whose name
    /// contains one lands under a different configuration key than the literal lookup asks for, and
    /// the reference then simply fails to resolve. Prefer a single underscore, a hyphen or a dot in a
    /// reference name. That is guidance and not a rule: rejecting a doubled underscore would be policy
    /// invented here, and it would protect nothing.
    /// </para>
    /// <para>
    /// Get-only and empty by default. The configuration binder populates the existing instance, so
    /// <c>Security:KeyStore:PermittedKeyRefs</c> binds as usual, while the absence of a setter keeps
    /// the collection from being replaced or nulled. An empty set is entirely valid and is what the
    /// settings file ships: it means no caller-referenced key material is configured, so every
    /// reference a caller could name is rejected. That is the safe default, and it is deliberately not
    /// an error.
    /// </para>
    /// <para>
    /// A REFERENCE IS NOT A KEY. Entries here are identifiers; the material they name lives outside
    /// this file, outside every settings file and outside every container definition, reached only
    /// through configuration injection from the orchestration layer. There is deliberately no map
    /// from a reference to a value, because such a map bound from a settings file would be exactly
    /// the hardcoded material this refactor's mandate eliminates.
    /// </para>
    /// </remarks>
    public IList<string> PermittedKeyRefs { get; } = [];
}

/// <summary>
/// Startup validation for <see cref="SecurityOptions"/>. Registered as a singleton
/// <see cref="IValidateOptions{TOptions}"/> alongside <c>ValidateOnStart</c>, so that a
/// misconfiguration refuses the host instead of surfacing on the first token request.
/// </summary>
/// <remarks>
/// <para>
/// A DISCRETE TYPE RATHER THAN A VALIDATION LAMBDA, and that is a testability requirement rather than
/// a style preference. Every rule below has to be drivable directly from the sibling test project -
/// construct an options instance, call <see cref="Validate"/>, assert the failure - without booting a
/// host, because that is what makes the per-service line-coverage gate reachable on this logic. A
/// lambda registered inside the service entry point is reachable only by starting a host, so it is
/// both slower to test and impossible to test exhaustively.
/// </para>
/// <para>
/// EVERY FAILURE IS AGGREGATED, never short-circuited on the first. An operator fixing a bring-up
/// should see all of it at once: one restart per defect is a poor experience and, worse, it hides
/// compounded misconfiguration, so a deployment with three faults reads as three faults.
/// </para>
/// <para>
/// EVERY MESSAGE NAMES THE OFFENDING CONFIGURATION KEY AND NO MESSAGE EVER ECHOES A VALUE. The key
/// paths are composed from <see cref="SecurityOptions.SectionName"/> so they cannot drift from the
/// section actually bound, collection failures carry the offending index, and the signing-material
/// failure is one fixed sentence - <see cref="SigningKeyUnusableMessage"/> - that describes the
/// accepted shapes and says nothing whatsoever about what was supplied: not the value, not a
/// substring, not a prefix, not even its length. A startup diagnostic is written to a log that is
/// shipped and retained, so it is exactly the wrong place to describe key material.
/// </para>
/// <para>
/// THE PRESENCE RULES ARE CHECKED HERE AS WELL AS BY THE ANNOTATIONS ON THE PROPERTIES, deliberately.
/// The annotations document each requirement where the property is declared and are enforced by
/// <c>ValidateDataAnnotations</c>; repeating them here means this validator is sufficient ON ITS OWN,
/// so the host still refuses to start if the annotation pass is ever left off the registration, and a
/// test can assert the complete rejection set against a single object. The overlap costs one
/// comparison per property and removes a whole class of registration-order defect.
/// </para>
/// </remarks>
public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    /// <summary>
    /// The closed set of JOSE signature algorithms this issuer may be configured to use.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A COMPILED CONSTANT THAT CONFIGURATION CANNOT EXTEND. It is exposed so that a test and a
    /// diagnostic can read the same list the check reads, and it is an immutable array behind a
    /// read-only interface so that no caller can add to it at run time. Widening this set is a source
    /// change, reviewed as one, which is the whole point: it decides what three other services must
    /// accept.
    /// </para>
    /// <para>
    /// Ordinal, case-sensitive comparison is used against it because JOSE algorithm identifiers are
    /// case-sensitive - <c>rs256</c> is not <c>RS256</c>, and quietly accepting the former would
    /// publish an algorithm identifier no stock verifier recognises.
    /// </para>
    /// <para>
    /// The reasoning for the three members and for every exclusion is recorded on
    /// <see cref="SecurityOptions.SigningAlgorithm"/> and in this file's header: the HMAC family is
    /// excluded structurally, the PSS family on the legacy signing primitive's missing padding
    /// argument [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70], and the elliptic-curve family
    /// because the legacy cryptographic class declares no such primitive. The three admitted values
    /// correspond to the legacy hash types SHA-256, SHA-384 and SHA-512, which that library's own
    /// constant catalogue names as the argument set of its signing and verifying primitives
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L927, L930-L932].
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> PermittedSigningAlgorithms { get; } =
        ["RS256", "RS384", "RS512"];

    /// <summary>
    /// The request-path prefix that both published metadata documents must sit under, and that the
    /// token endpoint must NOT sit under: <c>/.well-known/</c>.
    /// </summary>
    /// <remarks>
    /// This is an authentication boundary rather than a naming convention. The service entry point
    /// applies a default-deny authorization policy and exempts exactly three paths from it - the
    /// health probe and the two metadata documents - so a metadata path outside this prefix would
    /// leave a document that must be anonymous behind authentication, and a token endpoint inside it
    /// would put the one route that mints tokens inside the anonymous namespace. Both directions are
    /// checked.
    /// </remarks>
    public const string WellKnownMetadataPathPrefix = "/.well-known/";

    /// <summary>
    /// The upper bound on the length of a permitted key reference: 64 characters.
    /// </summary>
    /// <remarks>
    /// A key reference is caller-supplied text that gets concatenated onto a configuration-key prefix,
    /// so it is bounded as well as charset-constrained. Sixty-four characters comfortably admits a
    /// descriptive identifier or a hex-encoded fingerprint while leaving no room for a value smuggled
    /// in as a name.
    /// </remarks>
    public const int MaximumKeyRefLength = 64;

    /// <summary>
    /// The one fixed message reported when the signing material is present but cannot be imported as
    /// an asymmetric private key. It describes the accepted shapes and names the configuration key;
    /// it says nothing at all about the material that was supplied.
    /// </summary>
    /// <remarks>
    /// Fixed rather than composed so that no code path can accidentally interpolate the material, a
    /// fragment of it, or a measurement of it. The platform exception that caused the rejection is
    /// deliberately not attached and not re-thrown either: those describe a structure rather than
    /// their input, but there is no need to find out the hard way.
    /// </remarks>
    public const string SigningKeyUnusableMessage =
        "Configuration key '" + SecurityOptions.SigningKeyEnvironmentVariableName + "' is set but " +
        "could not be imported as an asymmetric private key. Two encodings are accepted and both " +
        "are attempted: armoured text first, then base64 of the bare binary encoding on a single " +
        "line. Random bytes are not an asymmetric key and cannot be used to sign; generate an RSA " +
        "private key instead. This message never echoes the configured value.";

    /// <summary>
    /// <see cref="MaximumKeyRefLength"/> rendered once, culture-invariantly, for the one failure
    /// message that quotes it.
    /// </summary>
    /// <remarks>
    /// Rendered once and invariantly rather than interpolated at the call site so that the bound
    /// appears with ASCII digits whatever culture the host happens to be running under. A startup
    /// diagnostic is read by an operator and grepped by tooling, so its numbers must not vary with the
    /// machine's locale. A constant cannot express this because formatting is a run-time operation.
    /// </remarks>
    private static readonly string MaximumKeyRefLengthText =
        MaximumKeyRefLength.ToString(CultureInfo.InvariantCulture);

    private const string IssuerKey = SecurityOptions.SectionName + ":Issuer";
    private const string AudiencesKey = SecurityOptions.SectionName + ":Audiences";
    private const string TokenLifetimeKey = SecurityOptions.SectionName + ":TokenLifetime";
    private const string SigningKeyIdKey = SecurityOptions.SectionName + ":SigningKeyId";
    private const string SigningAlgorithmKey = SecurityOptions.SectionName + ":SigningAlgorithm";
    private const string JwksPathKey = SecurityOptions.SectionName + ":JwksPath";

    private const string OpenIdConfigurationPathKey =
        SecurityOptions.SectionName + ":OpenIdConfigurationPath";

    private const string TokenEndpointPathKey = SecurityOptions.SectionName + ":TokenEndpointPath";
    private const string KeyStoreKey = SecurityOptions.SectionName + ":KeyStore";
    private const string ConfigurationKeyPrefixKey = KeyStoreKey + ":ConfigurationKeyPrefix";
    private const string PermittedKeyRefsKey = KeyStoreKey + ":PermittedKeyRefs";

    /// <summary>
    /// Validates a bound <see cref="SecurityOptions"/> instance, returning every failure found rather
    /// than only the first.
    /// </summary>
    /// <param name="name">
    /// The options name being validated. Every instance is validated regardless of name, because
    /// exactly one <see cref="SecurityOptions"/> exists in this service: a named instance that escaped
    /// validation would be a way to configure an unvalidated issuer.
    /// </param>
    /// <param name="options">The bound instance to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ValidateOptionsResult.Success"/> when every rule holds; otherwise
    /// <see cref="ValidateOptionsResult.Fail(IEnumerable{string})"/> carrying one message per failure,
    /// each naming the offending configuration key and none echoing a value.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The rules are grouped rather than inlined so that each group is independently readable and
    /// independently assertable. Ordering within the result is stable - signing material first,
    /// because a Security service that cannot sign is the fault that matters most - but no rule
    /// depends on another having run, so a single call surfaces the complete picture.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        CheckSigningMaterial(options, failures);
        CheckIssuer(options, failures);
        CheckAudienceRoster(options, failures);
        CheckSigningKeyIdentifier(options, failures);
        CheckTokenLifetimeIsPositive(options, failures);
        CheckSigningAlgorithmIsPermitted(options, failures);
        CheckMetadataPaths(options, failures);
        CheckTokenEndpointPath(options, failures);
        CheckKeyStore(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Rejections 1 and 2: the signing material must be present, and it must be usable as an
    /// asymmetric private key.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// THE TWO FAULTS ARE REPORTED SEPARATELY because they have different fixes: absent material means
    /// the variable was never populated, whereas unusable material means it was populated with the
    /// wrong thing - random bytes being the case that actually happens, since it looks like a
    /// plausible way to produce a signing value. Collapsing them into one message would send an
    /// operator to the wrong place.
    /// </para>
    /// <para>
    /// THERE IS NO FALLBACK ARM HERE, IN ANY ENVIRONMENT. No key is generated, no previous key is
    /// reused and no development exemption exists. Generating one would trade a loud startup failure
    /// for a silent fleet-wide authentication outage in which every token the other three services
    /// hold stops verifying, and each restart would invalidate the last generation's tokens as well.
    /// The legacy posture this mirrors terminates the process on a structural fault rather than
    /// continuing in a degraded state [ws_objects/pfw.pbl.src/pfw.sra:L143].
    /// </para>
    /// <para>
    /// NO KEY-SIZE RULE IS APPLIED, and that omission is a preserved legacy allowance rather than an
    /// oversight: the legacy constant catalogue keeps 1024-bit RSA as a first-class legal size
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L965], so refusing a short key here would be the kind
    /// of silent legacy correction this refactor forbids. A size the minting library itself refuses is
    /// that library's report to make, not this validator's policy to invent.
    /// </para>
    /// </remarks>
    private static void CheckSigningMaterial(SecurityOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add(
                $"Configuration key '{SecurityOptions.SigningKeyEnvironmentVariableName}' is not " +
                "set, is empty or contains only whitespace. It is the only signing key in the " +
                "system and it has no default: this service cannot mint a token without it, and " +
                "none is generated in any environment because doing so would invalidate every " +
                "token the other services currently hold. Supply an asymmetric private key through " +
                "the orchestration configuration layer. Note that this name is a FLAT " +
                "configuration key rather than a member of the '" + SecurityOptions.SectionName +
                "' section, because the environment provider only folds a double underscore into a " +
                "section separator.");

            // Nothing further to say about material that is not there. The usability check below
            // would only add a second failure describing the same single defect.
            return;
        }

        if (!CanImportPrivateKey(options.SigningKey))
        {
            failures.Add(SigningKeyUnusableMessage);
        }
    }

    /// <summary>
    /// Rejection 3: the issuer identity must be present.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// Blank includes whitespace-only, because a whitespace issuer would be stamped into the
    /// <c>iss</c> claim and published in the discovery document as though it were an identity. Three
    /// other services validate that claim, so an empty issuer does not fail locally - it fails
    /// everywhere, later, as an authentication error with no obvious cause.
    /// </remarks>
    private static void CheckIssuer(SecurityOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' is required and must not be blank. It is the " +
                "'iss' claim of every minted token, the 'issuer' member of the discovery document, " +
                "and the value the other services validate every token against.");
        }
    }

    /// <summary>
    /// Rejection 4 plus the roster coherence rules: at least one audience, no blank entry, and no
    /// case-insensitive duplicate.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// NO PARTICULAR COUNT IS REQUIRED, only that the roster is not empty: which services this issuer
    /// serves is a deployment decision, and a hardcoded count here would break the moment a service is
    /// added or a deployment narrows the roster deliberately. An EMPTY roster is different in kind - an
    /// issuer that may address no audience cannot mint a usable token at all.
    /// </para>
    /// <para>
    /// DUPLICATES ARE COMPARED CASE-INSENSITIVELY because an audience is a service identity rather
    /// than a case-sensitive protocol token, so two spellings of one identity are a configuration
    /// mistake however they differ in case. The offending index is named so the fault is locatable in
    /// a settings file; the value is not echoed, because an audience list is the sort of thing that
    /// gets pasted between deployments and the message should not become a second copy of it.
    /// </para>
    /// </remarks>
    private static void CheckAudienceRoster(SecurityOptions options, List<string> failures)
    {
        if (options.Audiences.Count == 0)
        {
            failures.Add(
                $"Configuration key '{AudiencesKey}' must list at least one audience. An issuer " +
                "that may address no audience cannot mint a usable token.");

            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < options.Audiences.Count; index++)
        {
            string audience = options.Audiences[index];

            if (string.IsNullOrWhiteSpace(audience))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(AudiencesKey, index)}' is blank. Every audience must " +
                    "be " +
                    "a non-blank service identity.");

                continue;
            }

            if (!seen.Add(audience.Trim()))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(AudiencesKey, index)}' repeats an audience " +
                    "already " +
                    "listed earlier, ignoring case. Each service identity must appear exactly once.");
            }
        }
    }

    /// <summary>
    /// Rejection 5: the key identifier must be present.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// Without it the published key set carries no <c>kid</c> and every verifier is left guessing
    /// which key to select, which also makes rotation impossible: overlapping an old and a new key
    /// depends entirely on the two being distinguishable by identifier.
    /// </remarks>
    private static void CheckSigningKeyIdentifier(SecurityOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.SigningKeyId))
        {
            failures.Add(
                $"Configuration key '{SigningKeyIdKey}' is required and must not be blank. It is " +
                "the 'kid' published in the key set and stamped into every token header, and it is " +
                "what makes key rotation possible.");
        }
    }

    /// <summary>
    /// Rejection 6: the token lifetime must be strictly positive.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// This rule lives in the validator rather than in an attribute because a range attribute cannot
    /// express "a <see cref="TimeSpan"/> greater than zero" - it compares against numeric bounds, and
    /// a duration parsed from configuration is neither a number nor comparable to one without
    /// stringly-typed conversion. Zero and negative are both rejected: either mints tokens that are
    /// already expired at the instant they are issued, which every verifier then rejects, so the
    /// service would appear healthy while nothing it produced ever worked.
    /// </remarks>
    private static void CheckTokenLifetimeIsPositive(SecurityOptions options, List<string> failures)
    {
        if (options.TokenLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"Configuration key '{TokenLifetimeKey}' must be a positive duration. A zero or " +
                "negative lifetime mints tokens that are already expired when they are issued.");
        }
    }

    /// <summary>
    /// Hardening 7: the signature algorithm must be a member of the closed allow-list.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// Presence is reported on its own when the value is blank, so a deployment that omitted the key
    /// is not also told its value is unrecognised. Otherwise the value is matched ordinally and
    /// case-sensitively against <see cref="PermittedSigningAlgorithms"/>, because JOSE algorithm
    /// identifiers are case-sensitive.
    /// </para>
    /// <para>
    /// THE FAILURE LISTS WHAT IS ACCEPTED AND NEVER ECHOES WHAT WAS SUPPLIED. Naming the permitted
    /// values is what an operator needs; repeating the rejected value adds nothing and would put
    /// caller-influenced text into a startup log.
    /// </para>
    /// </remarks>
    private static void CheckSigningAlgorithmIsPermitted(
        SecurityOptions options,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.SigningAlgorithm))
        {
            failures.Add(
                $"Configuration key '{SigningAlgorithmKey}' is required and must not be blank. " +
                $"Permitted values are {DescribePermittedAlgorithms()}.");

            return;
        }

        bool permitted = false;

        foreach (string candidate in PermittedSigningAlgorithms)
        {
            if (string.Equals(candidate, options.SigningAlgorithm, StringComparison.Ordinal))
            {
                permitted = true;

                break;
            }
        }

        if (!permitted)
        {
            failures.Add(
                $"Configuration key '{SigningAlgorithmKey}' is not one of the permitted signature " +
                $"algorithms. The permitted set is closed and is exactly {DescribePermittedAlgorithms()}, " +
                "compared case-sensitively. Symmetric algorithms are excluded because this service " +
                "publishes its verification material anonymously, so a symmetric key in that " +
                "document would publish the signing key itself and make every verifier a co-signer. " +
                "Probabilistic RSA padding and elliptic-curve signatures are excluded because the " +
                "legacy cryptographic surface can express neither. This message never echoes the " +
                "configured value.");
        }
    }

    /// <summary>
    /// Hardening 8: both published metadata paths must sit under
    /// <see cref="WellKnownMetadataPathPrefix"/>.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// An authentication control, not a naming rule. The service entry point applies a default-deny
    /// authorization policy and exempts exactly three paths - the health probe and these two - so a
    /// value outside the prefix would either leave a document that must be anonymous behind
    /// authentication, breaking every consumer's bearer handler before it holds a token, or move the
    /// anonymous exemption onto a path that should have been authenticated.
    /// </remarks>
    private static void CheckMetadataPaths(SecurityOptions options, List<string> failures)
    {
        CheckWellKnownPath(options.JwksPath, JwksPathKey, "the key set", failures);

        CheckWellKnownPath(
            options.OpenIdConfigurationPath,
            OpenIdConfigurationPathKey,
            "the discovery document",
            failures);
    }

    /// <summary>
    /// One published metadata path.
    /// </summary>
    /// <param name="value">The configured path.</param>
    /// <param name="configurationKey">The full configuration key path, for the message.</param>
    /// <param name="documentDescription">What the path publishes, for the message.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// The prefix must be present AND something must follow it: the bare prefix names a directory
    /// rather than a document, so it would register a route that publishes nothing while passing a
    /// naive prefix test.
    /// </remarks>
    private static void CheckWellKnownPath(
        string value,
        string configurationKey,
        string documentDescription,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(
                $"Configuration key '{configurationKey}' is required and must not be blank. It is " +
                $"the request path {documentDescription} is published at.");

            return;
        }

        bool rootedInWellKnown =
            value.StartsWith(WellKnownMetadataPathPrefix, StringComparison.Ordinal) &&
            value.Length > WellKnownMetadataPathPrefix.Length;

        if (!rootedInWellKnown)
        {
            failures.Add(
                $"Configuration key '{configurationKey}' must begin with " +
                $"'{WellKnownMetadataPathPrefix}' and must name a document beneath it. The service " +
                "grants anonymous access to exactly this path, the discovery path and the health " +
                $"probe, so a value outside '{WellKnownMetadataPathPrefix}' would either hide " +
                $"{documentDescription} behind authentication or aim the anonymous exemption at a " +
                "route that must stay authenticated. This message never echoes the configured value.");
        }
    }

    /// <summary>
    /// Hardening 9: the token endpoint must be rooted and must NOT fall inside
    /// <see cref="WellKnownMetadataPathPrefix"/>.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// The same anonymous-exemption control as <see cref="CheckMetadataPaths"/>, inverted. The route
    /// that mints tokens is the single most sensitive one this service exposes, and the entry point
    /// exempts the metadata namespace from authorization wholesale, so a token endpoint inside that
    /// namespace would become anonymous without anything in the endpoint's own declaration changing.
    /// Rootedness is required as well, because an unrooted value is not a request path.
    /// </remarks>
    private static void CheckTokenEndpointPath(SecurityOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.TokenEndpointPath))
        {
            failures.Add(
                $"Configuration key '{TokenEndpointPathKey}' is required and must not be blank. It " +
                "is the single declared source of truth for the token endpoint: the endpoint maps " +
                "its route from it and the discovery document advertises it, and the two must agree.");

            return;
        }

        if (!options.TokenEndpointPath.StartsWith('/'))
        {
            failures.Add(
                $"Configuration key '{TokenEndpointPathKey}' must be a rooted request path " +
                "beginning with '/'. This message never echoes the configured value.");
        }

        if (options.TokenEndpointPath.StartsWith(WellKnownMetadataPathPrefix, StringComparison.Ordinal))
        {
            failures.Add(
                $"Configuration key '{TokenEndpointPathKey}' must not begin with " +
                $"'{WellKnownMetadataPathPrefix}'. That namespace is exempted from authorization so " +
                "that the key set and the discovery document can be fetched anonymously, so a token " +
                "endpoint inside it would be reachable without authentication. This message never " +
                "echoes the configured value.");
        }
    }

    /// <summary>
    /// Hardening 10 plus the key-store coherence rule: every permitted key reference must be a safe
    /// bounded identifier, and a prefix must be configured whenever any reference is permitted.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// THE CHARSET RULE IS THE POINT. A permitted reference is concatenated onto a configuration-key
    /// prefix to resolve material, so an entry containing a separator, a path segment or whitespace
    /// would let a caller naming that reference reach a configuration key the key store was never
    /// meant to expose. Constraining the DECLARED set is what makes the caller-side match safe, since
    /// the declared set is exactly what a caller's input is matched against.
    /// </para>
    /// <para>
    /// AN EMPTY SET IS VALID AND IS THE SHIPPED DEFAULT. It means no caller-referenced material is
    /// configured, so every reference a caller could name is rejected - the safe state, and
    /// deliberately not an error. The prefix is therefore required only when the set is non-empty: a
    /// permitted reference with no prefix to resolve it against is unresolvable configuration and
    /// should stop the host, whereas demanding a prefix from a deployment that permits nothing would
    /// be a fabricated requirement.
    /// </para>
    /// <para>
    /// NO DEFERRED-CAPABILITY DENY-LIST EXISTS HERE, and none may be added. Nothing outside the
    /// declared set is reachable in the first place, so the allow-list already carries the whole
    /// policy; a list of names not to accept would add no protection while writing capability areas
    /// this phase does not implement into a file that would then have to name them.
    /// </para>
    /// </remarks>
    private static void CheckKeyStore(SecurityOptions options, List<string> failures)
    {
        SecurityKeyStoreOptions keyStore = options.KeyStore;

        for (int index = 0; index < keyStore.PermittedKeyRefs.Count; index++)
        {
            if (!IsSafeKeyRef(keyStore.PermittedKeyRefs[index]))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(PermittedKeyRefsKey, index)}' is not a safe " +
                    $"key reference. Each entry must be 1 to {MaximumKeyRefLengthText} " +
                    "characters of ASCII letters, ASCII digits, '.', '_' or '-' only. A reference is " +
                    "concatenated onto the key-store prefix to resolve material, so anything looking " +
                    "like a path or carrying a separator could reach a configuration key this store " +
                    "does not own. This message never echoes the configured value.");
            }
        }

        bool prefixRequired = keyStore.PermittedKeyRefs.Count > 0;

        if (prefixRequired && string.IsNullOrWhiteSpace(keyStore.ConfigurationKeyPrefix))
        {
            failures.Add(
                $"Configuration key '{ConfigurationKeyPrefixKey}' is required because " +
                $"'{PermittedKeyRefsKey}' permits at least one key reference. A permitted reference " +
                "with no prefix to resolve it against is unresolvable configuration.");
        }
    }

    /// <summary>
    /// Renders the full configuration key path of one element of a bound collection, so that a failure
    /// about element <paramref name="index"/> is locatable in a settings file.
    /// </summary>
    /// <param name="configurationKey">The collection's own configuration key path.</param>
    /// <param name="index">The zero-based index of the offending element.</param>
    /// <returns>The element's key path, for example <c>Security:Audiences[2]</c>.</returns>
    /// <remarks>
    /// One renderer for all three indexed failures, and culture-invariant, for two reasons. The index
    /// must appear with ASCII digits whatever culture the host runs under, since the whole value of the
    /// message is that it can be pasted into a search of a settings file. And the bracketed form is the
    /// spelling the configuration providers themselves use for a collection element, so a single
    /// renderer keeps all three messages speaking the operator's own notation rather than three
    /// approximations of it. The index is a position, never a value, so nothing sensitive passes
    /// through here.
    /// </remarks>
    private static string IndexedKey(string configurationKey, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{configurationKey}[{index}]");

    /// <summary>
    /// Renders the permitted algorithm set for a failure message.
    /// </summary>
    /// <returns>The permitted values, comma separated.</returns>
    /// <remarks>
    /// Composed from <see cref="PermittedSigningAlgorithms"/> rather than written out, so a message can
    /// never describe a set the check does not enforce. The values are algorithm identifiers and carry
    /// nothing sensitive.
    /// </remarks>
    private static string DescribePermittedAlgorithms() =>
        string.Join(", ", PermittedSigningAlgorithms);

    /// <summary>
    /// Determines whether a key reference is a safe, bounded identifier.
    /// </summary>
    /// <param name="value">The declared key reference.</param>
    /// <returns>
    /// <see langword="true"/> when the reference is 1 to <see cref="MaximumKeyRefLength"/> characters
    /// of ASCII letters, ASCII digits, <c>.</c>, <c>_</c> or <c>-</c>; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Hand-written rather than expressed as a regular expression, deliberately. The rule is a
    /// four-clause character test, so a character loop is shorter than the pattern would be, allocates
    /// nothing, needs no pattern cache and cannot be misread; a pattern would additionally invite the
    /// classic anchoring mistake in which a partially matching value is accepted.
    /// </para>
    /// <para>
    /// ASCII-ONLY BY DESIGN. The general Unicode letter and digit categories would admit characters
    /// that are visually confusable with the permitted set, and a configuration-key name is machine
    /// input rather than human text, so the conservative reading is the correct one. A
    /// <see langword="null"/> entry - possible in a bound collection - is rejected here rather than
    /// dereferenced.
    /// </para>
    /// </remarks>
    private static bool IsSafeKeyRef(string? value)
    {
        if (value is null || value.Length == 0 || value.Length > MaximumKeyRefLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool permitted =
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '-';

            if (!permitted)
            {
                return false;
            }
        }

        return true;
    }


    /// <summary>
    /// Determines whether the configured signing material can be imported as an asymmetric private
    /// key, without disclosing anything about it.
    /// </summary>
    /// <param name="material">The configured signing material. Never logged, echoed or measured.</param>
    /// <returns>
    /// <see langword="true"/> when the material imports as a private key in either accepted encoding;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// BOTH ENCODINGS ARE ACCEPTED, IN A FIXED ORDER, AND NEITHER MAY BE REFUSED. Armoured text is
    /// attempted first, then base64 of the bare binary encoding. Both shapes genuinely occur because
    /// the legacy key generator's armoured output is an OPTIONAL fourth argument
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20]: material produced without it is bare, and
    /// material produced with it is armoured. The single-line base64 form is additionally the only
    /// shape an environment file can carry, since that format has no line continuation.
    /// </para>
    /// <para>
    /// THE ACCEPTED SET IS FIXED IN CODE AND IS NOT SELECTED BY CONFIGURATION. Every path is attempted
    /// and only a value that fails all of them is rejected, so there is no setting that could tell this
    /// check to expect the wrong shape and reject a valid key.
    /// </para>
    /// <para>
    /// A PUBLIC KEY IS NOT SUFFICIENT AND IS REJECTED. Armoured import will happily read public
    /// material, so the armoured path additionally asserts that a private component is present; the
    /// bare paths need no such check because the two structures they attempt are private-key structures
    /// by definition. An issuer holding only a public key would start and then fail to sign, which is
    /// precisely the outcome fail-fast validation exists to prevent.
    /// </para>
    /// <para>
    /// KEY HYGIENE. Nothing here logs, traces, re-throws or returns any part of the material: the
    /// method's entire vocabulary is <see langword="true"/> and <see langword="false"/>. Every key
    /// instance is disposed, including every rejected candidate, and every decoded or exported byte
    /// buffer is zeroed before it is released rather than left for the garbage collector. The lambdas
    /// passed below are <see langword="static"/> so that no key material can be captured in a closure.
    /// </para>
    /// <para>
    /// This is the only operation in the file that is not a pure comparison, and it performs no I/O:
    /// the import is entirely in memory, so validation stays runnable in a unit test with no host, no
    /// file system and no network.
    /// </para>
    /// </remarks>
    private static bool CanImportPrivateKey(string material)
    {
        if (TryImportArmoured(material))
        {
            return true;
        }

        byte[]? bare = TryDecodeBase64(material);

        if (bare is null)
        {
            // Neither armoured nor valid base64, so there is no third encoding left to attempt.
            return false;
        }

        try
        {
            return TryImportBareStructures(bare);
        }
        finally
        {
            // The decoded buffer holds the key in the clear. Clear it rather than waiting for
            // collection, which is non-deterministic and may copy the buffer while compacting.
            CryptographicOperations.ZeroMemory(bare);
        }
    }

    /// <summary>
    /// Attempts the armoured encoding.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>
    /// <see langword="true"/> when the material is armoured AND carries a private component;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// No delimiter literal is tested for and none appears anywhere in this file. The attempt is
    /// simply made and its failure interpreted, which is both simpler than sniffing for a header and
    /// deliberate: a delimiter string is exactly what a credential scanner matches on, so writing one
    /// here would trip the scan that protects this file for no functional gain. Two exception types
    /// are handled because the platform reports unreadable content as a cryptographic failure and text
    /// carrying no usable block as an argument failure; both mean the same thing to this caller.
    /// </remarks>
    private static bool TryImportArmoured(string material)
    {
        using RSA candidate = RSA.Create();

        try
        {
            candidate.ImportFromPem(material);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return HasPrivateComponent(candidate);
    }

    /// <summary>
    /// Attempts each bare binary private-key structure in turn.
    /// </summary>
    /// <param name="bare">The decoded key bytes.</param>
    /// <returns>
    /// <see langword="true"/> when one of the attempted structures imported; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The order is evidence-led rather than arbitrary: the documented generation command in
    /// <c>orchestration/.env.example</c> produces the modern algorithm-tagged structure, so that is
    /// attempted first and the overwhelmingly common case costs one attempt. The older RSA-specific
    /// structure is attempted second, because an existing key may already be in it. Both are
    /// PRIVATE-key structures, so a public key cannot pass through this path and no separate private
    /// component check is required here. Public-key structures are deliberately not attempted at all:
    /// this method answers "can this sign?", and a public key cannot.
    /// </remarks>
    private static bool TryImportBareStructures(byte[] bare) =>
        TryImportBareStructure(bare, static (key, bytes) => key.ImportPkcs8PrivateKey(bytes, out _)) ||
        TryImportBareStructure(bare, static (key, bytes) => key.ImportRSAPrivateKey(bytes, out _));

    /// <summary>
    /// Attempts exactly one bare binary key structure.
    /// </summary>
    /// <param name="bare">The decoded key bytes.</param>
    /// <param name="import">The single platform import to attempt.</param>
    /// <returns>
    /// <see langword="true"/> when the bytes hold that structure; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Returns a verdict rather than raising, so the caller can attempt the next structure without
    /// exception control flow spanning the whole set. The candidate instance is disposed on both paths
    /// by the using declaration, so a partially initialised key is never left behind for the next
    /// attempt to inherit.
    /// </para>
    /// <para>
    /// EXACTLY ONE EXCEPTION TYPE IS HANDLED, and that was MEASURED on this platform rather than
    /// assumed: both bare imports report every rejection - random bytes, empty input, a public-key
    /// structure, and the other private structure's encoding - as a cryptographic exception or a
    /// platform-specific subclass of it, which this catch covers. No second arm is written for an
    /// exception type the platform does not raise here: an unreachable catch is dead code that cannot
    /// be tested and quietly claims to handle something. Anything genuinely unexpected propagates,
    /// which still refuses the host - the fail-fast outcome - rather than being swallowed.
    /// </para>
    /// </remarks>
    private static bool TryImportBareStructure(byte[] bare, Action<RSA, byte[]> import)
    {
        using RSA candidate = RSA.Create();

        try
        {
            import(candidate, bare);

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether an imported key carries a private component, and therefore whether it can
    /// sign.
    /// </summary>
    /// <param name="key">The imported key.</param>
    /// <returns>
    /// <see langword="true"/> when a private component is present; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Asked by attempting the private-key export and interpreting its failure, which is the platform's
    /// own answer to the question rather than an inference from key parameters. The exported buffer is
    /// key material in the clear and is therefore ZEROED in a finally block before being released,
    /// whichever way the attempt went; it is never returned, inspected, measured or logged. The export
    /// exists solely to be discarded.
    /// </para>
    /// <para>
    /// WHY THIS CHECK EXISTS AT ALL, measured rather than assumed: armoured import of a PUBLIC key
    /// SUCCEEDS on this platform. Without this assertion an issuer configured with the public half
    /// would pass validation, start, satisfy the readiness gate and then fail to sign every token -
    /// exactly the outcome fail-fast validation exists to prevent. A single exception type is handled
    /// because a public-only export is the one way this can fail and the platform reports it as a
    /// cryptographic exception; anything else propagates and still refuses the host.
    /// </para>
    /// </remarks>
    private static bool HasPrivateComponent(RSA key)
    {
        byte[]? exported = null;

        try
        {
            exported = key.ExportPkcs8PrivateKey();

            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            if (exported is not null)
            {
                CryptographicOperations.ZeroMemory(exported);
            }
        }
    }

    /// <summary>
    /// Decodes base64 signing material, returning <see langword="null"/> rather than raising when the
    /// text is not base64 at all.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>The decoded bytes, or <see langword="null"/> when the text is not valid base64.</returns>
    /// <remarks>
    /// The platform decoder tolerates embedded whitespace and line breaks, so material that arrived
    /// wrapped across lines by a secret store or an editor still decodes. A failure here is not an
    /// error to report on its own: it simply means this encoding is not the one, and the caller turns
    /// it into the single fixed rejection message that names no part of the value.
    /// </remarks>
    private static byte[]? TryDecodeBase64(string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

