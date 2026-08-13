// ==================================================================================================
//  SigningKeyProvider - resolves the one signing key this system has, and publishes only its public
//  half
//  ------------------------------------------------------------------------------------------------
//  THIS FILE IS NET-NEW .NET BEHAVIOUR, NOT A PORT. The legacy PowerBuilder estate contains no token
//  issuance behaviour at all: it opens no listening socket, registers no route, receives no
//  unsolicited request and therefore has nothing to authenticate against. A repository-wide search
//  for the token vocabulary finds it in three text files and not one of them mints anything - one
//  merely CONSUMES a caller-supplied token and belongs to a capability area outside this phase, one
//  is a vendored third-party script, and one is the anti-pattern named below. The discovery-metadata
//  path appears nowhere in the legacy at all.
//
//  So the legacy locators cited throughout this file are EVIDENCE AND PROVENANCE ONLY. They justify
//  decisions; none of them is translated, and none is read, opened, embedded, linked or referenced
//  at build time or at run time.
//
//  LEGACY PROVENANCE, EVERY PATH READ ONLY - the behavioural oracle for parity testing, never an
//  edit target:
//
//      ws_objects/pfw.shared.pbl.src/enums.sru:L927        the hash-type block comment, which names
//                                                          RSASign and VerifyRSASign as sharing the
//                                                          set - the RS256 anchor
//      ws_objects/pfw.shared.pbl.src/enums.sru:L930        SHA-256 = 2, the digest RS256 names
//      ws_objects/pfw.shared.pbl.src/enums.sru:L949-L951   the RSA padding set: PKCS number 1 and
//                                                          OAEP, and nothing else. NO PSS member and
//                                                          NO no-padding member exist
//      ws_objects/pfw.shared.pbl.src/enums.sru:L965        1024-bit RSA is a first-class legal size
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8       the whole surface is a closed native
//                                                          binary with no PowerScript body
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20  GenRSAKey, whose armoured output is an
//                                                          OPTIONAL fourth argument
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73  RSASign and VerifyRSASign - a hash type
//                                                          and NO padding argument at all
//      ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L75      a global auto-instance shadowing its own
//                                                          type name
//      ws_objects/pfw.pbl.src/pfw.sra:L111-L144            the fail-fast posture, terminating the
//                                                          process at :L143
//      tests/blink/test_jws.htm:L8-L23                     THE ANTI-PATTERN THIS FILE REPLACES
//
//  ==================================================================================================
//  THE ANTI-PATTERN THIS FILE EXISTS TO MAKE STRUCTURALLY IMPOSSIBLE
//  ==================================================================================================
//  tests/blink/test_jws.htm:L8-L23 signs a token with a private key WRITTEN INTO THE SOURCE. Its
//  shape, described because the shape is the lesson and the values are not reproduced anywhere: a
//  script variable is opened with an armour delimiter, a dozen lines of encoded private-key body are
//  concatenated onto it, a matching closing delimiter ends it, and the resulting in-source key is
//  handed straight to a third-party signer together with a hardcoded header and a hardcoded payload.
//
//  NO VALUE, FRAGMENT, ENCODED RUN, SUBJECT, EXPIRY, DIGEST OR TOKEN FROM THAT FILE APPEARS IN THIS
//  ONE, in any form - not transformed, not reversed, not split, not re-encoded and not disguised -
//  and none may ever be added, here or in a test, a fixture, a settings file or a documentation
//  example. .dockerignore excludes that directory from every image layer as an explicit secrets
//  control, which is independent corroboration that this material must not re-enter the .NET tree.
//
//  Three properties of this file are the direct inversion of that shape, and each is checkable:
//
//      1. THE KEY IS NEVER IN THE SOURCE. It arrives from configuration, through the options type,
//         and there is no other ingress - no file read, no environment probe, no configuration
//         indexer, no secret store, no embedded resource, no assembly attribute, no certificate
//         store and no network call.
//      2. THE HEADER IS NOT WRITTEN BY HAND. The key identifier is set ON the key, so the minting
//         library stamps it into the token header itself. Nothing has to restate it, so the header
//         and the published key set cannot disagree.
//      3. THE LIFETIME IS NOT A LITERAL. That file's expiry is a hardcoded instant that fell into
//         the past years ago, which is exactly why a lifetime must be configuration plus a clock.
//         Neither belongs here: the lifetime is a token concern, so this file takes no clock and
//         expresses no time-dependent behaviour whatsoever.
//
//  Its header additionally names a probabilistic signature scheme. THAT IS THE THIRD-PARTY SCRIPT'S
//  CHOICE AND NOT A CONTRACT: the legacy declares no identifier for it. Its padding set has exactly
//  two members, block padding and OAEP [enums.sru:L949-L951], and its signing primitive takes no
//  padding argument at all [n_crypto.sru:L70-L73], so the legacy cannot express that scheme and
//  neither does this issuer.
//
//  ==================================================================================================
//  WHY THE KEY IS ASYMMETRIC, AND WHY THAT IS STRUCTURAL RATHER THAN A PREFERENCE
//  ==================================================================================================
//  The configured signing material is an RSA PRIVATE KEY and tokens are signed with the RSA family
//  of block-padded signatures over the SHA-2 digests. A symmetric key is not a weaker option here;
//  it is an IMPOSSIBLE one, for a reason that has nothing to do with algorithm strength:
//
//      * The key set is published ANONYMOUSLY. It has to be - a consumer fetches it in order to
//        learn how to validate, so it cannot already hold a token to present when it asks.
//      * The other three services hold VERIFICATION MATERIAL ONLY and are explicitly not
//        independent signing authorities. Exactly one signing secret exists in the whole system and
//        this service holds it.
//      * A symmetric entry in a published key set would publish the SIGNING SECRET ITSELF, and
//        handing that secret to three verifiers would make each of them a co-signer. The sole-issuer
//        topology that is this service's entire reason for existing would be gone in one request.
//
//  With an asymmetric key the split is structural instead of procedural: the private half never
//  leaves this process, and the public half is the only thing that ever does.
//
//  THE ALGORITHM AGREES WITH THE LEGACY SIGNING PRIMITIVE RATHER THAN BEING CHOSEN FRESHLY.
//  enums.sru:L927 records, in the oracle's own source comment, that one hash-type set parameterises
//  the hashing, signing AND verifying primitives; SHA-256 is a member of it at :L930. The signature
//  scheme is fixed inside the closed binary because :L70-L73 expose a hash type and no padding
//  argument, and the sibling asymmetric provider adopts block padding for exactly that reason. RSA
//  plus SHA-256 plus block padding IS what the legacy primitive already expresses, so the default
//  here restates a legacy capability rather than introducing one.
//
//  THE PERMITTED SET IS THE THREE-MEMBER CLOSED SET THE OPTIONS CONTRACT PUBLISHES, and the reason
//  it is three rather than one is that the options validator admits three and documents that this
//  provider is what reads the setting. Narrowing it here to the default alone would let a value pass
//  validation and then be refused by the very type the validator says consumes it - two files in one
//  service disagreeing about their own contract. The three correspond exactly to the digests the
//  oracle's own comment names as its signing primitive's argument set [enums.sru:L930-L932]. Every
//  other value is refused, including the keyed-hash family for the structural reason above, the
//  probabilistic family for the missing-identifier reason above, and the elliptic-curve family
//  because the legacy cryptographic class declares no such primitive.
//
//  ==================================================================================================
//  WHY THE MINTING LIBRARY IS REFERENCED BY THIS SERVICE AND BY NO OTHER
//  ==================================================================================================
//  Microsoft.IdentityModel.JsonWebTokens is the token-MINTING library, and its package reference
//  appears in exactly one project file in the whole repository: this service's. That is not tidiness
//  - it is the sole-issuer topology expressed in the build graph, where a reviewer can check it by
//  reading project files rather than by auditing code. The other three services reference only the
//  inbound bearer handler, so they are structurally incapable of minting: the type that would do it
//  is not on their compile path at all.
//
//  This file is the reason that reference is needed. It produces the credential the issuer signs
//  with, and the verification projections everything else consumes.
//
//  ==================================================================================================
//  FAIL FAST, NEVER GRACEFUL DEGRADATION
//  ==================================================================================================
//  The legacy's answer to a structural fault is to end the process: its application object unpacks a
//  seven-field assert payload and then halts [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating
//  at :L143]. Read that event looking for a recovery arm and there is none - no retry, no default, no
//  continue, no downgrade to a warning.
//
//  The .NET equivalent at startup is a host that refuses to start, so every fault below is raised
//  from the constructor. Resolution happens once, eagerly, when the container builds this singleton;
//  a misconfigured issuer therefore never reaches the point of answering a health probe. That
//  matters more here than anywhere else in the service, because an issuer that starts and cannot
//  sign satisfies the orchestration readiness gate its own dependents are waiting on and then fails
//  every request behind it, looking healthy from the outside the entire time.
//
//  FIVE SOFTENINGS ARE FORBIDDEN, each of which would be a severe defect rather than a robustness
//  improvement:
//
//      * Falling back to a keyed-hash key. It would collapse the sole-issuer topology (see above).
//      * Generating a key. It would silently invalidate every token the other three services already
//        hold, and it would differ on every restart, so the fleet-wide outage would be intermittent
//        as well as silent. A loud startup failure is strictly better.
//      * Logging a warning and continuing. This type takes NO LOGGER, deliberately: an absent
//        dependency cannot be misused by a later edit the way a present one can, and the one thing
//        it could log is the one thing that must never be logged.
//      * Returning a null, placeholder or dummy credential.
//      * Deferring the failure to first use in a way that lets the host report healthy.
//
//  ==================================================================================================
//  KEY HYGIENE - THE RULES THIS FILE IS SHAPED TO ENFORCE, ALL OF THEM CHECKABLE
//  ==================================================================================================
//      * NO KEY, ARMOUR DELIMITER, ENCODED RUN, CERTIFICATE, PASSWORD OR TOKEN LITERAL APPEARS
//        ANYWHERE IN THIS FILE - not as code, not as a default value, not as an example, not in a
//        comment and not in test data. Not even a delimiter is written out: the armoured shape is
//        attempted and its failure interpreted rather than sniffed for, both because that is simpler
//        and because a delimiter string is precisely what a credential scanner matches on, so
//        writing one would trip the scan that protects this file for no functional gain.
//      * NO FAILURE MESSAGE ECHOES THE CONFIGURED VALUE, any fragment of it, or even a measurement
//        of it. Every message is FIXED at compile time and names the CONFIGURATION KEY, so no code
//        path can interpolate the material by accident.
//      * NO PLATFORM EXCEPTION IS CHAINED. Those describe a structure rather than their input, but
//        the certainty required to attach one is not available and the diagnostic value is nil: the
//        fixed message already says what the accepted shapes are.
//      * EVERY DECODED BUFFER IS ZEROED before release rather than left for collection, which is
//        non-deterministic and may copy the buffer while compacting. Every rejected candidate key is
//        disposed on the spot. Every lambda that touches key bytes is static, so no key material can
//        be captured in a closure.
//      * THE PUBLIC PROJECTIONS ARE BUILT FROM PUBLIC PARAMETERS, never by stripping fields from the
//        private key. A private component is therefore STRUCTURALLY ABSENT from them rather than
//        removed after the fact, and the published projection type declares no member that could
//        carry one.
//      * THIS TYPE DECLARES NO STRING RENDERING, NO DEBUGGER DISPLAY, NO SERIALIZATION HELPER AND NO
//        VALUE EQUALITY, and it is deliberately not a record, so that no generated member can print
//        or compare the material.
//
//  ==================================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ==================================================================================================
//      * NO ROTATION MACHINERY, no key ring, no second key, no key generation, no key-age or expiry
//        policy, no re-read on configuration change and no revocation list. A key identifier exists
//        so that rotation is EXPRESSIBLE - the published set's shape admits more than one entry and a
//        verifier selects by identifier - not so that rotation is IMPLEMENTED. Implementing it would
//        be a new capability, which this refactor does not add.
//      * NO KEY-SIZE FLOOR. The oracle keeps 1024-bit RSA as a first-class legal size
//        [enums.sru:L965], so refusing a short key here would be a silent correction of legacy
//        behaviour dressed up as a security fix. Structural USABILITY is what is validated: can this
//        material be imported, and can it sign. A size the minting library itself refuses is that
//        library's report to make at the moment it makes it, not a policy for this file to invent -
//        which is the position the options validator takes as well, so the two agree.
//      * NO CLOCK. Token lifetime belongs to the issuer, and taking a clock here would invite
//        time-dependent key behaviour, which is rotation by another name.
//      * NO OUTBOUND EDGE. There is no reference to, and no client for, any other service: this
//        service's callers reach in, it does not reach out. Its verification material is handed to
//        its own inbound handler IN PROCESS rather than fetched over HTTP, because a service
//        discovering its own metadata from itself would deadlock its own startup in a cold container.
//      * NO USER INTERFACE CONCERN OF ANY KIND. This phase is API and service level only: no
//        component library, no design tokens, no theming and no styling are in scope anywhere, and
//        none appears here.
//
//  RULES POSITION. The project's rules document contains exactly one line, stating that no user
//  rules were provided, so NO user-specified rule governs this file. Nothing is invented or
//  back-filled from convention in their place: the bar applied instead is the enterprise-standard
//  baseline the migration plan states - nullable reference types with warnings as errors, no secret
//  in source or settings or any container definition, constructor injection throughout so the
//  sibling test project can reach every branch, and the published contract as the only cross-service
//  coupling.
// ==================================================================================================

using System.Buffers.Text;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;

namespace PowerFramework.Security.Tokens;

/// <summary>
/// Resolves the system's single signing key from configuration exactly once, and exposes it as one
/// private credential for minting plus two public-only projections for verification.
/// </summary>
/// <remarks>
/// <para>
/// THE PUBLIC AND PRIVATE HALVES ARE SEPARATE MEMBERS, AND THAT SPLIT IS THIS TYPE'S WHOLE REASON
/// FOR EXISTING. <see cref="SigningCredentials"/> is the only member that carries private key
/// material, and it is reachable only in process, by the token issuer, which is itself reachable
/// only through an authenticated route. <see cref="PublicVerificationKey"/> and
/// <see cref="PublishedKeySet"/> are derived from PUBLIC PARAMETERS ONLY, so a private component is
/// structurally absent from them rather than removed after the fact.
/// </para>
/// <para>
/// It replaces the shape at <c>tests/blink/test_jws.htm:L8-L23</c>, where a private key written into
/// the source is handed straight to a signer. The key here arrives from
/// <see cref="SecurityOptions"/> and from nowhere else: there is no file read, no environment probe,
/// no configuration indexer, no secret store, no embedded resource, no assembly attribute, no
/// certificate store, no network call and no static holder. No value from that file is reproduced
/// here in any form.
/// </para>
/// <para>
/// THE ALGORITHM IS ASYMMETRIC BY STRUCTURAL NECESSITY, not by preference. The key set is published
/// anonymously and the other three services hold verification material only, so a symmetric entry
/// there would publish the signing secret itself and make each of those three a co-signer. The
/// default restates a legacy capability rather than introducing one: the oracle's own source comment
/// names one hash-type set as the argument of its hashing, signing and verifying primitives alike
/// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927</c>], SHA-256 is a member of it
/// [<c>:L930</c>, ported as <see cref="PowerFramework.Shared.Kernel.Enums.CRYPTO_HASH_SHA256"/>],
/// and its signing primitive takes a hash type with no padding argument at all
/// [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73</c>] so the scheme is block padding. The
/// probabilistic scheme the anti-pattern file names has NO legacy identifier: that padding set has
/// exactly two members [<c>enums.sru:L949-L951</c>], so it is that third-party script's choice and
/// not a contract.
/// </para>
/// <para>
/// FAIL FAST. Every fault is raised from the constructor, so a misconfigured issuer never starts and
/// never answers a probe. That mirrors the oracle, whose application object ends the process on a
/// structural fault [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>, terminating at <c>:L143</c>].
/// No fallback, no generated key, no warning-and-continue, no placeholder credential.
/// </para>
/// <para>
/// LIFETIME AND THREAD SAFETY. Register as a singleton. Every field is assigned during construction
/// and never mutated afterwards, so concurrent reads need no synchronisation, and the underlying
/// signature providers the minting library caches are themselves safe for concurrent use. Resolving
/// once is a lifetime concern rather than a cache: there is no eviction policy, no expiry and no
/// refresh, because there is exactly one key and it does not change while the process runs.
/// </para>
/// <para>
/// The instance owns the imported key and therefore implements <see cref="IDisposable"/>. The
/// container disposes it at host shutdown; members raise
/// <see cref="ObjectDisposedException"/> afterwards rather than handing out a key whose platform
/// handle has been released.
/// </para>
/// <example>
/// The intended composition, which the service entry point owns:
/// <code>
/// builder.Services.AddSingleton&lt;SigningKeyProvider&gt;();
///
/// // Inbound validation is configured IN PROCESS from the public half. Pointing the handler at this
/// // service's own discovery document would make startup fetch metadata from the service that is
/// // starting, which cannot succeed in a cold container.
/// bearer.TokenValidationParameters.IssuerSigningKey =
///     provider.PublicVerificationKey;
/// </code>
/// </example>
/// </remarks>
public sealed class SigningKeyProvider : IDisposable
{
    /// <summary>
    /// The configuration key that carries the signing algorithm: <c>Security:SigningAlgorithm</c>.
    /// </summary>
    /// <remarks>
    /// Composed from <see cref="SecurityOptions.SectionName"/> rather than written out, so the one
    /// spelling of the section name lives in one place and a rename cannot leave this message
    /// pointing at a key that no longer exists.
    /// </remarks>
    private const string SigningAlgorithmConfigurationKey =
        SecurityOptions.SectionName + ":SigningAlgorithm";

    /// <summary>
    /// The configuration key that carries the key identifier: <c>Security:SigningKeyId</c>.
    /// </summary>
    private const string SigningKeyIdConfigurationKey =
        SecurityOptions.SectionName + ":SigningKeyId";

    /// <summary>
    /// The key family published in the key set: <c>RSA</c>.
    /// </summary>
    /// <remarks>
    /// Fixed by the published contract, whose key schema constrains this member to a single-value
    /// enumeration [<c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>, schema
    /// <c>JsonWebKey</c>]. WHERE THIS FILE AND THAT DOCUMENT EVER DISAGREE, THE DOCUMENT WINS,
    /// because it is what a consumer's stock handler reads. The value is also the key-family
    /// identifier the key-set format registers, so the two coincide by construction rather than by
    /// coincidence.
    /// </remarks>
    private const string PublishedKeyType = "RSA";

    /// <summary>
    /// The intended use published in the key set: <c>sig</c>.
    /// </summary>
    /// <remarks>
    /// Signature verification, which is the only use this set exists to serve - an encryption key is
    /// never published here. Constrained to a single-value enumeration by the same schema.
    /// </remarks>
    private const string PublishedKeyUse = "sig";

    /// <summary>
    /// The one fixed message reported when the signing material is absent.
    /// </summary>
    /// <remarks>
    /// It names the configuration key and says nothing whatsoever about what was, or was not,
    /// supplied. Reported separately from the unusable case because the two have different fixes:
    /// absent means the variable was never populated, whereas unusable means it was populated with
    /// the wrong kind of thing.
    /// </remarks>
    private const string SigningKeyAbsentMessage =
        "Configuration key '" + SecurityOptions.SigningKeyEnvironmentVariableName + "' is not set, " +
        "is empty or contains only whitespace. It is the only signing key in the system and it has " +
        "no default. None is generated in any environment, because an ephemeral key would silently " +
        "invalidate every token the other services already hold and would differ on every restart. " +
        "Supply an asymmetric RSA private key through the orchestration configuration layer. Note " +
        "that this name is a FLAT configuration key rather than a member of the '" +
        SecurityOptions.SectionName + "' section. This message never echoes the configured value.";

    /// <summary>
    /// The one fixed message reported when the signing material is present but cannot be imported as
    /// an asymmetric private key in any accepted encoding.
    /// </summary>
    /// <remarks>
    /// Fixed at compile time rather than composed, so that no code path can interpolate the
    /// material, a fragment of it or a measurement of it. It describes the ACCEPTED SHAPES without
    /// writing out a delimiter, which keeps this file clear of the literal every credential scanner
    /// matches on. The platform exception that caused the rejection is deliberately not attached:
    /// this message already says everything a diagnostic needs.
    /// </remarks>
    private const string SigningKeyUnusableMessage =
        "Configuration key '" + SecurityOptions.SigningKeyEnvironmentVariableName + "' is set but " +
        "could not be imported as an asymmetric RSA private key. Two encodings are accepted and " +
        "both are attempted: armoured text first, then base64 of the bare binary encoding on a " +
        "single line. Random bytes are not an asymmetric key and cannot sign; generate an RSA " +
        "private key instead. This message never echoes the configured value.";

    /// <summary>
    /// The one fixed message reported when the signing material imports as an asymmetric key that
    /// carries no private component.
    /// </summary>
    /// <remarks>
    /// A SEPARATE FAULT WITH A SEPARATE FIX, and it is not hypothetical: armoured import of a public
    /// key SUCCEEDS on this platform, so without this rejection an issuer configured with the public
    /// half would start, satisfy the readiness gate, and then fail to sign every token. The options
    /// validator asserts the same thing for the same reason, so the two agree.
    /// </remarks>
    private const string SigningKeyPublicOnlyMessage =
        "Configuration key '" + SecurityOptions.SigningKeyEnvironmentVariableName + "' imported as " +
        "an asymmetric key that carries NO PRIVATE COMPONENT, so it cannot sign. Supply the " +
        "PRIVATE key: the public half is derived from it and published automatically, so there is " +
        "no public-key setting anywhere and no key to distribute by hand. This message never " +
        "echoes the configured value.";

    /// <summary>
    /// The one fixed message reported when the key identifier is absent.
    /// </summary>
    /// <remarks>
    /// The identifier is required rather than optional because it is what the published key and the
    /// token header agree on: a verifier selects by it, so an issuer without one cannot express a
    /// rollover at all. It carries no key material, which is why the message may name the key it
    /// belongs to.
    /// </remarks>
    private const string SigningKeyIdAbsentMessage =
        "Configuration key '" + SigningKeyIdConfigurationKey + "' is required and must not be " +
        "blank. It is the key identifier published in the key set and stamped into the header of " +
        "every minted token, and it is what a consumer selects by while more than one key is " +
        "current.";

    /// <summary>
    /// The one fixed message reported when the configured algorithm is not one this issuer signs
    /// with.
    /// </summary>
    /// <remarks>
    /// COMPOSED AT COMPILE TIME FROM THE SAME THREE CONSTANTS THE RESOLVER SWITCHES ON, so this
    /// message can never describe a set the check does not enforce. It names the configuration key
    /// and not the rejected value, uniformly with every other message here - the identifier is not
    /// sensitive, but a single rule with no exceptions is one that cannot be got wrong at a later
    /// call site.
    /// </remarks>
    private const string SigningAlgorithmUnsupportedMessage =
        "Configuration key '" + SigningAlgorithmConfigurationKey + "' is not one of the asymmetric " +
        "algorithms this issuer signs with. The permitted values are " +
        SecurityAlgorithms.RsaSha256 + ", " + SecurityAlgorithms.RsaSha384 + " and " +
        SecurityAlgorithms.RsaSha512 + ", compared case-sensitively because these identifiers are. " +
        "The keyed-hash family is refused structurally: the key set is published anonymously, so a " +
        "symmetric key there would publish the signing secret itself and make every verifier a " +
        "co-signer. The probabilistic family is refused because the legacy declares no identifier " +
        "for it, and the elliptic-curve family because the legacy declares no such primitive.";

    /// <summary>
    /// The imported private key. THE ONE PIECE OF PRIVATE KEY MATERIAL THIS TYPE HOLDS, and the one
    /// resource it owns and must dispose.
    /// </summary>
    /// <remarks>
    /// Held rather than re-imported because importing per request would repeat a comparatively
    /// expensive decode on every mint for no benefit; the material does not change while the process
    /// runs. It is never returned, rendered, logged or exported from any member: the only way out is
    /// through <see cref="SigningCredentials"/>, which is the credential the issuer signs with.
    /// </remarks>
    private readonly RSA _privateKey;

    /// <summary>
    /// The credential the token issuer signs with, built once during construction.
    /// </summary>
    private readonly SigningCredentials _signingCredentials;

    /// <summary>
    /// The public-only key handed to the inbound bearer handler in process.
    /// </summary>
    private readonly RsaSecurityKey _publicVerificationKey;

    /// <summary>
    /// The immutable published projection, built once during construction.
    /// </summary>
    private readonly PublishedJsonWebKeySet _publishedKeySet;

    /// <summary>
    /// Whether <see cref="Dispose"/> has run.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Resolves the signing key from configuration, or refuses to construct.
    /// </summary>
    /// <param name="options">
    /// The bound security configuration. THE ONLY INGRESS FOR THE SIGNING MATERIAL, by constructor
    /// injection.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="options"/> is <see langword="null"/>, which can only mean the composition root
    /// is miswired.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The configuration cannot yield a usable signing key: the material is absent, empty or
    /// whitespace only; it cannot be imported in any accepted encoding; it imported but carries no
    /// private component; the key identifier is blank; or the algorithm is not one this issuer signs
    /// with. Each case reports a fixed message naming the configuration key and never its value.
    /// </exception>
    /// <remarks>
    /// <para>
    /// EAGER BY DESIGN. Everything is resolved here rather than on first use, because the point of
    /// validating at all is that a misconfigured issuer must not reach the point of answering a
    /// health probe. There is no lazy path to fall back to and no arm that returns a partially
    /// initialised instance.
    /// </para>
    /// <para>
    /// ORDER MATTERS AND IS DELIBERATE. The identifier and the algorithm are checked BEFORE the
    /// material is imported, so a deployment that got a plain name or a plain identifier wrong is
    /// told exactly that, rather than being told about the key. Import is the last and most expensive
    /// step.
    /// </para>
    /// <para>
    /// The two accepted encodings and their fixed order mirror the options validator exactly -
    /// armoured text first, then base64 of the bare binary encoding - so nothing that passes
    /// validation can then be refused here. Both shapes genuinely occur: the legacy key generator's
    /// armoured output is an OPTIONAL fourth argument
    /// [<c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20</c>], and the single-line base64 form is
    /// the only shape an environment file can carry because that format has no line continuation. The
    /// modulus is MEASURED below rather than judged, and the validator measures and discards it, so there
    /// is no size rule anywhere for the two to disagree about either.
    /// </para>
    /// <para>
    /// NO KEY IS REFUSED FOR BEING SHORT, AND THAT IS THE REQUIREMENT RATHER THAN AN OVERSIGHT. An
    /// earlier revision enforced a configurable 2048-bit floor here and failed construction below it.
    /// AAP 0.6.6.4 keeps 1024-bit RSA a legal size across this estate - the oracle's own catalogue lists
    /// it as first class [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L965</c>] - and requires every weak
    /// cryptographic default to be replicated as an ANNOTATED default rather than corrected, so a
    /// rejection would be a behaviour change dressed as robustness (C-B). The modulus is therefore
    /// MEASURED and REPORTED: <see cref="SigningKeySizeBits"/> publishes it,
    /// <see cref="SigningKeyIsLegacyWeak"/> compares it against
    /// <see cref="SecurityOptions.LegacyWeakSigningKeySizeBits"/>, and a warning naming the measured
    /// size is logged once at construction when the comparison holds. The same allowance is untouched on
    /// the neighbouring C-02 surface, <c>Crypto/RsaProvider.GenRSAKey</c>, where a caller names the size
    /// and byte-for-byte parity is the obligation.
    /// </para>
    /// <para>
    /// WHAT IS STILL FATAL. Material that is absent, unreadable, or public-only fails construction, and
    /// the host therefore refuses to start: an issuer that cannot sign is a broken configuration rather
    /// than a weak one, and the fail-fast posture reproduced from
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L143</c> applies to it.
    /// </para>
    /// <para>
    /// NO CLEAN-UP ARM WRAPS THE STEPS AFTER THE IMPORT, and that is reasoned rather than overlooked.
    /// NO step after the import can fail for a configuration reason at all, now that no key is refused for
    /// its length: measuring the modulus, logging the annotation when it is short, exporting the public
    /// parameters, encoding two public members, and pairing the key with its algorithm all operate on a
    /// key that has just imported successfully. Were one of
    /// them to fail anyway, this constructor throws, so the host refuses to start and the process ends;
    /// there is no arm in which a leaked key handle outlives the failure, because there is no arm in
    /// which anything outlives it. Wrapping them would add a recovery path that only an
    /// already-terminating process could ever reach.
    /// </para>
    /// </remarks>
    public SigningKeyProvider(IOptions<SecurityOptions> options, ILogger<SigningKeyProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        SecurityOptions security = options.Value;

        // Checked first: a blank identifier and a wrong algorithm are configuration mistakes that
        // have nothing to do with the key, and reporting them before touching the material keeps the
        // diagnostic pointed at the thing that is actually wrong.
        string keyId = ResolveKeyId(security.SigningKeyId);
        string algorithm = ResolveSigningAlgorithm(security.SigningAlgorithm);
        string material = RequireSigningMaterial(security.SigningKey);

        _privateKey = ImportPrivateKey(material);

        // --------------------------------------------------------------------------------------------
        // THE MODULUS IS MEASURED AND ANNOTATED HERE, AND NOTHING IS REFUSED FOR ITS SIZE
        //
        // Placed immediately after the import because that is the first line at which the size is
        // knowable, and before any credential exists so that both projections below carry a key whose
        // measurement has already been published.
        //
        // A SHORT KEY IS USED, NOT REJECTED. AAP 0.6.6.4 keeps 1024-bit RSA legal across this estate and
        // requires each weak cryptographic default to be replicated as an ANNOTATED default; C-B forbids
        // correcting it. The annotation is therefore the whole mechanism: the measured size is published
        // on SigningKeySizeBits, the verdict on SigningKeyIsLegacyWeak, and a warning naming the measured
        // size is logged once here so an operator learns of the weakness from the service itself rather
        // than only from docs/SECRETS.md 4.1.
        //
        // THE SAME ALLOWANCE IS UNTOUCHED WHERE IT IS THE LEGACY'S. Crypto/RsaProvider's GenRSAKey
        // enforces no minimum either, preserving the catalogue's 1024-bit entry
        // [ws_objects/pfw.shared.pbl.src/enums.sru:L965], and a test pins the two surfaces together
        // rather than apart.
        //
        // A SIZE IS NOT A SECRET, so logging it discloses nothing: the modulus itself is published in
        // the key set this service serves anonymously at /.well-known/jwks.json.
        // --------------------------------------------------------------------------------------------
        SigningKeySizeBits = _privateKey.KeySize;
        SigningKeyIsLegacyWeak = SigningKeySizeBits < SecurityOptions.LegacyWeakSigningKeySizeBits;

        if (SigningKeyIsLegacyWeak)
        {
            logger?.LogWarning(
                "The configured signing key has a {SigningKeySizeBits}-bit RSA modulus, below the "
                + "{LegacyWeakSigningKeySizeBits}-bit size at which this service stops remarking on it. "
                + "The key is accepted and used: AAP 0.6.6.4 keeps 1024-bit RSA a legal size in this "
                + "estate and requires the weakness to be annotated rather than corrected. Rotate the "
                + "issuer key to a longer modulus when the deployment can.",
                SigningKeySizeBits,
                SecurityOptions.LegacyWeakSigningKeySizeBits);
        }

        // THE FALSE IS LOAD BEARING. Exporting WITHOUT the private parameters is what makes both
        // public projections below structurally incapable of carrying a private component: they are
        // built from these parameters and never from the key object, so there is nothing to strip
        // afterwards and nothing that a later edit could accidentally leave in.
        RSAParameters publicParameters = _privateKey.ExportParameters(includePrivateParameters: false);

        _publicVerificationKey = new RsaSecurityKey(publicParameters)
        {
            KeyId = keyId,
        };

        _publishedKeySet = new PublishedJsonWebKeySet(
            new PublishedJsonWebKey(
                keyType: PublishedKeyType,
                use: PublishedKeyUse,
                algorithm: algorithm,
                keyId: keyId,
                modulus: EncodePublicComponent(publicParameters.Modulus),
                exponent: EncodePublicComponent(publicParameters.Exponent)));

        // The identifier is set ON THE KEY rather than restated by the issuer, which is what makes
        // the token header and the published key agree structurally: the minting library reads it
        // from here and stamps it into the header itself, so there is no second spelling to drift.
        _signingCredentials = new SigningCredentials(
            new RsaSecurityKey(_privateKey)
            {
                KeyId = keyId,
            },
            algorithm);
    }

    /// <summary>
    /// The RSA modulus size, in bits, of the signing key this instance imported.
    /// </summary>
    /// <value>The measured modulus size. Always positive, because a key that did not import throws.</value>
    /// <remarks>
    /// NOT A SECRET, WHICH IS WHY IT MAY BE PUBLISHED AND LOGGED. The modulus itself is served
    /// anonymously in the key set at <c>/.well-known/jwks.json</c>, so its length discloses nothing an
    /// unauthenticated caller could not already read. It is exposed so that a deployment, a diagnostic
    /// and a test can all see the same number the construction-time warning names, rather than each
    /// re-deriving it from the key.
    /// </remarks>
    public int SigningKeySizeBits { get; }

    /// <summary>
    /// Whether the signing key's modulus is shorter than
    /// <see cref="SecurityOptions.LegacyWeakSigningKeySizeBits"/>, and is therefore reported as a
    /// preserved legacy weakness.
    /// </summary>
    /// <value>
    /// <see langword="true"/> when the measured modulus is below the annotation threshold; otherwise
    /// <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// AN ANNOTATION, NEVER A REFUSAL. A key this flags is still imported, still paired with its
    /// algorithm and still used to mint every token this service issues: AAP 0.6.6.4 keeps 1024-bit RSA
    /// a legal size across this estate and requires each weak cryptographic default to be replicated as
    /// an annotated default rather than corrected (C-B). Construction logs a warning naming the measured
    /// size when this is <see langword="true"/>, and <c>docs/SECRETS.md</c> 4.1 states the same position
    /// in prose. Nothing in this service branches on it beyond that warning, and nothing may be added
    /// that turns it into a rejection.
    /// </remarks>
    public bool SigningKeyIsLegacyWeak { get; }

    /// <summary>
    /// The credential the token issuer signs with: the private key paired with the resolved
    /// algorithm. THE ONLY MEMBER THAT CARRIES PRIVATE KEY MATERIAL.
    /// </summary>
    /// <value>The signing credential. Never <see langword="null"/>.</value>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// IN-PROCESS ONLY, AND FOR ONE CALLER. It must never be serialized, logged, returned from an
    /// endpoint, included in a diagnostic or placed on any wire. Its sole legitimate consumer is the
    /// token issuer, which is reachable only through an authenticated route, so the private half of
    /// the key never leaves this process by any path.
    /// </para>
    /// <para>
    /// Its key carries the configured key identifier, which is why nothing downstream has to restate
    /// the identifier in a token header. Verify the pairing rather than trusting it: a signature made
    /// with this credential validates against <see cref="PublicVerificationKey"/> and against the
    /// modulus and exponent in <see cref="PublishedKeySet"/>, which is the assertion that proves the
    /// three projections are one key pair.
    /// </para>
    /// </remarks>
    public SigningCredentials SigningCredentials
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _signingCredentials;
        }
    }

    /// <summary>
    /// The public half of the signing key, for validating tokens this service minted.
    /// </summary>
    /// <value>A public-only key whose private-key indicator is <see langword="false"/>.</value>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// PUBLIC BY CONSTRUCTION RATHER THAN BY REDACTION. It is built from parameters exported with the
    /// private components EXCLUDED, so the private exponent and the prime factors are absent from the
    /// object rather than present and unpublished. That is what makes the guarantee checkable: there
    /// is nothing to leak, not merely nothing exposed.
    /// </para>
    /// <para>
    /// This is what the service's own inbound bearer handler is given, IN PROCESS. Security validates
    /// tokens it minted itself, so pointing that handler at this service's own discovery document
    /// would make startup fetch metadata from the service that is starting - which cannot succeed in
    /// a cold container and would leave this service unable to satisfy the readiness gate its
    /// dependents wait on. The other three services do use the published document over HTTP, and that
    /// asymmetry is deliberate.
    /// </para>
    /// </remarks>
    public SecurityKey PublicVerificationKey
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _publicVerificationKey;
        }
    }

    /// <summary>
    /// The key set published anonymously as this service's verification material. Public parameters
    /// only.
    /// </summary>
    /// <value>An immutable projection carrying exactly one key.</value>
    /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// SERIALIZES TO EXACTLY SIX MEMBERS PER KEY - the key family, the intended use, the algorithm,
    /// the identifier, the modulus and the exponent - because the projection type DECLARES only those
    /// six. There is no private or symmetric member to omit, so none can be published by an oversight
    /// in a serializer setting, and the published contract's key schema admits no additional member
    /// either.
    /// </para>
    /// <para>
    /// EXACTLY ONE KEY, DELIBERATELY. The document's shape admits more than one so that a rollover is
    /// EXPRESSIBLE, and a verifier selects by identifier; publishing more would require rotation
    /// machinery, which this phase does not add. One configured key therefore yields one entry.
    /// </para>
    /// <para>
    /// The same immutable instance is returned on every access. That is safe precisely because it is
    /// immutable: no caller can enrich it, reorder it or add a member to it, so there is nothing a
    /// shared instance could leak that a fresh copy would not.
    /// </para>
    /// </remarks>
    public PublishedJsonWebKeySet PublishedKeySet
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return _publishedKeySet;
        }
    }

    /// <summary>
    /// Releases the imported private key.
    /// </summary>
    /// <remarks>
    /// The platform releases and clears the key material the handle holds, which is key hygiene
    /// rather than a behavioural concern. Idempotent, and safe to call from the container at host
    /// shutdown. Afterwards every member raises <see cref="ObjectDisposedException"/> rather than
    /// handing out a key whose handle has gone.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _privateKey.Dispose();
    }

    /// <summary>
    /// Requires a non-blank key identifier.
    /// </summary>
    /// <param name="keyId">The configured identifier.</param>
    /// <returns>The identifier, VERBATIM.</returns>
    /// <exception cref="InvalidOperationException">The identifier is blank.</exception>
    /// <remarks>
    /// Blank includes whitespace only, because a whitespace identifier would be published and stamped
    /// as though it were an identity. RETURNED UNTRIMMED, deliberately: every other reader of this
    /// setting gets the configured value, and a value this method silently reshaped would be a second,
    /// disagreeing spelling of the one thing the published key and the token header must match on.
    /// </remarks>
    private static string ResolveKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new InvalidOperationException(SigningKeyIdAbsentMessage);
        }

        return keyId;
    }

    /// <summary>
    /// Resolves the configured algorithm against the closed set this issuer signs with.
    /// </summary>
    /// <param name="algorithm">The configured algorithm identifier.</param>
    /// <returns>The identifier, which is also the value published as the key's algorithm.</returns>
    /// <exception cref="InvalidOperationException">
    /// The identifier is not one of the three permitted values.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A TOTAL SWITCH OVER THREE CONSTANTS, so the check and
    /// <see cref="SigningAlgorithmUnsupportedMessage"/> are written from the same three symbols and
    /// cannot describe different sets. Matching is ordinal and case-sensitive, which is what a switch
    /// over string constants already is, and it is correct rather than merely convenient: these
    /// identifiers are case-sensitive, and admitting a differently cased spelling would publish an
    /// algorithm identifier no stock verifier recognises.
    /// </para>
    /// <para>
    /// NOT TRIMMED, for the same reason the identifier is not: the options validator compares the
    /// configured value as given, so trimming here would let this type accept a value that validation
    /// refuses and put two files in one service at odds about their own contract.
    /// </para>
    /// <para>
    /// The three members are the closed set the options contract publishes, and they correspond to the
    /// three digests the oracle's own comment names as its signing primitive's argument set
    /// [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L927</c>, values at <c>:L930-L932</c>]. Everything
    /// else falls to the refusal arm - the keyed-hash family structurally, the probabilistic family
    /// for want of a legacy identifier, the elliptic-curve family for want of a legacy primitive, and
    /// an unsigned token because an issuer that does not sign is not an issuer.
    /// </para>
    /// </remarks>
    private static string ResolveSigningAlgorithm(string algorithm) =>
        algorithm switch
        {
            SecurityAlgorithms.RsaSha256 => SecurityAlgorithms.RsaSha256,
            SecurityAlgorithms.RsaSha384 => SecurityAlgorithms.RsaSha384,
            SecurityAlgorithms.RsaSha512 => SecurityAlgorithms.RsaSha512,
            _ => throw new InvalidOperationException(SigningAlgorithmUnsupportedMessage),
        };

    /// <summary>
    /// Requires signing material to be present.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>The material, unchanged and unexamined.</returns>
    /// <exception cref="InvalidOperationException">
    /// The material is absent, empty or whitespace only.
    /// </exception>
    /// <remarks>
    /// The absent case is reported separately from the unusable case because the fixes differ: absent
    /// means the configuration key was never populated, whereas unusable means it was populated with
    /// the wrong kind of thing. Collapsing them into one message would send an operator to the wrong
    /// place. Nothing here inspects, measures, trims or copies the value.
    /// </remarks>
    private static string RequireSigningMaterial(string? material)
    {
        if (string.IsNullOrWhiteSpace(material))
        {
            throw new InvalidOperationException(SigningKeyAbsentMessage);
        }

        return material;
    }

    /// <summary>
    /// Imports the configured material as an asymmetric private key, in the accepted encodings and in
    /// their fixed order.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>A key the caller owns and must dispose.</returns>
    /// <exception cref="InvalidOperationException">
    /// The material cannot be imported in any accepted encoding, or it imported without a private
    /// component.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ACCEPTED SET IS FIXED IN CODE AND IS NOT SELECTABLE BY CONFIGURATION. Every path is
    /// attempted and only material that fails all of them is refused, so there is no setting that
    /// could tell this method to expect the wrong shape and reject a valid key. The order mirrors the
    /// options validator exactly, so nothing that passes validation is refused here.
    /// </para>
    /// <para>
    /// ARMOURED TEXT IS ATTEMPTED FIRST, and its success is not sufficient: armoured import reads
    /// PUBLIC material happily, so that path additionally asserts a private component. The two bare
    /// binary structures need no such assertion, because both are private-key structures by
    /// definition - public structures are deliberately not attempted at all, since the question this
    /// method answers is "can this sign?" and a public key cannot.
    /// </para>
    /// <para>
    /// The bare order is evidence-led: the generation command the orchestration template documents
    /// produces the algorithm-tagged structure, so that is attempted first and the overwhelmingly
    /// common case costs one attempt; the older key-specific structure is attempted second because an
    /// existing key may already be in it.
    /// </para>
    /// <para>
    /// KEY HYGIENE. The decoded buffer holds the key in the clear and is ZEROED in a finally block
    /// whichever way the attempt went, rather than being left for collection - which is
    /// non-deterministic and may copy the buffer while compacting. The lambdas are static so that no
    /// key material can be captured in a closure. No failure names the material, and no platform
    /// exception is chained.
    /// </para>
    /// </remarks>
    private static RSA ImportPrivateKey(string material)
    {
        RSA? armoured = TryImportArmoured(material);

        if (armoured is not null)
        {
            if (HasPrivateComponent(armoured))
            {
                return armoured;
            }

            armoured.Dispose();

            throw new InvalidOperationException(SigningKeyPublicOnlyMessage);
        }

        byte[]? bare = TryDecodeBase64(material);

        if (bare is null)
        {
            // Neither armoured nor valid base64, so there is no third encoding left to attempt.
            throw new InvalidOperationException(SigningKeyUnusableMessage);
        }

        try
        {
            RSA? imported =
                TryImportBareStructure(bare, static (key, bytes) => key.ImportPkcs8PrivateKey(bytes, out _)) ??
                TryImportBareStructure(bare, static (key, bytes) => key.ImportRSAPrivateKey(bytes, out _));

            return imported ?? throw new InvalidOperationException(SigningKeyUnusableMessage);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bare);
        }
    }

    /// <summary>
    /// Attempts the armoured encoding.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>
    /// A key when the material is armoured and readable, or <see langword="null"/> when it is not
    /// armoured at all. A readable key may still be public only, which the caller checks.
    /// </returns>
    /// <remarks>
    /// NO DELIMITER LITERAL IS TESTED FOR AND NONE APPEARS ANYWHERE IN THIS FILE. The attempt is
    /// simply made and its failure interpreted, which is both simpler than sniffing for a header and
    /// deliberate: a delimiter string is exactly what a credential scanner matches on, so writing one
    /// here would trip the scan that protects this file for no functional gain. Two exception types
    /// are treated alike because the platform reports unreadable content as a cryptographic failure
    /// and text carrying no usable block as an argument failure, and both mean the same thing to this
    /// caller. The rejected candidate is disposed on the spot, so no partially initialised key is
    /// left behind for the next attempt to inherit.
    /// </remarks>
    private static RSA? TryImportArmoured(string material)
    {
        RSA candidate = RSA.Create();

        try
        {
            candidate.ImportFromPem(material);

            return candidate;
        }
        catch (Exception exception)
            when (exception is CryptographicException or ArgumentException)
        {
            candidate.Dispose();

            return null;
        }
    }

    /// <summary>
    /// Attempts exactly one bare binary private-key structure.
    /// </summary>
    /// <param name="bare">The decoded key bytes.</param>
    /// <param name="import">The single platform import to attempt.</param>
    /// <returns>
    /// A key when the bytes hold that structure; otherwise <see langword="null"/> so the caller can
    /// attempt the next one.
    /// </returns>
    /// <remarks>
    /// Returns a verdict rather than raising, so exception control flow does not span the whole set.
    /// EXACTLY ONE EXCEPTION TYPE IS HANDLED because that is what the platform raises here: both bare
    /// imports report every rejection - random bytes, empty input, a public-key structure and the
    /// other private structure's encoding - as a cryptographic failure or a platform-specific subclass
    /// of it, which this catch covers. No arm is written for an exception the platform does not raise
    /// on this path: an unreachable catch is dead code that cannot be tested and quietly claims to
    /// handle something. Anything genuinely unexpected propagates, which still refuses the host.
    /// </remarks>
    private static RSA? TryImportBareStructure(byte[] bare, Action<RSA, byte[]> import)
    {
        RSA candidate = RSA.Create();

        try
        {
            import(candidate, bare);

            return candidate;
        }
        catch (CryptographicException)
        {
            candidate.Dispose();

            return null;
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
    /// Asked by attempting the private export and interpreting its failure, which is the platform's
    /// own answer to the question rather than an inference drawn from key parameters. The exported
    /// buffer is key material in the clear and is therefore ZEROED in a finally block whichever way
    /// the attempt went; it is never returned, inspected, measured or logged, and exists solely to be
    /// discarded. This method's entire vocabulary is <see langword="true"/> and
    /// <see langword="false"/>.
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
    /// Decodes base64 signing material, answering <see langword="null"/> rather than raising when the
    /// text is not base64 at all.
    /// </summary>
    /// <param name="material">The configured signing material.</param>
    /// <returns>The decoded bytes, or <see langword="null"/> when the text is not valid base64.</returns>
    /// <remarks>
    /// The platform decoder tolerates embedded whitespace and line breaks, so material that arrived
    /// wrapped across lines by a secret store or an editor still decodes. A failure here is not an
    /// error to report on its own: it simply means this encoding is not the one, and the caller turns
    /// it into the single fixed refusal that names no part of the value.
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

    /// <summary>
    /// Encodes one PUBLIC key component for publication.
    /// </summary>
    /// <param name="component">The exported public component.</param>
    /// <returns>The component, base64url encoded without padding.</returns>
    /// <exception cref="InvalidOperationException">
    /// The component is missing or empty, which would mean the exported public key is structurally
    /// incomplete.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS ENCODING IS A WIRE-FORMAT REQUIREMENT OF THE PUBLISHED CONTRACT AND IS NOT THE LEGACY
    /// ENCODING SELECTOR. The key-set format specifies base64url WITHOUT padding for these members
    /// and the contract document restates it
    /// [<c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>, schema <c>JsonWebKey</c>], so
    /// the platform's url-safe encoder is used rather than the ported encoding surface. The legacy
    /// base64 selector [<c>ws_objects/pfw.shared.pbl.src/enums.sru:L924</c>] parameterises the
    /// cryptographic surface's payload conversions and answers a different question; conflating the
    /// two would publish padded, non-url-safe members that a stock consumer cannot read.
    /// </para>
    /// <para>
    /// ONLY PUBLIC COMPONENTS EVER REACH THIS METHOD - it is called with the modulus and the exponent
    /// of a key exported WITHOUT its private parameters, so there is no private component in scope to
    /// encode by mistake.
    /// </para>
    /// <para>
    /// The emptiness check exists because the exported members are declared as optional by the
    /// platform's parameter structure, which a default-constructed instance leaves unset. Publishing an
    /// empty member would produce a key set that a consumer accepts and can never verify against, so
    /// it is refused with the same fixed message as any other unusable material rather than papered
    /// over with a null-forgiving assertion.
    /// </para>
    /// </remarks>
    private static string EncodePublicComponent(byte[]? component)
    {
        if (component is null || component.Length == 0)
        {
            throw new InvalidOperationException(SigningKeyUnusableMessage);
        }

        return Base64Url.EncodeToString(component);
    }
}

/// <summary>
/// The published key set: this service's verification material, public parameters only.
/// </summary>
/// <remarks>
/// <para>
/// DECLARED IN THIS FILE ON PURPOSE. This folder holds exactly two files - this one and the token
/// issuer - so a projection type that exists only to shape what the key-set route returns is declared
/// beside the type that produces it rather than given a file of its own. It carries no behaviour.
/// </para>
/// <para>
/// SERIALIZES TO EXACTLY ONE MEMBER, <c>keys</c>, matching the published contract's set schema
/// [<c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>, schema <c>JsonWebKeySet</c>],
/// which requires that member and permits no other. WHERE THIS TYPE AND THAT DOCUMENT EVER DISAGREE,
/// THE DOCUMENT WINS AND THIS TYPE CHANGES, because the document is what a consumer's stock handler
/// reads.
/// </para>
/// <para>
/// IMMUTABLE, AND THAT IS A SECURITY PROPERTY RATHER THAN A STYLE CHOICE. The member is assigned once
/// during construction and the collection type admits no mutation at all, so no caller can add a key,
/// replace one, or enrich the set with a member it does not declare. The constructor is internal and
/// has exactly one call site, in the signing-key provider, so a set can only ever be produced from a
/// key that provider derived from public parameters.
/// </para>
/// </remarks>
public sealed class PublishedJsonWebKeySet
{
    /// <summary>
    /// Wraps one published key as a key set.
    /// </summary>
    /// <param name="key">The single published key.</param>
    /// <remarks>
    /// No argument guard, deliberately and not by omission: this constructor is internal, it has one
    /// call site, and that call site passes a key the provider has just built from validated
    /// configuration. A guard here could not be reached by supplying configuration, so it would be
    /// unreachable ceremony rather than error handling.
    /// </remarks>
    internal PublishedJsonWebKeySet(PublishedJsonWebKey key)
    {
        Keys = [key];
    }

    /// <summary>
    /// The published keys. Public parameters only.
    /// </summary>
    /// <value>
    /// Exactly one key in this phase. The set's shape admits more so that a rollover is EXPRESSIBLE
    /// and a consumer can select by identifier, but rotation is not implemented and one configured key
    /// yields one entry.
    /// </value>
    [JsonPropertyName("keys")]
    public ImmutableArray<PublishedJsonWebKey> Keys { get; }
}

/// <summary>
/// One published verification key. PUBLIC PARAMETERS ONLY.
/// </summary>
/// <remarks>
/// <para>
/// THE PROHIBITION ON PRIVATE MATERIAL IS STRUCTURAL RATHER THAN ADVISORY. This type DECLARES no
/// private and no symmetric member: there is no private exponent, no prime factor, no exponent
/// remainder, no coefficient and no symmetric key value, and none may ever be added. Publishing any
/// one of them on an anonymous route would hand every caller the ability to mint tokens
/// indistinguishable from legitimate ones and would defeat the sole-issuer topology in a single
/// request. Because the members are absent from the type rather than merely unset, no serializer
/// setting, naming policy or future edit to a shared serialization option can surface one, and a
/// reviewer can check the property by reading this declaration.
/// </para>
/// <para>
/// The six members and their spellings are fixed by the published contract's key schema
/// [<c>shared/PowerFramework.Contracts/OpenApi/security.v1.yaml</c>, schema <c>JsonWebKey</c>], which
/// additionally forbids any member it does not declare. The optional permitted-operations member is
/// not published: a key set that carries no private component cannot be used to sign in the first
/// place, so stating that would restate what the absence of a private member already guarantees.
/// </para>
/// <para>
/// NO SPECIMEN VALUE APPEARS ON ANY MEMBER OF THIS TYPE, and none may be added. A plausible-looking
/// encoded blob in an example is indistinguishable from real key material to a reader, and specimen
/// keys have a long history of being copied into a deployment unchanged. The members are DESCRIBED
/// instead. That is the same position the contract document takes on the same schema.
/// </para>
/// <para>
/// IMMUTABLE, WITH AN INTERNAL CONSTRUCTOR, and deliberately not a record: a generated string
/// rendering or value comparison would be a second way for the contents to be read out, and while
/// these contents are public by definition, the type that produces them is not the place to establish
/// that habit.
/// </para>
/// </remarks>
public sealed class PublishedJsonWebKey
{
    /// <summary>
    /// Builds one published key from already-encoded public components.
    /// </summary>
    /// <param name="keyType">The key family.</param>
    /// <param name="use">The intended use.</param>
    /// <param name="algorithm">The algorithm identifier this key is intended for.</param>
    /// <param name="keyId">The key identifier.</param>
    /// <param name="modulus">The public modulus, already base64url encoded without padding.</param>
    /// <param name="exponent">The public exponent, already base64url encoded without padding.</param>
    /// <remarks>
    /// Internal with exactly one call site, in the signing-key provider, which is what guarantees
    /// every instance is derived from a key exported WITHOUT its private parameters. No argument guard
    /// is written for the same reason it is not written on the set: the single caller supplies values
    /// it has already validated, so a guard could not be reached by supplying configuration.
    /// </remarks>
    internal PublishedJsonWebKey(
        string keyType,
        string use,
        string algorithm,
        string keyId,
        string modulus,
        string exponent)
    {
        KeyType = keyType;
        Use = use;
        Algorithm = algorithm;
        KeyId = keyId;
        Modulus = modulus;
        Exponent = exponent;
    }

    /// <summary>
    /// The key family. This issuer publishes asymmetric keys of one family only.
    /// </summary>
    [JsonPropertyName("kty")]
    [JsonPropertyOrder(1)]
    public string KeyType { get; }

    /// <summary>
    /// The intended use, which is signature verification. An encryption key is never published here.
    /// </summary>
    [JsonPropertyName("use")]
    [JsonPropertyOrder(2)]
    public string Use { get; }

    /// <summary>
    /// The algorithm identifier this key is intended for, published so a consumer need not infer it
    /// from the key family.
    /// </summary>
    [JsonPropertyName("alg")]
    [JsonPropertyOrder(3)]
    public string Algorithm { get; }

    /// <summary>
    /// The key identifier, which the header of every minted token repeats so a consumer can select the
    /// right key. Opaque: it is not parsed and it carries no key material.
    /// </summary>
    [JsonPropertyName("kid")]
    [JsonPropertyOrder(4)]
    public string KeyId { get; }

    /// <summary>
    /// The PUBLIC modulus, base64url encoded without padding as the key-set format requires. Public by
    /// definition - it is one half of what makes signature verification possible.
    /// </summary>
    [JsonPropertyName("n")]
    [JsonPropertyOrder(5)]
    public string Modulus { get; }

    /// <summary>
    /// The PUBLIC exponent, base64url encoded without padding. Public by definition.
    /// </summary>
    [JsonPropertyName("e")]
    [JsonPropertyOrder(6)]
    public string Exponent { get; }
}
