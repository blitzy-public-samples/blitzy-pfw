// ==================================================================================================
//  CryptoWeakDefaultAnnotationTests - THE PRESERVED-WEAKNESS SUITE FOR CONTRACT C-02
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, the CryptoService half of
//            contract C-02, read through the assembly fixture OpenApiContractDocuments.
//
//  ORACLE    ws_objects/pfw.crypto.pbl.src/n_crypto.sru   (65 overloads, native "pfw.dll")
//            ws_objects/pfw.shared.pbl.src/enums.sru       (the CRYPTO_* catalogue, L924 to L967)
//            Both are READ AS CITATIONS ONLY. Nothing in this file opens a legacy file at runtime.
//
//  THE ONE THING TO UNDERSTAND BEFORE EDITING THIS FILE
//  ------------------------------------------------------------------------------------------------
//  This is the one suite in the folder where the obvious instinct is exactly wrong. The instinct is
//  to assert that the cryptography is SAFE. The requirement is the opposite: the legacy weakness is
//  PRESERVED, and the test's job is to prove a caller can SEE the risk, not to remove it.
//
//  Constraint C-B (AAP 0.7.3) forbids behaviour improvements exactly as firmly as it forbids
//  regressions, and a "hardening" assertion in a test is a behavioural change smuggled in through
//  the back door: it makes the safe contract the passing one and the faithful contract the failing
//  one, so the next author fixes the contract to satisfy the test. Stated as a rule with no
//  exceptions:
//
//      NO ASSERTION IN THIS FILE MAY PASS ONLY BECAUSE THE CONTRACT WAS MADE SAFER.
//
//  Concretely, and these are the six things reviewers should grep for, none of which appears below:
//      no row demands an AEAD mode (GCM, CCM, Poly1305)
//      no row demands OAEP be the default RSA padding
//      no row demands a key-derivation function, a salt or an iteration count
//      no row demands a minimum RSA key size, and 2048 is asserted nowhere as a floor
//      no row demands the removal of ECB, MD5, SHA-1, CRC32 or DES
//      no row demands a symmetric padding selector
//  Every row asserts two things instead: the PRESERVED DEFAULT (or preserved legal value) is still
//  there, and an ANNOTATION recording it as a known legacy weakness is still reachable. Each fails
//  in exactly two situations, which is the C-B self-audit criterion applied literally: the default
//  disappeared, or the annotation disappeared.
//
//  THE SIX WEAKNESSES THIS SUITE PINS, EACH WITH THE LOCATOR THAT PROVES THE DEFAULT  (C-K)
//  ------------------------------------------------------------------------------------------------
//   W1  The default symmetric mode is ECB.
//       enums.sru:L946 declares CRYPTO_SYMCRYPT_MODE_DEFAULT = CRYPTO_SYMCRYPT_MODE_ECB, and 8 of
//       the 32 symmetric overloads at n_crypto.sru:L30-L61 omit the mode argument entirely, so
//       omitting it runs in ECB. Mode set is exactly ECB=0, CBC=1, CFB=2 [enums.sru:L943-L945].
//
//   W2  The default RSA padding is PKCS#1 v1.5, and no-padding is not selectable.
//       enums.sru:L951 declares CRYPTO_RSA_PADDING_DEFAULT = CRYPTO_RSA_PADDING_PKCS1, and 4 of the
//       8 RSA cipher overloads at n_crypto.sru:L62-L69 omit the argument. The enumeration has
//       EXACTLY TWO members [enums.sru:L949-L950], and that two-member shape IS the mechanism by
//       which no-padding is refused: there is no third constant for a caller to select, so the
//       absence in the oracle is the legacy's own rejection. It is structural, not a runtime check.
//
//   W3  Symmetric padding is PKCS#5-family only and is not selectable.
//       Mechanical proof, and the reason this row is an ABSENCE assertion: not one of the 32
//       symmetric overloads at n_crypto.sru:L30-L61 has a padding parameter, and the cipher block
//       at enums.sru:L936-L946 exposes a type and a mode and nothing else. Measured on the oracle:
//       the token "padding" occurs at exactly four sites in n_crypto.sru (L63, L65, L67, L69) and
//       every one of them is RSAEncrypt or RSADecrypt.
//
//   W4  No key-derivation function is reachable anywhere in the surface.
//       Mechanical proof: no salt, iteration count or derivation parameter appears in any of the 65
//       declarations at n_crypto.sru:L9-L73, and enums.sru declares no derivation constant of any
//       kind. A passphrase used as key material is therefore used as raw key bytes, unstretched and
//       unsalted.
//
//   W5  There is no authenticated encryption, so ciphertext carries no integrity tag.
//       Mechanical proof: the mode set is exactly the three members of W1. There is no GCM, no CCM
//       and no Poly1305 constant in enums.sru, and no tag or associated-data parameter anywhere in
//       n_crypto.sru.
//
//   W6  1024-bit RSA remains a legal key size.
//       enums.sru:L965 declares CRYPTO_RSA_BITS_1024 = 1024 as a first-class value beside 2048
//       [:L966] and 4096 [:L967], and GenRSAKey takes a plain readonly uint with no constraint of
//       its own [n_crypto.sru:L19-L20]. Note what this row therefore must NOT do: asserting a
//       minimum of 2048 would invent a guard the legacy does not have.
//
//  W3, W4 and W5 are established by ABSENCE. Absence is weaker evidence than presence in general,
//  and it is exactly the right kind here: a capability the legacy cannot express is a capability
//  this contract must not offer, because offering it would be a new feature. Each of the three
//  therefore asserts the absence AND the annotation that explains it, so neither half stands alone.
//
//  HOW AN ANNOTATION IS MATCHED, AND WHY IT IS NOT AN EXACT SENTENCE
//  ------------------------------------------------------------------------------------------------
//  Matching is CASE-INSENSITIVE and VOCABULARY-BASED. A row carries a set of vocabulary GROUPS;
//  each group is a set of synonyms, and a surface satisfies the group when it contains any one of
//  them. Every group must be satisfied by the same surface for that surface to count as carrying
//  the annotation.
//
//  An exact-sentence match was deliberately rejected. The prose belongs to the document's author,
//  and a brittle string comparison would make the contract un-editable: rewording a warning for
//  clarity would break a test that has no opinion about wording. What this suite has an opinion
//  about is whether the CONCERN is annotated at all. So the vocabulary is spelled out in
//  WeaknessVocabulary below, in one place, where it can be audited and extended rather than
//  scattered through assertions.
//
//  WHERE AN ANNOTATION IS ALLOWED TO LIVE
//  ------------------------------------------------------------------------------------------------
//  Anywhere on the REACHABLE SURFACE for that concern, which is the union of: the component schemas
//  named by the row, the summary and description of the operations named by the row, and the
//  document-level Info.Description that carries the authoritative weakness register. A caller
//  reading a generated client sees all three, so requiring one specific home would be an opinion
//  about document layout rather than about disclosure. Each row REPORTS which surfaces matched
//  through ITestOutputHelper, so a passing run still records where the annotation was found.
//
//  A measured fact that makes this generous policy sound rather than loose: in Microsoft.OpenApi
//  2.11.0 a $ref property is an OpenApiSchemaReference that PROXIES its target, so
//  SymEncryptRequest.Properties["mode"].Description returns the CryptoSymCryptMode description
//  verbatim. An annotation on the component schema is therefore genuinely reachable from the
//  request property a caller fills in, not merely nearby.
//
//  PURITY  (constraints C-A and C-F)
//  ------------------------------------------------------------------------------------------------
//  NO CRYPTOGRAPHIC OPERATION IS PERFORMED ANYWHERE IN THIS SUITE. Nothing is hashed, encrypted,
//  decrypted, signed or verified; System.Security.Cryptography is not referenced. The assertions
//  are about a parsed document's enum members, defaults, extension arrays, property names and
//  description text, and nothing else.
//
//  NO KEY, PASSPHRASE, INITIALIZATION VECTOR, CERTIFICATE OR TOKEN LITERAL APPEARS IN THIS FILE.
//  The strings below are OpenAPI schema names, JSON property names, legacy constant identifier
//  spellings and English vocabulary. AAP 0.6.6 requires the never-replicate posture for the eight
//  in-source secret sites, and this file has no need of a secret to test a description.
//
//  No clock, no randomness, no environment variable, no network, and no I/O beyond the fixture the
//  assembly already loaded. Repeatability is the hard prerequisite of the Golden-Master approach
//  this repository adopts (AAP 0.6.7).
//
//  BINDING CONSTRAINTS AT THIS SITE
//  One consequence of that worth stating, because it shaped the code: the legacy identifier
//  spellings appear ONLY as string literals and in comments, never as C# identifiers. .editorconfig
//  BAND 3 scopes its CA1707 suppression to the individual files that genuinely declare the
//  preserved constants, and says in terms that the narrowness is the design and a glob must not be
//  widened. This file is not one of those files and does not need to be: the spellings it checks are
//  DATA it compares against x-enum-varnames, not names it declares.
// ==================================================================================================

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Xunit;
using Xunit.Sdk;

namespace PowerFramework.Contracts.Tests;

/// <summary>
/// Asserts that every preserved cryptographic weakness of contract C-02 is still present in
/// <c>security.v1.yaml</c> and is still annotated as a known legacy weakness.
/// </summary>
/// <remarks>
/// <para>
/// The suite deliberately never asserts that the cryptography is safe. Constraint C-B forbids
/// correcting a legacy defect, so each row here asserts the preserved default plus the annotation
/// that discloses it. A row fails only when the default disappears or the annotation disappears.
/// </para>
/// <para>
/// Obtains the parsed document through a constructor parameter of type
/// <see cref="OpenApiContractDocuments"/>, which is the one convention of this folder: the fixture is
/// registered once for the whole assembly in <c>ContractTestContext.cs</c>.
/// </para>
/// </remarks>
/// <param name="documents">The assembly-wide OpenAPI fixture supplying the parsed Security document.</param>
/// <param name="output">Sink used to report which surface carried each annotation.</param>
public sealed class CryptoWeakDefaultAnnotationTests(
    OpenApiContractDocuments documents,
    ITestOutputHelper output)
{
    // ==============================================================================================
    //  SCHEMA AND OPERATION NAMES
    //  Component-schema names and operation identifiers as security.v1.yaml declares them. Held as
    //  constants so a rename shows up as one compile-time edit rather than as a scattered set of
    //  silently unmatched string literals.
    // ==============================================================================================

    private const string EncodingSchema = "CryptoEncoding";
    private const string HashTypeSchema = "CryptoHashType";
    private const string KeyedHashTypeSchema = "CryptoKeyedHashType";
    private const string CipherTypeSchema = "CryptoSymCryptType";
    private const string CipherModeSchema = "CryptoSymCryptMode";
    private const string RsaPaddingSchema = "CryptoRsaPadding";
    private const string RandomStringFlagsSchema = "CryptoRndFlags";
    private const string GuidFlagsSchema = "CryptoGuidFlags";
    private const string RsaKeyBitsSchema = "CryptoRsaKeyBits";
    private const string KeyReferenceSchema = "KeyReference";
    private const string SymmetricEncryptRequestSchema = "SymEncryptRequest";
    private const string SymmetricDecryptRequestSchema = "SymDecryptRequest";
    private const string RsaCipherRequestSchema = "RsaCipherRequest";
    private const string GenerateRsaKeyRequestSchema = "GenRsaKeyRequest";

    private const string SymmetricEncryptOperation = "symmetricEncrypt";
    private const string SymmetricDecryptOperation = "symmetricDecrypt";
    private const string RsaEncryptOperation = "rsaEncrypt";
    private const string RsaDecryptOperation = "rsaDecrypt";
    private const string GenerateRsaKeyOperation = "generateRsaKey";

    /// <summary>Tag every CryptoService operation carries, used to select the crypto surface.</summary>
    private const string CryptoServiceTag = "CryptoService";

    /// <summary>Specification extension naming a closed enumeration's members in member order.</summary>
    private const string EnumVarnamesExtension = "x-enum-varnames";

    /// <summary>Specification extension listing an open bitmask's individual bit values.</summary>
    private const string BitmaskValuesExtension = "x-bitmask-values";

    /// <summary>Specification extension naming an open bitmask's individual bits in value order.</summary>
    private const string BitmaskVarnamesExtension = "x-bitmask-varnames";

    /// <summary>Specification extension naming the legacy constant that supplies a schema default.</summary>
    private const string LegacyDefaultIdentifierExtension = "x-legacy-default-identifier";

    /// <summary>Specification extension listing the legacy convenience values of an open numeric domain.</summary>
    private const string LegacyPredefinedValuesExtension = "x-legacy-predefined-values";

    /// <summary>Specification extension naming the legacy convenience values of an open numeric domain.</summary>
    private const string LegacyPredefinedVarnamesExtension = "x-legacy-predefined-varnames";

    /// <summary>
    /// The extension a schema records its legacy domain ceiling in when its own maximum carries a
    /// service-level cap instead.
    /// </summary>
    /// <remarks>
    /// The convention predates the key-size cap: RandomSize has published its legacy 32-bit domain this
    /// way while capping its own maximum at 1 MiB. Recording the domain rather than discarding it is what
    /// keeps the schema a faithful statement of the legacy parameter AND a usable statement of what this
    /// service will actually do.
    /// </remarks>
    private const string LegacyDomainMaximumExtension = "x-legacy-domain-maximum";

    // ==============================================================================================
    //  THE VOCABULARY  -  the single auditable place where "is this annotated?" is defined
    //  ----------------------------------------------------------------------------------------------
    //  Every group below is a SYNONYM SET. A surface satisfies a group when its text contains any one
    //  member of that group, compared case-insensitively. A row lists the groups it requires, and all
    //  of a row's groups must be satisfied by the SAME surface before that surface counts.
    //
    //  These are deliberately phrase fragments rather than sentences. The document currently spells
    //  its warnings "## KNOWN LEGACY WEAKNESS ..." and marks each preservation decision "(C-B)", but
    //  this suite must survive that prose being rewritten, so several alternative spellings are
    //  accepted for every concern. Extending a group is the supported way to accommodate a rewording;
    //  narrowing one to a single exact sentence is not, because it re-creates the brittleness these
    //  groups exist to avoid.
    // ==============================================================================================

    /// <summary>
    /// Marks text as disclosing a preserved defect rather than merely describing a field. Every row
    /// requires this group, because a description that states the default without flagging it as a
    /// weakness discloses nothing.
    /// </summary>
    private static readonly string[] WeaknessDisclosureVocabulary =
    [
        "known legacy weakness",
        "legacy weakness",
        "preserved weakness",
        "preserved legacy weakness",
        "known weakness",
    ];

    /// <summary>
    /// Marks text as recording that the weakness is kept ON PURPOSE. This is what distinguishes an
    /// annotation from an apology, and it is the sentence a caller needs in order to trust that the
    /// behaviour will not change under them.
    /// </summary>
    private static readonly string[] DeliberatePreservationVocabulary =
    [
        "not silently strengthened",
        "not silently upgraded",
        "is not removed",
        "not removed from the accepted set",
        "preserved as the default",
        "it is preserved",
        "is preserved",
        "none corrected",
        "not corrected",
        "(c-b)",
    ];

    /// <summary>W1 vocabulary: the mode that omitting the field selects.</summary>
    private static readonly string[] ElectronicCodebookVocabulary = ["ecb"];

    /// <summary>W2 vocabulary: the padding scheme that omitting the field selects.</summary>
    private static readonly string[] Pkcs1PaddingVocabulary = ["pkcs#1", "pkcs1", "pkcs #1"];

    /// <summary>W2 vocabulary: the fact that a caller cannot ask for unpadded RSA.</summary>
    private static readonly string[] NoPaddingRefusedVocabulary =
    [
        "no-padding is not selectable",
        "no padding is not selectable",
        "no-padding",
        "no such constant",
        "no no-padding constant",
    ];

    /// <summary>W3 vocabulary: the symmetric padding scheme, and that it cannot be chosen.</summary>
    private static readonly string[] FixedSymmetricPaddingVocabulary =
    [
        "padding is fixed and not selectable",
        "not selectable",
        "no padding field",
        "pkcs#5",
        "pkcs5",
    ];

    /// <summary>W4 vocabulary: that no key stretching exists and material is used as-is.</summary>
    private static readonly string[] NoKeyDerivationVocabulary =
    [
        "no key-derivation function",
        "no key derivation function",
        "raw key bytes",
        "does not derive, stretch or salt",
        "no salt concept",
    ];

    /// <summary>W5 vocabulary: that ciphertext is unauthenticated.</summary>
    private static readonly string[] NoAuthenticatedEncryptionVocabulary =
    [
        "no authenticated encryption",
        "no integrity tag",
        "carries no integrity tag",
        "no authentication-tag field",
    ];

    /// <summary>W6 vocabulary: the smallest published RSA modulus length, which stays legal.</summary>
    private static readonly string[] SmallestRsaKeySizeVocabulary =
    [
        "1024",
    ];

    // ==============================================================================================
    //  ROW MODELS
    //  ----------------------------------------------------------------------------------------------
    //  Rows are records so that the table reads as data. ToString is overridden on each so xunit
    //  names the generated test cases after the weakness identifier, which is what makes a failing
    //  run legible at a glance: "W1-ECB-IS-THE-DEFAULT-SYMMETRIC-MODE" rather than a record dump.
    //
    //  All three shapes were verified to build warning-free as TheoryData type arguments under
    //  TreatWarningsAsErrors before being adopted.
    // ==============================================================================================

    /// <summary>
    /// One preserved weakness and the surfaces on which its disclosure is accepted.
    /// </summary>
    /// <param name="Id">Stable identifier, used as the test-case name.</param>
    /// <param name="Concern">What the annotation has to disclose, in one sentence.</param>
    /// <param name="LegacyLocator">The oracle site that proves the preserved behaviour.</param>
    /// <param name="SchemaSurfaces">Component schemas whose description may carry the disclosure.</param>
    /// <param name="OperationSurfaces">Operations whose summary or description may carry it.</param>
    /// <param name="RequiredVocabulary">Synonym groups that must all be satisfied by one surface.</param>
    public sealed record WeaknessRow(
        string Id,
        string Concern,
        string LegacyLocator,
        string[] SchemaSurfaces,
        string[] OperationSurfaces,
        string[][] RequiredVocabulary)
    {
    }

    /// <summary>
    /// A closed legacy enumeration, with the values and identifier spellings the oracle declares.
    /// </summary>
    /// <param name="SchemaName">Component schema declaring the enumeration.</param>
    /// <param name="LegacyLocator">The enums.sru range the values come from.</param>
    /// <param name="Values">Every legal value, in declaration order.</param>
    /// <param name="Identifiers">The legacy identifier spellings, in the same order.</param>
    public sealed record ClosedEnumRow(
        string SchemaName,
        string LegacyLocator,
        long[] Values,
        string[] Identifiers)
    {
    }

    /// <summary>
    /// An open legacy bitmask, whose individual bits compose and so are not a closed enumeration.
    /// </summary>
    /// <param name="SchemaName">Component schema declaring the bitmask.</param>
    /// <param name="LegacyLocator">The enums.sru range the bits come from.</param>
    /// <param name="Bits">Every named bit value, in declaration order.</param>
    /// <param name="Identifiers">The legacy identifier spellings of those bits, in the same order.</param>
    /// <param name="Default">The composed default the oracle declares.</param>
    /// <param name="DefaultIdentifier">The legacy identifier of that composed default.</param>
    /// <param name="DefaultComposedFrom">
    /// The bit identifiers the oracle sums to reach <paramref name="Default"/>, named rather than
    /// inferred. A positional rule would be wrong: the random-string default excludes its last bit
    /// while the GUID default includes both of its own, so the composition has to come from the
    /// declaration and not from the shape of the list.
    /// </param>
    public sealed record BitmaskRow(
        string SchemaName,
        string LegacyLocator,
        long[] Bits,
        string[] Identifiers,
        long Default,
        string DefaultIdentifier,
        string[] DefaultComposedFrom)
    {
    }

    /// <summary>
    /// A schema whose default value is a preserved legacy weakness.
    /// </summary>
    /// <param name="SchemaName">Component schema carrying the default.</param>
    /// <param name="LegacyLocator">The enums.sru line declaring the legacy default constant.</param>
    /// <param name="Default">The numeric default the oracle declares.</param>
    /// <param name="DefaultIdentifier">Legacy identifier of the default, from x-legacy-default-identifier.</param>
    /// <param name="DefaultMemberIdentifier">Legacy identifier of the enum member the default selects.</param>
    public sealed record WeakDefaultRow(
        string SchemaName,
        string LegacyLocator,
        long Default,
        string DefaultIdentifier,
        string DefaultMemberIdentifier)
    {
    }

    /// <summary>
    /// A capability the legacy cannot express, and which this contract therefore must not offer.
    /// </summary>
    /// <param name="Id">Stable identifier, used as the test-case name.</param>
    /// <param name="AbsentCapability">The capability whose absence is asserted.</param>
    /// <param name="LegacyLocator">The oracle range in which the capability is absent.</param>
    /// <param name="ForbiddenNameTokens">Property or parameter name fragments that would betray it.</param>
    /// <param name="PermittedDeclarations">
    /// Owner.property pairs that legitimately match a token because the legacy genuinely declares
    /// them. Empty for a capability the legacy has nowhere at all.
    /// </param>
    public sealed record AbsentCapabilityRow(
        string Id,
        string AbsentCapability,
        string LegacyLocator,
        string[] ForbiddenNameTokens,
        string[] PermittedDeclarations)
    {
    }

    // ==============================================================================================
    //  THE TABLES
    //  ----------------------------------------------------------------------------------------------
    //  One row per weakness and one row per vocabulary set, exactly as the folder requirement asks.
    //  Read these as the specification: everything below the tables is mechanism.
    // ==============================================================================================

    /// <summary>
    /// The six preserved weaknesses of the crypto surface, with the surfaces on which each
    /// disclosure is accepted.
    /// </summary>
    /// <remarks>
    /// Every row requires the disclosure group and the deliberate-preservation group, plus the
    /// vocabulary specific to its own concern. That combination is what makes a row fail for the two
    /// permitted reasons and no others: the concern stopped being described, or it stopped being
    /// described as a deliberately preserved weakness.
    /// </remarks>
    private static readonly IReadOnlyList<WeaknessRow> PreservedWeaknessTable =
    [
        // W1  The default symmetric mode is ECB.
        //     enums.sru:L946  CRYPTO_SYMCRYPT_MODE_DEFAULT = CRYPTO_SYMCRYPT_MODE_ECB
        //     Eight of the 32 overloads at n_crypto.sru:L30-L61 omit the mode argument, so omitting
        //     the field runs in ECB. NOT hardened to CBC, and this row would fail if it were, because
        //     the disclosure it requires would no longer have a subject.
        new WeaknessRow(
            Id: "W1-ECB-IS-THE-DEFAULT-SYMMETRIC-MODE",
            Concern: "Omitting the symmetric mode runs in ECB, which leaks plaintext structure.",
            LegacyLocator: "enums.sru:L943-L946",
            SchemaSurfaces: [CipherModeSchema],
            OperationSurfaces: [SymmetricEncryptOperation, SymmetricDecryptOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                DeliberatePreservationVocabulary,
                ElectronicCodebookVocabulary,
            ]),

        // W2a The default RSA padding is PKCS#1 v1.5.
        //     enums.sru:L951  CRYPTO_RSA_PADDING_DEFAULT = CRYPTO_RSA_PADDING_PKCS1
        //     Four of the eight RSA cipher overloads at n_crypto.sru:L62-L69 omit the argument.
        //     NOT upgraded to OAEP.
        new WeaknessRow(
            Id: "W2A-PKCS1-V1.5-IS-THE-DEFAULT-RSA-PADDING",
            Concern: "Omitting the RSA padding selects PKCS#1 v1.5, which is padding-oracle prone.",
            LegacyLocator: "enums.sru:L949-L951",
            SchemaSurfaces: [RsaPaddingSchema],
            OperationSurfaces: [RsaEncryptOperation, RsaDecryptOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                DeliberatePreservationVocabulary,
                Pkcs1PaddingVocabulary,
            ]),

        // W2b No-padding is not selectable, and the two-member enumeration IS the mechanism.
        //     enums.sru:L949-L950 declare exactly two constants and no third, so there is nothing for
        //     a caller to select. Structural absence, not a runtime rule. The structural half of this
        //     row is asserted separately by RsaPaddingEnumerationHasExactlyTheTwoLegacyMembers.
        new WeaknessRow(
            Id: "W2B-NO-PADDING-RSA-IS-NOT-SELECTABLE",
            Concern: "The legacy declares no no-padding constant, so a request for it is refused.",
            LegacyLocator: "enums.sru:L949-L950",
            SchemaSurfaces: [RsaPaddingSchema],
            OperationSurfaces: [RsaEncryptOperation, RsaDecryptOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                NoPaddingRefusedVocabulary,
            ]),

        // W3  Symmetric padding is PKCS#5-family only and is not selectable.
        //     Not one of the 32 overloads at n_crypto.sru:L30-L61 has a padding parameter, and the
        //     cipher block at enums.sru:L936-L946 exposes a type and a mode and nothing else.
        new WeaknessRow(
            Id: "W3-SYMMETRIC-PADDING-IS-FIXED-AND-UNSELECTABLE",
            Concern: "Symmetric padding is PKCS#5-family, fixed, and offers the caller no choice.",
            LegacyLocator: "n_crypto.sru:L30-L61 and enums.sru:L936-L946",
            SchemaSurfaces: [CipherModeSchema, SymmetricEncryptRequestSchema, SymmetricDecryptRequestSchema],
            OperationSurfaces: [SymmetricEncryptOperation, SymmetricDecryptOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                FixedSymmetricPaddingVocabulary,
            ]),

        // W4  No key-derivation function is reachable anywhere in the surface.
        //     No salt, iteration count or derivation parameter appears in any of the 65 declarations
        //     at n_crypto.sru:L9-L73, and enums.sru declares no derivation constant of any kind, so a
        //     passphrase used as key material is used as raw key bytes.
        new WeaknessRow(
            Id: "W4-NO-KEY-DERIVATION-FUNCTION-IS-REACHABLE",
            Concern: "Key material is used as raw key bytes, unstretched and unsalted.",
            LegacyLocator: "n_crypto.sru:L9-L73",
            SchemaSurfaces: [KeyReferenceSchema, GenerateRsaKeyRequestSchema],
            OperationSurfaces: [GenerateRsaKeyOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                NoKeyDerivationVocabulary,
            ]),

        // W5  There is no authenticated encryption, so ciphertext carries no integrity tag.
        //     The mode set is exactly ECB, CBC and CFB [enums.sru:L943-L945]; there is no GCM, CCM or
        //     Poly1305 constant, and no tag or associated-data parameter anywhere in n_crypto.sru.
        new WeaknessRow(
            Id: "W5-NO-AUTHENTICATED-ENCRYPTION-SO-NO-INTEGRITY-TAG",
            Concern: "Ciphertext carries no integrity tag and the contract offers no field for one.",
            LegacyLocator: "enums.sru:L943-L945",
            SchemaSurfaces: [CipherModeSchema, SymmetricEncryptRequestSchema, SymmetricDecryptRequestSchema],
            OperationSurfaces: [SymmetricEncryptOperation, SymmetricDecryptOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                NoAuthenticatedEncryptionVocabulary,
            ]),

        // W6  1024-bit RSA remains a legal key size.
        //     enums.sru:L965 declares CRYPTO_RSA_BITS_1024 = 1024 as a first-class value, and
        //     GenRSAKey takes a plain readonly uint with no guard [n_crypto.sru:L19-L20].
        //     NO MINIMUM IS ASSERTED ANYWHERE FOR THIS ROW. Demanding 2048 would invent a guard the
        //     legacy does not have, which is precisely the hardening C-B forbids.
        new WeaknessRow(
            Id: "W6-1024-BIT-RSA-REMAINS-A-LEGAL-KEY-SIZE",
            Concern: "1024-bit RSA is below every current recommendation and is still accepted.",
            LegacyLocator: "enums.sru:L965-L967",
            SchemaSurfaces: [RsaKeyBitsSchema],
            OperationSurfaces: [GenerateRsaKeyOperation],
            RequiredVocabulary:
            [
                WeaknessDisclosureVocabulary,
                DeliberatePreservationVocabulary,
                SmallestRsaKeySizeVocabulary,
            ]),
    ];

    /// <summary>Identifiers of the six preserved weaknesses, one theory row each.</summary>
    /// <returns>The identifiers, in table order.</returns>
    public static TheoryData<string> PreservedWeaknessIds() => [.. PreservedWeaknessTable.Select(row => row.Id)];

    /// <summary>
    /// The five closed legacy enumerations of the crypto surface, with values and identifier
    /// spellings taken verbatim from the oracle.
    /// </summary>
    /// <remarks>
    /// AAP 0.4.5.3 requires the legacy identifier spellings to survive the port verbatim, because
    /// they appear in serialized payloads, log records and characterization recordings, where a
    /// rename silently invalidates every stored comparison. The document carries them in
    /// x-enum-varnames in member order, which is what makes them checkable here.
    /// </remarks>
    private static readonly IReadOnlyList<ClosedEnumRow> ClosedLegacyEnumerationTable =
    [
        // enums.sru:L924-L925. The genuine legacy argument of StringToBlob and BlobToString, not the
        // JSON transport encoding of a blob field.
        new ClosedEnumRow(
            SchemaName: EncodingSchema,
            LegacyLocator: "enums.sru:L924-L925",
            Values: [0L, 1L],
            Identifiers: ["CRYPTO_ENCODING_BASE64", "CRYPTO_ENCODING_HEX"]),

        // enums.sru:L928-L933.
        // MD5 AND SHA1 ARE LISTED ON PURPOSE, AND SO IS CRC32, WHICH IS NOT A CRYPTOGRAPHIC HASH AT
        // ALL. Their presence is preserved legacy behaviour, not an oversight in this table: the
        // oracle's own comment at enums.sru:L927 shows this same set governs the RSA signature hash
        // through n_crypto.sru:L70-L73, and there is no narrower set for signatures. Removing any
        // member here would be the hardening C-B forbids.
        new ClosedEnumRow(
            SchemaName: HashTypeSchema,
            LegacyLocator: "enums.sru:L928-L933",
            Values: [0L, 1L, 2L, 3L, 4L, 5L],
            Identifiers:
            [
                "CRYPTO_HASH_MD5",
                "CRYPTO_HASH_SHA1",
                "CRYPTO_HASH_SHA256",
                "CRYPTO_HASH_SHA384",
                "CRYPTO_HASH_SHA512",
                "CRYPTO_HASH_CRC32",
            ]),

        // enums.sru:L936-L940. DES and 3DES are retained because the legacy declares them and
        // callers select them; single DES has a 56-bit effective key and is unfit for new use. It is
        // neither removed nor renumbered.
        new ClosedEnumRow(
            SchemaName: CipherTypeSchema,
            LegacyLocator: "enums.sru:L936-L940",
            Values: [0L, 1L, 2L, 3L, 4L],
            Identifiers:
            [
                "CRYPTO_SYMCRYPT_TYPE_DES",
                "CRYPTO_SYMCRYPT_TYPE_3DES",
                "CRYPTO_SYMCRYPT_TYPE_AES128",
                "CRYPTO_SYMCRYPT_TYPE_AES192",
                "CRYPTO_SYMCRYPT_TYPE_AES256",
            ]),

        // enums.sru:L943-L945. Exactly three members, and that exactness is the mechanical proof of
        // W5: an AEAD mode would have to appear here, and none does.
        new ClosedEnumRow(
            SchemaName: CipherModeSchema,
            LegacyLocator: "enums.sru:L943-L945",
            Values: [0L, 1L, 2L],
            Identifiers:
            [
                "CRYPTO_SYMCRYPT_MODE_ECB",
                "CRYPTO_SYMCRYPT_MODE_CBC",
                "CRYPTO_SYMCRYPT_MODE_CFB",
            ]),

        // enums.sru:L949-L950. Exactly two members, and that exactness is the mechanical proof of
        // W2b: a no-padding constant would have to appear here, and none does.
        new ClosedEnumRow(
            SchemaName: RsaPaddingSchema,
            LegacyLocator: "enums.sru:L949-L950",
            Values: [0L, 1L],
            Identifiers: ["CRYPTO_RSA_PADDING_PKCS1", "CRYPTO_RSA_PADDING_OAEP"]),
    ];

    /// <summary>Schema names of the five closed legacy enumerations, one theory row each.</summary>
    /// <returns>The schema names, in table order.</returns>
    public static TheoryData<string> ClosedLegacyEnumerationNames() => [.. ClosedLegacyEnumerationTable.Select(row => row.SchemaName)];

    /// <summary>
    /// The two open legacy bitmasks. Their bits compose, so neither is a closed enumeration and
    /// neither may be asserted as one.
    /// </summary>
    private static readonly IReadOnlyList<BitmaskRow> OpenLegacyBitmaskTable =
    [
        // enums.sru:L954-L957. The default is NUMBER + ALPHABET = 3, so the symbol class is excluded
        // from it. Reproduced exactly rather than widened to 7.
        new BitmaskRow(
            SchemaName: RandomStringFlagsSchema,
            LegacyLocator: "enums.sru:L954-L957",
            Bits: [1L, 2L, 4L],
            Identifiers:
            [
                "CRYPTO_RNDSTRING_NUMBER",
                "CRYPTO_RNDSTRING_ALPHABET",
                "CRYPTO_RNDSTRING_SYMBOL",
            ],
            Default: 3L,
            DefaultIdentifier: "CRYPTO_RNDSTRING_DEFAULT",
            // enums.sru:L957 sums NUMBER and ALPHABET and stops there, so SYMBOL is excluded.
            DefaultComposedFrom: ["CRYPTO_RNDSTRING_NUMBER", "CRYPTO_RNDSTRING_ALPHABET"]),

        // enums.sru:L960-L962. The default is BRACKET + SEPARATOR = 3, the fully decorated form.
        // These govern formatting only and have no bearing on the value generated.
        new BitmaskRow(
            SchemaName: GuidFlagsSchema,
            LegacyLocator: "enums.sru:L960-L962",
            Bits: [1L, 2L],
            Identifiers:
            [
                "CRYPTO_GUID_INCLUDE_BRACKET",
                "CRYPTO_GUID_INCLUDE_SEPARATOR",
            ],
            Default: 3L,
            DefaultIdentifier: "CRYPTO_GUID_DEFAULT",
            // enums.sru:L962 sums BOTH bits, which is why the composition cannot be inferred
            // positionally the way the random-string default could.
            DefaultComposedFrom: ["CRYPTO_GUID_INCLUDE_BRACKET", "CRYPTO_GUID_INCLUDE_SEPARATOR"]),
    ];

    /// <summary>Schema names of the two open legacy bitmasks, one theory row each.</summary>
    /// <returns>The schema names, in table order.</returns>
    public static TheoryData<string> OpenLegacyBitmaskNames() => [.. OpenLegacyBitmaskTable.Select(row => row.SchemaName)];

    /// <summary>
    /// The two schema defaults that are themselves preserved weaknesses, W1 and W2a.
    /// </summary>
    /// <remarks>
    /// Both are asserted as PRESENT and UNCHANGED. A contract that removed either default, or moved
    /// it to the safer member, would fail here, which is the point: the default is the behaviour, and
    /// the behaviour is preserved.
    /// </remarks>
    private static readonly IReadOnlyList<WeakDefaultRow> PreservedWeakDefaultTable =
    [
        new WeakDefaultRow(
            SchemaName: CipherModeSchema,
            LegacyLocator: "enums.sru:L946",
            Default: 0L,
            DefaultIdentifier: "CRYPTO_SYMCRYPT_MODE_DEFAULT",
            DefaultMemberIdentifier: "CRYPTO_SYMCRYPT_MODE_ECB"),

        new WeakDefaultRow(
            SchemaName: RsaPaddingSchema,
            LegacyLocator: "enums.sru:L951",
            Default: 0L,
            DefaultIdentifier: "CRYPTO_RSA_PADDING_DEFAULT",
            DefaultMemberIdentifier: "CRYPTO_RSA_PADDING_PKCS1"),
    ];

    /// <summary>Schema names of the two preserved weak defaults, one theory row each.</summary>
    /// <returns>The schema names, in table order.</returns>
    public static TheoryData<string> PreservedWeakDefaultNames() => [.. PreservedWeakDefaultTable.Select(row => row.SchemaName)];

    /// <summary>
    /// The three capabilities the legacy cannot express, whose absence from this contract is the
    /// assertion: W3, W4 and W5.
    /// </summary>
    /// <remarks>
    /// Offering any of the three would be a NEW FEATURE rather than a port, so the sweep looks for
    /// the field a safer contract would have had to add and requires that it is not there. The
    /// permitted-declaration list exists for exactly one case: the legacy genuinely declares an RSA
    /// padding argument, so RsaCipherRequest.padding is expected and is not an offender.
    /// </remarks>
    private static readonly IReadOnlyList<AbsentCapabilityRow> AbsentCapabilityTable =
    [
        // W3. The only padding selector the legacy has is the RSA one at n_crypto.sru:L63,L65,L67,L69.
        // No symmetric overload has a padding parameter, so no symmetric request may carry the field.
        new AbsentCapabilityRow(
            Id: "W3-NO-SYMMETRIC-PADDING-SELECTOR-EXISTS",
            AbsentCapability: "a selectable symmetric padding scheme",
            LegacyLocator: "n_crypto.sru:L30-L61",
            ForbiddenNameTokens: ["padding", "padmode", "padscheme"],
            PermittedDeclarations: ["RsaCipherRequest.padding"]),

        // W4. No salt, iteration count or derivation parameter appears in any of the 65 declarations
        // at n_crypto.sru:L9-L73, and enums.sru declares no derivation constant of any kind, so the
        // permitted list is empty: there is nowhere in this contract these may legitimately appear.
        new AbsentCapabilityRow(
            Id: "W4-NO-KEY-DERIVATION-PARAMETER-EXISTS",
            AbsentCapability: "a key-derivation function, salt or iteration count",
            LegacyLocator: "n_crypto.sru:L9-L73",
            ForbiddenNameTokens:
            [
                "kdf",
                "pbkdf",
                "pbkdf2",
                "scrypt",
                "bcrypt",
                "argon",
                "salt",
                "iteration",
                "iterations",
                "derive",
                "derivation",
                "passphrase",
                "stretch",
            ],
            PermittedDeclarations: []),

        // W5. The mode set is exactly ECB, CBC and CFB, and no tag or associated-data parameter
        // appears anywhere in n_crypto.sru, so ciphertext is unauthenticated and this contract has no
        // field in which a tag could travel.
        new AbsentCapabilityRow(
            Id: "W5-NO-AUTHENTICATION-TAG-FIELD-EXISTS",
            AbsentCapability: "an authentication tag or associated-data field",
            LegacyLocator: "n_crypto.sru:L30-L61",
            ForbiddenNameTokens:
            [
                "authtag",
                "auth_tag",
                "tag",
                "aad",
                "associateddata",
                "associated_data",
                "gcm",
                "ccm",
                "poly1305",
                "nonce",
            ],
            PermittedDeclarations: []),
    ];

    /// <summary>Identifiers of the three absent legacy capabilities, one theory row each.</summary>
    /// <returns>The identifiers, in table order.</returns>
    public static TheoryData<string> AbsentCapabilityIds() => [.. AbsentCapabilityTable.Select(row => row.Id)];

    /// <summary>
    /// Mode identifiers an authenticated-encryption capability would have to introduce. Asserted
    /// ABSENT from the mode vocabulary, never present.
    /// </summary>
    /// <remarks>
    /// The direction matters and is the whole C-B discipline in one member: this list is what a
    /// hardened contract WOULD contain, so the assertion is that none of it appears. An assertion
    /// that any of these was present would be a demand that the port add authenticated encryption.
    /// </remarks>
    private static readonly string[] AuthenticatedEncryptionModeTokens =
    [
        "gcm",
        "ccm",
        "poly1305",
        "chacha",
        "eax",
        "ocb",
        "siv",
    ];

    // ==============================================================================================
    //  PHASE 1  -  THE SIX ANNOTATIONS
    // ==============================================================================================

    /// <summary>
    /// Every preserved weakness is disclosed as a known legacy weakness somewhere a caller of the
    /// affected operation will see it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The accepted surfaces are the ones the row names: the component schemas that model the concern
    /// and the operations that expose it, taking each operation's summary and description together.
    /// The document-level weakness register is DELIBERATELY NOT one of them, and that exclusion is
    /// the difference between a test with teeth and a test without. The register lists all eight
    /// weaknesses in one table, so admitting it here would let every row pass on the register alone
    /// even after every point-of-use annotation had been deleted. The register is worth having and is
    /// asserted separately by
    /// <see cref="DocumentDescriptionCarriesTheAuthoritativeWeaknessRegister"/>.
    /// </para>
    /// <para>
    /// A surface counts only when it satisfies ALL of the row's vocabulary groups, because the halves
    /// are not independent: naming ECB without disclosing that it is a weakness, or disclosing a
    /// weakness without saying which one, would each leave a caller unable to act.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PreservedWeaknessIds))]
    public void PreservedWeaknessIsDisclosedOnItsReachableSurface(string weaknessId)
    {
        WeaknessRow row = ResolveRow(PreservedWeaknessTable, weaknessId, static candidate => candidate.Id, nameof(PreservedWeaknessTable));

        List<(string Surface, string Text)> reachableSurface = [];

        foreach (string schemaName in row.SchemaSurfaces)
        {
            reachableSurface.Add(($"schema {schemaName}", RequireSchema(schemaName).Description ?? string.Empty));
        }

        foreach (string operationId in row.OperationSurfaces)
        {
            OpenApiOperation operation = RequireOperation(operationId);

            // Summary and description are concatenated because the document legitimately splits a
            // disclosure across them: the symmetric operations put "Omitting the mode selects ECB" in
            // the summary and the weakness discussion in the description.
            reachableSurface.Add((
                $"operation {operationId}",
                string.Concat(operation.Summary ?? string.Empty, "\n", operation.Description ?? string.Empty)));
        }

        List<string> disclosingSurfaces =
        [
            .. reachableSurface
                .Where(candidate => FirstUnsatisfiedGroup(candidate.Text, row.RequiredVocabulary) is null)
                .Select(candidate => candidate.Surface),
        ];

        Assert.True(
            disclosingSurfaces.Count > 0,
            BuildMissingDisclosureMessage(row, reachableSurface));

        // Reporting where it was found is part of the requirement, not decoration: a reviewer
        // auditing the secrets-and-weakness posture needs to know which surface a caller actually
        // reads, and a passing test that says nothing forces them back into the YAML by hand.
        string surfaces = string.Join(", ", disclosingSurfaces);

        Report($"{row.Id} [{row.LegacyLocator}] disclosed on: {surfaces}");
    }

    /// <summary>
    /// W1 and W2a: the weak default is still the schema default, still points at the weak member, and
    /// still names the legacy constant it came from.
    /// </summary>
    /// <remarks>
    /// This is the row that would fail if somebody "fixed" the contract by moving the default to CBC
    /// or to OAEP, or by removing the default so that the field became mandatory. All three of those
    /// are behavioural changes to a preserved default, and all three are what C-B forbids. Note the
    /// direction once more: the assertion is that the WEAK value is the default. Nothing here prefers
    /// the strong one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PreservedWeakDefaultNames))]
    public void PreservedWeakDefaultIsStillTheSchemaDefault(string schemaName)
    {
        WeakDefaultRow row = ResolveRow(PreservedWeakDefaultTable, schemaName, static candidate => candidate.SchemaName, nameof(PreservedWeakDefaultTable));

        IOpenApiSchema schema = RequireSchema(row.SchemaName);

        long? declaredDefault = NumericDefault(schema);

        Assert.True(
            declaredDefault is not null,
            $"Schema '{row.SchemaName}' declares no default. The legacy declares "
            + $"{row.DefaultIdentifier} at {row.LegacyLocator}, so omitting the field has a defined "
            + "meaning and the schema must state it. Leaving the default unstated would make the weak "
            + "behaviour invisible rather than removing it.");

        Assert.Equal(row.Default, declaredDefault!.Value);

        // The default must select the WEAK member, identified by its legacy spelling rather than by
        // its number, so that a renumbering could not quietly satisfy this assertion.
        IReadOnlyList<long> values = ClosedEnumValues(schema, row.SchemaName);
        IReadOnlyList<string> identifiers =
            StringArrayExtension(schema, EnumVarnamesExtension, row.SchemaName);

        Assert.Equal(values.Count, identifiers.Count);

        // IReadOnlyList carries no IndexOf, and an explicit scan reads better here than a LINQ
        // projection would: the index is what pairs a value with its identifier spelling.
        int defaultIndex = -1;

        for (int index = 0; index < values.Count; index++)
        {
            if (values[index] == row.Default)
            {
                defaultIndex = index;
                break;
            }
        }

        Assert.True(
            defaultIndex >= 0,
            $"Schema '{row.SchemaName}' declares default {row.Default} but that value is not one of "
            + $"its members [{string.Join(", ", values)}].");

        Assert.Equal(row.DefaultMemberIdentifier, identifiers[defaultIndex]);

        // x-legacy-default-identifier ties the wire default back to the oracle constant that produced
        // it, which is what makes the default auditable against enums.sru rather than merely present.
        Assert.Equal(
            row.DefaultIdentifier,
            StringExtension(schema, LegacyDefaultIdentifierExtension, row.SchemaName));

        Report($"{row.SchemaName} default {row.Default} = {row.DefaultMemberIdentifier} via {row.DefaultIdentifier} [{row.LegacyLocator}]");
    }

    /// <summary>
    /// W2b, structural half: the RSA padding enumeration has exactly the two members the legacy
    /// declares, which is the entire mechanism by which no-padding is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count is the assertion. enums.sru:L949-L950 declares PKCS#1 and OAEP and no third constant,
    /// so there is nothing for a caller to select and the refusal needs no separate validation rule.
    /// That is why the agent brief calls the absence STRUCTURAL rather than a runtime rejection.
    /// </para>
    /// <para>
    /// Both failure directions are behavioural regressions, in opposite ways. A third member would
    /// widen the contract past the legacy, most obviously by adding the no-padding option the legacy
    /// refuses. A missing member would narrow it, removing an option the legacy offers. Neither is a
    /// hardening demand: the assertion is exact fidelity to a two-member set.
    /// </para>
    /// </remarks>
    [Fact]
    public void RsaPaddingEnumerationHasExactlyTheTwoLegacyMembers()
    {
        IOpenApiSchema padding = RequireSchema(RsaPaddingSchema);

        IReadOnlyList<long> values = ClosedEnumValues(padding, RsaPaddingSchema);
        IReadOnlyList<string> identifiers =
            StringArrayExtension(padding, EnumVarnamesExtension, RsaPaddingSchema);

        Assert.Equal<long>([0L, 1L], values);

        // OAEP APPEARS HERE AS A MEMBER, NOT AS A REQUIREMENT, and the distinction is the whole of
        // C-B in one line. enums.sru:L950 declares CRYPTO_RSA_PADDING_OAEP, so a faithful contract
        // offers it and this row says so. What no row anywhere in this file says is that OAEP should
        // be the DEFAULT: that remains PKCS#1 v1.5, asserted as such by
        // PreservedWeakDefaultIsStillTheSchemaDefault. Offering the stronger option is legacy
        // fidelity; preferring it would be the hardening C-B forbids.
        Assert.Equal<string>(["CRYPTO_RSA_PADDING_PKCS1", "CRYPTO_RSA_PADDING_OAEP"], identifiers);

        string members = string.Join(", ", identifiers);

        Report($"{RsaPaddingSchema} carries exactly {values.Count} members [{members}] and no third, so no-padding is structurally absent [enums.sru:L949-L950]");
    }

    /// <summary>
    /// W5, structural half: the symmetric mode vocabulary introduces no authenticated-encryption mode.
    /// </summary>
    /// <remarks>
    /// Read the direction carefully, because it is the inverse of the instinctive test. This asserts
    /// that GCM, CCM, Poly1305 and their relatives are ABSENT. An assertion that one of them was
    /// present would be a demand that the port add authenticated encryption to a legacy surface that
    /// has none, which is exactly the improvement C-B forbids. The absence is also the mechanical
    /// proof behind the annotation: ciphertext carries no integrity tag because there is no mode under
    /// which it could.
    /// </remarks>
    [Fact]
    public void SymmetricModeVocabularyContainsNoAuthenticatedEncryptionMode()
    {
        IOpenApiSchema mode = RequireSchema(CipherModeSchema);

        IReadOnlyList<string> identifiers =
            StringArrayExtension(mode, EnumVarnamesExtension, CipherModeSchema);

        List<string> aeadMembers =
        [
            .. identifiers.Where(identifier =>
                AuthenticatedEncryptionModeTokens.Any(token =>
                    identifier.Contains(token, StringComparison.OrdinalIgnoreCase))),
        ];

        Assert.True(
            aeadMembers.Count == 0,
            $"Schema '{CipherModeSchema}' declares authenticated-encryption member(s) "
            + $"[{string.Join(", ", aeadMembers)}]. The legacy mode set is exactly "
            + "CRYPTO_SYMCRYPT_MODE_ECB, CRYPTO_SYMCRYPT_MODE_CBC and CRYPTO_SYMCRYPT_MODE_CFB "
            + "[enums.sru:L943-L945], and n_crypto.sru declares no tag or associated-data parameter "
            + "anywhere, so adding an AEAD mode would be a new capability rather than a port (C-B).");

        // The mode set is exactly three members, which is what makes the absence above exhaustive
        // rather than a spot check.
        Assert.Equal(3, identifiers.Count);

        string modes = string.Join(", ", identifiers);

        Report($"{CipherModeSchema} carries {identifiers.Count} modes [{modes}] and no AEAD mode [enums.sru:L943-L945]");
    }

    /// <summary>
    /// W6, structural half: the smallest published RSA key size is still in the accepted vocabulary,
    /// beside 2048 and 4096, and the schema still imposes no cryptographic floor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NO MINIMUM KEY SIZE IS DEMANDED HERE, and the second half of this test is the guard that keeps
    /// it that way. The oracle labels its three constants "RSA predefine bits" [enums.sru:L964] - a
    /// convenience set, not an allowed set - and GenRSAKey takes a plain readonly uint with no guard
    /// [n_crypto.sru:L19-L20], so the schema must not be a closed enumeration either.
    /// </para>
    /// <para>
    /// The bound assertion needs its direction stated plainly, because it looks like the hardening it
    /// is designed to catch. readonly uint is PowerBuilder's 16-bit unsigned integer, so the legacy
    /// parameter's own domain is 0 to 65535, and the schema now records that domain in
    /// x-legacy-domain-maximum while its own maximum carries a service-level cap - the same shape
    /// RandomSize has carried all along, for the same class of reason. Asserting the FLOOR is still the
    /// type's floor is therefore a GUARD AGAINST hardening: it fails if somebody raises the minimum to
    /// 2048 and thereby removes a key size the legacy accepts. It does not ask for a minimum, and it
    /// passes unchanged while 1024 stays legal.
    /// </para>
    /// <para>
    /// AND THE CEILING IS ASSERTED TO BE THE LARGEST PUBLISHED CONVENIENCE VALUE, which is what makes
    /// the cap a bound on work rather than a narrowing of the vocabulary. Every value the oracle
    /// declares as first-class stays inside it; only the range above the published set is refused. A
    /// cap set below 4096 would remove a declared constant from the accepted set and would be exactly
    /// the hardening the row above forbids, so it is pinned here in the same place and for the same
    /// reason.
    /// </para>
    /// </remarks>
    [Fact]
    public void SmallestPublishedRsaKeySizeRemainsInTheKeySizeVocabulary()
    {
        IOpenApiSchema bits = RequireSchema(RsaKeyBitsSchema);

        IReadOnlyList<long> predefined =
            NumberArrayExtension(bits, LegacyPredefinedValuesExtension, RsaKeyBitsSchema);
        IReadOnlyList<string> predefinedIdentifiers =
            StringArrayExtension(bits, LegacyPredefinedVarnamesExtension, RsaKeyBitsSchema);

        // 1024 first, and named, because it is the preserved weakness. 2048 and 4096 are asserted
        // beside it so the row proves the whole published set survived rather than one member of it.
        Assert.Contains(1024L, predefined);
        Assert.Contains(2048L, predefined);
        Assert.Contains(4096L, predefined);
        Assert.Contains("CRYPTO_RSA_BITS_1024", predefinedIdentifiers);

        // Not a closed enumeration: closing it would narrow a parameter the legacy leaves open.
        Assert.True(
            bits.Enum is null || bits.Enum.Count == 0,
            $"Schema '{RsaKeyBitsSchema}' declares a closed enum. The oracle calls its three "
            + "constants \"RSA predefine bits\" [enums.sru:L964], a convenience set rather than an "
            + "allowed set, and n_crypto.sru:L19-L20 types the parameter as a plain readonly uint "
            + "with no constraint, so closing the set would be a behavioural narrowing (C-B).");

        // The FLOOR is the legacy parameter's own 16-bit unsigned domain, not a policy. Raising it would
        // remove 1024 from the accepted set, which is the hardening C-B forbids, so pinning it here is a
        // guard against that and not a request for one.
        Assert.Equal("0", bits.Minimum);

        // The CEILING is a service-level cap that narrows the legacy domain - the shape RandomSize
        // already uses - and it must be the LARGEST PUBLISHED CONVENIENCE VALUE, so that every size the
        // oracle declares as first-class stays acceptable and only the range above the published set is
        // refused. The legacy domain itself is published in the extension rather than discarded.
        Assert.Equal(
            predefined.Max().ToString(System.Globalization.CultureInfo.InvariantCulture),
            bits.Maximum);
        Assert.Equal(
            (long)ushort.MaxValue,
            NumberExtension(bits, LegacyDomainMaximumExtension, RsaKeyBitsSchema));

        string published = string.Join(", ", predefined);

        Report($"{RsaKeyBitsSchema} publishes [{published}] with no closed enum so 1024 remains legal [enums.sru:L965-L967], caps generation work at {bits.Maximum} and records the legacy domain ceiling {ushort.MaxValue} in {LegacyDomainMaximumExtension}");
    }

    /// <summary>
    /// W3, W4 and W5, structural half: a capability the legacy cannot express is not offered by this
    /// contract, anywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sweep is deliberately wider than the crypto surface. Every property declared by every
    /// component schema is inspected, plus every parameter of every CryptoService operation, so a
    /// forbidden field cannot hide in a shared schema that a crypto request reaches through a
    /// reference. A wider sweep can only make this stricter, never weaker.
    /// </para>
    /// <para>
    /// The permitted-declaration list is not a loophole, it is fidelity. The legacy genuinely declares
    /// an RSA padding argument at n_crypto.sru:L63, L65, L67 and L69, so RsaCipherRequest.padding
    /// must exist. What must not exist is a padding field on a symmetric request, because not one of
    /// the 32 symmetric overloads has one. Each permitted entry is therefore justified by an oracle
    /// declaration, and anything else matching a token is an offender.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(AbsentCapabilityIds))]
    public void AbsentLegacyCapabilityIsNotOfferedByTheContract(string capabilityId)
    {
        AbsentCapabilityRow row = ResolveRow(AbsentCapabilityTable, capabilityId, static candidate => candidate.Id, nameof(AbsentCapabilityTable));

        List<(string Owner, string Name)> declarations = [.. AllDeclaredPropertyNames(), .. CryptoOperationParameterNames()];

        List<string> offenders =
        [
            .. declarations
                .Where(declaration => row.ForbiddenNameTokens.Any(token =>
                    declaration.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Select(declaration => $"{declaration.Owner}.{declaration.Name}")
                .Where(qualified => !row.PermittedDeclarations.Contains(qualified, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            offenders.Count == 0,
            $"{row.Id}: the contract offers {row.AbsentCapability}, which the legacy cannot express. "
            + $"Unexpected declaration(s): {string.Join(", ", offenders)}. The oracle has no such "
            + $"parameter anywhere in {row.LegacyLocator}, so offering one would be a new feature "
            + $"rather than a port (C-B). Expected declaration(s), each justified by the oracle: "
            + $"{(row.PermittedDeclarations.Length == 0 ? "none" : string.Join(", ", row.PermittedDeclarations))}.");

        // Every permitted declaration must actually be there. Without this the permitted list could
        // silently rot into a list of things that no longer exist, and the sweep would then be
        // asserting the absence of a capability nobody was still offering.
        foreach (string permitted in row.PermittedDeclarations)
        {
            Assert.Contains(
                permitted,
                declarations.Select(declaration => $"{declaration.Owner}.{declaration.Name}"),
                StringComparer.Ordinal);
        }

        string tokens = string.Join(", ", row.ForbiddenNameTokens);

        Report($"{row.Id}: swept {declarations.Count} declarations for [{tokens}], and {row.AbsentCapability} is absent [{row.LegacyLocator}]");
    }

    /// <summary>
    /// W3, stated directly at the point of use: neither symmetric request carries a padding field.
    /// </summary>
    /// <remarks>
    /// The document-wide sweep above already implies this, since the only padding declaration it
    /// permits belongs to the RSA cipher request. This states it anyway, on the two schemas a caller
    /// actually fills in, because a reader looking for W3 should find an assertion that names
    /// SymEncryptRequest and SymDecryptRequest rather than having to derive it from a sweep. It also
    /// pins the exact property set, so a field added to either request has to be considered
    /// deliberately rather than slipping in unnoticed.
    /// </remarks>
    [Theory]
    [InlineData(SymmetricEncryptRequestSchema)]
    [InlineData(SymmetricDecryptRequestSchema)]
    public void SymmetricRequestOffersNoPaddingChoice(string requestSchemaName)
    {
        IOpenApiSchema request = RequireSchema(requestSchemaName);

        Assert.True(
            request.Properties is not null,
            $"Schema '{requestSchemaName}' declares no properties at all, so the 16 legacy overloads "
            + "it covers cannot be expressed.");

        IReadOnlyList<string> properties = [.. request.Properties!.Keys];

        // The legacy symmetric surface takes payload, key, initialization vector, cipher type and
        // mode, and nothing else [n_crypto.sru:L30-L61]. This is that set, and its exactness is what
        // proves there is no padding selector and no authentication tag.
        Assert.Equal<string>(
            ["data", "payloadForm", "keyRef", "ivRef", "cipherType", "mode"],
            properties);

        string declared = string.Join(", ", properties);

        Report($"{requestSchemaName} declares [{declared}], so it carries no padding field and no authentication-tag field [n_crypto.sru:L30-L61]");
    }

    /// <summary>
    /// The document-level weakness register exists and reads as a register of deliberately preserved
    /// weaknesses.
    /// </summary>
    /// <remarks>
    /// This is the caller's index into everything above: one table, in the document description, that
    /// says how many preserved weaknesses there are and where each is annotated. It is asserted here
    /// rather than folded into
    /// <see cref="PreservedWeaknessIsDisclosedOnItsReachableSurface"/> precisely so that it cannot
    /// substitute for a point-of-use annotation. Deliberately vocabulary-based and short: the register
    /// belongs to the document's author, and this suite has an opinion only about its existence.
    /// </remarks>
    [Fact]
    public void DocumentDescriptionCarriesTheAuthoritativeWeaknessRegister()
    {
        string description = documents.Security.Info?.Description ?? string.Empty;

        string? unsatisfied = FirstUnsatisfiedGroup(
            description,
            [WeaknessDisclosureVocabulary, DeliberatePreservationVocabulary]);

        Assert.True(
            unsatisfied is null,
            $"The Security document description carries no preserved-weakness register: no wording "
            + $"from [{unsatisfied}] appears in its {description.Length} characters. AAP 0.6.6.4 makes "
            + "annotation the whole remediation for the legacy cryptographic defaults, so a caller "
            + "needs one place that says the weak defaults are preserved on purpose.");

        Report($"weakness register present in Info.Description ({description.Length} characters) of {documents.SecurityDocumentPath}");
    }

    // ==============================================================================================
    //  PHASE 2  -  THE ALGORITHM IDENTIFIER SETS, PRESERVED VERBATIM
    // ==============================================================================================

    /// <summary>
    /// Each closed legacy enumeration carries the oracle's values and the oracle's identifier
    /// spellings, in the oracle's order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves matter and neither substitutes for the other. The NUMBERS are what travel on the
    /// wire, so a renumbering silently changes which algorithm a stored payload selects. The
    /// SPELLINGS are what appear in log records and characterization recordings, so a rename silently
    /// invalidates every stored comparison that mentions one (AAP 0.4.5.3). Order is asserted too,
    /// because x-enum-varnames is positional: the same names in a different order would map every
    /// value to the wrong identifier while both collections still looked correct in isolation.
    /// </para>
    /// <para>
    /// This document expresses these sets as numeric enums with the identifier spellings carried
    /// alongside in x-enum-varnames, so the numbers are asserted directly from the document as the
    /// brief requires. Nothing numeric is inferred: had the document modelled them as named string
    /// enums instead, the numeric mapping would have been left to enums.sru and docs/CONTRACTS.md
    /// rather than fabricated here.
    /// </para>
    /// <para>
    /// MD5, SHA-1, CRC32 and DES all appear in these tables ON PURPOSE. Their presence is preserved
    /// legacy behaviour, not an oversight, and no assertion anywhere in this file asks for any of them
    /// to be removed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ClosedLegacyEnumerationNames))]
    public void ClosedLegacyEnumerationCarriesItsLegacyValuesAndSpellings(string schemaName)
    {
        ClosedEnumRow row = ResolveRow(ClosedLegacyEnumerationTable, schemaName, static candidate => candidate.SchemaName, nameof(ClosedLegacyEnumerationTable));

        IOpenApiSchema schema = RequireSchema(row.SchemaName);

        Assert.Equal<long>(row.Values, ClosedEnumValues(schema, row.SchemaName));
        Assert.Equal<string>(
            row.Identifiers,
            StringArrayExtension(schema, EnumVarnamesExtension, row.SchemaName));

        // The oracle's own mapping is <value> = <IDENTIFIER>, so the two collections must be the same
        // length for the positional pairing above to mean anything.
        Assert.Equal(row.Values.Length, row.Identifiers.Length);

        string mapping = string.Join(", ", row.Values.Zip(row.Identifiers, (value, identifier) => $"{value}={identifier}"));

        Report($"{row.SchemaName} [{row.LegacyLocator}] = {mapping}");
    }

    /// <summary>
    /// Each open legacy bitmask carries the oracle's bit values, the oracle's bit spellings and the
    /// oracle's composed default, and stays open rather than being closed into an enumeration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These two are bitmasks rather than enumerations because their members compose, which is why
    /// the document declares x-bitmask-values and x-bitmask-varnames instead of an enum. Asserting a
    /// closed enum here would forbid the composition the legacy allows: the random-string flags
    /// legitimately compose to 7 for all three character classes.
    /// </para>
    /// <para>
    /// The composed defaults are preserved exactly rather than widened. The random-string default is
    /// NUMBER + ALPHABET = 3 [enums.sru:L957], so the symbol class is excluded from it; widening it to
    /// 7 would be a behaviour change even though it looks like a harmless improvement in variety.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OpenLegacyBitmaskNames))]
    public void OpenLegacyBitmaskCarriesItsBitsSpellingsAndComposedDefault(string schemaName)
    {
        BitmaskRow row = ResolveRow(OpenLegacyBitmaskTable, schemaName, static candidate => candidate.SchemaName, nameof(OpenLegacyBitmaskTable));

        IOpenApiSchema schema = RequireSchema(row.SchemaName);

        IReadOnlyList<long> documentBits =
            NumberArrayExtension(schema, BitmaskValuesExtension, row.SchemaName);
        IReadOnlyList<string> documentIdentifiers =
            StringArrayExtension(schema, BitmaskVarnamesExtension, row.SchemaName);

        Assert.Equal<long>(row.Bits, documentBits);
        Assert.Equal<string>(row.Identifiers, documentIdentifiers);
        Assert.Equal(row.Default, NumericDefault(schema));
        Assert.Equal(
            row.DefaultIdentifier,
            StringExtension(schema, LegacyDefaultIdentifierExtension, row.SchemaName));

        // The document's default number must be the composition of the DOCUMENT's own bit values for
        // the bits the oracle names, which ties the two independent halves of the schema together: a
        // renumbered bit would no longer sum to the declared default even though each half still
        // looked correct on its own.
        long composed = 0;

        foreach (string bitIdentifier in row.DefaultComposedFrom)
        {
            int bitIndex = documentIdentifiers
                .Select((identifier, index) => (identifier, index))
                .Where(candidate => string.Equals(candidate.identifier, bitIdentifier, StringComparison.Ordinal))
                .Select(candidate => candidate.index)
                .DefaultIfEmpty(-1)
                .First();

            Assert.True(
                bitIndex >= 0,
                $"Schema '{row.SchemaName}' does not declare bit '{bitIdentifier}', which "
                + $"{row.DefaultIdentifier} composes its default from [{row.LegacyLocator}]. Declared "
                + $"bits: {string.Join(", ", documentIdentifiers)}.");

            composed |= documentBits[bitIndex];
        }

        Assert.Equal(row.Default, composed);

        // Deliberately NOT a closed enumeration: the bits compose, so any composition inside the
        // stated domain is legal and closing the set would narrow the legacy.
        Assert.True(
            schema.Enum is null || schema.Enum.Count == 0,
            $"Schema '{row.SchemaName}' declares a closed enum, but its members compose "
            + $"[{row.LegacyLocator}]. Closing a bitmask would reject compositions the legacy accepts, "
            + "which is a behavioural narrowing (C-B).");

        string bitMapping = string.Join(", ", row.Bits.Zip(row.Identifiers, (bit, identifier) => $"{bit}={identifier}"));

        Report($"{row.SchemaName} [{row.LegacyLocator}] bits {bitMapping}, default {row.Default} = {row.DefaultIdentifier}");
    }

    /// <summary>
    /// The encoding vocabulary states no default, because both legacy declarations take the argument.
    /// </summary>
    /// <remarks>
    /// The absence of a default is itself faithful. StringToBlob and BlobToString both take the
    /// encoding argument [n_crypto.sru:L11-L12], and enums.sru:L924-L925 declares no
    /// CRYPTO_ENCODING_DEFAULT constant, so a caller always states it. Inventing a default here -
    /// base64 being the obvious guess - would silently make one encoding win for callers who omit the
    /// field, which the legacy never does.
    /// </remarks>
    [Fact]
    public void EncodingVocabularyStatesNoDefaultBecauseTheLegacyDeclaresNone()
    {
        IOpenApiSchema encoding = RequireSchema(EncodingSchema);

        Assert.Null(NumericDefault(encoding));
        Assert.False(
            encoding.Extensions?.ContainsKey(LegacyDefaultIdentifierExtension) ?? false,
            $"Schema '{EncodingSchema}' names a legacy default constant, but enums.sru:L924-L925 "
            + "declares none: both CRYPTO_ENCODING_BASE64 and CRYPTO_ENCODING_HEX are plain members "
            + "and every legacy declaration takes the argument.");

        Report($"{EncodingSchema} states no default, matching enums.sru:L924-L925");
    }

    // ==============================================================================================
    //  MECHANISM
    //  ----------------------------------------------------------------------------------------------
    //  Lookup and reading helpers. They live in this file rather than in ContractTestContext.cs by
    //  that file's own rule: a helper earns a place there only when two or more sibling tests need it,
    //  and every helper below is specific to reading the crypto vocabulary of one document.
    //
    //  Each raises an xunit-visible assertion failure naming what was sought, because a miss that
    //  surfaced as a NullReferenceException from inside a property would cost far more to diagnose
    //  than the message costs to write.
    //
    //  ONE ANALYSER SUGGESTION IS DECLINED HERE ON PURPOSE, so that it reads as a decision rather than
    //  as an oversight. CA1859 proposes narrowing the three readers below from IReadOnlyList<T> to
    //  List<T> "for improved performance". It is declined on both of the grounds that matter:
    //
    //    - It would WEAKEN the contract. These readers hand a caller the document's vocabulary, and
    //      IReadOnlyList says that vocabulary is not the caller's to mutate. Returning List<T> would
    //      let an assertion quietly reorder or extend the very thing it is meant to be checking.
    //    - Its whole justification is performance, and AAP 0.1.2 states in terms that this is NOT a
    //      performance refactor and that no performance objective may be used to justify a design
    //      choice. Each reader runs a handful of times over a document already parsed once per
    //      assembly, so there is no measurable saving to weigh against the weaker contract anyway.
    //
    //  The suggestion sits at `info`, below the build's warning gate, so nothing is being suppressed
    //  and no NoWarn is involved - the diagnostic simply does not apply here. The sibling suggestion on
    //  BuildFailure WAS taken, because narrowing that one to FailException tightens the signature
    //  instead of loosening it.
    // ==============================================================================================

    // ==============================================================================================
    //  THE TWO PUBLISHED CAPABILITY NARROWINGS
    //  ----------------------------------------------------------------------------------------------
    //  Distinct from every weakness above, and the distinction is the point. A WEAKNESS is behaviour
    //  the legacy had that this port reproduces and annotates. A NARROWING is behaviour the legacy
    //  DECLARED that this port cannot reproduce, because a parameter it needs exists only inside the
    //  closed binary - n_crypto is native "pfw.dll" [n_crypto.sru:L8] with no PowerScript body for any
    //  of its 63 declarations.
    //
    //  The rule for those is that the contract is narrowed with a DEFINED ERROR, never widened with a
    //  guess, and the narrowing must be legible on the wire contract rather than discovered at runtime.
    //  These tests hold the contract to that, in both directions: the narrowing must be published, AND
    //  it must be exactly as wide as the missing evidence - never wider.
    // ==============================================================================================

    /// <summary>
    /// The keyed and signing operations narrow the hash set to the five identifiers that have a keyed
    /// form, while the unkeyed digest operations keep all six.
    /// </summary>
    [Fact]
    public void TheKeyedHashNarrowingIsPublishedAndIsExactlyOneIdentifierWide()
    {
        IReadOnlyList<long> full = ClosedEnumValues(RequireSchema(HashTypeSchema), HashTypeSchema);
        IReadOnlyList<long> keyed =
            ClosedEnumValues(RequireSchema(KeyedHashTypeSchema), KeyedHashTypeSchema);

        // THE FULL SET IS UNREDUCED. Preserving the identifier set exactly is required (C-B), so the
        // narrowing must be a SECOND schema rather than an edit to the first.
        Assert.Equal<long>([0, 1, 2, 3, 4, 5], full);

        // The narrowed set is a prefix-preserving subset: same identifiers, same numbers, one absent.
        Assert.Equal<long>([0, 1, 2, 3, 4], keyed);
        Assert.Equal(full.Count - 1, keyed.Count);
        Assert.All(keyed, value => Assert.Contains(value, full));

        // Exactly one identifier is withheld, and it is the checksum - the only member with no keyed
        // or signed construction. A narrowing that dropped a real digest would fail here.
        Assert.Equal<long>([5], [.. full.Except(keyed)]);
    }

    /// <summary>
    /// Each of the six hash-taking request schemas points at the hash vocabulary its operation can
    /// actually honour.
    /// </summary>
    /// <param name="requestSchema">The request schema under test.</param>
    /// <param name="expectedVocabulary">The hash schema it must reference.</param>
    /// <remarks>
    /// THE ASSIGNMENT IS THE SUBSTANCE OF THE FIX, not the existence of the narrowed schema. Publishing
    /// a narrowed vocabulary that no operation referenced would leave the original contradiction
    /// exactly where it was: a schema-valid request accepted by a caller and refused by the provider.
    /// Both halves are asserted together so neither can regress alone - the two unkeyed digests must
    /// keep the full set, and the four keyed and signing operations must use the narrowed one.
    /// </remarks>
    [Theory]
    [InlineData("HashRequest", HashTypeSchema)]
    [InlineData("HashFileRequest", HashTypeSchema)]
    [InlineData("HmacRequest", KeyedHashTypeSchema)]
    [InlineData("HmacFileRequest", KeyedHashTypeSchema)]
    [InlineData("RsaSignRequest", KeyedHashTypeSchema)]
    [InlineData("RsaVerifyRequest", KeyedHashTypeSchema)]
    public void EachHashTakingRequestReferencesTheVocabularyItsOperationCanHonour(
        string requestSchema,
        string expectedVocabulary)
    {
        IOpenApiSchema request = RequireSchema(requestSchema);

        IOpenApiSchema selector =
            request.Properties?.TryGetValue("hashType", out IOpenApiSchema? property) == true
                ? property
                : throw BuildFailure(
                    $"Request schema '{requestSchema}' declares no 'hashType' property, so the "
                    + "vocabulary it admits cannot be checked.");

        // The reference is compared by the enumeration it resolves to rather than by a $ref string,
        // because that is what a generator and a validator actually see.
        Assert.Equal(
            ClosedEnumValues(RequireSchema(expectedVocabulary), expectedVocabulary),
            ClosedEnumValues(selector, $"{requestSchema}.hashType"));
    }

    /// <summary>
    /// The symmetric mode schema publishes its blocked cells in machine-readable form, keeps all three
    /// declared modes, and blocks exactly the cells whose parameters are unobservable.
    /// </summary>
    [Fact]
    public void TheBlockedSymmetricCellsArePublishedAndKeepTheModeSetIntact()
    {
        IOpenApiSchema mode = RequireSchema(CipherModeSchema);

        // THE IDENTIFIER SET IS UNTOUCHED. Withdrawing a capability must not withdraw a declaration.
        Assert.Equal<long>([0, 1, 2], ClosedEnumValues(mode, CipherModeSchema));

        JsonNode blocked =
            mode.Extensions?.TryGetValue("x-blocked-cells", out IOpenApiExtension? extension) == true
                && extension is JsonNodeExtension node
                ? node.Node
                : throw BuildFailure(
                    $"Schema '{CipherModeSchema}' publishes no 'x-blocked-cells' extension, so a "
                    + "caller cannot discover which cells are refused without provoking a failure.");

        JsonArray cells = Assert.IsType<JsonArray>(blocked);

        // Two entries, and BOTH reasons are named - they rest on different missing evidence and are
        // unblocked by different measurements, so collapsing them would lose which is which.
        Assert.Equal(2, cells.Count);

        List<string> reasons =
            [.. cells.Select(cell => cell?["reason"]?.GetValue<string>() ?? "(none)").Order(StringComparer.Ordinal)];

        Assert.Equal<string>(
            ["SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE", "SYMMETRIC_VECTOR_UNPROVABLE"],
            reasons);

        // The feedback mode is blocked in EITHER vector shape, because a vector does not disclose a
        // feedback width; the chaining mode is blocked only without one. That asymmetry is the evidence
        // that the narrowing is minimal rather than a blanket refusal of anything awkward.
        JsonNode feedback = Assert.Single(
            cells,
            cell => cell?["reason"]?.GetValue<string>() == "SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE") !;
        JsonNode vector = Assert.Single(
            cells,
            cell => cell?["reason"]?.GetValue<string>() == "SYMMETRIC_VECTOR_UNPROVABLE") !;

        // Read as decimal: the YAML reader materialises an unquoted scalar as a decimal node, so
        // requesting a 64-bit integer directly raises rather than converting.
        Assert.Equal(2m, feedback["mode"]?.GetValue<decimal>());
        Assert.Equal("any", feedback["ivSupplied"]?.GetValue<string>());
        Assert.Equal(1m, vector["mode"]?.GetValue<decimal>());
        Assert.False(vector["ivSupplied"]?.GetValue<bool>());

        Report($"{cells.Count} blocked cells published on {CipherModeSchema} of {documents.SecurityDocumentPath}");
    }

    /// <summary>Resolves a component schema of the Security document by name.</summary>
    /// <param name="schemaName">Component schema name, compared ordinally.</param>
    /// <returns>The schema.</returns>
    private IOpenApiSchema RequireSchema(string schemaName)
    {
        IDictionary<string, IOpenApiSchema> schemas =
            documents.Security.Components?.Schemas
            ?? throw BuildFailure(
                $"The Security document at '{documents.SecurityDocumentPath}' declares no "
                + "components/schemas at all, so contract C-02 has no vocabulary to check.");

        if (schemas.TryGetValue(schemaName, out IOpenApiSchema? schema))
        {
            return schema;
        }

        throw BuildFailure(
            $"Component schema '{schemaName}' was not found in '{documents.SecurityDocumentPath}'. "
            + $"{schemas.Count} schemas were searched. Crypto vocabulary schemas present: "
            + $"{string.Join(", ", schemas.Keys.Where(key => key.StartsWith("Crypto", StringComparison.Ordinal)).Order(StringComparer.Ordinal))}.");
    }

    /// <summary>Resolves an operation of the Security document by its operation identifier.</summary>
    /// <param name="operationId">The operationId as the document declares it, compared ordinally.</param>
    /// <returns>The operation.</returns>
    private OpenApiOperation RequireOperation(string operationId)
    {
        List<OpenApiOperation> matches =
        [
            .. documents.Security.Paths
                .SelectMany(path => path.Value.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
                .Select(entry => entry.Value)
                .Where(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal)),
        ];

        // Ambiguity is an error rather than a first-match coin toss, for the same reason
        // ContractTestContext.cs makes descriptor ambiguity an error: a duplicated operationId would
        // hand this suite the wrong operation and still pass.
        if (matches.Count == 1)
        {
            return matches[0];
        }

        throw BuildFailure(
            matches.Count == 0
                ? $"Operation '{operationId}' was not found in '{documents.SecurityDocumentPath}'. "
                  + $"Operation identifiers present: {string.Join(", ", AllOperationIds().Order(StringComparer.Ordinal))}."
                : $"Operation identifier '{operationId}' is declared {matches.Count} times in "
                  + $"'{documents.SecurityDocumentPath}'. An operationId must be unique across the "
                  + "document, so this is a defect in the contract rather than in the test.");
    }

    /// <summary>Every operation identifier the Security document declares.</summary>
    /// <returns>The identifiers, unordered, with unnamed operations rendered as a placeholder.</returns>
    private IEnumerable<string> AllOperationIds() =>
        documents.Security.Paths
            .SelectMany(path => path.Value.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
            .Select(entry => entry.Value.OperationId ?? "(no operationId)");

    /// <summary>
    /// Reads a schema's closed enumeration as integers.
    /// </summary>
    /// <param name="schema">Schema declaring the enumeration.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The values in declaration order.</returns>
    private static IReadOnlyList<long> ClosedEnumValues(IOpenApiSchema schema, string schemaName)
    {
        IList<JsonNode>? members = schema.Enum;

        if (members is null || members.Count == 0)
        {
            throw BuildFailure(
                $"Schema '{schemaName}' declares no closed enum, so its legacy member values cannot "
                + "be checked. The crypto vocabulary schemas that are genuinely open declare "
                + $"{BitmaskValuesExtension} or {LegacyPredefinedValuesExtension} instead, and are "
                + "checked by the bitmask and key-size tests rather than by this one.");
        }

        List<long> values = [];

        for (int index = 0; index < members.Count; index++)
        {
            JsonNode? member = members[index];

            if (member is null || !TryReadInteger(member, out long value))
            {
                throw BuildFailure(
                    $"Schema '{schemaName}' enum member at position {index} is "
                    + $"'{member?.ToJsonString() ?? "null"}', which is not a JSON integer. The legacy "
                    + "constants are integers and AAP 0.5 normalises all three declared widths onto a "
                    + "single JSON integer, so a non-integral member would change the wire form.");
            }

            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Reads a specification extension whose value is an array of strings.
    /// </summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key, for example x-enum-varnames.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The strings in declaration order.</returns>
    private static IReadOnlyList<string> StringArrayExtension(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        JsonArray array = ExtensionArray(schema, extensionName, schemaName);
        List<string> values = [];

        for (int index = 0; index < array.Count; index++)
        {
            JsonNode? element = array[index];

            if (element is null || element.GetValueKind() != JsonValueKind.String)
            {
                throw BuildFailure(
                    $"Schema '{schemaName}' extension '{extensionName}' element {index} is "
                    + $"'{element?.ToJsonString() ?? "null"}', which is not a JSON string. This "
                    + "extension carries the preserved legacy identifier spellings (AAP 0.4.5.3), so "
                    + "every element must be the identifier as PowerScript declares it.");
            }

            values.Add(element.GetValue<string>());
        }

        return values;
    }

    /// <summary>
    /// Reads a specification extension whose value is an array of integers.
    /// </summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key, for example x-bitmask-values.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The values in declaration order.</returns>
    private static IReadOnlyList<long> NumberArrayExtension(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        JsonArray array = ExtensionArray(schema, extensionName, schemaName);
        List<long> values = [];

        for (int index = 0; index < array.Count; index++)
        {
            JsonNode? element = array[index];

            if (element is null || !TryReadInteger(element, out long value))
            {
                throw BuildFailure(
                    $"Schema '{schemaName}' extension '{extensionName}' element {index} is "
                    + $"'{element?.ToJsonString() ?? "null"}', which is not a JSON integer.");
            }

            values.Add(value);
        }

        return values;
    }

    /// <summary>
    /// Reads a specification extension whose value is a single integer.
    /// </summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key, for example x-legacy-domain-maximum.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The integer value.</returns>
    private static long NumberExtension(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        JsonNode node = ExtensionNode(schema, extensionName, schemaName);

        if (!TryReadInteger(node, out long value))
        {
            throw BuildFailure(
                $"Schema '{schemaName}' extension '{extensionName}' is '{node.ToJsonString()}', which is "
                + "not a JSON integer. It records the legacy parameter's own domain ceiling, so it must be "
                + "that number.");
        }

        return value;
    }

    /// <summary>
    /// Reads a specification extension whose value is a single string.
    /// </summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key, for example x-legacy-default-identifier.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The string value.</returns>
    private static string StringExtension(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        JsonNode node = ExtensionNode(schema, extensionName, schemaName);

        if (node.GetValueKind() != JsonValueKind.String)
        {
            throw BuildFailure(
                $"Schema '{schemaName}' extension '{extensionName}' is '{node.ToJsonString()}', which "
                + "is not a JSON string. It names the legacy constant that supplies the default, so it "
                + "must be that identifier's spelling.");
        }

        return node.GetValue<string>();
    }

    /// <summary>Reads an extension node and requires it to be an array.</summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The array node.</returns>
    private static JsonArray ExtensionArray(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        JsonNode node = ExtensionNode(schema, extensionName, schemaName);

        return node as JsonArray
            ?? throw BuildFailure(
                $"Schema '{schemaName}' extension '{extensionName}' is '{node.ToJsonString()}', which "
                + "is not a JSON array.");
    }

    /// <summary>
    /// Reads a specification extension as a raw JSON node.
    /// </summary>
    /// <param name="schema">Schema carrying the extension.</param>
    /// <param name="extensionName">Extension key.</param>
    /// <param name="schemaName">Name used in failure messages.</param>
    /// <returns>The node.</returns>
    /// <remarks>
    /// Microsoft.OpenApi 2.x models an extension the object model does not recognise as a
    /// JsonNodeExtension wrapping the parsed JsonNode, which is the shape every x- extension in this
    /// document takes. Measured rather than assumed, and the same shape GatewayContractTests reads.
    /// </remarks>
    private static JsonNode ExtensionNode(
        IOpenApiSchema schema,
        string extensionName,
        string schemaName)
    {
        if (schema.Extensions is null
            || !schema.Extensions.TryGetValue(extensionName, out IOpenApiExtension? extension))
        {
            throw BuildFailure(
                $"Schema '{schemaName}' carries no '{extensionName}' extension. Present extensions: "
                + $"{(schema.Extensions is null || schema.Extensions.Count == 0 ? "none" : string.Join(", ", schema.Extensions.Keys.Order(StringComparer.Ordinal)))}. "
                + "The legacy identifier spellings survive the port only because this document carries "
                + "them in a machine-readable form (AAP 0.4.5.3), so a missing extension is a loss of "
                + "the very thing that makes the port auditable.");
        }

        if (extension is JsonNodeExtension node && node.Node is not null)
        {
            return node.Node;
        }

        throw BuildFailure(
            $"Schema '{schemaName}' extension '{extensionName}' is a "
            + $"{extension.GetType().Name} with no readable JSON node.");
    }

    /// <summary>Reads a schema's default as an integer, or null when it declares none.</summary>
    /// <param name="schema">Schema to read.</param>
    /// <returns>The numeric default, or null.</returns>
    private static long? NumericDefault(IOpenApiSchema schema) =>
        schema.Default is { } declared && TryReadInteger(declared, out long value) ? value : null;

    /// <summary>
    /// Reads a JSON number as a 64-bit integer without assuming which CLR type backs the node.
    /// </summary>
    /// <param name="node">The node to read.</param>
    /// <param name="value">The integer value on success.</param>
    /// <returns>True when the node is a JSON number with no fractional part.</returns>
    /// <remarks>
    /// <para>
    /// MEASURED, AND THE REASON THIS HELPER EXISTS AT ALL. The YAML reader materialises every numeric
    /// scalar as a decimal-backed <see cref="JsonValue"/>, so calling <c>GetValue&lt;long&gt;()</c>
    /// throws <see cref="InvalidOperationException"/> reading "a value of type System.Decimal cannot be
    /// converted to a System.Int64" even for a value as plain as <c>0</c>. That is a property of the
    /// reader rather than of the document, and it would have turned every vocabulary row into an
    /// unexplained error instead of a contract finding.
    /// </para>
    /// <para>
    /// Reading the node's JSON text and parsing it invariantly is backing-type agnostic, so it keeps
    /// working whether a future reader hands back an int, a long, a double or a decimal. Requiring the
    /// text to parse as an integer is deliberate rather than incidental: AAP 0.5 normalises the
    /// oracle's three declared widths onto a single JSON integer, so a fractional value would be a
    /// change to the wire form and belongs in a failure message.
    /// </para>
    /// </remarks>
    private static bool TryReadInteger(JsonNode node, out long value)
    {
        value = 0;

        if (node.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        return long.TryParse(
            node.ToJsonString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    /// <summary>
    /// Every property name declared anywhere in the Security document's component schemas, paired
    /// with the schema that declares it.
    /// </summary>
    /// <returns>Owner and property-name pairs.</returns>
    private IEnumerable<(string Owner, string Name)> AllDeclaredPropertyNames()
    {
        IDictionary<string, IOpenApiSchema> schemas =
            documents.Security.Components?.Schemas
            ?? throw BuildFailure(
                $"The Security document at '{documents.SecurityDocumentPath}' declares no "
                + "components/schemas, so no property sweep is possible.");

        foreach ((string schemaName, IOpenApiSchema schema) in schemas)
        {
            foreach ((string owner, string name) in DeclaredPropertyNames(schemaName, schema))
            {
                yield return (owner, name);
            }
        }
    }

    /// <summary>
    /// Walks one schema's inline shape and yields every property name it declares.
    /// </summary>
    /// <param name="owner">Path-like name of the schema being walked, used in failure messages.</param>
    /// <param name="schema">Schema to walk.</param>
    /// <returns>Owner and property-name pairs.</returns>
    /// <remarks>
    /// A reference is skipped rather than followed, which both terminates the walk and keeps each
    /// property attributed to the ONE schema that declares it: the target is a component schema and is
    /// therefore visited in its own right by the caller. Following references instead would report
    /// KeyReference once per referring request and make an offender's true home unidentifiable.
    /// </remarks>
    private static IEnumerable<(string Owner, string Name)> DeclaredPropertyNames(
        string owner,
        IOpenApiSchema schema)
    {
        if (schema is IOpenApiReferenceHolder)
        {
            yield break;
        }

        if (schema.Properties is not null)
        {
            foreach ((string name, IOpenApiSchema child) in schema.Properties)
            {
                yield return (owner, name);

                foreach ((string nestedOwner, string nestedName) in
                    DeclaredPropertyNames($"{owner}.{name}", child))
                {
                    yield return (nestedOwner, nestedName);
                }
            }
        }

        if (schema.Items is not null)
        {
            foreach ((string nestedOwner, string nestedName) in
                DeclaredPropertyNames($"{owner}[]", schema.Items))
            {
                yield return (nestedOwner, nestedName);
            }
        }

        if (schema.AdditionalProperties is not null)
        {
            foreach ((string nestedOwner, string nestedName) in
                DeclaredPropertyNames($"{owner}{{}}", schema.AdditionalProperties))
            {
                yield return (nestedOwner, nestedName);
            }
        }

        IEnumerable<IOpenApiSchema> composed =
        [
            .. schema.AllOf ?? [],
            .. schema.AnyOf ?? [],
            .. schema.OneOf ?? [],
        ];

        foreach (IOpenApiSchema branch in composed)
        {
            foreach ((string nestedOwner, string nestedName) in
                DeclaredPropertyNames($"{owner}/composed", branch))
            {
                yield return (nestedOwner, nestedName);
            }
        }
    }

    /// <summary>
    /// Every parameter name declared by an operation tagged as part of the CryptoService surface.
    /// </summary>
    /// <returns>Owner and parameter-name pairs.</returns>
    /// <remarks>
    /// The crypto operations of contract C-02 are all POST bodies today, so this yields nothing at
    /// present. It is swept anyway because a forbidden capability added later as a query or header
    /// parameter would bypass a body-only sweep entirely, and the absence of a KDF or an
    /// authentication tag has to hold for the whole surface rather than for one place it could appear.
    /// </remarks>
    private IEnumerable<(string Owner, string Name)> CryptoOperationParameterNames()
    {
        foreach ((string route, IOpenApiPathItem pathItem) in documents.Security.Paths)
        {
            foreach ((HttpMethod method, OpenApiOperation operation) in
                pathItem.Operations ?? new Dictionary<HttpMethod, OpenApiOperation>())
            {
                bool isCryptoOperation = operation.Tags is not null
                    && operation.Tags.Any(tag =>
                        string.Equals(tag.Name, CryptoServiceTag, StringComparison.Ordinal));

                if (!isCryptoOperation)
                {
                    continue;
                }

                foreach (IOpenApiParameter parameter in operation.Parameters ?? [])
                {
                    if (parameter.Name is { Length: > 0 } name)
                    {
                        yield return ($"{method} {route}", name);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the first vocabulary group the supplied text does not satisfy, or null when the text
    /// satisfies every group.
    /// </summary>
    /// <param name="text">Description or summary text to inspect.</param>
    /// <param name="requiredVocabulary">Synonym groups, all of which must be satisfied.</param>
    /// <returns>The unsatisfied group rendered for a message, or null.</returns>
    /// <remarks>
    /// Comparison is OrdinalIgnoreCase throughout: case-insensitive because the document shouts its
    /// weakness headings in capitals and writes the same words in lower case in prose, and ordinal
    /// because a culture-sensitive comparison could match differently on a different machine, which
    /// would break the repeatability the Golden-Master approach depends on.
    /// </remarks>
    private static string? FirstUnsatisfiedGroup(string text, string[][] requiredVocabulary)
    {
        foreach (string[] group in requiredVocabulary)
        {
            bool satisfied = group.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (!satisfied)
            {
                return string.Join(" | ", group);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the failure text for a weakness whose disclosure could not be found, naming every
    /// surface searched and the vocabulary each one was missing.
    /// </summary>
    /// <param name="row">The weakness row that failed.</param>
    /// <param name="reachableSurface">The surfaces searched, with their text.</param>
    /// <returns>The message.</returns>
    private static string BuildMissingDisclosureMessage(
        WeaknessRow row,
        IReadOnlyList<(string Surface, string Text)> reachableSurface)
    {
        StringBuilder message = new();

        message.Append(CultureInfo.InvariantCulture, $"{row.Id} is no longer disclosed anywhere a caller will see it.");
        message.Append(CultureInfo.InvariantCulture, $" Concern: {row.Concern}");
        message.Append(CultureInfo.InvariantCulture, $" Preserved behaviour proven at {row.LegacyLocator}.");
        message.Append(" Surfaces searched, with the vocabulary each was missing:");

        foreach ((string surface, string text) in reachableSurface)
        {
            string? unsatisfied = FirstUnsatisfiedGroup(text, row.RequiredVocabulary);

            message.Append(CultureInfo.InvariantCulture, $" [{surface}: {text.Length} characters, missing any of ({unsatisfied})]");
        }

        message.Append(
            " AAP 0.6.6.4 makes annotation the whole remediation for these defaults: the behaviour is"
            + " preserved deliberately (C-B), so the disclosure is the only thing protecting a caller."
            + " Restore the annotation rather than changing the default, and if the wording moved on"
            + " purpose, extend the vocabulary group in WeaknessVocabulary instead of narrowing it.");

        return message.ToString();
    }

    /// <summary>
    /// Resolves a row of one of the tables above from the key xunit supplied as the theory argument.
    /// </summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="table">The immutable table to search.</param>
    /// <param name="key">The key value xunit passed in.</param>
    /// <param name="keySelector">Reads a row's key.</param>
    /// <param name="tableName">Table name, for the failure message.</param>
    /// <returns>The single row carrying that key.</returns>
    /// <remarks>
    /// <para>
    /// WHY THE THEORY ARGUMENT IS A KEY RATHER THAN THE ROW ITSELF. Passing a record straight into a
    /// theory reads more directly, and the first form of this file did exactly that - but xunit reports
    /// it as xUnit1044, "the type argument is not serializable, which will cause Test Explorer to not
    /// enumerate individual data rows". That diagnostic is below the build's warning gate, so it would
    /// have compiled indefinitely while quietly costing something real: a run could no longer be
    /// filtered down to one weakness, and CI would report the theory as a single opaque case instead of
    /// as six named ones.
    /// </para>
    /// <para>
    /// A string key is trivially serializable, so every row enumerates and every row is addressable on
    /// its own. Nothing about the table-driven shape is lost, because the tables remain the
    /// specification and the key providers project their keys rather than restating them - a row cannot
    /// be added to a table without its case appearing, and a key cannot drift from the row it names.
    /// The test-case name carries the identifier too, reading
    /// <c>(weaknessId: "W1-ECB-IS-THE-DEFAULT-SYMMETRIC-MODE")</c>.
    /// </para>
    /// </remarks>
    private static TRow ResolveRow<TRow>(
        IReadOnlyList<TRow> table,
        string key,
        Func<TRow, string> keySelector,
        string tableName)
    {
        List<TRow> matches =
        [
            .. table.Where(row => string.Equals(keySelector(row), key, StringComparison.Ordinal)),
        ];

        // Ambiguity is an error rather than a first-match coin toss: a duplicated key would silently
        // run one row twice and never run the other, while the run still reported the expected count.
        if (matches.Count == 1)
        {
            return matches[0];
        }

        throw BuildFailure(
            matches.Count == 0
                ? $"'{key}' is not a key of {tableName}. Keys present: "
                  + $"{string.Join(", ", table.Select(keySelector))}. The key providers project this "
                  + "table, so a mismatch means the table was edited while a theory still names the old key."
                : $"'{key}' appears {matches.Count} times in {tableName}. Each key must identify exactly "
                  + "one row, so this is a duplicated table entry rather than a test defect.");
    }

    /// <summary>
    /// Writes one invariant-formatted line of evidence to the test output.
    /// </summary>
    /// <param name="message">The line, as an interpolated string.</param>
    /// <remarks>
    /// <para>
    /// Reporting where an annotation was found is part of the requirement rather than decoration, so
    /// every row emits a line naming the surface it matched and the oracle locator it pins. A reviewer
    /// auditing the preserved-weakness posture then reads the run instead of the YAML.
    /// </para>
    /// <para>
    /// Taking a <see cref="FormattableString"/> and formatting it invariantly in one place is what
    /// keeps the evidence byte-identical on every machine: a run under a culture that formats integers
    /// with a different group separator would otherwise emit different text for the same contract,
    /// which is the repeatability the Golden-Master approach depends on (AAP 0.6.7). It also avoids
    /// the trap that <c>string.Create</c> falls into here: its provider overload takes a
    /// <see langword="ref"/> interpolated-string handler, so an argument assembled with <c>+</c> is a
    /// plain string, does not convert, and fails to compile with CS1620.
    /// </para>
    /// </remarks>
    private void Report(FormattableString message) =>
        output.WriteLine(message.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Raises an xunit assertion failure, so a lookup miss is reported as a finding about the contract
    /// rather than as an unexpected exception.
    /// </summary>
    /// <param name="message">The failure text.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// Returns the concrete <see cref="FailException"/> rather than <see cref="Exception"/> so the
    /// signature states what it actually produces. The distinction matters to a reader: this type is
    /// what <c>Assert.Fail</c> raises, so a lookup miss is reported as an ASSERTION FAILURE about the
    /// contract rather than as an unexpected error in the test, which is the same discrimination
    /// ContractTestContext.cs draws between an argument defect and a finding.
    /// </remarks>
    private static FailException BuildFailure(string message) => FailException.ForFailure(message);
}
