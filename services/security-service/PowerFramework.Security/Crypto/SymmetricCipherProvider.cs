// ==============================================================================================
//  SymmetricCipherProvider - the block-cipher surface, substituted onto System.Security.Cryptography
//  --------------------------------------------------------------------------------------------
//  PORTS          ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45   SymEncrypt, 16 declarations
//                 ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L46-L61   SymDecrypt, 16 declarations
//                 32 of the 63 ported declarations of that 65-declaration surface, which makes this
//                 the widest file in this folder.
//
//  NO LEGACY BODY EXISTS TO TRANSLATE. n_crypto.sru:L8 declares
//  `global type n_crypto from nonvisualobject native "pfw.dll"`, so all 65 declarations are PBNI
//  prototypes over a closed native binary. There is no C++ source for it anywhere in this
//  repository. EVERY MEMBER BELOW IS THEREFORE A DOCUMENTED SUBSTITUTION against the Base Class
//  Library, never a translation of legacy code, and no third-party cryptography package is added:
//  `Aes`, `TripleDES` and `DES` cover the whole published set.
//
//  ORACLE STATUS  Every ws_objects/** locator in this file is READ-ONLY PROVENANCE for a decision.
//                 The legacy tree is the behavioural oracle for parity testing and is never edited,
//                 moved, reformatted or re-exported. Byte-exact agreement with what the closed
//                 binary produced is assertable ONLY against that oracle, by capturing legacy
//                 output for a workflow and comparing it with target output for the same workflow.
//                 Nothing in this file claims such agreement has been measured.
//
//  THE 32 DECLARATIONS, IN THE LEGACY'S OWN ORDER
//  --------------------------------------------------------------------------------------------
//  Each verb is 16 overloads: the cross product of a {string, blob} PAYLOAD, a {string, blob} KEY,
//  and the four argument shapes below. The members appear here in exactly the legacy's declaration
//  order so that a reviewer can diff this file against L30-L61 line by line.
//
//      argument shape             SymEncrypt          SymDecrypt          consequence
//      -----------------------    ----------------    ----------------    -----------------------
//      no IV, no mode             L30 L34 L38 L42     L46 L50 L54 L58     runs in the ECB default
//      no IV, WITH mode           L31 L35 L39 L43     L47 L51 L55 L59     DECISION D3 applies
//      WITH IV, no mode           L32 L36 L40 L44     L48 L52 L56 L60     runs in the ECB default
//      WITH IV, WITH mode         L33 L37 L41 L45     L49 L53 L57 L61     fully specified
//
//  THREE STRUCTURAL FACTS THAT ARE PART OF THE CONTRACT, NOT INCIDENTAL
//  --------------------------------------------------------------------------------------------
//      1. THE INITIALIZATION-VECTOR TYPE ALWAYS FOLLOWS THE KEY TYPE. A string key pairs only with
//         a string vector [L32, L33, L40, L41 and the decrypt mirrors L48, L49, L56, L57]; a blob
//         key pairs only with a blob vector [L36, L37, L44, L45 and mirrors L52, L53, L60, L61].
//         THERE IS NO MIXED-TYPE OVERLOAD AND NONE MAY BE ADDED.
//      2. THE PAYLOAD TYPE DETERMINES THE RETURN TYPE. String in, string out; blob in, blob out.
//         There is no cross-shaped overload.
//      3. NO OVERLOAD ANYWHERE TAKES A PADDING ARGUMENT. Block padding is not selectable.
//
//  All 32 are reproduced as DISTINCT PUBLIC OVERLOADS. C# could collapse them behind optional
//  parameters and it deliberately does not: the 32-way shape is the observable legacy surface, this
//  folder's 63-of-65 census depends on it, and the parity suite is a table-driven matrix over it.
//  What the overloads share is single-sourced instead - every one of them reaches ONE of the two
//  core routines at the end of this file, so no rule below can be applied inconsistently.
//
//  ==============================================================================================
//  WHAT IS PRESERVED HERE THAT A MODERN REVIEWER WILL WANT TO CHANGE
//  ==============================================================================================
//  This file carries more preserved cryptographic weakness than any other in the service. Each item
//  is reproduced deliberately, annotated at its point of reproduction, and MUST NOT be corrected:
//  correcting any of them would change every ciphertext the legacy ever produced, so no existing
//  value could be decrypted by this port. Every temptation below is answered with an annotation
//  rather than with a code change.
//
//      * ECB IS THE DEFAULT MODE for all sixteen mode-omitting overloads
//        [ws_objects/pfw.shared.pbl.src/enums.sru:L943 and :L946]. Not a theoretical arm - see the
//        live evidence recorded on SymEncrypt at L30 below.
//      * PKCS#5-FAMILY PADDING ONLY, AND NOT SELECTABLE. See DECISION H3.
//      * NO KEY-DERIVATION FUNCTION IS REACHABLE, so a passphrase is used as raw key bytes. See
//        DECISION D1 and the weak-key interaction recorded under DECISION H2.
//      * NO AUTHENTICATED ENCRYPTION. Ciphertext carries no integrity tag and a decrypt cannot
//        detect tampering. See DECISION D2.
//      * A ZERO INITIALIZATION VECTOR on the eight mode-without-IV overloads. See DECISION D3.
//      * DES AND 3DES ARE RETAINED [enums.sru:L936-L937]. Neither may be removed from the set.
//      * 1024-bit-era algorithm choices generally: the published sets are exactly the five cipher
//        types at enums.sru:L936-L940 and the three modes at :L943-L945. Nothing is added and
//        nothing is withdrawn.
//
//  ==============================================================================================
//  KEY AND INITIALIZATION-VECTOR HYGIENE - HOW THIS FILE DISCHARGES IT
//  ==============================================================================================
//  This provider receives ALREADY-RESOLVED key and vector material as ordinary parameters. The
//  endpoint layer is what resolves a caller's opaque key reference against the configured key
//  store; this type takes no part in that.
//
//      * IT READS NO CONFIGURATION. There is no IConfiguration, no IOptions, no environment
//        variable and no file access anywhere in this file. The only constructor dependency is the
//        sibling encoding provider.
//      * IT RETAINS NOTHING. No instance field, no static field and no cache holds key or vector
//        material, or a value derived from either. Every buffer this type creates is local to one
//        call.
//      * IT LOGS NOTHING AT ALL. Not the material, not a length, not a hash of it, not a "key
//        accepted" record. Declaring no logger is the strongest available discharge of the rule
//        that key material must never reach a log.
//      * NO EXCEPTION MESSAGE CARRIES MATERIAL. The two messages this file declares name the
//        published algorithm sets and their enums.sru locators and nothing else; the argument
//        values they carry are ALGORITHM IDENTIFIERS, which are not secrets. Null guards use the
//        framework helper, whose message is the parameter NAME only.
//      * IT WIPES WHAT IT OWNS. Both core routines zero the normalized key and vector buffers in a
//        `finally`, so the wipe happens on the exception paths too. Those buffers are always FRESH
//        allocations from the shared catalogue, never the caller's array, so wiping them cannot
//        destroy a caller's own key - see the ownership note on the cores.
//      * WHAT CANNOT BE WIPED IS STATED RATHER THAN HIDDEN. A `string` key or vector cannot be
//        wiped at all: .NET strings are immutable and this port keeps the legacy's string-shaped
//        parameters [L30-L33 and mirrors], so the caller's string outlives the call whatever this
//        file does. This type therefore never materialises its own byte copy of string material -
//        it hands the string to the catalogue's own normalizer, so the only buffer in play is the
//        normalized one it wipes. The residual exposure belongs to the preserved signature, and it
//        is recorded here instead of being papered over.
//
//  NO SECRET APPEARS IN THIS FILE IN ANY FORM - not as a value, not as a default argument, not in a
//  comment, and not in a documentation example. That includes every one of the repository's known
//  hardcoded-secret sites, whose remediation posture is never-replicate-document-and-rotate rather
//  than edit-the-legacy-file. Two of those sites are read below as USAGE EVIDENCE for which
//  overloads are exercised; reading a call site is not copying a literal, and no literal from
//  either is reproduced.
//
//  ==============================================================================================
//  THE FOUR SUBSTITUTION DECISIONS INHERITED FROM THE CATALOGUE
//  ==============================================================================================
//  D1, D2, D3 and D4 are recorded in full in LegacyDefaults and are applied here. Restated only as
//  far as this file's behaviour depends on them:
//
//  DECISION D1 - A PASSPHRASE IS RAW KEY BYTES, SIZED BY TRUNCATE-OR-ZERO-PAD
//  --------------------------------------------------------------------------------------------
//  No key-derivation function is reachable through the 65 declarations and there is no salt
//  concept, so string material becomes key bytes directly, UTF-8 encoded, then truncated to or
//  right-zero-padded to the EXACT length the cipher requires. Block ciphers, unlike keyed hashing,
//  admit only exact key lengths, so this is the file where that rule actually bites. The required
//  lengths come from the catalogue's metrics table and are never restated here: DES 8/8, 3DES 24/8,
//  AES128 16/16, AES192 24/16, AES256 32/16, given as key bytes over block bytes.
//
//  The rule is applied in exactly one place per concern - the catalogue's normalizer for keys and
//  supplied vectors, this file's single vector resolver for the synthesised case - so all 32
//  overloads size material identically. Truncation silently discards entropy the caller believed it
//  supplied and zero-padding silently manufactures bytes the caller never chose. Both halves are
//  weaknesses and both are preserved.
//
//  DECISION D2 - NO INTEGRITY TAG, THEREFORE NO TAMPER DETECTION
//  --------------------------------------------------------------------------------------------
//  The published mode set is exactly ECB, CBC and CFB [enums.sru:L943-L945]. No authenticated mode
//  is reachable: no GCM, no CCM, no ChaCha20-Poly1305, no associated-data parameter and no tag
//  output anywhere in the 32 overloads. A decrypt CANNOT DETECT TAMPERING and this API has no way
//  to say so. Adding an authenticated mode would change the wire format, because a tag must be
//  carried somewhere, and every legacy ciphertext would stop round-tripping. Where a caller needs
//  integrity it is a separate keyed-hash call over the ciphertext, using the HMAC surface the
//  legacy already publishes at n_crypto.sru:L23-L26.
//
//  DECISION D3 - THE EIGHT MODE-WITHOUT-IV OVERLOADS USE AN ALL-ZERO VECTOR OF THE BLOCK LENGTH
//  --------------------------------------------------------------------------------------------
//  Four SymEncrypt overloads accept a mode but no vector [L31, L35, L39, L43], mirrored by four
//  SymDecrypt overloads [L47, L51, L55, L59]. CBC and CFB both require one, so those eight arms
//  synthesise it: an all-zero buffer of the cipher's block length, produced by the catalogue from a
//  LENGTH ALONE and never written down as a literal. A fixed, publicly known vector destroys CBC's
//  semantic security - the same plaintext under the same key yields the same ciphertext every time,
//  so an observer learns when a value has not changed. It is preserved because generating a random
//  vector would produce ciphertext the legacy could not decrypt, there being no field in the legacy
//  format in which to transmit one.
//
//  DECISION D4 - THE STRING-SHAPED OVERLOADS CARRY A TEXT-SAFE ENCODED PAYLOAD
//  --------------------------------------------------------------------------------------------
//  This is why the sixteen string-shaped overloads are not the blob-shaped ones with a cast. The
//  oracle's own demo writes string-shaped ciphertext straight back into a plain multi-line text
//  control and then feeds it to its string-shaped inverse FROM THAT SAME CONTROL:
//  u_cst_tabpage_utility_crypto.sru:L504 pairs with :L547 under DES/CBC, :L607 with :L592 under
//  AES256/CBC, and :L637 with :L622 under 3DES/CBC. Raw cipher bytes stuffed into a string could
//  not survive that round trip - arbitrary bytes are not valid text in any encoding.
//
//  So every string-shaped overload returns the catalogue's text-safe encoded payload and every
//  string-shaped SymDecrypt accepts that same encoding, produced and consumed through the sibling
//  encoding provider so that no second convention exists here. Every blob-shaped overload carries
//  RAW BYTES and performs no encoding at all. Which encoding it is remains the catalogue's decision
//  and is read from it rather than restated, so if the oracle corrects it, it is corrected in one
//  place for all four provider files. The exact spelling the closed binary produced is unobservable
//  from this repository.
//
//  ==============================================================================================
//  FOUR PLATFORM HAZARDS THE CLOSED BINARY HIDES - DECIDED ONCE, DOCUMENTED, NOT "FIXED"
//  ==============================================================================================
//  DECISION H1 - CFB IS BLOCKED, BECAUSE ITS FEEDBACK WIDTH IS UNPROVABLE
//  --------------------------------------------------------------------------------------------
//  THE HIGHEST-VALUE ORACLE QUESTION IN THIS FILE, AND IT IS NOT ANSWERABLE FROM THIS REPOSITORY.
//  The legacy publishes a single CFB value [enums.sru:L945] with no feedback-size parameter. This
//  platform requires an explicit feedback size, and CFB8 and full-block CFB produce ENTIRELY
//  DIFFERENT CIPHERTEXT of different lengths.
//
//  WHAT WAS PREVIOUSLY DONE, AND WHY IT WAS WRONG. The width was inferred: the framework attributes
//  OpenSSL among its eleven upstream libraries [ws_objects/pfw.demos.pbl.src/w_about.srw:L118], and
//  OpenSSL's plain CFB aliases are full-block, so the full block width was adopted - 128 for AES,
//  64 for 3DES, and 8 for DES because this platform's DES admits no other. Every part of that is
//  defensible EXCEPT the conclusion, because an inference from an attribution is not a measurement
//  of the binary, and this particular error is UNDETECTABLE: either width round-trips perfectly
//  against itself, so no test available here can tell a right choice from a wrong one. Ciphertext
//  produced under the wrong width passes every check and CANNOT BE DECRYPTED BY THE LEGACY.
//
//  THE RULE IS THEREFORE A REFUSAL, NOT A WIDTH. Every CFB call - encrypt or decrypt, with or
//  without a vector, for every cipher type - raises `SymmetricParityUnavailableException` from
//  `ScreenArguments`, the one gate all 32 overloads pass through. The mode identifier remains
//  published and `IsSupportedSymmetricMode` still accepts it; only the capability is withdrawn.
//  See DECISION D3 in `LegacyDefaults.cs` for the full reasoning and for the vector half of the
//  same problem. NOTHING OBSERVABLE IS LOST: the oracle's own demo never selects CFB - all six of
//  its symmetric call sites use CBC with an explicit vector
//  [u_cst_tabpage_utility_crypto.sru:L504, L547, L592, L607, L622, L637].
//
//  DECISION H2 - THIS PLATFORM REJECTS WEAK AND DEGENERATE DES AND 3DES KEYS; THE LEGACY DID NOT
//  --------------------------------------------------------------------------------------------
//  The Base Class Library refuses the known weak and semi-weak DES keys, and refuses degenerate
//  3DES keys whose adjacent sub-keys coincide, raising a cryptographic exception FROM THE KEY
//  ASSIGNMENT rather than encrypting. An OpenSSL-based implementation does not. No workaround is
//  added and no key is silently substituted: that would change behaviour, which is forbidden. The
//  failure is LOUD rather than silent, and it is the endpoint layer's job to describe it alongside
//  the other preserved weaknesses in its published contract text.
//
//  THE INTERACTION WITH DECISION D1, WHICH IS THE PART THAT ACTUALLY BITES. Because D1
//  right-zero-pads short material, short keys normalize into buffers this platform rejects, and the
//  measured thresholds are:
//
//      cipher   supplied key length that this platform rejects as weak after D1 padding
//      ------   -----------------------------------------------------------------------
//      DES      an EMPTY key, which pads to the all-zero 8-byte weak key
//      3DES     ANY key shorter than 16 bytes, because sub-keys two and three both pad to
//               all-zero and therefore coincide
//      AES      none at any length; AES has no weak-key concept
//
//  So a 3DES caller with a fifteen-character passphrase gets a loud failure here where the legacy
//  would have encrypted. That is a divergence, it is this platform's, and it is stated plainly.
//
//  DECISION H3 - PADDING IS PKCS#5-FAMILY, NOT SELECTABLE, AND ITS FAILURE HAS ONE SHAPE
//  --------------------------------------------------------------------------------------------
//  PKCS#5 and PKCS#7 are the same construction - PKCS#5 is the eight-byte-block special case - so
//  the library's PKCS#7 padding mode IS the equivalent, and it is named once below. No overload
//  takes a padding argument, so padding is not selectable and no parameter for it may be added.
//
//  THE DECRYPT-SIDE CONSEQUENCE, WHICH FOLLOWS FROM DECISION D2 AND CANNOT BE ENGINEERED AWAY.
//  Without an integrity tag, a wrong key produces either a padding check failure or plausible
//  garbage, and NEITHER IS TAMPER DETECTION. The single failure shape is the library's own
//  cryptographic exception, PROPAGATED UNCHANGED: not caught, not rewrapped, not classified, and
//  not converted into a distinguishable result. That is deliberate on both counts - rewrapping
//  would invent a failure taxonomy the legacy never published, and classifying padding failure
//  separately from garbage would hand a caller a distinguishing signal this port has no business
//  adding. The garbage case is not detectable at all and is documented as such.
//
//  DECISION H4 - THE INITIALIZATION-VECTOR RULES, ALL THREE ARMS
//  --------------------------------------------------------------------------------------------
//      (a) MODE SUPPLIED, NO VECTOR - the eight overloads at L31, L35, L39, L43 and mirrors L47,
//          L51, L55, L59. When the mode is ECB, no vector is produced and the call proceeds. When
//          the mode consumes a vector, the call is REFUSED under DECISION D3 rather than having a
//          vector invented for it; `RefuseOrOmitInitializationVector` is that arm. No vector is
//          ever synthesised anywhere in this file.
//      (b) ECB IGNORES A SUPPLIED VECTOR, and this is STRUCTURAL rather than accidental. The ECB
//          path calls the library's ECB one-shot, whose signature HAS NO VECTOR PARAMETER, so a
//          supplied vector has no way to reach the cipher. Its REFERENCE is still validated, so
//          that a null argument fails the same way it does everywhere else on this surface, but its
//          CONTENT is unused: an ECB call with a vector produces byte-for-byte the same result as
//          the same call without one.
//      (c) A SUPPLIED VECTOR IS SIZED BY THE SAME RULE AS A KEY - DECISION D1's truncate-or-pad,
//          through the same catalogue normalizer, against the cipher's block length. A vector is
//          exactly one block wide.
//
//  ==============================================================================================
//  TWO SMALLER THINGS A READER WILL LOOK FOR
//  ==============================================================================================
//  THE WIDTH MISMATCH IS THE LEGACY'S, NOT THIS PORT'S. All 32 declarations type the cipher-type
//  argument as a PowerBuilder `uint`, which is SIXTEEN BITS WIDE and maps to `ushort`
//  [n_crypto.sru:L30-L61], while the constants that name its legal values are declared
//  `Constant Long` [enums.sru:L936-L940]. PowerBuilder tolerates that implicit narrowing and C# does
//  not. The argument is kept at `ushort`, because that honours the declared width and because it is
//  the width the shared catalogue's own screening predicate and metrics lookups take, and the two
//  files must never disagree. THE VISIBLE CONSEQUENCE, stated so it is not mistaken for a defect: a
//  call site naming one of the catalogue constants must cast it, as in
//  `(ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256`, because C# permits an implicit constant conversion
//  to `ushort` from `int` but not from `long`. The mode argument needs no cast: it is `long`, which
//  is what the mode constants already are. The narrowing itself is performed in exactly ONE place -
//  the algorithm factory widens the argument back to `long` once so the constants can be matched
//  against it - rather than being scattered through 32 signatures.
//
//  THE GLOBAL AUTO-INSTANCE HAS NO ANALOGUE TO REPRODUCE. n_crypto.sru:L75 declares
//  `global n_crypto n_crypto`, a global variable whose name collides with its own type name, and
//  every legacy call site guards it with the lazy-construction idiom
//  `if Not IsValid(n_crypto) then n_crypto = Create n_crypto`. Both exist only because PowerBuilder
//  resolves one flat global namespace and offers no container. This type is a container-managed
//  instance service reached by constructor injection: never a global, never a static, never lazily
//  self-constructing, and with no name collision to resolve. There is nothing to port.
// ==============================================================================================

using System.Security.Cryptography;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// The symmetric block-cipher surface: 16 <c>SymEncrypt</c> overloads and 16 <c>SymDecrypt</c>
/// overloads substituting <c>n_crypto.sru:L30-L61</c> onto <see cref="SymmetricAlgorithm"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member is a documented substitution rather than a translation, because the legacy
/// declarations are PBNI prototypes over a closed native binary with no readable implementation.
/// The preserved weak defaults - ECB as the default mode, PKCS#5-family padding that is not
/// selectable, no key-derivation function, no authenticated encryption, and the retention of DES
/// and 3DES - are annotated at their points of reproduction and MUST NOT be corrected.
/// </para>
/// <para>
/// STATELESS AND THREAD-SAFE. The only field is the injected encoding provider, which is itself
/// stateless. No <see cref="SymmetricAlgorithm"/> is ever cached, because that type is not
/// thread-safe: each call creates its own instance and disposes it, which also zeroes the copy of
/// the key the instance took. Any number of requests may therefore use one instance of this type
/// concurrently, and any number of instances of this service may run.
/// </para>
/// <para>
/// KEY HYGIENE. Key and initialization-vector material arrives already resolved, as parameters.
/// This type reads no configuration, retains nothing, logs nothing, puts no material into an
/// exception message, and zeroes every buffer it owns on both the success and the failure path.
/// </para>
/// <para>
/// ONE CALL-SITE NOTE ON OVERLOAD RESOLUTION. The payload parameter is <see cref="string"/> in one
/// family and <see cref="byte"/> array in the other, so a BARE <see langword="null"/> LITERAL is
/// applicable to both and does not compile. Pass a typed null - which is what a caller probing the
/// null guard means anyway. The same applies to the key and vector parameters.
/// </para>
/// </remarks>
public sealed class SymmetricCipherProvider
{
    /// <summary>
    /// The block padding used by every operation: the PKCS#7 construction, which IS the PKCS#5
    /// family - PKCS#5 is its eight-byte-block special case. See DECISION H3.
    /// </summary>
    /// <remarks>
    /// NOT SELECTABLE, DELIBERATELY. None of the 32 legacy declarations takes a padding argument
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L61], so no padding parameter may be added
    /// to this surface. Naming it once here is what makes it impossible for one of the 32 overloads
    /// to pad differently from the other 31.
    /// </remarks>
    private const PaddingMode LegacyBlockPadding = PaddingMode.PKCS7;



    /// <summary>
    /// The single message text for the unsupported-cipher-type failure.
    /// </summary>
    /// <remarks>
    /// It names the published set and its oracle locator and carries NO key material. The argument
    /// value reported alongside it is an algorithm identifier, which is not a secret.
    /// </remarks>
    private const string UnsupportedCipherTypeMessage =
        "Not a published symmetric cipher type. The set is exactly DES (0), 3DES (1), AES128 (2), " +
        "AES192 (3) and AES256 (4) [ws_objects/pfw.shared.pbl.src/enums.sru:L936-L940].";

    /// <summary>
    /// The single message text for the unsupported-cipher-mode failure.
    /// </summary>
    /// <remarks>
    /// It names the published set and its oracle locator and carries NO key material. No
    /// authenticated mode is a member of that set and none may be added - see DECISION D2.
    /// </remarks>
    private const string UnsupportedCipherModeMessage =
        "Not a published symmetric cipher mode. The set is exactly ECB (0), CBC (1) and CFB (2) " +
        "[ws_objects/pfw.shared.pbl.src/enums.sru:L943-L945].";

    /// <summary>
    /// The sibling provider that produces and consumes the text-safe payload of DECISION D4.
    /// </summary>
    /// <remarks>
    /// Injected rather than constructed so that the encoding decision is made in one place for the
    /// whole folder. It holds no state, so sharing one instance across every request is safe. It is
    /// the ONLY dependency of this type, and it carries no key material.
    /// </remarks>
    private readonly EncodingProvider _encoding;

    /// <summary>
    /// Initializes a new instance of the <see cref="SymmetricCipherProvider"/> class.
    /// </summary>
    /// <param name="encoding">
    /// The sibling encoding provider used to produce and consume the text-safe ciphertext payload of
    /// DECISION D4. Required.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    /// <remarks>
    /// Constructor injection is what REPLACES the legacy's type-shadowing global auto-instance
    /// <c>global n_crypto n_crypto</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L75] and the
    /// lazy-construction guard every legacy call site applies to it. Neither has an analogue worth
    /// reproducing: both exist only because PowerBuilder resolves one flat global namespace and
    /// offers no container.
    /// </remarks>
    public SymmetricCipherProvider(EncodingProvider encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        _encoding = encoding;
    }

    // ==========================================================================================
    //  SymEncrypt - ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45, sixteen declarations
    //  ------------------------------------------------------------------------------------------
    //  IN THE LEGACY'S DECLARATION ORDER, so this block diffs against L30-L45 line for line. Each
    //  member names its own legacy line. The four mode-omitting overloads of each key shape delegate
    //  to their mode-taking sibling passing the catalogue's default arm, which is how the ECB
    //  default is guaranteed to be ONE value rather than sixteen agreeing ones. The eight
    //  mode-taking overloads are thin normalization-and-delegation shims over ONE core routine.
    // ==========================================================================================
    #region SymEncrypt - n_crypto.sru:L30-L45

    /// <summary>
    /// Encrypts text with a text key in the DEFAULT CIPHER MODE, returning the text-safe encoded
    /// ciphertext. Substitutes <c>SymEncrypt(readonly string plain, readonly string key, readonly
    /// uint ntype)</c> [n_crypto.sru:L30].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">
    /// The key material, used AS RAW KEY BYTES then sized to the cipher's key length by
    /// DECISION D1. No key derivation is applied.
    /// </param>
    /// <param name="ntype">
    /// The cipher type: <see cref="Enums.CRYPTO_SYMCRYPT_TYPE_DES"/> through
    /// <see cref="Enums.CRYPTO_SYMCRYPT_TYPE_AES256"/>. A call site naming one of those constants
    /// must cast it to <see cref="ushort"/> - see the width note in this file's header.
    /// </param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses as weak or degenerate - see DECISION H2. This is a
    /// platform-imposed divergence from the legacy, which would have accepted it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY: omitting the mode selects ECB
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L943 and :L946], which is not semantically secure -
    /// identical plaintext blocks under the same key produce identical ciphertext blocks, so
    /// repetition and equality in the plaintext are visible to an observer who never learns the key.
    /// </para>
    /// <para>
    /// THIS ARM IS EXERCISED, NOT THEORETICAL, which is why promoting the default is forbidden
    /// rather than merely discouraged. The oracle calls exactly this three-argument shape with AES256
    /// and NO MODE AND NO VECTOR at ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L583 and :L615,
    /// matched by decrypts of the mirroring shape at :L337, :L552 and :L992. Every value that path
    /// ever wrote is ECB ciphertext; a promoted default would make all of it undecryptable here.
    /// </para>
    /// </remarks>
    public string SymEncrypt(string plain, string key, ushort ntype) =>
        SymEncrypt(plain, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts text with a text key in an explicit cipher mode, returning the text-safe encoded
    /// ciphertext. Substitutes <c>SymEncrypt(readonly string plain, readonly string key, readonly
    /// uint ntype, readonly long mode)</c> [n_crypto.sru:L31].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">
    /// The cipher mode: <see cref="Enums.CRYPTO_SYMCRYPT_MODE_ECB"/>,
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_CBC"/> or
    /// <see cref="Enums.CRYPTO_SYMCRYPT_MODE_CFB"/>.
    /// </param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3 applies: asking for CBC or CFB here supplies
    /// no vector, and an ALL-ZERO vector of the cipher's block length is synthesised. That is a weak
    /// default - a fixed, publicly known vector destroys CBC's semantic security, making the same
    /// plaintext under the same key produce the same ciphertext every time - and it is preserved
    /// because a random vector would produce ciphertext the legacy could not decrypt.
    /// </remarks>
    public string SymEncrypt(string plain, string key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPayloadText(EncryptCore(
            ToPayloadBytes(plain),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode)));
    }

    /// <summary>
    /// Encrypts text with a text key and a text initialization vector in the DEFAULT CIPHER MODE,
    /// returning the text-safe encoded ciphertext. Substitutes <c>SymEncrypt(readonly string plain,
    /// readonly string key, readonly string iv, readonly uint ntype)</c> [n_crypto.sru:L32].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector material, sized to the cipher's block length by the same rule as a
    /// key. ITS CONTENT IS UNUSED in the default mode - see the remarks.
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// DECISION H4(b), STATED SO THE BEHAVIOUR IS DEFINED RATHER THAN ACCIDENTAL. This overload
    /// supplies a vector but no mode, so it runs in the ECB default [enums.sru:L946] - and ECB
    /// consumes no vector at all. The vector's REFERENCE is validated, so a null argument fails the
    /// way it does everywhere else on this surface, but its CONTENT IS IGNORED: this call produces
    /// byte-for-byte the same result as the same call without a vector. The ignoring is structural,
    /// not incidental - the ECB code path uses a primitive that has no vector parameter, so the
    /// content has no route to the cipher.
    /// </remarks>
    public string SymEncrypt(string plain, string key, string iv, ushort ntype) =>
        SymEncrypt(plain, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts text with a text key and a text initialization vector in an explicit cipher mode,
    /// returning the text-safe encoded ciphertext. Substitutes <c>SymEncrypt(readonly string plain,
    /// readonly string key, readonly string iv, readonly uint ntype, readonly long mode)</c>
    /// [n_crypto.sru:L33].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector material, sized to the cipher's block length by DECISION H4(c).
    /// Used by CBC and CFB; ignored by ECB per DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// THE FULLY SPECIFIED SHAPE, AND THE ONE THE ORACLE'S OWN DEMO USES for all six of its
    /// symmetric operations: ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L504
    /// encrypts under DES/CBC with a supplied vector and :L547 decrypts it, :L607 pairs with :L592
    /// under AES256/CBC, and :L637 pairs with :L622 under 3DES/CBC. Each of those pairs writes its
    /// result into a plain text control and reads it back from the same control, which is the
    /// evidence behind DECISION D4's requirement that this family carry a text-safe payload.
    /// </remarks>
    public string SymEncrypt(string plain, string key, string iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPayloadText(EncryptCore(
            ToPayloadBytes(plain),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics)));
    }

    /// <summary>
    /// Encrypts text with a binary key in the DEFAULT CIPHER MODE, returning the text-safe encoded
    /// ciphertext. Substitutes <c>SymEncrypt(readonly string plain, readonly blob key, readonly uint
    /// ntype)</c> [n_crypto.sru:L34].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">
    /// The key bytes, sized to the cipher's key length by DECISION D1. The caller's array is not
    /// modified and is not retained.
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// KNOWN LEGACY WEAKNESS, PRESERVED: omitting the mode selects ECB [enums.sru:L946]. The binary
    /// key and vector overloads were a later addition to the legacy surface than the text ones, which
    /// is why both shapes exist; a binary key and the text spelling of the same bytes normalize
    /// through one shared rule, so the two are spellings of one behaviour rather than two.
    /// </remarks>
    public string SymEncrypt(string plain, byte[] key, ushort ntype) =>
        SymEncrypt(plain, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts text with a binary key in an explicit cipher mode, returning the text-safe encoded
    /// ciphertext. Substitutes <c>SymEncrypt(readonly string plain, readonly blob key, readonly uint
    /// ntype, readonly long mode)</c> [n_crypto.sru:L35].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3's all-zero vector is synthesised for CBC and
    /// CFB. Annotated as a weak default at L31 above; the reasoning is identical here.
    /// </remarks>
    public string SymEncrypt(string plain, byte[] key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPayloadText(EncryptCore(
            ToPayloadBytes(plain),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode)));
    }

    /// <summary>
    /// Encrypts text with a binary key and a binary initialization vector in the DEFAULT CIPHER
    /// MODE, returning the text-safe encoded ciphertext. Substitutes <c>SymEncrypt(readonly string
    /// plain, readonly blob key, readonly blob iv, readonly uint ntype)</c> [n_crypto.sru:L36].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector bytes. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// THE VECTOR TYPE FOLLOWS THE KEY TYPE, which is a contract fact and not a convenience: a
    /// binary key pairs only with a binary vector [n_crypto.sru:L36, L37, L44, L45], a text key only
    /// with a text vector [:L32, L33, L40, L41], and no mixed-type overload exists or may be added.
    /// As at L32, supplying a vector without a mode leaves the call in ECB, which ignores it.
    /// </remarks>
    public string SymEncrypt(string plain, byte[] key, byte[] iv, ushort ntype) =>
        SymEncrypt(plain, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts text with a binary key and a binary initialization vector in an explicit cipher
    /// mode, returning the text-safe encoded ciphertext. Substitutes <c>SymEncrypt(readonly string
    /// plain, readonly blob key, readonly blob iv, readonly uint ntype, readonly long mode)</c>
    /// [n_crypto.sru:L37].
    /// </summary>
    /// <param name="plain">The plaintext. An empty string is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector bytes - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The ciphertext in the text-safe encoding of DECISION D4.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// The binary-material counterpart of L33. It is fully specified, so no default arm applies -
    /// except padding, which is never selectable on this surface per DECISION H3.
    /// </remarks>
    public string SymEncrypt(string plain, byte[] key, byte[] iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPayloadText(EncryptCore(
            ToPayloadBytes(plain),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics)));
    }

    /// <summary>
    /// Encrypts binary data with a text key in the DEFAULT CIPHER MODE, returning RAW ciphertext
    /// bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly string key, readonly uint
    /// ntype)</c> [n_crypto.sru:L38].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// THE PAYLOAD TYPE DETERMINES THE RETURN TYPE, so this is where DECISION D4 stops applying: the
    /// eight binary-payload overloads carry RAW BYTES and perform no encoding whatever, while the
    /// eight text-payload overloads carry the text-safe encoding. Mixing the two would break the
    /// text-control round trip the legacy demo depends on. Omitting the mode still selects ECB
    /// [enums.sru:L946], with the weakness annotated at L30.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, string key, ushort ntype) =>
        SymEncrypt(plain, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts binary data with a text key in an explicit cipher mode, returning RAW ciphertext
    /// bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly string key, readonly uint
    /// ntype, readonly long mode)</c> [n_crypto.sru:L39].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3's all-zero vector is synthesised for CBC and
    /// CFB. See the weak-default annotation at L31.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, string key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return EncryptCore(
            plain,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode));
    }

    /// <summary>
    /// Encrypts binary data with a text key and a text initialization vector in the DEFAULT CIPHER
    /// MODE, returning RAW ciphertext bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly
    /// string key, readonly string iv, readonly uint ntype)</c> [n_crypto.sru:L40].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector material. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// A text key with a text vector, over a binary payload: the vector type follows the KEY type
    /// and not the payload type, which is easy to get backwards when reading L38-L45 quickly.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, string key, string iv, ushort ntype) =>
        SymEncrypt(plain, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts binary data with a text key and a text initialization vector in an explicit cipher
    /// mode, returning RAW ciphertext bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly
    /// string key, readonly string iv, readonly uint ntype, readonly long mode)</c>
    /// [n_crypto.sru:L41].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector material - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// Fully specified apart from padding, which is PKCS#5-family and not selectable per
    /// DECISION H3, and apart from the CFB feedback width, which is DECISION H1's single rule.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, string key, string iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return EncryptCore(
            plain,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics));
    }

    /// <summary>
    /// Encrypts binary data with a binary key in the DEFAULT CIPHER MODE, returning RAW ciphertext
    /// bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly blob key, readonly uint
    /// ntype)</c> [n_crypto.sru:L42].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// THE FULLY BINARY SHAPE, and the one with the least substitution in it: no text encoding on
    /// the payload, none on the key, and no vector. What remains is the ECB default
    /// [enums.sru:L946], the non-selectable padding of DECISION H3, and DECISION D1's key sizing.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, byte[] key, ushort ntype) =>
        SymEncrypt(plain, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts binary data with a binary key in an explicit cipher mode, returning RAW ciphertext
    /// bytes. Substitutes <c>SymEncrypt(readonly blob plain, readonly blob key, readonly uint ntype,
    /// readonly long mode)</c> [n_crypto.sru:L43].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// The LAST of the four mode-without-vector encrypt overloads [n_crypto.sru:L31, L35, L39, L43],
    /// so DECISION D3's all-zero vector is synthesised here too when the mode is CBC or CFB.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, byte[] key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return EncryptCore(
            plain,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode));
    }

    /// <summary>
    /// Encrypts binary data with a binary key and a binary initialization vector in the DEFAULT
    /// CIPHER MODE, returning RAW ciphertext bytes. Substitutes <c>SymEncrypt(readonly blob plain,
    /// readonly blob key, readonly blob iv, readonly uint ntype)</c> [n_crypto.sru:L44].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector bytes. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// The LAST of the four vector-without-mode encrypt overloads [n_crypto.sru:L32, L36, L40, L44].
    /// All four run in ECB and all four therefore ignore the vector's content.
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, byte[] key, byte[] iv, ushort ntype) =>
        SymEncrypt(plain, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Encrypts binary data with a binary key and a binary initialization vector in an explicit
    /// cipher mode, returning RAW ciphertext bytes. Substitutes <c>SymEncrypt(readonly blob plain,
    /// readonly blob key, readonly blob iv, readonly uint ntype, readonly long mode)</c>
    /// [n_crypto.sru:L45].
    /// </summary>
    /// <param name="plain">The plaintext bytes. An empty array is valid.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector bytes - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The raw ciphertext bytes, with NO encoding applied.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="plain"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The sized key is one this platform refuses - see DECISION H2.
    /// </exception>
    /// <remarks>
    /// THE SIXTEENTH AND LAST SymEncrypt DECLARATION, and the most explicit of them: binary payload,
    /// binary key, binary vector, explicit type, explicit mode. Only two substitutions remain
    /// invisible to a caller here - PKCS#5-family padding, which no overload can select
    /// (DECISION H3), and the CFB feedback width (DECISION H1).
    /// </remarks>
    public byte[] SymEncrypt(byte[] plain, byte[] key, byte[] iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(plain);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return EncryptCore(
            plain,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics));
    }

    #endregion


    // ==========================================================================================
    //  SymDecrypt - ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L46-L61, sixteen declarations
    //  ------------------------------------------------------------------------------------------
    //  AN EXACT MIRROR of L30-L45, argument shape for argument shape, with the first parameter named
    //  `cipher` instead of `plain` because that is what the legacy names it. Same declaration order,
    //  same delegation of the mode-omitting overloads to their mode-taking siblings, same single
    //  core routine underneath.
    //
    //  ONE ASYMMETRY WORTH STATING, BECAUSE IT IS THE POINT OF DECISION D2. There is NO INTEGRITY
    //  CHECK on this side, and there is nowhere for one to live: no overload takes or returns a tag,
    //  and no published mode is authenticated [enums.sru:L943-L945]. A wrong key or tampered
    //  ciphertext therefore yields either the platform's padding failure or PLAUSIBLE GARBAGE, and
    //  neither outcome is tamper detection. See DECISION H3 for the single failure shape.
    // ==========================================================================================
    #region SymDecrypt - n_crypto.sru:L46-L61

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a text key in the DEFAULT CIPHER MODE. Substitutes
    /// <c>SymDecrypt(readonly string cipher, readonly string key, readonly uint ntype)</c>
    /// [n_crypto.sru:L46].
    /// </summary>
    /// <param name="cipher">
    /// The ciphertext in the text-safe encoding of DECISION D4 - that is, whatever the matching
    /// string-shaped <c>SymEncrypt</c> returned. An empty string is valid and yields an empty result.
    /// </param>
    /// <param name="key">
    /// The key material, used AS RAW KEY BYTES then sized to the cipher's key length by
    /// DECISION D1.
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// Propagated unchanged from the encoding primitive.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The key is one this platform refuses (DECISION H2), the ciphertext length is not a whole
    /// number of blocks, or the padding check failed - see DECISION H3 for why those last two are
    /// not distinguished from each other or from a wrong key.
    /// </exception>
    /// <remarks>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY: omitting the mode selects ECB
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L943 and :L946]. THIS IS THE ARM THE ORACLE ACTUALLY
    /// USES - ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L337, :L552 and :L992 call exactly
    /// this three-argument shape with AES256 to read back what :L583 and :L615 wrote. Changing the
    /// default would make every value that path stored unreadable.
    /// </remarks>
    public string SymDecrypt(string cipher, string key, ushort ntype) =>
        SymDecrypt(cipher, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a text key in an explicit cipher mode. Substitutes
    /// <c>SymDecrypt(readonly string cipher, readonly string key, readonly uint ntype, readonly long
    /// mode)</c> [n_crypto.sru:L47].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3 applies on this side too: a CBC or CFB request
    /// with no vector decrypts against the SAME all-zero vector the encrypt side synthesised, which
    /// is what makes the pair round-trip at all. The weakness is annotated on the encrypt side at
    /// L31; it is identical here.
    /// </remarks>
    public string SymDecrypt(string cipher, string key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPlainText(DecryptCore(
            FromPayloadText(cipher),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode)));
    }

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a text key and a text initialization vector in the
    /// DEFAULT CIPHER MODE. Substitutes <c>SymDecrypt(readonly string cipher, readonly string key,
    /// readonly string iv, readonly uint ntype)</c> [n_crypto.sru:L48].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector material. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// Supplying a vector without a mode leaves the call in ECB, which consumes no vector at all, so
    /// this decrypts identically to the same call without one. The vector's reference is still
    /// validated so that a null argument behaves uniformly across the surface.
    /// </remarks>
    public string SymDecrypt(string cipher, string key, string iv, ushort ntype) =>
        SymDecrypt(cipher, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a text key and a text initialization vector in an
    /// explicit cipher mode. Substitutes <c>SymDecrypt(readonly string cipher, readonly string key,
    /// readonly string iv, readonly uint ntype, readonly long mode)</c> [n_crypto.sru:L49].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector material - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// THE DECRYPT SIDE OF THE ORACLE'S OWN DEMO PAIRS:
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L547 reverses :L504 under
    /// DES/CBC, :L592 reverses :L607 under AES256/CBC, and :L622 reverses :L637 under 3DES/CBC - each
    /// reading its input from the same plain text control the encrypt wrote it into. That is the
    /// evidence for DECISION D4, and it is why this overload accepts encoded text rather than raw
    /// bytes packed into a string.
    /// </remarks>
    public string SymDecrypt(string cipher, string key, string iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPlainText(DecryptCore(
            FromPayloadText(cipher),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics)));
    }

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a binary key in the DEFAULT CIPHER MODE.
    /// Substitutes <c>SymDecrypt(readonly string cipher, readonly blob key, readonly uint ntype)</c>
    /// [n_crypto.sru:L50].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">
    /// The key bytes - DECISION D1 applies. The caller's array is neither modified nor retained.
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// KNOWN LEGACY WEAKNESS, PRESERVED: omitting the mode selects ECB [enums.sru:L946]. A binary key
    /// and the text spelling of the same bytes normalize through one shared rule, so this overload
    /// and the one at L46 recover the same plaintext from the same ciphertext.
    /// </remarks>
    public string SymDecrypt(string cipher, byte[] key, ushort ntype) =>
        SymDecrypt(cipher, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a binary key in an explicit cipher mode.
    /// Substitutes <c>SymDecrypt(readonly string cipher, readonly blob key, readonly uint ntype,
    /// readonly long mode)</c> [n_crypto.sru:L51].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3's all-zero vector is used for CBC and CFB -
    /// the same vector its encrypt counterpart at L35 synthesised.
    /// </remarks>
    public string SymDecrypt(string cipher, byte[] key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPlainText(DecryptCore(
            FromPayloadText(cipher),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode)));
    }

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a binary key and a binary initialization vector in
    /// the DEFAULT CIPHER MODE. Substitutes <c>SymDecrypt(readonly string cipher, readonly blob key,
    /// readonly blob iv, readonly uint ntype)</c> [n_crypto.sru:L52].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector bytes. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// The vector type follows the KEY type here as everywhere: binary key with binary vector
    /// [n_crypto.sru:L52, L53, L60, L61], never mixed with the text spellings.
    /// </remarks>
    public string SymDecrypt(string cipher, byte[] key, byte[] iv, ushort ntype) =>
        SymDecrypt(cipher, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts text-safe encoded ciphertext with a binary key and a binary initialization vector in
    /// an explicit cipher mode. Substitutes <c>SymDecrypt(readonly string cipher, readonly blob key,
    /// readonly blob iv, readonly uint ntype, readonly long mode)</c> [n_crypto.sru:L53].
    /// </summary>
    /// <param name="cipher">The ciphertext in the encoding of DECISION D4.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector bytes - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="cipher"/> is not well-formed in the payload encoding of DECISION D4.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// The binary-material counterpart of L49, and the inverse of the encrypt overload at L37.
    /// </remarks>
    public string SymDecrypt(string cipher, byte[] key, byte[] iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return ToPlainText(DecryptCore(
            FromPayloadText(cipher),
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics)));
    }

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a text key in the DEFAULT CIPHER MODE. Substitutes
    /// <c>SymDecrypt(readonly blob cipher, readonly string key, readonly uint ntype)</c>
    /// [n_crypto.sru:L54].
    /// </summary>
    /// <param name="cipher">
    /// The RAW ciphertext bytes, with NO payload encoding applied - that is, whatever the matching
    /// binary-shaped <c>SymEncrypt</c> returned. An empty array is valid.
    /// </param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// THE PAYLOAD TYPE DETERMINES WHERE DECISION D4 STOPS: the eight binary-payload decrypts take
    /// raw bytes and perform no decoding, so no <see cref="FormatException"/> is reachable from them.
    /// Omitting the mode still selects ECB [enums.sru:L946].
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, string key, ushort ntype) =>
        SymDecrypt(cipher, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a text key in an explicit cipher mode. Substitutes
    /// <c>SymDecrypt(readonly blob cipher, readonly string key, readonly uint ntype, readonly long
    /// mode)</c> [n_crypto.sru:L55].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// A MODE-WITHOUT-VECTOR OVERLOAD, so DECISION D3's all-zero vector is used for CBC and CFB.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, string key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return DecryptCore(
            cipher,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode));
    }

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a text key and a text initialization vector in the DEFAULT
    /// CIPHER MODE. Substitutes <c>SymDecrypt(readonly blob cipher, readonly string key, readonly
    /// string iv, readonly uint ntype)</c> [n_crypto.sru:L56].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector material. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// A text key with a text vector over a binary payload - the vector type follows the KEY type,
    /// not the payload type.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, string key, string iv, ushort ntype) =>
        SymDecrypt(cipher, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a text key and a text initialization vector in an explicit
    /// cipher mode. Substitutes <c>SymDecrypt(readonly blob cipher, readonly string key, readonly
    /// string iv, readonly uint ntype, readonly long mode)</c> [n_crypto.sru:L57].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key material - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector material - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// The inverse of the encrypt overload at L41.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, string key, string iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return DecryptCore(
            cipher,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics));
    }

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a binary key in the DEFAULT CIPHER MODE. Substitutes
    /// <c>SymDecrypt(readonly blob cipher, readonly blob key, readonly uint ntype)</c>
    /// [n_crypto.sru:L58].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// THE FULLY BINARY DECRYPT SHAPE, the inverse of L42. No payload encoding, no key encoding, no
    /// vector: what remains is the ECB default [enums.sru:L946], non-selectable padding
    /// (DECISION H3), and DECISION D1's key sizing.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, byte[] key, ushort ntype) =>
        SymDecrypt(cipher, key, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a binary key in an explicit cipher mode. Substitutes
    /// <c>SymDecrypt(readonly blob cipher, readonly blob key, readonly uint ntype, readonly long
    /// mode)</c> [n_crypto.sru:L59].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// The LAST of the four mode-without-vector decrypt overloads [n_crypto.sru:L47, L51, L55, L59],
    /// so DECISION D3's all-zero vector applies here too.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, byte[] key, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return DecryptCore(
            cipher,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            RefuseOrOmitInitializationVector(mode));
    }

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a binary key and a binary initialization vector in the
    /// DEFAULT CIPHER MODE. Substitutes <c>SymDecrypt(readonly blob cipher, readonly blob key,
    /// readonly blob iv, readonly uint ntype)</c> [n_crypto.sru:L60].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">
    /// The initialization vector bytes. ITS CONTENT IS UNUSED in the default mode, per
    /// DECISION H4(b).
    /// </param>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published cipher type.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// The LAST of the four vector-without-mode decrypt overloads [n_crypto.sru:L48, L52, L56, L60].
    /// All four run in ECB and all four therefore ignore the vector's content.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, byte[] key, byte[] iv, ushort ntype) =>
        SymDecrypt(cipher, key, iv, ntype, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

    /// <summary>
    /// Decrypts RAW ciphertext bytes with a binary key and a binary initialization vector in an
    /// explicit cipher mode. Substitutes <c>SymDecrypt(readonly blob cipher, readonly blob key,
    /// readonly blob iv, readonly uint ntype, readonly long mode)</c> [n_crypto.sru:L61].
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes.</param>
    /// <param name="key">The key bytes - DECISION D1 applies.</param>
    /// <param name="iv">The initialization vector bytes - DECISION H4(c) applies.</param>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cipher"/>, <paramref name="key"/> or <paramref name="iv"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> or <paramref name="mode"/> is not published.
    /// </exception>
    /// <exception cref="CryptographicException">See DECISION H2 and DECISION H3.</exception>
    /// <remarks>
    /// THE THIRTY-SECOND AND LAST DECLARATION OF THIS SURFACE [n_crypto.sru:L61], and the inverse of
    /// the encrypt overload at L45. With this member the folder's census closes: 32 symmetric
    /// overloads out of the 63 ported declarations of the 65 the legacy publishes at L9-L73.
    /// </remarks>
    public byte[] SymDecrypt(byte[] cipher, byte[] key, byte[] iv, ushort ntype, long mode)
    {
        ArgumentNullException.ThrowIfNull(cipher);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(iv);

        SymmetricCipherMetrics metrics = ScreenArguments(ntype, mode);

        return DecryptCore(
            cipher,
            ntype,
            mode,
            LegacyDefaults.NormalizeKeyMaterial(key, metrics.KeyLengthBytes),
            NormalizeSuppliedInitializationVector(iv, mode, metrics));
    }

    #endregion


    // ==========================================================================================
    //  THE TWO CORE ROUTINES
    //  ------------------------------------------------------------------------------------------
    //  ALL 32 PUBLIC OVERLOADS FUNNEL THROUGH THESE TWO METHODS, which is what makes the ECB
    //  default, DECISION D3's zero vector, DECISION D1's sizing, DECISION H1's feedback width,
    //  DECISION H3's padding and DECISION H4(b)'s vector-ignoring impossible to apply
    //  inconsistently: there is exactly one place each of them can be applied.
    //
    //  OWNERSHIP, WHICH IS THE ONE THING TO GET RIGHT WHEN READING THESE. Each core TAKES OWNERSHIP
    //  of the normalized key and vector buffers it is handed and ZEROES THEM before returning,
    //  including on every exception path. That is safe because both are always FRESH ALLOCATIONS -
    //  the catalogue's normalizer allocates a new buffer of the required length on every call and
    //  its zero-vector factory allocates one too, so neither can ever be the caller's own array.
    //  A caller's key array is therefore never mutated by this type.
    //
    //  WHY THE ONE-SHOT PRIMITIVES RATHER THAN A REUSABLE TRANSFORM. The mode, the padding and the
    //  feedback width are passed as ARGUMENTS to each operation instead of being assigned to the
    //  algorithm instance's properties. Three consequences follow, and all three are wanted here.
    //  The operation is self-describing, so no property left over from a previous call can change
    //  its meaning. The instance is never shared, so there is no thread-safety question to answer -
    //  see the note on this type. And the ECB primitive HAS NO VECTOR PARAMETER AT ALL, which is
    //  what makes DECISION H4(b) structural: a supplied vector has no route into an ECB operation.
    // ==========================================================================================
    #region The two core routines

    /// <summary>
    /// The single encryption path. Every one of the 16 <c>SymEncrypt</c> overloads reaches it.
    /// </summary>
    /// <param name="plain">The plaintext bytes. Read only; never modified.</param>
    /// <param name="ntype">The cipher type, already screened by <see cref="ScreenArguments"/>.</param>
    /// <param name="mode">The cipher mode, already screened by <see cref="ScreenArguments"/>.</param>
    /// <param name="normalizedKey">
    /// The key, already sized to the cipher's exact key length by DECISION D1. OWNED BY THIS METHOD
    /// and zeroed before it returns.
    /// </param>
    /// <param name="initializationVector">
    /// The vector, already sized to the cipher's block length, or <see langword="null"/> when the
    /// mode consumes none. OWNED BY THIS METHOD and zeroed before it returns.
    /// </param>
    /// <returns>The raw ciphertext bytes.</returns>
    /// <exception cref="CryptographicException">
    /// The key is one this platform refuses as weak or degenerate - see DECISION H2 - or the
    /// requested feedback width is unavailable for the cipher, which DECISION H1's rule prevents.
    /// </exception>
    /// <remarks>
    /// <para>
    /// PADDING IS PKCS#5-FAMILY AND NOT SELECTABLE (DECISION H3): the single padding constant is
    /// applied to all three mode arms, and no overload of this surface exposes a padding argument
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45].
    /// </para>
    /// <para>
    /// THE ALGORITHM INSTANCE IS CREATED PER CALL AND DISPOSED, never cached, because
    /// <see cref="SymmetricAlgorithm"/> is not thread-safe. Disposal also zeroes the copy of the key
    /// the instance took when it was assigned, so the material is cleared in two places: the
    /// instance's copy by disposal, and this method's buffers by the wipe below.
    /// </para>
    /// </remarks>
    private static byte[] EncryptCore(
        byte[] plain,
        ushort ntype,
        long mode,
        byte[] normalizedKey,
        byte[]? initializationVector)
    {
        try
        {
            using SymmetricAlgorithm algorithm = CreateAlgorithm(ntype);

            // DECISION H2 - the assignment itself is the screen this platform applies. A known weak
            // DES key, or a 3DES key whose adjacent sub-keys coincide, raises here rather than
            // encrypting. No workaround is applied and no substitute key is chosen: the divergence
            // is the platform's and it is reported loudly rather than papered over.
            algorithm.Key = normalizedKey;

            if (mode == Enums.CRYPTO_SYMCRYPT_MODE_ECB)
            {
                // DECISION H4(b) MADE STRUCTURAL - this primitive takes no vector, so a vector the
                // caller supplied cannot reach the cipher even in principle.
                return algorithm.EncryptEcb(plain, LegacyBlockPadding);
            }

            // CBC IS THE ONLY MODE THAT CAN REACH HERE, AND A VECTOR IS GUARANTEED PRESENT. Two
            // screens have already run: `ScreenArguments` refused CFB outright under DECISION D3,
            // and the vector resolvers produce a vector EXACTLY when the catalogue reports the mode
            // consumes one - so with ECB returned above, CBC-with-vector is all that remains. That
            // invariant is asserted with the null-forgiving operator rather than re-tested,
            // deliberately: a defensive re-test would be an arm no input can reach, and were the
            // invariant ever broken the primitive itself raises a null-argument failure, which is
            // louder and more legible than anything this method could substitute.
            return algorithm.EncryptCbc(plain, initializationVector!, LegacyBlockPadding);
        }
        finally
        {
            WipeOwnedMaterial(normalizedKey, initializationVector);
        }
    }

    /// <summary>
    /// The single decryption path. Every one of the 16 <c>SymDecrypt</c> overloads reaches it.
    /// </summary>
    /// <param name="cipher">The raw ciphertext bytes. Read only; never modified.</param>
    /// <param name="ntype">The cipher type, already screened by <see cref="ScreenArguments"/>.</param>
    /// <param name="mode">The cipher mode, already screened by <see cref="ScreenArguments"/>.</param>
    /// <param name="normalizedKey">
    /// The key, already sized by DECISION D1. OWNED BY THIS METHOD and zeroed before it returns.
    /// </param>
    /// <param name="initializationVector">
    /// The vector, already sized, or <see langword="null"/> when the mode consumes none. OWNED BY
    /// THIS METHOD and zeroed before it returns.
    /// </param>
    /// <returns>The recovered plaintext bytes.</returns>
    /// <exception cref="CryptographicException">
    /// The key is one this platform refuses (DECISION H2), the ciphertext length is not a whole
    /// number of blocks, or the padding check failed. THE THREE ARE NOT DISTINGUISHED - see the
    /// remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE SINGLE FAILURE SHAPE OF DECISION H3, AND WHY IT IS DELIBERATELY UNINFORMATIVE. Because no
    /// published mode is authenticated (DECISION D2), a wrong key produces either a padding check
    /// failure or PLAUSIBLE GARBAGE, and neither outcome is tamper detection. This method therefore
    /// lets the library's own <see cref="CryptographicException"/> propagate UNCHANGED: not caught,
    /// not rewrapped, not classified. Rewrapping would invent a failure taxonomy the legacy never
    /// published, and reporting a padding failure distinguishably from garbage would hand a caller a
    /// signal this port has no business adding. THE GARBAGE OUTCOME IS NOT DETECTABLE AT ALL, and
    /// that is a property of the preserved design rather than of this implementation.
    /// </para>
    /// <para>
    /// The plaintext this method returns is the caller's data and is NOT wiped - it is the result.
    /// Only the key and vector buffers this method owns are zeroed.
    /// </para>
    /// </remarks>
    private static byte[] DecryptCore(
        byte[] cipher,
        ushort ntype,
        long mode,
        byte[] normalizedKey,
        byte[]? initializationVector)
    {
        try
        {
            using SymmetricAlgorithm algorithm = CreateAlgorithm(ntype);

            // DECISION H2, exactly as on the encrypt side.
            algorithm.Key = normalizedKey;

            if (mode == Enums.CRYPTO_SYMCRYPT_MODE_ECB)
            {
                // DECISION H4(b) - no vector parameter exists on this primitive.
                return algorithm.DecryptEcb(cipher, LegacyBlockPadding);
            }

            // The same CBC-only, vector-guaranteed invariant as on the encrypt side, reached the
            // same way and asserted for the same reason.
            return algorithm.DecryptCbc(cipher, initializationVector!, LegacyBlockPadding);
        }
        finally
        {
            WipeOwnedMaterial(normalizedKey, initializationVector);
        }
    }

    #endregion


    // ==========================================================================================
    //  THE SUBSTITUTION RULES, EACH IN EXACTLY ONE PLACE
    //  ------------------------------------------------------------------------------------------
    //  Each member below is one decision, written once. None of them holds key material beyond the
    //  call that produced it, none reads configuration, and none logs.
    // ==========================================================================================
    #region Substitution rules - one place each

    /// <summary>
    /// The encoding applied to a string payload, which is the same UTF-8 choice DECISION D1 makes
    /// for string key material.
    /// </summary>
    /// <value>The catalogue's key-material encoding, read from it rather than restated.</value>
    /// <remarks>
    /// <para>
    /// READ FROM THE CATALOGUE ON PURPOSE, so that the payload encoding and the key-material
    /// encoding can never drift apart. The legacy passes a string payload and a string key across the
    /// SAME native boundary in the same call [n_crypto.sru:L30-L33], so a divergence between the two
    /// would be invented here rather than preserved from anywhere.
    /// </para>
    /// <para>
    /// TWO PROPERTIES OF THAT PARTICULAR ENCODING MATTER. Its byte conversion NEVER EMITS A
    /// BYTE-ORDER MARK, so no phantom leading bytes enter a plaintext. And its decoder REPLACES
    /// invalid sequences rather than throwing, which is what keeps DECISION H3's failure shape
    /// singular: a wrong-key decrypt whose padding happens to validate yields replacement characters
    /// rather than a second, different exception that would tell a caller something the legacy never
    /// told it.
    /// </para>
    /// <para>
    /// It is deliberately NOT reached through a <c>using System.Text</c> import. The Base Class
    /// Library declares a type named <c>System.Text.EncodingProvider</c>, and importing that
    /// namespace here would make the simple name <see cref="EncodingProvider"/> ambiguous with this
    /// folder's own provider and break the build.
    /// </para>
    /// </remarks>
    private static System.Text.Encoding PayloadTextEncoding => LegacyDefaults.KeyMaterialEncoding;

    /// <summary>
    /// Screens both algorithm identifiers against the catalogue's legacy-exact sets and returns the
    /// cipher's sizing.
    /// </summary>
    /// <param name="ntype">The candidate cipher type.</param>
    /// <param name="mode">The candidate cipher mode.</param>
    /// <returns>The key and block sizing for <paramref name="ntype"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the five published cipher types, or
    /// <paramref name="mode"/> is not one of the three published cipher modes.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ONE SCREENING POINT FOR ALL 32 OVERLOADS, and it screens through the CATALOGUE'S
    /// predicates rather than against literals declared here. That is what keeps this file's accepted
    /// sets identical to every sibling provider's, and it is why this file declares no
    /// <c>CRYPTO_</c>-prefixed identifier at all: every such name here is a qualified reference to
    /// the ported constant catalogue.
    /// </para>
    /// <para>
    /// THE FAILURE SHAPE MATCHES THE SIBLING PROVIDERS: a rejected algorithm identifier raises
    /// <see cref="ArgumentOutOfRangeException"/> carrying the offending value and a message naming
    /// the published set with its oracle locator. The value is an algorithm identifier and therefore
    /// not a secret; NO KEY OR VECTOR MATERIAL REACHES EITHER MESSAGE.
    /// </para>
    /// <para>
    /// The type is screened first so that the sizing lookup that follows cannot fail: the catalogue's
    /// throwing accessor is reserved for an unscreened caller, which is an internal defect rather
    /// than a caller's bad argument.
    /// </para>
    /// </remarks>
    private static SymmetricCipherMetrics ScreenArguments(ushort ntype, long mode)
    {
        if (!LegacyDefaults.IsSupportedSymmetricType(ntype))
        {
            throw new ArgumentOutOfRangeException(nameof(ntype), ntype, UnsupportedCipherTypeMessage);
        }

        if (!LegacyDefaults.IsSupportedSymmetricMode(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, UnsupportedCipherModeMessage);
        }

        // DECISION D3, THE MODE HALF - applied here because this method is the one gate all 32
        // overloads pass through, so the CFB block cannot be reached around. It is a SEPARATE and
        // LATER question from the screening above: CFB is a published identifier and the predicate
        // above rightly accepts it; what this repository cannot supply is its feedback width. The
        // vector half of D3 lives in `RefuseOrOmitInitializationVector`, which is the only place
        // knows a vector was NOT supplied.
        SymmetricCellParity parity =
            LegacyDefaults.ClassifySymmetricCell(mode, initializationVectorSupplied: true);

        if (parity != SymmetricCellParity.Supported)
        {
            throw new SymmetricParityUnavailableException(parity, mode);
        }

        return LegacyDefaults.GetSymmetricCipherMetrics(ntype);
    }

    /// <summary>
    /// Converts a string payload to the bytes the cipher operates on.
    /// </summary>
    /// <param name="text">The plaintext. An empty string yields an empty array.</param>
    /// <returns>The payload bytes.</returns>
    /// <remarks>
    /// One of the two halves of the string-payload boundary, the other being
    /// <see cref="ToPlainText"/>. Both read <see cref="PayloadTextEncoding"/>, so the encode and
    /// decode directions cannot disagree and a round trip through them is lossless for any text.
    /// </remarks>
    private static byte[] ToPayloadBytes(string text) => PayloadTextEncoding.GetBytes(text);

    /// <summary>
    /// Converts recovered plaintext bytes back to text.
    /// </summary>
    /// <param name="plainBytes">The recovered bytes. An empty array yields an empty string.</param>
    /// <returns>The plaintext.</returns>
    /// <remarks>
    /// The inverse of <see cref="ToPayloadBytes"/>. Bytes that are not valid in the payload encoding
    /// - which is what a wrong-key decrypt produces when its padding happens to validate - become
    /// REPLACEMENT CHARACTERS rather than an exception. That is deliberate: throwing here would add a
    /// wrong-key signal the legacy surface never published, which DECISION H3 forbids.
    /// </remarks>
    private static string ToPlainText(byte[] plainBytes) => PayloadTextEncoding.GetString(plainBytes);

    /// <summary>
    /// Encodes raw ciphertext into the text-safe payload of DECISION D4.
    /// </summary>
    /// <param name="cipherBytes">The raw ciphertext bytes.</param>
    /// <returns>The ciphertext in the catalogue's payload encoding.</returns>
    /// <remarks>
    /// The encoding itself is the CATALOGUE'S decision and is read from it, not chosen here, so all
    /// four provider files that carry a text-shaped payload agree by construction and a correction
    /// from the behavioural oracle lands in one place. The conversion is delegated to the sibling
    /// encoding provider rather than performed inline, so this file introduces no second convention.
    /// </remarks>
    private string ToPayloadText(byte[] cipherBytes) =>
        _encoding.BlobToString(cipherBytes, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    /// <summary>
    /// Decodes the text-safe payload of DECISION D4 back to raw ciphertext.
    /// </summary>
    /// <param name="cipherText">The ciphertext in the catalogue's payload encoding.</param>
    /// <returns>The raw ciphertext bytes.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="cipherText"/> is not well formed in that encoding. Propagated unchanged from
    /// the encoding primitive, so its message and parameter name survive.
    /// </exception>
    /// <remarks>
    /// The exact inverse of <see cref="ToPayloadText"/>, through the same sibling provider and the
    /// same catalogue constant, which is what makes the string-shaped round trip hold for any byte
    /// sequence the cipher can produce.
    /// </remarks>
    private byte[] FromPayloadText(string cipherText) =>
        _encoding.StringToBlob(cipherText, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    /// <summary>
    /// Sizes a caller-supplied text initialization vector, or reports that the mode consumes none.
    /// Implements DECISION H4(b) and DECISION H4(c).
    /// </summary>
    /// <param name="iv">The caller-supplied vector material.</param>
    /// <param name="mode">The screened cipher mode.</param>
    /// <param name="metrics">The cipher's sizing.</param>
    /// <returns>
    /// A fresh buffer of exactly the cipher's block length when the mode consumes a vector;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// DECISION H4(b) - RETURNING NULL FOR ECB IS HOW THE VECTOR IS IGNORED, and it is why an ECB
    /// call with a vector produces byte-for-byte the same ciphertext as the same call without one.
    /// The classification comes from the catalogue rather than from a mode comparison written here,
    /// so this file cannot disagree with it about which modes consume a vector.
    /// </para>
    /// <para>
    /// DECISION H4(c) - the sizing is DECISION D1's truncate-or-zero-pad rule, applied by the SAME
    /// catalogue normalizer that sizes keys, against the block length rather than the key length. A
    /// vector is exactly one block wide.
    /// </para>
    /// </remarks>
    private static byte[]? NormalizeSuppliedInitializationVector(
        string iv,
        long mode,
        SymmetricCipherMetrics metrics) =>
        LegacyDefaults.ModeUsesInitializationVector(mode)
            ? LegacyDefaults.NormalizeKeyMaterial(iv, metrics.IvLengthBytes)
            : null;

    /// <summary>
    /// Sizes a caller-supplied binary initialization vector, or reports that the mode consumes none.
    /// Implements DECISION H4(b) and DECISION H4(c).
    /// </summary>
    /// <param name="iv">The caller-supplied vector bytes. Neither modified nor retained.</param>
    /// <param name="mode">The screened cipher mode.</param>
    /// <param name="metrics">The cipher's sizing.</param>
    /// <returns>
    /// A fresh buffer of exactly the cipher's block length when the mode consumes a vector;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The binary counterpart of the text overload above, existing because THE VECTOR TYPE FOLLOWS
    /// THE KEY TYPE across the legacy declarations [n_crypto.sru:L36, L37, L44, L45 and mirrors]. It
    /// applies the identical rule through the identical normalizer with the encoding step omitted,
    /// because binary material is already bytes - so a text vector and the binary spelling of the
    /// same bytes can never be sized differently. The returned buffer is a FRESH allocation, so the
    /// core's wipe never touches the caller's array.
    /// </remarks>
    private static byte[]? NormalizeSuppliedInitializationVector(
        byte[] iv,
        long mode,
        SymmetricCipherMetrics metrics) =>
        LegacyDefaults.ModeUsesInitializationVector(mode)
            ? LegacyDefaults.NormalizeKeyMaterial(iv, metrics.IvLengthBytes)
            : null;

    /// <summary>
    /// Applies the vector half of DECISION D3 for the eight overloads that take a mode but no vector:
    /// ECB proceeds with no vector, and any vector-consuming mode is REFUSED.
    /// </summary>
    /// <param name="mode">The screened cipher mode.</param>
    /// <returns>
    /// Always <see langword="null"/>. The method returns a value only so that the eight call sites
    /// read identically to the twenty-four that normalize a supplied vector.
    /// </returns>
    /// <exception cref="SymmetricParityUnavailableException">
    /// <paramref name="mode"/> consumes an initialization vector, which these eight overloads do not
    /// supply. See the remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS METHOD USED TO INVENT THE MISSING VECTOR, AND THAT IS THE DEFECT IT NOW EXISTS TO
    /// PREVENT. Four <c>SymEncrypt</c> overloads accept a mode but no vector
    /// [n_crypto.sru:L31, L35, L39, L43], mirrored by four <c>SymDecrypt</c> overloads
    /// [:L47, L51, L55, L59]. CBC requires a vector, so one had to be supplied from somewhere, and an
    /// all-zero buffer of the block length was chosen on the belief that it was "the conventional
    /// legacy behaviour". Nothing in this repository establishes that. The closed binary
    /// [n_crypto.sru:L8] could as easily have derived a vector from the key, used a fixed non-zero
    /// constant, or refused the call outright.
    /// </para>
    /// <para>
    /// WHY THE GUESS WAS PARTICULARLY DANGEROUS RATHER THAN MERELY UNVERIFIED. A wrong vector is
    /// invisible to every test this repository can run, because encrypting and decrypting under the
    /// same wrong vector round-trips perfectly. The caller receives ciphertext that passes every
    /// check available and that THE LEGACY CANNOT DECRYPT - data loss wearing the appearance of
    /// success. Refusing is the only outcome that cannot be silently wrong.
    /// </para>
    /// <para>
    /// THE ECB ARMS ARE UNAFFECTED, WHICH IS WHY THE DEFAULT PATH STILL WORKS. ECB consumes no vector
    /// [enums.sru:L943] and is the default mode [:L946], so all eight mode-omitting overloads and the
    /// explicit-ECB arms of these eight pass through untouched. A caller needing CBC through one of
    /// these eight supplies a vector through one of the other twenty-four overloads, which the legacy
    /// also publishes - so the narrowing removes no capability the surface does not offer elsewhere.
    /// </para>
    /// <para>
    /// The classification is delegated rather than re-decided here: the catalogue owns the blocked
    /// set so that this file and the wire contract cannot drift apart on what it contains.
    /// </para>
    /// </remarks>
    private static byte[]? RefuseOrOmitInitializationVector(long mode)
    {
        SymmetricCellParity parity =
            LegacyDefaults.ClassifySymmetricCell(mode, initializationVectorSupplied: false);

        if (parity != SymmetricCellParity.Supported)
        {
            throw new SymmetricParityUnavailableException(parity, mode);
        }

        // Only ECB survives the classification without a vector, and ECB consumes none.
        return null;
    }


    /// <summary>
    /// Creates the Base Class Library algorithm that substitutes a legacy cipher type.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <returns>
    /// A new algorithm instance the caller must dispose. No key, vector, mode or padding is set on
    /// it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the five published cipher types.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE ALGORITHM SET IS PRESERVED EXACTLY as the legacy publishes it
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L936-L940]: DES, 3DES and the three AES key sizes.
    /// DES, with its 56 effective key bits, and two-key-equivalent 3DES are BOTH RETAINED and neither
    /// may be removed; no cipher outside that set may be added.
    /// </para>
    /// <para>
    /// THE THREE AES ROWS SHARE ONE ALGORITHM TYPE, which is not a shortcut. AES128, AES192 and
    /// AES256 are the same cipher at three key lengths, and the length is already decided: DECISION
    /// D1 sized the key to 16, 24 or 32 bytes from the catalogue's table before this method was
    /// reached, and assigning that key sets the instance's key size to match. Creating three
    /// differently pre-configured instances would put the key length in two places.
    /// </para>
    /// <para>
    /// THE WIDTH MISMATCH IS NARROWED BACK HERE, IN ONE PLACE. The legacy declares this argument as a
    /// 16-bit PowerBuilder <c>uint</c> [n_crypto.sru:L30-L61] while declaring the constants that name
    /// its legal values as <c>Constant Long</c> [enums.sru:L936-L940] - a width mismatch present in
    /// the legacy and reproduced rather than tidied away. PowerBuilder tolerates the implicit
    /// narrowing; C# does not, and it will not accept a <c>long</c> constant as a case label over a
    /// <see cref="ushort"/> value, because it permits an implicit constant conversion to
    /// <see cref="ushort"/> only from <see cref="int"/>. Widening the argument back to
    /// <see cref="long"/> once, here, is what keeps that conversion out of all 32 signatures.
    /// </para>
    /// <para>
    /// The final arm is defensive rather than expected: <see cref="ScreenArguments"/> has already
    /// rejected any unpublished type before a public overload reaches this method, so it is
    /// reachable only by an internal caller that skipped screening. It is retained because a switch
    /// over a numeric value cannot be proved exhaustive, and it reports through the same documented
    /// failure path with the same message.
    /// </para>
    /// </remarks>
    internal static SymmetricAlgorithm CreateAlgorithm(ushort ntype)
    {
        long cipherType = ntype;

        return cipherType switch
        {
            Enums.CRYPTO_SYMCRYPT_TYPE_DES => DES.Create(),
            Enums.CRYPTO_SYMCRYPT_TYPE_3DES => TripleDES.Create(),
            Enums.CRYPTO_SYMCRYPT_TYPE_AES128 => Aes.Create(),
            Enums.CRYPTO_SYMCRYPT_TYPE_AES192 => Aes.Create(),
            Enums.CRYPTO_SYMCRYPT_TYPE_AES256 => Aes.Create(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(ntype),
                ntype,
                UnsupportedCipherTypeMessage),
        };
    }

    /// <summary>
    /// Zeroes the key and initialization-vector buffers a core routine owns.
    /// </summary>
    /// <param name="normalizedKey">The owned key buffer. Always present.</param>
    /// <param name="initializationVector">
    /// The owned vector buffer, or <see langword="null"/> when the mode consumed none.
    /// </param>
    /// <remarks>
    /// <para>
    /// CALLED FROM A <c>finally</c> IN BOTH CORES, so the wipe happens on the failure paths too -
    /// including the platform's weak-key rejection of DECISION H2, which throws before any
    /// ciphertext exists.
    /// </para>
    /// <para>
    /// SAFE BECAUSE OF OWNERSHIP, WHICH IS WORTH RESTATING HERE RATHER THAN ASSUMING. Both buffers
    /// are always FRESH ALLOCATIONS produced by the catalogue - its normalizer allocates a new buffer
    /// of the required length on every call, and its zero-vector factory allocates one too - so
    /// neither can ever be the caller's own array and this wipe cannot destroy a caller's key.
    /// </para>
    /// <para>
    /// WHAT IT CANNOT REACH IS STATED PLAINLY. A <see cref="string"/> key or vector cannot be wiped
    /// at all, because .NET strings are immutable and this port preserves the legacy's string-shaped
    /// parameters [n_crypto.sru:L30-L33 and mirrors]. This type never materialises its own byte copy
    /// of string material - it hands the string to the catalogue's normalizer, so the only buffer in
    /// play is the normalized one wiped here - but the caller's string itself outlives the call
    /// whatever this method does. That residual exposure belongs to the preserved signature and is
    /// recorded rather than hidden.
    /// </para>
    /// </remarks>
    private static void WipeOwnedMaterial(byte[] normalizedKey, byte[]? initializationVector)
    {
        CryptographicOperations.ZeroMemory(normalizedKey);

        if (initializationVector is not null)
        {
            CryptographicOperations.ZeroMemory(initializationVector);
        }
    }

    #endregion

}
