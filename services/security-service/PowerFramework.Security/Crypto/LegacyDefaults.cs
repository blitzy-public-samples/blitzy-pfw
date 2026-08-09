// ==============================================================================================
//  LegacyDefaults - the cryptographic default catalogue and substitution decision record
//  --------------------------------------------------------------------------------------------
//  CATALOGUES     ws_objects/pfw.crypto.pbl.src/n_crypto.sru
//                 86 lines, carrying exactly 65 `public function` declarations at L9-L73 and NO
//                 IMPLEMENTATION BODY ANYWHERE IN THE REPOSITORY. L8 declares
//                 `global type n_crypto from nonvisualobject native "pfw.dll"`, so every one of
//                 those 65 behaviours lives inside a closed native binary for which no C++ source
//                 exists in this repository or anywhere else it can be read from.
//
//  ORACLE STATUS  That .sru, and every other ws_objects/** path cited in this file, is READ ONLY.
//                 It is the behavioural oracle for parity testing, never an edit target. Every
//                 locator below is provenance for a decision and nothing more: no legacy file is
//                 edited, moved, reformatted or re-exported by this port.
//
//  WHY THIS FILE EXISTS
//  --------------------------------------------------------------------------------------------
//  Because the DLL is closed, every member of this folder is a SUBSTITUTION against
//  System.Security.Cryptography rather than a translation of legacy code. A substitution requires a
//  decision, and six providers each making the same decision independently is precisely how six
//  providers come to disagree. This file is therefore the single place where:
//
//      1. every WEAK LEGACY DEFAULT is reproduced as the default and annotated at one point of
//         reproduction, so that a future reader cannot mistake it for an implementation error;
//      2. every provider validates algorithm identifiers against ONE legacy-exact allowed set
//         rather than six divergent ones;
//      3. every substitution the closed binary forces on us is written down once, with its reason
//         and its evidence, rather than being rediscovered six times.
//
//  WHAT THIS FILE IS NOT
//  --------------------------------------------------------------------------------------------
//  It performs NO CRYPTOGRAPHY. There is no cipher, no digest, no signature, no key generation and
//  no random source here.
//
//  It holds NO KEY MATERIAL OF ANY KIND - no key, no initialization vector, no passphrase, no
//  certificate, no PEM block and no credential, whether as a value, as a default argument, as an
//  example in documentation or as sample data. The single all-zero initialization vector this file
//  admits to is produced by a computation over a length (see DECISION D3); it is never written
//  down as a literal. Nothing from any of the repository's known hardcoded-secret sites is
//  reproduced here in any form. Those sites are read as reference only, and their remediation
//  posture is never-replicate-document-and-rotate, never edit-the-legacy-file.
//
//  It is a `static class` with no mutable state, no constructor, no interface and no lifetime, so
//  it takes no part in dependency injection. Where the service composition root speaks of
//  registering the providers of this folder, read that as SIX DI-REGISTERED INSTANCE PROVIDERS
//  PLUS THIS STATIC CATALOGUE. It registers no route and opens no path.
//
//  THE 30 LEGACY CONSTANTS ARE REFERENCED, NEVER REDECLARED
//  --------------------------------------------------------------------------------------------
//  ws_objects/pfw.shared.pbl.src/enums.sru carries exactly 30 CRYPTO_* constants, inside the block
//  delimited by `/*--- Crypto ---*/` at L921 and `/*--- End Crypto ---*/` at L969. A second,
//  stray `/*--- End Crypto ---*/` marker appears far below at L999; the authoritative block ends
//  at L969, and nothing between L970 and L999 is a CRYPTO_* constant - L971 opens an unrelated
//  region. All 30 are already ported into `Enums` in the shared kernel with their SCREAMING_SNAKE
//  spellings preserved verbatim.
//
//  This file REFERENCES those 30 and never redeclares, re-literals or shadows any of them, which
//  is why no identifier declared below begins with the `CRYPTO_` prefix: every `CRYPTO_*`
//  occurrence in this file is either an `Enums.`-qualified reference or documentation text. Each
//  default arm below therefore takes its VALUE FROM the corresponding `Enums` member rather than
//  from a re-typed literal, so a future edit to the catalogue cannot silently desynchronise this
//  file - and a unit test asserts exactly that equality for each arm.
//
//  The declared PowerBuilder widths differ by group and drive the C# types, following the same
//  mapping the shared kernel uses:
//
//      PB Long   (32-bit signed)     -> C# long     the 20 encoding, hash, cipher-type,
//                                                   cipher-mode and RSA-padding constants
//      PB Ulong  (32-bit unsigned)   -> C# uint     the 4 random-string and 3 GUID flag constants
//      PB Uint   (16-bit unsigned)   -> C# ushort   the 3 RSA key-size constants
//
//  THE 65 DECLARATIONS, RECONCILED DECLARATION BY DECLARATION
//  --------------------------------------------------------------------------------------------
//  A census is only useful if a reader can re-derive it, so here is the entire surface with the
//  line range of each group and the sibling that owns it. 63 are ported; 2 are deliberately not.
//
//      n_crypto.sru   declaration group                    count   owner
//      ------------   ---------------------------------    -----   ------------------------------
//      L9             Copyright                                1   NOT PORTED - see below
//      L10            GetVersion                               1   NOT PORTED - see below
//      L11-L13        StringToBlob, BlobToString,
//                     BlobReverse                              3   EncodingProvider
//      L14-L18        GenRandomBlob, GenRandomString x2,
//                     GenGUID x2                               5   RandomProvider
//      L19-L20        GenRSAKey x2                             2   RsaProvider
//      L21-L22        Hash, unkeyed x2                         2   HashProvider
//      L23-L26        Hash, keyed x4                           4   HmacProvider
//      L27            HashFile, unkeyed                        1   HashProvider
//      L28-L29        HashFile, keyed x2                       2   HmacProvider
//      L30-L45        SymEncrypt x16                          16   SymmetricCipherProvider
//      L46-L61        SymDecrypt x16                          16   SymmetricCipherProvider
//      L62-L65        RSAEncrypt x4                            4   RsaProvider
//      L66-L69        RSADecrypt x4                            4   RsaProvider
//      L70-L71        RSASign x2                               2   RsaProvider
//      L72-L73        VerifyRSASign x2                         2   RsaProvider
//                                                          -----
//                                                             65   = 2 non-ported + 63 ported
//
//  The same 63 counted by owner, which is the check that no declaration was assigned twice or
//  dropped: EncodingProvider 3, RandomProvider 5, HashProvider 3, HmacProvider 6,
//  SymmetricCipherProvider 32, RsaProvider 14. 3 + 5 + 3 + 6 + 32 + 14 = 63.
//
//  THE SHAPE OF THE 32 SYMMETRIC OVERLOADS, WHICH IS WHY DECISION D3 EXISTS
//  --------------------------------------------------------------------------------------------
//  SymEncrypt is 16 overloads at L30-L45; SymDecrypt mirrors it exactly at L46-L61. Each set of 16
//  is the cross product of a {string, blob} payload, a {string, blob} key and the four argument
//  shapes below. Two structural facts fall straight out of reading them:
//
//      * NO OVERLOAD ANYWHERE TAKES A PADDING ARGUMENT. Block padding is not selectable, and the
//        changelog states it is PKCS#5 only [logfile.md:L1377].
//      * THE IV TYPE ALWAYS FOLLOWS THE KEY TYPE. A string key pairs only with a string IV and a
//        blob key only with a blob IV; there is no mixed-type overload. Blob key and IV parameters
//        were a later addition [logfile.md:L1048].
//
//      argument shape             SymEncrypt          SymDecrypt          consequence
//      -----------------------    ----------------    ----------------    -----------------------
//      no IV, no mode             L30 L34 L38 L42     L46 L50 L54 L58     runs in the ECB default
//      no IV, WITH mode           L31 L35 L39 L43     L47 L51 L55 L59     DECISION D3 applies
//      WITH IV, no mode           L32 L36 L40 L44     L48 L52 L56 L60     runs in the ECB default
//      WITH IV, WITH mode         L33 L37 L41 L45     L49 L53 L57 L61     fully specified
//
//  The second row is the awkward one, and it is not a rare branch: eight of the thirty-two accept
//  a mode but no IV, so a caller may ask for CBC or CFB - both of which require an IV - without
//  supplying one. An IV parameter was only added to the family later [logfile.md:L1343], which is
//  why the mode-without-IV shape exists at all.
//
//  ONE MORE SHAPE FACT, BECAUSE IT IS ASYMMETRIC AND SO IS EASY TO MISS. The signature-verification
//  pair does NOT follow the payload type uniformly the way the cipher family does: the signature
//  parameter changes type across the two overloads, `readonly string sign` at n_crypto.sru:L72 and
//  `readonly blob sign` at :L73. So the string-shaped verify takes an encoded signature - the
//  Base64 payload of DECISION D4 - while the blob-shaped verify takes raw signature bytes, and the
//  data parameter varies with it. A single verify implementation taking one signature type would
//  silently mis-handle one of the two.
//
//  ==============================================================================================
//  THE FOUR SUBSTITUTION DECISIONS
//  ==============================================================================================
//  These four behaviours are UNOBSERVABLE from the repository, because the code that implemented
//  them is inside the closed binary. Each is therefore recorded here as a decision with a reason,
//  is applied uniformly by all six siblings, and - this part matters - is NOT claimed as verified.
//  Byte-exact parity for any of the four is assertable only against the behavioural oracle, by
//  capturing the legacy output for a workflow and comparing it with the target output for the same
//  workflow. Until such a capture exists, each of these is a reasoned choice, not a proven one.
//
//  DECISION D1 - THERE IS NO KEY-DERIVATION FUNCTION ANYWHERE, SO A PASSPHRASE IS RAW KEY BYTES
//  --------------------------------------------------------------------------------------------
//  Nothing in the 65 declarations reaches a key-derivation function. There is no PBKDF2, no
//  scrypt, no bcrypt and no Argon2, no iteration-count parameter, and no salt concept of any kind:
//  the symmetric family's whole parameter list is payload, key, optional IV, cipher type and
//  optional mode. A `readonly string key` is consequently used AS KEY BYTES DIRECTLY.
//
//  This is materially weaker than a derived key. A short or low-entropy passphrase becomes a short
//  or low-entropy key with no stretching whatever, and two deployments choosing the same
//  passphrase get the same key. It is preserved deliberately: correcting it would change every
//  ciphertext the legacy ever produced, so no existing value could be decrypted by this port.
//
//  The rule the siblings follow, expressed once as `KeyMaterialEncoding` and
//  `NormalizeKeyMaterial` below so that six providers cannot implement six variants of it:
//
//      encoding      UTF-8, via the shared-framework read-only encoding whose `GetBytes` never
//                    emits a byte-order mark, so no BOM can leak into key material. UTF-8 is
//                    chosen because it is byte-identical to the legacy's ANSI path across the
//                    ASCII range - which is what every key and IV field in the oracle's own demo
//                    is typed into - while PowerBuilder's ANSI conversion is code-page and locale
//                    dependent and therefore not reproducible in a Linux container at all.
//                    Non-ASCII key material is the one case where the two could diverge, and that
//                    case is oracle-only verifiable.
//      too long      TRUNCATE to the required length, keeping the leading bytes.
//      too short     RIGHT-PAD with zero bytes to the required length.
//
//  Both length rules are themselves weaknesses and are preserved as such. Truncation silently
//  discards entropy the caller believed it had supplied; zero-padding silently manufactures key
//  bytes the caller never chose. Neither is corrected, and neither is hidden.
//
//  DECISION D2 - THERE IS NO AUTHENTICATED ENCRYPTION, SO CIPHERTEXT CARRIES NO INTEGRITY TAG
//  --------------------------------------------------------------------------------------------
//  The cipher-mode set is exactly ECB, CBC and CFB [enums.sru:L943-L945]. No AEAD mode is
//  reachable: no GCM, no CCM, no ChaCha20-Poly1305, and there is no associated-data parameter and
//  no tag output anywhere in the 32 symmetric overloads. A decrypt therefore CANNOT DETECT
//  TAMPERING. Ciphertext modified in transit decrypts to garbage, or - with a padding oracle - to
//  something an attacker chose, and the API has no way to say so.
//
//  Adding an AEAD mode would change the wire format, because a tag has to be carried somewhere,
//  and every ciphertext the legacy produced would stop round-tripping. It is therefore forbidden,
//  not merely discouraged. Integrity, where a caller needs it, is a separate keyed-hash call over
//  the ciphertext using the HMAC surface the legacy already publishes at L23-L26.
//
//  DECISION D3 - THE EIGHT MODE-WITHOUT-IV OVERLOADS USE AN ALL-ZERO IV OF THE BLOCK LENGTH
//  --------------------------------------------------------------------------------------------
//  Four of the sixteen SymEncrypt overloads accept a mode but no IV [n_crypto.sru:L31, L35, L39,
//  L43], mirrored by four of the sixteen SymDecrypt overloads [L47, L51, L55, L59] - eight of the
//  thirty-two. CBC and CFB both require an IV, so when one of those eight is called with CBC or
//  CFB an IV must come from somewhere. The conventional legacy behaviour, and what this port does,
//  is an ALL-ZERO IV OF THE CIPHER'S BLOCK LENGTH, produced by `CreateZeroInitializationVector`
//  below from a length alone - a computed buffer, never a literal.
//
//  This is a weak default and is annotated as one. A fixed, publicly known IV destroys CBC's
//  semantic security: identical plaintexts under the same key produce identical ciphertexts, so an
//  observer learns when a value has not changed, and the first block leaks equality just as ECB
//  does. It is preserved because the alternative - inventing a random IV - would produce
//  ciphertext the legacy could not decrypt, there being no field in which to transmit it.
//
//  The ECB arms are untouched by this decision: ECB uses no IV at all, so the four no-IV-no-mode
//  and four IV-but-no-mode shapes run in the default mode and never consult it.
//
//  DECISION D4 - THE STRING-SHAPED OVERLOADS CARRY A BASE64 TEXT-SAFE PAYLOAD, UNIFORMLY
//  --------------------------------------------------------------------------------------------
//  This is the folder's single most load-bearing substitution, and it is made once, here, and
//  consumed by HashProvider, HmacProvider, SymmetricCipherProvider and RsaProvider alike.
//
//  It is REQUIRED rather than optional, and the oracle's own demo proves it. There, string-shaped
//  ciphertext is written straight back into a plain multi-line text control and then fed to its
//  string-shaped inverse from that same control: SymEncrypt at
//  u_cst_tabpage_utility_crypto.sru:L504 pairs with SymDecrypt at :L547 under DES/CBC, :L607 with
//  :L592 under AES256/CBC, and :L637 with :L622 under 3DES/CBC; RSAEncrypt at :L744 pairs with
//  RSADecrypt at :L720; and RSASign at :L470 pairs with VerifyRSASign at :L474. A raw-byte payload
//  stuffed into a string could not survive that round trip - arbitrary cipher bytes are not valid
//  text in any encoding, and the control would mangle them.
//
//  THE RULE. Every string-shaped overload returns a printable, text-safe encoded payload and its
//  string-shaped inverse accepts that same encoding. Every blob-shaped overload carries RAW BYTES
//  and performs no encoding at all. The encoding is BASE64, expressed below as
//  `STRING_PAYLOAD_ENCODING` valued from the legacy's own encoding constant, and it is applied
//  uniformly to hash digests, symmetric ciphertext, RSA ciphertext and RSA signatures.
//
//  Why Base64 and not hex, given both are declared:
//
//      * `CRYPTO_ENCODING_BASE64` is 0 and is declared first [enums.sru:L924], with
//        `CRYPTO_ENCODING_HEX` at 1 [L925]. Base64 is the surface's own zero-valued, primary
//        encoding, and the demo's Base64 controls likewise precede its hex controls
//        [u_cst_tabpage_utility_crypto.sru:L419 and :L434, before :L562 and :L577].
//      * The changelog records that the symmetric and HMAC algorithms were deliberately changed,
//        breaking compatibility with earlier versions, specifically to make CROSS-LANGUAGE
//        symmetric-encryption interoperability possible [logfile.md:L1377]. Base64 is the
//        interoperable text form for ciphertext; hex is not the convention there.
//      * It is single-line, compact and round-trip-safe for arbitrary bytes, which is exactly what
//        the text-control round trip above demands.
//
//  Stated honestly, because this is a decision and not a measurement: a LOWERCASE HEX digest is
//  the more common convention for MD5 and the SHA family specifically, so a hex digest is the most
//  plausible way this single uniform rule could turn out to be wrong for the three hash-shaped
//  groups. The closed DLL's actual spelling - Base64 against hex, upper against lower case,
//  padding, any line breaking - is unobservable from the repository for every one of the four
//  categories. It is recorded here as ONE decision precisely so that the oracle can adjudicate it
//  in ONE place and, if it is wrong, correct it in ONE place.
//
//  THE COROLLARY, WHICH IS EASY TO GET BACKWARDS. `BlobToString` and `StringToBlob` [L11-L12] are
//  a SEPARATE, CALLER-DRIVEN surface on EncodingProvider: the caller picks Base64 or hex per call
//  [u_cst_tabpage_utility_crypto.sru:L419, :L434, :L562, :L577]. `Hash` does NOT route through
//  them - md5.srf:L12 and sha256.srf:L12 return `n_crypto.Hash(...)` directly as a string with no
//  encoding call anywhere in sight. The digest's text encoding is INTERNAL to Hash, which is the
//  whole reason it needs a decision record rather than a parameter.
//
//  ==============================================================================================
//  THE TWO DELIBERATE NON-PORTS
//  ==============================================================================================
//  `Copyright()` at n_crypto.sru:L9 and `GetVersion()` at :L10 are DELIBERATELY NOT PORTED. Three
//  independent reasons, any one of which would be sufficient:
//
//      * They return the closed pfw.dll's own copyright and version strings. This port eliminates
//        the native dependency outright rather than wrapping it, so there is no binary left to ask
//        and nothing meaningful for either function to return.
//      * The published cryptographic service contract does not enumerate them, so no consumer can
//        observe their absence.
//      * Anything they could return in this port would be freshly invented rather than preserved,
//        which is the opposite of what a behaviour-preserving migration is for.
//
//  This record is the ENTIRE deliverable for them. There is no eighth provider file, no provider
//  member, no endpoint and no exception-throwing placeholder for either. The resulting census,
//  stated so the omission is auditable rather than silent: 65 declarations at L9-L73, of which
//  63 are ported across the six sibling providers and 2 are deliberately non-ported.
//
//  Recorded alongside them, because it is the other thing a reader will look for and not find:
//  the type-shadowing global auto-instance `global n_crypto n_crypto` at n_crypto.sru:L75, and the
//  lazy-construction idiom `if Not IsValid(n_crypto) then n_crypto = Create n_crypto` that guards
//  every call site through it [md5.srf:L11, base64encode.srf:L10, randomstring.srf:L14,
//  guid.srf:L14], HAVE NO ANALOGUE TO REPRODUCE. They exist because PowerBuilder resolves one flat
//  global namespace and offers no container: the instance is a global whose name collides with its
//  own type name, and every caller must therefore check it before use. In this port the equivalent
//  objects are DI-REGISTERED INSTANCE SERVICES with a container-managed lifetime - never globals,
//  never statics, and never lazily self-constructing. There is nothing to port, only a collision
//  to avoid.
//
//  One spelling divergence worth carrying rather than tidying away: L17-L18 declare `GenGUID`
//  while guid.srf:L15 calls `GenGuid`. PowerScript is case-insensitive, so both spellings name one
//  function. The C# member is `GenGuid`, and the divergent declaration spelling is annotated at
//  the provider that owns it.
// ==============================================================================================

using System.Text;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// The catalogue of preserved legacy cryptographic defaults, the legacy-exact sets of supported
/// algorithm identifiers, the cipher sizing table, and the substitution rules that the six
/// providers of this folder share.
/// </summary>
/// <remarks>
/// <para>
/// Every member is either a legacy default reproduced verbatim, a legacy-exact allowed set, or a
/// documented substitution the closed native binary forces. NOTHING here is a hardening: no
/// minimum key size is imposed, no key-derivation function is introduced, no authenticated
/// encryption mode is added, no salt is invented, and the default cipher mode is not upgraded.
/// Where hardening would be tempting, an annotation appears instead.
/// </para>
/// <para>
/// This type performs no cryptography and holds no key material. It has no state, so every member
/// is safe to use concurrently from any number of requests.
/// </para>
/// </remarks>
public static class LegacyDefaults
{
    // ==========================================================================================
    //  THE PRESERVED WEAK DEFAULT ARMS
    //  ORACLE  ws_objects/pfw.shared.pbl.src/enums.sru:L943-L967
    //  ------------------------------------------------------------------------------------------
    //  Each arm is a compile-time constant so that a sibling can use it in an optional-parameter
    //  position, and each takes its VALUE FROM the corresponding `Enums` member rather than from a
    //  re-typed literal. That is deliberate and load-bearing: it makes desynchronisation between
    //  this catalogue and the ported constant catalogue impossible to introduce silently, and a
    //  unit test asserts the equality of every arm against its source for the same reason.
    //
    //  None of these identifiers begins with `CRYPTO_`, because the 30 legacy constants are
    //  referenced and never redeclared. Reading `Enums.CRYPTO_...` on the right of every `=` below
    //  is the proof of that.
    // ==========================================================================================
    #region Preserved weak defaults - enums.sru:L943-L967

    /// <summary>
    /// The symmetric cipher mode used by every overload that omits one, which is ECB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY. Valued from
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT"/>, which resolves to
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_ECB"/> [enums.sru:L946].
    /// </para>
    /// <para>
    /// ECB encrypts each block independently, so it is deterministic per block: identical
    /// plaintext blocks under the same key produce identical ciphertext blocks. That leaks the
    /// structure of the plaintext - repetition, alignment and equality between values are all
    /// visible to an observer who never learns the key. It is not semantically secure and should
    /// not be chosen for new work.
    /// </para>
    /// <para>
    /// It is nevertheless preserved as the default and MUST NOT be changed to CBC. This is not a
    /// theoretical arm: ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru calls
    /// SymEncrypt(value, key, CRYPTO_SYMCRYPT_TYPE_AES256) with NO MODE AND NO IV at :L583 and
    /// :L615, matched by decrypts of the same three-argument shape at :L337, :L552 and :L992. Every
    /// value that path has ever written is ECB ciphertext, so promoting the default would make all
    /// of it undecryptable by this port.
    /// </para>
    /// </remarks>
    public const long SYMMETRIC_MODE_DEFAULT = Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT;

    /// <summary>
    /// The RSA padding used by every overload that omits one, which is PKCS#1 v1.5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY. Valued from
    /// <see cref="Enums.CRYPTO_RSA_PADDING_DEFAULT"/>, which resolves to
    /// <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> [enums.sru:L951], annotated in the source
    /// itself as RSA_PKCS1_PADDING [enums.sru:L949].
    /// </para>
    /// <para>
    /// PKCS#1 v1.5 encryption padding is vulnerable to adaptive chosen-ciphertext attacks of the
    /// Bleichenbacher family when a decrypting service distinguishes a padding failure from other
    /// failures. OAEP is the modern choice and IS declared [enums.sru:L950], so it REMAINS
    /// SELECTABLE - it is neither removed nor silently promoted to the default.
    /// </para>
    /// <para>
    /// The default arm is exercised: ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru
    /// calls RSAEncrypt(text, publicKey) at :L744 and RSADecrypt(text, privateKey) at :L720, both
    /// two-argument and therefore both taking this default.
    /// </para>
    /// </remarks>
    public const long RSA_PADDING_DEFAULT = Enums.CRYPTO_RSA_PADDING_DEFAULT;

    /// <summary>
    /// The character-class flags used by the random-string overload that omits them: digits and
    /// letters, with symbols excluded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valued from <see cref="Enums.CRYPTO_RNDSTRING_DEFAULT"/>, which is the sum of
    /// <see cref="Enums.CRYPTO_RNDSTRING_NUMBER"/> and
    /// <see cref="Enums.CRYPTO_RNDSTRING_ALPHABET"/> [enums.sru:L957].
    /// </para>
    /// <para>
    /// <see cref="Enums.CRYPTO_RNDSTRING_SYMBOL"/> is DELIBERATELY EXCLUDED, exactly as
    /// ws_objects/pfw.crypto.pbl.src/randomstring.srf:L11 composes it - that line spells the sum
    /// out as NUMBER + ALPHABET rather than naming the default constant, and arrives at the same
    /// value. Narrowing the alphabet reduces the entropy per character of any generated string, so
    /// a caller who needs a given strength must ask for more characters rather than assume symbols
    /// are present. The exclusion is preserved, not corrected.
    /// </para>
    /// <para>
    /// No validation is imposed on a caller-supplied flag value. These flags are an ADDITIVE
    /// BITMASK, and how the closed binary treats zero or an unrecognised bit is unobservable from
    /// the repository; rejecting such a value here would narrow the legacy contract on a guess.
    /// </para>
    /// </remarks>
    public const uint RANDOM_STRING_FLAGS_DEFAULT = Enums.CRYPTO_RNDSTRING_DEFAULT;

    /// <summary>
    /// The formatting flags used by the GUID overload that omits them: bracketed and separated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valued from <see cref="Enums.CRYPTO_GUID_DEFAULT"/>, which is the sum of
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_BRACKET"/> and
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_SEPARATOR"/> [enums.sru:L962] - so the default form
    /// carries BOTH the surrounding brackets AND the group separators, exactly as
    /// ws_objects/pfw.crypto.pbl.src/guid.srf:L11 composes it.
    /// </para>
    /// <para>
    /// This is a formatting default rather than a security one, but it is preserved with the same
    /// discipline: the bracketed, separated spelling travels in stored values and log records, so
    /// changing the default would alter text that existing data already contains.
    /// </para>
    /// <para>
    /// As with the random-string flags, no validation is imposed on a caller-supplied value: the
    /// flags are an additive bitmask whose handling of an unknown bit is unobservable.
    /// </para>
    /// </remarks>
    public const uint GUID_FLAGS_DEFAULT = Enums.CRYPTO_GUID_DEFAULT;

    /// <summary>
    /// The text-safe encoding every string-shaped overload uses for its binary payload, which is
    /// Base64. See DECISION D4 in this file's header for the full rule and its evidence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Valued from <see cref="Enums.CRYPTO_ENCODING_BASE64"/> [enums.sru:L924], the legacy's own
    /// zero-valued and first-declared encoding. Applied UNIFORMLY to hash digests, symmetric
    /// ciphertext, RSA ciphertext and RSA signatures, so that a string-shaped result always feeds
    /// its string-shaped inverse. Blob-shaped overloads carry raw bytes and encode nothing.
    /// </para>
    /// <para>
    /// This is a substitution decision, not a measured fact: the spelling the closed binary
    /// actually produced is unobservable from the repository, and a lowercase hex digest is the
    /// more common convention for the hash-shaped groups specifically. It is centralised here so
    /// that the behavioural oracle can adjudicate it in one place and, if it is wrong, it can be
    /// corrected in one place.
    /// </para>
    /// </remarks>
    public const long STRING_PAYLOAD_ENCODING = Enums.CRYPTO_ENCODING_BASE64;

    /// <summary>
    /// The smallest RSA key size the legacy publishes as a predefined value, which is 1024 bits
    /// and which REMAINS LEGAL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY. Valued from
    /// <see cref="Enums.CRYPTO_RSA_BITS_1024"/> [enums.sru:L965]. A 1024-bit RSA modulus is below
    /// every current recommendation and is exercised by the oracle's own demo, which calls
    /// GenRSAKey(1024, ...) at
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L699.
    /// </para>
    /// <para>
    /// NO MINIMUM-SIZE GUARD IS IMPOSED ANYWHERE, and adding one would be a behavioural change of
    /// exactly the kind this port forbids. Note also what the three published sizes are NOT: the
    /// source calls them RSA PREDEFINE BITS [enums.sru:L964] and the key-generation declaration
    /// takes a plain unsigned integer [n_crypto.sru:L19-L20], so 1024, 2048 and 4096 are
    /// CONVENIENCE VALUES, NOT AN ALLOWED SET. This catalogue therefore deliberately publishes no
    /// key-size predicate: any predicate would narrow a contract that accepts any size.
    /// </para>
    /// </remarks>
    public const ushort RSA_SMALLEST_LEGAL_KEY_SIZE_BITS = Enums.CRYPTO_RSA_BITS_1024;

    #endregion

    // ==========================================================================================
    //  CAPABILITY FACTS - THE WEAKNESSES, MADE MACHINE-CHECKABLE
    //  ------------------------------------------------------------------------------------------
    //  The five constants below assert, in a form a unit test can read, the absences that the
    //  decision records above describe in prose. They exist so that each weakness is auditable
    //  rather than merely documented, and so that a sibling that needs to explain a refusal has
    //  exactly one member to point at instead of restating the reasoning in an error string.
    //
    //  Every one of them is `false`, and every one of them must STAY `false`. Flipping any single
    //  value here would not fix a weakness; it would merely make this file disagree with the six
    //  providers and with the published contract.
    // ==========================================================================================
    #region Capability facts - the preserved absences

    /// <summary>
    /// Whether any key-derivation function is reachable through the legacy surface. It is not.
    /// See DECISION D1.
    /// </summary>
    /// <remarks>
    /// No PBKDF2, scrypt, bcrypt or Argon2, no iteration count and no salt concept appears
    /// anywhere in the 65 declarations at n_crypto.sru:L9-L73. A passphrase is therefore used as
    /// raw key bytes by <see cref="NormalizeKeyMaterial(string, int)"/>. Preserved deliberately:
    /// introducing a derivation function would change every key, and so every ciphertext, the
    /// legacy ever produced.
    /// </remarks>
    public const bool KEY_DERIVATION_AVAILABLE = false;

    /// <summary>
    /// Whether any authenticated-encryption mode is reachable through the legacy surface. It is
    /// not, so ciphertext carries no integrity tag. See DECISION D2.
    /// </summary>
    /// <remarks>
    /// The mode set is exactly ECB, CBC and CFB [enums.sru:L943-L945]; there is no GCM, CCM or
    /// Poly1305, no associated-data parameter and no tag output in any of the 32 symmetric
    /// overloads. A decrypt cannot detect tampering. Adding an AEAD mode would change the wire
    /// format and is forbidden; integrity is obtained instead from a separate keyed-hash call over
    /// the ciphertext using the HMAC surface at n_crypto.sru:L23-L26.
    /// </remarks>
    public const bool AUTHENTICATED_ENCRYPTION_AVAILABLE = false;

    /// <summary>
    /// Whether the block padding scheme is selectable by a caller. It is not: it is fixed to the
    /// PKCS#5 and PKCS#7 family.
    /// </summary>
    /// <remarks>
    /// Two independent confirmations. Structurally, NO padding parameter appears in any of the 32
    /// symmetric overloads at n_crypto.sru:L30-L61 - the whole parameter list is payload, key,
    /// optional IV, cipher type and optional mode. Documentarily, the changelog states outright
    /// that SymEncrypt and SymDecrypt support PKCS5 padding only [logfile.md:L1377]. Because PKCS#5
    /// and PKCS#7 agree for every 8-byte block cipher and PKCS#7 generalises it to the 16-byte AES
    /// block, one padding implementation satisfies all five cipher types.
    /// </remarks>
    public const bool BLOCK_PADDING_SELECTABLE = false;

    /// <summary>
    /// Whether RSA no-padding is a supported value. It is NOT - this is the single member the RSA
    /// provider points at when refusing such a request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a PRESERVED LEGACY REFUSAL, not a hardening decision taken by this port. The legacy
    /// declares exactly two padding values, PKCS#1 at 0 and OAEP at 1 [enums.sru:L949-L950]; there
    /// is no constant for no-padding, and the legacy rejects a request for it. This port rejects it
    /// for the same reason and no other, so the refusal must not be described to a caller as a
    /// security improvement.
    /// </para>
    /// <para>
    /// A caller's padding argument is screened by <see cref="IsSupportedRsaPadding"/>, which is the
    /// gate; this constant is the reason, kept beside it so the two cannot drift apart.
    /// </para>
    /// </remarks>
    public const bool RSA_NO_PADDING_SUPPORTED = false;

    /// <summary>
    /// Whether this port enforces a minimum RSA key size. It does not.
    /// </summary>
    /// <remarks>
    /// The legacy imposes no minimum: key generation takes a plain unsigned integer
    /// [n_crypto.sru:L19-L20] and 1024 bits is used by the oracle's own demo at
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L699. Imposing a floor here
    /// would reject input the legacy accepted, which is a behavioural change and therefore
    /// forbidden. See <see cref="RSA_SMALLEST_LEGAL_KEY_SIZE_BITS"/>.
    /// </remarks>
    public const bool RSA_MINIMUM_KEY_SIZE_ENFORCED = false;

    #endregion


    // ==========================================================================================
    //  SUPPORTED-VALUE PREDICATES - THE LEGACY IDENTIFIER SETS, EXACTLY
    //  ORACLE  ws_objects/pfw.shared.pbl.src/enums.sru:L924-L945, L949-L950
    //  ------------------------------------------------------------------------------------------
    //  These are the shared allowed sets referred to in this file's opening rationale: one place
    //  where every provider screens an incoming algorithm identifier, so that six providers cannot
    //  each accept a slightly different set.
    //
    //  THE SETS ARE PRESERVED EXACTLY. NOTHING MAY BE ADDED AND NOTHING MAY BE REMOVED:
    //
    //      hash types      MD5, SHA1, SHA256, SHA384, SHA512, CRC32   0-5   [L928-L933]
    //      cipher types    DES, 3DES, AES128, AES192, AES256          0-4   [L936-L940]
    //      cipher modes    ECB, CBC, CFB                             0-2   [L943-L945]
    //      RSA padding     PKCS1, OAEP                               0-1   [L949-L950]
    //      encodings       Base64, hex                               0-1   [L924-L925]
    //
    //  Adding a value would publish an operation the legacy never offered, so there is no GCM, no
    //  CCM, no ChaCha20-Poly1305, no SHA-3 and no BLAKE2 here. Removing a value would reject input
    //  the legacy accepted, so DES, 3DES, MD5, SHA1, ECB and PKCS#1 all STAY - every one of them is
    //  weak by current standards, and every one of them remains selectable. CRC32 stays too, and it
    //  is worth flagging that CRC32 is a CHECKSUM AND NOT A CRYPTOGRAPHIC HASH: it offers no
    //  collision resistance whatever. It is offered only because the legacy offers it, and it is
    //  genuinely exercised, through HashFile at
    //  ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L281.
    //
    //  Every predicate is pure, total and side-effect free: it answers for any value of its
    //  parameter type without throwing, so a provider can screen input before doing any work and
    //  map a `false` onto the legacy-shaped invalid-argument return code itself.
    //
    //  DELIBERATELY ABSENT, AND THE ABSENCES ARE THE POINT:
    //      * no RSA key-size predicate - the three published sizes are convenience values rather
    //        than an allowed set, so any predicate would narrow the contract. See
    //        RSA_SMALLEST_LEGAL_KEY_SIZE_BITS.
    //      * no random-string or GUID flag predicate - those are additive bitmasks whose handling
    //        of an unknown bit is unobservable, so validating them would rest on a guess.
    // ==========================================================================================
    #region Supported-value predicates - enums.sru:L924-L950

    /// <summary>
    /// Reports whether <paramref name="encoding"/> is one of the two encodings the legacy
    /// publishes for the blob-to-text conversions.
    /// </summary>
    /// <param name="encoding">The candidate encoding identifier.</param>
    /// <returns>
    /// <see langword="true"/> for Base64 (0) and hex (1); otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The set is exactly <see cref="Enums.CRYPTO_ENCODING_BASE64"/> and
    /// <see cref="Enums.CRYPTO_ENCODING_HEX"/> [enums.sru:L924-L925]. This screens the
    /// caller-driven conversion surface at n_crypto.sru:L11-L12, which is a DIFFERENT concern from
    /// the internal payload encoding of the string-shaped overloads - see DECISION D4 and
    /// <see cref="STRING_PAYLOAD_ENCODING"/>.
    /// </remarks>
    public static bool IsSupportedEncoding(long encoding) =>
        encoding == Enums.CRYPTO_ENCODING_BASE64 ||
        encoding == Enums.CRYPTO_ENCODING_HEX;

    /// <summary>
    /// Reports whether <paramref name="ntype"/> is one of the six hash algorithm identifiers the
    /// legacy publishes.
    /// </summary>
    /// <param name="ntype">The candidate hash type identifier.</param>
    /// <returns>
    /// <see langword="true"/> for MD5 (0), SHA1 (1), SHA256 (2), SHA384 (3), SHA512 (4) and CRC32
    /// (5); otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The set runs from <see cref="Enums.CRYPTO_HASH_MD5"/> to
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/> [enums.sru:L928-L933].
    /// </para>
    /// <para>
    /// THIS ONE PREDICATE SERVES THREE PROVIDERS, and that is the legacy's own arrangement rather
    /// than a convenience: the source comment at enums.sru:L927 records that the hash-type set is
    /// the parameter set for Hash, RSASign AND VerifyRSASign alike. So the unkeyed hash provider,
    /// the keyed HMAC provider and the RSA provider's SIGNATURE ALGORITHM all screen against this
    /// same set - the RSA provider must not grow a separate one. The signature path is exercised
    /// with SHA256 at ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L470 and :L474.
    /// </para>
    /// <para>
    /// MD5 and SHA1 are both accepted. Both are broken for collision resistance and neither may be
    /// removed. CRC32 is accepted and is not a cryptographic hash at all; a caller that reaches for
    /// it for an integrity or signature purpose is making a mistake the legacy also permitted.
    /// </para>
    /// </remarks>
    public static bool IsSupportedHashType(long ntype) =>
        ntype >= Enums.CRYPTO_HASH_MD5 && ntype <= Enums.CRYPTO_HASH_CRC32;

    /// <summary>
    /// Reports whether <paramref name="ntype"/> is one of the five symmetric cipher identifiers the
    /// legacy publishes.
    /// </summary>
    /// <param name="ntype">The candidate cipher type identifier.</param>
    /// <returns>
    /// <see langword="true"/> for DES (0), 3DES (1), AES128 (2), AES192 (3) and AES256 (4);
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The set runs from <see cref="Enums.CRYPTO_SYMCRYPT_TYPE_DES"/> to
    /// <see cref="Enums.CRYPTO_SYMCRYPT_TYPE_AES256"/> [enums.sru:L936-L940].
    /// </para>
    /// <para>
    /// The parameter is a 16-bit unsigned integer because every one of the 32 symmetric overloads
    /// declares this argument as a PowerBuilder `uint` [n_crypto.sru:L30-L61], and a PowerBuilder
    /// `uint` is 16 bits wide, which maps to <see cref="ushort"/>. Note that the CONSTANTS
    /// themselves are declared `Long` in the constant catalogue, so the argument is narrower than
    /// the constants that name its legal values - a width mismatch present in the legacy and
    /// carried across rather than tidied away. Because the type is unsigned, no negative value can
    /// reach this predicate, so the lower bound needs no separate test.
    /// </para>
    /// <para>
    /// DES is accepted, with its 56 effective key bits, and so is two-key-equivalent 3DES. Neither
    /// may be removed.
    /// </para>
    /// </remarks>
    public static bool IsSupportedSymmetricType(ushort ntype) =>
        ntype <= Enums.CRYPTO_SYMCRYPT_TYPE_AES256;

    /// <summary>
    /// Reports whether <paramref name="mode"/> is one of the three cipher modes the legacy
    /// publishes.
    /// </summary>
    /// <param name="mode">The candidate cipher mode identifier.</param>
    /// <returns>
    /// <see langword="true"/> for ECB (0), CBC (1) and CFB (2); otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// The set is exactly <see cref="Enums.CRYPTO_SYMCRYPT_MODE_ECB"/>,
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_CBC"/> and
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_CFB"/> [enums.sru:L943-L945]. No authenticated mode is
    /// a member of it and none may be added - see
    /// <see cref="AUTHENTICATED_ENCRYPTION_AVAILABLE"/>. ECB is a member and stays one; it is also
    /// the default, for which see <see cref="SYMMETRIC_MODE_DEFAULT"/>.
    /// </remarks>
    public static bool IsSupportedSymmetricMode(long mode) =>
        mode >= Enums.CRYPTO_SYMCRYPT_MODE_ECB && mode <= Enums.CRYPTO_SYMCRYPT_MODE_CFB;

    /// <summary>
    /// Reports whether <paramref name="padding"/> is one of the two RSA padding schemes the legacy
    /// publishes.
    /// </summary>
    /// <param name="padding">The candidate RSA padding identifier.</param>
    /// <returns>
    /// <see langword="true"/> for PKCS#1 v1.5 (0) and OAEP (1); otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The set is exactly <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> and
    /// <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/> [enums.sru:L949-L950].
    /// </para>
    /// <para>
    /// NO-PADDING IS EXPLICITLY UNSUPPORTED and this predicate is where that shows up: the legacy
    /// declares no constant for it and rejects a request for it, so this port returns
    /// <see langword="false"/> for every value outside the two above. That refusal is PRESERVED
    /// LEGACY BEHAVIOUR rather than a hardening choice made here, and
    /// <see cref="RSA_NO_PADDING_SUPPORTED"/> is the member that records why, so a provider can
    /// cite the reason without paraphrasing it.
    /// </para>
    /// <para>
    /// PKCS#1 v1.5 is a member and stays one, and it is also the default - see
    /// <see cref="RSA_PADDING_DEFAULT"/>. OAEP is a member and must be neither removed nor
    /// promoted to the default.
    /// </para>
    /// </remarks>
    public static bool IsSupportedRsaPadding(long padding) =>
        padding == Enums.CRYPTO_RSA_PADDING_PKCS1 ||
        padding == Enums.CRYPTO_RSA_PADDING_OAEP;

    /// <summary>
    /// Reports whether <paramref name="mode"/> consumes an initialization vector, which CBC and
    /// CFB do and ECB does not.
    /// </summary>
    /// <param name="mode">The cipher mode identifier to classify.</param>
    /// <returns>
    /// <see langword="true"/> for CBC (1) and CFB (2); <see langword="false"/> for ECB (0) and for
    /// every value outside the published set.
    /// </returns>
    /// <remarks>
    /// <para>
    /// ECB USES NO INITIALIZATION VECTOR AT ALL, so an IV length is meaningful only for CBC and
    /// CFB. This predicate is what lets the cipher provider decide whether it needs one before it
    /// looks up a length, and it is the guard that keeps DECISION D3 confined to the arms that
    /// actually require it: an IV supplied to an ECB operation is unused, and the eight
    /// mode-without-IV overloads only need a synthesised IV when the mode they were handed is one
    /// of these two.
    /// </para>
    /// <para>
    /// An unpublished mode answers <see langword="false"/> rather than throwing, because screening
    /// the mode itself is <see cref="IsSupportedSymmetricMode"/>'s job and a classifier that threw
    /// would force every caller to order the two checks correctly.
    /// </para>
    /// </remarks>
    public static bool ModeUsesInitializationVector(long mode) =>
        mode == Enums.CRYPTO_SYMCRYPT_MODE_CBC ||
        mode == Enums.CRYPTO_SYMCRYPT_MODE_CFB;

    #endregion


    // ==========================================================================================
    //  THE CIPHER METRICS TABLE
    //  ------------------------------------------------------------------------------------------
    //  The cipher provider has to size key and IV material without embedding any, so the sizes live
    //  here as QUERYABLE DATA rather than as a switch buried inside that provider. Being data, the
    //  whole table can be enumerated and asserted row by row by a test; being here, it is also the
    //  one place the key-length rule of DECISION D1 and the zero-IV rule of DECISION D3 read their
    //  lengths from, so the three cannot disagree.
    //
    //  EVERY NUMBER BELOW IS AN ALGORITHM FACT, NOT A SECRET. A cipher's key length and block
    //  length are published properties of the algorithm; recording them carries no key material and
    //  engages no part of the no-hardcoded-secrets constraint.
    //
    //      cipher type   identifier   key bytes   block bytes   notes
    //      -----------   ----------   ---------   -----------   ------------------------------
    //      DES                    0           8             8   56 effective key bits
    //      3DES                   1          24             8   three 8-byte subkeys
    //      AES128                 2          16            16
    //      AES192                 3          24            16
    //      AES256                 4          32            16
    //
    //  The block length doubles as the IV length, because a CBC or CFB initialization vector is
    //  exactly one block wide. It is MEANINGFUL ONLY FOR CBC AND CFB: ECB consumes no IV at all, so
    //  for an ECB operation this column describes the padding granularity and nothing else. Use
    //  ModeUsesInitializationVector to decide whether an IV is wanted before reading a length.
    // ==========================================================================================
    #region Cipher metrics - enums.sru:L936-L940

    /// <summary>
    /// The single source of truth for the five rows, ordered by cipher type identifier.
    /// </summary>
    /// <remarks>
    /// A private array rather than a public one so that no caller can mutate a shared row, and the
    /// single source for both <see cref="AllSymmetricCipherMetrics"/> and the lookups below, so a
    /// row cannot be corrected in one place and missed in another. It is immutable constant data
    /// and not a cache: nothing is computed, evicted or refreshed, so it introduces no shared state
    /// that would constrain running any number of instances of this service.
    /// </remarks>
    private static readonly SymmetricCipherMetrics[] CipherMetricsTable =
    [
        new(Enums.CRYPTO_SYMCRYPT_TYPE_DES, KeyLengthBytes: 8, BlockLengthBytes: 8),
        new(Enums.CRYPTO_SYMCRYPT_TYPE_3DES, KeyLengthBytes: 24, BlockLengthBytes: 8),
        new(Enums.CRYPTO_SYMCRYPT_TYPE_AES128, KeyLengthBytes: 16, BlockLengthBytes: 16),
        new(Enums.CRYPTO_SYMCRYPT_TYPE_AES192, KeyLengthBytes: 24, BlockLengthBytes: 16),
        new(Enums.CRYPTO_SYMCRYPT_TYPE_AES256, KeyLengthBytes: 32, BlockLengthBytes: 16),
    ];

    /// <summary>
    /// The complete cipher metrics table, one row per published symmetric cipher type, ordered by
    /// cipher type identifier.
    /// </summary>
    /// <value>
    /// Exactly five rows: DES, 3DES, AES128, AES192 and AES256 [enums.sru:L936-L940].
    /// </value>
    /// <remarks>
    /// Exposed as a read-only list so that the table is enumerable - a test can assert every row,
    /// and a diagnostic surface can report the supported ciphers - without handing out a mutable
    /// array.
    /// </remarks>
    public static IReadOnlyList<SymmetricCipherMetrics> AllSymmetricCipherMetrics =>
        CipherMetricsTable;

    /// <summary>
    /// Attempts to obtain the key and block sizing for a symmetric cipher type. This is the
    /// non-throwing failure path a provider maps onto the legacy invalid-argument return code.
    /// </summary>
    /// <param name="ntype">
    /// The cipher type identifier, as the 16-bit unsigned value the legacy declares.
    /// </param>
    /// <param name="metrics">
    /// When this method returns <see langword="true"/>, the sizing for
    /// <paramref name="ntype"/>; otherwise the default value, which carries zero lengths.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="ntype"/> is a published cipher type; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This overload does not throw for an unsupported type, which is deliberate: a provider
    /// screening a caller's argument needs to turn a bad value into a legacy-shaped invalid-argument
    /// result rather than into an exception, because that is the shape the legacy surface reports
    /// failure in. Use <see cref="GetSymmetricCipherMetrics"/> instead when the type has already
    /// been screened and an unsupported value would be an internal defect.
    /// </remarks>
    public static bool TryGetSymmetricCipherMetrics(ushort ntype, out SymmetricCipherMetrics metrics)
    {
        // A linear scan over five rows, rather than indexing the table by identifier. The
        // identifiers happen to be contiguous from zero, so indexing would work today, but it would
        // silently depend on the array order matching the identifier order for ever. Matching on
        // the identifier itself cannot rot, and five comparisons cost nothing.
        foreach (SymmetricCipherMetrics candidate in CipherMetricsTable)
        {
            if (candidate.CipherType == ntype)
            {
                metrics = candidate;
                return true;
            }
        }

        metrics = default;
        return false;
    }

    /// <summary>
    /// Obtains the key and block sizing for a symmetric cipher type that has already been screened.
    /// </summary>
    /// <param name="ntype">
    /// The cipher type identifier, as the 16-bit unsigned value the legacy declares.
    /// </param>
    /// <returns>The sizing for <paramref name="ntype"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the five published cipher types.
    /// </exception>
    /// <remarks>
    /// The exception signals an INTERNAL DEFECT - a caller that reached this method without
    /// screening its input - and is not the channel through which a caller's bad argument is
    /// reported. For that, use <see cref="TryGetSymmetricCipherMetrics"/>, whose
    /// <see langword="false"/> result maps onto the legacy invalid-argument return code.
    /// </remarks>
    public static SymmetricCipherMetrics GetSymmetricCipherMetrics(ushort ntype)
    {
        if (!TryGetSymmetricCipherMetrics(ntype, out SymmetricCipherMetrics metrics))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ntype),
                ntype,
                "Not a published symmetric cipher type. The set is DES, 3DES, AES128, AES192 and " +
                "AES256, identifiers 0 to 4 [ws_objects/pfw.shared.pbl.src/enums.sru:L936-L940].");
        }

        return metrics;
    }

    #endregion


    // ==========================================================================================
    //  THE SUBSTITUTION RULES, MADE EXECUTABLE
    //  ------------------------------------------------------------------------------------------
    //  DECISION D1 and DECISION D3 in this file's header are not merely prose: each prescribes a
    //  rule that all six providers must apply IDENTICALLY, and a rule described in six comments is
    //  a rule implemented six ways. The three members below are those rules, written once.
    //
    //  They are pure functions over a length and a byte sequence. They hold no key material, they
    //  contain no literal that resembles key material, and they keep no state between calls: each
    //  returns a FRESH buffer, so no two operations can ever share one and nothing is cached.
    // ==========================================================================================
    #region Substitution rules - DECISION D1 and DECISION D3

    /// <summary>
    /// The encoding used to turn a string key, initialization vector or passphrase into bytes,
    /// which is UTF-8. See DECISION D1.
    /// </summary>
    /// <value>The shared framework's read-only UTF-8 encoding.</value>
    /// <remarks>
    /// <para>
    /// Chosen because it is byte-identical to the legacy's ANSI path across the ASCII range, which
    /// is what every key and initialization vector field in the oracle's own demo is typed into,
    /// whereas PowerBuilder's ANSI conversion is code-page and locale dependent and so is not
    /// reproducible in a Linux container at all. Non-ASCII key material is the one input on which
    /// the two could diverge, and that divergence is verifiable only against the behavioural
    /// oracle.
    /// </para>
    /// <para>
    /// This particular instance matters in two ways. It is READ ONLY, so no caller can reach
    /// through this property and alter the encoder or decoder fallback that every provider then
    /// inherits. And its byte conversion NEVER EMITS A BYTE-ORDER MARK - a BOM belongs to the
    /// preamble, which conversion does not consult - so no phantom leading bytes can enter key
    /// material and silently change a key.
    /// </para>
    /// </remarks>
    public static Encoding KeyMaterialEncoding => Encoding.UTF8;

    /// <summary>
    /// Applies the legacy key-length rule to a string key, initialization vector or passphrase:
    /// encode it as UTF-8, then truncate or zero-pad it to exactly the length the cipher requires.
    /// See DECISION D1.
    /// </summary>
    /// <param name="material">
    /// The caller-supplied key, initialization vector or passphrase text. May be empty, in which
    /// case the result is all zero bytes.
    /// </param>
    /// <param name="requiredLengthBytes">
    /// The exact length the cipher requires, taken from the cipher metrics table - the key length
    /// for a key, or the block length for an initialization vector.
    /// </param>
    /// <returns>A fresh buffer of exactly <paramref name="requiredLengthBytes"/> bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requiredLengthBytes"/> is zero or negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THERE IS NO KEY DERIVATION HERE, AND THAT IS THE POINT. No key-derivation function is
    /// reachable through the legacy surface and there is no salt concept, so a passphrase is used as
    /// RAW KEY BYTES - see <see cref="KEY_DERIVATION_AVAILABLE"/>. This is materially weaker than a
    /// derived key: a short or predictable passphrase yields a short or predictable key with no
    /// stretching whatever. It is preserved deliberately, because deriving a key instead would
    /// change every key, and therefore every ciphertext, the legacy ever produced.
    /// </para>
    /// <para>
    /// Both halves of the length rule are themselves weaknesses, and both are preserved:
    /// TRUNCATION of over-long material silently discards entropy the caller believed it had
    /// supplied, and ZERO-PADDING of over-short material silently manufactures key bytes the caller
    /// never chose. Neither is corrected and neither is hidden.
    /// </para>
    /// <para>
    /// OVERLOAD SELECTION, because there is one sharp edge here. This overload pairs with the span
    /// overload below, mirroring the legacy's own string-and-blob pairing across the 32 symmetric
    /// declarations. The ARGUMENT TYPE selects between them, and every real call site passes typed
    /// material - a string key resolves here, a byte array or span resolves to the span overload,
    /// and neither is ambiguous. A BARE <see langword="null"/> LITERAL is the one exception: under
    /// this language version's implicit span conversions it is applicable to both, so it does not
    /// compile. Pass a typed null instead, which is what a caller checking this guard means anyway.
    /// </para>
    /// </remarks>
    public static byte[] NormalizeKeyMaterial(string material, int requiredLengthBytes)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredLengthBytes);

        return NormalizeKeyMaterial(KeyMaterialEncoding.GetBytes(material), requiredLengthBytes);
    }

    /// <summary>
    /// Applies the legacy key-length rule to blob key or initialization vector material: truncate or
    /// zero-pad it to exactly the length the cipher requires. See DECISION D1.
    /// </summary>
    /// <param name="material">
    /// The caller-supplied key or initialization vector bytes. May be empty, in which case the
    /// result is all zero bytes.
    /// </param>
    /// <param name="requiredLengthBytes">
    /// The exact length the cipher requires, taken from the cipher metrics table.
    /// </param>
    /// <returns>A fresh buffer of exactly <paramref name="requiredLengthBytes"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requiredLengthBytes"/> is zero or negative.
    /// </exception>
    /// <remarks>
    /// This is the overload the blob-shaped key and initialization vector parameters use - blob key
    /// and IV support was a later addition to the legacy surface [logfile.md:L1048]. It applies the
    /// same rule as the string overload with the encoding step omitted, because blob material is
    /// already bytes. The two overloads share one implementation precisely so that a string key and
    /// the equivalent blob key can never be normalised differently.
    /// </remarks>
    public static byte[] NormalizeKeyMaterial(ReadOnlySpan<byte> material, int requiredLengthBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredLengthBytes);

        // The allocation is zero-filled by the runtime, which IS the zero-padding half of the rule;
        // copying only the leading bytes that fit IS the truncation half. Expressing both as one
        // sized allocation plus one bounded copy means neither half can be applied without the
        // other.
        byte[] normalized = new byte[requiredLengthBytes];
        int copiedLength = Math.Min(material.Length, requiredLengthBytes);
        material[..copiedLength].CopyTo(normalized);

        return normalized;
    }

    /// <summary>
    /// Produces the all-zero initialization vector that the eight mode-without-IV overloads use when
    /// the caller asks for CBC or CFB without supplying one. See DECISION D3.
    /// </summary>
    /// <param name="blockLengthBytes">
    /// The cipher's block length, taken from the cipher metrics table. An initialization vector is
    /// exactly one block wide.
    /// </param>
    /// <returns>A fresh, all-zero buffer of exactly <paramref name="blockLengthBytes"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="blockLengthBytes"/> is zero or negative.
    /// </exception>
    /// <remarks>
    /// <para>
    /// KNOWN WEAK DEFAULT, PRESERVED DELIBERATELY. Four of the sixteen SymEncrypt overloads accept
    /// a mode but no initialization vector [n_crypto.sru:L31, L35, L39, L43], mirrored by four of
    /// the sixteen SymDecrypt overloads [L47, L51, L55, L59]. CBC and CFB both require an
    /// initialization vector, so those eight arms must synthesise one, and this is it.
    /// </para>
    /// <para>
    /// A fixed, publicly known initialization vector destroys CBC's semantic security: the same
    /// plaintext under the same key produces the same ciphertext every time, so an observer learns
    /// when a value has not changed, and the first block leaks equality much as ECB does. It is
    /// preserved because the alternative - generating a random initialization vector - would produce
    /// ciphertext the legacy could not decrypt, there being no field in the legacy format in which
    /// to transmit one.
    /// </para>
    /// <para>
    /// The vector is COMPUTED FROM A LENGTH and is never written down as a literal, so this file
    /// contains nothing that resembles key material. The result is a fresh buffer on every call, so
    /// no two operations share one and a caller that overwrites it affects nothing else.
    /// </para>
    /// <para>
    /// The ECB arms never reach this method: ECB consumes no initialization vector at all. Screen
    /// the mode with <see cref="ModeUsesInitializationVector"/> first.
    /// </para>
    /// </remarks>
    public static byte[] CreateZeroInitializationVector(int blockLengthBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockLengthBytes);

        // Computed, never a literal: the runtime zero-fills a new array, so the all-zero vector is
        // produced by the allocation itself.
        return new byte[blockLengthBytes];
    }

    #endregion
}

/// <summary>
/// The published key and block sizing for one symmetric cipher type.
/// </summary>
/// <param name="CipherType">
/// The legacy cipher type identifier: DES (0), 3DES (1), AES128 (2), AES192 (3) or AES256 (4)
/// [ws_objects/pfw.shared.pbl.src/enums.sru:L936-L940]. Declared as a 64-bit signed value because
/// that is the width the constant catalogue declares these constants at.
/// </param>
/// <param name="KeyLengthBytes">
/// The exact key length the cipher requires, in bytes. Caller-supplied key material is brought to
/// this length by <see cref="LegacyDefaults.NormalizeKeyMaterial(string, int)"/>.
/// </param>
/// <param name="BlockLengthBytes">
/// The cipher's block length in bytes, which is also its initialization vector length and its
/// padding granularity.
/// </param>
/// <remarks>
/// <para>
/// Every value carried here is an ALGORITHM FACT AND NOT A SECRET: key length and block length are
/// published properties of DES, 3DES and AES. This type holds no key material, only sizes.
/// </para>
/// <para>
/// It is deliberately a top-level type rather than one nested inside
/// <see cref="LegacyDefaults"/>, so that a caller can name it without qualification and so that no
/// visible-nested-type diagnostic arises under this repository's warnings-as-errors gate.
/// </para>
/// </remarks>
public readonly record struct SymmetricCipherMetrics(
    long CipherType,
    int KeyLengthBytes,
    int BlockLengthBytes)
{
    /// <summary>
    /// The initialization vector length in bytes, which equals the block length.
    /// </summary>
    /// <value>
    /// The same value as <see cref="BlockLengthBytes"/>: a CBC or CFB initialization vector is
    /// exactly one block wide.
    /// </value>
    /// <remarks>
    /// MEANINGFUL ONLY FOR CBC AND CFB. ECB consumes no initialization vector at all, so for an ECB
    /// operation this value describes nothing the operation uses. Screen the mode with
    /// <see cref="LegacyDefaults.ModeUsesInitializationVector"/> before reading it.
    /// </remarks>
    public int IvLengthBytes => BlockLengthBytes;
}
