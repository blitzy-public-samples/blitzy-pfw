// ==============================================================================================
//  RsaProvider - the asymmetric half of the ported cryptographic surface
//  --------------------------------------------------------------------------------------------
//  LEGACY SOURCE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru
//                 L19-L20   GenRSAKey, two overloads
//                 L62-L65   RSAEncrypt, four overloads
//                 L66-L69   RSADecrypt, four overloads
//                 L70-L71   RSASign, two overloads
//                 L72-L73   VerifyRSASign, two overloads
//
//  THERE IS NO LEGACY BODY TO TRANSLATE. That file is 86 lines long, carries exactly 65
//  `public function` declarations at L9-L73, and contains NO IMPLEMENTATION ANYWHERE. Its L8
//  declares `global type n_crypto from nonvisualobject native "pfw.dll"`, so all 65 behaviours
//  live inside a closed native binary for which no C++ source exists in this repository or
//  anywhere it could be read from. EVERY MEMBER BELOW IS THEREFORE A SUBSTITUTION AGAINST
//  System.Security.Cryptography RATHER THAN A TRANSLATION, and every substitution that the
//  repository cannot settle is recorded as a named decision with its reason.
//
//  ORACLE STATUS. That .sru, and every other ws_objects/** path cited in this file, is READ ONLY:
//  it is the behavioural oracle for parity testing and never an edit target. Every locator here is
//  provenance for a decision and nothing more. No legacy file is edited, moved, reformatted or
//  re-exported by this port.
//
//  NO THIRD-PARTY CRYPTOGRAPHIC PACKAGE IS USED OR PERMITTED. The base class library's RSA type
//  covers every operation this surface publishes - key generation, key import and export in both
//  the armoured and the bare binary forms, encryption, decryption, signing and verification - so
//  the compile closure of this file is the BCL plus the shared kernel plus the two sibling files
//  in this folder, and nothing else.
//
//  ==============================================================================================
//  THE CENSUS: EXACTLY FOURTEEN MEMBERS, IN THE LEGACY'S OWN DECLARATION ORDER
//  ==============================================================================================
//  2 GenRSAKey + 4 RSAEncrypt + 4 RSADecrypt + 2 RSASign + 2 VerifyRSASign = 14. That figure is
//  load bearing rather than decorative: LegacyDefaults.cs reconciles the whole surface as 65
//  declarations = 63 ported + 2 deliberately non-ported, distributed as EncodingProvider 3,
//  RandomProvider 5, HashProvider 3, HmacProvider 6, SymmetricCipherProvider 32 and RsaProvider
//  14. A fifteenth member here, however convenient, breaks that reconciliation and publishes an
//  operation the legacy never offered.
//
//      locator   declaration                                                     returns
//      -------   ------------------------------------------------------------     ---------
//      L19       GenRSAKey(uint bits, ref string prikey, ref string pubkey)       boolean
//      L20       GenRSAKey(..., readonly boolean pemformat)                       boolean
//      L62       RSAEncrypt(readonly string plain, readonly string pubkey)        string
//      L63       RSAEncrypt(..., readonly long padding)                           string
//      L64       RSAEncrypt(readonly blob plain, readonly string pubkey)          blob
//      L65       RSAEncrypt(..., readonly long padding)                           blob
//      L66       RSADecrypt(readonly string cipher, readonly string prikey)       string
//      L67       RSADecrypt(..., readonly long padding)                           string
//      L68       RSADecrypt(readonly blob cipher, readonly string prikey)         blob
//      L69       RSADecrypt(..., readonly long padding)                           blob
//      L70       RSASign(readonly string data, readonly string prikey, ntype)     string
//      L71       RSASign(readonly blob data, readonly string prikey, ntype)       blob
//      L72       VerifyRSASign(string data, string sign, string pubkey, ntype)    boolean
//      L73       VerifyRSASign(blob data, blob sign, string pubkey, ntype)        boolean
//
//  THREE STRUCTURAL FACTS THAT ARE EASY TO GET WRONG, ALL READABLE IN THE TABLE ABOVE
//  --------------------------------------------------------------------------------------------
//  1. THE KEYS ARE ALWAYS TEXT. `prikey` and `pubkey` are declared `readonly string` in EVERY one
//     of the twelve non-generating overloads, INCLUDING the four whose payload is a blob. Only the
//     payload varies between text and bytes; a key never does. So there is no byte-shaped key
//     parameter to add and none may be added.
//
//  2. THE SIGNATURE PARAMETER'S TYPE VARIES, AND IT IS THE ONE ASYMMETRY IN THE FAMILY. It is
//     `readonly string sign` at L72 and `readonly blob sign` at L73, tracking the payload type
//     rather than following the keys. The string-shaped verify therefore accepts the encoded
//     signature that the string-shaped RSASign at L70 returns, and the blob-shaped verify accepts
//     the raw signature bytes that L71 returns. A single verify taking one signature type would
//     silently mis-handle one of the two, which is why both exist here.
//
//  3. GenRSAKey RETURNS A BOOLEAN AND WRITES THROUGH TWO `ref` OUT-PARAMETERS. The `ref` form is
//     retained exactly as declared, because collapsing it into a tuple return would DROP THE
//     BOOLEAN, and that boolean is the observable contract: it is the legacy's own way of saying
//     the request could not be satisfied. No non-`ref` convenience overload is offered either -
//     that would be a fifteenth member.
//
//  ==============================================================================================
//  WHAT THIS FILE IS NOT: THE TOKEN-ISSUANCE BOUNDARY
//  ==============================================================================================
//  The Security service is the SOLE TOKEN ISSUER in the decomposed system, and there is exactly
//  one signing secret in the whole system, held by Security and reaching it at run time from the
//  orchestration secret layer through the options pattern. The migration plan records
//  n_crypto.sru:L70-L73 - the four members at the bottom of the table above - as the PROVENANCE
//  for that issuer, which is precisely why the separation below has to be stated rather than
//  assumed:
//
//      THIS FILE IS A GENERAL-PURPOSE RSA PRIMITIVE PROVIDER, NOT THE TOKEN ISSUER.
//
//  It holds no signing authority. It OWNS NO KEY: every key it uses arrives as a method
//  parameter, already resolved by the caller. It knows nothing of the service's signing-secret
//  configuration name, it references neither the token issuer nor the signing-key provider, it
//  registers no route, and it opens no path. A future reader who wants token issuance wants those
//  types, not this one - and wiring a key store into this file would give it an authority the
//  architecture deliberately places elsewhere.
//
//  HOW A KEY REACHES A METHOD BELOW. The crypto endpoints resolve the caller's OPAQUE KEY
//  REFERENCE against the service's own key-store descriptor and pass the resolved key text in.
//  RAW KEY MATERIAL NEVER CROSSES THE WIRE FROM A CALLER, and this file is the far end of that
//  arrangement rather than a participant in it.
//
//  ==============================================================================================
//  KEY HYGIENE, WHICH MATTERS MORE HERE THAN ANYWHERE ELSE IN THE FOLDER
//  ==============================================================================================
//  This is the only file in the service that handles PRIVATE KEYS, so the rules it follows are
//  stricter than the folder's baseline and every one of them is checkable:
//
//      * NO KEY IS EVER READ FROM ANYWHERE. There is no configuration access, no options type, no
//        environment-variable read and no file read in this file. A key is a parameter or it does
//        not exist.
//      * NO KEY IS EVER RETAINED. There is no field, no property, no cache and no static holding
//        key material, and the sole instance field is the injected sibling encoder. Every RSA
//        instance is created inside the method that needs it and DISPOSED BY A `using` DECLARATION
//        before that method returns, so the platform can release and clear the key it holds. That
//        disposal is key hygiene, not a behavioural change: the legacy's native handles were
//        released by the closed binary and no observable behaviour depends on when.
//      * NO KEY IS EVER LOGGED. This type takes NO LOGGER, deliberately. Anything it logged would
//        be exactly the material that must not be logged, and an absent dependency cannot be
//        misused by a later edit the way a present one can.
//      * NO KEY EVER REACHES AN EXCEPTION MESSAGE. The single key-import failure has ONE FIXED
//        MESSAGE that names neither the key nor any fragment of it, and it never interpolates the
//        argument. Where an inner exception is attached it is one the base class library
//        generated, and those never embed their input.
//      * NO KEY, CERTIFICATE OR ARMOURED BLOCK APPEARS ANYWHERE IN THIS FILE - not as code, not as
//        a default argument value, not as documentation, not as an example and not in a comment.
//        Nothing from any of the repository's eleven known hardcoded-secret sites is reproduced
//        here in any form. Four of those sites are private-key sites and are named by locator
//        alone, because they are exactly the material this file would otherwise be tempted to
//        paste: tests/blink/test_jws.htm:L8-L22 (an armoured private key used to sign, and the
//        anti-pattern the signing-key provider exists to replace),
//        ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L163 (a bare binary private key
//        which, with the certificate at :L162, forms a complete usable identity), and
//        ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718 (two
//        distinct bare binary private keys). Reading those files as usage evidence is legitimate
//        and was done; copying a literal out of any of them - including into a test fixture - is
//        not. EVERY KEY USED BY A TEST OF THIS TYPE IS GENERATED BY THAT TEST AT RUN TIME.
//
//  ==============================================================================================
//  THE FOUR SUBSTITUTION DECISIONS, AND WHY EACH IS A DECISION AND NOT A MEASUREMENT
//  ==============================================================================================
//  Four behaviours of this surface are UNOBSERVABLE from the repository, because the code that
//  implemented them is inside the closed binary. Each is decided once, here, with its reason. NONE
//  IS CLAIMED AS VERIFIED: byte-exact parity for any of the four is assertable only against the
//  behavioural oracle, by capturing the legacy output for a workflow and comparing it with the
//  target output for the same workflow. Until such a capture exists each is a reasoned choice, and
//  each is centralised so that the oracle can adjudicate it in one place and, if it is wrong,
//  correct it in one place.
//
//  DECISION R1 - OAEP USES SHA-1 FOR BOTH THE LABEL DIGEST AND THE MASK GENERATION FUNCTION
//  --------------------------------------------------------------------------------------------
//  The legacy publishes ONE OAEP value with NO HASH PARAMETER [enums.sru:L950, whose own source
//  comment reads RSA_PKCS1_OAEP_PADDING], while the managed OAEP padding requires a named hash. A
//  hash must therefore be chosen, and the choice is not free: OAEP with different digests produces
//  incompatible ciphertext, so picking the wrong one makes every legacy OAEP ciphertext
//  undecryptable by this port.
//
//  THE BASIS FOR THE CHOICE, presented as an inference rather than a certainty. The framework
//  attributes OpenSSL among its eleven upstream libraries
//  [ws_objects/pfw.demos.pbl.src/w_about.srw:L118, carried forward into the repository's new root
//  NOTICE], and the legacy constant's own comment spells the OpenSSL macro name verbatim. OpenSSL's
//  RSA_PKCS1_OAEP_PADDING defaults to SHA-1 with an SHA-1 mask generation function when no digest
//  is set. A native surface that names the OpenSSL macro and exposes no digest parameter is
//  therefore most plausibly taking that library's default, so the SHA-1 OAEP variant is adopted.
//
//  IT IS NAMED IN EXACTLY ONE PLACE in this file, so that a single edit re-baselines it when the
//  oracle settles the question. And SHA-1 OAEP is itself a weakness inherited from the legacy
//  rather than a fresh choice made here: it is preserved for the same reason every other weak
//  default in this folder is preserved, and annotated at its point of reproduction.
//
//  DECISION R2 - SIGNATURES USE PKCS#1 v1.5, AND PSS IS NOT SELECTABLE
//  --------------------------------------------------------------------------------------------
//  L70-L73 expose a hash type and NO PADDING PARAMETER AT ALL, so the signature scheme is fixed
//  inside the binary. On the same OpenSSL basis as R1 - that library's signing entry point applies
//  PKCS#1 v1.5 block formatting - the PKCS#1 v1.5 signature padding is adopted.
//
//  PSS IS DELIBERATELY NOT OFFERED. It is the stronger scheme and it is not added, because THE
//  LEGACY DECLARES NO WAY TO SELECT IT: adding it would publish an operation the legacy never had,
//  and defaulting to it would invalidate every signature the legacy ever produced. That refusal is
//  behaviour preservation, not an oversight.
//
//  ONE USEFUL CONSEQUENCE FOR TESTING, worth recording because it is unusual. PKCS#1 v1.5
//  signatures are DETERMINISTIC: the same key, hash and message always produce byte-identical
//  output, there being no salt. A known-answer assertion over a key the test generates itself is
//  therefore possible, and it is what pins this decision against silent drift. PSS, by contrast,
//  is randomised and could not be pinned that way at all.
//
//  DECISION R3 - THE `pemformat` FLAG, THE THREE-ARGUMENT DEFAULT, AND THE TWO KEY STRUCTURES
//  --------------------------------------------------------------------------------------------
//  The four-argument GenRSAKey at L20 takes `readonly boolean pemformat`; the three-argument form
//  at L19 takes whatever default the binary applies. Both the meaning of the flag and the value of
//  that default are settled by evidence inside this repository, which makes R3 the best-supported
//  of the four decisions rather than the weakest.
//
//  WHAT "NOT ARMOURED" MEANS, AND WHICH BINARY STRUCTURES THE LEGACY ACTUALLY EMITTED. The
//  framework's own stored keys appear as BARE TEXT-ENCODED BINARY WITH NO ARMOUR AT ALL, at
//  w_test_websocket_mqtt.srw:L163 and at u_cst_tabpage_utility_crypto.sru:L466 and :L718, while
//  the demo passes the flag as true at u_cst_tabpage_utility_crypto.sru:L699 on the one occasion it
//  wants armoured text. Decoding ONLY THE LEADING STRUCTURAL BYTES of those stored keys - never
//  their key material - identifies the two structures unambiguously: the private ones are the
//  traditional single-purpose private-key structure, and the public ones are the algorithm-tagged
//  public-key structure. Generating a key of the same size through the managed surface reproduces
//  both, the public form matching the stored one in leading bytes AND IN TOTAL LENGTH EXACTLY.
//  That is a structural match rather than a guess, and it also fixes which armour labels the
//  armoured form must carry, since each structure has exactly one.
//
//  SO: `pemformat` true yields armoured text and false yields bare text-encoded binary, and FALSE
//  IS ADOPTED AS THE THREE-ARGUMENT DEFAULT on the evidence that every key the framework stored
//  without asking for armour is bare. This too is an inference, and the oracle would settle it.
//
//  IMPORT ACCEPTS BOTH FORMS UNCONDITIONALLY, whichever the caller produced and whichever
//  structure it holds. That is not a courtesy: without it a key generated one way could not be
//  used by the other twelve overloads, which would make the flag a trap rather than a choice. One
//  private routine does every import in this file for exactly that reason - so the accepted set
//  cannot diverge between members.
//
//  DECISION R4 - THE HASH SET ADMITS CRC32, FOR WHICH NO SIGNATURE SCHEME EXISTS
//  --------------------------------------------------------------------------------------------
//  The hash-type set is shared: enums.sru:L927 records in the source itself that the same six
//  values parameterise Hash, RSASign AND VerifyRSASign, and the shared predicate in
//  LegacyDefaults.cs consequently admits MD5 through CRC32 [enums.sru:L928-L933]. A caller may
//  therefore hand CRC32 to a signature member and be inside the published set while doing it.
//
//  THERE IS NO RSA-OVER-CRC32 SIGNATURE SCHEME. CRC32 is a checksum, not a cryptographic hash: it
//  has no collision resistance, no standard digest-algorithm identifier for a signature structure,
//  and no implementation in the base class library. NOTHING IS INVENTED HERE. Fabricating one
//  would produce a signature that verifies nothing while looking exactly like a real one, which is
//  worse than a refusal in the specific way this port cares about - a caller could not tell.
//
//  The CRC32 signature arm therefore takes ONE DOCUMENTED FAILURE PATH, and it is deliberately a
//  DIFFERENT SHAPE from the unsupported-value failure, because the two conditions are different: a
//  value outside the published set is an argument-domain error, whereas CRC32 is INSIDE the
//  published set and fails on a CAPABILITY LIMIT. What the closed binary actually did with such a
//  call is unobservable and no legacy call site exercises it - the demo's signature calls pass
//  SHA-256 [u_cst_tabpage_utility_crypto.sru:L470, :L474] - so the oracle would settle it. The
//  keyed CRC32 arm of the sibling HMAC provider faces the identical problem and MUST ADOPT THE
//  IDENTICAL SHAPE, so that the two files agree rather than each refusing in its own way.
//
//  MD5 AND SHA-1 SIGNING ARE RETAINED, by contrast, and are annotated where they are reproduced.
//  Both are broken for collision resistance and neither may be removed: removing either would
//  reject input the legacy accepted, and every signature produced under them would stop verifying.
//
//  ==============================================================================================
//  DECISION D4 APPLIED: THE STRING-SHAPED OVERLOADS CARRY A TEXT-SAFE PAYLOAD
//  ==============================================================================================
//  The rule is the folder's, not this file's, and it is recorded once in LegacyDefaults.cs: every
//  string-shaped overload returns a printable text-safe encoded payload and its string-shaped
//  inverse accepts that same encoding, while every blob-shaped overload carries RAW BYTES and
//  encodes nothing.
//
//  THE RSA MEMBERS ARE PART OF WHY THAT RULE IS REQUIRED RATHER THAN OPTIONAL. In the oracle's own
//  demo the string-shaped ciphertext and signature are written straight back into a plain
//  multi-line text control and then fed to their string-shaped inverses from that same control:
//  RSAEncrypt at u_cst_tabpage_utility_crypto.sru:L744 pairs with RSADecrypt at :L720, and RSASign
//  at :L470 pairs with VerifyRSASign at :L474. Raw cipher or signature bytes stuffed into a string
//  could not survive that trip - arbitrary bytes are not valid text in any encoding - so the
//  string-shaped members must encode. Those same four call sites also confirm two things this file
//  must get right: the TWO-ARGUMENT DEFAULT-PADDING PATH IS LIVE at :L744 and :L720, and the
//  SIGNATURE PATH IS LIVE WITH SHA-256 at :L470 and :L474. Neither is a rare branch.
//
//  This file invents no second convention. It reads the encoding from the shared catalogue on
//  every call rather than naming one, so re-baselining that single decision needs no edit here.
//
//  TURNING TEXT INTO BYTES IN THE FIRST PLACE is the folder's other shared substitution, and it is
//  read from the same catalogue for the same reason. Its rationale - that UTF-8 is byte-identical
//  to the legacy's conversion across the ASCII range, while PowerBuilder's own conversion is code
//  page and locale dependent and so is not reproducible in a Linux container - governs payload
//  text exactly as it governs key text. Non-ASCII payload text is the one input on which the two
//  could diverge, and that divergence is oracle-only verifiable.
//
//  ==============================================================================================
//  PAYLOAD SIZE: THE LIMIT IS THE LEGACY'S AND IT IS PRESERVED
//  ==============================================================================================
//  RSA encrypts at most one modulus-sized block less the padding overhead. PKCS#1 v1.5 costs 11
//  bytes; OAEP costs twice the digest length plus 2, which for the SHA-1 variant of DECISION R1 is
//  42. At the smallest key size the legacy publishes those ceilings are small - 117 bytes and 86
//  bytes respectively for a 1024-bit modulus, both measured on this toolchain - and they scale with
//  the key.
//
//  AN OVERSIZED PAYLOAD TAKES ONE DOCUMENTED FAILURE PATH: the cryptographic failure the platform
//  raises, propagated unchanged. NOTHING IS ADDED TO PAPER OVER IT. There is no chunking, no
//  splitting across blocks, no hybrid envelope and no silent fall back to a symmetric cipher, and
//  none may be added. Each would change the wire format so that the legacy could not decrypt the
//  result, and each would replace a clear refusal with a quiet success the caller never asked for.
//  The legacy failed here too; that failure is the behaviour.
//
//  An EMPTY payload is not oversized and is not an error: it encrypts to a full modulus-sized
//  block and decrypts back to nothing, which is well defined at every key size.
//
//  ==============================================================================================
//  WHAT A READER WILL LOOK FOR HERE AND NOT FIND
//  ==============================================================================================
//  NO ENTROPY SEAM IS INJECTED, and its absence is deliberate rather than an omission. The
//  determinism seams this migration names are the GUID, random-string and random-blob generators,
//  which is why the sibling random provider takes one. Key generation is NOT among them, and could
//  not usefully be: the managed factory draws from the platform's own cryptographic generator and
//  CANNOT BE SEEDED, so an injected source would be a seam that seams nothing. Injecting one would
//  fabricate a determinism guarantee the plan does not call for and this type cannot honour. The
//  consequence is stated plainly rather than worked around: GENERATED KEYS ARE NOT REPRODUCIBLE,
//  so a test of this type asserts STRUCTURAL properties of a generated key and never its bytes.
//
//  NO MINIMUM KEY SIZE IS ENFORCED. 1024 bits remains legal [enums.sru:L965] and is exercised by
//  the oracle's own demo [u_cst_tabpage_utility_crypto.sru:L699]. A floor here would reject input
//  the legacy accepted.
//
//  NO SCREAMING-SNAKE IDENTIFIER IS DECLARED IN THIS FILE. The repository scopes its
//  naming-analyzer suppressions to the files on its BAND 3 roster - the single source of truth for
//  that list - of which LegacyDefaults.cs is the only one
//  inside this service, so a preserved-spelling constant declared here would be a BUILD ERROR
//  under warnings-as-errors rather than a style remark. All 30 legacy CRYPTO_ constants are
//  REFERENCED through the shared kernel and NONE is redeclared, re-lettered or shadowed: every
//  CRYPTO_ occurrence below is either a qualified reference or documentation text.
//
//  NO ANALOGUE OF THE LEGACY GLOBAL IS REPRODUCED. n_crypto.sru:L75 declares
//  `global n_crypto n_crypto`, a global auto-instance whose name collides with its own type name,
//  and every caller through it is guarded by a lazy-construction idiom. Both exist because
//  PowerBuilder resolves one flat global namespace and offers no container. This type is a
//  container-registered instance service reached by CONSTRUCTOR INJECTION - never a global, never a
//  static, never lazily self-constructing. There is nothing to port, only a collision to avoid.
//
//  ==============================================================================================
// ==============================================================================================

using System.Security.Cryptography;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// The asymmetric cryptographic surface ported from the legacy framework: RSA key generation, RSA
/// encryption and decryption, and RSA signing and verification.
/// </summary>
/// <remarks>
/// <para>
/// Substitutes the fourteen RSA declarations of
/// <c>ws_objects/pfw.crypto.pbl.src/n_crypto.sru</c> - <c>GenRSAKey</c> at L19-L20,
/// <c>RSAEncrypt</c> at L62-L65, <c>RSADecrypt</c> at L66-L69, <c>RSASign</c> at L70-L71 and
/// <c>VerifyRSASign</c> at L72-L73 - none of which has an implementation body anywhere in the
/// repository. Every member is therefore a documented substitution against
/// <see cref="System.Security.Cryptography"/>, and the four substitution decisions it rests on are
/// recorded in this file's header as R1 through R4.
/// </para>
/// <para>
/// NOT THE TOKEN ISSUER. This type is a general-purpose RSA primitive provider. It holds no
/// signing authority, owns no key, reads no configuration and registers no route; the service's
/// sole token issuer and its signing-key provider are separate types that this one neither knows
/// nor references. See the header for the full separation.
/// </para>
/// <para>
/// KEY MATERIAL ARRIVES AS A PARAMETER AND IS NEVER RETAINED. Keys are always TEXT - every
/// key parameter of every overload is a string, including on the byte-shaped payload overloads -
/// and each is imported into a fresh <see cref="RSA"/> instance that is disposed before the method
/// returns. This type declares no field holding key material, keeps no cache, takes no logger, and
/// never lets a key or a fragment of one reach an exception message.
/// </para>
/// <para>
/// PRESERVED LEGACY WEAKNESSES, EVERY ONE DELIBERATE. PKCS#1 v1.5 is the default encryption
/// padding [enums.sru:L951] and OAEP remains selectable without being promoted
/// [enums.sru:L950]; no-padding is refused because the legacy declares no constant for it;
/// 1024-bit keys remain legal [enums.sru:L965] and no minimum is enforced; signatures use PKCS#1
/// v1.5 and PSS is not selectable; MD5 and SHA-1 signing are retained. None of these is corrected,
/// and each is annotated where it is reproduced.
/// </para>
/// <para>
/// STATELESS AND THREAD-SAFE. The only field is the injected sibling encoder, which is itself
/// stateless, so one instance serves any number of concurrent requests. No <see cref="RSA"/>
/// instance is ever cached or shared between calls - each is created, used and disposed inside one
/// method invocation, which is what makes concurrent use safe as well as hygienic.
/// </para>
/// </remarks>
public sealed class RsaProvider
{
    // ==========================================================================================
    //  THE NAMED-ONCE SUBSTITUTIONS
    //  ------------------------------------------------------------------------------------------
    //  Each of the three members below is the SINGLE PLACE its decision is expressed, so that the
    //  behavioural oracle can re-baseline it with one edit. None holds key material and none is
    //  mutable.
    // ==========================================================================================
    #region Named-once substitutions - DECISION R1, DECISION R2, DECISION R3

    /// <summary>
    /// The OAEP variant the single legacy OAEP value maps onto: SHA-1 for both the label digest
    /// and the mask generation function. DECISION R1, named here and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY, AND AN INFERENCE RATHER THAN A MEASUREMENT.
    /// The legacy publishes one OAEP value with no digest parameter
    /// [<see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/>, enums.sru:L950, whose source comment names the
    /// OpenSSL macro verbatim], while the managed padding requires a named digest. OpenSSL - which
    /// the framework attributes among its upstream libraries [w_about.srw:L118] - defaults that
    /// macro to SHA-1 with an SHA-1 mask generation function, so a native surface naming the macro
    /// and exposing no digest is most plausibly taking that default.
    /// </para>
    /// <para>
    /// SHA-1 is broken for collision resistance. It is nevertheless correct here: OAEP with a
    /// different digest produces incompatible ciphertext, so silently upgrading it would make every
    /// OAEP value the legacy ever produced undecryptable by this port. If a paired legacy recording
    /// shows a different digest, THIS ONE LINE IS THE CORRECTION and nothing else in the file
    /// changes.
    /// </para>
    /// </remarks>
    private static readonly RSAEncryptionPadding OaepPaddingSubstitute = RSAEncryptionPadding.OaepSHA1;

    /// <summary>
    /// The signature padding the legacy's parameterless signature scheme maps onto: PKCS#1 v1.5.
    /// DECISION R2, named here and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four signature declarations [n_crypto.sru:L70-L73] expose a hash type and NO padding
    /// parameter, so the scheme is fixed inside the closed binary. On the same OpenSSL basis as
    /// DECISION R1, whose signing entry point applies PKCS#1 v1.5 block formatting, that scheme is
    /// adopted.
    /// </para>
    /// <para>
    /// PSS IS NOT SELECTABLE AND MUST NOT BE ADDED. It is the stronger scheme, and the legacy
    /// declares no way to ask for it: adding it would publish an operation the legacy never had,
    /// and defaulting to it would invalidate every signature the legacy produced.
    /// </para>
    /// <para>
    /// PKCS#1 v1.5 signatures are DETERMINISTIC - no salt, so one key, hash and message always
    /// yield byte-identical output - which is what allows a known-answer assertion over a
    /// test-generated key to pin this choice against silent drift.
    /// </para>
    /// </remarks>
    private static readonly RSASignaturePadding SignaturePaddingSubstitute = RSASignaturePadding.Pkcs1;

    /// <summary>
    /// The value the three-argument <c>GenRSAKey</c> supplies for the flag its four-argument
    /// sibling takes explicitly: bare text-encoded binary rather than armoured text. DECISION R3.
    /// </summary>
    /// <remarks>
    /// The three-argument declaration [n_crypto.sru:L19] takes whatever default the closed binary
    /// applies. <see langword="false"/> is adopted on in-repository evidence: every key the
    /// framework stored without asking for armour is bare text-encoded binary
    /// [w_test_websocket_mqtt.srw:L163, u_cst_tabpage_utility_crypto.sru:L466 and :L718], while the
    /// demo passes the flag as <see langword="true"/> on the one occasion it wants armour
    /// [u_cst_tabpage_utility_crypto.sru:L699]. See the header for the full reasoning; the oracle
    /// would settle it.
    /// </remarks>
    private const bool PemFormatDefault = false;

    /// <summary>
    /// The sentinel that identifies armoured key text, used by the single import routine to choose
    /// between the armoured and the bare-Base64 paths of DECISION R3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A FORMAT MARKER, NOT KEY MATERIAL. It is the opening delimiter of an armoured block and
    /// deliberately STOPS SHORT OF ANY ARMOUR LABEL: this file therefore contains no label naming a
    /// private key, a public key or a certificate, so a secret scan over this file finds nothing to
    /// flag and no reader can mistake the constant for pasted key text. Which label an armoured key
    /// actually carries is the platform import's business, not this file's - it accepts all of them.
    /// </para>
    /// <para>
    /// Detection is deliberately a positive test for armour rather than a test for the absence of
    /// Base64 characters: a key that is armoured is unambiguous, whereas "not Base64" would depend
    /// on how the text happens to be wrapped or padded.
    /// </para>
    /// </remarks>
    private const string PemArmourMarker = "-----BEGIN";

    #endregion


    // ==========================================================================================
    //  THE FAILURE MESSAGES
    //  ------------------------------------------------------------------------------------------
    //  Held as constants so that each condition has one wording as well as one shape, and - the
    //  part that matters in this file specifically - so that no message can be assembled by
    //  interpolating an argument. NONE OF THE FOUR NAMES A KEY, A FRAGMENT OF ONE, OR A PAYLOAD.
    // ==========================================================================================
    #region Failure messages - no argument is ever interpolated into one

    /// <summary>
    /// The single message for a key that cannot be imported, in any form, for any reason.
    /// </summary>
    /// <remarks>
    /// DELIBERATELY UNINFORMATIVE ABOUT ITS INPUT. It names the accepted forms, which a caller can
    /// act on, and says nothing whatever about the value supplied, which a caller already has and
    /// which must never be echoed back, logged, or carried in an exception. It is shared by the
    /// armoured and the bare import paths so that neither can become the more talkative of the two.
    /// </remarks>
    private const string KeyImportFailureMessage =
        "The key could not be imported. Accepted forms are armoured key text and bare " +
        "Base64-encoded binary key text, in either the traditional private-key structure, the " +
        "algorithm-tagged private-key structure, the algorithm-tagged public-key structure or the " +
        "bare public-key structure. The supplied value is deliberately not reported.";

    /// <summary>
    /// The single message for a key argument that is empty or entirely white space.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="KeyImportFailureMessage"/> because it is a different defect: there
    /// is nothing to parse rather than something that failed to parse. It likewise reports nothing
    /// about the value.
    /// </remarks>
    private const string EmptyKeyMessage =
        "A key is required. The value supplied is empty or contains only white space.";

    /// <summary>
    /// The single message for an RSA padding identifier outside the two the legacy publishes,
    /// which is also where a no-padding request is refused.
    /// </summary>
    /// <remarks>
    /// The refusal of no-padding is PRESERVED LEGACY BEHAVIOUR and not a hardening choice made
    /// here - the legacy declares no constant for it and rejects a request for it, which
    /// <see cref="LegacyDefaults.RSA_NO_PADDING_SUPPORTED"/> records - so the wording must not
    /// describe it as a security improvement.
    /// </remarks>
    private const string UnsupportedPaddingMessage =
        "Not a published RSA padding mode. The set is exactly PKCS#1 v1.5 (0) and OAEP (1) " +
        "[ws_objects/pfw.shared.pbl.src/enums.sru:L949-L950]. No-padding is not a published value " +
        "and is refused, which is preserved legacy behaviour rather than a hardening choice.";

    /// <summary>
    /// The single message for a hash identifier outside the six the legacy publishes.
    /// </summary>
    /// <remarks>
    /// The set is shared with the unkeyed hash and keyed HMAC providers, because enums.sru:L927
    /// records in the source itself that one hash-type set parameterises <c>Hash</c>,
    /// <c>RSASign</c> and <c>VerifyRSASign</c> alike. This file screens through
    /// <see cref="LegacyDefaults.IsSupportedHashType"/> rather than declaring a second set.
    /// </remarks>
    private const string UnsupportedHashTypeMessage =
        "Not a published hash type. The set is exactly MD5 (0), SHA-1 (1), SHA-256 (2), " +
        "SHA-384 (3), SHA-512 (4) and CRC32 (5) " +
        "[ws_objects/pfw.shared.pbl.src/enums.sru:L928-L933].";

    /// <summary>
    /// The single message for the CRC32 signature arm, which is a capability limit rather than an
    /// argument-domain error. DECISION R4.
    /// </summary>
    /// <remarks>
    /// CRC32 is INSIDE the published hash set, so it passes
    /// <see cref="LegacyDefaults.IsSupportedHashType"/>; what does not exist is an
    /// RSA-over-CRC32 signature construction. Fabricating one would produce a signature that
    /// verifies nothing while looking like a real one, so this arm refuses instead. The keyed CRC32
    /// arm of the sibling HMAC provider faces the identical problem and must refuse in the
    /// identical shape.
    /// </remarks>
    private const string Crc32SignatureMessage =
        "CRC32 is a checksum rather than a cryptographic hash and there is no RSA-over-CRC32 " +
        "signature construction to apply: it has no digest-algorithm identifier for a signature " +
        "structure and no platform implementation. It remains a published hash type " +
        "[ws_objects/pfw.shared.pbl.src/enums.sru:L933] and is accepted by the unkeyed hash " +
        "surface; only the signature members refuse it, and they refuse rather than invent a " +
        "construction that would verify nothing.";

    #endregion


    /// <summary>
    /// The injected sibling encoder. Every text-safe payload this type produces or consumes passes
    /// through it, so this file holds no encoding logic and invents no second convention.
    /// </summary>
    /// <remarks>
    /// The ONLY field in this type, and it holds no key material and no state of its own. It is
    /// used for two distinct purposes that must not be conflated: the DECISION D4 payload encoding
    /// of the string-shaped overloads, which is read from
    /// <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/> on every call so that a re-baseline of
    /// that decision needs no edit here, and the Base64 encoding of exported and imported KEY TEXT,
    /// which is fixed at Base64 by DECISION R3 because the framework's own stored keys are Base64
    /// and must remain importable however D4 is later settled.
    /// </remarks>
    private readonly EncodingProvider _encoding;

    /// <summary>
    /// Creates a provider that encodes and decodes its text-safe payloads through
    /// <paramref name="encoding"/>.
    /// </summary>
    /// <param name="encoding">The sibling encoding provider.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="encoding"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// CONSTRUCTOR INJECTION IS THE WHOLE POINT. It is what replaces the legacy's global
    /// auto-instance and its lazy-construction guard [n_crypto.sru:L75], and it is why this type
    /// declares no static entry point of any kind. There is deliberately no parameterless
    /// constructor and no defaulted parameter that would construct an encoder when the argument is
    /// omitted; either would make the uninjected path the default one.
    /// </para>
    /// <para>
    /// NO ENTROPY SOURCE IS INJECTED HERE, and the absence is reasoned rather than accidental. The
    /// determinism seams this migration names are the GUID, random-string and random-blob
    /// generators - key generation is not among them, and the managed key factory draws from the
    /// platform's cryptographic generator and cannot be seeded, so an injected source would seam
    /// nothing. Generated keys are therefore NOT reproducible, and a test of this type asserts
    /// structural properties of a generated key rather than its bytes.
    /// </para>
    /// <para>
    /// NO CONFIGURATION, OPTIONS TYPE, KEY STORE OR LOGGER IS INJECTED EITHER. A key reaches this
    /// type as a method parameter or not at all, and an absent dependency cannot be misused by a
    /// later edit the way a present one can.
    /// </para>
    /// </remarks>
    public RsaProvider(EncodingProvider encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        _encoding = encoding;
    }



    // ==========================================================================================
    //  KEY GENERATION - n_crypto.sru:L19-L20
    //  ------------------------------------------------------------------------------------------
    //  Two overloads. The three-argument form delegates to the four-argument form with DECISION
    //  R3's default, which is how the legacy layers its own overload pairs and which keeps the
    //  generation and export logic in exactly one body.
    // ==========================================================================================
    #region GenRSAKey - n_crypto.sru:L19-L20

    /// <summary>
    /// Generates an RSA key pair of <paramref name="bits"/> bits and writes the private and public
    /// key text through the two <see langword="ref"/> parameters, in the bare Base64-encoded binary
    /// form that DECISION R3 adopts as this overload's default.
    /// </summary>
    /// <param name="bits">
    /// The requested modulus size in bits. <see cref="Enums.CRYPTO_RSA_BITS_1024"/>,
    /// <see cref="Enums.CRYPTO_RSA_BITS_2048"/> and <see cref="Enums.CRYPTO_RSA_BITS_4096"/> are
    /// the sizes the legacy publishes as convenience values, but ANY size the platform accepts is
    /// legal - they are not an allowed set.
    /// </param>
    /// <param name="prikey">
    /// Receives the private key text. Left UNTOUCHED when the method returns
    /// <see langword="false"/>.
    /// </param>
    /// <param name="pubkey">
    /// Receives the public key text. Left UNTOUCHED when the method returns
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a key pair was generated and both parameters were written;
    /// <see langword="false"/> when <paramref name="bits"/> is not a size the platform can generate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function boolean GenRSAKey(readonly uint bits, ref string prikey,
    /// ref string pubkey)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19].
    /// </para>
    /// <para>
    /// THE THREE-ARGUMENT FORM'S FORMAT IS DECIDED, NOT READ. It supplies
    /// <see cref="PemFormatDefault"/> to its four-argument sibling; see DECISION R3 in this file's
    /// header for the in-repository evidence behind that value, and note that the oracle would
    /// settle it. Nothing else differs between the two overloads.
    /// </para>
    /// </remarks>
    public bool GenRSAKey(ushort bits, ref string prikey, ref string pubkey)
    {
        return GenRSAKey(bits, ref prikey, ref pubkey, PemFormatDefault);
    }

    /// <summary>
    /// Generates an RSA key pair of <paramref name="bits"/> bits and writes the private and public
    /// key text through the two <see langword="ref"/> parameters, armoured or bare according to
    /// <paramref name="pemformat"/>.
    /// </summary>
    /// <param name="bits">
    /// The requested modulus size in bits. Any size the platform accepts is legal; see the remarks
    /// on the minimum.
    /// </param>
    /// <param name="prikey">
    /// Receives the private key text: armoured private key text when
    /// <paramref name="pemformat"/> is <see langword="true"/>, otherwise the same structure as bare
    /// Base64 with no armour. Left UNTOUCHED when the method returns <see langword="false"/>.
    /// </param>
    /// <param name="pubkey">
    /// Receives the public key text: armoured public key text when <paramref name="pemformat"/> is
    /// <see langword="true"/>, otherwise the same structure as bare Base64 with no armour. Left
    /// UNTOUCHED when the method returns <see langword="false"/>.
    /// </param>
    /// <param name="pemformat">
    /// <see langword="true"/> for armoured text; <see langword="false"/> for bare Base64-encoded
    /// binary. Either form is accepted back by every other member of this type.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a key pair was generated and both parameters were written;
    /// <see langword="false"/> when <paramref name="bits"/> is not a size the platform can generate.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function boolean GenRSAKey(readonly uint bits, ref string prikey,
    /// ref string pubkey, readonly boolean pemformat)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L20].
    /// </para>
    /// <para>
    /// WIDTH MAPPING, AND A CONTRAST WORTH KNOWING. A PowerBuilder <c>uint</c> is 16 BITS WIDE, so
    /// the declared <c>readonly uint bits</c> maps to <see cref="ushort"/>, which comfortably holds
    /// every published size. Unlike the symmetric cipher type - where the legacy declares a 16-bit
    /// parameter but 32-bit constants, a width mismatch this port carries rather than tidies - the
    /// RSA size constants are themselves declared 16-bit unsigned [enums.sru:L965-L967], SO THE
    /// PARAMETER WIDTH AND THE CONSTANT WIDTH AGREE HERE. That contrast is the evidence that the
    /// symmetric mismatch is a legacy inconsistency rather than a general pattern.
    /// </para>
    /// <para>
    /// NO MINIMUM SIZE IS ENFORCED, and 1024 BITS IS EXPLICITLY ACCEPTED
    /// [<see cref="LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS"/>, enums.sru:L965], because the
    /// oracle's own demo generates exactly that [u_cst_tabpage_utility_crypto.sru:L699]. A 1024-bit
    /// modulus is below every current recommendation; imposing a floor would nevertheless reject
    /// input the legacy accepted, which is a behavioural change this port forbids - see
    /// <see cref="LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED"/>. The ONLY size screening applied
    /// is what the platform itself will generate, asked of the platform rather than assumed.
    /// </para>
    /// <para>
    /// THE BOOLEAN IS A REAL RESULT, NOT A FORMALITY. A size the platform cannot generate returns
    /// <see langword="false"/> rather than throwing, because a boolean return is what the legacy
    /// declares and refusing through it is the faithful shape. Both <see langword="ref"/>
    /// parameters are then left exactly as the caller passed them, so a caller that ignores the
    /// boolean cannot mistake stale text for a fresh key.
    /// </para>
    /// <para>
    /// THE TWO EXPORTED STRUCTURES ARE FIXED BY EVIDENCE, NOT PREFERENCE. The private key is the
    /// traditional single-purpose private-key structure and the public key is the algorithm-tagged
    /// public-key structure, which is what the framework's own stored keys are; DECISION R3 records
    /// how their leading bytes and total length were matched. The armoured form carries the one
    /// armour label that belongs to each of those structures.
    /// </para>
    /// <para>
    /// KEY MATERIAL LEAVES THIS METHOD ONLY THROUGH THE TWO <see langword="ref"/> PARAMETERS. The
    /// generating instance is disposed before the method returns, nothing is retained, and nothing
    /// is logged. GENERATED KEYS ARE NOT REPRODUCIBLE - the platform generator cannot be seeded and
    /// no seam exists - so a test asserts structure and round-trip usability rather than bytes.
    /// </para>
    /// </remarks>
    public bool GenRSAKey(ushort bits, ref string prikey, ref string pubkey, bool pemformat)
    {
        // Ask the platform what it will generate rather than reimplementing a size policy, and do
        // it BEFORE generating anything. This is not a minimum-size guard: 1024 is legal on every
        // supported platform and is deliberately not excluded here or anywhere else.
        if (!IsPlatformLegalKeySize(bits))
        {
            return false;
        }

        using RSA rsa = RSA.Create(bits);

        // Exported in the two structures DECISION R3 pins. The armoured members emit the one
        // armour label belonging to each structure; the bare members emit the same bytes, encoded
        // as Base64 through the sibling encoder so that this file holds no second Base64
        // implementation. Base64 is named explicitly here rather than read from
        // LegacyDefaults.STRING_PAYLOAD_ENCODING, because key text is NOT a DECISION D4 payload:
        // the framework's own stored keys are Base64 and must stay importable however D4 is later
        // settled.
        if (pemformat)
        {
            prikey = rsa.ExportRSAPrivateKeyPem();
        }
        else
        {
            // THE EXPORTED PRIVATE STRUCTURE IS HELD IN A LOCAL AND ZEROED, rather than passed inline
            // as an anonymous temporary. Inline, the array holding the private key in the clear would be
            // left on the managed heap for the collector - which is non-deterministic and may COPY the
            // buffer while compacting, so the material can outlive the call in more than one place. A
            // local plus a finally is what makes the wipe happen on the failure path as well.
            byte[] privateDer = rsa.ExportRSAPrivateKey();

            try
            {
                prikey = _encoding.BlobToString(privateDer, Enums.CRYPTO_ENCODING_BASE64);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateDer);
            }
        }

        // THE PUBLIC STRUCTURE IS NOT WIPED, AND THAT IS NOT AN INCONSISTENCY. It carries no secret -
        // it is the half this service publishes anonymously - so wiping it would suggest a
        // confidentiality property that does not exist and is not needed.
        pubkey = pemformat
            ? rsa.ExportSubjectPublicKeyInfoPem()
            : _encoding.BlobToString(rsa.ExportSubjectPublicKeyInfo(), Enums.CRYPTO_ENCODING_BASE64);

        // WHAT THIS CANNOT REACH, STATED RATHER THAN HIDDEN. Both `prikey` shapes are strings, and a
        // .NET string cannot be wiped, so the armoured path has no buffer to clear and the Base64 path's
        // OUTPUT survives the call by design - it is the caller's requested result. The preserved legacy
        // signature returns key text through a by-reference string parameter
        // [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20], so that residual belongs to the
        // signature and not to this implementation. What is removed here is the AVOIDABLE copy.
        return true;
    }

    #endregion



    // ==========================================================================================
    //  ENCRYPTION - n_crypto.sru:L62-L65
    //  ------------------------------------------------------------------------------------------
    //  Four overloads: the cross product of a {string, blob} payload and a {default, explicit}
    //  padding. The two-argument forms delegate to their three-argument siblings with the preserved
    //  default, and the string-shaped forms delegate to the byte-shaped ones after encoding, so the
    //  actual encryption happens in exactly ONE body. The key parameter is a string in all four.
    // ==========================================================================================
    #region RSAEncrypt - n_crypto.sru:L62-L65

    /// <summary>
    /// Encrypts text with a public key using the preserved default padding, returning the
    /// ciphertext as a text-safe encoded payload.
    /// </summary>
    /// <param name="plain">
    /// The text to encrypt. An EMPTY string is valid and yields a full modulus-sized block.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <returns>The ciphertext, encoded per DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The payload exceeds what this key and padding can carry, or the imported key cannot encrypt.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string RSAEncrypt(readonly string plain, readonly string
    /// pubkey)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L62].
    /// </para>
    /// <para>
    /// THIS DEFAULT-PADDING PATH IS LIVE, NOT THEORETICAL. The oracle's own demo calls exactly this
    /// two-argument shape at u_cst_tabpage_utility_crypto.sru:L744 and feeds the result to the
    /// two-argument decrypt at :L720. The padding supplied is
    /// <see cref="LegacyDefaults.RSA_PADDING_DEFAULT"/>, which resolves to PKCS#1 v1.5
    /// [enums.sru:L951] - a KNOWN LEGACY WEAKNESS preserved deliberately, since PKCS#1 v1.5 is
    /// vulnerable to adaptive chosen-ciphertext attacks where a service distinguishes a padding
    /// failure from other failures. OAEP is declared [enums.sru:L950] and remains selectable
    /// through the three-argument sibling; it is neither removed nor promoted to the default,
    /// because promoting it would make every ciphertext the legacy produced undecryptable.
    /// </para>
    /// </remarks>
    public string RSAEncrypt(string plain, string pubkey)
    {
        return RSAEncrypt(plain, pubkey, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    /// <summary>
    /// Encrypts text with a public key using an explicitly chosen padding, returning the ciphertext
    /// as a text-safe encoded payload.
    /// </summary>
    /// <param name="plain">
    /// The text to encrypt. An EMPTY string is valid and yields a full modulus-sized block.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <param name="padding">
    /// Either <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> or
    /// <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/>.
    /// </param>
    /// <returns>The ciphertext, encoded per DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="padding"/> is outside the two published values, which is also how a
    /// no-padding request is refused.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The payload exceeds what this key and padding can carry, or the imported key cannot encrypt.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string RSAEncrypt(readonly string plain, readonly string
    /// pubkey, readonly long padding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L63]. The
    /// declared <c>readonly long padding</c> maps to <see cref="long"/>, matching the 32-bit width
    /// of the constants that name its legal values [enums.sru:L949-L951].
    /// </para>
    /// <para>
    /// DECISION D4 APPLIED IN BOTH DIRECTIONS OF THE PAIR. The text is turned into bytes with the
    /// folder's shared text substitution and the resulting ciphertext is encoded with the folder's
    /// shared payload encoding, both read from <see cref="LegacyDefaults"/> on every call rather
    /// than named here, so re-baselining either needs no edit in this file. That encoding is what
    /// lets the demo's round trip through a plain text control work at all.
    /// </para>
    /// <para>
    /// THE PAYLOAD CEILING IS THE LEGACY'S AND IS PRESERVED. PKCS#1 v1.5 costs 11 bytes and the
    /// SHA-1 OAEP variant of DECISION R1 costs 42, so a 1024-bit key carries at most 117 or 86
    /// bytes respectively. An oversized payload takes ONE failure path, the platform's
    /// cryptographic failure propagated unchanged. NO CHUNKING, NO HYBRID ENVELOPE AND NO SYMMETRIC
    /// FALL BACK IS ADDED, and none may be: each would change the wire format so that the legacy
    /// could not decrypt the result, and each would replace a clear refusal with a quiet success.
    /// Note that the ceiling applies to the ENCODED text's byte length after the shared text
    /// substitution, not to its character count, so multi-byte characters consume more of it.
    /// </para>
    /// </remarks>
    public string RSAEncrypt(string plain, string pubkey, long padding)
    {
        ArgumentNullException.ThrowIfNull(plain);

        byte[] cipher = RSAEncrypt(LegacyDefaults.KeyMaterialEncoding.GetBytes(plain), pubkey, padding);

        return _encoding.BlobToString(cipher, LegacyDefaults.STRING_PAYLOAD_ENCODING);
    }

    /// <summary>
    /// Encrypts bytes with a public key using the preserved default padding, returning raw
    /// ciphertext bytes.
    /// </summary>
    /// <param name="plain">
    /// The bytes to encrypt. An EMPTY array is valid and yields a full modulus-sized block.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text - a STRING even on this byte-shaped
    /// overload, exactly as the legacy declares it.
    /// </param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The payload exceeds what this key and padding can carry, or the imported key cannot encrypt.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function blob RSAEncrypt(readonly blob plain, readonly string
    /// pubkey)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L64]. The padding supplied is
    /// <see cref="LegacyDefaults.RSA_PADDING_DEFAULT"/>, PKCS#1 v1.5 [enums.sru:L951], preserved as
    /// the default for the reason recorded on the string-shaped two-argument sibling. Per DECISION
    /// D4 a byte-shaped overload carries RAW BYTES and encodes nothing.
    /// </remarks>
    public byte[] RSAEncrypt(byte[] plain, string pubkey)
    {
        return RSAEncrypt(plain, pubkey, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    /// <summary>
    /// Encrypts bytes with a public key using an explicitly chosen padding, returning raw
    /// ciphertext bytes. This is the single body in which encryption actually happens.
    /// </summary>
    /// <param name="plain">
    /// The bytes to encrypt. An EMPTY array is valid and yields a full modulus-sized block.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text - a STRING even on this byte-shaped
    /// overload.
    /// </param>
    /// <param name="padding">
    /// Either <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> or
    /// <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/>.
    /// </param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="padding"/> is outside the two published values.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The payload exceeds what this key and padding can carry. Note that supplying a PRIVATE key
    /// here is NOT an error: a private key carries the public parameters too, so it encrypts
    /// perfectly well, and the legacy would have accepted it for the same reason.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function blob RSAEncrypt(readonly blob plain, readonly string pubkey,
    /// readonly long padding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L65]. The other three
    /// encryption overloads reduce to this one, which is deliberate: one body means one padding
    /// resolution, one key import and one failure shape.
    /// </para>
    /// <para>
    /// ORDER OF OPERATIONS IS DELIBERATE. The padding is resolved - and therefore screened - BEFORE
    /// the key is imported, so an invalid padding value is refused without the key material ever
    /// being parsed. The imported instance is disposed by its <see langword="using"/> declaration
    /// before this method returns, which is key hygiene rather than a behavioural change.
    /// </para>
    /// </remarks>
    public byte[] RSAEncrypt(byte[] plain, string pubkey, long padding)
    {
        ArgumentNullException.ThrowIfNull(plain);

        // Screen the padding first: an invalid value must be refused before any key is parsed.
        RSAEncryptionPadding resolved = ResolveEncryptionPadding(padding);

        using RSA rsa = ImportKey(pubkey, nameof(pubkey));

        // An oversized payload raises the platform's cryptographic failure from here, propagated
        // unchanged. That refusal is the preserved behaviour; nothing splits, chunks or re-envelopes
        // the payload to avoid it.
        return rsa.Encrypt(plain, resolved);
    }

    #endregion



    // ==========================================================================================
    //  DECRYPTION - n_crypto.sru:L66-L69
    //  ------------------------------------------------------------------------------------------
    //  The exact inverse of the encryption group, overload for overload, and layered the same way
    //  so that decryption too happens in exactly ONE body. The key parameter is a string in all
    //  four, and it must be a PRIVATE key here - which is the one asymmetry with the encryption
    //  group, where either half of a pair works.
    // ==========================================================================================
    #region RSADecrypt - n_crypto.sru:L66-L69

    /// <summary>
    /// Decrypts a text-safe encoded ciphertext with a private key using the preserved default
    /// padding, returning the recovered text.
    /// </summary>
    /// <param name="cipher">
    /// The ciphertext as produced by the string-shaped encrypt, encoded per DECISION D4. An EMPTY
    /// string decodes to no bytes and is refused by the platform as a malformed block.
    /// </param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <returns>The recovered text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well formed for the DECISION D4 encoding.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The block does not decrypt under this key and padding, or the imported key has no private
    /// component.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function string RSADecrypt(readonly string cipher, readonly string
    /// prikey)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L66]. This is the live default-padding
    /// path exercised by the oracle's own demo at u_cst_tabpage_utility_crypto.sru:L720, paired with
    /// the two-argument encrypt at :L744. The padding supplied is
    /// <see cref="LegacyDefaults.RSA_PADDING_DEFAULT"/>, PKCS#1 v1.5 [enums.sru:L951], preserved as
    /// the default: promoting OAEP would make every value the legacy encrypted undecryptable.
    /// </remarks>
    public string RSADecrypt(string cipher, string prikey)
    {
        return RSADecrypt(cipher, prikey, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    /// <summary>
    /// Decrypts a text-safe encoded ciphertext with a private key using an explicitly chosen
    /// padding, returning the recovered text.
    /// </summary>
    /// <param name="cipher">
    /// The ciphertext as produced by the string-shaped encrypt, encoded per DECISION D4.
    /// </param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <param name="padding">
    /// Either <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> or
    /// <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/>, and it MUST match the padding the ciphertext was
    /// produced under.
    /// </param>
    /// <returns>The recovered text.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="padding"/> is outside the two published values, which is also how a
    /// no-padding request is refused.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well formed for the DECISION D4 encoding.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The block does not decrypt under this key and padding, or the imported key has no private
    /// component.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string RSADecrypt(readonly string cipher, readonly string
    /// prikey, readonly long padding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L67].
    /// </para>
    /// <para>
    /// A MALFORMED PAYLOAD SURFACES AS A FAILURE HERE, WHICH IS THE OPPOSITE OF THE VERIFY MEMBERS -
    /// and the asymmetry is deliberate rather than an inconsistency. Decrypt has to return the
    /// recovered text, so it has no false value to collapse a malformed input into; verify returns a
    /// boolean and therefore does. Both behaviours are documented at their own member.
    /// </para>
    /// <para>
    /// THE RECOVERED BYTES ARE TURNED BACK INTO TEXT WITH THE FOLDER'S SHARED SUBSTITUTION, which
    /// is LENIENT: a byte sequence that is not valid in that encoding yields replacement characters
    /// rather than an exception. That is a property of the chosen decoder and is not tightened here,
    /// because the string-shaped contract is text in and text out - a caller holding arbitrary bytes
    /// wants the byte-shaped overload, which performs no conversion at all.
    /// </para>
    /// </remarks>
    public string RSADecrypt(string cipher, string prikey, long padding)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        // Decoded through the shared payload encoding, read from the catalogue rather than named
        // here so that a DECISION D4 re-baseline needs no edit in this file. A malformed payload
        // raises the decoder's format failure and is deliberately NOT swallowed - see the remarks.
        byte[] encrypted = _encoding.StringToBlob(cipher, LegacyDefaults.STRING_PAYLOAD_ENCODING);

        byte[] plain = RSADecrypt(encrypted, prikey, padding);

        return LegacyDefaults.KeyMaterialEncoding.GetString(plain);
    }

    /// <summary>
    /// Decrypts raw ciphertext bytes with a private key using the preserved default padding,
    /// returning the recovered bytes.
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes, with no encoding applied.</param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text - a STRING even on this byte-shaped
    /// overload, exactly as the legacy declares it.
    /// </param>
    /// <returns>The recovered bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The block does not decrypt under this key and padding, or the imported key has no private
    /// component.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function blob RSADecrypt(readonly blob cipher, readonly string
    /// prikey)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L68]. The padding supplied is
    /// <see cref="LegacyDefaults.RSA_PADDING_DEFAULT"/>, PKCS#1 v1.5 [enums.sru:L951]. Per DECISION
    /// D4 a byte-shaped overload carries RAW BYTES and decodes nothing.
    /// </remarks>
    public byte[] RSADecrypt(byte[] cipher, string prikey)
    {
        return RSADecrypt(cipher, prikey, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    /// <summary>
    /// Decrypts raw ciphertext bytes with a private key using an explicitly chosen padding,
    /// returning the recovered bytes. This is the single body in which decryption actually happens.
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes, with no encoding applied.</param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text - a STRING even on this byte-shaped
    /// overload.
    /// </param>
    /// <param name="padding">
    /// Either <see cref="Enums.CRYPTO_RSA_PADDING_PKCS1"/> or
    /// <see cref="Enums.CRYPTO_RSA_PADDING_OAEP"/>, and it MUST match the padding the ciphertext was
    /// produced under.
    /// </param>
    /// <returns>The recovered bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="padding"/> is outside the two published values.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The block does not decrypt under this key and padding - a block of the wrong length, a block
    /// encrypted under a different key, or the wrong padding - or the supplied key has NO PRIVATE
    /// COMPONENT, which a public key does not.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function blob RSADecrypt(readonly blob cipher, readonly string prikey,
    /// readonly long padding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L69]. The other three
    /// decryption overloads reduce to this one.
    /// </para>
    /// <para>
    /// EVERY DECRYPTION FAILURE IS ONE SHAPE, WHICH MATTERS MORE HERE THAN ELSEWHERE. A wrong key, a
    /// wrong padding, a corrupted block and a wrong-length block all raise the same platform
    /// cryptographic failure, and this method deliberately does not classify them. PKCS#1 v1.5 is
    /// vulnerable to adaptive chosen-ciphertext attacks precisely when a service tells a caller
    /// WHICH of those went wrong, so refusing to distinguish them is not merely tidy - it avoids
    /// handing out a padding oracle. That default padding is a preserved legacy weakness
    /// [enums.sru:L951]; not amplifying it costs nothing observable.
    /// </para>
    /// <para>
    /// The padding is resolved before the key is imported, and the imported instance is disposed
    /// before this method returns.
    /// </para>
    /// </remarks>
    public byte[] RSADecrypt(byte[] cipher, string prikey, long padding)
    {
        ArgumentNullException.ThrowIfNull(cipher);

        // Screen the padding first: an invalid value must be refused before any key is parsed.
        RSAEncryptionPadding resolved = ResolveEncryptionPadding(padding);

        using RSA rsa = ImportKey(prikey, nameof(prikey));

        return rsa.Decrypt(cipher, resolved);
    }

    #endregion



    // ==========================================================================================
    //  SIGNING - n_crypto.sru:L70-L71
    //  ------------------------------------------------------------------------------------------
    //  Two overloads, one per payload shape. There is NO padding parameter anywhere in this group,
    //  which is what DECISION R2 exists to settle, and the hash type is screened against the SAME
    //  shared set the hash and HMAC providers use because enums.sru:L927 says it is one set.
    // ==========================================================================================
    #region RSASign - n_crypto.sru:L70-L71

    /// <summary>
    /// Signs text with a private key, returning the signature as a text-safe encoded payload.
    /// </summary>
    /// <param name="data">The text to sign. An EMPTY string is valid and signs an empty digest input.</param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <param name="ntype">
    /// The hash type: <see cref="Enums.CRYPTO_HASH_MD5"/>, <see cref="Enums.CRYPTO_HASH_SHA1"/>,
    /// <see cref="Enums.CRYPTO_HASH_SHA256"/>, <see cref="Enums.CRYPTO_HASH_SHA384"/> or
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>The signature, encoded per DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is outside the six published hash types.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="ntype"/> is <see cref="Enums.CRYPTO_HASH_CRC32"/>. See DECISION R4.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The supplied key has no private component, or its modulus is too small to carry a signature
    /// for the chosen digest.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string RSASign(readonly string data, readonly string prikey,
    /// readonly long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70]. The declared
    /// <c>readonly long ntype</c> maps to <see cref="long"/>, matching the 32-bit width of the
    /// constants that name its legal values [enums.sru:L928-L933].
    /// </para>
    /// <para>
    /// THIS PATH IS LIVE WITH SHA-256. The oracle's own demo calls exactly this string-shaped shape
    /// at u_cst_tabpage_utility_crypto.sru:L470 and feeds the result straight to the string-shaped
    /// verify at :L474, which is what fixes the DECISION D4 encoding for this member: the signature
    /// travels through a plain text control between the two calls, so raw bytes could not survive it.
    /// </para>
    /// <para>
    /// DECISION R2 GOVERNS THE SCHEME. There is no padding parameter, so PKCS#1 v1.5 signature
    /// padding is applied; PSS is not selectable and must not be added. Because PKCS#1 v1.5
    /// signatures are deterministic, the same key, hash and text always produce byte-identical
    /// output here.
    /// </para>
    /// </remarks>
    public string RSASign(string data, string prikey, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);

        byte[] signature = RSASign(LegacyDefaults.KeyMaterialEncoding.GetBytes(data), prikey, ntype);

        return _encoding.BlobToString(signature, LegacyDefaults.STRING_PAYLOAD_ENCODING);
    }

    /// <summary>
    /// Signs bytes with a private key, returning the raw signature bytes. This is the single body in
    /// which signing actually happens.
    /// </summary>
    /// <param name="data">The bytes to sign. An EMPTY array is valid.</param>
    /// <param name="prikey">
    /// The private key, as armoured or bare Base64 text - a STRING even on this byte-shaped
    /// overload, exactly as the legacy declares it.
    /// </param>
    /// <param name="ntype">The hash type; see the string-shaped sibling for the set.</param>
    /// <returns>The raw signature bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="prikey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is outside the six published hash types.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="ntype"/> is <see cref="Enums.CRYPTO_HASH_CRC32"/>. See DECISION R4.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The supplied key has no private component, or its modulus is too small to carry a signature
    /// for the chosen digest - a 512-bit key cannot carry an SHA-512 PKCS#1 v1.5 signature, for
    /// instance, and the legacy would have failed there too.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function blob RSASign(readonly blob data, readonly string prikey,
    /// readonly long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L71]. Per DECISION D4 a
    /// byte-shaped overload carries RAW BYTES and encodes nothing, which is exactly what makes the
    /// byte-shaped verify at :L73 its correct inverse.
    /// </para>
    /// <para>
    /// MD5 AND SHA-1 SIGNING ARE ACCEPTED AND ARE PRESERVED LEGACY WEAKNESSES. Both are broken for
    /// collision resistance, so a signature over either is forgeable by an adversary who can choose
    /// the message. Neither may be removed: they are published members of the shared hash set
    /// [enums.sru:L928-L929], removing either would reject input the legacy accepted, and every
    /// signature already produced under them would stop verifying. They are annotated rather than
    /// deprecated, and this is that annotation.
    /// </para>
    /// <para>
    /// ORDER OF OPERATIONS IS DELIBERATE: the hash type is screened - including the DECISION R4
    /// CRC32 refusal - BEFORE the key is imported, so an unsignable request is refused without the
    /// private key material ever being parsed. The imported instance is disposed before this method
    /// returns.
    /// </para>
    /// </remarks>
    public byte[] RSASign(byte[] data, string prikey, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Screen the hash type first, so a refused request never parses the private key.
        HashAlgorithmName hash = ResolveSignatureHash(ntype);

        using RSA rsa = ImportKey(prikey, nameof(prikey));

        return rsa.SignData(data, hash, SignaturePaddingSubstitute);
    }

    #endregion


    // ==========================================================================================
    //  VERIFICATION - n_crypto.sru:L72-L73
    //  ------------------------------------------------------------------------------------------
    //  Two overloads, and THE ONE PLACE IN THIS FAMILY WHERE A PARAMETER TYPE VARIES: the signature
    //  is `readonly string sign` at L72 and `readonly blob sign` at L73, tracking the payload rather
    //  than the key. Each therefore accepts exactly what its matching RSASign overload returns.
    // ==========================================================================================
    #region VerifyRSASign - n_crypto.sru:L72-L73

    /// <summary>
    /// Verifies a text-safe encoded signature over text, using a public key.
    /// </summary>
    /// <param name="data">The text that was signed.</param>
    /// <param name="sign">
    /// The signature, encoded per DECISION D4 - that is, exactly what the string-shaped
    /// <see cref="RSASign(string, string, long)"/> returns. This parameter is a STRING at L72 and a
    /// byte array at L73; the divergence is the legacy's and is preserved.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text. Keys are always text.
    /// </param>
    /// <param name="ntype">The hash type; it must match the one the signature was produced under.</param>
    /// <returns>
    /// <see langword="true"/> when the signature is valid for this text, key and hash; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is outside the six published hash types.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="ntype"/> is <see cref="Enums.CRYPTO_HASH_CRC32"/>. See DECISION R4.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function boolean VerifyRSASign(readonly string data, readonly string
    /// sign, readonly string pubkey, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L72]. It is the inverse of the string-shaped sign
    /// at :L70, and the oracle's own demo pairs exactly those two
    /// [u_cst_tabpage_utility_crypto.sru:L470 then :L474].
    /// </para>
    /// <para>
    /// A MALFORMED SIGNATURE PAYLOAD RETURNS <see langword="false"/> RATHER THAN THROWING, AND THAT
    /// IS A DELIBERATE CHOICE. Decoding text that is not well formed for the DECISION D4 encoding
    /// would naturally raise a format failure, and it is caught here so that a caller CANNOT
    /// DISTINGUISH "the signature is wrong" from "the signature is not even well formed". Reporting
    /// the difference would hand a caller an oracle the legacy never published, from a boolean
    /// member whose whole contract is one bit. Note the contrast with
    /// <see cref="RSADecrypt(string, string, long)"/>, which DOES surface a malformed payload: that
    /// member has to return recovered text and so has no false value to collapse into.
    /// </para>
    /// <para>
    /// WHAT STILL THROWS, AND WHY THE LINE IS DRAWN THERE. Anything about the SIGNATURE collapses to
    /// <see langword="false"/>. Anything about the CALLER'S OWN ARGUMENTS - a null argument, a hash
    /// type outside the published set, the CRC32 capability limit, or a key that is absent or cannot
    /// be imported - still surfaces as a failure, because those are defects in the request rather
    /// than verdicts on the signature, and silently answering <see langword="false"/> to them would
    /// hide a caller's bug as a security result.
    /// </para>
    /// </remarks>
    public bool VerifyRSASign(string data, string sign, string pubkey, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(sign);

        byte[] signature;

        try
        {
            signature = _encoding.StringToBlob(sign, LegacyDefaults.STRING_PAYLOAD_ENCODING);
        }
        catch (FormatException)
        {
            // A signature payload that is not well formed is a VERIFICATION FAILURE, not an
            // exception: see the remarks. Collapsing it here is what keeps "wrong" and "malformed"
            // indistinguishable to a caller. The exception is deliberately not logged - it carries
            // the caller's payload - and deliberately not rethrown.
            return false;
        }

        return VerifyRSASign(LegacyDefaults.KeyMaterialEncoding.GetBytes(data), signature, pubkey, ntype);
    }

    /// <summary>
    /// Verifies a raw signature over bytes, using a public key. This is the single body in which
    /// verification actually happens.
    /// </summary>
    /// <param name="data">The bytes that were signed.</param>
    /// <param name="sign">
    /// The raw signature bytes - that is, exactly what the byte-shaped
    /// <see cref="RSASign(byte[], string, long)"/> returns, with no encoding applied. This parameter
    /// is a byte array at L73 and a string at L72.
    /// </param>
    /// <param name="pubkey">
    /// The public key, as armoured or bare Base64 text - a STRING even on this byte-shaped overload.
    /// </param>
    /// <param name="ntype">The hash type; it must match the one the signature was produced under.</param>
    /// <returns>
    /// <see langword="true"/> when the signature is valid for these bytes, key and hash; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="pubkey"/> is empty, white space, or cannot be imported.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is outside the six published hash types.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="ntype"/> is <see cref="Enums.CRYPTO_HASH_CRC32"/>. See DECISION R4.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function boolean VerifyRSASign(readonly blob data, readonly blob sign,
    /// readonly string pubkey, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L73].
    /// </para>
    /// <para>
    /// EVERY VERDICT ON THE SIGNATURE IS <see langword="false"/>, WITH NO CLASSIFICATION. A
    /// signature of the wrong length, one produced under a different key, one produced under a
    /// different hash, and one that is simply wrong all answer <see langword="false"/> here, and the
    /// platform's verification primitive is what guarantees that: it is documented to report a
    /// verdict rather than raise on a bad signature, so NO CATCH IS WRAPPED AROUND IT. Adding one
    /// would be unreachable code pretending to be a safeguard.
    /// </para>
    /// <para>
    /// DECISION R2 GOVERNS THE SCHEME, as it does for signing: PKCS#1 v1.5, with no padding
    /// parameter to select anything else. A PSS signature therefore does not verify here, which is
    /// correct - the legacy could not have produced one.
    /// </para>
    /// <para>
    /// A PRIVATE key is accepted in <paramref name="pubkey"/> and verifies correctly, because a
    /// private key carries the public parameters. That is not a hole to close: the legacy would have
    /// accepted it for the same reason, and refusing it would reject input the legacy allowed.
    /// </para>
    /// </remarks>
    public bool VerifyRSASign(byte[] data, byte[] sign, string pubkey, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(sign);

        // Screen the hash type first - including DECISION R4's CRC32 refusal - so an unverifiable
        // request is refused before the key is parsed.
        HashAlgorithmName hash = ResolveSignatureHash(ntype);

        using RSA rsa = ImportKey(pubkey, nameof(pubkey));

        // Reports a verdict rather than raising on a bad or wrong-length signature, which is what
        // keeps every signature-side failure a single indistinguishable false.
        return rsa.VerifyData(data, sign, hash, SignaturePaddingSubstitute);
    }

    #endregion



    // ==========================================================================================
    //  THE FOUR PRIVATE ROUTINES
    //  ------------------------------------------------------------------------------------------
    //  Each is the SINGLE point of its concern for the whole file, and that singularity is the
    //  design rather than a tidiness preference:
    //
    //      ImportKey                 every one of the twelve non-generating overloads imports
    //                                through it, so the accepted key forms CANNOT diverge between
    //                                members and the key-hygiene rules are enforced once
    //      ResolveEncryptionPadding   the one padding screen and the one padding mapping, so
    //                                DECISION R1 and the no-padding refusal each live in one place
    //      ResolveSignatureHash       the one hash screen and the one digest mapping, so DECISION
    //                                R4's CRC32 ruling cannot be applied inconsistently
    //      IsPlatformLegalKeySize     the one key-size question, asked of the platform rather than
    //                                answered from a policy of our own
    //
    //  None holds key material, none writes to a field, and none logs anything.
    // ==========================================================================================
    /// <summary>
    /// Reports which kind of RSA key some material holds, WITHOUT performing any cryptographic
    /// operation with it and without reporting anything about the material itself.
    /// </summary>
    /// <param name="keyText">The key material to classify, exactly as it was configured.</param>
    /// <returns>
    /// <see cref="RsaKeyKind.Unusable"/> when the material imports as no RSA structure at all,
    /// <see cref="RsaKeyKind.PublicOnly"/> when it imports but carries the public half only, and
    /// <see cref="RsaKeyKind.KeyPair"/> when it carries the private half - from which the public half
    /// is derivable, so a key pair satisfies every operation on this surface.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyText"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// THIS IS NOT A LEGACY SUBSTITUTION AND SUBSTITUTES NO OVERLOAD. It exists because the boundary
    /// above this type must answer a question the legacy never had to: <c>n_crypto</c> was handed key
    /// material as an ordinary in-parameter by the same in-process code that owned it, whereas here the
    /// material is CONFIGURED BY THE DEPLOYMENT and reached through an opaque reference the caller
    /// cannot inspect. A key that imports but holds only the public half therefore fails a decrypt or a
    /// signature for a reason that is nobody's fault but the deployment's - and, without this, that
    /// failure is indistinguishable at the boundary from an unprocessable payload, which is the
    /// caller's. Classifying the material lets the boundary answer a server fault as one.
    /// </para>
    /// <para>
    /// EVERY OBSERVABLE BEHAVIOUR OF THE SUBSTITUTED OVERLOADS IS UNTOUCHED. Nothing above calls this
    /// instead of an operation; it is consulted BEFORE one, and the operation then runs exactly as it
    /// did. The import policy is the single one <see cref="ImportKey"/> owns - deliberately, so that a
    /// material this method calls usable cannot then be refused by the operation, and the reverse.
    /// </para>
    /// <para>
    /// IT REPORTS A KIND AND NOTHING ELSE. No length, no fragment, no parameter and no exception detail
    /// crosses back, and nothing is logged: the classification is three states wide precisely so that
    /// it cannot become a channel for the material it inspected. The key size is deliberately not
    /// reported either - the operation's own platform screen owns that question, and 1024 bits remains
    /// a legal size on this surface as a preserved legacy weakness.
    /// </para>
    /// </remarks>
    internal RsaKeyKind ClassifyKey(string keyText)
    {
        ArgumentNullException.ThrowIfNull(keyText);

        RSA imported;

        try
        {
            imported = ImportKey(keyText, nameof(keyText));
        }
        catch (ArgumentException)
        {
            // The single fixed failure ImportKey raises for empty material and for material in no
            // recognised form alike. It is swallowed rather than propagated because this method's
            // whole contract is to answer a question rather than to raise, and because the exception's
            // own message is the one that names the accepted forms - which the boundary must not echo.
            return RsaKeyKind.Unusable;
        }

        try
        {
            return HoldsPrivateHalf(imported) ? RsaKeyKind.KeyPair : RsaKeyKind.PublicOnly;
        }
        finally
        {
            imported.Dispose();
        }
    }

    #region Private routines - one point of decision each

    /// <summary>
    /// Asks an imported key whether it holds its private half, by attempting the one export that
    /// requires it.
    /// </summary>
    /// <param name="key">The imported key.</param>
    /// <returns>
    /// <see langword="true"/> when the private half is present; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// ASKED OF THE PLATFORM RATHER THAN INFERRED FROM KEY PARAMETERS, so the answer is the platform's
    /// own. The exported buffer IS private key material in the clear, so it is zeroed in a finally
    /// block whichever way the attempt went, and it is never returned, measured, inspected or logged -
    /// it exists solely to be discarded. This routine's entire vocabulary is true and false.
    /// <para>
    /// The signing layer carries its own copy of this probe deliberately rather than sharing this one.
    /// That layer runs at STARTUP, raises its own fixed configuration messages and must not depend on
    /// the ported legacy surface at all; coupling the two would put the host's fail-fast gate behind a
    /// type whose reason to exist is legacy parity.
    /// </para>
    /// </remarks>
    private static bool HoldsPrivateHalf(RSA key)
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
    /// Imports a key from text, accepting armoured text or bare Base64-encoded binary in any of the
    /// four structures either form may hold. DECISION R3's dual-accepting import.
    /// </summary>
    /// <param name="keyText">The key text exactly as the caller supplied it.</param>
    /// <param name="parameterName">
    /// The name of the caller's own parameter, so that a failure names <c>prikey</c> or
    /// <c>pubkey</c> rather than an internal name.
    /// </param>
    /// <returns>
    /// A new <see cref="RSA"/> holding the imported key. THE CALLER OWNS IT and must dispose it;
    /// every call site in this file does so with a <see langword="using"/> declaration.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyText"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="keyText"/> is empty or white space, or is not a key this method can import.
    /// </exception>
    /// <remarks>
    /// <para>
    /// WHY ONE ROUTINE. Twelve public overloads import a key. Twelve import sites would be twelve
    /// chances for the accepted set to drift, and DECISION R3 turns on import accepting BOTH forms
    /// unconditionally: a key generated as bare Base64 must work in an overload whose caller expected
    /// armour, and the reverse. One routine is how that is guaranteed rather than hoped for.
    /// </para>
    /// <para>
    /// THE FORM IS DETECTED, NOT GUESSED. Armoured text is identified by the armour opening
    /// sentinel, which is a FORMAT MARKER and not key material - it deliberately stops short of any
    /// armour label, so this file contains no label a secret scan would flag and no label a reader
    /// could mistake for pasted key text. Anything else is treated as bare Base64 and decoded
    /// through the sibling encoder, which tolerates surrounding and embedded white space and so
    /// accepts a key that has been line-wrapped in transit.
    /// </para>
    /// <para>
    /// THE FOUR BARE STRUCTURES ARE TRIED IN EVIDENCE ORDER, and the ordering is short-circuiting so
    /// a successful import does no further work. The traditional private-key structure comes first
    /// because it is what this type exports and what the framework's own stored keys hold; the
    /// algorithm-tagged private-key structure comes next; then the algorithm-tagged public-key
    /// structure, which is the public form this type exports and the framework stored; and finally
    /// the bare public-key structure. Each attempt uses a FRESH instance, so a rejected candidate
    /// can never leave a half-initialized object behind for the next attempt to inherit.
    /// </para>
    /// <para>
    /// KEY HYGIENE, WHICH IS THE WHOLE REASON THIS METHOD IS SHAPED THIS WAY. It never logs, never
    /// echoes and never interpolates <paramref name="keyText"/> or any fragment of it: EVERY failure
    /// carries the one fixed message <see cref="KeyImportFailureMessage"/>, which describes the
    /// accepted forms and says nothing about what was supplied. Where an inner exception is attached
    /// it is one the platform generated - those describe a structure, never their input. And every
    /// rejected candidate instance is DISPOSED on the spot rather than left for finalization, so no
    /// partially imported key lingers.
    /// </para>
    /// </remarks>
    private RSA ImportKey(string keyText, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(keyText, parameterName);

        if (string.IsNullOrWhiteSpace(keyText))
        {
            // A different defect from a failed parse: there is nothing to parse at all.
            throw new ArgumentException(EmptyKeyMessage, parameterName);
        }

        if (keyText.Contains(PemArmourMarker, StringComparison.Ordinal))
        {
            return ImportArmoured(keyText, parameterName);
        }

        byte[] der;

        try
        {
            // Base64 is named explicitly rather than read from the DECISION D4 catalogue: key text
            // is not a D4 payload, and DECISION R3 fixes it at Base64 because the framework's own
            // stored keys are Base64 and must stay importable however D4 is later settled.
            der = _encoding.StringToBlob(keyText, Enums.CRYPTO_ENCODING_BASE64);
        }
        catch (FormatException ex)
        {
            // The text is neither armoured nor valid Base64, so it is not a key this method can
            // read. The message names no part of it.
            throw new ArgumentException(KeyImportFailureMessage, parameterName, ex);
        }

        // THE DECODED STRUCTURE IS WIPED ON EVERY PATH, INCLUDING THE FAILURE PATH. Two of the four
        // structures attempted below are PRIVATE-key structures, so this buffer holds a private key in
        // the clear whenever a caller supplied one. Leaving it for the collector is non-deterministic
        // and may COPY the buffer while compacting, which puts the material in a second place nothing
        // can then reach. The finally also covers the no-import-succeeded throw, which is exactly the
        // path a hand-written wipe after the chain would miss.
        RSA? imported;

        try
        {
            // Tried in evidence order and short-circuited by the null-coalescing chain, so a successful
            // import does no further work. Static lambdas: they capture nothing, so no key material can
            // be closed over.
            imported =
                TryImportDer(der, static (rsa, bytes) => rsa.ImportRSAPrivateKey(bytes, out _)) ??
                TryImportDer(der, static (rsa, bytes) => rsa.ImportPkcs8PrivateKey(bytes, out _)) ??
                TryImportDer(der, static (rsa, bytes) => rsa.ImportSubjectPublicKeyInfo(bytes, out _)) ??
                TryImportDer(der, static (rsa, bytes) => rsa.ImportRSAPublicKey(bytes, out _));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(der);
        }

        if (imported is null)
        {
            throw new ArgumentException(KeyImportFailureMessage, parameterName);
        }

        return imported;
    }

    /// <summary>
    /// Imports a key from armoured text.
    /// </summary>
    /// <param name="keyText">The armoured key text.</param>
    /// <param name="parameterName">The caller's own parameter name, for the failure.</param>
    /// <returns>A new <see cref="RSA"/> the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentException">The armoured text could not be imported.</exception>
    /// <remarks>
    /// Split out from <see cref="ImportKey"/> only so that the bare-Base64 path reads as one
    /// straight line; it is not a second import policy and is called from exactly one place. The
    /// platform import handles every armour label the two exported structures can carry, plus the
    /// algorithm-tagged variants of each, so a caller's armoured key imports whichever of the four
    /// it holds. TWO exception types are caught because the platform raises a structural failure for
    /// unreadable content and an argument failure for text carrying no usable block at all; both
    /// mean the same thing to a caller and are therefore reported as the same one fixed message,
    /// which names no part of the key.
    /// </remarks>
    private static RSA ImportArmoured(string keyText, string parameterName)
    {
        RSA candidate = RSA.Create();

        try
        {
            candidate.ImportFromPem(keyText);

            return candidate;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            candidate.Dispose();

            throw new ArgumentException(KeyImportFailureMessage, parameterName, ex);
        }
    }

    /// <summary>
    /// Attempts one bare-binary key structure, returning <see langword="null"/> rather than raising
    /// when that structure is not the one the bytes hold.
    /// </summary>
    /// <param name="der">The decoded key bytes.</param>
    /// <param name="import">The one platform import to attempt.</param>
    /// <returns>
    /// A new <see cref="RSA"/> the caller owns when the attempt succeeded; otherwise
    /// <see langword="null"/>, with the candidate instance already disposed.
    /// </returns>
    /// <remarks>
    /// A FRESH INSTANCE PER ATTEMPT is the point of this method. Reusing one instance across
    /// attempts would rely on a rejected import leaving it pristine, which is an assumption about
    /// platform internals rather than a documented guarantee. Disposing each rejected candidate
    /// immediately also means no partially imported key waits for finalization. The failure is
    /// swallowed deliberately and is NOT logged: it would carry the caller's key text, and the
    /// caller learns of a total failure from <see cref="ImportKey"/>'s single fixed message instead.
    /// </remarks>
    private static RSA? TryImportDer(byte[] der, Action<RSA, byte[]> import)
    {
        RSA candidate = RSA.Create();

        try
        {
            import(candidate, der);

            return candidate;
        }
        catch (CryptographicException)
        {
            candidate.Dispose();

            return null;
        }
    }

    /// <summary>
    /// Screens a caller's padding identifier against the two the legacy publishes and maps it onto
    /// the platform padding. This is where a no-padding request is refused and where DECISION R1 is
    /// applied.
    /// </summary>
    /// <param name="padding">The caller's padding identifier.</param>
    /// <returns>The platform padding to use.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="padding"/> is outside <see cref="LegacyDefaults.IsSupportedRsaPadding"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SCREENED THROUGH THE SHARED CATALOGUE, NEVER AGAINST LOCAL LITERALS. That is what keeps this
    /// file's accepted set identical to every sibling provider's, and it is also why no
    /// <c>CRYPTO_</c>-prefixed identifier is declared here.
    /// </para>
    /// <para>
    /// THE NO-PADDING REFUSAL IS PRESERVED LEGACY BEHAVIOUR, NOT A HARDENING CHOICE. The legacy
    /// declares exactly two padding values [enums.sru:L949-L950], no constant for no-padding, and
    /// rejects a request for it; <see cref="LegacyDefaults.RSA_NO_PADDING_SUPPORTED"/> records that,
    /// and this method is the gate that enforces it. Textbook RSA with no padding is catastrophically
    /// weak, so refusing it is also the right outcome - but the REASON it is refused here is that the
    /// legacy refused it, and the message must not claim otherwise.
    /// </para>
    /// <para>
    /// THE CONDITIONAL IS TOTAL AND HAS NO DEAD ARM. The screen above admits exactly two values, so
    /// the two branches below cover every value that reaches them; an unreachable default would be
    /// dead code left permanently uncovered. OAEP maps to <see cref="OaepPaddingSubstitute"/>, which
    /// is DECISION R1's single naming point - it is neither removed nor promoted to the default.
    /// </para>
    /// </remarks>
    private static RSAEncryptionPadding ResolveEncryptionPadding(long padding)
    {
        if (!LegacyDefaults.IsSupportedRsaPadding(padding))
        {
            throw new ArgumentOutOfRangeException(nameof(padding), padding, UnsupportedPaddingMessage);
        }

        return padding == Enums.CRYPTO_RSA_PADDING_PKCS1
            ? RSAEncryptionPadding.Pkcs1
            : OaepPaddingSubstitute;
    }

    /// <summary>
    /// Screens a caller's hash identifier against the six the legacy publishes and maps it onto the
    /// platform digest name. This is where DECISION R4's CRC32 ruling is applied.
    /// </summary>
    /// <param name="ntype">The caller's hash identifier.</param>
    /// <returns>The platform digest name to sign or verify with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is outside <see cref="LegacyDefaults.IsSupportedHashType"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="ntype"/> is <see cref="Enums.CRYPTO_HASH_CRC32"/>, for which no RSA signature
    /// construction exists.
    /// </exception>
    /// <remarks>
    /// <para>
    /// ONE SHARED SET, THREE PROVIDERS, BECAUSE THE LEGACY SAYS SO. enums.sru:L927 records in the
    /// source itself that the same six values parameterise <c>Hash</c>, <c>RSASign</c> AND
    /// <c>VerifyRSASign</c>, so this method screens through
    /// <see cref="LegacyDefaults.IsSupportedHashType"/> and declares NO second allowed set.
    /// </para>
    /// <para>
    /// DECISION R4, AND WHY IT USES A DIFFERENT FAILURE TYPE FROM THE ONE ABOVE IT. CRC32 is INSIDE
    /// the published set [enums.sru:L933], so it passes the screen; what does not exist is an
    /// RSA-over-CRC32 signature construction - CRC32 is a checksum with no collision resistance, no
    /// digest-algorithm identifier for a signature structure and no platform implementation. That
    /// makes it a CAPABILITY LIMIT rather than an argument-domain error, and the two are reported
    /// with different exception types precisely so a caller can tell "you passed a value that does
    /// not exist" from "you passed a real value for which this operation does not exist". Nothing is
    /// invented: a fabricated construction would produce a signature that verifies nothing while
    /// looking exactly like a real one. What the closed binary actually did is unobservable and no
    /// legacy call site exercises it - the demo signs with SHA-256
    /// [u_cst_tabpage_utility_crypto.sru:L470, :L474] - so the oracle would settle it. THE SIBLING
    /// HMAC PROVIDER'S KEYED CRC32 ARM FACES THE IDENTICAL PROBLEM AND MUST REFUSE IN THE IDENTICAL
    /// SHAPE.
    /// </para>
    /// <para>
    /// MD5 AND SHA-1 ARE MAPPED, NOT REFUSED. Both are broken for collision resistance and both are
    /// preserved: they are published members of the set [enums.sru:L928-L929], and refusing either
    /// would reject input the legacy accepted and invalidate every signature already made under it.
    /// </para>
    /// <para>
    /// THE SWITCH IS TOTAL AND HAS NO DEAD ARM. The screen admits six values and the CRC32 refusal
    /// removes one, leaving exactly the five mapped below; the final arm therefore carries SHA-512
    /// rather than an unreachable failure that could never be covered by a test.
    /// </para>
    /// </remarks>
    private static HashAlgorithmName ResolveSignatureHash(long ntype)
    {
        if (!LegacyDefaults.IsSupportedHashType(ntype))
        {
            throw new ArgumentOutOfRangeException(nameof(ntype), ntype, UnsupportedHashTypeMessage);
        }

        if (ntype == Enums.CRYPTO_HASH_CRC32)
        {
            // DECISION R4. Deliberately a different type from the screen above: this value IS
            // published, and it is the construction that does not exist.
            throw new NotSupportedException(Crc32SignatureMessage);
        }

        // Total over the five values that can still reach here, so the final arm is SHA-512 rather
        // than an unreachable throw.
        return ntype switch
        {
            Enums.CRYPTO_HASH_MD5 => HashAlgorithmName.MD5,
            Enums.CRYPTO_HASH_SHA1 => HashAlgorithmName.SHA1,
            Enums.CRYPTO_HASH_SHA256 => HashAlgorithmName.SHA256,
            Enums.CRYPTO_HASH_SHA384 => HashAlgorithmName.SHA384,
            _ => HashAlgorithmName.SHA512,
        };
    }

    /// <summary>
    /// Reports whether the platform will generate an RSA key of <paramref name="candidateBits"/>
    /// bits.
    /// </summary>
    /// <param name="candidateBits">The requested modulus size in bits.</param>
    /// <returns>
    /// <see langword="true"/> when the platform accepts the size; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS IS NOT A MINIMUM-SIZE POLICY, and the distinction is the whole reason it asks the
    /// platform instead of holding a table. 1024 bits is legal [enums.sru:L965] and is exercised by
    /// the oracle's own demo [u_cst_tabpage_utility_crypto.sru:L699], so it must be accepted here;
    /// <see cref="LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED"/> records that no floor is imposed
    /// anywhere. The three published sizes are convenience values rather than an allowed set, which
    /// is why the shared catalogue deliberately publishes no key-size predicate for this method to
    /// call - any such predicate would narrow a contract that accepts any size.
    /// </para>
    /// <para>
    /// WHY ASK RATHER THAN ASSUME. The legal sizes are a platform property, not a framework one:
    /// they differ between the cryptographic providers the runtime sits on, in the step between
    /// permitted sizes as well as in the bounds. Asking means this method needs no update when it
    /// runs somewhere new, and it means a refusal reflects a real inability rather than a rule of
    /// our own invention. The probe instance generates no key - it is consulted for its size ranges
    /// and disposed - so the check costs nothing and is safe to run before every generation.
    /// </para>
    /// <para>
    /// A size of zero, an odd size, or a size beyond the platform's ceiling all answer
    /// <see langword="false"/>, which the caller reports through the legacy's own boolean return.
    /// </para>
    /// </remarks>
    private static bool IsPlatformLegalKeySize(int candidateBits)
    {
        using RSA probe = RSA.Create();

        foreach (KeySizes range in probe.LegalKeySizes)
        {
            // A zero step describes a single permitted size rather than a range; every other step
            // describes an arithmetic progression from the lower bound.
            if (candidateBits >= range.MinSize &&
                candidateBits <= range.MaxSize &&
                (range.SkipSize == 0
                    ? candidateBits == range.MinSize
                    : (candidateBits - range.MinSize) % range.SkipSize == 0))
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}

/// <summary>
/// Which half of an RSA key pair some configured material holds.
/// </summary>
/// <remarks>
/// <para>
/// THREE STATES, BECAUSE THE BOUNDARY ABOVE OWES A DIFFERENT ANSWER TO EACH. Material that imports as no
/// RSA structure at all and material that imports as a public key are both deployment faults when a
/// private half is required, but they are different faults - one says the configured value is not a key,
/// the other says it is the wrong one - and a deployment fixing either needs to know which.
/// </para>
/// <para>
/// A KEY PAIR SATISFIES EVERY OPERATION ON THIS SURFACE, which is why there is no fourth state for
/// "private only": the public half is derivable from the private one, so an encrypt or a verification
/// handed a key pair succeeds exactly as it would with the public half alone. Only the reverse
/// direction - a private half required and a public-only key supplied - is a mismatch.
/// </para>
/// <para>
/// IT CARRIES NO MEASUREMENT OF THE MATERIAL. No size, no structure name and no fragment: the whole
/// point of answering in three states is that the answer cannot become a channel for what was inspected.
/// </para>
/// </remarks>
internal enum RsaKeyKind
{
    /// <summary>
    /// The material imports as no RSA key structure in either accepted form. A deployment fault.
    /// </summary>
    Unusable,

    /// <summary>
    /// The material imports and carries the PUBLIC half only. Sufficient for encryption and for
    /// signature verification, and a deployment fault for decryption or signing.
    /// </summary>
    PublicOnly,

    /// <summary>
    /// The material imports and carries the PRIVATE half, so the public half is derivable from it.
    /// Sufficient for every operation on this surface.
    /// </summary>
    KeyPair,
}
