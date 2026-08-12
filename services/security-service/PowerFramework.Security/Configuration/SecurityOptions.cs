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
//  WHAT THIS FILE *DOES* DECLARE THAT AN EARLIER REVISION REFUSED TO, AND WHY THAT REFUSAL WAS WRONG
//  ==================================================================================================
//  An earlier revision of this header argued that SigningKeyFormat and SigningKeyMinimumSizeBits must
//  stay UNBOUND, on the ground that a key-size floor would be a silent legacy correction: the legacy
//  constant catalogue keeps CRYPTO_RSA_BITS_1024 = 1024 as a first-class legal size
//  [ws_objects/pfw.shared.pbl.src/enums.sru:L965]. The premise is true. The conclusion does not follow,
//  and the settings file has carried the correct reasoning at length all along:
//
//    * THE 1024 ALLOWANCE BELONGS TO A DIFFERENT SURFACE. It is preserved on C-02's key GENERATION
//      surface, where the caller supplies the size and byte-for-byte parity is the obligation - see
//      Crypto/LegacyDefaults.cs and AAP 0.6.6.4, which lists 1024-bit RSA among the weak defaults
//      replicated as ANNOTATED defaults. Nothing below touches that surface, and a test pins the two
//      apart so a future edit cannot quietly merge them.
//    * THE SIGNING KEY IS NET-NEW, SO THERE IS NO LEGACY BEHAVIOUR TO CORRECT. The legacy has no token
//      issuer at all [AAP 0.1.4: decomposition creates the system's first-ever ingress], so this key
//      has no legacy analogue whose behaviour a floor could change. Applying C-02's allowance to the
//      system's own trust root would not be parity - it would import a weakness from a surface that has
//      nothing to do with it.
//    * AND AN UNBOUND LEAF IS WORSE THAN NO LEAF. Both keys were declared and documented in
//      appsettings.json while the binder ignored them, so a deployment could set
//      SigningKeyMinimumSizeBits to 4096, read the documentation, and still start with a 1024-bit key.
//      A setting that is declared, documented and inert is a false assurance rather than a neutral one.
//
//  Both are therefore bound and enforced below. The format discriminator is NOT dead either: it names
//  the closed set of accepted shapes and the acceptance ORDER, so a value outside that set is refused
//  at startup instead of reaching an import that would fail for an unexplained reason.
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
    /// coordinated change rather than a local one.
    /// </para>
    /// <para>
    /// <b>ITS SHAPE IS CONSTRAINED, AND AN EARLIER REVISION OF THIS PARAGRAPH SAID OTHERWISE.</b> That
    /// revision argued the format was a deployment decision and that a rule invented here could reject
    /// an identity a deployment legitimately uses. The premise is wrong for this particular value,
    /// because the format is NOT free: the discovery document's <c>jwks_uri</c> and
    /// <c>token_endpoint</c> members are COMPOSED from it, so it has to be an absolute address a
    /// consumer's bearer handler can fetch. <see cref="SecurityOptionsValidator"/> therefore requires
    /// it to be absolute, http or https, and free of embedded credentials, a query string and a
    /// fragment - the identical rule every sibling service already applies to every address it binds.
    /// The consequence of leaving it unchecked was measured: six bogus shapes started the host and
    /// readiness opened on a service whose discovery document answered 500 while its key set answered
    /// 200, which breaks the exact mechanism contract C-01 depends on.
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
    /// What each authenticated caller is permitted to ask for: the audiences it may address and the
    /// scopes it may hold. Bound from <c>Security:Callers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, A TRUSTED IDENTITY WAS AN UNLIMITED ONE. <see cref="Audiences"/> is a GLOBAL roster
    /// - the set of identities this issuer may address at all - and on its own it says nothing about who
    /// may address them. Any caller whose certificate chained to the configured client authority could
    /// therefore request a token for ANY service in the system carrying ANY scope set it named, and every
    /// requested scope was granted verbatim. That is a confused-deputy hole in the middle of the token
    /// topology: the whole point of a sole issuer is that it decides, and it was not deciding. This
    /// roster is what turns "authenticated" into "authorised" (CWE-862, CWE-863; constraint C-G).
    /// </para>
    /// <para>
    /// IT IS A MATRIX, NOT TWO LISTS. Each entry carries a caller identity and one GRANT PER AUDIENCE, so
    /// the scopes a caller may hold are scoped to the audience it is addressing. DataServices addresses two
    /// audiences with entirely different scope sets, and a flat per-caller scope list would let it hold
    /// Security's cryptographic scope inside a Persistence-audience token - harmless only because each
    /// receiver checks the audience too, and harmful the moment two audiences share a scope name.
    /// </para>
    /// <para>
    /// THE PERMITTED SET IS AN INTERSECTION, NOT A DEMAND. A caller still asks for what it wants; the
    /// issuer grants the overlap between the request and the matching grant and reports what it granted.
    /// That is not an invented behaviour - contract C-01 states that the granted set MAY BE NARROWER than
    /// the requested one, that a narrowing is a successful outcome rather than an error, and that an empty
    /// granted set is a legal response value meaning nothing requested was granted. This roster is
    /// therefore the mechanism the published contract already anticipated.
    /// </para>
    /// <para>
    /// AN AUDIENCE, BY CONTRAST, IS NOT NARROWED - IT IS REFUSED. A token carries exactly one audience by
    /// contract, deliberately, so that it is never valid somewhere its holder did not intend; there is
    /// nothing to intersect, and a request naming an audience the caller holds no grant for is answered
    /// with the published forbidden response - the same response as an audience this issuer does not serve
    /// at all, so the refusal cannot be used to enumerate which audiences exist or which callers are
    /// configured.
    /// </para>
    /// <para>
    /// IDENTITIES ONLY, AND NO MATERIAL. Every value in this group is a name - a certificate common name,
    /// an audience identity, a scope string. There is no member here that could hold a certificate, a
    /// key, a thumbprint or a secret, which is the same rule the mutual-TLS group follows: the CHAIN is
    /// what establishes that a caller is who it claims, and this roster only says what that claim permits.
    /// </para>
    /// <para>
    /// AN EMPTY AUTHORIZATION MATRIX FAILS VALIDATION - BUT THE MATRIX IS THE UNION OF THIS MEMBER AND
    /// <see cref="CallerAuthorizations"/>, SO THE REQUIREMENT IS NOT EXPRESSIBLE AS AN ATTRIBUTE HERE. An
    /// issuer that permits no caller anything can mint nothing usable, and starting in that state would
    /// present a healthy service that refuses every issuance - which is indistinguishable from an outage.
    /// The two members are two AUTHORING SHAPES for one matrix, though: this one nests each caller's
    /// grants under its identity, and <see cref="CallerAuthorizations"/> states one flat caller-audience
    /// row at a time. A <c>[MinLength(1)]</c> here would demand the nested shape specifically and reject a
    /// deployment that authored the whole matrix in the flat one, which is a configuration this service
    /// reads and honours. <see cref="SecurityOptionsValidator"/> therefore states the requirement over
    /// BOTH members, names both keys in its diagnostic, and is the single place the rule lives.
    /// </para>
    /// <para>
    /// GET-ONLY for the same reason as <see cref="Audiences"/>: the binder populates an existing
    /// collection, so binding works exactly as it would with a setter while the property itself cannot be
    /// replaced or nulled.
    /// </para>
    /// </remarks>
    public IList<SecurityCallerOptions> Callers { get; } = [];

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
    /// The closed set of private-key encodings the signing material may arrive in, and the order they
    /// are attempted in. Defaults to <c>PemOrPkcs8Base64</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ONE PERMITTED VALUE TODAY, AND THE LEAF STILL EARNS ITS PLACE. <c>PemOrPkcs8Base64</c> names the
    /// acceptance order: PEM first - both the PKCS#8 form <c>openssl genpkey</c> emits and the older
    /// PKCS#1 form an existing key may already be in - then base64 of the DER encoding of a PKCS#8
    /// structure on a single line, which is what <c>openssl genpkey ... -outform DER | base64 -w0</c>
    /// produces and what <c>orchestration/.env.example</c> instructs, because an environment file has no
    /// line continuation and cannot carry a multi-line PEM block at all.
    /// </para>
    /// <para>
    /// NEITHER SHAPE MAY BE REFUSED, which is why the value names both rather than one: the legacy
    /// generator's PEM output is an OPTIONAL fourth argument
    /// [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>], so legacy private-key material
    /// genuinely exists in both encodings.
    /// </para>
    /// <para>
    /// WHY VALIDATE A SINGLE-VALUED SET AT ALL. A deployment that sets this to something else -
    /// <c>Pkcs12</c> and <c>Jwk</c> being the plausible guesses - is expressing an expectation this
    /// service does not meet. Refusing the value at startup says so; ignoring it would let the
    /// deployment believe its expectation had been honoured and then fail the import for a reason that
    /// looks unrelated.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string SigningKeyFormat { get; set; } = PermittedSigningKeyFormat;

    /// <summary>
    /// The smallest RSA modulus, in bits, this service will accept for its OWN signing identity.
    /// Defaults to 2048.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A FLOOR ON THIS KEY AND ONLY ON THIS KEY. See this file's header: C-02's key-generation surface
    /// keeps the legacy 1024-bit allowance untouched, and this value governs the system's own trust
    /// root - which is net-new and therefore has no legacy behaviour to preserve.
    /// </para>
    /// <para>
    /// CONFIGURABLE UPWARDS, NOT DOWNWARDS BELOW THE FLOOR ITSELF. A deployment may raise it to 3072 or
    /// 4096; a value below <see cref="AbsoluteMinimumSigningKeySizeBits"/> is refused, because a
    /// configuration file that could lower the floor to nothing would make the floor decorative.
    /// </para>
    /// </remarks>
    [Range(AbsoluteMinimumSigningKeySizeBits, MaximumSigningKeySizeBits)]
    public int SigningKeyMinimumSizeBits { get; set; } = DefaultSigningKeySizeBits;

    /// <summary>The only signing-key encoding set this service implements.</summary>
    public const string PermittedSigningKeyFormat = "PemOrPkcs8Base64";

    /// <summary>The default signing-key floor, in bits.</summary>
    public const int DefaultSigningKeySizeBits = 2048;

    /// <summary>
    /// The lowest floor a deployment may configure, in bits.
    /// </summary>
    /// <remarks>
    /// EQUAL TO THE DEFAULT ON PURPOSE, so the floor can be raised but never lowered. A deployment that
    /// needs a shorter key for the trust root needs a different trust root, not a weaker setting.
    /// </remarks>
    public const int AbsoluteMinimumSigningKeySizeBits = 2048;

    /// <summary>
    /// The largest floor a deployment may configure, in bits.
    /// </summary>
    /// <remarks>
    /// A CEILING ON THE SETTING, NOT ON THE KEY. It exists so a typo - an extra digit - is refused at
    /// startup rather than rejecting every key the deployment owns for a reason nobody can see.
    /// </remarks>
    public const int MaximumSigningKeySizeBits = 16384;

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
    /// The issuance roster: WHICH callers may obtain a token, and for which audiences and scopes.
    /// Subject names, audience names, scope names and CONFIGURATION-KEY names only - never a secret.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS EXISTS AT ALL, WHICH IS THE WHOLE OF THE DEFECT IT CLOSES. Without a roster the issuer
    /// can only ask one question - is the requested audience one this deployment serves - and that
    /// question is the same for every caller. Any caller able to authenticate at the issuance edge
    /// could therefore mint a token for ANY configured audience carrying ANY scope set it cared to
    /// name, which makes the audience roster a list of who this system trusts rather than a list of who
    /// each caller may impersonate. Least privilege is not expressible without a per-subject
    /// statement, and this is that statement.
    /// </para>
    /// <para>
    /// THE ROSTER IS ALSO THE CREDENTIAL DIRECTORY, and folding the two together is deliberate rather
    /// than economical. A caller's identity, the secret it authenticates with, the audiences it may
    /// address and the scopes it may request are one decision about one caller; splitting them across
    /// two configuration sections would let a deployment authenticate a caller it has authorised
    /// nothing for, or authorise a caller it cannot authenticate - both of which read as working
    /// configuration and neither of which mints a usable token.
    /// </para>
    /// <para>
    /// AT LEAST ONE ENTRY IS REQUIRED, AND AN EMPTY ROSTER IS A STARTUP FAILURE RATHER THAN A SAFE
    /// DEFAULT. That is the opposite of <see cref="SecurityKeyStoreOptions.PermittedKeyRefs"/>, whose
    /// empty default is safe, and the difference is which way the fault falls: an empty key-store
    /// allow-list refuses every reference, which is a service that works with one capability switched
    /// off, whereas an empty issuance roster refuses every caller, which is a system in which no
    /// service can obtain a credential at all while this service's readiness probe reports healthy
    /// throughout. A deployment that means to mint nothing does not deploy the sole issuer.
    /// </para>
    /// <para>
    /// GET-ONLY, so the collection can be bound but never replaced or nulled. The configuration binder
    /// populates the existing instance, so <c>Security:Clients</c> binds as an array of objects exactly
    /// as it would with a setter.
    /// </para>
    /// <para>
    /// NO SECRET IS DECLARABLE HERE. <see cref="SecurityClientOptions.SecretConfigurationKey"/> names a
    /// flat configuration key; the material it names is injected from the orchestration secret layer and
    /// appears in no source file, no settings file and no container definition. That is the same
    /// discipline the signing key and the key store are held to, and it is what keeps this section
    /// reviewable in a settings file at all.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<SecurityClientOptions> Clients { get; } = [];

    /// <summary>
    /// The mutual-TLS trust configuration for the one edge that can authenticate by client certificate:
    /// which authority's client certificates this service accepts on <c>POST /v1/tokens</c>. Bound from
    /// <c>Security:MutualTls</c>. A path - never material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY IT IS REQUIRED AND WHY ITS ABSENCE WAS A DEFECT. <c>POST /v1/tokens</c> accepts TWO caller
    /// credentials - a shared secret presented as an HTTP <c>Basic</c> credential, or a client
    /// certificate - and no bearer token, because a caller cannot present a bearer token in order to
    /// obtain its first bearer token. This group is the trust half of the certificate alternative. Where
    /// a caller authenticates that way the endpoint reads the presented certificate's common
    /// name and reconciles it against the claimed subject - but a NAME proves nothing on its own. Unless
    /// the certificate's chain is verified against a known authority, any caller can mint a self-signed
    /// certificate whose common name is <c>powerframework-gateway</c> and be issued a Gateway token. The
    /// documented topology issues caller certificates from a LOCAL authority that is in no container's
    /// operating-system trust store, so the platform cannot make that decision either. This group is
    /// the anchor that lets the decision be made at all.
    /// </para>
    /// <para>
    /// Get-only and always present, so the group can never be null and never be replaced wholesale; the
    /// configuration binder populates the existing instance for a complex property with no setter.
    /// </para>
    /// </remarks>
    public SecurityMutualTlsOptions MutualTls { get; } = new();

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

    /// <summary>
    /// The file the CLIENT-certificate trust anchor is read from: the certificate authority whose
    /// signature a caller's certificate must chain to before the token operation will honour the
    /// identity it carries.
    /// </summary>
    /// <value>
    /// A path to a PEM file holding one or more certificate authorities, or an empty string when this
    /// deployment configures no client trust anchor of its own - in which case the composition root
    /// adopts the listener's anchor from <see cref="SecurityMutualTlsOptions.ClientCaPath"/> if that one
    /// is configured, and only when NEITHER is configured does no certificate establish an identity. In
    /// that last state the CERTIFICATE credential is unavailable and the token operation answers
    /// <c>401</c> to a caller presenting one; the HTTP <c>Basic</c> credential from the issuance roster
    /// is a separate scheme on the same operation and keeps minting normally.
    /// </value>
    /// <remarks>
    /// <para>
    /// A PATH, NEVER MATERIAL, AND THAT IS A SECRETS CONTROL (C-F) EVEN THOUGH A PUBLIC CERTIFICATE IS
    /// NOT SECRET. The whole trust configuration of this service arrives the same way - the listener's
    /// server certificate is a path pair, the caller-side identities are path pairs - so a reader looks
    /// for trust material in exactly one kind of place. No settings file in this repository names a path,
    /// because a path is deployment-specific; this one is supplied through
    /// <c>SECURITY_MTLS_CLIENT_CA_PATH</c> from the orchestration layer.
    /// </para>
    /// <para>
    /// 🔴 ONE PUBLISHED VARIABLE, TWO ANCHORS, AND THIS KEY IS THE ONE THAT IS NOT PUBLISHED. There are
    /// two client-certificate anchors in this service and they serve different layers: this key is the
    /// ISSUANCE anchor, read by <c>Tokens/ClientCertificateTrust</c> so that a completed handshake's
    /// certificate may establish an identity, while <see cref="SecurityMutualTlsOptions.ClientCaPath"/>
    /// is the LISTENER's, read by the composition root so Kestrel will complete such a handshake at all.
    /// The orchestration layer publishes one variable for this authority -
    /// <c>SECURITY_MTLS_CLIENT_CA_PATH</c> - and maps it to the listener key alone. The sentence above
    /// therefore used to describe an intent rather than a mechanism, and the gap it left was measurable:
    /// a deployment following the documented bootstrap completed the handshake and was then refused
    /// <c>401</c> on every certificate, because the anchor this type reads was named nowhere an operator
    /// would look. The composition root closes it by ADOPTING the listener's anchor when this key is
    /// unset - the <c>PostConfigure</c> on <c>AddOptions&lt;SecurityOptions&gt;</c> in this service's
    /// <c>Program.cs</c>. The two remain distinct settings on purpose, because a deployment may let its
    /// listener complete handshakes for a broader authority than issuance will honour identities from, so
    /// an explicitly configured value here is always authoritative and is never merged with the
    /// listener's.
    /// </para>
    /// <para>
    /// WHY THE ANCHOR IS NAMED HERE RATHER THAN LEFT TO THE CONTAINER'S OS TRUST STORE, which is what an
    /// earlier plan for this service proposed. Two reasons, and both are decisive. The OS store of a
    /// .NET base image already trusts every public root in it, so leaving the anchor implicit makes the
    /// set of issuers who can mint a caller identity for this system as wide as the public web PKI -
    /// nothing about that set is stated anywhere, reviewable, or under this deployment's control. And an
    /// implicit anchor cannot be verified: there is no in-process observation that distinguishes
    /// "correctly trusts our CA" from "trusts everything", so the property could not be tested at all.
    /// Naming it makes the trust boundary narrow, explicit and assertable, and it is what lets the
    /// published <c>401</c> for an untrusted certificate be a behaviour rather than a promise.
    /// </para>
    /// <para>
    /// UNSET IS A LEGITIMATE STATE AND FAILS CLOSED, WHICH IS THE OPPOSITE OF FAILING OPEN. A deployment
    /// that configures no anchor under EITHER key cannot issue tokens on the CERTIFICATE credential -
    /// every certificate is untrusted, and the operation answers the same <c>401</c> it answers for a
    /// caller that presented none, while the roster's <c>Basic</c> credential keeps minting - and
    /// meanwhile <c>/health</c>,
    /// the published key set, the discovery document and the C-02 operations all stay reachable, so the
    /// readiness chain the other three services wait on is unaffected. A path that is SET and unreadable
    /// is a different matter and refuses the host: the deployment stated an intent it cannot meet, which
    /// is the same structural fault the legacy ends the process for [pfw.sra:L143].
    /// </para>
    /// </remarks>
    public string ClientCertificateAuthorityPath { get; set; } = string.Empty;

    /// <summary>
    /// How thoroughly a caller certificate's revocation status is checked while its chain is built.
    /// </summary>
    /// <value>
    /// One of the names in <see cref="ClientCertificateRevocationModes.Recognised"/>. Defaults to
    /// <see cref="ClientCertificateRevocationModes.NoCheck"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// THE DEFAULT IS THE ONE THAT KEEPS A CORRECT DEPLOYMENT WORKING, AND IT IS A DEFAULT RATHER THAN A
    /// FIXED CHOICE. A locally issued certificate authority - including the one this repository's own
    /// generation recipe produces - publishes no distribution point and no responder, so a chain built
    /// with revocation checking enabled reports the status as UNKNOWN and, because this service refuses
    /// an unknown status rather than ignoring it, would refuse every caller. Defaulting to a check that
    /// cannot succeed would therefore turn a correctly configured deployment into a broken one.
    /// </para>
    /// <para>
    /// A DEPLOYMENT THAT PUBLISHES REVOCATION DATA RAISES IT, and the two stronger modes are honoured
    /// exactly as named: an offline check consults cached lists only, an online check may reach the
    /// issuer's responder. Neither is silently downgraded, and an indeterminate status under either is a
    /// REFUSAL - there is no arm anywhere that treats "could not tell" as "not revoked".
    /// </para>
    /// <para>
    /// A string rather than the platform's own enumeration, for the same reason the signing-key format is
    /// a string: an unrecognised enum name fails inside the configuration binder with the binder's
    /// message, whereas a string plus a validator arm produces a startup failure that names the offending
    /// configuration key and the recognised set.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ClientCertificateRevocationMode { get; set; } =
        ClientCertificateRevocationModes.NoCheck;

    /// <summary>
    /// The caller-to-audience-to-scope authorization matrix: which authenticated caller may obtain a
    /// token for which audience, and which scopes that combination may carry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WITHOUT THIS, AUTHENTICATION WAS THE WHOLE OF AUTHORIZATION. A caller establishing itself at the
    /// transport could request ANY audience on <see cref="Audiences"/> and ANY syntactically valid scope
    /// set, and receive both in full - so a token minted for one service's caller was minted for every
    /// service's caller, and the scope claim was decorative because nothing constrained what a caller
    /// could ask for. The published <c>403</c> on the issuance operation - "the authenticated caller is
    /// not permitted to obtain a token for the requested subject or audience" - had no mechanism behind
    /// its audience half at all. This is that mechanism.
    /// </para>
    /// <para>
    /// AN EMPTY MATRIX AUTHORISES NOTHING, AND THAT IS THE FAIL-CLOSED DIRECTION. A deployment that
    /// declares no authorization has stated no caller it will mint for, and the only answer consistent
    /// with that is to mint for none - the opposite reading, "nothing configured, so allow anything", is
    /// the exact shape of the defect this exists to close. It costs nothing in practice because
    /// <c>appsettings.json</c> SHIPS the matrix: the callers, the audiences and the scopes are this
    /// system's own published topology rather than deployment-specific material, so the default
    /// configuration is simultaneously closed and working. A deployment that blanks it has made a
    /// deliberate, visible choice.
    /// </para>
    /// <para>
    /// ONE ENTRY PER CALLER-AND-AUDIENCE PAIR, which mirrors the contract's own shape: the request
    /// carries ONE audience, deliberately, so that a token is never valid somewhere its holder did not
    /// intend. A caller that legitimately addresses two services has two entries, and the scopes it may
    /// carry to each are stated separately - which is the property a single per-caller scope list could
    /// not express.
    /// </para>
    /// <para>
    /// A SCOPE NARROWING IS A SUCCESS, NOT A REFUSAL, and that is the published contract rather than a
    /// choice made here: the response's granted set "may be narrower than the requested one", and a
    /// caller is told to read it. So a request whose scopes are partly permitted is granted the
    /// permitted part; only a request with NO permitted scope at all is refused, because there would be
    /// nothing to grant and the response's scope member is required.
    /// </para>
    /// <para>
    /// IT CARRIES NO CREDENTIAL AND CANNOT. Every value here is an identity or a protocol token -
    /// nothing secret, nothing derived from key material - so it is legitimately expressible in a
    /// settings file, unlike every path and every key this configuration deliberately refuses to hold.
    /// </para>
    /// <para>
    /// Get-only and empty by default, so the binder populates the existing instance and the collection
    /// can be neither replaced nor nulled - the same shape as <see cref="Audiences"/> and for the same
    /// reason.
    /// </para>
    /// </remarks>
    public IList<CallerAuthorizationOptions> CallerAuthorizations { get; } = [];
}

/// <summary>
/// One row of the authorization matrix: a caller, the single audience it may address, and the scopes it
/// may carry to that audience.
/// </summary>
/// <remarks>
/// A CLASS WITH SETTABLE MEMBERS BECAUSE THE CONFIGURATION BINDER CONSTRUCTS IT. It carries no
/// behaviour, performs no validation of its own, and is validated as part of the options contract by
/// <see cref="SecurityOptionsValidator"/> - so that a malformed row refuses the host with a message
/// naming its index and its offending key, rather than throwing from inside the binder.
/// </remarks>
public sealed class CallerAuthorizationOptions
{
    /// <summary>The authenticated caller identity this row authorises.</summary>
    /// <value>
    /// A non-blank identity, compared ORDINALLY against the request's subject - which the issuance
    /// operation has already reconciled against the identity the client certificate establishes.
    /// </value>
    /// <remarks>
    /// ORDINAL EVERYWHERE, WITH NO TRIMMING AND NO CASE FOLDING, because that is how every other
    /// identity in this service is compared: the audience against its roster, the key identifier against
    /// the published key set, and the subject against the certificate. Folding case here would authorise
    /// a caller whose certificate establishes a different identity.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Caller { get; set; } = string.Empty;

    /// <summary>The single audience this caller may obtain a token for.</summary>
    /// <value>A non-blank audience identity, which must also appear on <see cref="SecurityOptions.Audiences"/>.</value>
    /// <remarks>
    /// REQUIRED TO BE ON THE GLOBAL ROSTER TOO, and the redundancy is deliberate: the roster is what the
    /// issuer will mint for at all, so a row naming an audience outside it authorises something that
    /// could never be granted - a configuration contradiction an operator should be told about at
    /// startup rather than discover as a puzzling refusal.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The scopes this caller may carry to this audience.</summary>
    /// <value>At least one non-blank, white-space-free scope.</value>
    /// <remarks>
    /// AT LEAST ONE, because a row permitting no scope permits nothing and would be indistinguishable
    /// from an absent row while looking like a grant. White space is refused for the same reason the
    /// request validator refuses it: the scope claim is space-delimited, so a scope containing a space
    /// would arrive at a verifier as two scopes.
    /// </remarks>
    public IList<string> Scopes { get; } = [];
}

/// <summary>
/// The recognised values of <see cref="SecurityOptions.ClientCertificateRevocationMode"/>.
/// </summary>
/// <remarks>
/// The three names are the platform's own revocation modes, spelled exactly as the platform spells them
/// so that a reader of the settings file and a reader of the chain policy see the same word. A named
/// holder rather than literals, for the reason recorded on <see cref="SigningKeyFormats"/>.
/// </remarks>
public static class ClientCertificateRevocationModes
{
    /// <summary>Revocation is not consulted at all.</summary>
    public const string NoCheck = "NoCheck";

    /// <summary>Only cached revocation lists are consulted; nothing is fetched.</summary>
    public const string Offline = "Offline";

    /// <summary>The issuer's revocation source may be reached over the network.</summary>
    public const string Online = "Online";

    /// <summary>The recognised names, for a failure message and for a test to enumerate.</summary>
    public static IReadOnlyList<string> Recognised { get; } = [NoCheck, Offline, Online];

    /// <summary>Whether a configured mode name is one the platform implements.</summary>
    /// <param name="mode">The configured value.</param>
    /// <returns><see langword="true"/> when recognised.</returns>
    /// <remarks>
    /// Case-insensitive, matching how the signing-key format is screened and how a settings file is
    /// ordinarily read: a deployment writing <c>nocheck</c> has named a mode this service implements, and
    /// refusing it would be a spelling rule invented here.
    /// </remarks>
    public static bool IsRecognised(string? mode) =>
        mode is not null
        && Recognised.Any(candidate => string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The recognised values of <see cref="SecurityOptions.SigningKeyFormat"/> and the bounds of
/// <see cref="SecurityOptions.SigningKeyMinimumSizeBits"/>.
/// </summary>
/// <remarks>
/// A named holder rather than literals scattered across the options type, its validator and its tests:
/// the recognised set has to be quotable in a failure message and assertable in a test, and three
/// copies of a spelling is how one of them drifts. Not an enum, for the reason recorded on
/// <see cref="SecurityOptions.SigningKeyFormat"/>.
/// </remarks>
public static class SigningKeyFormats
{
    /// <summary>
    /// Armoured text first - both the algorithm-tagged and the RSA-specific forms - then base64 of the
    /// bare binary encoding on a single line.
    /// </summary>
    public const string PemOrPkcs8Base64 = "PemOrPkcs8Base64";

    /// <summary>The default floor on the issuer key's size, in bits.</summary>
    /// <remarks>
    /// 2048 is the smallest RSA size in current general use for a signing identity. It is a DEFAULT and
    /// not a constant in the check, so a deployment may raise it from configuration.
    /// </remarks>
    public const int DefaultMinimumKeySizeBits = 2048;

    /// <summary>The smallest size a configured floor may name.</summary>
    /// <remarks>
    /// The legacy catalogue's own smallest RSA size [<c>enums.sru:L965</c>]. A floor may be lowered to
    /// it, which is what keeps this a configurable policy rather than a hardcoded refusal - but doing so
    /// is a deliberate, visible act in the settings file rather than the silent default.
    /// </remarks>
    public const int SmallestExpressibleKeySizeBits = 1024;

    /// <summary>The largest size a configured floor may name.</summary>
    /// <remarks>
    /// A bound rather than an opinion: without one, a mistyped value refuses every key that could ever
    /// be supplied, and the resulting bring-up failure names the key material rather than the typo.
    /// </remarks>
    public const int LargestSaneKeySizeBits = 16384;

    /// <summary>The recognised format names, for a failure message and for a test to enumerate.</summary>
    public static IReadOnlyList<string> Recognised { get; } = [PemOrPkcs8Base64];

    /// <summary>Whether a configured format name is one this service implements.</summary>
    /// <param name="format">The configured value.</param>
    /// <returns><see langword="true"/> when recognised.</returns>
    /// <remarks>
    /// Compared case-insensitively because a format name is an identifier an operator types rather than
    /// a protocol token, and refusing a bring-up over the case of a word this service chose itself would
    /// be a fault with no security value. The recognised SET is still closed.
    /// </remarks>
    public static bool IsRecognised(string? format) =>
        format is not null
        && Recognised.Any(candidate => string.Equals(candidate, format.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One entry in the issuance roster: a caller identity, the configuration key its shared secret is
/// injected under, and the closed sets of audiences and scopes that caller may ask for.
/// </summary>
/// <remarks>
/// <para>
/// THE SUBJECT IS BOTH THE CREDENTIAL IDENTITY AND THE TOKEN SUBJECT, and collapsing the two is the
/// property that makes the issuance edge's reconciliation meaningful. A caller presents this name as
/// its credential identity, claims this name in the request body, and receives a token whose subject
/// claim is this name; the endpoint compares the claim to the authenticated identity ordinally, so
/// there is no arrangement in which a caller obtains a token for a subject other than its own.
/// </para>
/// <para>
/// A top-level type rather than one nested inside <see cref="SecurityOptions"/>, so it is bindable and
/// assertable on its own, exactly as <see cref="SecurityKeyStoreOptions"/> is. It lives in this file
/// because a roster entry and the contract it belongs to are one decision.
/// </para>
/// <para>
/// IT CARRIES NO SECRET AND HAS NOWHERE TO PUT ONE. There is deliberately no <c>Secret</c> member: a
/// settings file able to hold one would be exactly the hardcoded credential this refactor's mandate
/// eliminates, and the eight in-source secret sites the sweep found are what that mandate exists for.
/// </para>
/// </remarks>
public sealed class SecurityClientOptions
{
    /// <summary>
    /// The caller identity. Both the credential identity presented at the issuance edge and the
    /// subject claim of every token minted for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compared ORDINALLY everywhere it is used - against the presented credential identity, against
    /// the claimed subject in the request body, and against the other roster entries when duplicates
    /// are checked. Folding case would let two entries that differ only in case collide, and a
    /// deployment would then have two rosters' worth of permissions arbitrated by whichever entry the
    /// binder happened to place first.
    /// </para>
    /// <para>
    /// NOT TRIMMED AND NOT REPAIRED. The value reaches the token's subject claim exactly as configured,
    /// so a leading space here would be a leading space in every token minted for this caller and in
    /// every log record naming it.
    /// </para>
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// The name of the flat configuration key this caller's shared secret is injected under - for
    /// example <c>SECURITY_CLIENT_SECRET_GATEWAY</c>. A NAME, NEVER A VALUE.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SAME INDIRECTION THE KEY STORE USES, for the same reason: a settings file names the key and
    /// the orchestration secret layer supplies the material, so no credential is ever committed. The
    /// lookup is <c>configuration[SecretConfigurationKey]</c> and nothing more elaborate.
    /// </para>
    /// <para>
    /// THE DOUBLE-UNDERSCORE TRAP APPLIES. The composed lookup is a FLAT configuration key of exactly
    /// this spelling, not a path inside the <c>Security</c> section, so a value supplied as an
    /// environment variable resolves by its own literal name - which is what makes the name above work
    /// unchanged as a variable name. A name containing a doubled underscore would be folded into a
    /// section separator on the way in by the environment provider and would then simply fail to
    /// resolve, so the validator constrains the charset to letters, digits, <c>.</c>, <c>_</c> and
    /// <c>-</c> and the registry fails startup when a named key resolves to nothing.
    /// </para>
    /// <para>
    /// OPTIONAL, AND THE ABSENCE IS MEANINGFUL RATHER THAN LAX. An entry with no secret key is a caller
    /// that authenticates by CLIENT CERTIFICATE only: the issuance edge accepts two schemes, and a
    /// deployment that terminates TLS and issues client certificates has no shared secret to name. Such
    /// an entry can never be authenticated by the credential scheme, because a roster entry with no
    /// resolved secret is not a candidate for secret comparison at all - it is not that any secret
    /// matches it.
    /// </para>
    /// </remarks>
    public string? SecretConfigurationKey { get; set; }

    /// <summary>
    /// The closed set of audiences this caller may request a token for. Every entry must also appear on
    /// <see cref="SecurityOptions.Audiences"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO GATES RATHER THAN ONE, AND THEY ANSWER DIFFERENT QUESTIONS. The deployment-wide roster says
    /// which audiences this issuer serves at all; this set says which of them THIS caller may address.
    /// A request is refused if it fails either, and the validator additionally refuses a deployment in
    /// which this set names an audience the deployment-wide roster does not - not because such an entry
    /// is dangerous, but because it is unreachable configuration that reads as a granted permission.
    /// </para>
    /// <para>
    /// NON-EMPTY, because a caller permitted no audience can obtain no usable token, and an entry that
    /// can obtain nothing is a roster entry an operator believes is working.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<string> Audiences { get; } = [];

    /// <summary>
    /// The closed set of scopes this caller may request. A request naming anything outside it is
    /// refused rather than silently narrowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REFUSED, NOT NARROWED, AND THAT IS A CONTRACT DECISION RATHER THAN STRICTNESS FOR ITS OWN SAKE.
    /// The published contract permits a granted set to be narrower than a requested one and requires a
    /// caller to read the granted set from the response, so narrowing would be contract-legal. It is
    /// nevertheless the wrong behaviour here: a caller that asked for a scope it may not have has a
    /// misconfiguration, and answering it with a usable token whose scope set silently differs turns
    /// that misconfiguration into a failure at whichever downstream service refuses the call later, far
    /// from its cause. A refusal at the issuance edge names the problem where it can be fixed.
    /// </para>
    /// <para>
    /// EVERY ENTRY IS A SCOPE TOKEN, validated against the RFC 6749 section 3.3 charset. The granted set
    /// travels as ONE space-delimited value in both the response and the token claim, so an entry
    /// containing white space could not be recovered by a reader and would silently become two scopes -
    /// the same constraint the issuance request type enforces on the caller's side, applied here to the
    /// declared side so the two cannot disagree.
    /// </para>
    /// <para>
    /// NON-EMPTY, for the same reason the audience set is: a token that authorises nothing is not a
    /// credential, and a roster entry that can only produce one is a trap.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<string> Scopes { get; } = [];
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
/// Which authority's client certificates this service accepts on the one mutual-TLS edge in the system.
/// Bound from <c>Security:MutualTls</c>. One path, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A CA CERTIFICATE IS PUBLIC MATERIAL, SO THE PATH IS NOT ABOUT CONFIDENTIALITY. It is that the anchor
/// is a DEPLOYMENT artefact - one local authority per environment, rotated on its own schedule, mounted
/// read-only from the orchestration layer as <c>SECURITY_MTLS_CLIENT_CA_PATH</c> names. Embedding one in
/// a settings file would pin every environment to a single authority and make rotation a code change.
/// </para>
/// <para>
/// ONE MEMBER, AND NO KEY PATH, WHICH IS THE PROPERTY THAT MATTERS MOST HERE. Verifying a chain needs
/// only the public root. A trust anchor with its private key beside it would mean this service could
/// ISSUE the very client certificates it authenticates callers by, so a compromise of this service would
/// become a compromise of every caller identity rather than of the signing key alone.
/// </para>
/// <para>
/// WHAT AN UNSET PATH MEANS, STATED PLAINLY BECAUSE IT IS A SECURITY-RELEVANT DEFAULT. Unset means the
/// PLATFORM decides whether a presented client certificate chains to something trustworthy, which is
/// correct for a deployment whose caller certificates come from an authority already in the container's
/// trust store, and correct for a test host that supplies a certificate directly. It is NOT a bypass:
/// with no anchor configured the endpoint still refuses a certificate the platform rejected, because
/// the platform's own validation runs first and an untrusted certificate never reaches the handler.
/// </para>
/// </remarks>
public sealed class SecurityMutualTlsOptions
{
    /// <summary>
    /// Path to the PEM-encoded certificate authority bundle caller certificates are verified against.
    /// Empty means platform default trust.
    /// </summary>
    /// <remarks>
    /// The file may carry one certificate or a concatenated chain of them; every certificate it carries
    /// becomes an acceptable root for a CALLER identity, and nothing else does.
    /// </remarks>
    public string ClientCaPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether caller-certificate trust is pinned to a mounted anchor rather than left to the platform.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientCaPath);
}

/// <summary>
/// One entry in the issuance permission roster: a caller identity, the audiences it may address, and the
/// scopes it may hold. Bound from an element of <c>Security:Callers</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE IDENTITY IS THE CERTIFICATE'S, NOT THE REQUEST'S. <c>POST /v1/tokens</c> derives the caller identity
/// from the common name of the client certificate presented during the handshake and refuses a request whose
/// declared subject differs from it, so by the time this roster is consulted the identity has been
/// established by a chain built to the configured client authority rather than asserted in a body. That is
/// the property that makes an allow-list keyed on a name meaningful: without the chain check the name would
/// be self-asserted and this roster would be decoration.
/// </para>
/// <para>
/// AUDIENCES ARE REFUSED; SCOPES ARE INTERSECTED. A token carries exactly one audience by contract, so a
/// request naming an audience this caller may not address has nothing to narrow and is answered with the
/// published forbidden response. A scope set, by contrast, is granted as the overlap with
/// <see cref="Scopes"/> - contract C-01 states that the granted set may be narrower than the requested one,
/// that a narrowing is a success rather than an error, and that an empty granted set is a legal response
/// meaning nothing requested was granted.
/// </para>
/// <para>
/// NAMES ONLY. Every member is an identity or a scope string. There is no member that could hold a
/// certificate, a key, a thumbprint or a secret, and there is deliberately nowhere to put one: the chain
/// establishes WHO a caller is, and this type only records what that identity permits.
/// </para>
/// </remarks>
public sealed class SecurityCallerOptions
{
    /// <summary>
    /// The caller identity this entry governs - the common name of the client certificate it presents.
    /// </summary>
    /// <remarks>
    /// Compared ORDINALLY against the certificate's common name, because an identity is compared exactly
    /// everywhere else in this service: the issuer compares the audience ordinally against its roster and
    /// trims nothing, and the issuance endpoint reconciles the declared subject against the certificate the
    /// same way. Folding case here would honour an identity the certificate does not establish.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the caller identity this entry governs - the common name of the client certificate "
            + "that caller presents. An entry with no identity governs nothing and would silently permit "
            + "nothing, so the host refuses to start instead.")]
    public string Identity { get; set; } = string.Empty;

    /// <summary>
    /// What this caller may request, one entry per audience it may address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A GRANT PER AUDIENCE RATHER THAN ONE SCOPE LIST FOR THE CALLER, which is what makes this roster a
    /// matrix rather than two independent lists. DataServices addresses two audiences with entirely
    /// different scope sets - Persistence for its read and write scopes, Security for its cryptographic
    /// scope - and a flat per-caller scope list would permit it to hold Security's scope in a
    /// Persistence-audience token. That particular over-grant happens to be harmless because each receiver
    /// checks the audience as well as the scope, but the shape would licence a genuinely harmful one the
    /// moment two audiences shared a scope name, and a permission model should not depend on a coincidence
    /// of naming.
    /// </para>
    /// <para>
    /// Empty fails validation: a caller permitted no audience can obtain no usable token, and an entry
    /// saying so is a mistake rather than a policy - a deployment that wants a caller to hold nothing
    /// removes the caller.
    /// </para>
    /// </remarks>
    [MinLength(1)]
    public IList<SecurityCallerGrantOptions> Grants { get; } = [];
}

/// <summary>
/// One cell of the issuance permission matrix: an audience a caller may address, and the scopes it may
/// hold in a token for that audience. Bound from an element of <c>Security:Callers:[n]:Grants</c>.
/// </summary>
/// <remarks>
/// <para>
/// THE AUDIENCE IS REFUSED AND THE SCOPES ARE INTERSECTED, and the asymmetry is the contract's. A token
/// carries exactly one audience by design so that it is never valid somewhere its holder did not intend -
/// there is nothing to narrow, so a request naming an audience this caller has no grant for is answered
/// with the published forbidden response. A scope set is different: contract C-01 states the granted set
/// may be narrower than the requested one, that a narrowing is a successful outcome rather than an error,
/// and that an empty granted set is a legal response meaning nothing requested was granted.
/// </para>
/// <para>
/// NAMES ONLY - an audience identity and scope strings. There is nowhere here for a certificate, a key, a
/// thumbprint or a secret, by design.
/// </para>
/// </remarks>
public sealed class SecurityCallerGrantOptions
{
    /// <summary>
    /// The audience this grant covers. Must also be a member of <see cref="SecurityOptions.Audiences"/>.
    /// </summary>
    /// <remarks>
    /// The subset rule is enforced rather than documented: an audience this issuer cannot mint for at all
    /// is dead configuration, so the grant would appear to permit something while permitting nothing, and
    /// the resulting refusal reads like a permission decision when it is a roster typo.
    /// </remarks>
    [Required(
        AllowEmptyStrings = false,
        ErrorMessage =
            "must name the audience this grant covers. A grant with no audience covers nothing while "
            + "appearing to grant something, so the host refuses to start instead.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The scopes this caller may hold in a token for <see cref="Audience"/>. A request is granted the
    /// overlap between what it asked for and this set.
    /// </summary>
    /// <remarks>
    /// Empty fails validation: a grant permitting no scope produces a token that authorises nothing, which
    /// the receiver then refuses - a healthy-looking issuer minting useless credentials. A deployment that
    /// wants a caller to hold nothing for an audience removes the grant.
    /// </remarks>
    [MinLength(1)]
    public IList<string> Scopes { get; } = [];
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
    /// The longest scope token an issuance-roster entry may grant.
    /// </summary>
    /// <remarks>
    /// A BOUND ON DECLARED CONFIGURATION, NOT ON A CALLER'S REQUEST, and the distinction matters. A
    /// caller's requested scope set is bounded by the request schema and by what this roster grants;
    /// this value bounds what a deployment may WRITE DOWN, so that a scope name cannot become a
    /// pathological string that ends up in a token claim, in a log record and in a characterization
    /// recording. The same value as the key-reference bound, because both are identifiers a human types
    /// into a settings file and neither has any reason to be longer.
    /// </remarks>
    public const int MaximumScopeLength = 64;

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
    /// The fixed rejection for signing material whose RSA modulus is shorter than the configured
    /// floor. Two placeholders: the measured size, then the configured floor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A SIZE IS NOT A SECRET, so unlike the unusable-material message this one DOES state what was
    /// measured. The modulus length of a key is published in the key set this service serves anonymously
    /// [<c>GET /.well-known/jwks.json</c>], so an operator learns nothing from this message they could
    /// not read off the wire - and without the measured value the operator cannot tell a 1024-bit key
    /// from a 2047-bit one, which is the difference between a wrong key and a wrong generation command.
    /// </para>
    /// <para>
    /// IT NAMES THE SURFACE THE FLOOR APPLIES TO, because the neighbouring C-02 surface deliberately
    /// still accepts 1024 bits and an operator who has just read that documentation would otherwise
    /// reasonably conclude this refusal is a defect.
    /// </para>
    /// </remarks>
    public const string SigningKeyTooShortMessageFormat =
        "Configuration key '" + SecurityOptions.SigningKeyEnvironmentVariableName + "' imported " +
        "successfully but its RSA modulus is {0} bits, below the configured floor of {1} bits in '" +
        SecurityOptions.SectionName + ":" + nameof(SecurityOptions.SigningKeyMinimumSizeBits) +
        "'. This floor governs THIS SERVICE'S OWN SIGNING IDENTITY only: the key-generation surface " +
        "published at /v1/crypto deliberately still accepts 1024 bits, preserving the legacy " +
        "allowance [ws_objects/pfw.shared.pbl.src/enums.sru:L965], and nothing about that surface " +
        "changes. Generate a longer key for the issuer, or raise nothing and lower nothing here.";

    /// <summary>
    /// The fixed rejection for a signing-key format outside the one set this service implements. One
    /// placeholder: the permitted value.
    /// </summary>
    /// <remarks>
    /// The supplied value is NOT echoed, matching every other rejection in this file: naming what is
    /// accepted is what an operator needs, and repeating the rejected value would put
    /// deployment-supplied text into a startup log for no gain.
    /// </remarks>
     // THE SIGNING-KEY FORMAT IS SCREENED IN EXACTLY ONE PLACE, AND IT IS NOT HERE.
    //
    // Two independent screens existed: a standalone one comparing the configured value against the single
    // permitted constant, and the arm of CheckSigningMaterial that tests it against SigningKeyFormats -
    // the closed set, whose member order also states the ACCEPTANCE ORDER the provider actually applies
    // (armoured text first, then single-line base64 of the bare binary encoding). Both produced a failure
    // naming this key, so a deployment that misspelled the format was told the same thing twice and any
    // assertion expecting one diagnostic per defect failed on the pair.
    //
    // CheckSigningMaterial's arm is the one retained, because the screen and the SIZE enforcement it sits
    // beside are one decision about one value: the size can only be measured on material that imported,
    // and the format is what decides whether it can. Splitting them let the two disagree about whether an
    // unusable value should also be reported as too short.
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

    /// <summary>
    /// <see cref="MaximumScopeLength"/> rendered once, culture-invariantly, for the one failure message
    /// that reports it.
    /// </summary>
    /// <remarks>
    /// Composed from the constant rather than written out, so a message can never describe a bound the
    /// check does not enforce, and culture-invariantly so the digits are ASCII under every host culture.
    /// </remarks>
    private static readonly string MaximumScopeLengthText =
        MaximumScopeLength.ToString(CultureInfo.InvariantCulture);

    private const string IssuerKey = SecurityOptions.SectionName + ":Issuer";
    private const string AudiencesKey = SecurityOptions.SectionName + ":Audiences";

    /// <summary>The configuration path of the issuance permission roster.</summary>
    private const string CallersKey = SecurityOptions.SectionName + ":Callers";
    private const string ClientsKey = SecurityOptions.SectionName + ":Clients";
    private const string TokenLifetimeKey = SecurityOptions.SectionName + ":TokenLifetime";
    private const string SigningKeyIdKey = SecurityOptions.SectionName + ":SigningKeyId";
    private const string SigningAlgorithmKey = SecurityOptions.SectionName + ":SigningAlgorithm";
    private const string JwksPathKey = SecurityOptions.SectionName + ":JwksPath";

    private const string OpenIdConfigurationPathKey =
        SecurityOptions.SectionName + ":OpenIdConfigurationPath";

    private const string TokenEndpointPathKey = SecurityOptions.SectionName + ":TokenEndpointPath";

    private const string SigningKeyFormatKey = SecurityOptions.SectionName + ":SigningKeyFormat";

    private const string SigningKeyMinimumSizeBitsKey =
        SecurityOptions.SectionName + ":SigningKeyMinimumSizeBits";
    private const string CallerAuthorizationsKey =
        SecurityOptions.SectionName + ":CallerAuthorizations";

    private const string ClientCertificateAuthorityPathKey =
        SecurityOptions.SectionName + ":ClientCertificateAuthorityPath";

    private const string ClientCertificateRevocationModeKey =
        SecurityOptions.SectionName + ":ClientCertificateRevocationMode";
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
        CheckCallerRoster(options, failures);
        CheckSigningKeyIdentifier(options, failures);
        CheckTokenLifetimeIsPositive(options, failures);
        CheckSigningAlgorithmIsPermitted(options, failures);
        CheckMetadataPaths(options, failures);
        CheckTokenEndpointPath(options, failures);
        CheckKeyStore(options, failures);
        CheckClientCertificateTrust(options, failures);
        CheckCallerAuthorizations(options, failures);
        CheckClientRoster(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Rejections 1 and 2 plus the modulus floor: the signing material must be present, it must be
    /// usable as an asymmetric private key, and its modulus must reach the configured floor.
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
    /// A KEY-SIZE FLOOR *IS* APPLIED, AND AN EARLIER REVISION OF THIS PARAGRAPH ARGUED THAT IT MUST NOT
    /// BE. That argument ran: the legacy constant catalogue keeps 1024-bit RSA as a first-class legal
    /// size [ws_objects/pfw.shared.pbl.src/enums.sru:L965], therefore refusing a short key here would
    /// be a silent legacy correction. The premise is true and the conclusion is wrong, because the two
    /// clauses are about DIFFERENT KEYS. The 1024 allowance belongs to C-02's key-GENERATION surface,
    /// where a caller names the size and parity is the obligation - <c>RsaProvider.GenRSAKey</c>
    /// enforces no minimum and is untouched by this check. The key measured here is this service's own
    /// signing identity, which is NET-NEW: the legacy has no token issuer at all, so there is no legacy
    /// behaviour a floor on it could correct. See this file's header for the full argument, and
    /// <c>SigningKeyPolicyTests</c> for the test that pins the two surfaces apart.
    /// </para>
    /// <para>
    /// THE FLOOR IS MEASURED HERE *AND* ENFORCED AGAIN IN <c>SigningKeyProvider</c>, deliberately. This
    /// check is what produces a readable startup failure naming the setting; the provider's is what
    /// makes the guarantee structural, because it stands between the import and the point at which
    /// <c>SigningCredentials</c> become reachable. Neither is redundant: a validator alone could be
    /// bypassed by any future construction path that does not run options validation, and the provider
    /// alone would report the fault as an exception rather than as a named configuration failure.
    /// </para>
    /// <para>
    /// THE SIZE IS ONLY MEASURED ON MATERIAL THAT IMPORTED. An unusable value reports that one fault and
    /// returns, so a deployment that supplied random bytes is never additionally told those bytes are
    /// too short - which would be true, useless, and a second message for a single defect.
    /// </para>
    /// </remarks>
    private static void CheckSigningMaterial(SecurityOptions options, List<string> failures)
    {
        if (!SigningKeyFormats.IsRecognised(options.SigningKeyFormat))
        {
            failures.Add(
                $"Configuration key '{SigningKeyFormatKey}' names a format this service does not " +
                "implement. The recognised values are: " +
                string.Join(", ", SigningKeyFormats.Recognised) +
                ". That name states the ACCEPTANCE ORDER and the closed set of shapes the signing " +
                "material may arrive in - armoured text first, then base64 of the bare binary " +
                "encoding on a single line - and a deployment naming anything else has stated an " +
                "expectation this service cannot meet. This message never echoes the configured " +
                "signing material.");
        }

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

        if (!CanImportPrivateKey(options.SigningKey, out int keySizeBits))
        {
            failures.Add(SigningKeyUnusableMessage);

            // The measured size of material that did not import is meaningless, so the floor is not
            // consulted. See the third remark above.
            return;
        }

        if (keySizeBits < options.SigningKeyMinimumSizeBits)
        {
            failures.Add(string.Format(
                CultureInfo.InvariantCulture,
                SigningKeyTooShortMessageFormat,
                keySizeBits,
                options.SigningKeyMinimumSizeBits));
        }
    }

    /// <summary>
    /// The signing-key encoding must name the one set this service implements.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// Presence is left to the declared annotation, which already refuses a blank value, so this check
    /// speaks only to a value that is present and unrecognised. The comparison is ordinal and
    /// case-INSENSITIVE: unlike a JOSE algorithm identifier, this value is a name this project coined
    /// for its own settings file, so there is no external specification making its casing significant
    /// and refusing a deployment over one is pedantry that costs a bring-up.
    /// </para>
    /// <para>
    /// WHY A SINGLE-VALUED SET IS WORTH VALIDATING. The alternative is to ignore the leaf, which is
    /// precisely the defect this check was added to remove: a declared, documented setting that the
    /// binder read and nothing consulted. A deployment naming an encoding this service does not
    /// implement holds an expectation that will not be met, and the cheapest honest answer is to say so
    /// at startup rather than to fail the import later for a reason that looks unrelated to the setting
    /// the operator actually changed.
    /// </para>
    /// </remarks>
     /// <summary>
    /// Rejection 3: the issuer identity must be present, and it must be an absolute http or https
    /// address shaped like an identity rather than like a request.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// Blank includes whitespace-only, because a whitespace issuer would be stamped into the
    /// <c>iss</c> claim and published in the discovery document as though it were an identity. Three
    /// other services validate that claim, so an empty issuer does not fail locally - it fails
    /// everywhere, later, as an authentication error with no obvious cause.
    /// </para>
    /// <para>
    /// <b>AND THE SHAPE IS CHECKED HERE RATHER THAN AT REQUEST TIME, WHICH IS THE CORRECTION.</b> An
    /// earlier revision checked only for blankness on the reasoning that the format is a deployment
    /// decision. The consequence was that <c>not-a-uri</c>, <c>javascript:alert(1)</c>,
    /// <c>file:///etc/passwd</c>, <c>ftp://h/p</c>, a whitespace-padded value and a value carrying a
    /// query and a fragment ALL STARTED THE HOST - and readiness opened on a service whose OIDC
    /// discovery document answered 500 while its key set still answered 200. Contract C-01's whole
    /// mechanism is that a consumer's stock bearer handler self-configures from that document with zero
    /// bespoke code, so the one artifact every verifier must fetch was the one that broke, and a
    /// merely MISTYPED but absolute issuer was worse still: the document published cleanly and made
    /// every issued token unverifiable with no signal anywhere.
    /// </para>
    /// <para>
    /// THE RULE IS THE SIBLING SERVICES' RULE, NOT A NEW ONE. Gateway refuses exactly these shapes on
    /// every address it binds
    /// [<c>services/gateway-service/PowerFramework.Gateway/Configuration/GatewayOptions.cs</c>,
    /// <c>AddressValidation.Check</c>], and this service was the outlier. Each of the four component
    /// rules earns its place:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   ABSOLUTE, because a relative value cannot be an identity three other services compare a claim
    ///   against, and because the discovery and key-set addresses are COMPOSED from it - a relative
    ///   issuer composes to nothing a consumer can fetch.
    ///   </description></item>
    ///   <item><description>
    ///   HTTP OR HTTPS, because the addresses composed from it are fetched over HTTP by a bearer
    ///   handler. A <c>javascript:</c>, <c>file:</c> or <c>ftp:</c> issuer parses as absolute and
    ///   composes into a <c>jwks_uri</c> no consumer can retrieve.
    ///   </description></item>
    ///   <item><description>
    ///   NO USERINFO, which is a secrets control rather than tidiness: an issuer carrying
    ///   <c>user:secret@</c> is stamped into every token's <c>iss</c> claim and published anonymously
    ///   in the discovery document, so the credential would leave the process in both directions (C-F).
    ///   </description></item>
    ///   <item><description>
    ///   NO QUERY AND NO FRAGMENT, because both are dropped rather than merged when a well-known path
    ///   is composed onto the issuer, so a deployment that configured either would be wrong with no
    ///   diagnostic - and a fragment is never transmitted at all.
    ///   </description></item>
    /// </list>
    /// <para>
    /// A TRAILING OR LEADING SPACE IS REFUSED RATHER THAN TRIMMED. The <c>iss</c> claim is compared
    /// BYTE-IDENTICALLY by three verifiers, so silently trimming here would make this service mint
    /// tokens carrying a value the operator did not configure - and the operator would have no way to
    /// see which of the two spellings was in force.
    /// </para>
    /// <para>
    /// <see cref="Endpoints.JwksEndpoints"/> keeps its own request-time absoluteness guard, and that is
    /// not redundancy to be removed: it is the guard for a value reaching that endpoint by some path
    /// this validator did not see, and it is the only place that can answer the contract's error shape
    /// to an anonymous caller. What changes is that it is now unreachable through configuration.
    /// </para>
    /// <para>
    /// NO MESSAGE ECHOES THE VALUE, for the same reason the userinfo rule exists - a rejected issuer is
    /// exactly the shape that may carry a credential. The scheme is the one exception, and it is a
    /// fixed token from a small set that carries nothing.
    /// </para>
    /// </remarks>
    private static void CheckIssuer(SecurityOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' is required and must not be blank. It is the " +
                "'iss' claim of every minted token, the 'issuer' member of the discovery document, " +
                "and the value the other services validate every token against.");

            return;
        }

        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out Uri? issuer)
            || !string.Equals(options.Issuer, options.Issuer.Trim(), StringComparison.Ordinal))
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' must be an absolute address with no surrounding " +
                "whitespace, for example 'https://security-service:5104'. It is composed with the " +
                "well-known paths to produce the 'jwks_uri' and 'token_endpoint' members of the " +
                "discovery document, so a relative or padded value produces a document that describes " +
                "nothing a consumer can fetch. The configured value is deliberately not quoted here, " +
                "because a rejected address may carry a credential.");

            return;
        }

        // Uri.Scheme is lower-cased by the parser, so an ordinal comparison is both correct and free of
        // any culture dependency.
        if (!string.Equals(issuer.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' must use the http or https scheme; " +
                $"'{issuer.Scheme}' cannot be fetched by a consumer's bearer handler, so the " +
                "discovery document composed from it would describe an unreachable key set.");

            return;
        }

        if (issuer.UserInfo.Length > 0)
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' must not embed credentials in the address. Remove " +
                "the 'user:password@' portion: this value is stamped into the 'iss' claim of every " +
                "minted token and published in the anonymous discovery document, so a credential here " +
                "would leave the process in both directions. The configured value is deliberately not " +
                "quoted here.");

            return;
        }

        if (!string.IsNullOrEmpty(issuer.Query) || !string.IsNullOrEmpty(issuer.Fragment))
        {
            failures.Add(
                $"Configuration key '{IssuerKey}' is an identity composed with the well-known paths " +
                "and must carry neither a query string nor a fragment. Both are dropped rather than " +
                "merged when a path is composed onto it, so a deployment that configured either would " +
                "have no effect and no diagnostic, and a fragment is never transmitted at all. The " +
                "configured value is deliberately not quoted here.");
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
    /// The permission roster rules: at least one caller, and for each one a non-blank identity that
    /// appears once, at least one permitted audience drawn from the global roster, and at least one
    /// permitted scope.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// THIS ROSTER IS WHAT TURNS "AUTHENTICATED" INTO "AUTHORISED", so its coherence is a security
    /// property rather than a tidiness one. Without it any caller whose certificate chained to the
    /// configured client authority could request a token for ANY service carrying ANY scope set, and every
    /// requested scope was granted verbatim - a confused deputy in the middle of the token topology
    /// (CWE-862, CWE-863). Each rule below closes a way that intent could be silently lost.
    /// </para>
    /// <para>
    /// THE SUBSET RULE IS THE ONE WORTH DWELLING ON. A caller entry naming an audience that is not in
    /// <see cref="SecurityOptions.Audiences"/> is dead configuration: the issuer refuses that audience for
    /// every caller, so the entry grants nothing while appearing to grant something. Left unchecked it
    /// produces a forbidden response that reads like a permission decision when it is a roster typo, and
    /// the operator's instinct would be to widen the caller entry that is already correct. It is compared
    /// ORDINALLY against the global roster because the issuer's own membership check is ordinal, so a
    /// case-insensitive check here would pass an entry the issuer will later refuse.
    /// </para>
    /// <para>
    /// DUPLICATE IDENTITIES ARE COMPARED CASE-INSENSITIVELY, unlike the audience subset check, and the
    /// asymmetry is deliberate. Two entries for one caller differing only in case are unambiguously a
    /// mistake - one of them is unreachable, and which one wins is an implementation detail nobody should
    /// have to know. Within one entry, duplicate audiences and duplicate scopes are likewise refused: a
    /// repeated permission is either a paste error or a half-finished edit, and a roster is exactly the
    /// artifact where such a thing hides.
    /// </para>
    /// <para>
    /// NO MESSAGE ECHOES A VALUE - only the offending key path and index. A permission roster is pasted
    /// between deployments, and a startup log should not become a second copy of one.
    /// </para>
    /// </remarks>
    private static void CheckCallerRoster(SecurityOptions options, List<string> failures)
    {
        // ------------------------------------------------------------------------------------------
        // NO EMPTINESS RULE IS APPLIED TO THE MATRIX, AND THE ABSENCE IS DELIBERATE.
        //
        // An earlier revision refused a host whose matrix - the union of this member and
        // `Security:CallerAuthorizations` - was empty, reasoning that an issuer deciding nothing cannot
        // decide correctly. It refuses a state that is coherent and occasionally wanted: a host serving
        // the published key set, the discovery document, health and the whole of contract C-02 while
        // issuing no token at all. Every unit-test host is in that state, and so is a local bring-up of
        // the other three services against an issuer that mints nothing.
        //
        // FAIL-CLOSED IS PRESERVED WITHOUT IT. An empty matrix grants nobody anything: TokenIssuer folds
        // both shapes into one dictionary and answers every request CallerNotPermitted when that
        // dictionary is empty. Accepting the configuration is not the same as granting anything, and the
        // two halves are asserted as a pair so that neither reads as leniency on its own.
        //
        // THE ROSTER THAT MAY NOT BE EMPTY IS A DIFFERENT MEMBER, and conflating the two is what produced
        // the rule this replaces. `Security:Clients` is the credential directory rather than a permission
        // statement: empty, no caller can authenticate at all, so this - the SOLE token issuer - can give
        // no service a credential while its readiness probe reports healthy for as long as nobody tries.
        // That emptiness IS refused, in CheckIssuanceRoster, and its diagnostic names that key.
        // ------------------------------------------------------------------------------------------

        // The per-entry checks below apply to the nested shape only; the flat shape has its own pass in
        // CheckCallerAuthorizations. Returning early when nothing is nested keeps the two independent, so
        // a deployment using only the flat shape is not walked here at all.
        if (options.Callers.Count == 0)
        {
            return;
        }

        // Ordinal, because the issuer's own audience membership check is ordinal: a case-insensitive
        // subset check here would accept an entry the issuer will refuse at run time.
        HashSet<string> roster = new(StringComparer.Ordinal);

        foreach (string audience in options.Audiences)
        {
            if (!string.IsNullOrWhiteSpace(audience))
            {
                _ = roster.Add(audience.Trim());
            }
        }

        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < options.Callers.Count; index++)
        {
            SecurityCallerOptions caller = options.Callers[index];
            string entryKey = IndexedKey(CallersKey, index);

            if (caller is null)
            {
                failures.Add(
                    $"Configuration key '{entryKey}' is bound to an explicit null. Remove the entry " +
                    "rather than declaring it empty; a null entry grants nothing and reads as though it " +
                    "grants something.");

                continue;
            }

            if (string.IsNullOrWhiteSpace(caller.Identity))
            {
                failures.Add(
                    $"Configuration key '{entryKey}:{nameof(SecurityCallerOptions.Identity)}' is blank. " +
                    "It must name the caller identity this entry governs - the common name of the client " +
                    "certificate that caller presents.");
            }
            else if (!identities.Add(caller.Identity.Trim()))
            {
                failures.Add(
                    $"Configuration key '{entryKey}:{nameof(SecurityCallerOptions.Identity)}' repeats an " +
                    "identity already listed earlier, ignoring case. One of the two entries is " +
                    "unreachable and which one wins is not something a deployment should have to know, " +
                    "so each caller identity must appear exactly once.");
            }

            CheckCallerGrants(caller, entryKey, roster, failures);
        }
    }

    /// <summary>
    /// Checks one caller entry's grants: at least one, each naming a distinct audience that is a member of
    /// the global roster, and each carrying at least one non-blank, non-repeated scope.
    /// </summary>
    /// <param name="caller">The entry being checked.</param>
    /// <param name="entryKey">The entry's configuration path, for the messages.</param>
    /// <param name="roster">The global audience roster, already trimmed.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// AUDIENCES ARE COMPARED ORDINALLY AGAINST THE GLOBAL ROSTER, matching the issuer's own membership
    /// check, so this validator cannot accept a grant the issuer will later refuse. DUPLICATES within one
    /// caller - two grants for the same audience, or one scope twice in a grant - are reported
    /// case-insensitively instead: two spellings of one identity in one place is unambiguously a mistake
    /// however they differ, and which of two grants took effect should never be an implementation detail.
    /// </remarks>
    private static void CheckCallerGrants(
        SecurityCallerOptions caller,
        string entryKey,
        HashSet<string> roster,
        List<string> failures)
    {
        string grantsKey = $"{entryKey}:{nameof(SecurityCallerOptions.Grants)}";

        if (caller.Grants.Count == 0)
        {
            failures.Add(
                $"Configuration key '{grantsKey}' must carry at least one grant. A caller permitted no " +
                "audience can obtain no usable token, so an entry saying so is a mistake rather than a " +
                "policy - remove the caller instead.");

            return;
        }

        HashSet<string> audiencesSeen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < caller.Grants.Count; index++)
        {
            SecurityCallerGrantOptions grant = caller.Grants[index];
            string grantKey = IndexedKey(grantsKey, index);

            if (grant is null)
            {
                failures.Add(
                    $"Configuration key '{grantKey}' is bound to an explicit null. Remove the grant " +
                    "rather than declaring it empty; a null grant permits nothing and reads as though it " +
                    "permits something.");

                continue;
            }

            CheckGrantAudience(grant, grantKey, roster, audiencesSeen, failures);
            CheckGrantScopes(grant, grantKey, failures);
        }
    }

    /// <summary>
    /// Checks one grant's audience: present, not already granted to this caller, and on the global roster.
    /// </summary>
    /// <param name="grant">The grant being checked.</param>
    /// <param name="grantKey">The grant's configuration path, for the messages.</param>
    /// <param name="roster">The global audience roster, already trimmed.</param>
    /// <param name="audiencesSeen">The audiences already granted to this caller.</param>
    /// <param name="failures">The accumulating failure list.</param>
    private static void CheckGrantAudience(
        SecurityCallerGrantOptions grant,
        string grantKey,
        HashSet<string> roster,
        HashSet<string> audiencesSeen,
        List<string> failures)
    {
        string key = $"{grantKey}:{nameof(SecurityCallerGrantOptions.Audience)}";

        if (string.IsNullOrWhiteSpace(grant.Audience))
        {
            failures.Add(
                $"Configuration key '{key}' is blank. Every grant must name the audience it covers, " +
                "because a grant with no audience covers nothing while appearing to grant something.");

            return;
        }

        string trimmed = grant.Audience.Trim();

        if (!audiencesSeen.Add(trimmed))
        {
            failures.Add(
                $"Configuration key '{key}' repeats an audience this caller already has a grant for, " +
                "ignoring case. One of the two grants would be unreachable and which one took effect " +
                "would be an implementation detail governing a permission decision, so the roster is " +
                "refused rather than merged. Combine the two scope lists into one grant.");

            return;
        }

        if (!roster.Contains(trimmed))
        {
            failures.Add(
                $"Configuration key '{key}' names an audience that is not a member of '{AudiencesKey}'. " +
                "This issuer refuses that audience for every caller, so the grant permits nothing while " +
                "appearing to permit something - and the resulting refusal reads like a permission " +
                "decision when it is a roster typo. Add the identity to the global roster, or correct " +
                "this grant. Neither value is echoed here.");
        }
    }

    /// <summary>
    /// Checks one grant's scopes: at least one, none blank, and none repeated.
    /// </summary>
    /// <param name="grant">The grant being checked.</param>
    /// <param name="grantKey">The grant's configuration path, for the messages.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// A scope is matched ORDINALLY at issuance because RFC 6749 scope tokens are case-sensitive, but
    /// DUPLICATES are reported case-insensitively: two spellings of one scope in one grant is a mistake
    /// however they differ, and missing it leaves a roster nobody can read confidently.
    /// </remarks>
    private static void CheckGrantScopes(
        SecurityCallerGrantOptions grant,
        string grantKey,
        List<string> failures)
    {
        string scopesKey = $"{grantKey}:{nameof(SecurityCallerGrantOptions.Scopes)}";

        if (grant.Scopes.Count == 0)
        {
            failures.Add(
                $"Configuration key '{scopesKey}' must list at least one scope. A grant permitting no " +
                "scope produces a token that authorises nothing, which the receiver then refuses - a " +
                "healthy-looking issuer minting useless credentials. Remove the grant instead.");

            return;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < grant.Scopes.Count; index++)
        {
            string scope = grant.Scopes[index];
            string key = IndexedKey(scopesKey, index);

            if (string.IsNullOrWhiteSpace(scope))
            {
                failures.Add(
                    $"Configuration key '{key}' is blank. Every permitted scope must be a non-blank " +
                    "scope token.");

                continue;
            }

            if (!seen.Add(scope.Trim()))
            {
                failures.Add(
                    $"Configuration key '{key}' repeats a scope already permitted by this grant, " +
                    "ignoring case. A repeated permission is a paste error rather than a stronger grant.");
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
    /// Rejection: every row of the authorization matrix must name a caller, an audience the roster
    /// carries, and at least one usable scope - and no caller-and-audience pair may appear twice.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// AN EMPTY MATRIX IS NOT A FAILURE HERE, DELIBERATELY, even though it authorises nothing. It is the
    /// fail-closed posture recorded on <see cref="SecurityOptions.CallerAuthorizations"/>, and it is the
    /// state every host that does not issue tokens legitimately runs in - refusing to start for it would
    /// make a bring-up of the rest of the stack impossible for a service that, with no matrix, could not
    /// have minted anything anyway.
    /// </para>
    /// <para>
    /// A ROW NAMING AN AUDIENCE OUTSIDE THE ROSTER IS A FAILURE, AND THAT IS THE ONE RULE HERE THAT IS
    /// ABOUT COHERENCE RATHER THAN SHAPE. The roster is what the issuer will mint for at all, so such a
    /// row authorises something that can never be granted: the request would be refused by the roster
    /// check before this matrix was ever consulted. An operator reading only the matrix would see a
    /// permission that does not work, with nothing to explain why - so it is reported at startup instead.
    /// </para>
    /// <para>
    /// THE DUPLICATE RULE IS ABOUT AMBIGUITY, NOT TIDINESS. Two rows for one caller-and-audience pair
    /// would leave the effective scope set dependent on which row is consulted first, and a permission
    /// model whose answer depends on ordering is not a permission model. First-wins or union would both
    /// be inventions; refusing the configuration is the only answer that needs no invented rule.
    /// </para>
    /// <para>
    /// EVERY MESSAGE NAMES THE OFFENDING ROW'S INDEX and no message echoes an identity or a scope. An
    /// index is a position rather than a value, which is what lets a failure be locatable in a settings
    /// file without putting configured content into a log record.
    /// </para>
    /// </remarks>
    private static void CheckCallerAuthorizations(SecurityOptions options, List<string> failures)
    {
        // KEYED ON A PAIR RATHER THAN A JOINED STRING, so that no separator has to be trusted not to
        // occur inside an identity. A joined key would make caller "a" with audience "b:c" collide with
        // caller "a:b" and audience "c", and no separator choice removes that: every printable character
        // is legal inside a service identity, and even a control character is only excluded by argument.
        // The pair's default comparer compares each member with the ordinal string comparer, which is
        // what every other identity comparison in this service uses.
        HashSet<(string Caller, string Audience)> seenPairs = [];

        for (int index = 0; index < options.CallerAuthorizations.Count; index++)
        {
            CallerAuthorizationOptions row = options.CallerAuthorizations[index];

            if (string.IsNullOrWhiteSpace(row.Caller))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(CallerAuthorizationsKey, index)}:Caller' is " +
                    "required and must not be blank. A row authorising no caller authorises nothing " +
                    "while looking like a grant. This message never echoes the configured value.");
            }

            if (string.IsNullOrWhiteSpace(row.Audience))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(CallerAuthorizationsKey, index)}:Audience' is " +
                    "required and must not be blank. Each row authorises exactly one audience, which " +
                    "mirrors the contract's one-audience-per-request shape. This message never echoes " +
                    "the configured value.");
            }
            else if (!options.Audiences.Contains(row.Audience, StringComparer.Ordinal))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(CallerAuthorizationsKey, index)}:Audience' names " +
                    $"an audience that is not on '{AudiencesKey}'. The roster is what this issuer will " +
                    "mint for at all, so this row authorises a combination that could never be " +
                    "granted. This message never echoes the configured value.");
            }

            if (row.Scopes.Count == 0)
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(CallerAuthorizationsKey, index)}:Scopes' must " +
                    "carry at least one scope. A row permitting no scope permits nothing, which is " +
                    "indistinguishable from an absent row while looking like a grant.");
            }

            for (int scopeIndex = 0; scopeIndex < row.Scopes.Count; scopeIndex++)
            {
                string scope = row.Scopes[scopeIndex];

                if (!string.IsNullOrEmpty(scope) && !scope.Any(char.IsWhiteSpace))
                {
                    continue;
                }

                failures.Add(
                    "Configuration key " +
                    $"'{IndexedKey(IndexedKey(CallerAuthorizationsKey, index) + ":Scopes", scopeIndex)}' " +
                    "is not a usable scope. A scope must be non-empty and must carry no white space of " +
                    "any kind, because the scope claim is space-delimited and a scope containing a " +
                    "space would reach a verifier as two scopes. This message never echoes the " +
                    "configured value.");
            }

            // Checked even when a member above was reported blank: a second blank row is a second
            // defect, and reporting it as a duplicate rather than swallowing it tells an operator that
            // fixing the first row alone will not be enough.
            if (!seenPairs.Add((row.Caller, row.Audience)))
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(CallerAuthorizationsKey, index)}' repeats a " +
                    "caller-and-audience pair declared by an earlier row. Two rows for one pair would " +
                    "make the effective scope set depend on which is consulted first, and a permission " +
                    "model whose answer depends on ordering is not one. This message never echoes the " +
                    "configured values.");
            }
        }
    }

    /// <summary>
    /// Rejection: the client-certificate trust settings must be internally coherent - a named
    /// revocation mode this service implements, and an anchor path that is either unset or readable.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// THE MODE IS CHECKED HERE AND THE ANCHOR IS LOADED ELSEWHERE, DELIBERATELY. A validator's job is to
    /// reject a configuration that cannot be honoured, and a mode name outside the recognised set is
    /// exactly that - it is decidable from the value alone, with no file system involved. Whether the
    /// anchor path resolves to real certificates is decidable only by reading the file, which is the
    /// trust layer's own responsibility and is where that failure refuses the host; duplicating the read
    /// here would open the file twice and give two places to disagree about what counts as readable.
    /// </para>
    /// <para>
    /// AN UNSET ANCHOR IS NOT A FAILURE. It is the fail-closed state recorded on
    /// <see cref="SecurityOptions.ClientCertificateAuthorityPath"/>: a deployment that configures none
    /// issues no tokens and serves everything else. Refusing the host for it would make every host that
    /// does not need issuance - a unit-test host, a local bring-up of the other three services against a
    /// stubbed issuer - unstartable, which is a cost with no security benefit, because a host with no
    /// anchor cannot honour a certificate anyway.
    /// </para>
    /// <para>
    /// WHITE SPACE IS NOT AN UNSET PATH, AND IT IS REPORTED. A path consisting of blanks is a populated
    /// variable holding nothing usable - the shape a substitution that expanded to nothing produces - and
    /// silently treating it as unset would turn a deployment that intended mutual TLS into one that
    /// quietly refuses every caller. The message names the key and never echoes the value.
    /// </para>
    /// </remarks>
    private static void CheckClientCertificateTrust(SecurityOptions options, List<string> failures)
    {
        if (!ClientCertificateRevocationModes.IsRecognised(options.ClientCertificateRevocationMode))
        {
            failures.Add(
                $"Configuration key '{ClientCertificateRevocationModeKey}' names a revocation mode " +
                "this service does not implement. The recognised values are " +
                $"{string.Join(", ", ClientCertificateRevocationModes.Recognised)}, compared without " +
                "regard to case. This message never echoes the configured value.");
        }

        string anchor = options.ClientCertificateAuthorityPath;

        if (anchor.Length > 0 && string.IsNullOrWhiteSpace(anchor))
        {
            failures.Add(
                $"Configuration key '{ClientCertificateAuthorityPathKey}' is present but holds only " +
                "white space, which is a populated setting carrying no usable path rather than an " +
                "unset one. Leave it entirely unset to run without a client trust anchor - in which " +
                "case no caller certificate establishes an identity and token issuance answers 401 - " +
                "or point it at a readable PEM file holding the issuing certificate authority. This " +
                "message never echoes the configured value.");
        }
    }

    /// <summary>
    /// Rejections covering the issuance roster: it must not be empty, every entry must name a distinct
    /// subject, every named secret key must be a resolvable flat key name, and every audience and scope
    /// an entry grants must be a value this deployment can actually honour.
    /// </summary>
    /// <param name="options">The bound instance.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// <para>
    /// THE MOST IMPORTANT RULE HERE IS THE CROSS-CHECK, not any of the shape rules. An entry may grant
    /// an audience only if the deployment-wide roster serves it, because the issuer applies BOTH gates
    /// and the deployment-wide one first: an entry granting an audience the deployment does not serve is
    /// a permission that can never be exercised, and it reads in a settings file as though it can. That
    /// is the failure mode this check exists for - unreachable configuration that looks like working
    /// configuration - and it is exactly the class of defect a validator can catch and a test of the
    /// happy path cannot.
    /// </para>
    /// <para>
    /// AN EMPTY ROSTER IS REFUSED, AND IT IS REFUSED LOUDLY. The alternative reading - "no clients
    /// configured, so mint for nobody" - is safe in the narrow sense and catastrophic in the useful
    /// sense: this is the sole issuer, so a deployment in that state cannot give any service a
    /// credential, while its readiness probe reports healthy for as long as nobody tries. The legacy
    /// posture this mirrors ends a structural fault in process termination rather than in a warning
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
    /// </para>
    /// <para>
    /// DUPLICATE SUBJECTS ARE REFUSED because the registry keys by subject: a second entry for the same
    /// subject would either be silently dropped or silently win, and in both readings one of the two
    /// permission sets an operator wrote down is not the one being enforced. Ordinal comparison, matching
    /// every other identity comparison in this service.
    /// </para>
    /// <para>
    /// NO MESSAGE ECHOES A VALUE, and that discipline extends further here than elsewhere in this file
    /// because one of these members names a credential key. A failure reports the configuration key path
    /// - composed from the section name and the offending index so it can be pasted into a search - and
    /// the RULE that was broken, and nothing else. Not the subject, not the audience, not the scope and
    /// not the secret key name.
    /// </para>
    /// </remarks>
    private static void CheckClientRoster(SecurityOptions options, List<string> failures)
    {
        if (options.Clients.Count == 0)
        {
            failures.Add(
                $"Configuration key '{ClientsKey}' is empty. At least one issuance-roster entry is " +
                "required: this service is the sole token issuer, so an empty roster is a deployment " +
                "in which no service can obtain a credential at all while this one continues to " +
                "report healthy. Declare one entry per calling identity, each naming the audiences " +
                "and scopes that identity may request.");

            return;
        }

        // The deployment-wide roster the per-entry grants are cross-checked against. Read once, and
        // ordinally, because the issuer compares against it ordinally.
        HashSet<string> servedAudiences = new(options.Audiences, StringComparer.Ordinal);

        HashSet<string> seenSubjects = new(StringComparer.Ordinal);

        for (int index = 0; index < options.Clients.Count; index++)
        {
            SecurityClientOptions client = options.Clients[index];

            if (client is null)
            {
                failures.Add(
                    $"Configuration key '{IndexedKey(ClientsKey, index)}' bound to nothing. Every " +
                    "roster entry must be an object declaring a subject, its permitted audiences and " +
                    "its permitted scopes.");

                continue;
            }

            CheckClientSubject(client, index, seenSubjects, failures);
            CheckClientSecretKeyName(client, index, failures);
            CheckClientAudiences(client, index, servedAudiences, failures);
            CheckClientScopes(client, index, failures);
        }
    }

    /// <summary>
    /// Checks one roster entry's subject: present, and not already claimed by an earlier entry.
    /// </summary>
    /// <param name="client">The roster entry.</param>
    /// <param name="index">Its zero-based position, for the failure message.</param>
    /// <param name="seenSubjects">The subjects earlier entries claimed. Mutated.</param>
    /// <param name="failures">The accumulating failure list.</param>
    private static void CheckClientSubject(
        SecurityClientOptions client,
        int index,
        HashSet<string> seenSubjects,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(client.Subject))
        {
            failures.Add(
                $"Configuration key '{IndexedKey(ClientsKey, index)}:Subject' is required and must " +
                "carry at least one non-whitespace character. It is both the credential identity the " +
                "caller presents and the subject claim of every token minted for it.");

            return;
        }

        if (!seenSubjects.Add(client.Subject))
        {
            failures.Add(
                $"Configuration key '{IndexedKey(ClientsKey, index)}:Subject' repeats a subject an " +
                "earlier entry already declares. The roster is keyed by subject, so a duplicate means " +
                "one of the two permission sets is not the one being enforced. This message does not " +
                "echo the configured value.");
        }
    }

    /// <summary>
    /// Checks one roster entry's secret-key NAME, when it declares one.
    /// </summary>
    /// <param name="client">The roster entry.</param>
    /// <param name="index">Its zero-based position, for the failure message.</param>
    /// <param name="failures">The accumulating failure list.</param>
    /// <remarks>
    /// An absent name is valid - such an entry authenticates by client certificate only - but a name
    /// that is present and blank is not: it is a deployment that meant to name a key and named nothing,
    /// which would otherwise be indistinguishable from meaning to name none. The charset rule is the
    /// same conservative identifier rule the key store applies, and for the same reason: the value is a
    /// configuration-key name that must survive the environment provider's own name mangling intact.
    /// </remarks>
    private static void CheckClientSecretKeyName(
        SecurityClientOptions client,
        int index,
        List<string> failures)
    {
        if (client.SecretConfigurationKey is null)
        {
            return;
        }

        if (!IsSafeKeyRef(client.SecretConfigurationKey))
        {
            failures.Add(
                $"Configuration key '{IndexedKey(ClientsKey, index)}:SecretConfigurationKey' is not a " +
                $"usable configuration-key name. It must be 1 to {MaximumKeyRefLengthText} characters " +
                "of ASCII letters, ASCII digits, '.', '_' or '-'. Omit the member entirely for a " +
                "caller that authenticates by client certificate rather than by a shared secret. This " +
                "message never echoes the configured value, and the value is a NAME rather than a " +
                "secret in any case.");
        }
    }

    /// <summary>
    /// Checks one roster entry's audience grants: non-empty, and every entry served by the deployment.
    /// </summary>
    /// <param name="client">The roster entry.</param>
    /// <param name="index">Its zero-based position, for the failure message.</param>
    /// <param name="servedAudiences">The deployment-wide audience roster.</param>
    /// <param name="failures">The accumulating failure list.</param>
    private static void CheckClientAudiences(
        SecurityClientOptions client,
        int index,
        HashSet<string> servedAudiences,
        List<string> failures)
    {
        if (client.Audiences.Count == 0)
        {
            failures.Add(
                $"Configuration key '{IndexedKey(ClientsKey, index)}:Audiences' is empty. A caller " +
                "permitted no audience can obtain no usable token, so an entry granting none is a " +
                "roster entry an operator believes is working.");

            return;
        }

        for (int audienceIndex = 0; audienceIndex < client.Audiences.Count; audienceIndex++)
        {
            string audience = client.Audiences[audienceIndex];

            if (string.IsNullOrWhiteSpace(audience))
            {
                failures.Add(
                    $"Configuration key " +
                    $"'{IndexedKey(IndexedKey(ClientsKey, index) + ":Audiences", audienceIndex)}' is " +
                    "empty or contains only whitespace.");

                continue;
            }

            if (!servedAudiences.Contains(audience))
            {
                failures.Add(
                    $"Configuration key " +
                    $"'{IndexedKey(IndexedKey(ClientsKey, index) + ":Audiences", audienceIndex)}' " +
                    $"grants an audience that '{AudiencesKey}' does not serve. The issuer applies both " +
                    "gates and the deployment-wide one first, so this grant can never be exercised - " +
                    "it is unreachable configuration that reads as a granted permission. Add the " +
                    $"audience to '{AudiencesKey}' or remove it here. This message does not echo the " +
                    "configured value.");
            }
        }
    }

    /// <summary>
    /// Checks one roster entry's scope grants: non-empty, and every entry a usable scope token.
    /// </summary>
    /// <param name="client">The roster entry.</param>
    /// <param name="index">Its zero-based position, for the failure message.</param>
    /// <param name="failures">The accumulating failure list.</param>
    private static void CheckClientScopes(
        SecurityClientOptions client,
        int index,
        List<string> failures)
    {
        if (client.Scopes.Count == 0)
        {
            failures.Add(
                $"Configuration key '{IndexedKey(ClientsKey, index)}:Scopes' is empty. A token that " +
                "authorises nothing is not a credential, and every issuance request must name at " +
                "least one scope, so an entry granting none can only ever produce a refusal.");

            return;
        }

        HashSet<string> seenScopes = new(StringComparer.Ordinal);

        for (int scopeIndex = 0; scopeIndex < client.Scopes.Count; scopeIndex++)
        {
            string scope = client.Scopes[scopeIndex];
            string keyPath = IndexedKey(IndexedKey(ClientsKey, index) + ":Scopes", scopeIndex);

            if (!IsScopeToken(scope))
            {
                failures.Add(
                    $"Configuration key '{keyPath}' is not a usable scope token. It must be 1 to " +
                    $"{MaximumScopeLengthText} characters, each a visible ASCII character other than " +
                    "'\"' or '\\' - the RFC 6749 section 3.3 scope-token charset. The granted set " +
                    "travels as one space-delimited value in the response and in the token claim, so a " +
                    "scope containing white space could not be recovered by a reader and would " +
                    "silently become two scopes. This message does not echo the configured value.");

                continue;
            }

            if (!seenScopes.Add(scope))
            {
                failures.Add(
                    $"Configuration key '{keyPath}' repeats a scope this entry already grants. A " +
                    "repeated grant is not more permissive than a single one, so it is a typographical " +
                    "error rather than a policy, and the issuance request type refuses a repeated " +
                    "scope on the caller's side for the same reason.");
            }
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
    /// Determines whether a declared scope grant is a usable scope token.
    /// </summary>
    /// <param name="value">The declared scope.</param>
    /// <returns>
    /// <see langword="true"/> when the value is 1 to <see cref="MaximumScopeLength"/> characters, each a
    /// visible ASCII character other than <c>"</c> or <c>\</c>; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE CHARSET IS RFC 6749 SECTION 3.3's, NOT ONE INVENTED HERE - the printable ASCII range with the
    /// double quote and the backslash removed. Taking the standard's own definition rather than reusing
    /// the conservative configuration-key rule is deliberate: a scope name is a protocol value that
    /// travels in a token claim and is read by three other services, so the set of names a deployment may
    /// declare should be the set the protocol permits. Narrowing it to the key-reference charset would
    /// refuse names the standard allows and that a future deployment may legitimately need.
    /// </para>
    /// <para>
    /// WHITE SPACE IS THE EXCLUSION THAT CARRIES THE WEIGHT, and it is excluded by construction rather
    /// than by a separate test, because the space character is not a visible ASCII character. The granted
    /// set is joined into ONE space-delimited value for the response and the claim, so a scope containing
    /// a space could not be recovered by a reader and would silently become two scopes - the identical
    /// constraint the issuance request type enforces on the caller's side.
    /// </para>
    /// <para>
    /// A <see langword="null"/> entry - possible in a bound collection - is rejected here rather than
    /// dereferenced, matching how the key-reference check treats one.
    /// </para>
    /// </remarks>
    private static bool IsScopeToken(string? value)
    {
        if (value is null || value.Length == 0 || value.Length > MaximumScopeLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            // The visible ASCII range, less the two characters the grammar excludes.
            bool permitted =
                character is >= '\u0021' and <= '\u007E' &&
                character is not ('"' or '\\');

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
    /// <param name="material">The configured signing material. Never logged or echoed.</param>
    /// <param name="keySizeBits">
    /// Receives the modulus size in bits of the imported key, so the caller can apply the configured
    /// floor without importing the material a second time; zero when nothing imported.
    /// </param>
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
    /// method's entire vocabulary is <see langword="true"/>, <see langword="false"/> and ONE
    /// MEASUREMENT - the modulus length, which is not a secret, since this service publishes it in the
    /// key set it serves anonymously. No byte of the key itself is returned or measured. Every key
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
    private static bool CanImportPrivateKey(string material, out int keySizeBits)
    {
        if (TryImportArmoured(material, out keySizeBits))
        {
            return true;
        }

        byte[]? bare = TryDecodeBase64(material);

        if (bare is null)
        {
            // Neither armoured nor valid base64, so there is no third encoding left to attempt.
            keySizeBits = 0;

            return false;
        }

        try
        {
            return TryImportBareStructures(bare, out keySizeBits);
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
    /// <param name="keySizeBits">
    /// Receives the modulus size in bits when the material imported and carries a private component;
    /// otherwise zero.
    /// </param>
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
    private static bool TryImportArmoured(string material, out int keySizeBits)
    {
        keySizeBits = 0;

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

        if (!HasPrivateComponent(candidate))
        {
            return false;
        }

        // Read from the IMPORTED key rather than from the platform default, and read only after the
        // private-component assertion has passed: a public-only key imports on this platform and would
        // report a perfectly respectable modulus length, so measuring before that check would let a
        // verification-only key satisfy a floor it has no business satisfying.
        keySizeBits = candidate.KeySize;

        return true;
    }

    /// <summary>
    /// Attempts each bare binary private-key structure in turn.
    /// </summary>
    /// <param name="bare">The decoded key bytes.</param>
    /// <param name="keySizeBits">
    /// Receives the modulus size in bits of whichever structure imported; otherwise zero.
    /// </param>
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
    private static bool TryImportBareStructures(byte[] bare, out int keySizeBits) =>
        TryImportBareStructure(
            bare,
            static (key, bytes) => key.ImportPkcs8PrivateKey(bytes, out _),
            out keySizeBits) ||
        TryImportBareStructure(
            bare,
            static (key, bytes) => key.ImportRSAPrivateKey(bytes, out _),
            out keySizeBits);

    /// <summary>
    /// Attempts exactly one bare binary key structure.
    /// </summary>
    /// <param name="bare">The decoded key bytes.</param>
    /// <param name="import">The single platform import to attempt.</param>
    /// <param name="keySizeBits">
    /// Receives the modulus size in bits when the structure imported; otherwise zero.
    /// </param>
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
    private static bool TryImportBareStructure(
        byte[] bare,
        Action<RSA, byte[]> import,
        out int keySizeBits)
    {
        keySizeBits = 0;

        using RSA candidate = RSA.Create();

        try
        {
            import(candidate, bare);
        }
        catch (CryptographicException)
        {
            return false;
        }

        // Both attempted structures are PRIVATE-key structures, so unlike the armoured path there is
        // no public-only case to exclude before the modulus can be measured.
        keySizeBits = candidate.KeySize;

        return true;
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
