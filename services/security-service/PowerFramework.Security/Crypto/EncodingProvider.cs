// ==============================================================================================
//  EncodingProvider - the blob-to-text conversion surface of the legacy cryptographic library
//  --------------------------------------------------------------------------------------------
//  SUBSTITUTES    ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L13, plus the four global
//                 function wrappers that sit on top of those three declarations:
//                     ws_objects/pfw.crypto.pbl.src/base64encode.srf
//                     ws_objects/pfw.crypto.pbl.src/hexencode.srf
//                     ws_objects/pfw.crypto.pbl.src/base64decode.srf
//                     ws_objects/pfw.crypto.pbl.src/hexdecode.srf
//
//  ORACLE STATUS  Every ws_objects/** path cited in this file is READ ONLY. It is the behavioural
//                 oracle for parity testing, never an edit target. Each locator below is
//                 provenance for a decision and nothing more: no legacy file is edited, moved,
//                 reformatted or re-exported by this port.
//
//  WHY EVERY MEMBER IS A SUBSTITUTION AND NOT A TRANSLATION
//  --------------------------------------------------------------------------------------------
//  n_crypto.sru is 86 lines carrying exactly 65 `public function` declarations at L9-L73 and NO
//  IMPLEMENTATION BODY ANYWHERE IN THE REPOSITORY. Its L8 reads
//  `global type n_crypto from nonvisualobject native "pfw.dll"`, so all 65 behaviours live inside
//  a closed native binary for which no C++ source exists here or anywhere it can be read from.
//
//  There is therefore no legacy code to translate. Each member below is a documented substitution
//  against the Base Class Library, and NO THIRD-PARTY PACKAGE IS ADDED for any of them:
//  System.Convert's Base64 and hexadecimal families cover the entire surface. The sibling
//  LegacyDefaults holds the decisions that more than one provider shares; the three decisions
//  that belong to THIS surface alone are recorded further down.
//
//  THE CENSUS: 3 NATIVE DECLARATIONS + 4 WRAPPERS = 7 MEMBERS, AND NOTHING ELSE
//  --------------------------------------------------------------------------------------------
//  This file owns exactly seven public members. The count is a hard census rather than a
//  guideline: LegacyDefaults reconciles all 65 legacy declarations across six providers as
//  3 + 5 + 3 + 6 + 32 + 14 = 63 ported plus 2 deliberately non-ported, and an eighth member here
//  - a convenience overload, a Try-variant, a helper promoted to public - would silently break
//  that reconciliation. A unit test asserts the count by reflection for exactly that reason.
//
//      member          legacy declaration                                          locator
//      -------------   ---------------------------------------------------------   --------------
//      BlobToString    public function string BlobToString(readonly blob data,     n_crypto.sru
//                                                          readonly long encoding)         :L12
//      StringToBlob    public function blob StringToBlob(readonly string data,     n_crypto.sru
//                                                        readonly long encoding)           :L11
//      BlobReverse     public function boolean BlobReverse(ref blob data)          n_crypto.sru
//                                                                                          :L13
//      Base64Encode    global function string base64encode(readonly blob data)     base64encode
//                                                                              .srf:L10-L11
//      HexEncode       global function string hexencode(readonly blob data)        hexencode
//                                                                              .srf:L10-L11
//      Base64Decode    global function blob base64decode(readonly string data)     base64decode
//                                                                              .srf:L10-L11
//      HexDecode       global function blob hexdecode(readonly string data)        hexdecode
//                                                                              .srf:L10-L11
//
//  THE DIRECTION IS FIXED BY THE LEGACY SIGNATURES, NOT CHOSEN
//  --------------------------------------------------------------------------------------------
//  ENCODE IS BLOB TO STRING; DECODE IS STRING TO BLOB. All four wrapper bodies were read in full
//  and every one of them confirms it:
//
//      base64encode.srf:L11   return n_crypto.BlobToString(data, Enums.CRYPTO_ENCODING_BASE64)
//      hexencode.srf:L11      return n_crypto.BlobToString(data, Enums.CRYPTO_ENCODING_HEX)
//      base64decode.srf:L11   return n_crypto.StringToBlob(data, Enums.CRYPTO_ENCODING_BASE64)
//      hexdecode.srf:L11      return n_crypto.StringToBlob(data, Enums.CRYPTO_ENCODING_HEX)
//
//  That delegation is reproduced literally: the four wrappers below call this type's own
//  BlobToString and StringToBlob rather than reaching for Convert themselves, so ONE dispatch
//  point and ONE validation point govern all seven members. Duplicating the conversion inside a
//  wrapper would let the wrapper and the dispatcher drift apart, which is precisely the class of
//  divergence a single decision record exists to prevent.
//
//  TEXT-TO-BYTES IS THE CALLER'S STEP, NOT THIS TYPE'S
//  --------------------------------------------------------------------------------------------
//  These two conversions move BYTES to and from the Base64 or hexadecimal TEXT REPRESENTATION of
//  those bytes. They perform no character-encoding step of their own, and they take no encoding
//  parameter for one, because the oracle's own caller does that step explicitly on both sides:
//
//      ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L419
//          mle_result.Text = _crypto.BlobToString(Blob(mle_result.Text, EncodingANSI!),
//                                                 Enums.CRYPTO_ENCODING_BASE64)
//      u_cst_tabpage_utility_crypto.sru:L434
//          mle_result.Text = String(_crypto.StringToBlob(mle_result.Text,
//                                                        Enums.CRYPTO_ENCODING_BASE64),
//                                   EncodingANSI!)
//
//  The hexadecimal pair is identical in shape at :L577 and :L562. The Blob(...) and String(...)
//  calls are the caller's, so a UTF-8 assumption must NOT be made on the caller's behalf here.
//  A third site, :L652, encodes a random blob for display and does no text conversion at all,
//  which is the same surface used with no character encoding anywhere in sight.
//
//  THIS SURFACE IS SEPARATE FROM DIGEST AND CIPHER ENCODING - SEE LegacyDefaults DECISION D4
//  --------------------------------------------------------------------------------------------
//  Hash does NOT route through here. u_cst_tabpage_utility_crypto.sru:L404 reads
//  `mle_result.Text = _crypto.Hash(mle_result.Text, Enums.CRYPTO_HASH_SHA256)` - an
//  already-printable digest with no conversion call anywhere near it, and HashFile behaves the
//  same way at :L281 and :L489. The text encoding of a digest, of symmetric ciphertext, of RSA
//  ciphertext and of an RSA signature is therefore INTERNAL to those operations, and the single
//  place it is decided is LegacyDefaults.STRING_PAYLOAD_ENCODING - DECISION D4.
//
//  This type's job is to SUPPLY the primitives that HashProvider, HmacProvider,
//  SymmetricCipherProvider and RsaProvider use to honour D4 uniformly. That is why every member
//  here is cheap, allocation-conscious and FREE OF POLICY: it applies the encoding it is asked
//  for and never selects one on a caller's behalf. Note in particular that this file does not
//  read LegacyDefaults.STRING_PAYLOAD_ENCODING as a default for anything - a default encoding
//  here would quietly turn a caller-driven surface into a policy-driven one.
//
//  A NAME COLLISION EVERY CONSUMER WILL MEET, MEASURED RATHER THAN GUESSED
//  --------------------------------------------------------------------------------------------
//  The Base Class Library already declares a type called System.Text.EncodingProvider - the
//  abstract base for a custom character-encoding provider, an entirely unrelated concept. So a
//  file that imports BOTH System.Text AND this namespace cannot use the simple name
//  `EncodingProvider` at all: it fails to compile with
//
//      error CS0104: 'EncodingProvider' is an ambiguous reference between
//                    'PowerFramework.Security.Crypto.EncodingProvider' and
//                    'System.Text.EncodingProvider'
//
//  That is an observed compiler error from this repository's own build, not a theoretical risk,
//  and it is recorded here because the sibling providers are the files most likely to hit it -
//  SymmetricCipherProvider and RsaProvider have every reason to import System.Text, and
//  LegacyDefaults already does. Three points a consumer needs:
//
//      * THE NAME IS NOT NEGOTIABLE. It is the type name the migration plan assigns to this file,
//        and it is what the composition root and the OpenAPI-described cryptographic surface refer
//        to, so it is kept rather than renamed around a collision.
//      * A FILE IN THIS NAMESPACE IS UNAFFECTED. A namespace member is resolved before any using
//        directive is consulted, so LegacyDefaults compiles cleanly with System.Text imported.
//        The collision bites only a file that reaches BOTH namespaces through using directives.
//      * THE FIX IS AT THE CONSUMER, AND IT IS ONE LINE. Either qualify the character encoding at
//        its use site - `System.Text.Encoding.UTF8` - and drop the System.Text import, or add a
//        using alias for whichever of the two types that file means. Do not add an alias here: an
//        alias in this file would bind nothing for anyone else.
//
//  ==============================================================================================
//  THE THREE DECISIONS THAT BELONG TO THIS SURFACE
//  ==============================================================================================
//  The closed binary cannot be read, so the exact text these three functions produced is
//  UNOBSERVABLE FROM THE REPOSITORY. Each decision below is therefore recorded with its reason
//  and is explicitly NOT CLAIMED AS VERIFIED PARITY: it is settleable only against the
//  behavioural oracle, by capturing legacy output for a workflow and comparing it with the target
//  output for the same workflow. Each is made in ONE place so the oracle can correct it in one.
//
//  DECISION E1 - HEXADECIMAL IS EMITTED IN UPPER CASE, AND DECODING ACCEPTS EITHER CASE
//  --------------------------------------------------------------------------------------------
//  Chosen: upper case, which is what Convert.ToHexString produces. Four reasons, and the
//  repository's silence is itself one of them:
//
//      * ONE PRIMITIVE, NO POST-PROCESSING. Convert.ToHexString is a single call whose output is
//        upper case; a lower-case choice means Convert.ToHexStringLower, equally a single call, so
//        the tie cannot be broken on implementation grounds and must be broken on evidence.
//      * THE REPOSITORY CARRIES NO LOWER-CASE HEXADECIMAL EVIDENCE AT ALL. The changelog entry
//        that INTRODUCED this pair specifies no case [logfile.md:L931], no call site compares the
//        result against a literal, and the adjacent number-base helpers give nothing away either
//        because dectohex.srf and hextodec.srf delegate to the equally native DecToString and
//        StringToDec [ws_objects/pfw.utility.pbl.src/dectohex.srf:L10].
//      * THIS IS A BLOB TRANSPORT ENCODING, NOT A MESSAGE DIGEST. LegacyDefaults' DECISION D4
//        flags lower-case hexadecimal as the more common convention for MD5 and the SHA family
//        specifically; that convention is about digests and does not reach a surface whose
//        evidenced uses are rendering a random blob for display
//        [u_cst_tabpage_utility_crypto.sru:L652] and a caller-driven round trip through a text
//        control [:L562, :L577].
//      * THE CHOICE CANNOT BREAK A ROUND TRIP. Decoding accepts upper, lower and mixed case, so
//        HexDecode(HexEncode(x)) holds either way and so does HexDecode of a hexadecimal string
//        that reached the caller from somewhere else in either case.
//
//  THE EMITTED CASE IS THE OBSERVABLE THE ORACLE WOULD SETTLE. If a capture shows lower case, the
//  correction is one call - Convert.ToHexStringLower - at one place in this file, and no decoding
//  behaviour changes at all.
//
//  DECISION E2 - BASE64 IS THE STANDARD ALPHABET, PADDED, ON ONE LINE
//  --------------------------------------------------------------------------------------------
//  Chosen: the standard RFC 4648 alphabet, PADDING EMITTED, NO LINE BREAKS INSERTED - which is
//  what Convert.ToBase64String produces, its formatting options defaulting to none. Decoding is
//  tolerant of embedded and surrounding white space, which is a property of
//  Convert.FromBase64String rather than anything this file adds.
//
//  NO URL-SAFE VARIANT IS OFFERED, because the legacy declares none: enums.sru publishes exactly
//  two encodings, Base64 at L924 and hexadecimal at L925, and adding a third would publish an
//  operation the legacy never had. The same reasoning excludes Base32, percent-encoding and every
//  other alphabet.
//
//  This decision has genuine in-repository corroboration, which is worth more than the others'
//  reasoning: ws_objects/pfw.net.http.ext.pbl.src/n_cst_alipay.sru:L145 encodes an RSA signature
//  with `BlobToString(..., Enums.CRYPTO_ENCODING_BASE64)` and :L518 decodes one with
//  `StringToBlob(sign, Enums.CRYPTO_ENCODING_BASE64)`, for a payment gateway whose published wire
//  format is standard padded Base64 on a single line. Had the legacy emitted an unpadded, line-
//  broken or URL-safe form, that integration could not have worked. White-space tolerance on the
//  decoding side matters for the same practical reason the oracle's own demo shows: encoded text
//  is round-tripped through a multi-line text control [:L419 and :L434, :L577 and :L562], and
//  text that has been through a control or a transport may come back with white space attached.
//
//  DECISION E3 - BlobReverse REPORTS TRUE FOR EVERY BUFFER IT REVERSES, INCLUDING THE TRIVIAL ONES
//  --------------------------------------------------------------------------------------------
//  The legacy returns a `boolean` [n_crypto.sru:L13] and that boolean IS PART OF THE OBSERVABLE
//  CONTRACT, so it is preserved rather than dropped in favour of a value-returning shape. What
//  the closed binary returned false for is unobservable, so the semantics are decided here:
//  TRUE MEANS THE POSTCONDITION HOLDS - the buffer now contains the reverse of what it contained
//  on entry. A zero-byte and a one-byte buffer are each their own reverse, so both satisfy that
//  postcondition trivially and both report true.
//
//  Consequently this port NEVER RETURNS FALSE, and that is a deliberate outcome rather than an
//  oversight. The only failure the legacy boolean could plausibly have carried is an invalid blob
//  handle at the native-interface level, which has no managed analogue: in managed code the array
//  either exists or the argument is null, and a null is a caller defect reported through the
//  uniform null policy below rather than through the return value.
//
//  THE ALTERNATIVE WAS CONSIDERED AND REJECTED. Returning false for an empty buffer would make
//  the boolean carry information, but it would also make every caller that writes
//  `if (BlobReverse(ref b))` treat a legitimately empty payload as an error. Reporting failure for
//  a valid input is a worse outcome than a boolean that is always true, so the postcondition
//  reading wins. Oracle-only verifiable, like E1 and E2.
//
//  ==============================================================================================
//  THE TWO FAILURE SHAPES, ONE PER CONDITION
//  ==============================================================================================
//  There are exactly two ways a call here can fail, and each has exactly one shape. No Try-
//  variant exists for either: it would break the seven-member census, and it would silence a
//  malformed input the legacy surface would have rejected.
//
//      condition                     shape                            raised by
//      ---------------------------   ------------------------------   ------------------------
//      encoding is not one of the    ArgumentOutOfRangeException      this file, after screening
//      two published values          on the `encoding` parameter      LegacyDefaults
//                                                                     .IsSupportedEncoding
//      the text is not well formed   FormatException                  Convert.FromBase64String
//      for the requested encoding                                     or Convert.FromHexString,
//                                                                     PROPAGATED UNCHANGED
//      a reference argument is null  ArgumentNullException            this file, first statement
//
//  The malformed-text shape is deliberately NOT caught and rewrapped. Propagating the Base Class
//  Library's own exception keeps its message, its parameter name and its stack intact, and
//  re-throwing a hand-written substitute would add a failure mode of this port's own invention on
//  top of a condition the primitive already reports precisely.
//
//  THE NULL POLICY IS UNIFORM ACROSS ALL SEVEN MEMBERS: a null reference argument raises
//  ArgumentNullException from the first statement of the member. Nullable reference types are
//  enabled repository-wide and all seven members declare their reference parameters
//  non-nullable, so a null is a caller defect rather than a data condition; the guard is retained
//  regardless because nullability annotations are not enforced at run time and this assembly is
//  reachable from a service boundary. This matches the sibling catalogue, which guards
//  LegacyDefaults.NormalizeKeyMaterial the same way. An EMPTY buffer or an EMPTY string, by
//  contrast, is valid input on every member and is never an error - see each member's remarks.
//
//  ==============================================================================================
//  WHAT THIS FILE IS NOT
//  ==============================================================================================
//  IT HOLDS NO KEY MATERIAL AND MUST NEVER BE GIVEN ANY. There is no key, initialization vector,
//  passphrase, certificate, PEM block or credential here as a value, as a default argument, as
//  sample data or as an example in documentation - the only byte sequences appearing anywhere in
//  this file's documentation are obviously synthetic single-digit values. Nothing from any of the
//  repository's known hardcoded-secret sites is reproduced in any form; those sites are read as
//  usage reference only and their remediation posture is never-replicate-document-and-rotate.
//
//  IT READS NO CONFIGURATION and has no options type, so there is nothing for a secret to be
//  injected into.
//
//  IT LOGS NOTHING, AND THAT IS A SECURITY DECISION RATHER THAN AN OMISSION. This type sees
//  whatever a caller happens to be encoding, which on the sibling providers' paths includes
//  ciphertext and signature bytes; a provider that logged its input or output would leak exactly
//  that. It therefore takes no logger dependency at all, so there is no logging call to add later
//  by accident.
//
//  IT REGISTERS NO ROUTE AND OPENS NO PATH. These operations are reached only through the
//  service's authenticated cryptographic endpoints, behind the composition root's default-deny
//  authorization policy.
//
//  IT DECLARES NO CONSTANT OF ITS OWN FOR AN ENCODING. The 30 legacy CRYPTO_* constants are
//  REFERENCED AND NEVER REDECLARED: every CRYPTO_* occurrence in this file is either an
//  `Enums.`-qualified reference or documentation text, which is also why no identifier declared
//  below uses the legacy SCREAMING_SNAKE spelling. That spelling is preserved verbatim only in
//  the shared kernel's catalogue and in the files on the root .editorconfig's BAND 3 roster - the
//  single source of truth for that list - which its naming suppressions are scoped to; this file is
//  not one of them, so a SCREAMING_SNAKE declaration here would be
//  a build failure under warnings-as-errors rather than a style disagreement.
//
//  IT REPRODUCES NO LAZY-INITIALIZATION GUARD. The legacy declares a type-shadowing global
//  auto-instance `global n_crypto n_crypto` [n_crypto.sru:L75] and every wrapper opens with
//  `if Not IsValid(n_crypto) then n_crypto = Create n_crypto` [base64encode.srf:L10,
//  hexencode.srf:L10, base64decode.srf:L10, hexdecode.srf:L10]. Both exist only because
//  PowerBuilder resolves a single flat global namespace, offers no container, and does not
//  eagerly construct that global. THEY HAVE NO ANALOGUE TO REPRODUCE: this type is a
//  DI-registered instance service with a container-managed lifetime, so there is no global to
//  check and nothing to construct on first use. See LegacyDefaults' non-port record for the same
//  finding stated once for the whole folder.
// ==============================================================================================

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// Converts binary payloads to and from their printable Base64 or hexadecimal text
/// representation, reproducing the legacy cryptographic library's blob-to-text surface at
/// ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11-L13 together with the four global function
/// wrappers built on it.
/// </summary>
/// <remarks>
/// <para>
/// Seven members exactly: three from the native class declarations and four from the wrappers.
/// The two encodings are the only two the legacy publishes,
/// <see cref="Enums.CRYPTO_ENCODING_BASE64"/> and <see cref="Enums.CRYPTO_ENCODING_HEX"/>
/// [ws_objects/pfw.shared.pbl.src/enums.sru:L924-L925]; no third encoding, no URL-safe variant
/// and no convenience overload is offered.
/// </para>
/// <para>
/// PERFORMS NO CHARACTER ENCODING. These methods move bytes to and from the TEXT REPRESENTATION
/// of those bytes. Turning a string into bytes in the first place is the caller's step, exactly as
/// it is in the oracle's own caller
/// [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L419, :L434].
/// </para>
/// <para>
/// APPLIES NO POLICY. It encodes what it is asked to encode and never selects an encoding on a
/// caller's behalf. The separate question of which encoding the string-shaped hash, cipher and RSA
/// overloads use internally is answered once by
/// <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/> - DECISION D4 - and this type
/// deliberately does not consult it.
/// </para>
/// <para>
/// HOLDS NO KEY MATERIAL, READS NO CONFIGURATION AND LOGS NOTHING. The absence of a logger is
/// deliberate: this type sees whatever a caller is encoding, so anything it logged would be
/// exactly the payload that must not be logged.
/// </para>
/// <para>
/// STATELESS AND THREAD-SAFE. There is no field, no cache and no static mutable state, so one
/// instance serves any number of concurrent requests and instances may be created freely. It is
/// registered in the service container by the composition root and consumed by constructor
/// injection; it is never reached through a global or a static.
/// </para>
/// <para>
/// NAME COLLISION, FOR CONSUMERS. The Base Class Library also declares
/// <c>System.Text.EncodingProvider</c>, an unrelated type. A file importing both
/// <c>System.Text</c> and this namespace cannot use the simple name and fails with CS0104; qualify
/// the character encoding at its use site or add a using alias in that file. See this file's
/// header for the full note.
/// </para>
/// </remarks>
public sealed class EncodingProvider
{
    /// <summary>
    /// The single message text for the unsupported-encoding failure, shared by
    /// <see cref="BlobToString"/> and <see cref="StringToBlob"/> so that the one condition has one
    /// wording as well as one shape.
    /// </summary>
    /// <remarks>
    /// A constant rather than a helper method, deliberately: this type declares no static member
    /// of any kind beyond this string, because its operations must be reachable only as instance
    /// members through the container.
    /// </remarks>
    private const string UnsupportedEncodingMessage =
        "Not a published encoding. The set is exactly Base64 (0) and hexadecimal (1) " +
        "[ws_objects/pfw.shared.pbl.src/enums.sru:L924-L925].";

    /// <summary>
    /// Initializes a new instance of the <see cref="EncodingProvider"/> class.
    /// </summary>
    /// <remarks>
    /// An ordinary parameterless constructor, declared explicitly so that the shape of this type
    /// is unambiguous: it takes no dependency, captures no state and performs no initialization,
    /// which is what makes it safe to register with any lifetime. It deliberately does not
    /// reproduce the legacy's lazy-construction guard
    /// [ws_objects/pfw.crypto.pbl.src/base64encode.srf:L10]; a container-managed instance has
    /// nothing to check before use.
    /// </remarks>
    public EncodingProvider()
    {
    }

    /// <summary>
    /// Converts binary data to its printable text representation in the requested encoding. This
    /// is the ENCODE direction: blob in, string out.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode. An EMPTY array is valid and yields an empty string in either
    /// encoding.
    /// </param>
    /// <param name="encoding">
    /// Either <see cref="Enums.CRYPTO_ENCODING_BASE64"/> or
    /// <see cref="Enums.CRYPTO_ENCODING_HEX"/>.
    /// </param>
    /// <returns>
    /// The encoded text: standard padded single-line Base64, or upper-case hexadecimal with no
    /// separator.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="encoding"/> is not one of the two published encodings.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string BlobToString(readonly blob data, readonly long
    /// encoding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L12], whose implementation lives
    /// in the closed native binary and cannot be read. The Base64 shape is DECISION E2 and the
    /// hexadecimal letter case is DECISION E1, both recorded in this file's header and both
    /// verifiable only against the behavioural oracle.
    /// </para>
    /// <para>
    /// This is the single encoding dispatch point for the whole type:
    /// <see cref="Base64Encode"/> and <see cref="HexEncode"/> both route through it rather than
    /// calling a conversion primitive of their own, which is how the legacy's own wrappers are
    /// built [base64encode.srf:L11, hexencode.srf:L11].
    /// </para>
    /// <para>
    /// One allocation per call - the returned string - and no intermediate buffer.
    /// </para>
    /// </remarks>
    public string BlobToString(byte[] data, long encoding)
    {
        ArgumentNullException.ThrowIfNull(data);

        // The one validation point for both directions. Screening through the shared catalogue
        // rather than against local literals is what keeps this type's accepted set identical to
        // every sibling provider's, and it is also why no CRYPTO_* constant is declared here.
        if (!LegacyDefaults.IsSupportedEncoding(encoding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoding),
                encoding,
                UnsupportedEncodingMessage);
        }

        // The screen above admits exactly two values, so this conditional is total: there is no
        // unreachable default arm to leave uncovered, and adding one would be dead code.
        //
        // DECISION E2 - Convert.ToBase64String emits the standard alphabet WITH PADDING and
        // inserts NO LINE BREAKS, its formatting options defaulting to none. Corroborated by the
        // in-repository consumer at
        // ws_objects/pfw.net.http.ext.pbl.src/n_cst_alipay.sru:L145.
        //
        // DECISION E1 - Convert.ToHexString emits UPPER CASE with no separator. This is the one
        // observable the behavioural oracle would settle; if a capture shows lower case, the
        // correction is Convert.ToHexStringLower here and nothing else anywhere.
        return encoding == Enums.CRYPTO_ENCODING_BASE64
            ? Convert.ToBase64String(data)
            : Convert.ToHexString(data);
    }

    /// <summary>
    /// Converts printable text in the requested encoding back to the binary data it represents.
    /// This is the DECODE direction: string in, blob out.
    /// </summary>
    /// <param name="data">
    /// The encoded text. An EMPTY string is valid and yields an empty array in either encoding.
    /// Base64 text may carry surrounding or embedded white space; hexadecimal text may not.
    /// </param>
    /// <param name="encoding">
    /// Either <see cref="Enums.CRYPTO_ENCODING_BASE64"/> or
    /// <see cref="Enums.CRYPTO_ENCODING_HEX"/>.
    /// </param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="encoding"/> is not one of the two published encodings.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="data"/> is not well formed for <paramref name="encoding"/>: for Base64, a
    /// character outside the standard alphabet, a bad padding residue, or a URL-safe substitution;
    /// for hexadecimal, an odd number of digits, a non-hexadecimal character, or any white space.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function blob StringToBlob(readonly string data, readonly long
    /// encoding)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L11], the exact inverse of
    /// <see cref="BlobToString"/>, sharing its dispatch, its validation and its unsupported-
    /// encoding failure shape.
    /// </para>
    /// <para>
    /// TOLERANCES, AND WHY THEY ARE ASYMMETRIC. Base64 decoding ignores white space and
    /// hexadecimal decoding does not; hexadecimal decoding accepts upper, lower and mixed case.
    /// All three are properties of the chosen primitives rather than behaviour added here, and
    /// adding a white-space stripper for hexadecimal would be inventing a tolerance the legacy
    /// gives no evidence of. The case tolerance is what makes DECISION E1 safe in both
    /// directions, and the white-space tolerance is what lets encoded text survive the round trip
    /// through a text control that the oracle's own caller performs
    /// [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L419 and :L434].
    /// </para>
    /// <para>
    /// A malformed payload raises <see cref="FormatException"/> from the primitive itself,
    /// PROPAGATED UNCHANGED rather than caught and rewrapped, so its message, parameter name and
    /// stack survive. No Try-shaped alternative is offered: it would exceed this type's member
    /// census and would silence input the legacy surface rejected.
    /// </para>
    /// </remarks>
    public byte[] StringToBlob(string data, long encoding)
    {
        ArgumentNullException.ThrowIfNull(data);

        // The same validation point, the same message and the same shape as the encode direction.
        if (!LegacyDefaults.IsSupportedEncoding(encoding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoding),
                encoding,
                UnsupportedEncodingMessage);
        }

        // Total for the same reason as the encode direction. Both primitives return an empty
        // array for an empty string and raise FormatException - and nothing else - for text that
        // is not well formed, which is the single documented malformed-input shape.
        return encoding == Enums.CRYPTO_ENCODING_BASE64
            ? Convert.FromBase64String(data)
            : Convert.FromHexString(data);
    }

    /// <summary>
    /// Reverses the order of the bytes in a buffer, in place.
    /// </summary>
    /// <param name="data">
    /// The buffer to reverse. Reversed IN PLACE, so the caller's array contents change and the
    /// reference itself is left as it was.
    /// </param>
    /// <returns>
    /// <see langword="true"/>, always, for any non-null buffer - see DECISION E3.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function boolean BlobReverse(ref blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L13], the only member of this surface that
    /// mutates its argument and the only one declared <c>ref</c>. The <c>ref</c> is retained
    /// because the legacy declares it: it is part of the observable contract, and it also leaves
    /// room for an implementation that assigns a fresh array without changing the signature. This
    /// implementation does not need that room - it reverses the existing buffer, so the call
    /// allocates nothing.
    /// </para>
    /// <para>
    /// THE BOOLEAN IS PRESERVED RATHER THAN DROPPED. Returning the reversed array instead and
    /// discarding the boolean would be a smaller API but a different contract, so it is not done;
    /// nor is a non-mutating companion overload added, which would exceed this type's member
    /// census.
    /// </para>
    /// <para>
    /// EVERY LENGTH, DELIBERATELY. A NULL buffer is a caller defect and raises
    /// <see cref="ArgumentNullException"/>, in line with the uniform null policy of the other six
    /// members. A ZERO-BYTE and a ONE-BYTE buffer are each already their own reverse, so both are
    /// left untouched and both report success - neither is an error, and neither is a no-op
    /// reported as a failure. An EVEN length swaps every byte in pairs; an ODD length swaps every
    /// byte in pairs and leaves the exact middle byte in place, which needs no special handling.
    /// The operation is its own inverse at every length, so reversing twice restores the original
    /// buffer.
    /// </para>
    /// <para>
    /// The return value is <see langword="true"/> in every case that returns at all - DECISION E3
    /// in this file's header records why, and why the alternative of reporting false for an empty
    /// buffer was rejected.
    /// </para>
    /// </remarks>
    public bool BlobReverse(ref byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Zero-byte and one-byte buffers are their own reverse. The early return is not a
        // micro-optimization dressed up as a guard: it is the point at which DECISION E3's
        // trivial cases are stated in code, so that a reader can see they are successes rather
        // than an accident of how the primitive happens to behave.
        if (data.Length < 2)
        {
            return true;
        }

        // In place, no allocation, and correct for both parities: an odd-length buffer's middle
        // byte is already in its final position.
        Array.Reverse(data);

        // DECISION E3 - true means the postcondition holds: the buffer now contains the reverse
        // of what it contained on entry. This port has no false arm, because the only failure the
        // legacy boolean could have carried is a native-interface handle fault with no managed
        // analogue.
        return true;
    }

    /// <summary>
    /// Encodes binary data as standard Base64 text.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode. An EMPTY array is valid and yields an empty string.
    /// </param>
    /// <returns>Standard padded single-line Base64 text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <remarks>
    /// Substitutes <c>global function string base64encode(readonly blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/base64encode.srf:L10-L11], whose entire body is
    /// <c>return n_crypto.BlobToString(data, Enums.CRYPTO_ENCODING_BASE64)</c>. That delegation is
    /// mirrored exactly: this method calls <see cref="BlobToString"/> and holds no conversion
    /// logic of its own, so the Base64 shape of DECISION E2 is decided in one place. The wrapper's
    /// lazy-construction guard at :L10 has no analogue and is deliberately not reproduced.
    /// </remarks>
    public string Base64Encode(byte[] data) =>
        BlobToString(data, Enums.CRYPTO_ENCODING_BASE64);

    /// <summary>
    /// Encodes binary data as upper-case hexadecimal text.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode. An EMPTY array is valid and yields an empty string.
    /// </param>
    /// <returns>Upper-case hexadecimal text with no separator, two digits per byte.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <remarks>
    /// Substitutes <c>global function string hexencode(readonly blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/hexencode.srf:L10-L11], whose entire body is
    /// <c>return n_crypto.BlobToString(data, Enums.CRYPTO_ENCODING_HEX)</c>. The delegation is
    /// mirrored exactly, so the letter case of DECISION E1 is decided in one place. The changelog
    /// entry that introduced this function and its inverse describes them only as hexadecimal
    /// encoding and decoding of a blob, specifying no case [logfile.md:L931].
    /// </remarks>
    public string HexEncode(byte[] data) =>
        BlobToString(data, Enums.CRYPTO_ENCODING_HEX);

    /// <summary>
    /// Decodes standard Base64 text back to the binary data it represents.
    /// </summary>
    /// <param name="data">
    /// The Base64 text. An EMPTY string is valid and yields an empty array. Surrounding and
    /// embedded white space is ignored.
    /// </param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="data"/> is not well-formed Base64.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function blob base64decode(readonly string data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/base64decode.srf:L10-L11], whose entire body is
    /// <c>return n_crypto.StringToBlob(data, Enums.CRYPTO_ENCODING_BASE64)</c>. Note the
    /// direction, which the legacy fixes and this port does not choose: DECODE takes a string and
    /// returns bytes. Accepts what <see cref="Base64Encode"/> produces, so the round trip holds
    /// for any byte sequence.
    /// </remarks>
    public byte[] Base64Decode(string data) =>
        StringToBlob(data, Enums.CRYPTO_ENCODING_BASE64);

    /// <summary>
    /// Decodes hexadecimal text back to the binary data it represents, accepting upper, lower or
    /// mixed case.
    /// </summary>
    /// <param name="data">
    /// The hexadecimal text, two digits per byte. An EMPTY string is valid and yields an empty
    /// array. Case is not significant; white space is not permitted.
    /// </param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="data"/> has an odd number of digits, contains a non-hexadecimal character,
    /// or contains white space.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function blob hexdecode(readonly string data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/hexdecode.srf:L10-L11], whose entire body is
    /// <c>return n_crypto.StringToBlob(data, Enums.CRYPTO_ENCODING_HEX)</c>. The case tolerance is
    /// what makes DECISION E1 safe: whichever case the oracle eventually settles for
    /// <see cref="HexEncode"/>, this method already accepts it, so no round trip can fail on
    /// casing.
    /// </remarks>
    public byte[] HexDecode(string data) =>
        StringToBlob(data, Enums.CRYPTO_ENCODING_HEX);
}
