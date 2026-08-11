// ==================================================================================================
//  JwksShapeTests.cs - THE LEAK CONTROL ON THE SECURITY SERVICE'S TWO ANONYMOUS PUBLICATION ROUTES
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  Endpoints/JwksEndpoints.cs publishes cryptographic material on two routes that require no
//  credential of any kind:
//
//      GET  <Security:JwksPath>                 default /.well-known/jwks.json
//      GET  <Security:OpenIdConfigurationPath>  default /.well-known/openid-configuration
//
//  This file is the automated check standing between a correct implementation of those two routes and
//  a PUBLISHED SIGNING KEY. Security is the SOLE token issuer in the whole decomposition: exactly one
//  signing secret exists anywhere in it, this service holds it, and Gateway, DataServices and
//  Persistence obtain trust through nothing but the two documents asserted below. A leak here would
//  therefore be SYSTEMIC rather than local - any caller able to reach the key set would be able to
//  mint tokens the other three services could not distinguish from legitimate ones.
//
//  WHAT IS ASSERTED, AND WHY EACH GROUP EXISTS
//
//    1. ANONYMITY, WITH A REFUTATION. Both routes answer with no Authorization header present. Each
//       anonymity row is paired with a refutation on the authenticated probe route using the SAME
//       client, because a default-deny fallback policy that had silently stopped applying would make
//       an unauthenticated 200 prove nothing at all. The full unauthorized matrix belongs to
//       AuthorizationTests.cs; the refutation here exists only to give these rows their force.
//
//    2. PUBLIC-ONLY MATERIAL, AS AN ALLOW LIST RATHER THAN A DENY LIST. The served key's member set
//       is asserted to be EXACTLY the six public members, and additionally to be a subset of the
//       member set the authored contract permits. An allow list is strictly stronger than a deny
//       list: it also catches a component nobody thought to name. The deny list is kept ANYWAY, as a
//       table-driven theory with one row per private component, because a row failure names the
//       component that leaked instead of reporting an opaque set difference.
//
//    3. AGREEMENT WITH THE ISSUER. The published identifier, the configured identifier and the
//       identifier stamped into a real minted token's header are asserted equal, and the published
//       modulus and exponent are used - after being FETCHED over the wire and decoded - to validate a
//       real minted token. A key set that publishes a different key from the one tokens are signed
//       with is silently unusable by every consumer, and no shape assertion alone can detect that.
//
//    4. DISCOVERY SUFFICIENCY, IN ITS BEHAVIOURAL FORM. A stock configuration manager - the same
//       retrieval stack Microsoft.AspNetCore.Authentication.JwtBearer uses internally - is pointed at
//       the discovery address and NOTHING ELSE, and the material it discovers on its own is then used
//       to validate a minted token. Asserting that is asserting the architectural decision itself:
//       Security is REST rather than gRPC precisely so a consumer writes ZERO bespoke retrieval code,
//       and choosing gRPC would have forced a hand-written key-set retrieval implementation into three
//       separate services - a net INCREASE in hand-written security-critical code.
//
//    5. THE AUTHORED CONTRACT'S SHAPES. shared/PowerFramework.Contracts/OpenApi/security.v1.yaml is
//       AUTHORITATIVE FOR THE WIRE. Where it and the implementation could differ, the YAML wins and
//       these rows assert the YAML: both schemas set additionalProperties to false, so the permitted
//       member sets below are closed sets rather than minimums, and the deliberate absence of the two
//       interactive-flow members from the discovery document is asserted as part of the contract
//       rather than left as prose.
//
//    6. THE FAILURE SHAPE. Both inconsistency describers and both handlers' failure paths are
//       exercised directly, and the published problem is asserted to be the SHARED
//       application/problem+json shape carrying the legacy return code - the factory owned by
//       Endpoints/PingEndpoints.cs - rather than a second error shape invented here.
//
//  ==================================================================================================
//  CONSTRAINT COMPLIANCE - WHAT EACH GOVERNING CONSTRAINT REQUIRES OF THIS FILE SPECIFICALLY
//  ==================================================================================================
//
//  RULES POSITION. The project's rules document contains exactly one line: NO USER RULES WERE
//  PROVIDED. Nothing is invented, inferred or back-filled from convention in their place, and their
//  absence is not read as licence to lower the bar. The enterprise-standard baseline applies instead:
//  warning-clean under warnings-as-errors, correct nullable annotations with no null-forgiving
//  suppression anywhere below, no secret in source, no package added, every disposable disposed, and
//  no performance property asserted because the repository publishes none.
//
//  C-F  NOTHING HARDCODED, AND THE NAMED SECRET SITES ARE A FLOOR RATHER THAN A CEILING. Two distinct
//       obligations land on this file.
//
//       The ordinary one: the key the host under test publishes is GENERATED EPHEMERALLY by
//       SecurityAppFactory, and the key the direct-call rows use is generated by that same factory's
//       generator. There is NO key literal, no armoured block, no encoded key run, no passphrase, no
//       initialization vector and no token anywhere below, and none of the eight in-source sites
//       inventoried in docs/SECRETS.md is reproduced in any form - not the armoured private key at
//       tests/blink/test_jws.htm:L8-L22, not the two encoded private keys at
//       ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718, and not the
//       symmetric configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23.
//       Those are LOCATORS. Their contents are not here and must never be pasted here.
//
//       The one unique to this file: THESE ASSERTIONS ARE THEMSELVES A C-F CONTROL. They are what
//       prevents this service from BECOMING a secret-publishing site, so they are written in the
//       strongest form available - an allow list over the actually-parsed members, a per-component
//       theory, a raw-text scan for the armour markers, and a decode-and-import probe that refuses
//       BOTH encodings the signing-key provider itself accepts.
//
//       LOG HYGIENE IS PART OF THE SAME OBLIGATION AND IS ENFORCED THROUGHOUT. No assertion below can
//       print a fetched document, a member value, or any decoded byte into test output: wherever the
//       subject of an assertion could carry key material the row uses a boolean assertion with a
//       hand-written STRUCTURAL message, never an equality or containment assertion that would render
//       the actual value into the failure report. Equality assertions appear only where both sides are
//       non-secret by construction - a member NAME, a status code, a media type, a route path, a key
//       IDENTIFIER, an algorithm identifier or an issuer identifier. A control that leaked the material
//       it guards into a continuous-integration log would defeat itself.
//
//  C-G  NO NEW ATTACK SURFACE: EVERY NEW BOUNDARY AUTHENTICATED. The two routes asserted here are
//       anonymous BY DESIGN and this file asserts that anonymity AS CORRECT. Verification material is
//       public by definition - publishing it is what allows a signature to be checked, and that is
//       precisely what distinguishes it from a signing key - and a consumer's bearer handler fetches
//       both addresses IN ORDER TO LEARN HOW TO AUTHENTICATE, so it cannot already hold a token. There
//       is therefore deliberately NO row here demanding either route be authenticated: such a row
//       would break every stock consumer. What IS asserted instead is that what they publish is
//       public-only. Neither route is used below as a way to reach anything else unauthenticated.
//
//  C-C  THE LEGACY TREE IS READ ONLY AND IS THE BEHAVIOURAL ORACLE. Every legacy path in this file
//       appears in a comment and ONLY in a comment. Nothing below opens, reads, copies or
//       fixture-loads any path under ws_objects/ or under the pre-existing browser-harness
//       directories in tests/, at construction time or at request time.
//
//       tests/blink/test_jws.htm:L8-L23 is cited for one reason: it is THE ANTI-PATTERN THESE
//       ASSERTIONS EXIST TO PREVENT FROM RECURRING ON A SERVER ROUTE. That page carries a plaintext
//       armoured RSA private key inline and signs a web token with it in the browser, so the key that
//       verifies its output is the key that produced it and both are in version control forever. It is
//       a real in-repository secret site, its remediation posture is never-replicate rather than
//       remove-from-source, and it is neither opened nor emulated nor quoted here.
//
//       The cryptographic provenance is likewise cited rather than consumed:
//         * ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73 - the signature pair this issuer is
//           built on. RSASign takes the PRIVATE key and VerifyRSASign takes the PUBLIC one, and RSA
//           key generation returns the two halves separately at :L19-L20. Publishing only the public
//           half is the direct continuation of that separation rather than a new policy.
//         * ws_objects/pfw.shared.pbl.src/enums.sru:L927,L930 - the comment at :L927 records that one
//           digest set governs the hash primitive, RSASign AND VerifyRSASign, and its SHA-256 member
//           sits at :L930. That is the settled provenance for this issuer's algorithm identifier, and
//           the corresponding constant is CONSUMED from the shared kernel below rather than re-spelled.
//         * ws_objects/pfw.pbl.src/pfw.sra:L111-L144 - the fail-fast posture, which ends in HALT CLOSE
//           at :L143. Its .NET continuation is why a structural fault in the signing material is a
//           refusal to start rather than a degraded key set, and why no row below expects a partial or
//           placeholder document.
//
//  C-B  NO BEHAVIOUR IMPROVEMENTS. The contract is asserted AS SPECIFIED. There is deliberately no row
//       demanding key rotation, more than one key in the set, a cache directive or response header of
//       any kind, or a stronger algorithm: none is in scope, and asserting one would pull unrequested
//       behaviour into a security-critical service. RS256 with a single identifier IS the contract.
//       Member ORDER is likewise not asserted: JSON object member order carries no meaning for any
//       consumer, so pinning it would convert a harmless edit into a failure.
//
//  C-D  NOTHING FOR ANY DEFERRED SERVICE. No key, identifier, audience, route or discovery member for
//       DesignSystem, Documents, Integration or ScriptBridge appears below, and none may: no signing,
//       verification or mutual-TLS material is scaffolded for any deferred service, so none may appear
//       in the published metadata either. The discovery rows assert the member set exactly, which is
//       what makes that absence checkable rather than merely intended.
//
//  C-L  THE ENVIRONMENT'S DOCUMENTED OPERATIONAL CONTRACT IS BINDING. The two addresses are the
//       documented mechanism by which Gateway, DataServices and Persistence obtain verification
//       material, so the DOCUMENTED paths are asserted rather than convenient alternatives: each path
//       is read from configuration AND its value is compared against the well-known address a stock
//       bearer handler looks for, because a handler pointed at an authority derives the discovery
//       address by fixed convention and will not find a renamed one.
//
//  C-H  80 PERCENT LINE COVERAGE PER SERVICE, MEASURED FROM COBERTURA. This file's share is
//       Endpoints/JwksEndpoints.cs IN FULL - the registration method's path guards, both request
//       handlers on both their success and failure paths, both inconsistency describers across every
//       branch, the allow-list projection, and all three response shapes - together with
//       Tokens/SigningKeyProvider.cs's public-only projection path.
//
//  C-I / C-A  INDEPENDENT BUILD, NO CROSS-SERVICE COUPLING. This file passes under the verbatim
//       per-service workflow - restore, then build in the release configuration, then test collecting
//       cross-platform coverage - from this service's directory with no sibling service, no shared
//       solution and no orchestration present. No solution filter is authored. Only this service's own
//       types are referenced, plus the shared kernel that arrives transitively. THE HOST IS BOUND TO
//       THE IN-MEMORY TRANSPORT AND TO NOTHING ELSE: no listening address and no port number appears
//       below, so this service's real port cannot be contended for.
//
//  .editorconfig  NO PRESERVED-SPELLING IDENTIFIER IS DECLARED HERE. The repository's naming
//       suppressions are file-glob scoped to the production files that genuinely carry the legacy
//       upper-case-with-underscores constants, and NO test file is in that scope. With warnings as
//       errors a preserved-spelling declaration here would be a build error rather than a style nit.
//       Where such a constant is meant it is CONSUMED from PowerFramework.Shared.Kernel and never
//       re-declared.
// ==================================================================================================

using System.Buffers.Text;
using System.Collections.Immutable;
using System.Net;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Asserts the shape, the anonymity, the issuer agreement and the discovery sufficiency of the two
/// <c>/.well-known/</c> publications, and asserts that neither one can publish private or symmetric key
/// material.
/// </summary>
/// <remarks>
/// <para>
/// SCOPE, AND HOW IT DIFFERS FROM THE TWO ADJACENT SUITES. This suite's subject is the SERVED DOCUMENT:
/// what a consumer actually receives on the wire from a booted host, plus the projection and the failure
/// paths that produce it. <c>SigningKeyProviderTests.cs</c> asserts the same public-only property one
/// layer lower, on the provider's projection in isolation and on its refusal to construct from unusable
/// material. <c>TokenEndpointsTests.cs</c> asserts issuance itself across the transport-credential seam.
/// The three overlap deliberately at exactly one point - that a minted token verifies against published
/// material - because that property is the one whose failure is invisible to every other assertion in
/// the service.
/// </para>
/// <para>
/// EVERY ROW BOOTS ITS OWN HOST. No state is shared between rows, so a row cannot pass because an
/// earlier one left the container in a convenient condition, and each row's ephemeral key is its own.
/// The host is reached only over the in-memory transport.
/// </para>
/// </remarks>
public sealed class JwksShapeTests
{
    // ----------------------------------------------------------------------------------------------
    //  WIRE MEMBER NAMES. Spelled once here, consumed everywhere below. These are JSON names fixed by
    //  the standards a stock handler reads and by OpenApi/security.v1.yaml, not C# names - which is
    //  also why they are held in conventionally named constants rather than as identifiers.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The key-set document's only member [<c>OpenApi/security.v1.yaml</c> JsonWebKeySet].</summary>
    private const string KeysMember = "keys";

    /// <summary>The published key family member.</summary>
    private const string KeyFamilyMember = "kty";

    /// <summary>The published intended-use member.</summary>
    private const string KeyUseMember = "use";

    /// <summary>The published algorithm member.</summary>
    private const string AlgorithmMember = "alg";

    /// <summary>The published key-identifier member, which a token header repeats.</summary>
    private const string KeyIdentifierMember = "kid";

    /// <summary>The published RSA public modulus member.</summary>
    private const string ModulusMember = "n";

    /// <summary>The published RSA public exponent member.</summary>
    private const string ExponentMember = "e";

    /// <summary>
    /// The permitted-operations member, which the authored contract ALLOWS but this issuer does not
    /// emit.
    /// </summary>
    /// <remarks>
    /// Named so the distinction between the contract's permitted set and this issuer's emitted set is
    /// explicit rather than accidental. It is optional in the schema, so its absence conforms; the
    /// allow-list row below therefore admits it as permitted while the exact-set row pins that this
    /// issuer omits it.
    /// </remarks>
    private const string KeyOperationsMember = "key_ops";

    /// <summary>The discovery document's issuer-identifier member.</summary>
    private const string IssuerMember = "issuer";

    /// <summary>The discovery document's key-set address member.</summary>
    private const string KeySetAddressMember = "jwks_uri";

    /// <summary>The discovery document's issuance-address member.</summary>
    private const string TokenEndpointMember = "token_endpoint";

    /// <summary>The discovery document's signature-algorithm list member.</summary>
    private const string SigningAlgorithmsMember = "id_token_signing_alg_values_supported";

    /// <summary>The symmetric key value member, which must never appear in either document.</summary>
    /// <remarks>
    /// This is the single most dangerous member name in the standard: it carries the key ITSELF rather
    /// than a public parameter of one. It is named here so the assertions that refuse it can be read as
    /// refusing it deliberately.
    /// </remarks>
    private const string SymmetricKeyValueMember = "k";

    // ----------------------------------------------------------------------------------------------
    //  PUBLISHED VALUES THAT ARE PART OF THE CONTRACT
    // ----------------------------------------------------------------------------------------------

    /// <summary>The only key family this issuer publishes.</summary>
    private const string AsymmetricKeyFamily = "RSA";

    /// <summary>
    /// The symmetric key family, which must never be published, used only as the value a row proves is
    /// unrepresentable.
    /// </summary>
    private const string SymmetricKeyFamily = "oct";

    /// <summary>The only intended use this issuer publishes.</summary>
    private const string SignatureUse = "sig";

    // ----------------------------------------------------------------------------------------------
    //  DOCUMENTED ADDRESSES (C-L). Values, not secrets: a stock bearer handler pointed at an authority
    //  derives the discovery address by fixed convention, so a renamed path is unreachable however
    //  correctly it is configured. Each is compared against the configured option rather than used in
    //  place of it.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The documented key-set address.</summary>
    private const string DocumentedKeySetPath = "/.well-known/jwks.json";

    /// <summary>The documented discovery address a stock bearer handler looks for exactly.</summary>
    private const string DocumentedMetadataPath = "/.well-known/openid-configuration";

    /// <summary>
    /// The authenticated probe route, used ONLY as the refutation that the default-deny fallback policy
    /// is live.
    /// </summary>
    /// <remarks>
    /// The complete unauthorized matrix belongs to <c>AuthorizationTests.cs</c>. This constant exists so
    /// each anonymity row can show that the identical credential-free client IS refused elsewhere;
    /// without that pairing an anonymous 200 would be consistent with authorization having stopped
    /// applying altogether.
    /// </remarks>
    private const string AuthenticatedProbePath = "/v1/ping";

    // ----------------------------------------------------------------------------------------------
    //  INPUTS FOR THE DIRECT-CALL ROWS. Every one is a NAME or an IDENTIFIER; none is key material.
    //  The signing material itself is always generated, never written.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The modulus size generated for the direct-call rows, in bits.</summary>
    /// <remarks>
    /// Matches the size the in-process host factory generates, so a direct-call row and a served-document
    /// row exercise the same shape of material. It is not a re-declaration of any deployment floor.
    /// </remarks>
    private const int ProbeKeySizeInBits = 2048;

    /// <summary>An absolute issuer identifier for the direct-call rows.</summary>
    /// <remarks>
    /// Uses the reserved <c>.invalid</c> top-level domain, which can never resolve, so no row can
    /// accidentally reach a network. It carries no port, so this file names no port at all.
    /// </remarks>
    private const string ProbeIssuer = "https://security.invalid";

    /// <summary>
    /// The scheme separator <see cref="ProbeIssuer"/> begins with.
    /// </summary>
    /// <remarks>
    /// Skipped before looking for a doubled path separator, because the scheme's own <c>//</c> is not one.
    /// </remarks>
    private const string SchemeSeparator = "https://";

    /// <summary>A doubled path separator: what a mis-joined address carries and a correct one does not.</summary>
    private const string DoubledSeparator = "//";

    /// <summary>An audience identity for the direct-call rows.</summary>
    private const string ProbeAudience = "powerframework-jwks-shape";

    /// <summary>The key identifier the direct-call rows configure and publish.</summary>
    private const string ProbeKeyIdentifier = "jwks-shape-probe-1";

    /// <summary>
    /// A DIFFERENT key identifier, used to make the published set and the configured set disagree.
    /// </summary>
    private const string DivergentKeyIdentifier = "jwks-shape-probe-2";

    /// <summary>A value that is not an absolute address, for the metadata-fault rows.</summary>
    private const string RelativeIssuerValue = "security-service";

    /// <summary>A value that is not a rooted path, for the metadata-fault rows.</summary>
    private const string UnrootedPathValue = "well-known/jwks.json";

    /// <summary>
    /// The value used where a published key's public parameter must be present but its contents are
    /// irrelevant: the base64url encoding of the three bytes of the conventional RSA public exponent.
    /// </summary>
    /// <remarks>
    /// USED ONLY BY THE DIRECT-CALL STRUCTURAL ROWS, and chosen precisely because it is not key material
    /// in any meaningful sense: the public exponent is a fixed, universally known value that every RSA
    /// key in ordinary use shares, and it is a PUBLIC parameter in the first place. Nothing below signs,
    /// verifies or imports with it - the rows that use it are asserting which member NAMES a projection
    /// can emit, and a projection is indifferent to the contents of the strings it copies. Generating a
    /// real key for those rows would be slower and would prove nothing extra.
    /// </remarks>
    private const string PlaceholderPublicComponent = "AQAB";

    // ----------------------------------------------------------------------------------------------
    //  FAULT NAMES for the metadata-inconsistency theory. Serializable strings rather than delegates,
    //  because a non-serializable theory data type is a build error under warnings-as-errors.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Fault: the issuer identifier is blank.</summary>
    private const string BlankIssuerFault = "BlankIssuer";

    /// <summary>Fault: the issuer identifier is not an absolute address.</summary>
    private const string RelativeIssuerFault = "RelativeIssuer";

    /// <summary>Fault: the key-set path is not rooted.</summary>
    private const string UnrootedKeySetPathFault = "UnrootedKeySetPath";

    /// <summary>Fault: the issuance path is not rooted.</summary>
    private const string UnrootedTokenEndpointPathFault = "UnrootedTokenEndpointPath";

    // ----------------------------------------------------------------------------------------------
    //  MEMBER SETS. Held as static readonly arrays rather than written inline at each call site, which
    //  keeps the sets single-sourced and keeps the analyzer's constant-array guidance satisfied.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The EXACT member set this issuer publishes on a key: six public members and nothing else.
    /// </summary>
    /// <remarks>
    /// This is the allow list. Asserting the served set EQUALS this set is strictly stronger than
    /// asserting the absence of a list of private members, because it also refuses a member nobody
    /// thought to name.
    /// </remarks>
    private static readonly ImmutableArray<string> PublishedKeyMembers =
    [
        KeyFamilyMember,
        KeyUseMember,
        AlgorithmMember,
        KeyIdentifierMember,
        ModulusMember,
        ExponentMember,
    ];

    /// <summary>
    /// The member set the authored contract PERMITS on a key
    /// [<c>OpenApi/security.v1.yaml</c> JsonWebKey, <c>additionalProperties: false</c>].
    /// </summary>
    /// <remarks>
    /// A CLOSED set rather than a minimum, because the schema refuses additional members. It is the
    /// six above plus the optional permitted-operations member. Asserting the served set is a subset of
    /// this one is the contract-conformance half; asserting it equals the six is the implementation half.
    /// </remarks>
    private static readonly ImmutableArray<string> ContractPermittedKeyMembers =
    [
        KeyFamilyMember,
        KeyUseMember,
        KeyOperationsMember,
        AlgorithmMember,
        KeyIdentifierMember,
        ModulusMember,
        ExponentMember,
    ];

    /// <summary>
    /// The members the authored contract REQUIRES on a key
    /// [<c>OpenApi/security.v1.yaml</c> JsonWebKey, <c>required: [kty, kid, n, e]</c>].
    /// </summary>
    private static readonly ImmutableArray<string> ContractRequiredKeyMembers =
    [
        KeyFamilyMember,
        KeyIdentifierMember,
        ModulusMember,
        ExponentMember,
    ];

    /// <summary>
    /// The EXACT member set this issuer publishes in the discovery document.
    /// </summary>
    private static readonly ImmutableArray<string> PublishedMetadataMembers =
    [
        IssuerMember,
        KeySetAddressMember,
        TokenEndpointMember,
        SigningAlgorithmsMember,
    ];

    /// <summary>
    /// The member set the authored contract PERMITS in the discovery document
    /// [<c>OpenApi/security.v1.yaml</c> ProviderMetadata, <c>additionalProperties: false</c>].
    /// </summary>
    private static readonly ImmutableArray<string> ContractPermittedMetadataMembers =
    [
        IssuerMember,
        KeySetAddressMember,
        TokenEndpointMember,
        SigningAlgorithmsMember,
        "grant_types_supported",
        "subject_types_supported",
        "response_types_supported",
    ];

    /// <summary>
    /// The members the authored contract REQUIRES in the discovery document
    /// [<c>OpenApi/security.v1.yaml</c> ProviderMetadata, <c>required: [issuer, jwks_uri]</c>].
    /// </summary>
    private static readonly ImmutableArray<string> ContractRequiredMetadataMembers =
    [
        IssuerMember,
        KeySetAddressMember,
    ];

    /// <summary>
    /// The two members whose ABSENCE from the discovery document is part of the contract (C-K).
    /// </summary>
    /// <remarks>
    /// There is no interactive flow and no user-facing flow anywhere in this system: every caller is a
    /// service, identity on the issuance operation is established by the transport, and there is no end
    /// user to redirect, to prompt for consent or to describe. Publishing either member would advertise
    /// a capability that does not exist and invite a consumer to attempt a redirect that cannot succeed.
    /// </remarks>
    private static readonly ImmutableArray<string> InteractiveFlowMembers =
    [
        "authorization_endpoint",
        "userinfo_endpoint",
    ];

    /// <summary>
    /// The armour markers whose presence anywhere in a served body is a HARD FAILURE.
    /// </summary>
    /// <remarks>
    /// EACH ONE IS ASSEMBLED FROM FRAGMENTS ON PURPOSE, and the reason is a C-F control rather than
    /// style: a secret-scanning sweep over this repository greps for the contiguous spellings, and a
    /// leak control that itself trips the sweep is indistinguishable from the leak it is looking for.
    /// Concatenation at compile time produces the exact marker to search for while leaving no
    /// contiguous literal in the source. The private-key label is deliberately the bare label rather
    /// than any one algorithm's spelling of it, so the single marker covers every armoured private-key
    /// variant.
    /// </remarks>
    private static readonly ImmutableArray<string> ArmourMarkers =
    [
        "-----" + "BEG" + "IN",
        "-----" + "E" + "ND",
        "PRIVATE" + " " + "KEY",
    ];

    /// <summary>
    /// Every private RSA component, plus the symmetric key value, that must never be published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The private exponent comes first because it ALONE is sufficient to forge a signature. The two
    /// prime factors and the three Chinese-remainder values follow, each of which also permits
    /// reconstruction of the private key. Last is the symmetric key value, which is not an RSA component
    /// at all but is included because it is the member a symmetric fallback would publish, and a
    /// symmetric entry on an anonymously published key set would publish the signing secret itself and
    /// make every verifier a co-signer.
    /// </para>
    /// <para>
    /// Held as an array so both the theory and the type-level structural row draw on ONE list. A second
    /// list would be a second place to forget a component.
    /// </para>
    /// </remarks>
    private static readonly ImmutableArray<string> ForbiddenKeyMemberNames =
    [
        "d",
        "p",
        "q",
        "dp",
        "dq",
        "qi",
        SymmetricKeyValueMember,
    ];

    /// <summary>
    /// The serializer configurations the structural projection row is asserted under.
    /// </summary>
    /// <remarks>
    /// The default, one that writes indented output, and one that includes members whose values are
    /// null - the last being the setting most likely to surface a member that is normally omitted, which
    /// is exactly the failure mode a member-omission-based defence would have.
    /// </remarks>
    private static IEnumerable<JsonSerializerOptions> SerializerConfigurations()
    {
        yield return JsonSerializerOptions.Default;
        yield return new JsonSerializerOptions { WriteIndented = true };
        yield return new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };
    }

    /// <summary>
    /// One row per private RSA component that must never be published.
    /// </summary>
    /// <remarks>
    /// THE DENY LIST, KEPT ALONGSIDE THE ALLOW LIST RATHER THAN INSTEAD OF IT. The allow-list row is the
    /// stronger assertion, so this theory is not the primary defence; it exists because a row failure
    /// NAMES the component that leaked in the row's own title, which is what a reader of a failing
    /// continuous-integration report needs. A set-difference failure would say only that the shape
    /// changed.
    /// </remarks>
    /// <returns>One row per forbidden member name.</returns>
    public static TheoryData<string> ForbiddenKeyMembers()
    {
        TheoryData<string> rows = new();

        foreach (string member in ForbiddenKeyMemberNames)
        {
            rows.Add(member);
        }

        return rows;
    }

    /// <summary>
    /// One row per branch of the discovery document's metadata-consistency check.
    /// </summary>
    /// <returns>One row per fault name.</returns>
    public static TheoryData<string> MetadataFaults()
    {
        TheoryData<string> rows = new();

        rows.Add(BlankIssuerFault);
        rows.Add(RelativeIssuerFault);
        rows.Add(UnrootedKeySetPathFault);
        rows.Add(UnrootedTokenEndpointPathFault);

        return rows;
    }

    // ==============================================================================================
    //  GROUP 1 - ANONYMITY. Both publications answer with no credential, and the paired refutation
    //  shows the default-deny fallback policy is nonetheless live on the same client.
    // ==============================================================================================

    /// <summary>
    /// The key set is served to a client that presents nothing at all.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE REFUTATION IS WHAT GIVES THIS ROW ITS FORCE. <c>Program.cs</c> installs a fallback
    /// authorization policy requiring an authenticated user, so if that policy were inert the success
    /// below would prove nothing about this route. The second half shows the IDENTICAL credential-free
    /// client is refused on the authenticated probe route, which establishes that the key set is open
    /// BECAUSE its registration calls <c>AllowAnonymous()</c> explicitly - remove that call and this row
    /// fails with an unauthorized status.
    /// </remarks>
    [Fact]
    public async Task TheKeySetIsServedWithoutAnyCredentialAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        // No header is added, and the absence is asserted rather than assumed: a factory that had
        // started attaching a default credential would make every anonymity row vacuous.
        Assert.Null(client.DefaultRequestHeaders.Authorization);

        SecurityOptions options = factory.ResolveSecurityOptions();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(options.JwksPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage refused = await client.GetAsync(
            new Uri(AuthenticatedProbePath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The discovery document is served to a client that presents nothing at all.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// This route's anonymity is not merely convenient, it is MECHANICALLY NECESSARY: a consumer's
    /// stock bearer handler reads this document IN ORDER TO LEARN HOW TO AUTHENTICATE, so it cannot
    /// already hold a token when it asks. Requiring one would deadlock the self-configuration the
    /// document exists to provide. The same refutation is paired here for the same reason as above.
    /// </remarks>
    [Fact]
    public async Task TheDiscoveryDocumentIsServedWithoutAnyCredentialAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        Assert.Null(client.DefaultRequestHeaders.Authorization);

        SecurityOptions options = factory.ResolveSecurityOptions();

        using HttpResponseMessage response = await client.GetAsync(
            new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage refused = await client.GetAsync(
            new Uri(AuthenticatedProbePath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// Every address the discovery document publishes is the configured issuer joined to a configured
    /// path, and none of them moves when the caller names a different authority.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// WHAT WENT WRONG, AS A FACT ABOUT THE CODE. This operation composed both published addresses from
    /// the incoming request's scheme, host and path base. All three are caller-controlled - a Host header,
    /// or an <c>X-Forwarded-*</c> header a proxy honours - so a request arriving with a chosen authority
    /// was answered with a discovery document telling every consumer to fetch THIS issuer's verification
    /// keys from that authority. A consumer's stock bearer handler follows <c>jwks_uri</c> without
    /// question, which is the whole purpose of the document, so the consequence of a caller succeeding at
    /// this is that it chooses the key material every service in the system validates tokens against.
    /// </para>
    /// <para>
    /// THE FORGED AUTHORITY IS ONE THE ALLOW-LIST ADMITS, WHICH IS WHAT MAKES THE ROW ABOUT THE DOCUMENT
    /// RATHER THAN ABOUT HOST FILTERING. A loopback spelling is used, so the request is served normally
    /// and the published addresses are the only thing that could differ. The sibling row below drives the
    /// filtering half.
    /// </para>
    /// <para>
    /// Both addresses are asserted in FULL rather than by suffix. A suffix assertion is satisfied by an
    /// address built on any authority at all, so it is exactly the assertion the original defect passed.
    /// One of the three authorities carries a DISTINCTIVE PORT the issuer does not have, so reflecting any
    /// part of the caller's authority - the host, the port, or both - shows up as an inequality rather than
    /// having to be searched for.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedAddressesComeFromConfigurationAndNotFromTheCallerAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();
        string expectedKeySet = options.Issuer.TrimEnd('/') + options.JwksPath;
        string expectedTokenEndpoint = options.Issuer.TrimEnd('/') + options.TokenEndpointPath;

        foreach (string authority in (string[])["localhost", "127.0.0.1", "localhost:9999"])
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Host = authority;

            using HttpResponseMessage response = await client.GetAsync(
                new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            Assert.Equal(options.Issuer, document.RootElement.GetProperty("issuer").GetString());
            Assert.Equal(expectedKeySet, document.RootElement.GetProperty("jwks_uri").GetString());
            Assert.Equal(
                expectedTokenEndpoint,
                document.RootElement.GetProperty("token_endpoint").GetString());
        }
    }

    /// <summary>
    /// A request naming an authority the deployment does not publish is refused before any route runs.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// DEFENCE IN DEPTH RATHER THAN THE PRIMARY CONTROL, and stated as such. The document is already
    /// independent of the caller after the row above, so this changes no published value; what it adds is
    /// that the service holding the system's only signing key does not answer at all under an authority
    /// its deployment never declared. The setting was <c>*</c>, which admits every authority.
    /// </para>
    /// <para>
    /// THE ANONYMOUS ROUTE IS USED DELIBERATELY. Host filtering runs ahead of routing and authentication,
    /// so a refusal here cannot be confused with the 401 an authenticated route would give: the route
    /// chosen answers 200 to a caller presenting nothing, and the only reason it can fail is the
    /// authority.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnAuthorityTheDeploymentDoesNotDeclareIsRefusedAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();

        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "not-a-declared-authority.invalid";

        using HttpResponseMessage refused = await client.GetAsync(
            new Uri(options.OpenIdConfigurationPath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    /// <summary>
    /// The configured addresses ARE the well-known addresses a stock bearer handler looks for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other row reads these two paths FROM CONFIGURATION rather than from a literal, which is
    /// correct - they are settings, and a row that hardcoded them would pass against a host configured
    /// with something else. This row is the one place the VALUES themselves are pinned, and it is
    /// necessary precisely because the rest do not pin them: a handler pointed at an authority derives
    /// the discovery address by fixed convention and will not find a renamed one, so a rename would
    /// leave every other row green while breaking every real consumer (C-L).
    /// </para>
    /// <para>
    /// The well-known namespace prefix is CONSUMED from the options validator rather than re-spelled,
    /// so the prefix has exactly one definition in the service. The distinctness assertion mirrors the
    /// registration method's own guard: two routes on one address is an ambiguous match at request time.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheConfiguredAddressesAreTheDocumentedWellKnownOnes()
    {
        using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();

        Assert.Equal(DocumentedKeySetPath, options.JwksPath);
        Assert.Equal(DocumentedMetadataPath, options.OpenIdConfigurationPath);

        Assert.StartsWith(
            SecurityOptionsValidator.WellKnownMetadataPathPrefix,
            options.JwksPath,
            StringComparison.Ordinal);
        Assert.StartsWith(
            SecurityOptionsValidator.WellKnownMetadataPathPrefix,
            options.OpenIdConfigurationPath,
            StringComparison.Ordinal);

        Assert.NotEqual(options.JwksPath, options.OpenIdConfigurationPath);
    }

    // ==============================================================================================
    //  GROUP 2 - PUBLIC-ONLY MATERIAL. The hard-failure assertions. An allow list over the actually
    //  parsed members, a per-component deny list, a symmetric-family refusal, and a raw-text scan.
    // ==============================================================================================

    /// <summary>
    /// The served key carries EXACTLY the six public members, and every one of them is well formed.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ALLOW-LIST ROW, AND THE PRIMARY DEFENCE IN THIS FILE. The member set is read out of the
    /// PARSED document by enumeration rather than by looking a handful of names up, because the whole
    /// point is to detect members that SHOULD NOT EXIST and a lookup can only find the ones it was told
    /// to ask for. Binding to a response type would be worse still: a deserializer discards unknown
    /// members silently, so a document carrying a private component would round-trip into a clean
    /// object and the leak would be invisible.
    /// </para>
    /// <para>
    /// Member ORDER is deliberately not asserted - see the file header on C-B. The set is compared
    /// order-insensitively, and the count is asserted alongside it so a duplicated member cannot hide
    /// inside a set comparison.
    /// </para>
    /// <para>
    /// The two public parameters are asserted through boolean assertions with structural messages
    /// rather than through equality or containment assertions, so no failure path can render the
    /// modulus or the exponent into test output.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheServedKeyCarriesExactlyTheSixPublicMembersAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject key = RequireSingleKey(
            ParseObject(await FetchDocumentAsync(client, options.JwksPath)));

        ImmutableArray<string> members = ReadMemberNames(key);

        Assert.Equal(PublishedKeyMembers.Length, members.Length);
        Assert.Equal(Sorted(PublishedKeyMembers), Sorted(members));

        Assert.Equal(AsymmetricKeyFamily, ReadString(key, KeyFamilyMember));
        Assert.Equal(SignatureUse, ReadString(key, KeyUseMember));
        Assert.Equal(options.SigningAlgorithm, ReadString(key, AlgorithmMember));
        Assert.Equal(options.SigningKeyId, ReadString(key, KeyIdentifierMember));

        // The two public parameters. Presence, non-emptiness and the unpadded base64url alphabet are
        // asserted WITHOUT the value entering a failure message: base64url is what the key-set format
        // requires, and a value in the padded standard alphabet would be undecodable by a stock
        // consumer even though it would look plausible to a reader.
        AssertUnpaddedBase64Url(key, ModulusMember);
        AssertUnpaddedBase64Url(key, ExponentMember);
    }

    /// <summary>
    /// No private component appears anywhere in the served key set. One row per component.
    /// </summary>
    /// <param name="forbiddenMember">The member whose absence this row asserts.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A DENY LIST WHOSE VALUE IS DIAGNOSTIC RATHER THAN STRUCTURAL. The allow-list row above already
    /// refuses these members, and refuses any other member too; what this theory adds is that a failure
    /// NAMES the component that leaked in the row's own title, which is what makes a failing report
    /// actionable rather than merely alarming.
    /// </para>
    /// <para>
    /// The search covers the whole document rather than only a key object, so a component smuggled into
    /// the key-set wrapper, into a nested object or into any array element is caught as well. Comparison
    /// is ordinal and case-sensitive because these member names are.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ForbiddenKeyMembers))]
    public async Task NoPrivateComponentIsPublishedAsync(string forbiddenMember)
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject document = ParseObject(await FetchDocumentAsync(client, options.JwksPath));

        Assert.False(
            ContainsMemberAnywhere(document, forbiddenMember),
            $"The published key set carries a member named '{forbiddenMember}'. That is a private or "
            + "symmetric key component, and publishing one on an anonymous route hands every caller "
            + "the ability to mint tokens indistinguishable from legitimate ones. The offending value "
            + "is deliberately not reproduced in this message.");
    }

    /// <summary>
    /// No symmetric key is published, and a symmetric key value is structurally unrepresentable.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THIS IS NOT A HYPOTHETICAL. A symmetric entry is exactly what would be published if someone
    /// "helpfully" made the signing-key provider fall back to a keyed-hash key when asymmetric material
    /// turned out to be unusable - a fallback the provider is specified never to perform, and whose
    /// refusal <c>SigningKeyProviderTests.cs</c> asserts at the point of construction. This row is the
    /// second line of defence behind that one, on the published document.
    /// </para>
    /// <para>
    /// The second half is the STRUCTURAL proof and is the stronger of the two. It hands the projection a
    /// published key that CLAIMS the symmetric family, and shows the projected document still carries
    /// exactly the six public members and no key value member: the response shape declares nowhere for
    /// one to go, so no serializer setting, naming policy or upstream change can produce one. A reviewer
    /// can confirm the property by reading one type declaration, and this row makes that confirmation
    /// executable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NoSymmetricKeyOrKeyValueMemberIsPublishedAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject document = ParseObject(await FetchDocumentAsync(client, options.JwksPath));

        foreach (JsonObject key in ReadKeys(document))
        {
            Assert.Equal(AsymmetricKeyFamily, ReadString(key, KeyFamilyMember));
            Assert.NotEqual(SymmetricKeyFamily, ReadString(key, KeyFamilyMember));
        }

        Assert.False(
            ContainsMemberAnywhere(document, SymmetricKeyValueMember),
            "The published key set carries a symmetric key value member. A symmetric entry on an "
            + "anonymously published key set publishes the signing secret itself and makes every "
            + "verifier a co-signer.");

        // THE STRUCTURAL HALF: even given a published key that claims the symmetric family, the
        // projection has nowhere to put a key value.
        JsonObject projected = ParseObject(
            JsonSerializer.Serialize(
                JwksEndpoints.ProjectKeySet(
                    new PublishedJsonWebKeySet(
                        new PublishedJsonWebKey(
                            keyType: SymmetricKeyFamily,
                            use: SignatureUse,
                            algorithm: options.SigningAlgorithm,
                            keyId: ProbeKeyIdentifier,
                            modulus: PlaceholderPublicComponent,
                            exponent: PlaceholderPublicComponent)))));

        JsonObject projectedKey = RequireSingleKey(projected);

        Assert.Equal(Sorted(PublishedKeyMembers), Sorted(ReadMemberNames(projectedKey)));
        Assert.False(
            ContainsMemberAnywhere(projected, SymmetricKeyValueMember),
            "The allow-list projection produced a symmetric key value member, which means the "
            + "published response shape has grown somewhere to put one.");
    }

    /// <summary>
    /// The projection cannot carry a private component whatever the published material claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE COMPANION STRUCTURAL PROOF TO THE THEORY ABOVE, and the reason the theory is a diagnostic
    /// rather than the defence. The theory asserts a property of one document produced by one host; this
    /// row asserts a property of the TYPE, so it holds for every document the service will ever produce.
    /// </para>
    /// <para>
    /// The projection is driven directly rather than over the wire, with every serializer configuration
    /// a caller might plausibly install - the default, one that writes indented output, and one that
    /// includes members whose values are null, which is the setting most likely to surface a member that
    /// is normally omitted. In every case the emitted member set is the six, and every forbidden name is
    /// absent.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheProjectionCannotCarryAPrivateComponentUnderAnySerializerSetting()
    {
        PublishedJsonWebKeySet published = new(
            new PublishedJsonWebKey(
                keyType: AsymmetricKeyFamily,
                use: SignatureUse,
                algorithm: SecurityAlgorithms.RsaSha256,
                keyId: ProbeKeyIdentifier,
                modulus: PlaceholderPublicComponent,
                exponent: PlaceholderPublicComponent));

        JsonWebKeySetDocument projected = JwksEndpoints.ProjectKeySet(published);

        Assert.Single(projected.Keys);

        foreach (JsonSerializerOptions serializerOptions in SerializerConfigurations())
        {
            JsonObject document = ParseObject(JsonSerializer.Serialize(projected, serializerOptions));
            JsonObject key = RequireSingleKey(document);

            Assert.Equal(Sorted(PublishedKeyMembers), Sorted(ReadMemberNames(key)));

            foreach (string forbiddenMember in ForbiddenKeyMemberNames)
            {
                Assert.False(
                    ContainsMemberAnywhere(document, forbiddenMember),
                    $"The projection emitted a member named '{forbiddenMember}' under one of the "
                    + "serializer configurations, which means the published response shape declares "
                    + "somewhere for a private or symmetric component to go.");
            }
        }
    }

    /// <summary>
    /// Neither served body carries an armour marker, and no value in either decodes to a private key.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// A STRUCTURAL CHECK AND A RAW-TEXT CHECK TOGETHER CATCH DIFFERENT MISTAKES. The member-set rows
    /// above catch a serialization mistake - a response shape that grew a member. This row catches a
    /// HAND-BUILT document: a string pasted into an otherwise well-shaped member, which no member-set
    /// assertion can see because the member itself is legitimate.
    /// </para>
    /// <para>
    /// Two layers. First the armour markers, scanned over the raw body, which catch the armoured text
    /// form - the exact form the legacy anti-pattern at <c>tests/blink/test_jws.htm:L8-L22</c> carries
    /// inline. Second, and stronger, every string value in either document is decoded and offered to
    /// the two import paths the signing-key provider ITSELF accepts. That second layer is deterministic
    /// rather than probabilistic: a random public modulus cannot parse as a private-key structure, so the
    /// row cannot fail by coincidence, and it refuses exactly the encodings a leak would arrive in.
    /// </para>
    /// <para>
    /// Both assertions are boolean with structural messages. Neither the body nor any member value nor
    /// any decoded byte reaches test output, which matters most on precisely the row whose failure would
    /// mean real key material was present.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task NeitherServedBodyCarriesArmouredOrEncodedPrivateKeyMaterialAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();

        string keySetPayload = await FetchDocumentAsync(client, options.JwksPath);
        string metadataPayload = await FetchDocumentAsync(client, options.OpenIdConfigurationPath);

        foreach (string marker in ArmourMarkers)
        {
            Assert.False(
                keySetPayload.Contains(marker, StringComparison.Ordinal),
                "The published key set body carries an armoured key marker. The marker itself is not "
                + "reproduced in this message, and neither is the body.");

            Assert.False(
                metadataPayload.Contains(marker, StringComparison.Ordinal),
                "The published discovery body carries an armoured key marker. The marker itself is "
                + "not reproduced in this message, and neither is the body.");
        }

        foreach (string value in ReadStringValues(ParseObject(keySetPayload)))
        {
            Assert.False(
                DecodesToAnAsymmetricPrivateKey(value),
                "A member of the published key set decodes to an asymmetric PRIVATE key in one of the "
                + "two encodings the signing-key provider accepts. The value is deliberately not "
                + "reproduced in this message.");
        }

        foreach (string value in ReadStringValues(ParseObject(metadataPayload)))
        {
            Assert.False(
                DecodesToAnAsymmetricPrivateKey(value),
                "A member of the published discovery document decodes to an asymmetric PRIVATE key. "
                + "The value is deliberately not reproduced in this message.");
        }
    }

    // ==============================================================================================
    //  GROUP 3 - AGREEMENT WITH THE ISSUER. A correctly shaped key set that publishes a DIFFERENT key
    //  from the one tokens are signed with is silently unusable by every consumer, and no shape
    //  assertion can detect that.
    // ==============================================================================================

    /// <summary>
    /// The published identifier equals the configured one and the one stamped into a minted token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THREE VALUES, TWO EQUALITIES, AND BOTH MATTER SEPARATELY. Agreement between the published
    /// identifier and the CONFIGURED one is what the key set's own consistency check enforces, so it can
    /// only fail if that check regressed. Agreement between the published identifier and the one in a
    /// real minted token's HEADER is the one nothing else guards: a consumer selects its verification key
    /// by that header value, so a divergence there makes every token unverifiable while both documents
    /// still look perfectly well formed. All three are non-secret identifiers, so equality assertions are
    /// used here and the actual values may safely appear in a failure report.
    /// </remarks>
    [Fact]
    public async Task ThePublishedIdentifierMatchesTheConfiguredOneAndTheMintedTokenHeaderAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject key = RequireSingleKey(
            ParseObject(await FetchDocumentAsync(client, options.JwksPath)));

        string publishedIdentifier = ReadString(key, KeyIdentifierMember);

        Assert.Equal(options.SigningKeyId, publishedIdentifier);

        IssuedToken minted = factory.IssueToken();
        JsonWebToken parsed = new(minted.AccessToken);

        Assert.Equal(publishedIdentifier, parsed.Kid);
        Assert.Equal(publishedIdentifier, minted.KeyId);
    }

    /// <summary>
    /// The published algorithm equals the configured one, is a permitted one, and is the one stamped
    /// into a minted token's header.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// The permitted set is CONSUMED from the options validator rather than re-spelled, so the closed set
    /// of algorithms this issuer may sign with has exactly one definition in the service.
    /// </para>
    /// <para>
    /// THE PARITY ANCHOR, MADE EXECUTABLE RATHER THAN LEFT AS PROSE. The legacy signature primitives take
    /// a digest selector whose catalogue comment records that ONE set governs the hash primitive,
    /// <c>RSASign</c> AND <c>VerifyRSASign</c> [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and its
    /// SHA-256 member sits at [:L930]. That is the settled provenance for this issuer publishing an
    /// RSA-with-SHA-256 identifier [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73]. The constant is
    /// CONSUMED from the shared kernel rather than re-declared, which is the same rule the rest of the
    /// estate follows for preserved legacy identifiers: exactly one definition, and no file outside the
    /// scoped suppression list declares one of its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task ThePublishedAlgorithmMatchesTheConfiguredOneAndTheLegacyDigestSelectorAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject key = RequireSingleKey(
            ParseObject(await FetchDocumentAsync(client, options.JwksPath)));

        string publishedAlgorithm = ReadString(key, AlgorithmMember);

        Assert.Equal(options.SigningAlgorithm, publishedAlgorithm);
        Assert.Equal(SecurityAlgorithms.RsaSha256, publishedAlgorithm);
        Assert.Contains(publishedAlgorithm, SecurityOptionsValidator.PermittedSigningAlgorithms);

        IssuedToken minted = factory.IssueToken();
        JsonWebToken parsed = new(minted.AccessToken);

        Assert.Equal(publishedAlgorithm, parsed.Alg);

        // The legacy digest selector this identifier corresponds to, consumed from the shared kernel.
        Assert.Equal(2L, Enums.CRYPTO_HASH_SHA256);
        Assert.Equal(
            SecurityAlgorithms.RsaSha256,
            DescribeLegacyDigestAsAlgorithmIdentifier(Enums.CRYPTO_HASH_SHA256));
    }

    /// <summary>
    /// The FETCHED modulus and exponent genuinely verify a token this service minted.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT PROVES THE TWO HALVES ARE ONE KEY PAIR RATHER THAN TWO UNRELATED ONES. Everything
    /// above asserts shape and identifier agreement; a service could satisfy all of it while publishing a
    /// perfectly well-formed key belonging to a different pair, and every consumer would then reject every
    /// token with no diagnostic pointing anywhere near the key set.
    /// </para>
    /// <para>
    /// The verification key is rebuilt FROM THE PUBLISHED PARAMETERS - decoded out of the fetched
    /// document - rather than taken from the host's own service graph, so the row validates with exactly
    /// what a consumer would have. Taking the key from the container would assert a property of the
    /// process instead of a property of the document, which is the wrong subject entirely.
    /// </para>
    /// <para>
    /// The validation parameters check everything a real consumer checks, including the lifetime: the
    /// factory's clock seam is fixed at the real current instant precisely so a minted token remains
    /// inside its validity window while the clock is still deterministic. The failure message carries the
    /// exception TYPE only, never its message, because a validation failure message can echo token
    /// content.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheFetchedPublicParametersValidateAMintedTokenAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject key = RequireSingleKey(
            ParseObject(await FetchDocumentAsync(client, options.JwksPath)));

        RsaSecurityKey verificationKey = BuildVerificationKey(key);
        IssuedToken minted = factory.IssueToken();

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            minted.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudiences = [factory.ResolveInboundAudience()],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = verificationKey,
                ValidAlgorithms = [options.SigningAlgorithm],
            });

        Assert.True(
            result.IsValid,
            "A token this service minted did NOT validate against the modulus and exponent this "
            + $"service published, which means the two are not one key pair. Failure type: "
            + $"{result.Exception?.GetType().Name ?? "none reported"}.");

        Assert.Equal(options.Issuer, result.Issuer);
    }

    // ==============================================================================================
    //  GROUP 4 - DISCOVERY SUFFICIENCY. Asserting this is asserting the architectural decision: this
    //  surface is REST rather than gRPC precisely so a consumer writes ZERO bespoke retrieval code.
    // ==============================================================================================

    /// <summary>
    /// The discovery document publishes the issuer, the key-set address and the signature algorithm.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE MINIMUM A STOCK BEARER HANDLER NEEDS, asserted member by member. The key-set address is
    /// asserted to be ABSOLUTE and to point at the configured key-set PATH rather than at the configured
    /// issuer's host: the implementation composes it from the address the request arrived on, which is
    /// what makes the document reachable by whoever fetched it and is why no host is fixed in code. A row
    /// that demanded the issuer's host would fail behind any proxy and would be asserting a behaviour the
    /// service deliberately does not have.
    /// </para>
    /// <para>
    /// The address is then FETCHED to prove it resolves to the key set rather than merely looking
    /// plausible - an address that is well formed and wrong is the failure this member exists to make
    /// impossible.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TheDiscoveryDocumentCarriesTheIssuerTheKeySetAddressAndTheAlgorithmAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject metadata = ParseObject(
            await FetchDocumentAsync(client, options.OpenIdConfigurationPath));

        Assert.Equal(options.Issuer, ReadString(metadata, IssuerMember));

        Uri keySetAddress = new(ReadString(metadata, KeySetAddressMember), UriKind.Absolute);

        Assert.Equal(options.JwksPath, keySetAddress.AbsolutePath);

        Uri tokenAddress = new(ReadString(metadata, TokenEndpointMember), UriKind.Absolute);

        Assert.Equal(options.TokenEndpointPath, tokenAddress.AbsolutePath);

        ImmutableArray<string> algorithms = ReadStringArray(metadata, SigningAlgorithmsMember);

        Assert.Contains(SecurityAlgorithms.RsaSha256, algorithms);
        Assert.Contains(options.SigningAlgorithm, algorithms);

        // The published address is FETCHED rather than trusted: a well-formed wrong address is exactly
        // the failure this member exists to make impossible.
        using HttpResponseMessage fetched = await client.GetAsync(
            new Uri(keySetAddress.AbsolutePath, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        JsonObject key = RequireSingleKey(
            ParseObject(
                await fetched.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

        Assert.Equal(options.SigningKeyId, ReadString(key, KeyIdentifierMember));
    }

    /// <summary>
    /// The published issuer equals the issuer claim the service stamps into a minted token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE CLASSIC CAUSE OF A SILENT VALIDATION FAILURE IN A STOCK BEARER HANDLER. A handler configured
    /// from discovery validates every inbound token's issuer claim against the document's issuer member,
    /// byte for byte, so a mismatch rejects every token while both documents remain individually valid
    /// and every shape assertion in this file still passes.
    /// </remarks>
    [Fact]
    public async Task TheDiscoveredIssuerEqualsTheIssuerClaimOfAMintedTokenAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject metadata = ParseObject(
            await FetchDocumentAsync(client, options.OpenIdConfigurationPath));

        string publishedIssuer = ReadString(metadata, IssuerMember);
        JsonWebToken parsed = new(factory.IssueToken().AccessToken);

        Assert.Equal(options.Issuer, publishedIssuer);
        Assert.Equal(publishedIssuer, parsed.Issuer);
    }

    /// <summary>
    /// A stock retrieval stack self-configures from the discovery address alone and accepts a minted
    /// token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE BEHAVIOURAL FORM, AND THE STRONGEST ROW IN THIS GROUP. It asserts the ARCHITECTURAL DECISION
    /// rather than a field: this surface is plain HTTP rather than a binary protocol precisely so a
    /// consumer points a stock handler at it and writes NO retrieval code, and choosing otherwise would
    /// have forced a hand-written key-set retrieval implementation into THREE separate services - a net
    /// increase in hand-written security-critical code.
    /// </para>
    /// <para>
    /// The stack assembled below is not a test approximation of that path: the configuration manager, the
    /// discovery-document retriever and the document retriever are the SAME three types
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> composes internally when it is given an
    /// authority. THE ONLY INPUT IT RECEIVES IS THE DISCOVERY ADDRESS. No key, no identifier, no issuer
    /// and no algorithm is configured: the manager reads the discovery document, follows the key-set
    /// address it finds there on its own, and materialises the verification material itself. Everything
    /// the validation parameters below are built from therefore came out of the two published documents.
    /// </para>
    /// <para>
    /// The one concession to running in process is the transport - the document retriever is handed this
    /// host's in-memory client and told not to require transport security, because the in-memory
    /// transport has none. That is a property of the test harness, not of the retrieval logic: the
    /// service's own listener is transport-secured and its settings require secure metadata. Nothing else
    /// about the handler's behaviour is altered, and in particular no key is supplied to it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AStockRetrievalStackSelfConfiguresFromDiscoveryAndAcceptsAMintedTokenAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();

        Uri? baseAddress = client.BaseAddress;
        Assert.NotNull(baseAddress);

        Uri discoveryAddress = new(baseAddress, options.OpenIdConfigurationPath);

        // The single input. Note the type is not disposable in this library version, which is why it is
        // held in a plain local rather than a using declaration.
        ConfigurationManager<OpenIdConnectConfiguration> stockRetrieval = new(
            discoveryAddress.AbsoluteUri,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(client) { RequireHttps = false });

        OpenIdConnectConfiguration discovered =
            await stockRetrieval.GetConfigurationAsync(TestContext.Current.CancellationToken);

        SecurityKey discoveredKey = Assert.Single(discovered.SigningKeys);

        Assert.Equal(options.Issuer, discovered.Issuer);
        Assert.Equal(options.SigningKeyId, discoveredKey.KeyId);

        IssuedToken minted = factory.IssueToken();

        TokenValidationResult result = await new JsonWebTokenHandler().ValidateTokenAsync(
            minted.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = discovered.Issuer,
                ValidateAudience = true,
                ValidAudiences = [factory.ResolveInboundAudience()],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,

                // DISCOVERED, not configured. This is the assertion: no key was supplied to the stack.
                IssuerSigningKeys = discovered.SigningKeys,
                ValidAlgorithms = [options.SigningAlgorithm],
            });

        Assert.True(
            result.IsValid,
            "A stock retrieval stack given nothing but the discovery address could not validate a "
            + "token this service minted, so a consumer cannot self-configure from the published "
            + $"metadata. Failure type: {result.Exception?.GetType().Name ?? "none reported"}.");
    }

    /// <summary>
    /// The discovery document publishes no interactive-flow member, which is part of the contract.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// AN ABSENCE ASSERTED AS A DECISION RATHER THAN LEFT AS A GAP (C-K). There is no interactive flow
    /// and no user-facing flow anywhere in this system: every caller is a service, identity on the
    /// issuance operation is established by the transport, and there is no end user to redirect, to
    /// prompt for consent or to describe. Publishing either member would advertise a capability that does
    /// not exist and invite a consumer to attempt a redirect that cannot succeed. The authored contract
    /// closes its member set for exactly this reason, so the absence is machine-checkable rather than
    /// prose - and this row is the check.
    /// </remarks>
    [Fact]
    public async Task TheDiscoveryDocumentPublishesNoInteractiveFlowMemberAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();
        JsonObject metadata = ParseObject(
            await FetchDocumentAsync(client, options.OpenIdConfigurationPath));

        foreach (string member in InteractiveFlowMembers)
        {
            Assert.False(
                ContainsMemberAnywhere(metadata, member),
                $"The discovery document publishes '{member}'. There is no interactive flow anywhere in "
                + "this system, so the member advertises a capability that does not exist, and the "
                + "authored contract closes its member set precisely to prevent it being added without "
                + "a version change.");
        }
    }

    // ==============================================================================================
    //  GROUP 5 - AGREEMENT WITH THE AUTHORED CONTRACT. security.v1.yaml is AUTHORITATIVE FOR THE WIRE:
    //  where it and the implementation could differ, the YAML wins and these rows assert the YAML.
    // ==============================================================================================

    /// <summary>
    /// Both served documents conform to the member sets the authored contract declares.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// BOTH SCHEMAS CLOSE THEIR MEMBER SETS, so the permitted sets asserted here are CLOSED SETS rather
    /// than minimums and a subset assertion is a genuine conformance check rather than a formality. Each
    /// document is therefore checked twice: every required member is present, and every member present is
    /// permitted.
    /// </para>
    /// <para>
    /// The key's permitted set is deliberately WIDER than the set the previous group pins exactly: the
    /// contract allows an optional permitted-operations member that this issuer does not emit, and
    /// omitting an optional member conforms. Keeping the two assertions separate is what lets a reader
    /// tell a contract violation from an implementation change - the first is a published-contract break,
    /// the second is not.
    /// </para>
    /// <para>
    /// The key set's own wrapper is checked as well, because its schema closes its member set too: a
    /// document that grew a sibling of the key array would conform to nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BothServedDocumentsConformToTheAuthoredContractMemberSetsAsync()
    {
        await using SecurityAppFactory factory = new();
        using HttpClient client = factory.CreateClient();

        SecurityOptions options = factory.ResolveSecurityOptions();

        JsonObject keySet = ParseObject(await FetchDocumentAsync(client, options.JwksPath));

        // JsonWebKeySet: additionalProperties false, required [keys].
        Assert.Equal(Sorted([KeysMember]), Sorted(ReadMemberNames(keySet)));

        JsonObject key = RequireSingleKey(keySet);
        ImmutableArray<string> keyMembers = ReadMemberNames(key);

        foreach (string required in ContractRequiredKeyMembers)
        {
            Assert.Contains(required, keyMembers);
        }

        foreach (string present in keyMembers)
        {
            Assert.Contains(present, ContractPermittedKeyMembers);
        }

        // The two closed enumerations the key schema declares.
        Assert.Equal(AsymmetricKeyFamily, ReadString(key, KeyFamilyMember));
        Assert.Equal(SignatureUse, ReadString(key, KeyUseMember));

        JsonObject metadata = ParseObject(
            await FetchDocumentAsync(client, options.OpenIdConfigurationPath));
        ImmutableArray<string> metadataMembers = ReadMemberNames(metadata);

        foreach (string required in ContractRequiredMetadataMembers)
        {
            Assert.Contains(required, metadataMembers);
        }

        foreach (string present in metadataMembers)
        {
            Assert.Contains(present, ContractPermittedMetadataMembers);
        }

        // What this issuer actually emits, pinned separately from what the contract permits.
        Assert.Equal(Sorted(PublishedMetadataMembers), Sorted(metadataMembers));
    }

    // ==============================================================================================
    //  GROUP 6 - THE FAILURE SHAPE. Every inconsistency the two handlers can detect resolves to the
    //  SHARED problem shape carrying the legacy return code, not to a second error shape invented here.
    // ==============================================================================================

    /// <summary>
    /// A key set that disagrees with the configuration fails with the shared problem shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DRIVEN DIRECTLY BECAUSE IT IS UNREACHABLE OVER THE WIRE, AND THAT IS BY DESIGN. The signing-key
    /// provider derives the published set FROM the same options the handler compares it against, so a
    /// booted host cannot be made to disagree with itself - which is exactly the property that makes the
    /// handler's consistency check a defence against a future refactor rather than against today's code.
    /// Handing the handler a provider built on one configuration and a second, divergent configuration is
    /// the only way to observe the branch, and the internals it needs are reachable because the
    /// application project grants this assembly visibility for precisely this purpose.
    /// </para>
    /// <para>
    /// The published problem is asserted to be the SHARED shape - the status the return code maps to, the
    /// symbolic return-code title, the return-code extension member and the severity extension member -
    /// all of them read through the shared factory's own constants rather than through literals, so a
    /// second error shape cannot appear here without this row failing. The detail is asserted to disclose
    /// nothing: it must not carry the identifier, the algorithm, the issuer or any part of the material.
    /// </para>
    /// </remarks>
    [Fact]
    public void AKeySetThatDisagreesWithTheConfigurationFailsWithTheSharedProblemShape()
    {
        string material = SecurityAppFactory.CreateSigningKeyMaterial(ProbeKeySizeInBits);

        using SigningKeyProvider provider = new(Options.Create(CreateProbeOptions(material)));

        SecurityOptions divergent = CreateProbeOptions(material);
        divergent.SigningKeyId = DivergentKeyIdentifier;

        Assert.NotNull(JwksEndpoints.DescribeKeySetInconsistency(provider.PublishedKeySet, divergent));

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(
            JwksEndpoints.PublishKeySet(
                provider,
                Options.Create(divergent),
                NullLoggerFactory.Instance));

        AssertSharedProblemShape(problem);

        // The identifier that diverged, the algorithm and the issuer must not be echoed, and neither
        // must the material. A structured classifier is published; a disclosure is not.
        string detail = Assert.IsType<string>(problem.ProblemDetails.Detail);

        Assert.False(
            detail.Contains(DivergentKeyIdentifier, StringComparison.OrdinalIgnoreCase)
                || detail.Contains(ProbeKeyIdentifier, StringComparison.OrdinalIgnoreCase)
                || detail.Contains(ProbeIssuer, StringComparison.OrdinalIgnoreCase)
                || detail.Contains(SecurityAlgorithms.RsaSha256, StringComparison.OrdinalIgnoreCase),
            "The published problem detail echoes a configured value. The detail is a structured "
            + "classifier and must disclose nothing about the configuration or the material.");

        Assert.False(
            DetailCarriesArmourOrKeyMaterial(detail, material),
            "The published problem detail carries key material or an armoured marker.");
    }

    /// <summary>
    /// A key set carrying an algorithm outside the permitted set fails rather than being published.
    /// </summary>
    /// <remarks>
    /// A SEPARATE BRANCH FROM THE IDENTIFIER DISAGREEMENT, AND A MORE DANGEROUS ONE. An algorithm the
    /// issuer does not sign with can only reach the published document through a defect, and publishing
    /// it would invite a consumer to constrain itself to an algorithm no token ever carries. The
    /// published key here CLAIMS a keyed-hash algorithm, which is the family the issuer refuses
    /// structurally because a symmetric entry on an anonymous key set would publish the signing secret
    /// itself.
    /// </remarks>
    [Fact]
    public void AKeySetCarryingAnUnpermittedAlgorithmIsRefused()
    {
        SecurityOptions options = CreateProbeOptions(
            SecurityAppFactory.CreateSigningKeyMaterial(ProbeKeySizeInBits));

        PublishedJsonWebKeySet unpermitted = new(
            new PublishedJsonWebKey(
                keyType: AsymmetricKeyFamily,
                use: SignatureUse,
                algorithm: SecurityAlgorithms.HmacSha256,
                keyId: ProbeKeyIdentifier,
                modulus: PlaceholderPublicComponent,
                exponent: PlaceholderPublicComponent));

        Assert.DoesNotContain(
            SecurityAlgorithms.HmacSha256,
            SecurityOptionsValidator.PermittedSigningAlgorithms);

        Assert.NotNull(JwksEndpoints.DescribeKeySetInconsistency(unpermitted, options));

        // And an incomplete key is refused too: a blank public parameter would serialize to a member a
        // consumer cannot decode, which is worse than an absent one because it looks conformant.
        PublishedJsonWebKeySet incomplete = new(
            new PublishedJsonWebKey(
                keyType: AsymmetricKeyFamily,
                use: SignatureUse,
                algorithm: options.SigningAlgorithm,
                keyId: options.SigningKeyId,
                modulus: string.Empty,
                exponent: PlaceholderPublicComponent));

        Assert.NotNull(JwksEndpoints.DescribeKeySetInconsistency(incomplete, options));

        // The consistent case is asserted in the same row so the three refusals above cannot be passing
        // because the check refuses everything.
        PublishedJsonWebKeySet consistent = new(
            new PublishedJsonWebKey(
                keyType: AsymmetricKeyFamily,
                use: SignatureUse,
                algorithm: options.SigningAlgorithm,
                keyId: options.SigningKeyId,
                modulus: PlaceholderPublicComponent,
                exponent: PlaceholderPublicComponent));

        Assert.Null(JwksEndpoints.DescribeKeySetInconsistency(consistent, options));
    }

    /// <summary>
    /// Every metadata fault the discovery handler can detect fails with the shared problem shape.
    /// </summary>
    /// <param name="faultName">The fault this row injects.</param>
    /// <remarks>
    /// ONE ROW PER BRANCH, so a failure names the branch that stopped being detected. Each fault is one a
    /// misconfiguration could genuinely produce, and each would otherwise publish a discovery document a
    /// consumer cannot use: a blank or relative issuer cannot be compared against a token's issuer claim,
    /// and an unrooted path cannot be composed into an absolute address at all. The consistent case is
    /// asserted alongside them so the four refusals cannot be passing because the check refuses
    /// everything.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MetadataFaults))]
    public void EveryMetadataFaultFailsWithTheSharedProblemShape(string faultName)
    {
        string material = SecurityAppFactory.CreateSigningKeyMaterial(ProbeKeySizeInBits);

        Assert.Null(JwksEndpoints.DescribeMetadataInconsistency(CreateProbeOptions(material)));

        SecurityOptions faulted = CreateProbeOptions(material);

        switch (faultName)
        {
            case BlankIssuerFault:
                faulted.Issuer = string.Empty;
                break;

            case RelativeIssuerFault:
                faulted.Issuer = RelativeIssuerValue;
                break;

            case UnrootedKeySetPathFault:
                faulted.JwksPath = UnrootedPathValue;
                break;

            case UnrootedTokenEndpointPathFault:
                faulted.TokenEndpointPath = UnrootedPathValue;
                break;

            default:
                Assert.Fail($"Unhandled metadata fault '{faultName}'.");
                return;
        }

        Assert.NotNull(JwksEndpoints.DescribeMetadataInconsistency(faulted));

        using SigningKeyProvider provider = new(Options.Create(CreateProbeOptions(material)));

        ProblemHttpResult problem = Assert.IsType<ProblemHttpResult>(
            JwksEndpoints.PublishProviderMetadata(
                provider,
                Options.Create(faulted),
                NullLoggerFactory.Instance));

        AssertSharedProblemShape(problem);

        string detail = Assert.IsType<string>(problem.ProblemDetails.Detail);

        Assert.False(
            DetailCarriesArmourOrKeyMaterial(detail, material),
            "The published problem detail carries key material or an armoured marker.");
    }

    /// <summary>
    /// A consistent configuration publishes both documents rather than a problem.
    /// </summary>
    /// <remarks>
    /// THE POSITIVE COUNTERPART TO GROUP 6, driven through the same direct entry points the failure rows
    /// use. Without it, every refusal above would be consistent with a handler that refuses
    /// unconditionally, and the whole group would be vacuous. It also covers the success arm of both
    /// handlers at the unit level, where every published value comes from the supplied options - neither
    /// handler reads the request at all, which is what makes both addresses independent of the caller.
    /// </remarks>
    [Fact]
    public void AConsistentConfigurationPublishesBothDocuments()
    {
        SecurityOptions options = CreateProbeOptions(
            SecurityAppFactory.CreateSigningKeyMaterial(ProbeKeySizeInBits));

        using SigningKeyProvider provider = new(Options.Create(options));

        Ok<JsonWebKeySetDocument> keySet = Assert.IsType<Ok<JsonWebKeySetDocument>>(
            JwksEndpoints.PublishKeySet(provider, Options.Create(options), NullLoggerFactory.Instance));

        JsonWebKeySetDocument keySetBody = Assert.IsType<JsonWebKeySetDocument>(keySet.Value);
        JsonWebKeyDocument publishedKey = Assert.Single(keySetBody.Keys);

        Assert.Equal(AsymmetricKeyFamily, publishedKey.KeyType);
        Assert.Equal(SignatureUse, publishedKey.Use);
        Assert.Equal(options.SigningAlgorithm, publishedKey.Algorithm);
        Assert.Equal(options.SigningKeyId, publishedKey.KeyId);
        Assert.False(
            string.IsNullOrWhiteSpace(publishedKey.Modulus),
            "The projected key carries no public modulus.");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedKey.Exponent),
            "The projected key carries no public exponent.");

        Ok<ProviderMetadataDocument> metadata = Assert.IsType<Ok<ProviderMetadataDocument>>(
            JwksEndpoints.PublishProviderMetadata(
                provider,
                Options.Create(options),
                NullLoggerFactory.Instance));

        ProviderMetadataDocument metadataBody =
            Assert.IsType<ProviderMetadataDocument>(metadata.Value);

        Assert.Equal(options.Issuer, metadataBody.Issuer);

        // Both addresses are the canonical issuer joined to the configured path, asserted in full rather
        // than by suffix: a suffix assertion holds for an address built on any authority at all, which is
        // exactly the defect that composing them from the request produced.
        Assert.Equal(options.Issuer + options.JwksPath, metadataBody.JsonWebKeySetUri);
        Assert.Equal(options.Issuer + options.TokenEndpointPath, metadataBody.TokenEndpoint);
        Assert.Contains(options.SigningAlgorithm, metadataBody.SigningAlgorithms);
    }

    /// <summary>
    /// An issuer configured WITH a trailing separator still publishes single-separator addresses, and is
    /// itself published verbatim.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THIS ROW EXISTS AS A SEPARATE ONE. Both spellings of an authority are legitimate in
    /// configuration and the validator accepts both, so the joining step drops one trailing separator
    /// before appending a rooted path. Every other row here configures an issuer WITHOUT one, so all of
    /// them hold whether or not that trim is present - the trim was live, correct and completely
    /// unasserted, and removing it would have published <c>https://host//.well-known/jwks.json</c> with a
    /// full suite passing.
    /// </para>
    /// <para>
    /// A DOUBLED SEPARATOR IS NOT COSMETIC. A consumer's stock bearer handler fetches <c>jwks_uri</c> as
    /// given; whether a doubled path resolves is the serving host's business, not the consumer's, so an
    /// address this document publishes has to be the one the route actually answers on.
    /// </para>
    /// <para>
    /// THE ISSUER IS ASSERTED UNTRIMMED. It is the <c>iss</c> claim of every minted token and has to match
    /// byte for byte, so the trim belongs to the joined ADDRESSES and must not reach the published
    /// identity. Asserting both in one row is what pins that asymmetry.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATrailingSeparatorOnTheIssuerIsNotDoubledInThePublishedAddresses()
    {
        SecurityOptions options = CreateProbeOptions(
            SecurityAppFactory.CreateSigningKeyMaterial(ProbeKeySizeInBits));

        options.Issuer = ProbeIssuer + "/";

        // The configuration is legitimate: the row is about a valid spelling, not about a refusal.
        Assert.Null(JwksEndpoints.DescribeMetadataInconsistency(options));

        using SigningKeyProvider provider = new(Options.Create(options));

        Ok<ProviderMetadataDocument> metadata = Assert.IsType<Ok<ProviderMetadataDocument>>(
            JwksEndpoints.PublishProviderMetadata(
                provider,
                Options.Create(options),
                NullLoggerFactory.Instance));

        ProviderMetadataDocument metadataBody =
            Assert.IsType<ProviderMetadataDocument>(metadata.Value);

        Assert.Equal(ProbeIssuer + options.JwksPath, metadataBody.JsonWebKeySetUri);
        Assert.Equal(ProbeIssuer + options.TokenEndpointPath, metadataBody.TokenEndpoint);

        // Stated a second way, so a future edit that trims differently cannot satisfy the equalities above
        // by some other route: no published address carries a doubled separator after the scheme.
        Assert.DoesNotContain(
            DoubledSeparator,
            metadataBody.JsonWebKeySetUri[SchemeSeparator.Length..],
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            DoubledSeparator,
            metadataBody.TokenEndpoint[SchemeSeparator.Length..],
            StringComparison.Ordinal);

        // And the identity itself keeps the separator it was configured with.
        Assert.Equal(options.Issuer, metadataBody.Issuer);
        Assert.EndsWith("/", metadataBody.Issuer, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  HELPERS. Every one is private and static. None of them logs, prints or places anything into a
    //  failure message that could carry key material: the fetch helper returns the body to its caller
    //  for structural inspection and asserts only on the status and the media type, and the member and
    //  value readers surface NAMES and booleans rather than rendering values.
    // ==============================================================================================

    /// <summary>
    /// Fetches one published document, asserts it was served as JSON, and returns its raw body.
    /// </summary>
    /// <param name="client">The credential-free client for the host under test.</param>
    /// <param name="path">The configured path of the document.</param>
    /// <returns>The raw response body.</returns>
    /// <remarks>
    /// The status and media type are asserted HERE rather than in every caller, so no row can inspect the
    /// body of a response the service refused to serve and conclude that the shape is correct. The body
    /// itself is returned rather than asserted on: every assertion about its contents is made by a caller
    /// that knows which document it is looking at.
    /// </remarks>
    private static async Task<string> FetchDocumentAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(
            new Uri(path, UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            MediaTypeNames.Application.Json,
            response.Content.Headers.ContentType?.MediaType);

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Parses a served body into an object node.</summary>
    /// <param name="payload">The raw body.</param>
    /// <returns>The root object.</returns>
    /// <remarks>
    /// A node tree rather than a typed response object, and the choice is the whole point of this file: a
    /// deserializer discards members it does not know about, so binding would hide precisely the members
    /// these assertions exist to detect.
    /// </remarks>
    private static JsonObject ParseObject(string payload)
    {
        JsonNode? parsed = JsonNode.Parse(payload);

        Assert.NotNull(parsed);

        JsonObject? root = parsed as JsonObject;

        Assert.NotNull(root);

        return root;
    }

    /// <summary>Reads the single published key out of a key-set document.</summary>
    /// <param name="keySet">The key-set document.</param>
    /// <returns>The single key object.</returns>
    /// <remarks>
    /// EXACTLY ONE KEY IS THE CONTRACT TODAY. The authored contract permits more than one while a
    /// rollover is in progress and explicitly permits exactly one; this issuer publishes one, and
    /// asserting that is asserting the contract as specified rather than demanding a rotation capability
    /// that is not in scope (C-B).
    /// </remarks>
    private static JsonObject RequireSingleKey(JsonObject keySet)
    {
        ImmutableArray<JsonObject> keys = ReadKeys(keySet);

        Assert.Single(keys);

        return keys[0];
    }

    /// <summary>Reads every key object out of a key-set document.</summary>
    /// <param name="keySet">The key-set document.</param>
    /// <returns>The key objects, in document order.</returns>
    private static ImmutableArray<JsonObject> ReadKeys(JsonObject keySet)
    {
        JsonNode? keysNode = keySet[KeysMember];

        Assert.NotNull(keysNode);

        JsonArray? keys = keysNode as JsonArray;

        Assert.NotNull(keys);

        ImmutableArray<JsonObject>.Builder builder = ImmutableArray.CreateBuilder<JsonObject>();

        foreach (JsonNode? entry in keys)
        {
            Assert.NotNull(entry);

            JsonObject? key = entry as JsonObject;

            Assert.NotNull(key);

            builder.Add(key);
        }

        return builder.ToImmutable();
    }

    /// <summary>Reads the member names of an object node.</summary>
    /// <param name="node">The object node.</param>
    /// <returns>The member names, in document order.</returns>
    private static ImmutableArray<string> ReadMemberNames(JsonObject node)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(node.Count);

        foreach (KeyValuePair<string, JsonNode?> member in node)
        {
            builder.Add(member.Key);
        }

        return builder.ToImmutable();
    }

    /// <summary>Reads a required string member of an object node.</summary>
    /// <param name="node">The object node.</param>
    /// <param name="memberName">The member to read.</param>
    /// <returns>The member's value.</returns>
    /// <remarks>
    /// Used only for members that are NON-SECRET BY CONSTRUCTION - a key family, an intended use, an
    /// algorithm identifier, a key identifier, an issuer identifier or an address - because the returned
    /// value goes on to be compared with an equality assertion that would render it into a failure
    /// report. The two public parameters are read through the dedicated helpers instead, which never
    /// surface their contents.
    /// </remarks>
    private static string ReadString(JsonObject node, string memberName)
    {
        JsonNode? member = node[memberName];

        Assert.NotNull(member);

        string? value = member.GetValue<string>();

        Assert.NotNull(value);

        return value;
    }

    /// <summary>Reads a required array-of-string member of an object node.</summary>
    /// <param name="node">The object node.</param>
    /// <param name="memberName">The member to read.</param>
    /// <returns>The member's values, in document order.</returns>
    private static ImmutableArray<string> ReadStringArray(JsonObject node, string memberName)
    {
        JsonNode? member = node[memberName];

        Assert.NotNull(member);

        JsonArray? values = member as JsonArray;

        Assert.NotNull(values);

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(values.Count);

        foreach (JsonNode? entry in values)
        {
            Assert.NotNull(entry);

            string? value = entry.GetValue<string>();

            Assert.NotNull(value);

            builder.Add(value);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Asserts a public parameter is present, non-blank, and in the unpadded URL-safe alphabet.
    /// </summary>
    /// <param name="key">The published key object.</param>
    /// <param name="memberName">The public parameter to check.</param>
    /// <remarks>
    /// <para>
    /// ONE OF THE TWO PLACES THE PUBLIC PARAMETERS ARE INSPECTED, AND IT NEVER SURFACES THEIR CONTENTS.
    /// Every assertion is boolean with a structural message, so no failure path can render a modulus or an
    /// exponent into test output. That matters even though both are public parameters: the habit of
    /// asserting on key-shaped values with equality assertions is exactly how a private one ends up in a
    /// log.
    /// </para>
    /// <para>
    /// The alphabet check is substantive rather than cosmetic. The key-set format requires the URL-safe
    /// alphabet without padding, and a value in the standard alphabet - with its two different symbols and
    /// its padding character - is undecodable by a stock consumer even though it would look entirely
    /// plausible to a reader. The check uses the framework's own validator, so it agrees by construction
    /// with the encoder the signing-key provider uses.
    /// </para>
    /// </remarks>
    private static void AssertUnpaddedBase64Url(JsonObject key, string memberName)
    {
        string value = ReadRawComponent(key, memberName);

        Assert.True(
            Base64Url.IsValid(value.AsSpan()),
            $"The published key's '{memberName}' member is not in the unpadded URL-safe alphabet the "
            + "key-set format requires, so a stock consumer cannot decode it. The value is deliberately "
            + "not reproduced in this message.");
    }

    /// <summary>Reads a public parameter without ever surfacing it in a message.</summary>
    /// <param name="key">The published key object.</param>
    /// <param name="memberName">The parameter to read.</param>
    /// <returns>The encoded parameter.</returns>
    private static string ReadRawComponent(JsonObject key, string memberName)
    {
        JsonNode? member = key[memberName];

        Assert.NotNull(member);

        string? value = member.GetValue<string>();

        Assert.False(
            string.IsNullOrWhiteSpace(value),
            $"The published key's '{memberName}' member is absent, empty or blank. Without it a "
            + "consumer cannot verify a signature at all.");

        return value;
    }

    /// <summary>Reports whether a member name appears anywhere in a node tree.</summary>
    /// <param name="node">The node to search.</param>
    /// <param name="memberName">The member name to look for.</param>
    /// <returns><see langword="true"/> when the name appears at any depth.</returns>
    /// <remarks>
    /// SEARCHES THE WHOLE TREE, not just the object it is handed, so a component smuggled into a wrapper,
    /// a nested object or an array element is caught as well. Comparison is ordinal and case-sensitive
    /// because the member names of both published documents are.
    /// </remarks>
    private static bool ContainsMemberAnywhere(JsonNode? node, string memberName)
    {
        switch (node)
        {
            case JsonObject actualObject:
                foreach (KeyValuePair<string, JsonNode?> member in actualObject)
                {
                    if (string.Equals(member.Key, memberName, StringComparison.Ordinal)
                        || ContainsMemberAnywhere(member.Value, memberName))
                    {
                        return true;
                    }
                }

                return false;

            case JsonArray actualArray:
                foreach (JsonNode? entry in actualArray)
                {
                    if (ContainsMemberAnywhere(entry, memberName))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Collects every string value in a node tree.</summary>
    /// <param name="node">The node to walk.</param>
    /// <returns>Every string value found, at any depth.</returns>
    /// <remarks>
    /// Feeds the decode-and-import probe. Values are returned to the caller for inspection and are never
    /// written into a message by this helper or by any caller of it.
    /// </remarks>
    private static ImmutableArray<string> ReadStringValues(JsonNode? node)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();

        CollectStringValues(node, builder);

        return builder.ToImmutable();
    }

    /// <summary>Recursive worker for <see cref="ReadStringValues(JsonNode?)"/>.</summary>
    /// <param name="node">The node to walk.</param>
    /// <param name="builder">The accumulator.</param>
    private static void CollectStringValues(JsonNode? node, ImmutableArray<string>.Builder builder)
    {
        switch (node)
        {
            case JsonObject actualObject:
                foreach (KeyValuePair<string, JsonNode?> member in actualObject)
                {
                    CollectStringValues(member.Value, builder);
                }

                break;

            case JsonArray actualArray:
                foreach (JsonNode? entry in actualArray)
                {
                    CollectStringValues(entry, builder);
                }

                break;

            case JsonValue actualValue:
                if (actualValue.TryGetValue(out string? text) && text is not null)
                {
                    builder.Add(text);
                }

                break;

            default:
                break;
        }
    }

    /// <summary>Reports whether a candidate string decodes to an asymmetric PRIVATE key.</summary>
    /// <param name="candidate">The string to probe.</param>
    /// <returns><see langword="true"/> when the candidate is importable private key material.</returns>
    /// <remarks>
    /// <para>
    /// THE DETERMINISTIC HALF OF THE RAW-TEXT DEFENCE, AND THE REASON IT IS DETERMINISTIC RATHER THAN
    /// PROBABILISTIC. A prefix or substring heuristic over encoded text can both miss a real leak and fire
    /// on a public modulus by coincidence, which on a security control is the worst of both outcomes -
    /// false assurance plus an intermittent failure that invites suppression. Attempting the import
    /// instead cannot do either: a public parameter is not a private-key structure, so it fails every
    /// import path, while an actual private key in either accepted encoding succeeds on one of them.
    /// </para>
    /// <para>
    /// THE ENCODINGS PROBED ARE EXACTLY THE ONES THE SIGNING-KEY PROVIDER ITSELF ACCEPTS - the armoured
    /// text form, and the single-line encoding of the bare binary structure in both its wrapped and
    /// unwrapped shapes - because those are the shapes a leak would arrive in. The URL-safe alphabet is
    /// probed alongside the standard one, since that is the alphabet this document's own members use.
    /// </para>
    /// <para>
    /// Nothing here logs, returns or retains any decoded byte, and every buffer it decodes is zeroed
    /// before it is released.
    /// </para>
    /// </remarks>
    private static bool DecodesToAnAsymmetricPrivateKey(string candidate)
    {
        if (CarriesArmourMarker(candidate) && ImportsAsArmouredPrivateKey(candidate))
        {
            return true;
        }

        byte[]? decoded = TryDecodeStandard(candidate) ?? TryDecodeUrlSafe(candidate);

        if (decoded is null)
        {
            return false;
        }

        try
        {
            return ImportsAsBareStructure(
                       decoded,
                       static (key, bytes) => key.ImportPkcs8PrivateKey(bytes, out _))
                   || ImportsAsBareStructure(
                       decoded,
                       static (key, bytes) => key.ImportRSAPrivateKey(bytes, out _));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
        }
    }

    /// <summary>Reports whether a string carries one of the armour markers.</summary>
    /// <param name="text">The text to scan.</param>
    /// <returns><see langword="true"/> when a marker is present.</returns>
    private static bool CarriesArmourMarker(string text)
    {
        foreach (string marker in ArmourMarkers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reports whether armoured text imports as a key carrying a private component.</summary>
    /// <param name="candidate">The armoured text.</param>
    /// <returns><see langword="true"/> when the import succeeds and a private component is present.</returns>
    private static bool ImportsAsArmouredPrivateKey(string candidate)
    {
        using RSA probe = RSA.Create();

        try
        {
            probe.ImportFromPem(candidate);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }

        byte[]? exported = null;

        try
        {
            exported = probe.ExportPkcs8PrivateKey();

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

    /// <summary>Reports whether a decoded buffer imports through one bare-structure path.</summary>
    /// <param name="bare">The decoded buffer.</param>
    /// <param name="import">The import path to attempt.</param>
    /// <returns><see langword="true"/> when the import succeeds.</returns>
    private static bool ImportsAsBareStructure(byte[] bare, Action<RSA, byte[]> import)
    {
        using RSA probe = RSA.Create();

        try
        {
            import(probe, bare);

            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Decodes a candidate in the standard alphabet, or reports failure.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The decoded bytes, or <see langword="null"/> when the candidate is not in that alphabet.</returns>
    private static byte[]? TryDecodeStandard(string candidate)
    {
        try
        {
            return Convert.FromBase64String(candidate);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Decodes a candidate in the URL-safe alphabet, or reports failure.</summary>
    /// <param name="candidate">The candidate.</param>
    /// <returns>The decoded bytes, or <see langword="null"/> when the candidate is not in that alphabet.</returns>
    private static byte[]? TryDecodeUrlSafe(string candidate)
    {
        try
        {
            return Base64Url.DecodeFromChars(candidate.AsSpan());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Builds a verification key from the parameters a consumer would have FETCHED.</summary>
    /// <param name="key">The published key object.</param>
    /// <returns>The verification key.</returns>
    /// <remarks>
    /// <para>
    /// Rebuilt from the published parameters rather than taken from the host's service graph, so the
    /// assertion it feeds is about the DOCUMENT rather than about the process. The parameters overload is
    /// used deliberately: it copies the values, so this helper owns no disposable and can hand back a key
    /// with no lifetime for a caller to manage.
    /// </para>
    /// <para>
    /// The CONCRETE key type is returned rather than the abstract base. Validation parameters accept the
    /// base type, so either would compile; returning the concrete one avoids a needless widening and
    /// keeps this file clean at the strictest analyzer severity, which is the bar the highest-severity
    /// file in the project should be held to.
    /// </para>
    /// </remarks>
    private static RsaSecurityKey BuildVerificationKey(JsonObject key) =>
        new(
            new RSAParameters
            {
                Modulus = Base64Url.DecodeFromChars(ReadRawComponent(key, ModulusMember).AsSpan()),
                Exponent = Base64Url.DecodeFromChars(ReadRawComponent(key, ExponentMember).AsSpan()),
            })
        {
            KeyId = ReadString(key, KeyIdentifierMember),
        };

    /// <summary>
    /// Builds an options instance for the direct-call rows around GENERATED signing material.
    /// </summary>
    /// <param name="signingKeyMaterial">Material generated by the host factory's own generator.</param>
    /// <returns>A consistent options instance.</returns>
    /// <remarks>
    /// The three published paths, the lifetime and the key-store descriptor are left at their declared
    /// defaults, so this helper states only what a row varies. The material is always a parameter and
    /// never a literal (C-F).
    /// </remarks>
    private static SecurityOptions CreateProbeOptions(string signingKeyMaterial)
    {
        SecurityOptions options = new()
        {
            Issuer = ProbeIssuer,
            SigningKeyId = ProbeKeyIdentifier,
            SigningAlgorithm = SecurityAlgorithms.RsaSha256,
            SigningKey = signingKeyMaterial,
        };

        options.Audiences.Add(ProbeAudience);

        return options;
    }

    /// <summary>
    /// Asserts a published problem is the SHARED problem shape rather than a second error shape.
    /// </summary>
    /// <param name="problem">The published problem.</param>
    /// <remarks>
    /// Every expectation is read through the shared factory's own members - the status the return code
    /// maps to, the symbolic title the shared formatter produces, and the two extension member names - so
    /// a divergence between this file and the shared factory is impossible by construction. The content
    /// type is asserted too, because a consumer distinguishes a problem from a payload by it.
    /// </remarks>
    private static void AssertSharedProblemShape(ProblemHttpResult problem)
    {
        Assert.Equal(MediaTypeNames.Application.ProblemJson, problem.ContentType);
        Assert.Equal(ProblemResults.MapStatusCode(RetCode.E_INTERNAL_ERROR), problem.StatusCode);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.Equal(Formatting.FormatRetCode(RetCode.E_INTERNAL_ERROR), problem.ProblemDetails.Title);

        Assert.Equal(
            RetCode.E_INTERNAL_ERROR,
            Assert.IsType<long>(
                problem.ProblemDetails.Extensions[ProblemResults.RetCodeExtensionMember]));

        Assert.Equal(
            ProblemResults.DescribeSeverity(ProblemSeverity.StopSign),
            Assert.IsType<string>(
                problem.ProblemDetails.Extensions[ProblemResults.MessageSeverityExtensionMember]));

        // The failing return code must classify as a failure under the preserved tri-state algebra and
        // must not be mistakable for a success. The predicates are CONSUMED from the shared kernel.
        Assert.True(Predicates.IsFailed(RetCode.E_INTERNAL_ERROR));
        Assert.False(ProblemResults.ClaimsSuccess(RetCode.E_INTERNAL_ERROR));
    }

    /// <summary>
    /// Reports whether a published detail carries an armour marker or any run of the material.
    /// </summary>
    /// <param name="detail">The published problem detail.</param>
    /// <param name="material">The generated signing material the row supplied.</param>
    /// <returns><see langword="true"/> when the detail discloses something it must not.</returns>
    /// <remarks>
    /// The material is checked in fragments as well as whole, because a PARTIAL echo is as damaging as a
    /// complete one and is far easier to introduce by accident. Neither the detail nor the material is
    /// written into any message by this helper - it answers a boolean and the caller supplies a
    /// structural message.
    /// </remarks>
    private static bool DetailCarriesArmourOrKeyMaterial(string detail, string material)
    {
        if (CarriesArmourMarker(detail))
        {
            return true;
        }

        foreach (string fragment in MaterialFragments(material))
        {
            if (detail.Contains(fragment, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Splits generated material into fragments long enough to be recognisable.</summary>
    /// <param name="material">The generated material.</param>
    /// <returns>Fragments to search a message for.</returns>
    private static ImmutableArray<string> MaterialFragments(string material)
    {
        const int fragmentLength = 16;

        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();

        string compact = material
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        for (int start = 0; start + fragmentLength <= compact.Length; start += fragmentLength)
        {
            builder.Add(compact.Substring(start, fragmentLength));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Maps the legacy digest selector onto the algorithm identifier this issuer publishes.
    /// </summary>
    /// <param name="legacyDigestSelector">The preserved legacy digest constant.</param>
    /// <returns>The corresponding published algorithm identifier.</returns>
    /// <remarks>
    /// <para>
    /// THE PROVENANCE MAP, AND IT IS DELIBERATELY NARROW. The legacy catalogue's comment records that ONE
    /// digest set governs the hash primitive, <c>RSASign</c> AND <c>VerifyRSASign</c>
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and this issuer signs with the SHA-256 member of
    /// that set [:L930]. Only the three digests the closed algorithm set admits are mapped: the remaining
    /// members of the legacy catalogue - the two legacy-weak digests and the checksum - are NOT signature
    /// digests for this issuer, and mapping them would imply this service may sign with them.
    /// </para>
    /// <para>
    /// The constants are CONSUMED from the shared kernel. Nothing is re-declared here, which is what keeps
    /// this file outside the scoped naming suppressions the preserved spellings need.
    /// </para>
    /// </remarks>
    private static string DescribeLegacyDigestAsAlgorithmIdentifier(long legacyDigestSelector) =>
        legacyDigestSelector switch
        {
            Enums.CRYPTO_HASH_SHA256 => SecurityAlgorithms.RsaSha256,
            Enums.CRYPTO_HASH_SHA384 => SecurityAlgorithms.RsaSha384,
            Enums.CRYPTO_HASH_SHA512 => SecurityAlgorithms.RsaSha512,
            _ => throw new ArgumentOutOfRangeException(
                nameof(legacyDigestSelector),
                "The legacy digest selector is not one this issuer signs with. The permitted set is "
                + "closed and is published by the options validator; a selector outside it has no "
                + "algorithm identifier here, and inventing one would imply this service may sign with "
                + "it."),
        };

    /// <summary>Returns member names in a stable order for set comparison.</summary>
    /// <param name="members">The names to order.</param>
    /// <returns>The names, ordinally sorted.</returns>
    /// <remarks>
    /// Member ORDER on the wire is deliberately not asserted anywhere in this file - see the file header
    /// on C-B - so every set comparison goes through this helper. Sorting ordinally rather than by culture
    /// keeps the comparison independent of the machine running it.
    /// </remarks>
    private static ImmutableArray<string> Sorted(ImmutableArray<string> members) =>
        [.. members.Sort(StringComparer.Ordinal)];
}
