// ==============================================================================================
//  CryptoParityTests - the CROSS-PROVIDER parity matrix for the 65-declaration legacy
//  cryptographic surface
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/
//                         EncodingProvider.cs        n_crypto.sru:L11-L13  + the 4 .srf wrappers
//                         HashProvider.cs            n_crypto.sru:L21-L22, :L27 + the 6 .srf
//                                                                              wrappers
//                         HmacProvider.cs            n_crypto.sru:L23-L26, :L28-L29
//                         SymmetricCipherProvider.cs n_crypto.sru:L30-L61
//                         RsaProvider.cs             n_crypto.sru:L19-L20, :L62-L73
//                         LegacyDefaults.cs          the four substitution decisions D1-D4
//
//  BEHAVIOURAL ORACLE ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L8-L75
//                         L8   `global type n_crypto from nonvisualobject native "pfw.dll"`
//                         L9   Copyright()   - DELIBERATE NON-PORT
//                         L10  GetVersion()  - DELIBERATE NON-PORT
//                         L11  StringToBlob(readonly string data, readonly long encoding) -> blob
//                         L12  BlobToString(readonly blob data, readonly long encoding) -> string
//                         L13  BlobReverse(ref blob data) -> boolean  (the surface's only ref blob)
//                         L14-L18  GenRandomBlob, GenRandomString x2, GenGUID x2
//                         L19-L20  GenRSAKey(readonly uint bits, ref string prikey,
//                                            ref string pubkey [, readonly boolean pemformat])
//                         L21-L22  Hash, unkeyed, over string and over blob  -> string
//                         L23-L26  Hash, keyed, the FULL 2x2 data-by-key cross product -> string
//                         L27      HashFile, unkeyed
//                         L28-L29  HashFile, keyed, with a string key then a blob key
//                         L30-L45  SymEncrypt x16
//                         L46-L61  SymDecrypt x16
//                         L62-L65  RSAEncrypt x4      L66-L69  RSADecrypt x4
//                         L70-L71  RSASign x2         L72-L73  VerifyRSASign x2
//                         L75  `global n_crypto n_crypto` - a global auto-instance shadowing its
//                              own type name; in .NET the instance is an injected dependency, so
//                              every provider below is CONSTRUCTED DIRECTLY and never reached
//                              through a static singleton
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L921-L969
//                         L924 CRYPTO_ENCODING_BASE64 = 0    L925 CRYPTO_ENCODING_HEX = 1
//                         L927 the source comment that establishes ONE hash-type set serving
//                              Hash, RSASign AND VerifyRSASign - the reason this file asserts the
//                              three providers screen against a single set rather than three
//                         L928-L933 CRYPTO_HASH_MD5 = 0 .. CRYPTO_HASH_CRC32 = 5
//                         L936-L940 CRYPTO_SYMCRYPT_TYPE_DES = 0 .. _AES256 = 4
//                         L943-L946 CRYPTO_SYMCRYPT_MODE_ECB = 0, _CBC = 1, _CFB = 2,
//                                   _DEFAULT = _ECB
//                         L949-L951 CRYPTO_RSA_PADDING_PKCS1 = 0, _OAEP = 1, _DEFAULT = _PKCS1
//                         L965-L967 CRYPTO_RSA_BITS_1024/2048/4096
//                     ws_objects/pfw.crypto.pbl.src/md5.srf:L7-L8, sha1.srf:L7-L8,
//                         sha256.srf:L7-L8   the six unkeyed hash wrappers, each a single
//                         delegating line to n_crypto.Hash with a fixed identifier
//                     ws_objects/pfw.crypto.pbl.src/base64encode.srf:L7, base64decode.srf:L7,
//                         hexencode.srf:L7, hexdecode.srf:L7   the four encoding wrappers, which
//                         FIX THE DIRECTION BY SIGNATURE: encode is blob -> string, decode is
//                         string -> blob, and neither reverse exists
//
//                     Every path above is READ-ONLY legacy oracle under constraint C-C. It is read
//                     as specification and CITED BY LOCATOR ONLY. NOTHING IN THIS FILE OPENS,
//                     READS, COPIES OR FIXTURE-LOADS ANY PATH UNDER ws_objects/** OR UNDER
//                     tests/blink, tests/sciter OR tests/webview AT TEST TIME. The HashFile
//                     coverage below writes its OWN temporary file and deletes it.
//
//  WHAT THIS SUITE IS, AND WHAT IT DELIBERATELY IS NOT
//  --------------------------------------------------------------------------------------------
//  n_crypto is a PBNI class over the closed pfw.dll: the .sru carries 65 prototypes and NO BODY
//  EXISTS ANYWHERE IN THE REPOSITORY. There is also NO CRYPTO ORACLE WINDOW - ws_objects/
//  pfw.tests.pbl.src publishes no w_test_crypto*.srw - and the only two crypto-exercising legacy
//  objects, ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru and
//  ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru, are hardcoded-secret sites and
//  therefore unusable as fixtures.
//
//  The consequence is a rule this file obeys without exception: EVERY EXPECTATION IS EITHER
//  DERIVABLE FROM THE DECLARED SURFACE PLUS THE PUBLISHED CONSTANTS, OR IS AN INDEPENDENTLY
//  PUBLISHED KNOWN-ANSWER VECTOR, OR CARRIES AN EXPLICIT ANNOTATION SAYING IT IS THE DOCUMENTED
//  SUBSTITUTION DECISION RATHER THAN MEASURED LEGACY BEHAVIOUR. No value is asserted that cannot
//  be justified from one of those three sources.
//
//  This file is the PARITY MATRIX, not a per-provider characterization suite. Each provider already
//  has, or does not need, its own sibling: EncodingProviderTests, HmacProviderTests,
//  LegacyDefaultsTests, RandomProviderTests and SymmetricCipherProviderTests characterize their
//  single subject. What NO sibling can assert, because each sees one provider, is the set of
//  AGREEMENTS BETWEEN providers - and those agreements are precisely where a six-file substitution
//  drifts apart. The five things this file exists to pin:
//
//      1. THE CENSUS. 65 declarations decompose into exactly 63 ported members and 2 deliberate
//         non-ports, and the ported members are distributed across the providers in exactly one
//         way. Asserted by reflection, so it cannot drift from the code the way a hand-kept list
//         would.
//      2. ONE HASH-TYPE SET, NOT THREE. enums.sru:L927 says the identifier set serves Hash,
//         RSASign and VerifyRSASign alike, so HashProvider, HmacProvider and RsaProvider must
//         screen against the same set - with two documented capability limits inside it.
//      3. DECISION D4 CONSUMED UNIFORMLY. The string-shaped members of four different providers
//         must all render through the ONE EncodingProvider rendering. Asserted against published
//         digests and against the byte-shaped siblings, never against this file's own output.
//      4. THE DELIBERATE ASYMMETRIES. Result type follows the payload and never the key; a string
//         key pairs only with a string initialization vector; encode is blob-to-string only and
//         decode string-to-blob only; a verify signature follows its data type. Each is CORRECT
//         legacy behaviour under constraint C-B, asserted as such, never flagged as something that
//         "ought" to be symmetrized.
//      5. THE DECLARED ARGUMENT WIDTHS. PowerBuilder `uint` is 16-bit and `ulong` is 32-bit, so a
//         silent widening to int or long is a real and invisible failure mode. Pinned by parameter
//         type AND by a value-level boundary row at the top of the width.
//
//  WHAT IT CANNOT BE EVIDENCE OF. It cannot prove byte-exact agreement with the closed binary.
//  Four observables are genuinely open and are annotated at every point of use rather than
//  smuggled in as fact: DECISION D1's text encoding of string key material and string payloads,
//  DECISION D3's all-zero initialization vector on the eight mode-without-IV arms, DECISION D4's
//  choice of Base64 over hexadecimal for every string-shaped payload, and DECISION H2's CRC32
//  variant. Adjudicating any of the four requires the behavioural oracle; this file makes each
//  one FAIL VISIBLY if a provider changes it unilaterally, which is the most a repository-only
//  suite can honestly offer.
//
//  KEY HYGIENE - CONSTRAINT C-F, WHICH IS ABSOLUTE HERE
//  --------------------------------------------------------------------------------------------
//  EVERY key, passphrase, initialization vector, authenticator key and RSA key pair below is either
//  GENERATED AT TEST TIME - by RsaProvider.GenRSAKey for the shared pair and by
//  System.Security.Cryptography's RSA.Create for the two extra import spellings - or is COMPUTED
//  FROM AN INDEX by SyntheticText and SyntheticBytes. Symmetric material is computed rather than
//  drawn from a random source ON PURPOSE: a parity matrix has to be reproducible, so every failure
//  is reproducible from the row that produced it, and an entropy source would make a rare cell
//  failure irreproducible. There is NOT ONE key literal in this file, and deliberately not one long
//  base64-shaped literal of any kind: the published known-answer vectors are written in their
//  canonical UPPERCASE HEXADECIMAL spelling, which is how the standards that publish them write
//  them, and the Base64 form that DECISION D4 requires is DERIVED from the hex inside each
//  assertion. Nor is any armour-delimiter or encoded-key-prefix token spelled out anywhere, even in
//  prose: the one test that needs armoured-but-unusable text builds it from the GENERATED pair's own
//  delimiter lines at run time. A secret-pattern sweep of this file - for armour delimiters,
//  private-key wording, encoded-key prefixes or long base64 runs - therefore returns nothing at all.
//
//  NONE of the values recorded in docs/SECRETS.md is reproduced here in any form - not the PEM RSA
//  private key at tests/blink/test_jws.htm:L8-L22, not the two base64-DER private keys at
//  ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718, not the 32
//  character configuration key at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23, and
//  not the certificate or broker credential material in
//  ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw. Those are read-only oracle under C-C
//  and appear here as locators in prose only. No assertion message and no test output carries key
//  content.
//
//  GOVERNING CONSTRAINTS. review_rules returns exactly one line, "No user rules provided", so NO
//  USER RULE GOVERNS THIS FILE and none is invented. The bar applied instead is the enterprise
//  baseline the plan sets in AAP section 0.7.2 - table-driven parity matrices, warning-clean code
//  under warnings-as-errors, no secret in any fixture, disposables disposed, and a hard per-service
//  coverage gate - together with the non-rule constraints of AAP section 0.7.3 that bind exactly as
//  rules would: C-B, C-C, C-D, C-F, C-H and C-K. Every one of them is discharged by name somewhere
//  above or at the assertion that honours it.
// ==============================================================================================

// System.Text is DELIBERATELY NOT IMPORTED. It publishes its own `EncodingProvider`, an abstract
// code-page registration hook that has nothing to do with this repository's crypto encoding
// provider, and importing it makes the name ambiguous in code AND in every documentation reference.
// The one type needed from that namespace, System.Text.Encoding, is therefore written out in full at
// its handful of use sites - the same choice the production cipher provider makes for the same
// reason.
using System.Reflection;
using System.Security.Cryptography;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The cross-provider parity matrix for the legacy cryptographic surface declared at
/// <c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every provider is constructed DIRECTLY with a real <see cref="EncodingProvider"/>. The providers
/// are documented as stateless instance services that read no configuration and hold no key
/// material, so there is nothing for a substitute to isolate and a substitute would only prove that
/// this file agrees with itself. This suite consequently needs NO HTTP HOST and deliberately does
/// not consume the service-level application factory: it is a fast, pure-unit suite.
/// </para>
/// <para>
/// Key material arrives as an argument to every call. No provider resolves a key reference - that
/// boundary belongs to the published cryptographic endpoint, which is not this suite's subject.
/// </para>
/// </remarks>
public sealed class CryptoParityTests
{
    // ==========================================================================================
    //  THE PUBLISHED IDENTIFIER SETS
    //  ------------------------------------------------------------------------------------------
    //  Referenced from PowerFramework.Shared.Kernel.Enums, never re-declared. The SCREAMING_SNAKE
    //  spellings are preserved THERE because they travel in serialized payloads, log records and
    //  characterization recordings; the repository's .editorconfig scopes its naming suppressions
    //  to the files that declare them, and a test file is deliberately NOT in that list. So this
    //  file declares no such identifier and only references them.
    // ==========================================================================================

    /// <summary>
    /// All six published hash identifiers [enums.sru:L928-L933], in declaration order.
    /// </summary>
    private static readonly long[] AllHashTypes =
    [
        Enums.CRYPTO_HASH_MD5,
        Enums.CRYPTO_HASH_SHA1,
        Enums.CRYPTO_HASH_SHA256,
        Enums.CRYPTO_HASH_SHA384,
        Enums.CRYPTO_HASH_SHA512,
        Enums.CRYPTO_HASH_CRC32,
    ];

    /// <summary>
    /// The five hash identifiers that have a keyed form, which is the published set less
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>.
    /// </summary>
    /// <remarks>
    /// The exclusion is a CAPABILITY LIMIT AND NOT A GAP: there is no HMAC-CRC32 construction, in
    /// this framework or anywhere else, because HMAC's security argument rests on properties a
    /// linear checksum does not have. <see cref="HmacProvider"/> records that as its DECISION M3
    /// and <see cref="RsaProvider"/> reaches the same conclusion for signing. Both refusals are
    /// asserted below as CORRECT behaviour.
    /// </remarks>
    private static readonly long[] KeyedHashTypes =
    [
        Enums.CRYPTO_HASH_MD5,
        Enums.CRYPTO_HASH_SHA1,
        Enums.CRYPTO_HASH_SHA256,
        Enums.CRYPTO_HASH_SHA384,
        Enums.CRYPTO_HASH_SHA512,
    ];

    /// <summary>
    /// The five published symmetric cipher types [enums.sru:L936-L940], as the 16-bit values the
    /// legacy declares the argument at.
    /// </summary>
    /// <remarks>
    /// THE CAST IS THE LEGACY'S OWN INCONSISTENCY MADE VISIBLE, not a convenience. The legacy
    /// declares every one of the thirty-two symmetric overloads with a <c>readonly uint ntype</c>
    /// parameter [n_crypto.sru:L30-L61], and PowerBuilder's <c>uint</c> is SIXTEEN BITS, while the
    /// constants that name its only legal values are declared <c>Constant Long</c> - thirty-two bits
    /// - one section earlier [enums.sru:L936-L940]. A call site therefore narrows on every single
    /// call, in the legacy exactly as here.
    /// </remarks>
    private static readonly ushort[] AllCipherTypes =
    [
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_DES,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_3DES,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
    ];

    /// <summary>
    /// The three published symmetric cipher modes [enums.sru:L943-L945], in declaration order.
    /// </summary>
    private static readonly long[] AllCipherModes =
    [
        Enums.CRYPTO_SYMCRYPT_MODE_ECB,
        Enums.CRYPTO_SYMCRYPT_MODE_CBC,
        Enums.CRYPTO_SYMCRYPT_MODE_CFB,
    ];

    /// <summary>
    /// The two published RSA padding identifiers [enums.sru:L949-L950].
    /// </summary>
    /// <remarks>
    /// No-padding is absent from the legacy set and stays absent - <c>LegacyDefaults</c> records it
    /// as an unsupported value rather than an omission, and PKCS#1 v1.5 remains the DEFAULT arm.
    /// Both are preserved weaknesses under constraint C-B.
    /// </remarks>
    private static readonly long[] AllRsaPaddings =
    [
        Enums.CRYPTO_RSA_PADDING_PKCS1,
        Enums.CRYPTO_RSA_PADDING_OAEP,
    ];

    /// <summary>
    /// The two published text encodings [enums.sru:L924-L925], in declaration order.
    /// </summary>
    private static readonly long[] AllEncodings =
    [
        Enums.CRYPTO_ENCODING_BASE64,
        Enums.CRYPTO_ENCODING_HEX,
    ];

    /// <summary>
    /// Identifiers that are NOT in the published hash set, used to prove the screen is a closed set
    /// rather than a range check.
    /// </summary>
    /// <remarks>
    /// <c>-1</c> sits below the set, <c>6</c> immediately above its last member
    /// [enums.sru:L933], and the two extremes catch a screen written as an unsigned comparison or
    /// as a cast.
    /// </remarks>
    private static readonly long[] UnpublishedHashTypes =
    [
        -1L,
        6L,
        long.MinValue,
        long.MaxValue,
    ];

    // ==========================================================================================
    //  THE SYSTEM UNDER TEST - FIVE PROVIDERS SHARING ONE ENCODING PROVIDER
    //  ------------------------------------------------------------------------------------------
    //  One EncodingProvider instance is shared by the four providers that render a string-shaped
    //  payload, which is exactly how the composition root wires them. Sharing it is what makes the
    //  DECISION D4 agreement assertions below meaningful rather than circular: the four providers
    //  are proved to route through the SAME rendering, and that rendering is then proved against
    //  independently published digests.
    // ==========================================================================================

    private readonly EncodingProvider _encoding = new();
    private readonly HashProvider _hash;
    private readonly HmacProvider _hmac;
    private readonly SymmetricCipherProvider _cipher;
    private readonly RsaProvider _rsa;

    /// <summary>
    /// Wires the five providers exactly as the container does, with no substitute anywhere.
    /// </summary>
    public CryptoParityTests()
    {
        _hash = new HashProvider(_encoding);
        _hmac = new HmacProvider(_encoding);
        _cipher = new SymmetricCipherProvider(_encoding);
        _rsa = new RsaProvider(_encoding);
    }

    // ==========================================================================================
    //  KEY MATERIAL - GENERATED, NEVER LITERAL (CONSTRAINT C-F)
    // ==========================================================================================

    /// <summary>
    /// One RSA key pair, in both of the two spellings <see cref="RsaProvider"/> emits, generated
    /// once for the whole class.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GENERATED AT TEST TIME by the system under test itself, through both
    /// <c>GenRSAKey</c> arities [n_crypto.sru:L19-L20]. Nothing about it is a literal, so constraint
    /// C-F is satisfied structurally rather than by review.
    /// </para>
    /// <para>
    /// Generated ONCE because an RSA key pair is immutable - four strings - so sharing it across
    /// tests is safe under any degree of test parallelism, and because key generation is the single
    /// most expensive operation in this file. A per-test pair would multiply that cost by the row
    /// count of every RSA theory for no additional assurance.
    /// </para>
    /// <para>
    /// The size is <see cref="LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS"/>, which is 1024
    /// [enums.sru:L965]. THAT IS A PRESERVED LEGACY WEAKNESS, not a test shortcut: 1024-bit RSA is
    /// below every current recommendation and remains a LEGAL value in this port precisely because
    /// it is legal in the legacy and rejecting it would refuse input the legacy accepted
    /// (constraint C-B). Exercising the weakest legal size is also the strictest choice available,
    /// because it gives the smallest modulus and therefore the tightest payload ceiling for the
    /// padding round trips below.
    /// </para>
    /// </remarks>
    private static readonly RsaKeyMaterial SharedRsaKeys = CreateSharedRsaKeys();

    /// <summary>
    /// The plaintext used for every RSA payload round trip.
    /// </summary>
    /// <remarks>
    /// Short by necessity and not by preference. RSA encrypts at most one modulus-worth of data, and
    /// the OAEP-SHA1 substitute that <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/> resolves to spends
    /// two digest lengths plus two bytes of that budget, leaving 86 bytes under a 1024-bit modulus.
    /// This value is well inside that ceiling so the same payload can be driven through BOTH
    /// published paddings, which is what makes the padding axis a real matrix rather than two
    /// unrelated tests. It is ASCII so its byte spelling is identical under any ASCII-compatible
    /// encoding, which removes DECISION D1 as a variable from the RSA assertions.
    /// </remarks>
    private const string RsaPayloadText = "parity payload for the RSA surface";

    /// <summary>
    /// The plaintext used for every symmetric round trip.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT a whole multiple of either published block length - 8 bytes for DES and
    /// 3DES, 16 for the AES family - so that PKCS#7 block padding is genuinely exercised on every
    /// cell rather than accidentally bypassed by an aligned payload.
    /// </remarks>
    private const string CipherPayloadText = "parity payload for the symmetric surface";

    /// <summary>
    /// The key text of RFC 4231 test case 2, which is also RFC 2202 test case 2.
    /// </summary>
    /// <remarks>
    /// AN INDEPENDENTLY PUBLISHED TEST VECTOR AND NOT A CREDENTIAL. It is four printable ASCII
    /// characters drawn from a public standards document specifically so that implementations can
    /// compare answers, it protects nothing, and it is the reason the keyed matrix below can assert
    /// absolute expected values instead of merely round-tripping. Because it is printable ASCII its
    /// text spelling and its byte spelling are identical, which is what lets ALL FOUR keyed
    /// overloads [n_crypto.sru:L23-L26] be driven over IDENTICAL material and therefore be required
    /// to agree.
    /// </remarks>
    private const string PublishedHmacKeyText = "Jefe";

    /// <summary>
    /// The message text of RFC 4231 test case 2, which is also RFC 2202 test case 2.
    /// </summary>
    private const string PublishedHmacDataText = "what do ya want for nothing?";

    /// <summary>
    /// The canonical input of the CRC-32 catalogue's check value, which is the nine ASCII digits
    /// one through nine.
    /// </summary>
    /// <remarks>
    /// Named rather than inlined because it is the ONE input whose expected CRC-32 output is a
    /// published, universally quoted constant, and the assertion that consumes it is the only place
    /// in this file where DECISION H2's variant choice becomes falsifiable.
    /// </remarks>
    private const string Crc32CheckValueInput = "123456789";

    /// <summary>
    /// Generates the shared key pair through both published <c>GenRSAKey</c> arities.
    /// </summary>
    /// <returns>The Base64-DER and PEM spellings of one private key and one public key.</returns>
    /// <remarks>
    /// Constructs its own provider pair rather than reaching for the instance fields, because a
    /// static initializer runs before any instance exists. Both providers are stateless, so a second
    /// instance is indistinguishable from the shared one.
    /// </remarks>
    private static RsaKeyMaterial CreateSharedRsaKeys()
    {
        RsaProvider provider = new(new EncodingProvider());

        string derPrivateKey = string.Empty;
        string derPublicKey = string.Empty;
        string pemPrivateKey = string.Empty;
        string pemPublicKey = string.Empty;

        // The three-argument arity [n_crypto.sru:L19] takes the format decision from the provider's
        // own default; the four-argument arity [:L20] states it. Driving both here is what makes the
        // arity-parity assertion below able to compare them.
        bool derGenerated = provider.GenRSAKey(
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS,
            ref derPrivateKey,
            ref derPublicKey);

        bool pemGenerated = provider.GenRSAKey(
            LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS,
            ref pemPrivateKey,
            ref pemPublicKey,
            pemformat: true);

        return new RsaKeyMaterial(
            derGenerated,
            derPrivateKey,
            derPublicKey,
            pemGenerated,
            pemPrivateKey,
            pemPublicKey);
    }

    /// <summary>
    /// Builds printable ASCII material of an exact byte length, computed from an index.
    /// </summary>
    /// <param name="length">The required length in bytes, which equals the character count.</param>
    /// <param name="offset">
    /// A per-role offset so that a key, an initialization vector and a payload never collide.
    /// </param>
    /// <returns>Synthetic, non-credential material of exactly <paramref name="length"/> bytes.</returns>
    /// <remarks>
    /// <para>
    /// PRINTABLE UPPERCASE ASCII IS A DELIBERATE CHOICE with two consequences the assertions depend
    /// on. First, one character is one byte under every ASCII-compatible encoding, so the text
    /// spelling and the byte spelling of the same material are BYTE-IDENTICAL and the string-key and
    /// blob-key overloads can legitimately be required to agree. Second, the material is
    /// unmistakably synthetic on sight, which is what constraint C-F asks of a fixture.
    /// </para>
    /// <para>
    /// The ascending walk also avoids the degenerate patterns a symmetric platform refuses: it never
    /// produces an all-zero, all-ones or repeating-halves key, so no cell of the cipher matrix fails
    /// for a reason unrelated to what it is testing.
    /// </para>
    /// </remarks>
    private static string SyntheticText(int length, int offset)
    {
        char[] buffer = new char[length];

        for (int index = 0; index < length; index++)
        {
            buffer[index] = (char)('A' + ((offset + index) % 26));
        }

        return new string(buffer);
    }

    /// <summary>
    /// The byte spelling of <see cref="SyntheticText(int, int)"/>, converted through the documented
    /// key-material encoding rather than through a locally chosen one.
    /// </summary>
    /// <param name="length">The required length in bytes.</param>
    /// <param name="offset">The per-role offset.</param>
    /// <returns>Exactly <paramref name="length"/> synthetic bytes.</returns>
    /// <remarks>
    /// Routed through <see cref="LegacyDefaults.KeyMaterialEncoding"/> - DECISION D1's encoding half
    /// - so that this helper cannot introduce a SECOND encoding decision behind the providers' back.
    /// If that decision is ever re-baselined against the oracle, this helper follows it.
    /// </remarks>
    private static byte[] SyntheticBytes(int length, int offset) =>
        LegacyDefaults.KeyMaterialEncoding.GetBytes(SyntheticText(length, offset));

    // ==========================================================================================
    //  THE THIRTY-TWO SYMMETRIC OVERLOAD SHAPES, ENUMERATED FROM THE DECLARATION LIST
    //  ------------------------------------------------------------------------------------------
    //  n_crypto.sru:L30-L45 is SymEncrypt and :L46-L61 is SymDecrypt, and the two blocks mirror each
    //  other declaration for declaration. Each block is the product of three independent binary
    //  axes over a fixed payload shape:
    //
    //      binary key?  supplies IV?  supplies mode?     string payload      binary payload
    //      -----------  ------------  --------------     ---------------     ---------------
    //      no           no            no                 L30 / L46           L38 / L54
    //      no           no            YES                L31 / L47           L39 / L55
    //      no           YES           no                 L32 / L48           L40 / L56
    //      no           YES           YES                L33 / L49           L41 / L57
    //      YES          no            no                 L34 / L50           L42 / L58
    //      YES          no            YES                L35 / L51           L43 / L59
    //      YES          YES           no                 L36 / L52           L44 / L60
    //      YES          YES           YES                L37 / L53           L45 / L61
    //
    //  Sixteen rows, each naming one encrypt declaration and its mirrored decrypt declaration, so
    //  the table below is 16 entries covering all 32 declarations. Two structural facts are visible
    //  straight from the table and are asserted as CORRECT legacy behaviour further down:
    //
    //      * THE IV TYPE FOLLOWS THE KEY TYPE, ALWAYS. A string key pairs only with a string IV
    //        [L32, L33, L40, L41] and a blob key only with a blob IV [L36, L37, L44, L45]. There is
    //        no mixed-type overload anywhere in L30-L61.
    //      * THE RESULT TYPE FOLLOWS THE PAYLOAD TYPE, NEVER THE KEY TYPE. A string payload with a
    //        blob key still returns a string [L34-L37]; a blob payload with a string key still
    //        returns a blob [L38-L41].
    //
    //  The eight rows with `supplies mode? YES` but `supplies IV? no` - L31, L35, L39, L43 and their
    //  mirrors L47, L51, L55, L59 - are exactly the population DECISION D3 covers, because CBC and
    //  CFB both need an initialization vector the caller has no parameter to supply.
    // ==========================================================================================

    /// <summary>
    /// The sixteen argument shapes, each pairing one <c>SymEncrypt</c> declaration with its mirrored
    /// <c>SymDecrypt</c> declaration.
    /// </summary>
    /// <remarks>
    /// Ordered so that the sequence of encrypt locators reads L30 through L45 and the sequence of
    /// decrypt locators reads L46 through L61, which lets the census assertion below compare the
    /// table against the declaration list as a set rather than trusting the ordering.
    /// </remarks>
    private static readonly CipherShape[] AllCipherShapes =
    [
        new(30, 46, BinaryPayload: false, BinaryKey: false, SuppliesIv: false, SuppliesMode: false),
        new(31, 47, BinaryPayload: false, BinaryKey: false, SuppliesIv: false, SuppliesMode: true),
        new(32, 48, BinaryPayload: false, BinaryKey: false, SuppliesIv: true, SuppliesMode: false),
        new(33, 49, BinaryPayload: false, BinaryKey: false, SuppliesIv: true, SuppliesMode: true),
        new(34, 50, BinaryPayload: false, BinaryKey: true, SuppliesIv: false, SuppliesMode: false),
        new(35, 51, BinaryPayload: false, BinaryKey: true, SuppliesIv: false, SuppliesMode: true),
        new(36, 52, BinaryPayload: false, BinaryKey: true, SuppliesIv: true, SuppliesMode: false),
        new(37, 53, BinaryPayload: false, BinaryKey: true, SuppliesIv: true, SuppliesMode: true),
        new(38, 54, BinaryPayload: true, BinaryKey: false, SuppliesIv: false, SuppliesMode: false),
        new(39, 55, BinaryPayload: true, BinaryKey: false, SuppliesIv: false, SuppliesMode: true),
        new(40, 56, BinaryPayload: true, BinaryKey: false, SuppliesIv: true, SuppliesMode: false),
        new(41, 57, BinaryPayload: true, BinaryKey: false, SuppliesIv: true, SuppliesMode: true),
        new(42, 58, BinaryPayload: true, BinaryKey: true, SuppliesIv: false, SuppliesMode: false),
        new(43, 59, BinaryPayload: true, BinaryKey: true, SuppliesIv: false, SuppliesMode: true),
        new(44, 60, BinaryPayload: true, BinaryKey: true, SuppliesIv: true, SuppliesMode: false),
        new(45, 61, BinaryPayload: true, BinaryKey: true, SuppliesIv: true, SuppliesMode: true),
    ];

    /// <summary>
    /// One argument shape of the symmetric family, identified by the legacy declaration lines it
    /// reproduces.
    /// </summary>
    /// <param name="EncryptLine">
    /// The one-based line of the <c>SymEncrypt</c> declaration in
    /// <c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru</c>, in the range 30 to 45.
    /// </param>
    /// <param name="DecryptLine">
    /// The line of the mirrored <c>SymDecrypt</c> declaration, in the range 46 to 61.
    /// </param>
    /// <param name="BinaryPayload">
    /// <see langword="true"/> for the blob-payload overloads, which return a blob;
    /// <see langword="false"/> for the string-payload overloads, which return a string.
    /// </param>
    /// <param name="BinaryKey">
    /// <see langword="true"/> for the blob-key overloads. When <see langword="true"/> and
    /// <paramref name="SuppliesIv"/> is also <see langword="true"/>, the initialization vector is a
    /// blob too - the type always follows the key.
    /// </param>
    /// <param name="SuppliesIv">Whether the overload accepts an initialization vector at all.</param>
    /// <param name="SuppliesMode">
    /// Whether the overload accepts a cipher mode. When <see langword="false"/> the operation runs in
    /// the catalogue's default mode whatever the caller intended.
    /// </param>
    /// <remarks>
    /// A private nested record so that no test-only type becomes part of the assembly's visible
    /// surface, and so that the shape can be compared and printed by value in a failure message.
    /// </remarks>
    private sealed record CipherShape(
        int EncryptLine,
        int DecryptLine,
        bool BinaryPayload,
        bool BinaryKey,
        bool SuppliesIv,
        bool SuppliesMode);

    /// <summary>
    /// The key and initialization-vector material for one cipher type, in both spellings.
    /// </summary>
    /// <param name="CipherType">The published cipher identifier the material is sized for.</param>
    /// <param name="TextKey">The key as text, exactly the required key length in bytes.</param>
    /// <param name="BinaryKey">The identical key as bytes.</param>
    /// <param name="TextIv">
    /// The initialization vector as text, exactly one block long. Meaningful only for CBC and CFB;
    /// ECB consumes no vector at all.
    /// </param>
    /// <param name="BinaryIv">The identical vector as bytes.</param>
    /// <remarks>
    /// The text and binary spellings are BYTE-IDENTICAL by construction, which is the precondition
    /// that makes "the four key spellings of one operation must agree" a legitimate assertion rather
    /// than a coincidence.
    /// </remarks>
    private sealed record CipherMaterial(
        ushort CipherType,
        string TextKey,
        byte[] BinaryKey,
        string TextIv,
        byte[] BinaryIv);

    /// <summary>
    /// The four spellings of one RSA key pair, plus the boolean each generation call returned.
    /// </summary>
    /// <param name="DerGenerated">What the three-argument arity returned [n_crypto.sru:L19].</param>
    /// <param name="DerPrivateKey">The Base64-DER private key, which is the provider's default.</param>
    /// <param name="DerPublicKey">The Base64-DER public key.</param>
    /// <param name="PemGenerated">What the four-argument arity returned [n_crypto.sru:L20].</param>
    /// <param name="PemPrivateKey">The armoured private key.</param>
    /// <param name="PemPublicKey">The armoured public key.</param>
    /// <remarks>
    /// HOLDS GENERATED TEST KEY MATERIAL AND NOTHING ELSE. It is never logged, never carried into an
    /// assertion message and never persisted; the process exit destroys it.
    /// </remarks>
    private sealed record RsaKeyMaterial(
        bool DerGenerated,
        string DerPrivateKey,
        string DerPublicKey,
        bool PemGenerated,
        string PemPrivateKey,
        string PemPublicKey);

    /// <summary>
    /// A temporary file that this test owns end to end, for the file-shaped hash members.
    /// </summary>
    /// <remarks>
    /// CONSTRAINT C-C IS THE WHOLE REASON THIS TYPE EXISTS. <c>HashFile</c> [n_crypto.sru:L27] and
    /// its two keyed siblings [:L28-L29] need a real path, and the legacy oracle's own call sites
    /// point at shipped native binaries under the read-only tree. Pointing a test there would make
    /// the suite read the oracle at run time, so instead each file-shaped test WRITES ITS OWN FILE
    /// into the platform temporary directory and DELETES IT on the way out, including when the
    /// assertion fails.
    /// </remarks>
    private sealed class TemporaryPayloadFile : IDisposable
    {
        /// <summary>
        /// Writes <paramref name="content"/> to a freshly named temporary path.
        /// </summary>
        /// <param name="content">The bytes the file is to contain. May be empty.</param>
        internal TemporaryPayloadFile(byte[] content)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"pfw-crypto-parity-{Guid.NewGuid():N}.bin");

            File.WriteAllBytes(Path, content);
        }

        /// <summary>
        /// The absolute path of the file, valid until this instance is disposed.
        /// </summary>
        internal string Path { get; }

        /// <summary>
        /// Deletes the file, tolerating a path that has already gone.
        /// </summary>
        public void Dispose() => File.Delete(Path);
    }

    /// <summary>
    /// Builds key and vector material sized for one cipher type from the published metrics.
    /// </summary>
    /// <param name="ntype">The published cipher identifier.</param>
    /// <param name="zeroVector">
    /// When <see langword="true"/>, the initialization vector is all zero bytes - the vector
    /// DECISION D3 synthesizes for the eight mode-without-IV arms. It is COMPUTED from the block
    /// length, never written down as a literal.
    /// </param>
    /// <returns>Material whose text and byte spellings are byte-identical.</returns>
    /// <remarks>
    /// Lengths come from <see cref="LegacyDefaults.GetSymmetricCipherMetrics(ushort)"/> rather than
    /// from a table restated here, so a cipher's key or block length cannot be asserted against one
    /// value in the catalogue and a different one in this file.
    /// </remarks>
    private static CipherMaterial CreateCipherMaterial(ushort ntype, bool zeroVector = false)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);

        string textKey = SyntheticText(metrics.KeyLengthBytes, offset: 3);
        string textIv = zeroVector
            ? new string('\0', metrics.IvLengthBytes)
            : SyntheticText(metrics.IvLengthBytes, offset: 11);

        return new CipherMaterial(
            ntype,
            textKey,
            LegacyDefaults.KeyMaterialEncoding.GetBytes(textKey),
            textIv,
            LegacyDefaults.KeyMaterialEncoding.GetBytes(textIv));
    }

    // ==========================================================================================
    //  THE SHAPE DISPATCHERS
    //  ------------------------------------------------------------------------------------------
    //  Four dispatchers, one per payload shape and direction, each a total switch over the three
    //  binary axes. They exist so that every one of the thirty-two declarations is reached by NAME
    //  from a table-driven theory: a dispatcher arm that is never entered is a shape that was never
    //  covered, and the switch being total over three booleans means the compiler itself proves all
    //  eight arms of each dispatcher exist. Each arm carries the legacy line it reproduces.
    // ==========================================================================================

    /// <summary>
    /// Encrypts a string payload through the requested shape [n_crypto.sru:L30-L37].
    /// </summary>
    /// <param name="shape">The argument shape to exercise.</param>
    /// <param name="material">Key and vector material sized for the shape's cipher type.</param>
    /// <param name="plain">The text to encrypt.</param>
    /// <param name="mode">
    /// The cipher mode. IGNORED by the four shapes that declare no mode parameter, which run in the
    /// catalogue default instead - that is the legacy's behaviour and it is correct here.
    /// </param>
    /// <returns>Cipher text in the DECISION D4 payload encoding.</returns>
    private string EncryptThroughShape(
        CipherShape shape,
        CipherMaterial material,
        string plain,
        long mode) =>
        (shape.BinaryKey, shape.SuppliesIv, shape.SuppliesMode) switch
        {
            (false, false, false) => _cipher.SymEncrypt(plain, material.TextKey, material.CipherType),
            (false, false, true) => _cipher.SymEncrypt(plain, material.TextKey, material.CipherType, mode),
            (false, true, false) => _cipher.SymEncrypt(plain, material.TextKey, material.TextIv, material.CipherType),
            (false, true, true) => _cipher.SymEncrypt(plain, material.TextKey, material.TextIv, material.CipherType, mode),
            (true, false, false) => _cipher.SymEncrypt(plain, material.BinaryKey, material.CipherType),
            (true, false, true) => _cipher.SymEncrypt(plain, material.BinaryKey, material.CipherType, mode),
            (true, true, false) => _cipher.SymEncrypt(plain, material.BinaryKey, material.BinaryIv, material.CipherType),
            (true, true, true) => _cipher.SymEncrypt(plain, material.BinaryKey, material.BinaryIv, material.CipherType, mode),
        };

    /// <summary>
    /// Encrypts a blob payload through the requested shape [n_crypto.sru:L38-L45].
    /// </summary>
    /// <param name="shape">The argument shape to exercise.</param>
    /// <param name="material">Key and vector material sized for the shape's cipher type.</param>
    /// <param name="plain">The bytes to encrypt.</param>
    /// <param name="mode">The cipher mode, ignored by the four mode-omitting shapes.</param>
    /// <returns>Raw cipher bytes, with no payload encoding applied.</returns>
    private byte[] EncryptThroughShape(
        CipherShape shape,
        CipherMaterial material,
        byte[] plain,
        long mode) =>
        (shape.BinaryKey, shape.SuppliesIv, shape.SuppliesMode) switch
        {
            (false, false, false) => _cipher.SymEncrypt(plain, material.TextKey, material.CipherType),
            (false, false, true) => _cipher.SymEncrypt(plain, material.TextKey, material.CipherType, mode),
            (false, true, false) => _cipher.SymEncrypt(plain, material.TextKey, material.TextIv, material.CipherType),
            (false, true, true) => _cipher.SymEncrypt(plain, material.TextKey, material.TextIv, material.CipherType, mode),
            (true, false, false) => _cipher.SymEncrypt(plain, material.BinaryKey, material.CipherType),
            (true, false, true) => _cipher.SymEncrypt(plain, material.BinaryKey, material.CipherType, mode),
            (true, true, false) => _cipher.SymEncrypt(plain, material.BinaryKey, material.BinaryIv, material.CipherType),
            (true, true, true) => _cipher.SymEncrypt(plain, material.BinaryKey, material.BinaryIv, material.CipherType, mode),
        };

    /// <summary>
    /// Decrypts a string payload through the shape mirroring the encrypt shape
    /// [n_crypto.sru:L46-L53].
    /// </summary>
    /// <param name="shape">The argument shape whose mirrored decrypt declaration to exercise.</param>
    /// <param name="material">The same material the encrypt call used.</param>
    /// <param name="cipher">Cipher text in the DECISION D4 payload encoding.</param>
    /// <param name="mode">The cipher mode, ignored by the four mode-omitting shapes.</param>
    /// <returns>The recovered text.</returns>
    private string DecryptThroughShape(
        CipherShape shape,
        CipherMaterial material,
        string cipher,
        long mode) =>
        (shape.BinaryKey, shape.SuppliesIv, shape.SuppliesMode) switch
        {
            (false, false, false) => _cipher.SymDecrypt(cipher, material.TextKey, material.CipherType),
            (false, false, true) => _cipher.SymDecrypt(cipher, material.TextKey, material.CipherType, mode),
            (false, true, false) => _cipher.SymDecrypt(cipher, material.TextKey, material.TextIv, material.CipherType),
            (false, true, true) => _cipher.SymDecrypt(cipher, material.TextKey, material.TextIv, material.CipherType, mode),
            (true, false, false) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.CipherType),
            (true, false, true) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.CipherType, mode),
            (true, true, false) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.BinaryIv, material.CipherType),
            (true, true, true) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.BinaryIv, material.CipherType, mode),
        };

    /// <summary>
    /// Decrypts a blob payload through the shape mirroring the encrypt shape
    /// [n_crypto.sru:L54-L61].
    /// </summary>
    /// <param name="shape">The argument shape whose mirrored decrypt declaration to exercise.</param>
    /// <param name="material">The same material the encrypt call used.</param>
    /// <param name="cipher">Raw cipher bytes.</param>
    /// <param name="mode">The cipher mode, ignored by the four mode-omitting shapes.</param>
    /// <returns>The recovered bytes.</returns>
    private byte[] DecryptThroughShape(
        CipherShape shape,
        CipherMaterial material,
        byte[] cipher,
        long mode) =>
        (shape.BinaryKey, shape.SuppliesIv, shape.SuppliesMode) switch
        {
            (false, false, false) => _cipher.SymDecrypt(cipher, material.TextKey, material.CipherType),
            (false, false, true) => _cipher.SymDecrypt(cipher, material.TextKey, material.CipherType, mode),
            (false, true, false) => _cipher.SymDecrypt(cipher, material.TextKey, material.TextIv, material.CipherType),
            (false, true, true) => _cipher.SymDecrypt(cipher, material.TextKey, material.TextIv, material.CipherType, mode),
            (true, false, false) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.CipherType),
            (true, false, true) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.CipherType, mode),
            (true, true, false) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.BinaryIv, material.CipherType),
            (true, true, true) => _cipher.SymDecrypt(cipher, material.BinaryKey, material.BinaryIv, material.CipherType, mode),
        };

    // ==========================================================================================
    //  THEORY DATA - PRIMITIVES ONLY
    //  ------------------------------------------------------------------------------------------
    //  Every row carries only values xUnit can serialize, so each theory case is individually
    //  addressable and re-runnable. Where a row needs a shape or a provider type it carries an INDEX
    //  or a SIMPLE NAME and the test resolves it, rather than smuggling a reference type into the
    //  data set.
    // ==========================================================================================

    /// <summary>
    /// The six published unkeyed hash algorithms with a published known-answer vector each.
    /// </summary>
    /// <returns>
    /// One row per algorithm: the identifier, a human-readable algorithm name for the failure
    /// message, the input text, and the expected digest in canonical uppercase hexadecimal.
    /// </returns>
    /// <remarks>
    /// <para>
    /// PROVENANCE OF EVERY EXPECTED VALUE, so that none of them is a magic constant. The five
    /// cryptographic digests are the published <c>"abc"</c> vectors: MD5 from RFC 1321 appendix A.5,
    /// SHA-1 and the SHA-2 family from the FIPS 180-4 example set. The CRC-32 row is the CRC
    /// catalogue's CHECK VALUE - the defined output of the nine ASCII digits one through nine - which
    /// is the single most widely quoted CRC-32 test value and the only value that distinguishes one
    /// CRC-32 variant from another.
    /// </para>
    /// <para>
    /// The CRC-32 row therefore carries an ANNOTATION rather than a claim of legacy parity. Its
    /// expected value assumes DECISION H2's variant: the reflected IEEE 802.3 and PKZIP form, with
    /// reflected polynomial <c>0xEDB88320</c>, an initial register of all ones, a final complement,
    /// and the four result bytes laid out MOST SIGNIFICANT FIRST. That variant is a REASONED
    /// INFERENCE from the repository's own zlib attribution and not a measurement of the closed
    /// binary, so if the oracle ever contradicts it this row is the place the contradiction surfaces.
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string, string> PublishedDigestVectors()
    {
        return new TheoryData<long, string, string, string>
        {
            // RFC 1321 appendix A.5.
            { Enums.CRYPTO_HASH_MD5, "MD5", "abc", "900150983CD24FB0D6963F7D28E17F72" },

            // FIPS 180-4 example set.
            { Enums.CRYPTO_HASH_SHA1, "SHA1", "abc", "A9993E364706816ABA3E25717850C26C9CD0D89D" },
            {
                Enums.CRYPTO_HASH_SHA256,
                "SHA256",
                "abc",
                "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"
            },
            {
                Enums.CRYPTO_HASH_SHA384,
                "SHA384",
                "abc",
                "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED" +
                "8086072BA1E7CC2358BAECA134C825A7"
            },
            {
                Enums.CRYPTO_HASH_SHA512,
                "SHA512",
                "abc",
                "DDAF35A193617ABACC417349AE20413112E6FA4E89A97EA20A9EEEE64B55D39A" +
                "2192992A274FC1A836BA3C23A3FEEBBD454D4423643CE80E2A9AC94FA54CA49F"
            },

            // The CRC catalogue check value - see the remarks above for why this row is annotated.
            { Enums.CRYPTO_HASH_CRC32, "CRC32", Crc32CheckValueInput, "CBF43926" },
        };
    }

    /// <summary>
    /// The six published unkeyed hash algorithms with the published digest of the EMPTY input.
    /// </summary>
    /// <returns>One row per algorithm: the identifier, its name, and the expected hexadecimal.</returns>
    /// <remarks>
    /// The empty input is a real and separately published case rather than a degenerate one: every
    /// algorithm has a defined, non-empty digest of zero bytes, and the CRC-32 arm's is the only
    /// all-zero result in the set - which is exactly the value a broken final complement or a wrong
    /// initial register would perturb. All five cryptographic values are the standard published
    /// empty-string digests.
    /// </remarks>
    public static TheoryData<long, string, string> PublishedEmptyInputDigests()
    {
        return new TheoryData<long, string, string>
        {
            { Enums.CRYPTO_HASH_MD5, "MD5", "D41D8CD98F00B204E9800998ECF8427E" },
            { Enums.CRYPTO_HASH_SHA1, "SHA1", "DA39A3EE5E6B4B0D3255BFEF95601890AFD80709" },
            {
                Enums.CRYPTO_HASH_SHA256,
                "SHA256",
                "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"
            },
            {
                Enums.CRYPTO_HASH_SHA384,
                "SHA384",
                "38B060A751AC96384CD9327EB1B1E36A21FDB71114BE07434C0CC7BF63F6E1DA" +
                "274EDEBFE76F65FBD51AD2F14898B95B"
            },
            {
                Enums.CRYPTO_HASH_SHA512,
                "SHA512",
                "CF83E1357EEFB8BDF1542850D66D8007D620E4050B5715DC83F4A921D36CE9CE" +
                "47D0D13C5D85F2B0FF8318D2877EEC2F63B931BD47417A81A538327AF927DA3E"
            },
            { Enums.CRYPTO_HASH_CRC32, "CRC32", "00000000" },
        };
    }

    /// <summary>
    /// The five keyed algorithms with the published authenticator of RFC 4231 test case 2.
    /// </summary>
    /// <returns>One row per algorithm: the identifier, its name, and the expected hexadecimal.</returns>
    /// <remarks>
    /// <para>
    /// One key and one message serve all five rows: the key text and message text of RFC 4231
    /// section 4.3, which is the same case as RFC 2202 test case 2. The MD5 and SHA-1 expectations
    /// come from RFC 2202 test case 2; the SHA-256, SHA-384 and SHA-512 expectations from RFC 4231
    /// section 4.3.
    /// </para>
    /// <para>
    /// USING ONE PUBLISHED CASE ACROSS ALL FIVE ALGORITHMS IS THE POINT. Because both the key and the
    /// message are printable ASCII, their text and byte spellings are byte-identical, so all four
    /// keyed overloads [n_crypto.sru:L23-L26] can be driven over the SAME material and are then
    /// required to produce the SAME published answer. That turns the two-by-two cross product from
    /// four unrelated round trips into a single agreement assertion with an externally fixed target.
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string> PublishedAuthenticatorVectors()
    {
        return new TheoryData<long, string, string>
        {
            { Enums.CRYPTO_HASH_MD5, "HMAC-MD5", "750C783E6AB0B503EAA86E310A5DB738" },
            { Enums.CRYPTO_HASH_SHA1, "HMAC-SHA1", "EFFCDF6AE5EB2FA2D27416D5F184DF9C259A7C79" },
            {
                Enums.CRYPTO_HASH_SHA256,
                "HMAC-SHA256",
                "5BDCC146BF60754E6A042426089575C75A003F089D2739839DEC58B964EC3843"
            },
            {
                Enums.CRYPTO_HASH_SHA384,
                "HMAC-SHA384",
                "AF45D2E376484031617F78D2B58A6B1B9C7EF464F5A01B47E42EC3736322445E" +
                "8E2240CA5E69E2C78B3239ECFAB21649"
            },
            {
                Enums.CRYPTO_HASH_SHA512,
                "HMAC-SHA512",
                "164B7A7BFCF819E2E395FBE73B56E0A387BD64222E831FD610270CD7EA250554" +
                "9758BF75C05A994A6D034F65F8F0E6FDCAEAB1A34D4A6B4B636E070A38BCE737"
            },
        };
    }

    /// <summary>
    /// Every one of the six published hash identifiers, one per row.
    /// </summary>
    /// <returns>One row per identifier [enums.sru:L928-L933].</returns>
    public static TheoryData<long> AllHashTypeRows()
    {
        TheoryData<long> data = new();

        foreach (long ntype in AllHashTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// The five identifiers that have a keyed form.
    /// </summary>
    /// <returns>One row per identifier, the published set less the checksum.</returns>
    public static TheoryData<long> KeyedHashTypeRows()
    {
        TheoryData<long> data = new();

        foreach (long ntype in KeyedHashTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// Identifiers outside the published set.
    /// </summary>
    /// <returns>One row per unpublished identifier.</returns>
    public static TheoryData<long> UnpublishedHashTypeRows()
    {
        TheoryData<long> data = new();

        foreach (long ntype in UnpublishedHashTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// The full fifteen-cell cipher-type-by-cipher-mode grid.
    /// </summary>
    /// <returns>One row per cell: five published types by three published modes.</returns>
    public static TheoryData<ushort, long> TypeAndModeGrid()
    {
        TheoryData<ushort, long> data = new();

        foreach (ushort ntype in AllCipherTypes)
        {
            foreach (long mode in AllCipherModes)
            {
                data.Add(ntype, mode);
            }
        }

        return data;
    }

    /// <summary>
    /// The complete symmetric matrix: every argument shape on every cell of the type-by-mode grid.
    /// </summary>
    /// <returns>
    /// One row per shape per cell - sixteen shapes by five types by three modes, which is 240 rows
    /// reaching all thirty-two declarations [n_crypto.sru:L30-L61].
    /// </returns>
    /// <remarks>
    /// THE ROW COUNT IS ITSELF AN ASSERTION and is checked below, because a matrix that silently
    /// collapsed to one row would still go green. The shape arrives as an INDEX into
    /// <see cref="AllCipherShapes"/> so that every value in the row is serializable and each of the
    /// 240 cases can be re-run individually from its display name.
    /// </remarks>
    public static TheoryData<int, ushort, long> ShapeByTypeAndModeMatrix()
    {
        TheoryData<int, ushort, long> data = new();

        for (int shapeIndex = 0; shapeIndex < AllCipherShapes.Length; shapeIndex++)
        {
            foreach (ushort ntype in AllCipherTypes)
            {
                foreach (long mode in AllCipherModes)
                {
                    data.Add(shapeIndex, ntype, mode);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// The five published cipher types alone, for the assertions that do not vary the mode.
    /// </summary>
    /// <returns>One row per published cipher type.</returns>
    public static TheoryData<ushort> CipherTypeRows()
    {
        TheoryData<ushort> data = new();

        foreach (ushort ntype in AllCipherTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// The two published text encodings.
    /// </summary>
    /// <returns>One row per encoding [enums.sru:L924-L925].</returns>
    public static TheoryData<long> EncodingRows()
    {
        TheoryData<long> data = new();

        foreach (long encoding in AllEncodings)
        {
            data.Add(encoding);
        }

        return data;
    }

    /// <summary>
    /// The two published RSA padding identifiers.
    /// </summary>
    /// <returns>One row per padding [enums.sru:L949-L950].</returns>
    public static TheoryData<long> RsaPaddingRows()
    {
        TheoryData<long> data = new();

        foreach (long padding in AllRsaPaddings)
        {
            data.Add(padding);
        }

        return data;
    }

    /// <summary>
    /// The declared argument widths of the legacy surface, one row per parameter that carries a
    /// width the port could silently get wrong.
    /// </summary>
    /// <returns>
    /// One row per parameter: the provider's simple type name, the method name, the parameter name,
    /// and the expected common-language-runtime type name.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS THE DETECTOR FOR A SILENT WIDENING, which is otherwise invisible: widening a 16-bit
    /// parameter to <c>int</c> compiles, passes every behavioural test, and changes the contract.
    /// PowerBuilder's <c>uint</c> is SIXTEEN bits and its <c>ulong</c> is THIRTY-TWO, while its
    /// <c>long</c> is thirty-two bits signed - so the mapping the port must hold is
    /// <c>uint</c> to <see cref="ushort"/>, <c>ulong</c> to <see cref="uint"/> and <c>long</c> to
    /// <see cref="long"/>.
    /// </para>
    /// <para>
    /// Names travel as strings rather than as <see cref="Type"/> values so every row is serializable.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, string> DeclaredArgumentWidths()
    {
        return new TheoryData<string, string, string, string>
        {
            // n_crypto.sru:L19-L20 - `readonly uint bits`, a 16-bit PowerBuilder uint.
            { nameof(RsaProvider), "GenRSAKey", "bits", nameof(UInt16) },

            // n_crypto.sru:L30-L61 - `readonly uint ntype` on all thirty-two symmetric
            // declarations, even though the constants naming its legal values are Constant Long.
            { nameof(SymmetricCipherProvider), "SymEncrypt", "ntype", nameof(UInt16) },
            { nameof(SymmetricCipherProvider), "SymDecrypt", "ntype", nameof(UInt16) },

            // n_crypto.sru:L31 and siblings - `readonly long mode`, a 32-bit signed PowerBuilder
            // long, which the shared kernel maps to C# long.
            { nameof(SymmetricCipherProvider), "SymEncrypt", "mode", nameof(Int64) },
            { nameof(SymmetricCipherProvider), "SymDecrypt", "mode", nameof(Int64) },

            // n_crypto.sru:L21-L29 - `readonly long ntype` on every hash declaration, keyed or not.
            { nameof(HashProvider), "Hash", "ntype", nameof(Int64) },
            { nameof(HashProvider), "HashFile", "ntype", nameof(Int64) },
            { nameof(HmacProvider), "Hash", "ntype", nameof(Int64) },
            { nameof(HmacProvider), "HashFile", "ntype", nameof(Int64) },

            // n_crypto.sru:L11-L12 - `readonly long encoding`.
            { nameof(EncodingProvider), "BlobToString", "encoding", nameof(Int64) },
            { nameof(EncodingProvider), "StringToBlob", "encoding", nameof(Int64) },

            // n_crypto.sru:L63, :L65, :L67, :L69 - `readonly long padding`; :L70-L73 - `readonly
            // long ntype`, the same width and the same set as the hash declarations
            // [enums.sru:L927].
            { nameof(RsaProvider), "RSAEncrypt", "padding", nameof(Int64) },
            { nameof(RsaProvider), "RSADecrypt", "padding", nameof(Int64) },
            { nameof(RsaProvider), "RSASign", "ntype", nameof(Int64) },
            { nameof(RsaProvider), "VerifyRSASign", "ntype", nameof(Int64) },
        };
    }

    /// <summary>
    /// Resolves a provider's simple name to its runtime type, for the reflection-driven theories.
    /// </summary>
    /// <param name="simpleName">The simple type name carried in a theory row.</param>
    /// <returns>The provider type.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="simpleName"/> is not one of the five providers this suite depends on.
    /// </exception>
    /// <remarks>
    /// A closed switch over the five providers rather than an assembly-wide name lookup, so that a
    /// misspelled row fails loudly at the switch instead of silently resolving to nothing.
    /// </remarks>
    private static Type ResolveProviderType(string simpleName) => simpleName switch
    {
        nameof(EncodingProvider) => typeof(EncodingProvider),
        nameof(HashProvider) => typeof(HashProvider),
        nameof(HmacProvider) => typeof(HmacProvider),
        nameof(SymmetricCipherProvider) => typeof(SymmetricCipherProvider),
        nameof(RsaProvider) => typeof(RsaProvider),
        _ => throw new ArgumentOutOfRangeException(
            nameof(simpleName),
            simpleName,
            "Not one of the five providers this parity suite depends on."),
    };

    /// <summary>
    /// The public instance methods a provider declares under one name, excluding anything inherited.
    /// </summary>
    /// <param name="type">The provider type.</param>
    /// <param name="name">The declaration name, for example <c>SymEncrypt</c>.</param>
    /// <returns>Every overload of that name the provider itself declares.</returns>
    /// <remarks>
    /// <c>DeclaredOnly</c> keeps the object-inherited members out of every census below, so the
    /// counts reconcile against the legacy declaration list rather than against the runtime's base
    /// type.
    /// </remarks>
    private static MethodInfo[] DeclaredOverloads(Type type, string name) =>
        [.. type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))];

    /// <summary>
    /// Every public instance method a provider declares itself.
    /// </summary>
    /// <param name="type">The provider type.</param>
    /// <returns>The provider's whole published instance surface.</returns>
    private static MethodInfo[] DeclaredSurface(Type type) =>
        [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)];

    // ==========================================================================================
    //  REGION 1 - THE CENSUS: 65 DECLARATIONS, 63 PORTED, 2 DELIBERATELY NOT
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73
    //  ------------------------------------------------------------------------------------------
    //  Counted by reflection rather than kept as a list, because a hand-kept list drifts from the
    //  code and a reflected count cannot. This is the assertion that makes the whole folder's
    //  reconciliation auditable in one place.
    // ==========================================================================================

    /// <summary>
    /// The five providers this suite depends on publish exactly the 58 members that reproduce a
    /// declaration, plus exactly the 10 that reproduce a legacy global-function wrapper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ARITHMETIC, so a reader can re-derive it instead of trusting it. The declaration list
    /// [n_crypto.sru:L9-L73] carries 65 prototypes. Two are deliberate non-ports, <c>Copyright</c> at
    /// L9 and <c>GetVersion</c> at L10, leaving 63. Five of the 63 - the random blob, the two random
    /// strings and the two identifiers at L14-L18 - belong to the random provider, which is a
    /// separate subject with its own suite and is deliberately not a dependency of this file. That
    /// leaves 58 here:
    /// </para>
    /// <para>
    /// 32 symmetric [L30-L61] plus 3 unkeyed hash [L21-L22, L27] plus 6 keyed hash [L23-L26,
    /// L28-L29] plus 3 encoding [L11-L13] plus 14 RSA [L19-L20, L62-L73] = 58. With the random
    /// provider's 5, that is 63 ported and the census closes.
    /// </para>
    /// <para>
    /// The 10 wrapper members are a SEPARATE population and are counted separately on purpose: they
    /// reproduce legacy global functions - <c>md5.srf</c>, <c>sha1.srf</c>, <c>sha256.srf</c> with two
    /// overloads each, and <c>base64encode.srf</c>, <c>base64decode.srf</c>, <c>hexencode.srf</c>,
    /// <c>hexdecode.srf</c> with one each - not <c>n_crypto</c> declarations. Folding them into the
    /// 58 would break the reconciliation.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFiveProvidersPublishExactlyFiftyEightDeclarationsAndTenWrappers()
    {
        int declarationDerived =
            DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt").Length +
            DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt").Length +
            DeclaredOverloads(typeof(HashProvider), "Hash").Length +
            DeclaredOverloads(typeof(HashProvider), "HashFile").Length +
            DeclaredOverloads(typeof(HmacProvider), "Hash").Length +
            DeclaredOverloads(typeof(HmacProvider), "HashFile").Length +
            DeclaredOverloads(typeof(EncodingProvider), "StringToBlob").Length +
            DeclaredOverloads(typeof(EncodingProvider), "BlobToString").Length +
            DeclaredOverloads(typeof(EncodingProvider), "BlobReverse").Length +
            DeclaredOverloads(typeof(RsaProvider), "GenRSAKey").Length +
            DeclaredOverloads(typeof(RsaProvider), "RSAEncrypt").Length +
            DeclaredOverloads(typeof(RsaProvider), "RSADecrypt").Length +
            DeclaredOverloads(typeof(RsaProvider), "RSASign").Length +
            DeclaredOverloads(typeof(RsaProvider), "VerifyRSASign").Length;

        int wrapperDerived =
            DeclaredOverloads(typeof(HashProvider), "Md5").Length +
            DeclaredOverloads(typeof(HashProvider), "Sha1").Length +
            DeclaredOverloads(typeof(HashProvider), "Sha256").Length +
            DeclaredOverloads(typeof(EncodingProvider), "Base64Encode").Length +
            DeclaredOverloads(typeof(EncodingProvider), "Base64Decode").Length +
            DeclaredOverloads(typeof(EncodingProvider), "HexEncode").Length +
            DeclaredOverloads(typeof(EncodingProvider), "HexDecode").Length;

        Assert.Equal(58, declarationDerived);
        Assert.Equal(10, wrapperDerived);

        // Nothing else is published: the two populations account for the entire visible instance
        // surface of all five providers, so no member escaped the census.
        int wholeSurface =
            DeclaredSurface(typeof(EncodingProvider)).Length +
            DeclaredSurface(typeof(HashProvider)).Length +
            DeclaredSurface(typeof(HmacProvider)).Length +
            DeclaredSurface(typeof(SymmetricCipherProvider)).Length +
            DeclaredSurface(typeof(RsaProvider)).Length;

        Assert.Equal(declarationDerived + wrapperDerived, wholeSurface);
    }

    /// <summary>
    /// Every declaration group has exactly the overload count the legacy declares for it.
    /// </summary>
    /// <remarks>
    /// The total alone is not sufficient: two errors that cancel - an extra symmetric overload and a
    /// missing RSA one - would leave the sum intact. Each group is therefore pinned individually
    /// against its own line range.
    /// </remarks>
    [Fact]
    public void EveryDeclarationGroupHasItsPublishedOverloadCount()
    {
        // n_crypto.sru:L30-L45 and :L46-L61.
        Assert.Equal(16, DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt").Length);
        Assert.Equal(16, DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt").Length);

        // n_crypto.sru:L21-L22 unkeyed over string and blob; :L27 the unkeyed file member.
        Assert.Equal(2, DeclaredOverloads(typeof(HashProvider), "Hash").Length);
        Assert.Single(DeclaredOverloads(typeof(HashProvider), "HashFile"));

        // n_crypto.sru:L23-L26 the full data-by-key cross product; :L28-L29 the two keyed file
        // members, one per key spelling.
        Assert.Equal(4, DeclaredOverloads(typeof(HmacProvider), "Hash").Length);
        Assert.Equal(2, DeclaredOverloads(typeof(HmacProvider), "HashFile").Length);

        // n_crypto.sru:L11, :L12, :L13 - one each, and L13 is the surface's only ref blob.
        Assert.Single(DeclaredOverloads(typeof(EncodingProvider), "StringToBlob"));
        Assert.Single(DeclaredOverloads(typeof(EncodingProvider), "BlobToString"));
        Assert.Single(DeclaredOverloads(typeof(EncodingProvider), "BlobReverse"));

        // n_crypto.sru:L19-L20, :L62-L65, :L66-L69, :L70-L71, :L72-L73.
        Assert.Equal(2, DeclaredOverloads(typeof(RsaProvider), "GenRSAKey").Length);
        Assert.Equal(4, DeclaredOverloads(typeof(RsaProvider), "RSAEncrypt").Length);
        Assert.Equal(4, DeclaredOverloads(typeof(RsaProvider), "RSADecrypt").Length);
        Assert.Equal(2, DeclaredOverloads(typeof(RsaProvider), "RSASign").Length);
        Assert.Equal(2, DeclaredOverloads(typeof(RsaProvider), "VerifyRSASign").Length);
    }

    /// <summary>
    /// The two deliberate non-ports are absent from every provider, and no wrapper was invented for
    /// an algorithm the legacy publishes no wrapper for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Copyright</c> [n_crypto.sru:L9] and <c>GetVersion</c> [:L10] report the closed binary's own
    /// identity. There is no binary to report on once the behaviour is substituted, so porting them
    /// would mean inventing an answer. Their absence is the 63-of-65 census and is asserted rather
    /// than assumed.
    /// </para>
    /// <para>
    /// The wrapper check is the same principle in the other direction. The legacy publishes
    /// <c>md5.srf</c>, <c>sha1.srf</c> and <c>sha256.srf</c> and NO <c>sha384.srf</c> or
    /// <c>sha512.srf</c>, so those two algorithms are reachable only through the identifier. Adding a
    /// convenience wrapper for them would invent surface the legacy never had and would break the
    /// 3-plus-6 split above.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoNonPortsAreAbsentAndNoWrapperWasInvented()
    {
        Type[] providers =
        [
            typeof(EncodingProvider),
            typeof(HashProvider),
            typeof(HmacProvider),
            typeof(SymmetricCipherProvider),
            typeof(RsaProvider),
        ];

        foreach (Type provider in providers)
        {
            Assert.Empty(DeclaredOverloads(provider, "Copyright"));
            Assert.Empty(DeclaredOverloads(provider, "GetVersion"));
        }

        Assert.Empty(DeclaredOverloads(typeof(HashProvider), "Sha384"));
        Assert.Empty(DeclaredOverloads(typeof(HashProvider), "Sha512"));
    }

    /// <summary>
    /// No provider collapses two legacy declarations into one member with an optional parameter.
    /// </summary>
    /// <remarks>
    /// This is why the census can be trusted. An optional parameter would let a single C# member
    /// stand in for two legacy declarations, so the counts above would still reconcile while the
    /// surface had silently changed shape - and a caller compiled against the collapsed form would
    /// bind differently from one compiled against the legacy pair. Every overload is therefore
    /// required to declare every one of its parameters.
    /// </remarks>
    [Fact]
    public void NoDeclarationIsCollapsedByAnOptionalParameter()
    {
        Type[] providers =
        [
            typeof(EncodingProvider),
            typeof(HashProvider),
            typeof(HmacProvider),
            typeof(SymmetricCipherProvider),
            typeof(RsaProvider),
        ];

        foreach (Type provider in providers)
        {
            foreach (MethodInfo method in DeclaredSurface(provider))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.False(
                        parameter.IsOptional,
                        $"{provider.Name}.{method.Name} declares an optional parameter " +
                        $"'{parameter.Name}', which would collapse two legacy declarations into one.");
                }
            }
        }
    }

    // ==========================================================================================
    //  REGION 2 - ONE HASH-TYPE SET SERVING HASH, RSASIGN AND VERIFYRSASIGN
    //  ORACLE  ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933
    //  ------------------------------------------------------------------------------------------
    //  The source comment at L927 reads `//Hash types (n_crypto::Hash/RSASign/VerifyRSASign:
    //  [ntype])`. That is a single sentence with a large consequence: the identifier set is ONE set
    //  shared by three different capabilities, so a port that gave the signing surface its own set
    //  would contradict the source. Within that one set there are two DOCUMENTED CAPABILITY LIMITS,
    //  both concerning the checksum arm, and they are asserted here as correct behaviour.
    // ==========================================================================================

    /// <summary>
    /// All six identifiers are accepted by the unkeyed digest surface, and the catalogue agrees.
    /// </summary>
    /// <param name="ntype">The published identifier under test.</param>
    /// <remarks>
    /// The unkeyed surface is the only one of the three with no limit inside the set: all six
    /// identifiers, checksum included, produce a digest. The catalogue predicate is consulted rather
    /// than a locally restated set, so this file cannot disagree with the catalogue about what is
    /// published.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllHashTypeRows))]
    public void TheUnkeyedSurfaceAcceptsEveryPublishedIdentifier(long ntype)
    {
        Assert.True(LegacyDefaults.IsSupportedHashType(ntype));

        string digest = _hash.Hash("parity", ntype);

        Assert.NotEmpty(digest);
    }

    /// <summary>
    /// The keyed surface accepts the five identifiers with a keyed form and REFUSES the checksum, and
    /// the signing surface reaches the same conclusion by its own route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BOTH REFUSALS ARE CORRECT BEHAVIOUR under constraint C-B and neither is a gap to be closed.
    /// There is no HMAC-CRC32 construction and no RSA-with-CRC32 signature scheme: HMAC's security
    /// argument and a signature's collision resistance both rest on properties a linear checksum
    /// does not have, and fabricating either would authenticate nothing while looking as though it
    /// did. The keyed surface's own decision record is explicit that falling back to an unkeyed
    /// checksum would be the one outcome worse than an error, because it would discard the caller's
    /// key silently.
    /// </para>
    /// <para>
    /// THE TWO EXCEPTION TYPES DIFFER, AND THAT ASYMMETRY IS PINNED RATHER THAN SMOOTHED. The keyed
    /// digest surface reports an argument-range failure, naming the offending identifier; the signing
    /// surface reports a not-supported failure. Both are defensible readings of the same limit - one
    /// treats it as a bad value for this call, the other as a capability the scheme does not have -
    /// and this test records which provider chose which so that a future change to either becomes a
    /// build-visible difference instead of a silent one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheChecksumIdentifierIsPublishedButHasNoKeyedAndNoSigningForm()
    {
        // It IS a published identifier - the limit is on its keyed and signing use, not on the value.
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_CRC32));
        Assert.NotEmpty(_hash.Hash("parity", Enums.CRYPTO_HASH_CRC32));

        ArgumentOutOfRangeException keyed = Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash("parity", PublishedHmacKeyText, Enums.CRYPTO_HASH_CRC32));

        Assert.Equal("ntype", keyed.ParamName);

        Assert.Throws<NotSupportedException>(
            () => _rsa.RSASign("parity", SharedRsaKeys.DerPrivateKey, Enums.CRYPTO_HASH_CRC32));
    }

    /// <summary>
    /// All four keyed overloads refuse the checksum identically, so the limit is a property of the
    /// surface rather than of one overload.
    /// </summary>
    /// <remarks>
    /// Driven across the whole two-by-two cross product [n_crypto.sru:L23-L26] and both file members
    /// [:L28-L29] because a limit enforced in five of six places is a defect that a per-overload test
    /// would not find.
    /// </remarks>
    [Fact]
    public void EveryKeyedMemberRefusesTheChecksumIdentifier()
    {
        byte[] data = LegacyDefaults.KeyMaterialEncoding.GetBytes("parity");
        byte[] key = SyntheticBytes(16, offset: 5);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash("parity", PublishedHmacKeyText, Enums.CRYPTO_HASH_CRC32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash("parity", key, Enums.CRYPTO_HASH_CRC32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash(data, PublishedHmacKeyText, Enums.CRYPTO_HASH_CRC32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash(data, key, Enums.CRYPTO_HASH_CRC32));

        using TemporaryPayloadFile file = new(data);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.HashFile(file.Path, PublishedHmacKeyText, Enums.CRYPTO_HASH_CRC32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.HashFile(file.Path, key, Enums.CRYPTO_HASH_CRC32));
    }

    /// <summary>
    /// An identifier outside the published set is refused by all three surfaces with the same
    /// exception, so the screen is a closed set and not a range test.
    /// </summary>
    /// <param name="ntype">An identifier the legacy does not publish.</param>
    /// <remarks>
    /// The rows deliberately include the value immediately above the set's last member and the two
    /// extremes of the width, which is what catches a screen written as an unsigned comparison or as
    /// a cast rather than as membership. That all three providers agree is the point: it is the
    /// enums.sru:L927 claim - one set, three capabilities - made executable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnpublishedHashTypeRows))]
    public void AnUnpublishedIdentifierIsRefusedByAllThreeSurfaces(long ntype)
    {
        Assert.False(LegacyDefaults.IsSupportedHashType(ntype));

        Assert.Throws<ArgumentOutOfRangeException>(() => _hash.Hash("parity", ntype));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.Hash("parity", PublishedHmacKeyText, ntype));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSASign("parity", SharedRsaKeys.DerPrivateKey, ntype));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.VerifyRSASign(
                "parity",
                _encoding.Base64Encode(SyntheticBytes(8, offset: 1)),
                SharedRsaKeys.DerPublicKey,
                ntype));
    }

    // ==========================================================================================
    //  THE DECISION D4 PAYLOAD HELPERS
    //  ------------------------------------------------------------------------------------------
    //  Both route through the SHARED EncodingProvider instance and through the catalogue's own
    //  payload-encoding member, never through a locally chosen encoder. That is what makes the
    //  assertions below able to prove "these four providers use ONE rendering" rather than "these
    //  four providers agree with a rendering this test file invented".
    // ==========================================================================================

    /// <summary>
    /// Decodes a string-shaped payload back to bytes using the catalogue's payload encoding.
    /// </summary>
    /// <param name="payload">A payload produced by any string-shaped member.</param>
    /// <returns>The raw bytes the payload carries.</returns>
    private byte[] DecodePayload(string payload) =>
        _encoding.StringToBlob(payload, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    /// <summary>
    /// Renders bytes into the string-shaped payload form using the catalogue's payload encoding.
    /// </summary>
    /// <param name="bytes">The bytes to render.</param>
    /// <returns>The payload a string-shaped member is required to produce for those bytes.</returns>
    private string EncodePayload(byte[] bytes) =>
        _encoding.BlobToString(bytes, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    // ==========================================================================================
    //  REGION 3 - THE UNKEYED DIGEST MATRIX
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21-L22, :L27
    //          ws_objects/pfw.crypto.pbl.src/md5.srf, sha1.srf, sha256.srf
    //  ------------------------------------------------------------------------------------------
    //  Six algorithms over two data shapes, against INDEPENDENTLY PUBLISHED known-answer vectors.
    //  The vectors are written in canonical uppercase hexadecimal - the spelling the standards
    //  themselves use - and the DECISION D4 Base64 form is derived from them inside each assertion.
    //  That gives two properties at once: the digest BYTES are checked against an external
    //  authority, and the payload ENCODING is checked against the shared encoding provider, without
    //  a single base64-shaped literal appearing anywhere in this file.
    // ==========================================================================================

    /// <summary>
    /// Every published digest is reproduced, through both data overloads, in the shared payload
    /// encoding.
    /// </summary>
    /// <param name="ntype">The published algorithm identifier.</param>
    /// <param name="algorithmName">The algorithm's name, for the failure message.</param>
    /// <param name="input">The published input text.</param>
    /// <param name="expectedHex">The published digest, canonical uppercase hexadecimal.</param>
    /// <remarks>
    /// <para>
    /// FOUR DISTINCT CLAIMS, in the order they are asserted. First, the two data overloads
    /// [n_crypto.sru:L21 for text, :L22 for bytes] agree - which holds for an ASCII input under any
    /// ASCII-compatible text encoding, so this row does NOT silently depend on DECISION D1. Second,
    /// the digest bytes equal the published vector, which is the only claim here backed by an
    /// authority outside this repository. Third, the string form is exactly the Base64 rendering of
    /// those bytes through the shared encoding provider - DECISION D4. Fourth, it is NOT the
    /// hexadecimal rendering, which is the assertion that makes the Base64-over-hexadecimal choice
    /// explicit instead of incidental.
    /// </para>
    /// <para>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K. Claims one and two are derivable. CLAIMS THREE AND
    /// FOUR ARE NOT MEASURED LEGACY BEHAVIOUR: the closed binary's text spelling of a digest is
    /// unobservable from the repository, and lowercase hexadecimal is the more common convention for
    /// MD5 and the SHA family specifically, so DECISION D4 is the most plausible place the port could
    /// turn out to be wrong for this group. It is asserted here as THE DOCUMENTED DECISION so that a
    /// unilateral change becomes a build failure, and so that an oracle capture has exactly one place
    /// to re-baseline.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedDigestVectors))]
    public void EveryPublishedDigestIsReproducedThroughBothDataOverloads(
        long ntype,
        string algorithmName,
        string input,
        string expectedHex)
    {
        byte[] inputBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(input);
        byte[] publishedDigest = Convert.FromHexString(expectedHex);

        string fromText = _hash.Hash(input, ntype);
        string fromBytes = _hash.Hash(inputBytes, ntype);

        Assert.Equal(fromBytes, fromText);

        Assert.Equal(
            expectedHex,
            Convert.ToHexString(DecodePayload(fromBytes)));

        Assert.Equal(_encoding.Base64Encode(publishedDigest), fromBytes);

        Assert.NotEqual(_encoding.HexEncode(publishedDigest), fromBytes);

        Assert.False(
            string.IsNullOrEmpty(algorithmName),
            "Every vector row names its algorithm so a failure identifies which arm broke.");
    }

    /// <summary>
    /// The empty input is a valid input for every algorithm and yields its published digest of zero
    /// bytes.
    /// </summary>
    /// <param name="ntype">The published algorithm identifier.</param>
    /// <param name="algorithmName">The algorithm's name, for the failure message.</param>
    /// <param name="expectedHex">The published digest of zero bytes.</param>
    /// <remarks>
    /// Not a degenerate case. Every algorithm has a defined, fixed digest of zero bytes, so an empty
    /// argument must be ACCEPTED rather than rejected as missing - and the checksum arm's expected
    /// value is the set's only all-zero result, which is precisely the value a wrong initial register
    /// or a missing final complement in DECISION H2's implementation would perturb. Both the empty
    /// string and the empty array are driven, because they reach the two different overloads.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedEmptyInputDigests))]
    public void TheEmptyInputYieldsThePublishedDigestOfZeroBytes(
        long ntype,
        string algorithmName,
        string expectedHex)
    {
        string fromEmptyText = _hash.Hash(string.Empty, ntype);
        string fromEmptyBytes = _hash.Hash([], ntype);

        Assert.Equal(fromEmptyBytes, fromEmptyText);
        Assert.Equal(expectedHex, Convert.ToHexString(DecodePayload(fromEmptyBytes)));

        Assert.False(
            string.IsNullOrEmpty(algorithmName),
            "Every vector row names its algorithm so a failure identifies which arm broke.");
    }

    /// <summary>
    /// Each algorithm's digest is the published width, including the checksum's four bytes.
    /// </summary>
    /// <remarks>
    /// Widths are published algorithm facts, so asserting them is derivation and not guesswork. The
    /// checksum row is the one worth stating out loud: a CRC-32 is FOUR bytes, an order of magnitude
    /// narrower than the SHA-2 digests it sits beside in the same identifier set, which is a
    /// standing reminder that the sixth arm is a checksum and not a cryptographic hash.
    /// </remarks>
    [Fact]
    public void EachAlgorithmProducesItsPublishedDigestWidth()
    {
        Assert.Equal(16, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_MD5)).Length);
        Assert.Equal(20, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_SHA1)).Length);
        Assert.Equal(32, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_SHA256)).Length);
        Assert.Equal(48, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_SHA384)).Length);
        Assert.Equal(64, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_SHA512)).Length);
        Assert.Equal(4, DecodePayload(_hash.Hash("parity", Enums.CRYPTO_HASH_CRC32)).Length);
    }

    /// <summary>
    /// The checksum arm reproduces the CRC catalogue's check value, which is the one assertion that
    /// can falsify DECISION H2's variant choice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check value is defined as the output over the nine ASCII digits one through nine, and it
    /// differs between CRC-32 variants - which is exactly why the catalogue publishes it. Reproducing
    /// it establishes the reflected IEEE 802.3 and PKZIP form: reflected polynomial
    /// <c>0xEDB88320</c>, an initial register of all ones, a final complement, and the result laid
    /// out MOST SIGNIFICANT BYTE FIRST so that the encoded payload matches the conventional written
    /// spelling of a CRC-32.
    /// </para>
    /// <para>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K. That variant is DECISION H2, a reasoned inference from
    /// the repository's own zlib attribution rather than a measurement of the closed binary: the base
    /// class library ships no CRC-32, so the port implements one, and which variant the DLL used is
    /// not observable from any file in this repository. The byte ORDER in particular is a convention
    /// choice with no in-repository evidence at all. If an oracle capture disagrees, this test and
    /// the checksum rows of the two vector tables are the places it surfaces.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheChecksumArmReproducesTheCrcCatalogueCheckValue()
    {
        string digest = _hash.Hash(Crc32CheckValueInput, Enums.CRYPTO_HASH_CRC32);

        Assert.Equal("CBF43926", Convert.ToHexString(DecodePayload(digest)));
    }

    /// <summary>
    /// Each of the six wrapper members is exactly its identifier-driven call, never a second
    /// implementation.
    /// </summary>
    /// <remarks>
    /// Each legacy wrapper body is one delegating line - <c>md5.srf:L12</c> returns
    /// <c>n_crypto.Hash(data, Enums.CRYPTO_HASH_MD5)</c> and its five siblings are the same shape - so
    /// the wrappers must be spellings of the dispatcher and not parallel implementations that could
    /// drift from it. Both overloads of all three wrappers are driven, which is the whole
    /// <c>md5.srf:L7-L8</c> / <c>sha1.srf:L7-L8</c> / <c>sha256.srf:L7-L8</c> surface.
    /// </remarks>
    [Fact]
    public void EveryUnkeyedWrapperIsExactlyItsIdentifierDrivenCall()
    {
        const string text = "wrapper parity";
        byte[] bytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(text);

        Assert.Equal(_hash.Hash(text, Enums.CRYPTO_HASH_MD5), _hash.Md5(text));
        Assert.Equal(_hash.Hash(bytes, Enums.CRYPTO_HASH_MD5), _hash.Md5(bytes));

        Assert.Equal(_hash.Hash(text, Enums.CRYPTO_HASH_SHA1), _hash.Sha1(text));
        Assert.Equal(_hash.Hash(bytes, Enums.CRYPTO_HASH_SHA1), _hash.Sha1(bytes));

        Assert.Equal(_hash.Hash(text, Enums.CRYPTO_HASH_SHA256), _hash.Sha256(text));
        Assert.Equal(_hash.Hash(bytes, Enums.CRYPTO_HASH_SHA256), _hash.Sha256(bytes));
    }

    /// <summary>
    /// Every algorithm arm is distinct, so no two identifiers are silently wired to one algorithm.
    /// </summary>
    /// <remarks>
    /// A dispatcher with a copy-paste error - two arms selecting the same algorithm - would still
    /// pass a per-algorithm vector test for the arm that happened to be correct. Comparing all six
    /// outputs over one input catches that class of defect directly.
    /// </remarks>
    [Fact]
    public void EveryAlgorithmArmProducesADistinctDigest()
    {
        HashSet<string> digests = [];

        foreach (long ntype in AllHashTypes)
        {
            Assert.True(
                digests.Add(_hash.Hash("distinctness", ntype)),
                $"Identifier {ntype} produced a digest another identifier already produced.");
        }

        Assert.Equal(AllHashTypes.Length, digests.Count);
    }

    // ==========================================================================================
    //  REGION 4 - THE KEYED AUTHENTICATOR MATRIX
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26, :L28-L29
    //  ------------------------------------------------------------------------------------------
    //  The legacy declares the keyed form as the FULL two-by-two cross product of a {string, blob}
    //  data axis with a {string, blob} key axis. All four cells are driven over IDENTICAL published
    //  material, so they are required both to agree with each other and to agree with an external
    //  authority - which is strictly stronger than four independent round trips.
    // ==========================================================================================

    /// <summary>
    /// Every published authenticator is reproduced identically by all four keyed overloads.
    /// </summary>
    /// <param name="ntype">The keyed algorithm identifier.</param>
    /// <param name="algorithmName">The construction's name, for the failure message.</param>
    /// <param name="expectedHex">The published authenticator, canonical uppercase hexadecimal.</param>
    /// <remarks>
    /// <para>
    /// The key and the message are the published test-case text, whose byte and text spellings are
    /// identical because both are printable ASCII. The four cells therefore see the SAME bytes on
    /// both axes, and any disagreement between them is a real defect rather than an encoding
    /// artefact.
    /// </para>
    /// <para>
    /// The last assertion is the cross-provider claim: the keyed surface renders its result through
    /// the SAME shared encoding provider as the unkeyed surface, so DECISION D4 is consumed once by
    /// two providers rather than decided twice. Two independently-decided renderings would let the
    /// same caller get one spelling from the keyed path and a different spelling from the unkeyed
    /// path over the same bytes.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedAuthenticatorVectors))]
    public void EveryPublishedAuthenticatorIsReproducedByAllFourKeyedOverloads(
        long ntype,
        string algorithmName,
        string expectedHex)
    {
        byte[] dataBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(PublishedHmacDataText);
        byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(PublishedHmacKeyText);
        byte[] published = Convert.FromHexString(expectedHex);

        // n_crypto.sru:L23 text data, text key.
        string textDataTextKey = _hmac.Hash(PublishedHmacDataText, PublishedHmacKeyText, ntype);

        // n_crypto.sru:L24 text data, blob key.
        string textDataBlobKey = _hmac.Hash(PublishedHmacDataText, keyBytes, ntype);

        // n_crypto.sru:L25 blob data, text key.
        string blobDataTextKey = _hmac.Hash(dataBytes, PublishedHmacKeyText, ntype);

        // n_crypto.sru:L26 blob data, blob key.
        string blobDataBlobKey = _hmac.Hash(dataBytes, keyBytes, ntype);

        Assert.Equal(textDataTextKey, textDataBlobKey);
        Assert.Equal(textDataTextKey, blobDataTextKey);
        Assert.Equal(textDataTextKey, blobDataBlobKey);

        Assert.Equal(expectedHex, Convert.ToHexString(DecodePayload(textDataTextKey)));

        Assert.Equal(_encoding.Base64Encode(published), textDataTextKey);

        Assert.False(
            string.IsNullOrEmpty(algorithmName),
            "Every vector row names its construction so a failure identifies which arm broke.");
    }

    /// <summary>
    /// The keyed and unkeyed surfaces render their results through one encoding, and a keyed result
    /// is never equal to the unkeyed result over the same data.
    /// </summary>
    /// <param name="ntype">The keyed algorithm identifier.</param>
    /// <remarks>
    /// <para>
    /// The first claim is the DECISION D4 agreement stated as a property rather than per vector: for
    /// every keyed algorithm, the keyed payload decodes and re-encodes to itself through the same
    /// provider that renders the unkeyed payload.
    /// </para>
    /// <para>
    /// The second claim guards the failure mode the keyed surface's decision record calls the one
    /// outcome worse than an error: a keyed call that silently discarded the key would produce
    /// exactly the unkeyed digest, and a caller would believe it held an authenticator when it held a
    /// checksum anyone can recompute.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedHashTypeRows))]
    public void AKeyedResultSharesTheRenderingButNeverTheValueOfTheUnkeyedResult(long ntype)
    {
        const string data = "shared rendering";

        string keyed = _hmac.Hash(data, PublishedHmacKeyText, ntype);
        string unkeyed = _hash.Hash(data, ntype);

        Assert.Equal(keyed, EncodePayload(DecodePayload(keyed)));
        Assert.Equal(unkeyed, EncodePayload(DecodePayload(unkeyed)));

        Assert.NotEqual(unkeyed, keyed);
    }

    // ==========================================================================================
    //  REGION 5 - THE FILE-SHAPED MEMBERS
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L27, :L28-L29
    //  ------------------------------------------------------------------------------------------
    //  CONSTRAINT C-C GOVERNS THIS REGION ABSOLUTELY. The legacy's own call sites point the file
    //  members at shipped native binaries inside the read-only tree; pointing a test there would
    //  make this suite read the oracle at run time. Every test below therefore writes its OWN file
    //  and deletes it, including on failure.
    // ==========================================================================================

    /// <summary>
    /// The unkeyed file member agrees with the in-memory member over the same bytes, for every
    /// algorithm.
    /// </summary>
    /// <param name="ntype">The published algorithm identifier.</param>
    /// <remarks>
    /// The file member consumes its input incrementally in bounded-memory chunks while the in-memory
    /// member digests a single buffer, so the two travel different code paths to the same answer.
    /// Requiring them to agree is what proves the incremental path - including DECISION H2's
    /// incremental checksum - is a faithful streaming of the same computation.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllHashTypeRows))]
    public void TheUnkeyedFileMemberAgreesWithTheInMemoryMember(long ntype)
    {
        byte[] content = SyntheticBytes(4096, offset: 7);

        using TemporaryPayloadFile file = new(content);

        Assert.Equal(_hash.Hash(content, ntype), _hash.HashFile(file.Path, ntype));
    }

    /// <summary>
    /// Both keyed file members agree with their in-memory counterparts, for every keyed algorithm and
    /// both key spellings.
    /// </summary>
    /// <param name="ntype">The keyed algorithm identifier.</param>
    /// <remarks>
    /// Drives <c>HashFile</c> with a string key [n_crypto.sru:L28] and with a blob key [:L29] over
    /// identical material, so the two file members are required to agree with each other as well as
    /// with the in-memory cross product of region 4.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedHashTypeRows))]
    public void BothKeyedFileMembersAgreeWithTheirInMemoryCounterparts(long ntype)
    {
        byte[] content = SyntheticBytes(4096, offset: 13);
        byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(PublishedHmacKeyText);

        using TemporaryPayloadFile file = new(content);

        string fromTextKey = _hmac.HashFile(file.Path, PublishedHmacKeyText, ntype);
        string fromBlobKey = _hmac.HashFile(file.Path, keyBytes, ntype);

        Assert.Equal(_hmac.Hash(content, PublishedHmacKeyText, ntype), fromTextKey);
        Assert.Equal(_hmac.Hash(content, keyBytes, ntype), fromBlobKey);
        Assert.Equal(fromTextKey, fromBlobKey);
    }

    /// <summary>
    /// A zero-length file is valid and yields the digest of zero bytes.
    /// </summary>
    /// <remarks>
    /// The empty-file case reaches the incremental path with no iterations at all, which is where a
    /// streaming implementation that finalized inside its read loop rather than after it would
    /// return the wrong answer or none.
    /// </remarks>
    [Fact]
    public void AZeroLengthFileYieldsTheDigestOfZeroBytes()
    {
        using TemporaryPayloadFile file = new([]);

        foreach (long ntype in AllHashTypes)
        {
            Assert.Equal(_hash.Hash([], ntype), _hash.HashFile(file.Path, ntype));
        }
    }

    /// <summary>
    /// The algorithm identifier is screened BEFORE the filesystem is touched.
    /// </summary>
    /// <remarks>
    /// ORDERING IS THE ASSERTION, not the exception type. A caller-supplied path reaching a hash
    /// member is an arbitrary-path primitive that an in-process library could never have been, so an
    /// unsupported identifier must not become a way to distinguish a path that exists from one that
    /// does not. The argument failure arriving instead of a file-not-found failure is what proves the
    /// screen runs first.
    /// </remarks>
    [Fact]
    public void TheIdentifierIsScreenedBeforeTheFilesystemIsTouched()
    {
        string absentPath = Path.Combine(
            Path.GetTempPath(),
            $"pfw-crypto-parity-absent-{Guid.NewGuid():N}.bin");

        Assert.Throws<ArgumentOutOfRangeException>(() => _hash.HashFile(absentPath, ntype: 6L));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _hmac.HashFile(absentPath, PublishedHmacKeyText, ntype: 6L));
    }

    /// <summary>
    /// A missing file surfaces the framework's own file failure rather than a fabricated sentinel.
    /// </summary>
    /// <remarks>
    /// DECISION H4, annotated as such: what the closed binary returned for an unreadable file is
    /// unobservable, so the port propagates the platform exception unchanged instead of inventing a
    /// return value the legacy may or may not have used. Preserved deliberately because a fabricated
    /// sentinel would be indistinguishable from a legitimate digest for some inputs.
    /// </remarks>
    [Fact]
    public void AMissingFileSurfacesTheFrameworkFileFailure()
    {
        string absentPath = Path.Combine(
            Path.GetTempPath(),
            $"pfw-crypto-parity-absent-{Guid.NewGuid():N}.bin");

        Assert.Throws<FileNotFoundException>(
            () => _hash.HashFile(absentPath, Enums.CRYPTO_HASH_SHA256));
        Assert.Throws<FileNotFoundException>(
            () => _hmac.HashFile(absentPath, PublishedHmacKeyText, Enums.CRYPTO_HASH_SHA256));
    }

    // ==========================================================================================
    //  REGION 6 - THE SYMMETRIC MATRIX: 5 TYPES x 3 MODES x 16 ARGUMENT SHAPES
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L61
    //          ws_objects/pfw.shared.pbl.src/enums.sru:L936-L945
    //  ------------------------------------------------------------------------------------------
    //  WHY ROUND-TRIPPING IS THE ONLY AVAILABLE VALUE ASSERTION HERE. There is no oracle capture and
    //  no published vector for the legacy's own key normalization, its payload text encoding or its
    //  synthesized vector, so an expected cipher text would have to be INVENTED - and an invented
    //  cipher text asserts only that the implementation has not changed since the day the test was
    //  written, while looking like parity. This region therefore asserts round trips, agreements
    //  between shapes, and the structural rules that reflection can prove, and it fabricates nothing.
    // ==========================================================================================

    /// <summary>
    /// Selects the one argument shape matching a combination of the four axes.
    /// </summary>
    /// <param name="binaryPayload">Whether the payload axis is a blob.</param>
    /// <param name="binaryKey">Whether the key axis is a blob.</param>
    /// <param name="suppliesIv">Whether the shape declares an initialization vector.</param>
    /// <param name="suppliesMode">Whether the shape declares a cipher mode.</param>
    /// <returns>The single matching shape.</returns>
    /// <remarks>
    /// Named lookup rather than a positional index, so that a test reads as the combination it means
    /// and cannot silently point at the wrong declaration if the table is ever reordered.
    /// <see cref="Enumerable.Single{TSource}(IEnumerable{TSource}, Func{TSource, bool})"/> also fails
    /// loudly if the table ever stops being a complete, duplicate-free product of the axes.
    /// </remarks>
    private static CipherShape Shape(
        bool binaryPayload,
        bool binaryKey,
        bool suppliesIv,
        bool suppliesMode) =>
        AllCipherShapes.Single(shape =>
            shape.BinaryPayload == binaryPayload &&
            shape.BinaryKey == binaryKey &&
            shape.SuppliesIv == suppliesIv &&
            shape.SuppliesMode == suppliesMode);

    /// <summary>
    /// The shape table is a complete, duplicate-free enumeration of exactly the thirty-two declared
    /// overloads.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ANTI-COLLAPSE GUARD, and it is the reason the 240-row matrix below can be trusted.
    /// A parity matrix that silently lost rows still goes green, so the table that generates it is
    /// pinned first: sixteen entries, the encrypt locators covering exactly lines 30 through 45, the
    /// decrypt locators covering exactly lines 46 through 61, and each of the sixteen axis
    /// combinations present exactly once.
    /// </remarks>
    [Fact]
    public void TheShapeTableCoversExactlyTheThirtyTwoDeclaredOverloads()
    {
        Assert.Equal(16, AllCipherShapes.Length);

        Assert.Equal(
            Enumerable.Range(30, 16),
            AllCipherShapes.Select(shape => shape.EncryptLine).Order());

        Assert.Equal(
            Enumerable.Range(46, 16),
            AllCipherShapes.Select(shape => shape.DecryptLine).Order());

        // Every axis combination present exactly once: Shape throws if a combination is missing or
        // duplicated, so calling it for all sixteen combinations proves the product is complete.
        foreach (bool binaryPayload in (bool[])[false, true])
        {
            foreach (bool binaryKey in (bool[])[false, true])
            {
                foreach (bool suppliesIv in (bool[])[false, true])
                {
                    foreach (bool suppliesMode in (bool[])[false, true])
                    {
                        Assert.NotNull(Shape(binaryPayload, binaryKey, suppliesIv, suppliesMode));
                    }
                }
            }
        }

        // The eight mode-without-vector arms of DECISION D3 - four encrypt and four decrypt - are
        // exactly the shapes that declare a mode and no vector.
        Assert.Equal(
            4,
            AllCipherShapes.Count(shape => shape.SuppliesMode && !shape.SuppliesIv));
    }

    /// <summary>
    /// The parity matrix has the row count the declaration list implies, so it cannot have silently
    /// collapsed.
    /// </summary>
    /// <remarks>
    /// Sixteen shapes by five published cipher types by three published modes is 240 rows, and each
    /// row exercises one encrypt declaration and its mirrored decrypt declaration - so the matrix
    /// reaches all thirty-two declarations on every one of the fifteen grid cells. Asserting the
    /// count here means a future edit that narrows the data source fails a test rather than quietly
    /// reducing coverage.
    /// </remarks>
    [Fact]
    public void TheParityMatrixHasTheRowCountTheDeclarationListImplies()
    {
        Assert.Equal(240, ShapeByTypeAndModeMatrix().Count);
        Assert.Equal(15, TypeAndModeGrid().Count);
        Assert.Equal(5, CipherTypeRows().Count);
        Assert.Equal(6, PublishedDigestVectors().Count);
        Assert.Equal(6, PublishedEmptyInputDigests().Count);
        Assert.Equal(5, PublishedAuthenticatorVectors().Count);
        Assert.Equal(2, EncodingRows().Count);
        Assert.Equal(2, RsaPaddingRows().Count);
    }

    /// <summary>
    /// Every one of the thirty-two declarations round trips on every cell of the type-by-mode grid.
    /// </summary>
    /// <param name="shapeIndex">The index into the shape table of the pair under test.</param>
    /// <param name="ntype">The published cipher type.</param>
    /// <param name="mode">The published cipher mode.</param>
    /// <remarks>
    /// <para>
    /// Each row encrypts through one declaration and decrypts through its mirror, which is the pairing
    /// the legacy's two blocks are written to be used in. A pair is always self-consistent: the four
    /// mode-omitting pairs run in the catalogue default at both ends - correct behaviour, not a
    /// defect - and the twelve remaining pairs run in the requested mode at both ends.
    /// </para>
    /// <para>
    /// The payload is deliberately not block-aligned for either published block length, so PKCS#7
    /// padding is exercised on all 240 rows rather than accidentally bypassed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ShapeByTypeAndModeMatrix))]
    public void EveryDeclarationRoundTripsOnEveryCellOfTheGrid(int shapeIndex, ushort ntype, long mode)
    {
        CipherShape shape = AllCipherShapes[shapeIndex];
        CipherMaterial material = CreateCipherMaterial(ntype);

        if (shape.BinaryPayload)
        {
            byte[] plain = LegacyDefaults.KeyMaterialEncoding.GetBytes(CipherPayloadText);
            byte[] cipher = EncryptThroughShape(shape, material, plain, mode);

            Assert.NotEqual(plain, cipher);
            Assert.Equal(plain, DecryptThroughShape(shape, material, cipher, mode));
        }
        else
        {
            string cipher = EncryptThroughShape(shape, material, CipherPayloadText, mode);

            Assert.NotEqual(CipherPayloadText, cipher);
            Assert.Equal(
                CipherPayloadText,
                DecryptThroughShape(shape, material, cipher, mode));
        }
    }

    /// <summary>
    /// The string-shaped family's cipher text is the payload rendering of the blob-shaped family's
    /// cipher bytes, on every cell of the grid and for both key spellings.
    /// </summary>
    /// <param name="ntype">The published cipher type.</param>
    /// <param name="mode">The published cipher mode.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE CIPHER SURFACE'S HALF OF THE DECISION D4 AGREEMENT. The two families are not two
    /// algorithms: they are one operation with two payload spellings, so the string form must be the
    /// shared encoding provider's rendering of the byte form. If they ever diverge, a caller could not
    /// encrypt through one family and decrypt through the other over a transport that carries text.
    /// </para>
    /// <para>
    /// The fully-specified shapes are used - vector and mode both supplied [n_crypto.sru:L33 and :L41
    /// for the string-key spelling, :L37 and :L45 for the blob-key spelling] - so nothing about this
    /// assertion depends on DECISION D3's synthesized vector.
    /// </para>
    /// <para>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K: that the rendering is Base64 rather than hexadecimal is
    /// DECISION D4 and is not measured legacy behaviour. What IS asserted without qualification is
    /// that the two families share ONE rendering, whichever it turns out to be.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TypeAndModeGrid))]
    public void TheStringFamilyCipherTextIsThePayloadRenderingOfTheBlobFamilyCipherBytes(
        ushort ntype,
        long mode)
    {
        CipherMaterial material = CreateCipherMaterial(ntype);
        string plainText = CipherPayloadText;
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(plainText);

        foreach (bool binaryKey in (bool[])[false, true])
        {
            CipherShape textShape = Shape(
                binaryPayload: false,
                binaryKey,
                suppliesIv: true,
                suppliesMode: true);

            CipherShape blobShape = Shape(
                binaryPayload: true,
                binaryKey,
                suppliesIv: true,
                suppliesMode: true);

            string textCipher = EncryptThroughShape(textShape, material, plainText, mode);
            byte[] blobCipher = EncryptThroughShape(blobShape, material, plainBytes, mode);

            Assert.Equal(EncodePayload(blobCipher), textCipher);
            Assert.Equal(blobCipher, DecodePayload(textCipher));
        }
    }

    /// <summary>
    /// The text-key and blob-key spellings of identical material produce identical cipher text, on
    /// every cell of the grid and in both payload families.
    /// </summary>
    /// <param name="ntype">The published cipher type.</param>
    /// <param name="mode">The published cipher mode.</param>
    /// <remarks>
    /// <para>
    /// The blob key and vector parameters were a later addition to the legacy family, so the two key
    /// spellings are alternative ways of naming the same bytes and not two different key concepts.
    /// The material this file builds is byte-identical across the two spellings by construction, so
    /// the two must agree - and the assertion is meaningful precisely because DECISION D1 uses a
    /// passphrase AS RAW KEY BYTES with no derivation step that could differ between the paths.
    /// </para>
    /// <para>
    /// This also exercises the pairing the legacy declares: a text key travels with a text vector
    /// [:L33, :L41] and a blob key with a blob vector [:L37, :L45], never mixed.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TypeAndModeGrid))]
    public void TheTwoKeySpellingsOfIdenticalMaterialAgree(ushort ntype, long mode)
    {
        CipherMaterial material = CreateCipherMaterial(ntype);
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(CipherPayloadText);

        string textKeyCipher = EncryptThroughShape(
            Shape(binaryPayload: false, binaryKey: false, suppliesIv: true, suppliesMode: true),
            material,
            CipherPayloadText,
            mode);

        string blobKeyCipher = EncryptThroughShape(
            Shape(binaryPayload: false, binaryKey: true, suppliesIv: true, suppliesMode: true),
            material,
            CipherPayloadText,
            mode);

        Assert.Equal(textKeyCipher, blobKeyCipher);

        byte[] textKeyBytes = EncryptThroughShape(
            Shape(binaryPayload: true, binaryKey: false, suppliesIv: true, suppliesMode: true),
            material,
            plainBytes,
            mode);

        byte[] blobKeyBytes = EncryptThroughShape(
            Shape(binaryPayload: true, binaryKey: true, suppliesIv: true, suppliesMode: true),
            material,
            plainBytes,
            mode);

        Assert.Equal(textKeyBytes, blobKeyBytes);
    }

    /// <summary>
    /// The eight mode-without-vector declarations behave exactly as the fully-specified declaration
    /// does with an all-zero vector, which is DECISION D3.
    /// </summary>
    /// <param name="ntype">The published cipher type.</param>
    /// <remarks>
    /// <para>
    /// The eight arms at [n_crypto.sru:L31, :L35, :L39, :L43] and their mirrors [:L47, :L51, :L55,
    /// :L59] accept a mode but declare no vector parameter, so a caller can ask for CBC or CFB - both
    /// of which require a vector - with no way to supply one. DECISION D3 resolves that with an
    /// ALL-ZERO VECTOR OF THE CIPHER'S BLOCK LENGTH, and this test is the assertion that makes the
    /// resolution observable: the no-vector arm is required to equal the vector-taking arm given a
    /// vector of zero bytes, in both non-default modes and both payload families.
    /// </para>
    /// <para>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K. D3 is a REASONED CHOICE, not a measurement. It is also
    /// a genuine weakness that is preserved rather than corrected: a fixed, publicly known vector
    /// destroys CBC's semantic security, so identical plaintexts under one key produce identical
    /// cipher text and an observer learns when a value has not changed. Correcting it by generating a
    /// random vector would produce cipher text the legacy could not decrypt, there being no parameter
    /// in which to transmit one - so the weakness is asserted here as CORRECT behaviour under
    /// constraint C-B.
    /// </para>
    /// <para>
    /// The vector itself is COMPUTED from the block length, never written down: the byte spelling
    /// comes from the catalogue's own zero-vector factory and the text spelling from a repeated zero
    /// character of the same length.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeRows))]
    public void TheModeWithoutVectorArmsUseTheAllZeroVectorOfDecisionD3(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        CipherMaterial zeroVectorMaterial = CreateCipherMaterial(ntype, zeroVector: true);

        Assert.Equal(
            LegacyDefaults.CreateZeroInitializationVector(metrics.BlockLengthBytes),
            zeroVectorMaterial.BinaryIv);

        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(CipherPayloadText);

        // Only the two modes that consume a vector can distinguish D3 from the default arm; the
        // catalogue itself is the authority on which those are.
        foreach (long mode in AllCipherModes.Where(LegacyDefaults.ModeUsesInitializationVector))
        {
            foreach (bool binaryKey in (bool[])[false, true])
            {
                Assert.Equal(
                    EncryptThroughShape(
                        Shape(binaryPayload: false, binaryKey, suppliesIv: true, suppliesMode: true),
                        zeroVectorMaterial,
                        CipherPayloadText,
                        mode),
                    EncryptThroughShape(
                        Shape(binaryPayload: false, binaryKey, suppliesIv: false, suppliesMode: true),
                        zeroVectorMaterial,
                        CipherPayloadText,
                        mode));

                Assert.Equal(
                    EncryptThroughShape(
                        Shape(binaryPayload: true, binaryKey, suppliesIv: true, suppliesMode: true),
                        zeroVectorMaterial,
                        plainBytes,
                        mode),
                    EncryptThroughShape(
                        Shape(binaryPayload: true, binaryKey, suppliesIv: false, suppliesMode: true),
                        zeroVectorMaterial,
                        plainBytes,
                        mode));
            }
        }
    }

    /// <summary>
    /// The result type follows the PAYLOAD type and never the key type, across all thirty-two
    /// declarations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE ASYMMETRY MOST LIKELY TO BE TIDIED WRONGLY, so it is pinned universally rather
    /// than by example. A string payload with a blob key still returns a string
    /// [n_crypto.sru:L34-L37]; a blob payload with a string key still returns a blob [:L38-L41]. The
    /// rule stated positively is that the first parameter's type and the return type are always the
    /// same, which reflection can check for every overload at once and which no amount of example
    /// testing could establish as a universal.
    /// </para>
    /// <para>
    /// It is CORRECT legacy behaviour under constraint C-B, not an oversight to be symmetrized: the
    /// payload spelling is what the caller is working in, and the key spelling is an independent
    /// convenience.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResultTypeFollowsThePayloadTypeAndNeverTheKeyType()
    {
        MethodInfo[] family =
        [
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt"),
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt"),
        ];

        Assert.Equal(32, family.Length);

        foreach (MethodInfo method in family)
        {
            ParameterInfo payload = method.GetParameters()[0];

            Assert.Equal(payload.ParameterType, method.ReturnType);
        }
    }

    /// <summary>
    /// A vector parameter always has the key parameter's type, so the mixed-type overload the legacy
    /// never declared does not exist here either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Across [n_crypto.sru:L30-L61] a string key pairs only with a string vector and a blob key only
    /// with a blob vector. THE ABSENCE OF THE MIXED FORM IS ITSELF THE SPECIFICATION, and absence is
    /// exactly what an example-driven test cannot assert - so it is checked structurally: every
    /// overload that declares a vector is required to declare it at the key's type, and the count of
    /// vector-declaring overloads is required to be the sixteen the table says it is.
    /// </para>
    /// <para>
    /// Under constraint C-B this narrowness is correct and is not to be widened. A caller holding a
    /// text key and raw vector bytes converts one of them; the surface does not grow an overload.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVectorParameterAlwaysHasTheKeyParameterType()
    {
        MethodInfo[] family =
        [
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt"),
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt"),
        ];

        int vectorDeclaringOverloads = 0;

        foreach (MethodInfo method in family)
        {
            ParameterInfo[] parameters = method.GetParameters();
            ParameterInfo key = parameters[1];
            ParameterInfo? vector = parameters
                .FirstOrDefault(parameter =>
                    string.Equals(parameter.Name, "iv", StringComparison.Ordinal));

            if (vector is null)
            {
                continue;
            }

            vectorDeclaringOverloads++;

            Assert.Equal(key.ParameterType, vector.ParameterType);
        }

        Assert.Equal(16, vectorDeclaringOverloads);
    }

    /// <summary>
    /// No symmetric overload exposes a padding argument, and the catalogue agrees that padding is not
    /// selectable.
    /// </summary>
    /// <remarks>
    /// Block padding is PKCS#5-family only and is not a parameter anywhere in [n_crypto.sru:L30-L61].
    /// That is a real capability limit preserved under constraint C-B - a caller cannot ask for a
    /// different padding, and cannot ask for none - and it is asserted here so that adding a padding
    /// argument would fail rather than pass unnoticed as an "improvement".
    /// </remarks>
    [Fact]
    public void NoSymmetricOverloadExposesAPaddingArgument()
    {
        MethodInfo[] family =
        [
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt"),
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt"),
        ];

        foreach (MethodInfo method in family)
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => string.Equals(parameter.Name, "padding", StringComparison.Ordinal));
        }

        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);
    }

    /// <summary>
    /// An unpublished cipher type or cipher mode is refused rather than coerced.
    /// </summary>
    /// <remarks>
    /// The published sets are closed - five types [enums.sru:L936-L940] and three modes
    /// [:L943-L945] - so the value immediately above each set's last member must be rejected. This
    /// also proves the screens are membership tests rather than range tests that a cast could slip
    /// past.
    /// </remarks>
    [Fact]
    public void AnUnpublishedCipherTypeOrModeIsRefused()
    {
        CipherMaterial material = CreateCipherMaterial(
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        Assert.False(LegacyDefaults.IsSupportedSymmetricType(5));
        Assert.False(LegacyDefaults.IsSupportedSymmetricMode(3));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _cipher.SymEncrypt(CipherPayloadText, material.TextKey, ntype: 5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _cipher.SymEncrypt(
                CipherPayloadText,
                material.TextKey,
                material.CipherType,
                mode: 3));
    }

    // ==========================================================================================
    //  REGION 7 - THE ENCODING SURFACE AND ITS ONE-WAY DECLARATIONS
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L13
    //          ws_objects/pfw.crypto.pbl.src/base64encode.srf:L7, base64decode.srf:L7,
    //                                        hexencode.srf:L7, hexdecode.srf:L7
    //  ------------------------------------------------------------------------------------------
    //  This is the surface DECISION D4 is expressed in terms of, and unlike D4 it is CALLER-DRIVEN:
    //  the caller names the encoding on every call. Both published encodings are therefore first
    //  class here, even though only one of them is the payload rendering.
    // ==========================================================================================

    /// <summary>
    /// Both published encodings round trip in both directions and their wrappers are exactly the
    /// identifier-driven calls.
    /// </summary>
    /// <param name="encoding">The published encoding identifier.</param>
    /// <remarks>
    /// The four wrapper members [<c>base64encode.srf:L7</c>, <c>base64decode.srf:L7</c>,
    /// <c>hexencode.srf:L7</c>, <c>hexdecode.srf:L7</c>] each have a one-line legacy body delegating
    /// to the identifier-driven member, so they must be spellings of it and not second
    /// implementations. Driving the encoding as data and then comparing against the matching wrapper
    /// covers both members of each pair on both rows.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EncodingRows))]
    public void BothPublishedEncodingsRoundTripAndTheirWrappersAgree(long encoding)
    {
        byte[] payload = SyntheticBytes(48, offset: 17);

        string encoded = _encoding.BlobToString(payload, encoding);

        Assert.NotEmpty(encoded);
        Assert.Equal(payload, _encoding.StringToBlob(encoded, encoding));

        if (encoding == Enums.CRYPTO_ENCODING_BASE64)
        {
            Assert.Equal(encoded, _encoding.Base64Encode(payload));
            Assert.Equal(payload, _encoding.Base64Decode(encoded));
        }
        else
        {
            Assert.Equal(encoded, _encoding.HexEncode(payload));
            Assert.Equal(payload, _encoding.HexDecode(encoded));
        }
    }

    /// <summary>
    /// The two encodings are genuinely different renderings of the same bytes, and the hexadecimal
    /// one is emitted in upper case.
    /// </summary>
    /// <remarks>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K. THE CASE IS A SUBSTITUTION CHOICE AND NOT A MEASURED
    /// FACT: the closed binary's hexadecimal case is unobservable from the repository, and lower case
    /// is at least as common a convention. Upper case is what the platform's own hexadecimal formatter
    /// emits, so the port inherits it. It is pinned here so that a change becomes visible in a diff
    /// and so that an oracle capture has one assertion to re-baseline.
    /// </remarks>
    [Fact]
    public void TheTwoEncodingsDifferAndHexadecimalIsEmittedInUpperCase()
    {
        byte[] payload = SyntheticBytes(24, offset: 19);

        string base64 = _encoding.Base64Encode(payload);
        string hexadecimal = _encoding.HexEncode(payload);

        Assert.NotEqual(base64, hexadecimal);
        Assert.Equal(hexadecimal.ToUpperInvariant(), hexadecimal);
        Assert.Equal(payload.Length * 2, hexadecimal.Length);
    }

    /// <summary>
    /// Encoding is blob-to-string only and decoding is string-to-blob only; neither reverse direction
    /// exists, deliberately.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BlobToString</c> [n_crypto.sru:L12] has no string-to-string sibling and <c>StringToBlob</c>
    /// [:L11] has no blob-to-blob sibling, and the four legacy wrappers fix the direction in their own
    /// signatures - <c>base64encode.srf:L7</c> takes a blob and returns a string,
    /// <c>base64decode.srf:L7</c> takes a string and returns a blob, and the hexadecimal pair mirrors
    /// them.
    /// </para>
    /// <para>
    /// THE ABSENCE IS THE SPECIFICATION under constraint C-B. These are conversions BETWEEN a byte
    /// domain and a text domain, so a same-domain overload would have no defined meaning: what would
    /// it mean to Base64-encode a string, when the string's own bytes are not yet decided? The
    /// direction is asserted structurally so the surface cannot be quietly completed into a symmetry
    /// the legacy never had.
    /// </para>
    /// </remarks>
    [Fact]
    public void EncodingIsBlobToStringOnlyAndDecodingIsStringToBlobOnly()
    {
        MethodInfo encode = Assert.Single(
            DeclaredOverloads(typeof(EncodingProvider), "BlobToString"));
        MethodInfo decode = Assert.Single(
            DeclaredOverloads(typeof(EncodingProvider), "StringToBlob"));

        Assert.Equal(typeof(byte[]), encode.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(string), encode.ReturnType);

        Assert.Equal(typeof(string), decode.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(byte[]), decode.ReturnType);

        // The wrappers fix the same direction, one member per legacy global function.
        foreach (string encoderName in (string[])["Base64Encode", "HexEncode"])
        {
            MethodInfo encoder = Assert.Single(
                DeclaredOverloads(typeof(EncodingProvider), encoderName));

            Assert.Equal(typeof(byte[]), encoder.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(string), encoder.ReturnType);
        }

        foreach (string decoderName in (string[])["Base64Decode", "HexDecode"])
        {
            MethodInfo decoder = Assert.Single(
                DeclaredOverloads(typeof(EncodingProvider), decoderName));

            Assert.Equal(typeof(string), decoder.GetParameters()[0].ParameterType);
            Assert.Equal(typeof(byte[]), decoder.ReturnType);
        }
    }

    /// <summary>
    /// The reversal member mutates the caller's array in place, reports success, and is the surface's
    /// only by-reference declaration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>BlobReverse(ref blob data)</c> [n_crypto.sru:L13] is the ONLY <c>ref</c> parameter in all 65
    /// declarations, so its in-place semantics are a genuine one-off rather than a house style: it
    /// returns a boolean and its real output is the mutation of the argument. A port that returned a
    /// reversed copy and left the argument alone would compile, would satisfy a naive test that read
    /// the return value, and would break every caller.
    /// </para>
    /// <para>
    /// An array shorter than two elements has nothing to reverse, so the member reports success
    /// without touching it - which is correct, and is the arm a length-driven implementation would get
    /// wrong for the empty case.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReversalMemberMutatesItsArgumentInPlace()
    {
        MethodInfo reverse = Assert.Single(
            DeclaredOverloads(typeof(EncodingProvider), "BlobReverse"));

        Assert.True(reverse.GetParameters()[0].ParameterType.IsByRef);
        Assert.Equal(typeof(bool), reverse.ReturnType);

        byte[] evenLength = SyntheticBytes(8, offset: 23);
        byte[] expectedEven = [.. evenLength.Reverse()];

        Assert.True(_encoding.BlobReverse(ref evenLength));
        Assert.Equal(expectedEven, evenLength);

        byte[] oddLength = SyntheticBytes(7, offset: 2);
        byte[] expectedOdd = [.. oddLength.Reverse()];

        Assert.True(_encoding.BlobReverse(ref oddLength));
        Assert.Equal(expectedOdd, oddLength);

        // Nothing to reverse, and still a success rather than a failure.
        byte[] single = SyntheticBytes(1, offset: 4);
        byte[] expectedSingle = [.. single];

        Assert.True(_encoding.BlobReverse(ref single));
        Assert.Equal(expectedSingle, single);

        byte[] empty = [];

        Assert.True(_encoding.BlobReverse(ref empty));
        Assert.Empty(empty);
    }

    /// <summary>
    /// An unpublished encoding identifier is refused by both directions.
    /// </summary>
    /// <remarks>
    /// The published set is exactly two values [enums.sru:L924-L925], so the value immediately above
    /// it must be rejected rather than treated as a default.
    /// </remarks>
    [Fact]
    public void AnUnpublishedEncodingIsRefusedByBothDirections()
    {
        Assert.False(LegacyDefaults.IsSupportedEncoding(2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _encoding.BlobToString(SyntheticBytes(4, offset: 1), encoding: 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _encoding.StringToBlob("AAAA", encoding: 2));
    }

    // ==========================================================================================
    //  REGION 8 - THE RSA SURFACE
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20, :L62-L73
    //          ws_objects/pfw.shared.pbl.src/enums.sru:L949-L951, :L965-L967
    //  ------------------------------------------------------------------------------------------
    //  Fourteen declarations: two generation arities, four encrypt, four decrypt, two sign and two
    //  verify. EVERY KEY BELOW IS GENERATED AT TEST TIME (constraint C-F) - by the provider's own
    //  generation members for the shared pair, and by the platform's key factory for the two
    //  additional import spellings. Nothing is a literal, and no key content reaches an assertion
    //  message.
    // ==========================================================================================

    /// <summary>
    /// Both generation arities succeed at the smallest legal size and produce material the rest of the
    /// surface accepts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three-argument arity [n_crypto.sru:L19] takes the armouring decision from the provider; the
    /// four-argument arity [:L20] states it. Both are required to return success and to produce a pair
    /// that actually works, because a generation member that returned success and emitted unusable
    /// text would pass any assertion that only checked the boolean.
    /// </para>
    /// <para>
    /// PRESERVED WEAKNESS, ANNOTATED. The size is the catalogue's smallest legal value, 1024 bits
    /// [enums.sru:L965], which is below every current recommendation. It remains legal because it is
    /// legal in the legacy and rejecting it would refuse input the legacy accepted - constraint C-B -
    /// and the catalogue records separately that no minimum is enforced. This test asserts that it
    /// genuinely still generates, which is a behavioural claim the catalogue itself cannot make.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothGenerationAritiesSucceedAtTheSmallestLegalSize()
    {
        Assert.True(SharedRsaKeys.DerGenerated);
        Assert.True(SharedRsaKeys.PemGenerated);

        Assert.NotEmpty(SharedRsaKeys.DerPrivateKey);
        Assert.NotEmpty(SharedRsaKeys.DerPublicKey);
        Assert.NotEmpty(SharedRsaKeys.PemPrivateKey);
        Assert.NotEmpty(SharedRsaKeys.PemPublicKey);

        Assert.False(LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED);

        // Both spellings are usable end to end, which is the only meaningful test of a generated key.
        foreach ((string privateKey, string publicKey) in (ValueTuple<string, string>[])
        [
            (SharedRsaKeys.DerPrivateKey, SharedRsaKeys.DerPublicKey),
            (SharedRsaKeys.PemPrivateKey, SharedRsaKeys.PemPublicKey),
        ])
        {
            string cipher = _rsa.RSAEncrypt(RsaPayloadText, publicKey);

            Assert.Equal(RsaPayloadText, _rsa.RSADecrypt(cipher, privateKey));

            string signature = _rsa.RSASign(RsaPayloadText, privateKey, Enums.CRYPTO_HASH_SHA256);

            Assert.True(
                _rsa.VerifyRSASign(
                    RsaPayloadText,
                    signature,
                    publicKey,
                    Enums.CRYPTO_HASH_SHA256));
        }
    }

    /// <summary>
    /// The two generated spellings are genuinely different text forms of one key concept, and the
    /// unarmoured spelling is the arity-three default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The armoured spelling is a multi-line, delimiter-wrapped text block; the unarmoured spelling is
    /// a single-line encoded blob with no delimiters at all. Distinguishing them by SHAPE - the
    /// presence of a line separator - rather than by matching a delimiter literal is deliberate: it
    /// keeps every armour marker and every key-shaped literal out of this file, which is what makes a
    /// constraint C-F sweep of it come back empty.
    /// </para>
    /// <para>
    /// That arity three produces the unarmoured spelling is a PROVIDER DEFAULT and is annotated as
    /// such: the legacy's own default for the omitted <c>pemformat</c> argument [n_crypto.sru:L19
    /// against :L20] is not observable from the repository. What is asserted without qualification is
    /// that the two arities produce DIFFERENT spellings and that both are importable, so the argument
    /// is not being ignored.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoGeneratedSpellingsAreDistinctAndArityThreeIsUnarmoured()
    {
        Assert.DoesNotContain('\n', SharedRsaKeys.DerPrivateKey);
        Assert.DoesNotContain('\n', SharedRsaKeys.DerPublicKey);

        Assert.Contains('\n', SharedRsaKeys.PemPrivateKey);
        Assert.Contains('\n', SharedRsaKeys.PemPublicKey);

        Assert.NotEqual(SharedRsaKeys.DerPrivateKey, SharedRsaKeys.PemPrivateKey);

        // The unarmoured spelling is the payload encoding of raw key bytes, so it decodes cleanly
        // through the shared encoding provider; the armoured one does not, because it is not a bare
        // encoded blob.
        Assert.NotEmpty(_encoding.Base64Decode(SharedRsaKeys.DerPublicKey));
    }

    /// <summary>
    /// A key size outside the platform's legal range is reported as failure without throwing and
    /// without disturbing the caller's variables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CEILING ROW IS THE WIDTH DETECTOR. <c>readonly uint bits</c> [n_crypto.sru:L19-L20] is a
    /// SIXTEEN-BIT PowerBuilder unsigned integer, so the top of its range is 65535 - a value no RSA
    /// implementation will generate. Driving exactly that value proves two things at once: the
    /// parameter really is 16-bit, because a widened parameter would make this row express a different
    /// number; and the member screens the size BEFORE attempting generation, because an implementation
    /// that tried first would not return at all in any useful time.
    /// </para>
    /// <para>
    /// Zero is the other end of the same range. Both return failure rather than raising, and both
    /// leave the by-reference arguments exactly as the caller set them - which matters because a
    /// caller that ignores the boolean must not find plausible-looking rubbish in its key variables.
    /// </para>
    /// </remarks>
    [Fact]
    public void AKeySizeOutsideThePlatformRangeIsReportedAsFailureWithoutThrowing()
    {
        const string untouched = "untouched";

        string privateKey = untouched;
        string publicKey = untouched;

        Assert.False(_rsa.GenRSAKey(ushort.MaxValue, ref privateKey, ref publicKey));
        Assert.Equal(untouched, privateKey);
        Assert.Equal(untouched, publicKey);

        Assert.False(_rsa.GenRSAKey(0, ref privateKey, ref publicKey, pemformat: true));
        Assert.Equal(untouched, privateKey);
        Assert.Equal(untouched, publicKey);
    }

    /// <summary>
    /// Both published paddings round trip through both payload shapes of the encrypt and decrypt
    /// families.
    /// </summary>
    /// <param name="padding">The published padding identifier.</param>
    /// <remarks>
    /// Drives the padding-taking overloads [n_crypto.sru:L63 and :L65 for encrypt, :L67 and :L69 for
    /// decrypt] across both payload shapes, which is four of the eight declarations per row. The
    /// payload is short enough to fit the tighter of the two paddings under the smallest legal
    /// modulus, so one payload serves both rows and the padding axis is a genuine matrix rather than
    /// two unrelated tests.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RsaPaddingRows))]
    public void BothPublishedPaddingsRoundTripThroughBothPayloadShapes(long padding)
    {
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText);

        string textCipher = _rsa.RSAEncrypt(
            RsaPayloadText,
            SharedRsaKeys.DerPublicKey,
            padding);

        Assert.Equal(
            RsaPayloadText,
            _rsa.RSADecrypt(textCipher, SharedRsaKeys.DerPrivateKey, padding));

        byte[] blobCipher = _rsa.RSAEncrypt(plainBytes, SharedRsaKeys.DerPublicKey, padding);

        Assert.Equal(
            plainBytes,
            _rsa.RSADecrypt(blobCipher, SharedRsaKeys.DerPrivateKey, padding));
    }

    /// <summary>
    /// The padding-omitting overloads behave exactly as the padding-taking ones do with the
    /// catalogue's default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drives the four padding-omitting declarations [n_crypto.sru:L62, :L64, :L66, :L68] and requires
    /// each to interoperate with its padding-taking sibling given the default identifier - encrypt
    /// without and decrypt with, and the reverse - which is a stronger claim than comparing two cipher
    /// texts, RSA encryption being randomized so two cipher texts of one payload never match.
    /// </para>
    /// <para>
    /// PRESERVED WEAKNESS, ANNOTATED. The default is PKCS#1 v1.5 [enums.sru:L951], which is the older
    /// and weaker of the two published schemes, and no-padding is not a published value at all. Both
    /// are preserved under constraint C-B: changing the default would change what every existing
    /// caller produces.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePaddingOmittingOverloadsUseTheCatalogueDefault()
    {
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText);
        long defaultPadding = LegacyDefaults.RSA_PADDING_DEFAULT;

        // Encrypted without the argument, decrypted with the default stated explicitly.
        string textCipher = _rsa.RSAEncrypt(RsaPayloadText, SharedRsaKeys.DerPublicKey);

        Assert.Equal(
            RsaPayloadText,
            _rsa.RSADecrypt(textCipher, SharedRsaKeys.DerPrivateKey, defaultPadding));

        byte[] blobCipher = _rsa.RSAEncrypt(plainBytes, SharedRsaKeys.DerPublicKey);

        Assert.Equal(
            plainBytes,
            _rsa.RSADecrypt(blobCipher, SharedRsaKeys.DerPrivateKey, defaultPadding));

        // And the reverse: encrypted with the default stated, decrypted without the argument.
        string statedTextCipher = _rsa.RSAEncrypt(
            RsaPayloadText,
            SharedRsaKeys.DerPublicKey,
            defaultPadding);

        Assert.Equal(
            RsaPayloadText,
            _rsa.RSADecrypt(statedTextCipher, SharedRsaKeys.DerPrivateKey));

        byte[] statedBlobCipher = _rsa.RSAEncrypt(
            plainBytes,
            SharedRsaKeys.DerPublicKey,
            defaultPadding);

        Assert.Equal(plainBytes, _rsa.RSADecrypt(statedBlobCipher, SharedRsaKeys.DerPrivateKey));

        Assert.False(LegacyDefaults.RSA_NO_PADDING_SUPPORTED);
    }

    /// <summary>
    /// The string-shaped and blob-shaped encrypt families interoperate through the shared payload
    /// rendering.
    /// </summary>
    /// <remarks>
    /// RSA encryption is randomized, so two cipher texts of one payload never match and the cross
    /// family claim has to be made by CROSSING THE FAMILIES rather than by comparing outputs: cipher
    /// text produced by the string family, decoded through the shared encoding provider, must decrypt
    /// through the blob family, and the reverse must hold too. That is the RSA half of the DECISION D4
    /// agreement, and it is what a caller relies on when cipher text travels over a text transport
    /// and is consumed by a byte-oriented reader.
    /// </remarks>
    [Fact]
    public void TheTwoEncryptFamiliesInteroperateThroughTheSharedRendering()
    {
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText);

        string textCipher = _rsa.RSAEncrypt(RsaPayloadText, SharedRsaKeys.DerPublicKey);

        Assert.Equal(
            plainBytes,
            _rsa.RSADecrypt(DecodePayload(textCipher), SharedRsaKeys.DerPrivateKey));

        byte[] blobCipher = _rsa.RSAEncrypt(plainBytes, SharedRsaKeys.DerPublicKey);

        Assert.Equal(
            RsaPayloadText,
            _rsa.RSADecrypt(EncodePayload(blobCipher), SharedRsaKeys.DerPrivateKey));
    }

    /// <summary>
    /// Every signature algorithm signs and verifies through all four declarations, and the two
    /// signature spellings are one signature.
    /// </summary>
    /// <param name="ntype">The signature algorithm identifier, from the shared hash-type set.</param>
    /// <remarks>
    /// <para>
    /// The algorithm axis is the SAME set the digest surface uses [enums.sru:L927], which is why the
    /// rows come from the shared keyed-algorithm source rather than from a signing-specific list.
    /// </para>
    /// <para>
    /// The signature scheme substituted here is PKCS#1 v1.5, which is DETERMINISTIC - unlike a
    /// probabilistic scheme, signing the same bytes with the same key twice gives the same signature.
    /// That is what makes the cross-spelling assertion possible at all: the string-shaped signature
    /// [n_crypto.sru:L70] must be the payload rendering of the blob-shaped one [:L71]. Both verify
    /// declarations [:L72, :L73] are then driven against BOTH spellings, crossed, so a signature
    /// produced by either family verifies through either family.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedHashTypeRows))]
    public void EverySignatureAlgorithmSignsAndVerifiesThroughAllFourDeclarations(long ntype)
    {
        byte[] dataBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText);

        string textSignature = _rsa.RSASign(RsaPayloadText, SharedRsaKeys.DerPrivateKey, ntype);
        byte[] blobSignature = _rsa.RSASign(dataBytes, SharedRsaKeys.DerPrivateKey, ntype);

        Assert.Equal(EncodePayload(blobSignature), textSignature);
        Assert.Equal(blobSignature, DecodePayload(textSignature));

        Assert.True(
            _rsa.VerifyRSASign(RsaPayloadText, textSignature, SharedRsaKeys.DerPublicKey, ntype));
        Assert.True(
            _rsa.VerifyRSASign(dataBytes, blobSignature, SharedRsaKeys.DerPublicKey, ntype));

        // Crossed: the blob signature rendered as text, and the text signature decoded to bytes.
        Assert.True(
            _rsa.VerifyRSASign(
                RsaPayloadText,
                EncodePayload(blobSignature),
                SharedRsaKeys.DerPublicKey,
                ntype));
        Assert.True(
            _rsa.VerifyRSASign(
                dataBytes,
                DecodePayload(textSignature),
                SharedRsaKeys.DerPublicKey,
                ntype));
    }

    /// <summary>
    /// A verify declaration's signature parameter has its data parameter's type, and no mixed overload
    /// exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pairing is explicit in the legacy: text data travels with a text signature
    /// [n_crypto.sru:L72] and blob data with a blob signature [:L73]. THIS IS THE ONE PLACE THE
    /// FAMILY'S USUAL RULE DOES NOT APPLY - elsewhere a spelling axis is independent of the payload
    /// axis, whereas here the signature spelling FOLLOWS the data spelling - which is exactly why it
    /// is easy to miss and why it is pinned structurally.
    /// </para>
    /// <para>
    /// A single implementation accepting one signature spelling for both data spellings would
    /// mis-handle one of the two: the text form carries an encoded payload and the blob form carries
    /// raw signature bytes.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVerifySignatureParameterFollowsItsDataParameterType()
    {
        MethodInfo[] verify = DeclaredOverloads(typeof(RsaProvider), "VerifyRSASign");

        Assert.Equal(2, verify.Length);

        foreach (MethodInfo method in verify)
        {
            ParameterInfo[] parameters = method.GetParameters();

            Assert.Equal(parameters[0].ParameterType, parameters[1].ParameterType);
            Assert.Equal(typeof(bool), method.ReturnType);
        }

        // The signing pair mirrors it in the return position: a text payload signs to text, a blob
        // payload to a blob [n_crypto.sru:L70-L71].
        foreach (MethodInfo method in DeclaredOverloads(typeof(RsaProvider), "RSASign"))
        {
            Assert.Equal(method.GetParameters()[0].ParameterType, method.ReturnType);
        }
    }

    /// <summary>
    /// Verification fails, without raising, for a tampered signature, the wrong key, altered data, or
    /// a signature that is not even well-formed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A verify member returns a boolean [n_crypto.sru:L72-L73], so a NEGATIVE RESULT MUST BE A
    /// RETURNED FALSE AND NOT AN EXCEPTION - a caller that wrote a plain conditional around it would
    /// otherwise fail open or crash on the one input it most needs to handle.
    /// </para>
    /// <para>
    /// The malformed-payload row is the subtle one: a string-shaped signature that is not valid encoded
    /// text cannot be decoded at all, and the correct answer is still a plain false rather than a
    /// format failure escaping to the caller.
    /// </para>
    /// </remarks>
    [Fact]
    public void VerificationFailsWithoutRaisingForEveryKindOfBadInput()
    {
        byte[] dataBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText);
        byte[] signature = _rsa.RSASign(
            dataBytes,
            SharedRsaKeys.DerPrivateKey,
            Enums.CRYPTO_HASH_SHA256);

        // A single flipped bit in the signature.
        byte[] tampered = [.. signature];
        tampered[0] ^= 0x01;

        Assert.False(
            _rsa.VerifyRSASign(
                dataBytes,
                tampered,
                SharedRsaKeys.DerPublicKey,
                Enums.CRYPTO_HASH_SHA256));

        // Altered data against a genuine signature.
        Assert.False(
            _rsa.VerifyRSASign(
                LegacyDefaults.KeyMaterialEncoding.GetBytes(RsaPayloadText + "!"),
                signature,
                SharedRsaKeys.DerPublicKey,
                Enums.CRYPTO_HASH_SHA256));

        // A different, unrelated key pair, generated for this assertion alone.
        string otherPrivateKey = string.Empty;
        string otherPublicKey = string.Empty;

        Assert.True(
            _rsa.GenRSAKey(
                LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS,
                ref otherPrivateKey,
                ref otherPublicKey));

        Assert.False(
            _rsa.VerifyRSASign(
                dataBytes,
                signature,
                otherPublicKey,
                Enums.CRYPTO_HASH_SHA256));

        // A string-shaped signature that is not well-formed encoded text at all.
        Assert.False(
            _rsa.VerifyRSASign(
                RsaPayloadText,
                "this is not an encoded signature",
                SharedRsaKeys.DerPublicKey,
                Enums.CRYPTO_HASH_SHA256));

        // A different algorithm from the one the signature was produced under.
        Assert.False(
            _rsa.VerifyRSASign(
                dataBytes,
                signature,
                SharedRsaKeys.DerPublicKey,
                Enums.CRYPTO_HASH_SHA512));
    }

    /// <summary>
    /// Key import accepts every spelling the surface can be handed and refuses unusable material by
    /// argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy takes a key as a bare <c>string</c> in all fourteen RSA declarations, with no format
    /// discriminator anywhere, so the port has to accept whatever a caller reasonably has: the
    /// armoured text form, and the unarmoured encoded form in the private, wrapped-private and public
    /// structures the platform can export. Each is generated here at test time; none is a literal.
    /// </para>
    /// <para>
    /// Unusable material fails as an ARGUMENT failure naming the offending parameter, not as a
    /// cryptographic one, because from the caller's position a key it cannot use is a bad argument.
    /// Empty, blank, non-encoded and well-encoded-but-not-a-key are all driven, and a null key is a
    /// null-argument failure.
    /// </para>
    /// </remarks>
    [Fact]
    public void KeyImportAcceptsEverySpellingAndRefusesUnusableMaterialByArgument()
    {
        using RSA generated = RSA.Create(LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);

        string wrappedPrivateKey = _encoding.Base64Encode(generated.ExportPkcs8PrivateKey());
        string barePublicKey = _encoding.Base64Encode(generated.ExportRSAPublicKey());

        string cipher = _rsa.RSAEncrypt(RsaPayloadText, barePublicKey);

        Assert.Equal(RsaPayloadText, _rsa.RSADecrypt(cipher, wrappedPrivateKey));

        // Unusable material, four ways.
        Assert.Throws<ArgumentException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, string.Empty));
        Assert.Throws<ArgumentException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, "   "));
        Assert.Throws<ArgumentException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, "not encoded at all %%%"));
        Assert.Throws<ArgumentException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, _encoding.Base64Encode(SyntheticBytes(32, offset: 6))));

        // A fifth way, and the one that reaches the armoured path's own failure arm: text with the
        // GENERATED key's real delimiter lines but a body that is well-formed encoded data and not a
        // key. The delimiter lines are taken from the generated pair at run time rather than written
        // out as literals, which keeps every armour marker out of this file's source (constraint C-F)
        // while still driving the armoured import to failure.
        string[] segments = SharedRsaKeys.PemPrivateKey.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.True(segments.Length > 2, "A generated armoured key has delimiter lines and a body.");

        string armouredButNotAKey = string.Join(
            '\n',
            segments[0],
            _encoding.Base64Encode(SyntheticBytes(48, offset: 8)),
            segments[^1]);

        Assert.Throws<ArgumentException>(
            () => _rsa.RSADecrypt("AAAA", armouredButNotAKey));

        ArgumentNullException missing = Assert.Throws<ArgumentNullException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, null!));

        Assert.Equal("pubkey", missing.ParamName);
    }

    /// <summary>
    /// An unpublished RSA padding identifier is refused by both directions.
    /// </summary>
    /// <remarks>
    /// The published set is exactly two values [enums.sru:L949-L950]. No-padding in particular is NOT
    /// a member and is not silently mapped onto one of the two, which the catalogue records as an
    /// unsupported capability rather than an omission.
    /// </remarks>
    [Fact]
    public void AnUnpublishedRsaPaddingIsRefused()
    {
        Assert.False(LegacyDefaults.IsSupportedRsaPadding(2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSAEncrypt(RsaPayloadText, SharedRsaKeys.DerPublicKey, padding: 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSADecrypt("AAAA", SharedRsaKeys.DerPrivateKey, padding: 2));
    }

    // ==========================================================================================
    //  REGION 9 - THE DECLARED ARGUMENT WIDTHS
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14-L20, :L21-L29, :L30-L61, :L62-L73
    //          ws_objects/pfw.shared.pbl.src/enums.sru:L954-L967
    //  ------------------------------------------------------------------------------------------
    //  WHY THIS REGION EXISTS AT ALL. A widened numeric parameter is the most invisible defect in
    //  this whole port: it compiles, it passes every behavioural test, it never throws, and it
    //  silently changes the published contract. PowerBuilder's `uint` is SIXTEEN bits and its `ulong`
    //  is THIRTY-TWO, neither of which matches the C# type of the same name, so the mapping the port
    //  must hold is uint to ushort, ulong to uint, and long to long. Two independent detectors are
    //  used together: the declared parameter TYPE, and the CEILING of that type read from the type
    //  itself - because a widened parameter has a different ceiling even when it still accepts every
    //  legal value.
    // ==========================================================================================

    /// <summary>
    /// Reads a numeric type's own maximum-value constant, so that a ceiling assertion is made against
    /// the DECLARED type rather than against a value this file chose.
    /// </summary>
    /// <param name="numericType">The declared parameter type.</param>
    /// <returns>The boxed maximum value the type can represent.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="numericType"/> publishes no maximum-value constant, which means the parameter
    /// is not the integral type this suite expected.
    /// </exception>
    /// <remarks>
    /// This is what makes "a boundary row at the top of the width" a real detector. A row that simply
    /// wrote the number 65535 would keep passing after a widening to <see cref="int"/>, because 65535
    /// is a perfectly legal <see cref="int"/>; reading the ceiling FROM the declared type instead means
    /// the widening changes the answer and the assertion fails.
    /// </remarks>
    private static object CeilingOf(Type numericType)
    {
        FieldInfo? maximum = numericType.GetField(
            "MaxValue",
            BindingFlags.Public | BindingFlags.Static);

        return maximum?.GetValue(null)
            ?? throw new ArgumentException(
                "The declared parameter type publishes no maximum value, so it is not one of the " +
                "integral types the legacy declaration list uses.",
                nameof(numericType));
    }

    /// <summary>
    /// Every parameter that carries a legacy width is declared at the type that width maps to.
    /// </summary>
    /// <param name="providerName">The provider's simple type name.</param>
    /// <param name="methodName">The declaration name.</param>
    /// <param name="parameterName">The parameter whose width is under test.</param>
    /// <param name="expectedTypeName">The expected runtime type's simple name.</param>
    /// <remarks>
    /// Applied to EVERY overload that declares the named parameter, not merely the first, because a
    /// widening introduced on one overload of sixteen is exactly the kind of defect a single-overload
    /// check would miss. The row is also required to match at least one overload, so a renamed
    /// parameter fails loudly instead of vacuously passing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(DeclaredArgumentWidths))]
    public void EveryLegacyWidthIsDeclaredAtItsMappedType(
        string providerName,
        string methodName,
        string parameterName,
        string expectedTypeName)
    {
        MethodInfo[] overloads = DeclaredOverloads(ResolveProviderType(providerName), methodName);

        Assert.NotEmpty(overloads);

        int matched = 0;

        foreach (MethodInfo overload in overloads)
        {
            ParameterInfo? parameter = overload.GetParameters()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));

            if (parameter is null)
            {
                continue;
            }

            matched++;

            Assert.Equal(expectedTypeName, parameter.ParameterType.Name);
        }

        Assert.True(
            matched > 0,
            $"{providerName}.{methodName} declares no parameter named '{parameterName}', so this " +
            "width row is asserting nothing.");
    }

    /// <summary>
    /// The key-size parameter is sixteen bits wide, and its ceiling is rejected as a size rather than
    /// silently accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>readonly uint bits</c> [n_crypto.sru:L19-L20] is a SIXTEEN-BIT PowerBuilder unsigned integer.
    /// The ceiling is read from the declared type itself, so a widening to <see cref="int"/> or
    /// <see cref="long"/> changes the ceiling and fails this test even though every legal key size
    /// would still work.
    /// </para>
    /// <para>
    /// The value-level half of the same detector lives in the generation test above, which drives
    /// exactly <see cref="ushort.MaxValue"/> and requires a plain failure rather than an attempt.
    /// Together they make a silent widening undetectable-by-accident, which is the point.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheKeySizeParameterIsSixteenBitsWide()
    {
        foreach (MethodInfo overload in DeclaredOverloads(typeof(RsaProvider), "GenRSAKey"))
        {
            ParameterInfo bits = overload.GetParameters()[0];

            Assert.Equal("bits", bits.Name);
            Assert.Equal(typeof(ushort), bits.ParameterType);
            Assert.Equal((object)ushort.MaxValue, CeilingOf(bits.ParameterType));
        }
    }

    /// <summary>
    /// The symmetric cipher-type parameter is sixteen bits wide even though the constants naming its
    /// legal values are thirty-two.
    /// </summary>
    /// <remarks>
    /// THIS MISMATCH IS THE LEGACY'S OWN AND IS PRESERVED, NOT TIDIED. All thirty-two symmetric
    /// declarations take <c>readonly uint ntype</c> [n_crypto.sru:L30-L61] - sixteen bits - while the
    /// five constants that name its only legal values are declared <c>Constant Long</c>
    /// [enums.sru:L936-L940] - thirty-two bits. Every call site therefore narrows, in the legacy
    /// exactly as here. Widening the parameter to match the constants would be the obvious tidy-up and
    /// would change the published contract, so under constraint C-B the mismatch stands and is
    /// asserted.
    /// </remarks>
    [Fact]
    public void TheSymmetricTypeParameterIsSixteenBitsWideWhileItsConstantsAreThirtyTwo()
    {
        MethodInfo[] family =
        [
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymEncrypt"),
            .. DeclaredOverloads(typeof(SymmetricCipherProvider), "SymDecrypt"),
        ];

        foreach (MethodInfo overload in family)
        {
            ParameterInfo ntype = overload.GetParameters()
                .Single(parameter => string.Equals(parameter.Name, "ntype", StringComparison.Ordinal));

            Assert.Equal(typeof(ushort), ntype.ParameterType);
            Assert.Equal((object)ushort.MaxValue, CeilingOf(ntype.ParameterType));
        }

        // The catalogue's own screening predicate takes the narrow type too, so the narrowing happens
        // at the caller and not silently inside the screen.
        MethodInfo screen = typeof(LegacyDefaults).GetMethod(
            nameof(LegacyDefaults.IsSupportedSymmetricType),
            BindingFlags.Public | BindingFlags.Static)!;

        Assert.Equal(typeof(ushort), screen.GetParameters()[0].ParameterType);

        // And the constants really are the wider type, which is the other half of the mismatch.
        Assert.Equal(typeof(long), Enums.CRYPTO_SYMCRYPT_TYPE_AES256.GetType());
    }

    /// <summary>
    /// The size and flag parameters of the random surface are thirty-two bits wide and unsigned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GenRandomBlob</c>, both <c>GenRandomString</c> arities and the flag-taking identifier member
    /// [n_crypto.sru:L14-L18] declare <c>readonly ulong</c>, which is a THIRTY-TWO-BIT PowerBuilder
    /// unsigned integer and therefore maps to <see cref="uint"/> rather than to C#'s <c>ulong</c>. The
    /// flag constants those parameters exist to receive are declared <c>Constant Ulong</c>
    /// [enums.sru:L954-L962], and the catalogue's ported defaults are <see cref="uint"/> - which is the
    /// in-dependency half of this assertion and is checked first.
    /// </para>
    /// <para>
    /// WHY THE RANDOM PROVIDER IS REACHED BY NAME HERE, AND ONLY HERE. That provider is NOT among this
    /// file's declared dependencies and it already has its own characterization suite, which owns every
    /// behavioural claim about alphabets, flag masking and identifier shapes. Restating any of that
    /// here would duplicate it, and constraint C-B is explicit that a preserved behaviour is asserted
    /// once. What its own suite does NOT assert, because a per-provider suite has no reason to, is that
    /// its widths agree with the SAME width mapping the rest of the surface follows - and a width
    /// mapping applied in five providers out of six is not a mapping. This test therefore makes a
    /// SHAPE-ONLY assertion, resolved from the assembly this file already legitimately references, and
    /// calls nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRandomSizeAndFlagParametersAreThirtyTwoBitsWideAndUnsigned()
    {
        // The in-dependency half: the catalogue's ported flag defaults carry the mapped type.
        Assert.Equal(typeof(uint), LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT.GetType());
        Assert.Equal(typeof(uint), LegacyDefaults.GUID_FLAGS_DEFAULT.GetType());

        Type randomProvider = typeof(RsaProvider).Assembly.GetType(
            "PowerFramework.Security.Crypto.RandomProvider",
            throwOnError: true)!;

        foreach ((string methodName, int parameterCount, string parameterName) in
            (ValueTuple<string, int, string>[])
            [
                ("GenRandomBlob", 1, "size"),
                ("GenRandomString", 1, "size"),
                ("GenRandomString", 2, "flags"),
                ("GenGuid", 1, "flags"),
            ])
        {
            MethodInfo declaration = DeclaredOverloads(randomProvider, methodName)
                .Single(overload => overload.GetParameters().Length == parameterCount);

            ParameterInfo parameter = declaration.GetParameters()
                .Single(candidate =>
                    string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));

            Assert.Equal(typeof(uint), parameter.ParameterType);
            Assert.Equal((object)uint.MaxValue, CeilingOf(parameter.ParameterType));
        }
    }

    // ==========================================================================================
    //  REGION 10 - DECISION D4 CONSUMED ONCE BY FOUR PROVIDERS
    //  ------------------------------------------------------------------------------------------
    //  The single most load-bearing substitution in the folder, and the one no per-provider suite can
    //  pin: four independent files must render a string-shaped payload the SAME way. The mechanism is
    //  constructor injection of one encoding provider, so both the mechanism and its effect are
    //  asserted - the shape that makes divergence impossible, and the behaviour that shows it has not
    //  occurred.
    // ==========================================================================================

    /// <summary>
    /// Every string-shaped provider is constructed from the one encoding provider and from nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SHAPE IS THE GUARANTEE. Four providers that each chose their own renderer would be four
    /// decisions that could drift; four providers that each REQUIRE the renderer as their only
    /// dependency are one decision consumed four times. Asserting the constructor shape is therefore
    /// asserting the mechanism, not the style.
    /// </para>
    /// <para>
    /// The same assertion carries a second guarantee that matters for constraint C-F: a single
    /// parameter of the renderer's type means NO PROVIDER TAKES CONFIGURATION AND NO PROVIDER TAKES KEY
    /// MATERIAL AT CONSTRUCTION. Key material arrives per call and is never held, which is why a
    /// key-reference resolution boundary belongs at the published endpoint rather than in any of these
    /// types. The renderer itself takes nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryStringShapedProviderIsConstructedFromTheOneEncodingProviderAlone()
    {
        Type[] stringShapedProviders =
        [
            typeof(HashProvider),
            typeof(HmacProvider),
            typeof(SymmetricCipherProvider),
            typeof(RsaProvider),
        ];

        foreach (Type provider in stringShapedProviders)
        {
            ConstructorInfo constructor = Assert.Single(provider.GetConstructors());
            ParameterInfo dependency = Assert.Single(constructor.GetParameters());

            Assert.Equal(typeof(EncodingProvider), dependency.ParameterType);

            // Constructed with a null renderer, each refuses rather than silently substituting one.
            // Reflective invocation wraps whatever the constructor raised, so the wrapper is unpacked
            // rather than asserted on: the claim is about the constructor's behaviour, not about how
            // reflection reports it.
            TargetInvocationException wrapped = Assert.Throws<TargetInvocationException>(
                () => constructor.Invoke([null]));

            ArgumentNullException refused =
                Assert.IsType<ArgumentNullException>(wrapped.InnerException);

            Assert.Equal("encoding", refused.ParamName);
        }

        // The renderer is the root of the chain and depends on nothing.
        Assert.Empty(Assert.Single(typeof(EncodingProvider).GetConstructors()).GetParameters());
    }

    /// <summary>
    /// All four string-shaped families carry the catalogue's payload encoding, and none of them carries
    /// the other published encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One assertion per family, in one place, over one set of inputs: an unkeyed digest, a keyed
    /// authenticator, symmetric cipher text and an RSA signature. Each is required to survive a decode
    /// and re-encode through the catalogue's payload encoding unchanged, and each is required NOT to
    /// equal the other published encoding's rendering of the same bytes.
    /// </para>
    /// <para>
    /// ANNOTATION, REQUIRED BY CONSTRAINT C-K. That the payload encoding is Base64 rather than
    /// hexadecimal is DECISION D4 - a reasoned choice from the legacy's own zero-valued primary
    /// encoding constant and from its documented cross-language interoperability intent, NOT a
    /// measurement of the closed binary, whose text spelling is unobservable from this repository. The
    /// claim asserted without qualification is the AGREEMENT: whatever the rendering turns out to be,
    /// all four families use the same one, and re-baselining it means changing one catalogue member
    /// rather than four providers.
    /// </para>
    /// </remarks>
    [Fact]
    public void AllFourStringShapedFamiliesCarryTheCataloguePayloadEncoding()
    {
        Assert.True(LegacyDefaults.IsSupportedEncoding(LegacyDefaults.STRING_PAYLOAD_ENCODING));

        CipherMaterial material = CreateCipherMaterial((ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        string[] payloads =
        [
            // The unkeyed digest family [n_crypto.sru:L21].
            _hash.Hash(RsaPayloadText, Enums.CRYPTO_HASH_SHA256),

            // The keyed authenticator family [:L23].
            _hmac.Hash(RsaPayloadText, PublishedHmacKeyText, Enums.CRYPTO_HASH_SHA256),

            // The symmetric cipher family [:L33].
            _cipher.SymEncrypt(
                CipherPayloadText,
                material.TextKey,
                material.TextIv,
                material.CipherType,
                Enums.CRYPTO_SYMCRYPT_MODE_CBC),

            // The RSA signature family [:L70].
            _rsa.RSASign(RsaPayloadText, SharedRsaKeys.DerPrivateKey, Enums.CRYPTO_HASH_SHA256),
        ];

        foreach (string payload in payloads)
        {
            Assert.NotEmpty(payload);

            byte[] carried = DecodePayload(payload);

            Assert.NotEmpty(carried);
            Assert.Equal(payload, EncodePayload(carried));

            long otherEncoding =
                LegacyDefaults.STRING_PAYLOAD_ENCODING == Enums.CRYPTO_ENCODING_BASE64
                    ? Enums.CRYPTO_ENCODING_HEX
                    : Enums.CRYPTO_ENCODING_BASE64;

            Assert.NotEqual(_encoding.BlobToString(carried, otherEncoding), payload);
        }
    }

    /// <summary>
    /// The two absent capabilities of the whole surface stay absent, and no member offers them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DECISION D1 - THERE IS NO KEY DERIVATION ANYWHERE. Nothing in the 65 declarations reaches a
    /// derivation function: there is no iteration count, no salt and no work factor in any parameter
    /// list, so a passphrase is used AS RAW KEY BYTES. That is materially weaker than a derived key -
    /// a low-entropy passphrase becomes a low-entropy key with no stretching whatever - and it is
    /// preserved because deriving would change every value the legacy ever produced, so nothing
    /// already encrypted could be read back.
    /// </para>
    /// <para>
    /// DECISION D2 - THERE IS NO AUTHENTICATED ENCRYPTION. The mode set is exactly ECB, CBC and CFB
    /// [enums.sru:L943-L945], with no associated-data parameter and no tag output anywhere in the
    /// thirty-two symmetric declarations, so A DECRYPT CANNOT DETECT TAMPERING. Adding a mode that
    /// could would change the wire format, because a tag has to be carried somewhere. Integrity, where
    /// a caller needs it, is a separate keyed-hash call over the cipher text using the surface the
    /// legacy already publishes at [n_crypto.sru:L23-L26].
    /// </para>
    /// <para>
    /// Both are asserted as CORRECT under constraint C-B. The tampering demonstration is deliberately
    /// included so the absence is concrete rather than a claim: the cipher text is altered, the decrypt
    /// either yields different bytes or fails on padding, and in NEITHER case is the caller told the
    /// data was modified.
    /// </para>
    /// </remarks>
    [Fact]
    public void NeitherKeyDerivationNorAuthenticatedEncryptionIsReachable()
    {
        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);
        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);

        // No derivation parameter exists to be passed on any member of any provider.
        Type[] providers =
        [
            typeof(EncodingProvider),
            typeof(HashProvider),
            typeof(HmacProvider),
            typeof(SymmetricCipherProvider),
            typeof(RsaProvider),
        ];

        string[] absentParameterNames = ["salt", "iterations", "iterationcount", "workfactor", "tag"];

        foreach (Type provider in providers)
        {
            foreach (MethodInfo method in DeclaredSurface(provider))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.DoesNotContain(
                        parameter.Name?.ToLowerInvariant() ?? string.Empty,
                        absentParameterNames);
                }
            }
        }

        // Tampering is undetectable: the surface has no way to report it.
        CipherMaterial material = CreateCipherMaterial((ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256);
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(CipherPayloadText);

        byte[] cipher = _cipher.SymEncrypt(
            plainBytes,
            material.BinaryKey,
            material.BinaryIv,
            material.CipherType,
            Enums.CRYPTO_SYMCRYPT_MODE_CBC);

        byte[] tampered = [.. cipher];
        tampered[0] ^= 0xFF;

        try
        {
            byte[] recovered = _cipher.SymDecrypt(
                tampered,
                material.BinaryKey,
                material.BinaryIv,
                material.CipherType,
                Enums.CRYPTO_SYMCRYPT_MODE_CBC);

            // Silent corruption: plausible-looking bytes, and no signal at all that they are wrong.
            Assert.NotEqual(plainBytes, recovered);
        }
        catch (CryptographicException)
        {
            // Or a padding failure, which reports a malformed block and NOT that the data was
            // modified - the caller still cannot distinguish tampering from corruption.
        }
    }
}
