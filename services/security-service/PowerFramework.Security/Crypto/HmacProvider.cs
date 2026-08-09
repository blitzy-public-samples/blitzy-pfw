// ==============================================================================================
//  HmacProvider - the KEYED half of the legacy cryptographic hashing surface
//  --------------------------------------------------------------------------------------------
//  ORACLE         ws_objects/pfw.crypto.pbl.src/n_crypto.sru
//                 86 lines carrying exactly 65 `public function` declarations at L9-L73 and NO
//                 IMPLEMENTATION BODY ANYWHERE IN THE REPOSITORY. L8 declares
//                 `global type n_crypto from nonvisualobject native "pfw.dll"`, so all 65
//                 behaviours live inside a closed native binary for which no C++ source exists
//                 here or anywhere it can be read from. Every member below is therefore a
//                 SUBSTITUTION against System.Security.Cryptography, never a translation of
//                 legacy code: there is no body to port, only declarations to satisfy and
//                 observed behaviour to preserve.
//
//  CONSTANTS      ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933
//                 The six hash-type constants, inside the `/*--- Crypto ---*/` block that opens at
//                 L921 and closes at L969. A stray duplicate `/*--- End Crypto ---*/` marker
//                 appears far below at L999 and is a trap: the authoritative block ends at L969.
//                 None of those 30 constants is redeclared here; each is referenced through
//                 `Enums.` on the shared kernel.
//
//  CALL SITE      ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449
//                 The one place in the repository that calls a KEYED overload, sitting under a
//                 label reading "HMAC" and passing three arguments with
//                 `Enums.CRYPTO_HASH_SHA256`. It is read here as PROOF THAT THE 3-ARGUMENT KEYED
//                 FORM IS IN LIVE USE and for nothing else: the key it passes is a hardcoded
//                 literal and one of the repository's known secret sites, so it is not quoted,
//                 paraphrased or partially transcribed anywhere in this file or in its tests.
//
//  READ ONLY      Every ws_objects/** path named in this file is READ ONLY. It is the behavioural
//                 oracle for parity testing, never an edit target. Locators are provenance for a
//                 decision and nothing more: no legacy file is edited, moved, reformatted or
//                 re-exported by this port. That applies to the demo objects as much as to the
//                 framework ones, and most of all to the demo objects that carry hardcoded
//                 credentials - the remediation posture for those is NEVER REPLICATE, DOCUMENT,
//                 AND ROTATE, never "edit the legacy file".
//
//  ==============================================================================================
//  THE CENSUS THIS FILE SATISFIES: EXACTLY 6 PUBLIC MEMBERS, AND NOT ONE MORE
//  ==============================================================================================
//  n_crypto.sru declares SIX `Hash` overloads at L21-L26 and THREE `HashFile` overloads at
//  L27-L29. This file owns the KEYED ones and nothing else. The split with HashProvider is total
//  and non-overlapping, and it is a boundary rather than a convenience:
//
//      :L21-L22    Hash, unkeyed, 2 overloads          HashProvider - NOT HERE
//      :L23-L26    Hash, keyed, 4 overloads            THIS FILE
//      :L27        HashFile, unkeyed, 1 overload       HashProvider - NOT HERE
//      :L28-L29    HashFile, keyed, 2 overloads        THIS FILE
//
//      legacy declaration                                             locator          C# member
//      -----------------------------------------------------------    -------------    ----------
//      public function string Hash(readonly string data,               n_crypto.sru     Hash
//                                 readonly string key,                 :L23             (string,
//                                 readonly long ntype)                                   string)
//      public function string Hash(readonly string data,               n_crypto.sru     Hash
//                                 readonly blob key,                   :L24             (string,
//                                 readonly long ntype)                                   byte[])
//      public function string Hash(readonly blob data,                 n_crypto.sru     Hash
//                                 readonly string key,                 :L25             (byte[],
//                                 readonly long ntype)                                   string)
//      public function string Hash(readonly blob data,                 n_crypto.sru     Hash
//                                 readonly blob key,                   :L26             (byte[],
//                                 readonly long ntype)                                   byte[])
//                                                                                       ---- 4 ----
//      public function string HashFile(readonly string filename,       n_crypto.sru     HashFile
//                                     readonly string key,             :L28             (string,
//                                     readonly long ntype)                               string)
//      public function string HashFile(readonly string filename,       n_crypto.sru     HashFile
//                                     readonly blob key,               :L29             (string,
//                                     readonly long ntype)                               byte[])
//                                                                                       ---- 2 ----
//                                                                                       TOTAL   6
//
//  THE FOUR `Hash` OVERLOADS ARE THE COMPLETE CROSS PRODUCT of {string data, blob data} by
//  {string key, blob key}. All four cells are present, none is omitted and none is collapsed. C#
//  could express the whole family as one span-based method, and doing so would be WRONG HERE: the
//  four-way shape is the observable legacy surface, a caller reading the member list must see the
//  same four cells the legacy published, and the folder census depends on the count. The two
//  `HashFile` overloads carry only the key-type axis, because `filename` is always a string - the
//  legacy declares no blob-filename form and none is invented.
//
//  THIS IS A HARD CENSUS, NOT A LOWER BOUND. The six providers in this folder together account for
//  3 + 5 + 3 + 6 + 32 + 14 = 63 ported declarations plus 2 deliberately non-ported, which is
//  exactly the 65 the oracle declares. This file is the 6. A seventh public member here - a
//  convenience overload, a Try-variant, a byte[]-returning shortcut, a verify helper or a promoted
//  private seam - would silently break that arithmetic, so the count and the absence of any unkeyed
//  overload are both asserted by reflection in a unit test rather than left to inspection.
//
//  RETURNS TEXT, NOT BYTES. All six legacy declarations return `string`, and the oracle's own
//  caller assigns the keyed result straight into a plain multi-line text control
//  [u_cst_tabpage_utility_crypto.sru:L449]. The digest is therefore ALREADY TEXT-ENCODED when it
//  leaves the function, and the encoding is not this file's choice - see the D4 section below.
//
//  ==============================================================================================
//  KEY HANDLING: THE CONSTRAINT THAT DOMINATES THIS FILE
//  ==============================================================================================
//  This is the first file in the folder's creation order whose surface TAKES A KEY, so the
//  repository-wide "nothing hardcoded" mandate binds it harder than any of its siblings. The rules
//  are mechanical, not aspirational, and each one is visible in the code below.
//
//      1. THE KEY ARRIVES AS A PARAMETER, ALREADY RESOLVED. A caller supplies an opaque key
//         reference to the endpoint layer, which resolves it against the key-store descriptor in
//         application settings and hands THIS TYPE THE RESOLVED BYTES. Consequently there is no
//         IConfiguration, no IOptions, no environment-variable read and no file read anywhere
//         below, and no configuration or options type is injected. This provider cannot obtain a
//         key by any route other than its own parameter list, which is what makes "raw key
//         material never crosses the wire from a caller" enforceable at the boundary instead of
//         merely intended.
//
//      2. NO KEY MATERIAL IS RETAINED. There is no instance field, no static field, no cache and
//         no memoized keyed-algorithm object anywhere in this type. The one field is the injected
//         encoding provider, which holds no state at all. The keyed algorithm is CONSTRUCTED,
//         USED AND DISPOSED INSIDE A SINGLE CALL - which is simultaneously the thread-safety
//         requirement, because those objects carry mutable digest state and are not safe to share.
//
//      3. NOTHING IS LOGGED, AND THE ABSENCE OF A LOGGER IS DELIBERATE. This type sees both the
//         payload and the key, so anything it logged would be exactly what must not be logged.
//         There is no ILogger field, no ILogger constructor parameter and no diagnostic write
//         below. Every exception message names only a parameter, a length or an algorithm
//         identifier; NO EXCEPTION MESSAGE INTERPOLATES KEY CONTENT, and none may be added that
//         does.
//
//      4. TRANSIENT COPIES ARE WIPED, CALLER BUFFERS ARE NOT. Where this file makes its own copy
//         of key bytes - which happens exactly twice, when a `string` key is encoded - that copy
//         is zeroed in a `finally`, so the wipe happens on the exception paths too. A caller's
//         `byte[]` key is passed through as a read-only span and is NEVER mutated: zeroing an
//         array the caller still owns would be an observable side effect, and the legacy had none.
//         Wiping this file's own copies, by contrast, is unobservable to any caller and therefore
//         not a behavioural change.
//
//      5. NO EMBEDDED KEY, INITIALIZATION VECTOR, PASSPHRASE, CERTIFICATE OR PEM BLOCK EXISTS
//         HERE - not as a value, not as a default argument, not as sample data and not as an
//         example in documentation. Nothing from any of the repository's known hardcoded-secret
//         sites is reproduced in any form, and that explicitly includes the hardcoded keyed-hash
//         key at the one keyed call site this file cites as usage evidence. There is no Base64 or
//         hexadecimal blob anywhere below, and no literal that could be mistaken for real key
//         material.
//
//  ==============================================================================================
//  DECISION M1 - THE STRING-TO-BYTES STEP, AND WHY IT IS BORROWED RATHER THAN RE-DECIDED
//  ==============================================================================================
//  Four of the six members accept text on one axis or the other, and text has to become bytes
//  before an HMAC can consume it. The closed binary did that conversion internally, so the choice
//  is invisible from the repository and is a REASONED DECISION RATHER THAN A MEASURED FACT. It is
//  also not cosmetic: a UTF-8 choice and an ANSI choice produce DIFFERENT authenticators for any
//  input containing a non-ASCII character.
//
//  THIS FILE MAKES NO NEW CHOICE. It reuses the two decisions that already exist, one per axis,
//  and that is the whole point:
//
//      the data axis    HashProvider.InputTextEncoding - DECISION H1, the same named member the
//                       UNKEYED path uses for its own string data. Borrowing it rather than
//                       restating it is what guarantees a caller cannot get one answer from
//                       `Hash(data, ntype)` and a differently-encoded one from
//                       `Hash(data, key, ntype)` over the same text.
//      the key axis     LegacyDefaults.KeyMaterialEncoding - DECISION D1's encoding half, the same
//                       named member the symmetric cipher surface uses for a string key. Key
//                       material is key material wherever it enters the folder.
//
//  Both resolve to UTF-8 today, for the same recorded reason: UTF-8 is byte-identical to an ANSI
//  conversion across the whole ASCII range - which is what every field in the oracle's own demo is
//  typed into - whereas PowerBuilder's ANSI conversion is code-page and locale dependent and so is
//  NOT REPRODUCIBLE IN A LINUX CONTAINER AT ALL. Choosing ANSI would mean choosing a code page,
//  and choosing a code page would be inventing a fact the repository does not contain.
//
//  That they agree is ASSERTED BY A TEST rather than assumed, so that if either decision is ever
//  re-baselined against the behavioural oracle the divergence is a build-visible failure instead
//  of a silent parity fork. And because both are single named members, settling either question
//  is a one-line correction in the file that owns it and nothing anywhere else.
//
//  ASCII-ONLY INPUTS ARE UNAFFECTED by all of this, which is exactly why the published
//  known-answer vectors for HMAC-MD5, HMAC-SHA1 and the HMAC-SHA-2 family are valid tests of this
//  file regardless of how M1 is eventually settled.
//
//  ==============================================================================================
//  DECISION M2 - THERE IS NO KEY DERIVATION, AND D1's LENGTH RULE DOES NOT APPLY HERE
//  ==============================================================================================
//  DECISION D1 records that NO KEY-DERIVATION FUNCTION IS REACHABLE anywhere in the legacy's 65
//  declarations - no PBKDF2, no scrypt, no bcrypt, no Argon2, no iteration count and no salt
//  concept - so a passphrase is used AS RAW KEY BYTES. That is honoured here exactly: nothing
//  below derives, stretches or salts a key.
//
//  This is materially weaker than a derived key, and it is preserved rather than corrected. A
//  short or low-entropy passphrase becomes a short or low-entropy key with no stretching whatever,
//  and two deployments choosing the same passphrase get the same key. Deriving instead would
//  change every authenticator the legacy ever produced, so no value stored under the old rule
//  could be verified by this port.
//
//  NOW THE DISTINCTION THAT IS EASY TO GET WRONG, AND THE REASON THIS SECTION EXISTS. D1 has two
//  halves - an ENCODING half and a LENGTH-ADJUSTMENT half - and ONLY THE ENCODING HALF APPLIES IN
//  THIS FILE.
//
//      * `LegacyDefaults.NormalizeKeyMaterial`, which truncates over-long key material and
//        zero-pads over-short material to an exact length, EXISTS FOR THE FIXED-SIZE CIPHER KEYS
//        of the symmetric surface. DES needs exactly 8 bytes, AES-256 exactly 32; there is no
//        choice but to adjust.
//      * HMAC HAS NO FIXED KEY LENGTH. The construction itself accepts a key of ANY length: a key
//        shorter than the hash's block size is zero-padded up to it, and a key longer than the
//        block size is hashed down to the digest length first. That normalization is part of the
//        HMAC definition and the framework performs it.
//
//  So `NormalizeKeyMaterial` IS DELIBERATELY NOT CALLED BELOW. Calling it would impose a truncate-
//  or-pad rule the primitive does not want, and it would need a length to normalize to that
//  nothing in the legacy supplies - which is to say it would be an invented fact. An empty key, a
//  one-byte key, a block-length key and a key far longer than the block are consequently all
//  accepted and all produce well-defined results, which is asserted by a test precisely so that
//  nobody "completes" this file by adding the length rule.
//
//  NO MINIMUM KEY LENGTH IS ENFORCED, and adding one would be a behavioural change. The legacy
//  declarations carry no length precondition, the closed binary's behaviour on a short key is
//  unobservable, and rejecting a key the legacy accepted would break a caller that works today.
//  An empty key therefore yields a perfectly well-formed authenticator that authenticates
//  nothing meaningful; that is annotated as a known legacy weakness and left alone.
//
//  ==============================================================================================
//  DECISION M3 - THE KEYED CRC32 ARM IS A DEFINED, DOCUMENTED FAILURE - NEVER A FABRICATION
//  ==============================================================================================
//  `LegacyDefaults.IsSupportedHashType` accepts exactly six values [enums.sru:L928-L933], and the
//  keyed declarations at L23-L26 and L28-L29 take THE SAME `ntype` PARAMETER as the unkeyed ones.
//  On the face of the signatures, then, a caller may pass CRC32 to a keyed overload. That has to
//  be answered, and the honest answer is narrower than the signature.
//
//  THE PROBLEM, STATED PLAINLY RATHER THAN PAPERED OVER:
//
//      * THERE IS NO HMAC-CRC32, in this framework or anywhere else. HMAC is defined over an
//        iterated cryptographic hash function; its security proof rests on properties CRC32 does
//        not have. CRC32 is a LINEAR checksum, so a keyed construction over it is forgeable
//        directly from the algebra - an "authenticator" built on it AUTHENTICATES NOTHING AT ALL.
//      * THE LEGACY BEHAVIOUR IS UNOBSERVABLE. There is no body in the repository, and no call
//        site exercises the combination: the only keyed call site
//        [u_cst_tabpage_utility_crypto.sru:L449] passes SHA-256, and the only CRC32 call site
//        [:L281] is the UNKEYED `HashFile`. So there is nothing to read and nothing to imitate.
//
//  THE RULING. The keyed CRC32 arm produces a single, explicitly named, documented failure, in
//  THE SAME FAILURE SHAPE the unsupported-identifier arm uses - the same exception type naming the
//  same parameter - with its own wording so a reader can tell the two conditions apart. This is a
//  DOCUMENTED CAPABILITY LIMIT WITH A STATED REASON, and it is deliberate rather than an
//  oversight: it is NARROWER THAN THE LEGACY SIGNATURE ADMITS, and the behavioural oracle is what
//  would settle what the closed binary actually did.
//
//  WHAT IS EXPLICITLY REJECTED, because it is the tempting option and it is the worst one: DO NOT
//  SILENTLY FALL BACK TO AN UNKEYED CRC32. That would DISCARD THE CALLER'S KEY WITHOUT TELLING IT,
//  returning a plausible-looking eight-character string that any downstream comparison would
//  accept while providing no authentication whatever. A caller believing it had a keyed checksum
//  would have an unkeyed one. A defined error is strictly better than a silent wrong answer, and
//  that is the one outcome worse than the error. Fabricating a bespoke keyed-CRC construction is
//  rejected for the same reason plus one more: it would be an invention with no oracle behind it.
//
//  The unkeyed CRC32 arm remains fully available on HashProvider, which is where the legacy's own
//  CRC32 call site reaches it. Nothing is lost that the legacy demonstrably had.
//
//  ==============================================================================================
//  THE DIGEST OUTPUT ENCODING IS NOT A DECISION OF THIS FILE - IT IS DECISION D4
//  ==============================================================================================
//  This file does not choose how a digest becomes printable. It applies
//  `LegacyDefaults.STRING_PAYLOAD_ENCODING` through the injected encoding provider, which is
//  DECISION D4: Base64, uniform across hash digests, keyed digests, symmetric ciphertext, RSA
//  ciphertext and RSA signatures. LegacyDefaults is the single decision record, including the
//  honest statement that a lowercase hexadecimal digest is the more common convention for the MD5
//  and SHA families specifically and is the most plausible way that one uniform rule turns out to
//  be wrong for exactly those groups. Centralising it is what lets the oracle adjudicate it in one
//  place and, if it is wrong, correct it in one place.
//
//  NO SECOND ENCODING CONVENTION IS INTRODUCED HERE, and routing through the SAME injected
//  provider that HashProvider uses - rather than calling a conversion primitive directly - is what
//  makes "the keyed and unkeyed paths share one output convention" a shared CODE PATH instead of a
//  shared intention. The resulting widths, documented because callers size columns and buffers
//  from them, are standard padded single-line Base64 over the raw digest bytes:
//
//      arm             digest bytes    Base64 characters
//      -------------   ------------    -----------------------------------------
//      HMAC-MD5                  16    24, the last two of which are padding
//      HMAC-SHA1                 20    28, the last one of which is padding
//      HMAC-SHA256               32    44, the last one of which is padding
//      HMAC-SHA384               48    64, with no padding
//      HMAC-SHA512               64    88, the last two of which are padding
//      keyed CRC32                -    unreachable - see DECISION M3
//
//  A keyed digest is the same length as the unkeyed digest of the same algorithm, because HMAC
//  outputs one digest of the underlying hash. So the five rows above match HashProvider's five
//  cryptographic rows exactly, which is a useful sanity check on both files.
//
//  ==============================================================================================
//  MD5 AND SHA-1 ARE RETAINED, AND NO DIGEST COMPARISON HAPPENS HERE
//  ==============================================================================================
//  All five cryptographic arms stay. MD5 and SHA-1 are both broken for COLLISION resistance - MD5
//  catastrophically, SHA-1 practically - and neither may be removed, deprecated, gated behind a
//  flag or quietly redirected to a stronger algorithm: removing an arm would reject input the
//  legacy accepted, and redirecting one would change every authenticator the legacy ever produced.
//  Worth stating precisely, because it is a genuine difference from the unkeyed case: HMAC's
//  security does NOT rest on the collision resistance of its hash, so HMAC-MD5 and HMAC-SHA1 are
//  considerably less broken than bare MD5 and bare SHA-1. They are still not what a new design
//  should choose, and they are annotated at their point of reproduction rather than altered.
//
//  A note that belongs with MD5 specifically: on a host configured to enforce FIPS 140 the
//  platform may refuse to produce an MD5-based authenticator at all, surfacing as a cryptographic
//  or platform-support failure raised by the framework rather than by this type. That is an
//  ENVIRONMENT PROPERTY, not a licence to substitute a different algorithm. The failure propagates
//  unchanged; silently answering with an HMAC-SHA256 instead would be a behavioural change
//  disguised as robustness.
//
//  NO DIGEST COMPARISON IS PERFORMED ANYWHERE IN THIS FILE, and that is a property of the surface
//  rather than an omission: the legacy's keyed surface has no verify member - it produces
//  authenticators and never checks one - so there is nothing here to compare. Recorded because the
//  rule for anything added later is not optional: ANY comparison of two digests or two
//  authenticators must use the framework's FIXED-TIME comparison, never string or sequence
//  equality, because ordinary equality returns early on the first differing byte and so leaks the
//  length of a correct prefix through timing. That would be an unobservable implementation detail
//  chosen for correctness, not a behavioural change - the legacy could not have observed the
//  difference either way.
//
//  ==============================================================================================
//  TWO LEGACY IDIOMS WITH NO ANALOGUE TO REPRODUCE
//  ==============================================================================================
//  A reader comparing this file with the legacy will look for these and should find them absent by
//  design rather than by oversight.
//
//      * THE TYPE-SHADOWING GLOBAL. `global n_crypto n_crypto` [n_crypto.sru:L75] declares an auto
//        instance whose name collides with its own type name, which is legal only because
//        PowerBuilder resolves ONE FLAT GLOBAL NAMESPACE ordered by the target's library list and
//        offers no container at all.
//      * THE LAZY-CONSTRUCTION GUARD. Every wrapper object in the legacy crypto library opens with
//        a validity check that constructs that global on first use, because the instance it guards
//        is reachable by any caller before anything has constructed it.
//
//  In this port the equivalent object is a CONTAINER-MANAGED INSTANCE consumed by constructor
//  injection: never a global, never a static, never lazily self-constructing, and therefore with
//  nothing to check before use. There is nothing to port, only a collision to avoid.
//
//  CONCURRENCY. This type is stateless apart from the injected provider and is safe to call from
//  any number of requests at once. There is no mutable static state below of any kind, and - as
//  rule 2 above requires - NO KEYED ALGORITHM INSTANCE IS EVER CACHED IN A FIELD.
//
//  NAMING. The repository's naming-analyzer suppressions are scoped file by file and THIS FILE IS
//  NOT ONE OF THEM, so no SCREAMING_SNAKE identifier is declared anywhere below. Every `CRYPTO_*`
//  occurrence is an `Enums.`-qualified reference or documentation prose, and every member this
//  file introduces is PascalCase.
//
//  REGISTERS NO ROUTE. This type opens no listener and declares no endpoint; the operations below
//  sit behind the composition root's default-deny authorization policy. See the exposure note on
//  HashFile for the one decision that belongs to the endpoint author rather than here.
// ==============================================================================================

using System.Security.Cryptography;
using System.Text;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// Computes keyed message authenticators - HMAC - reproducing the legacy cryptographic library's
/// keyed hashing surface at ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26 for in-memory data
/// and :L28-L29 for a file.
/// </summary>
/// <remarks>
/// <para>
/// SIX PUBLIC MEMBERS EXACTLY: four <c>Hash</c> overloads forming the complete
/// <c>{string data, blob data}</c> by <c>{string key, blob key}</c> cross product, and two
/// <c>HashFile</c> overloads carrying only the key-type axis because a filename is always text.
/// The UNKEYED overloads of the same legacy surface - <c>Hash</c> at n_crypto.sru:L21-L22 and
/// <c>HashFile</c> at :L27 - are a different primitive with a different security contract and
/// belong to <see cref="HashProvider"/>. No member of this type omits a key, and none may be added
/// that does.
/// </para>
/// <para>
/// FIVE ALGORITHM ARMS ARE REACHABLE, not six. <see cref="Enums.CRYPTO_HASH_MD5"/> through
/// <see cref="Enums.CRYPTO_HASH_SHA512"/> all produce an authenticator;
/// <see cref="Enums.CRYPTO_HASH_CRC32"/> is a deliberate, documented capability limit because no
/// keyed CRC32 construction exists and a fabricated one would authenticate nothing. See DECISION
/// M3 in this file's header - the limit is narrower than the legacy signature admits, and it fails
/// loudly rather than discarding the caller's key.
/// </para>
/// <para>
/// HOLDS NO KEY MATERIAL. A key arrives only as a parameter, already resolved by the endpoint layer
/// from an opaque reference; this type has no configuration, no options, no environment read and no
/// file read through which a key could enter. Nothing is retained between calls, the keyed
/// algorithm is created and disposed inside a single call, and any copy of key bytes this type
/// makes for itself is wiped in a <c>finally</c>.
/// </para>
/// <para>
/// LOGS NOTHING. This type sees both the payload and the key, so anything it logged would be
/// exactly what must not be logged. There is deliberately no logger, and no exception message
/// interpolates key content.
/// </para>
/// <para>
/// RETURNS TEXT, NOT BYTES. Every member returns the authenticator already encoded, because all six
/// legacy declarations return a string and the oracle's own caller assigns the result straight into
/// a text control [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449]. The
/// encoding is not chosen here: it is <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>,
/// DECISION D4, applied through the same injected <see cref="EncodingProvider"/> that
/// <see cref="HashProvider"/> uses, so the keyed and unkeyed paths share one output convention as a
/// code path rather than as an intention.
/// </para>
/// <para>
/// ACCEPTS A KEY OF ANY LENGTH, and imposes no length rule of its own. HMAC zero-pads a key shorter
/// than the hash's block size and hashes one longer than it, as part of the construction itself, so
/// DECISION D1's truncate-or-pad rule - which the fixed-size cipher keys of
/// <see cref="SymmetricCipherProvider"/> need - deliberately does NOT apply here. No minimum length
/// is enforced, because enforcing one would reject input the legacy accepted. See DECISION M2.
/// </para>
/// <para>
/// STATELESS AND THREAD-SAFE. Apart from the injected provider there is no field, no cache and no
/// mutable static state, and no keyed algorithm instance is ever retained between calls, so one
/// instance serves any number of concurrent requests. It is registered in the service container by
/// the composition root and consumed by constructor injection; it is never reached through a global
/// or a static, which is what replaces the legacy's type-shadowing global auto instance
/// [n_crypto.sru:L75] and the lazy-construction guard that global required.
/// </para>
/// <para>
/// REGISTERS NO ROUTE. See the exposure note on <see cref="HashFile(string, byte[], long)"/> for
/// the one publication decision that belongs to the endpoint author rather than to this type.
/// </para>
/// </remarks>
public sealed class HmacProvider
{
    /// <summary>
    /// The single message text for the unsupported-algorithm failure, so that the one condition has
    /// one wording as well as one shape wherever it is reached from.
    /// </summary>
    /// <remarks>
    /// It describes the published set rather than defining it. The definition is
    /// <see cref="LegacyDefaults.IsSupportedHashType"/>, which is the only gate this type consults;
    /// this string must never become a second, drifting statement of what is allowed. The wording
    /// deliberately matches the equivalent message in <see cref="HashProvider"/>, because the two
    /// screens admit the same six identifiers and a caller comparing them should see one statement.
    /// </remarks>
    private const string UnsupportedHashTypeMessage =
        "Not a published hash type. The set is exactly MD5 (0), SHA1 (1), SHA256 (2), SHA384 (3), " +
        "SHA512 (4) and CRC32 (5) [ws_objects/pfw.shared.pbl.src/enums.sru:L928-L933].";

    /// <summary>
    /// The single message text for the keyed CRC32 failure of DECISION M3, distinct from
    /// <see cref="UnsupportedHashTypeMessage"/> so that the two conditions are tellable apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CRC32 IS a published hash type, so the unsupported-type wording would be actively misleading
    /// here: the identifier is legal, the KEYED COMBINATION is not. The message therefore states the
    /// reason and names the alternative that does exist, so a caller is never left guessing whether
    /// it passed a bad number.
    /// </para>
    /// <para>
    /// It names no key, no length and no payload - see the key-handling rules in this file's header.
    /// </para>
    /// </remarks>
    private const string KeyedChecksumUnsupportedMessage =
        "CRC32 (5) is a published hash type but has no keyed form. HMAC is defined over an " +
        "iterated cryptographic hash; CRC32 is a linear checksum, so a keyed construction over it " +
        "is forgeable from the algebra and would authenticate nothing. The legacy behaviour for " +
        "this combination cannot be read from the repository - no call site exercises it - so it " +
        "is a deliberate, documented capability limit rather than an oversight, and it is narrower " +
        "than the legacy signature admits. Use MD5 (0), SHA1 (1), SHA256 (2), SHA384 (3) or " +
        "SHA512 (4) for a keyed digest, or the unkeyed CRC32 surface for a checksum.";

    /// <summary>
    /// The buffer length used when authenticating a file, in bytes.
    /// </summary>
    /// <remarks>
    /// 80 KiB, matching <see cref="HashProvider"/> exactly so that the keyed and unkeyed file paths
    /// read a file identically. It is the buffer length the framework itself uses for stream
    /// copying, so it is a conventional value rather than a tuned one, and NO PERFORMANCE CLAIM IS
    /// ATTACHED TO IT: the requirement it satisfies is bounded memory, not throughput. It exists
    /// because <see cref="HashFile(string, byte[], long)"/> must STREAM - the file the oracle's own
    /// demo digests is a multi-megabyte native binary, and reading a caller-named file whole into
    /// memory would make its size a memory-exhaustion lever.
    /// </remarks>
    private const int FileReadBufferLengthBytes = 81920;

    /// <summary>
    /// The injected encoding provider through which every authenticator is rendered printable.
    /// </summary>
    /// <remarks>
    /// This is the ONLY field on the type, and it holds no state of its own. Injected rather than
    /// constructed so that the output encoding of this type, of <see cref="HashProvider"/>, of
    /// <see cref="SymmetricCipherProvider"/> and of <see cref="RsaProvider"/> is demonstrably the
    /// same code path and not four similar ones. NO KEY MATERIAL IS HELD IN THIS OR ANY OTHER FIELD.
    /// </remarks>
    private readonly EncodingProvider _encoding;

    /// <summary>
    /// Initializes a new instance of the <see cref="HmacProvider"/> class.
    /// </summary>
    /// <param name="encoding">
    /// The provider that renders a raw authenticator printable. Supplied by the container.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// TAKES NO KEY, NO CONFIGURATION AND NO OPTIONS, and that is a contract rather than a
    /// simplification: a key must arrive on a call, already resolved by the endpoint layer from an
    /// opaque reference, so that this type cannot obtain one from application settings, an
    /// environment variable or a file even by accident.
    /// </para>
    /// <para>
    /// This constructor is also the replacement for the legacy's lazy-construction guard and for the
    /// type-shadowing global auto instance it guards [n_crypto.sru:L75]. A container-managed
    /// instance has nothing to check before use, so neither idiom has an analogue to reproduce -
    /// see this file's header.
    /// </para>
    /// </remarks>
    public HmacProvider(EncodingProvider encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        _encoding = encoding;
    }

    /// <summary>
    /// The encoding used to turn <c>string</c> DATA into the bytes that are authenticated. This is
    /// DECISION H1, BORROWED from <see cref="HashProvider.InputTextEncoding"/> rather than restated.
    /// </summary>
    /// <value>The same encoding instance the unkeyed hashing path uses for its own string data.</value>
    /// <remarks>
    /// <para>
    /// Borrowing is the whole point, and it is why this member forwards instead of holding a value:
    /// it guarantees a caller cannot get one answer from the unkeyed <c>Hash(data, ntype)</c> and a
    /// differently-encoded one from the keyed <c>Hash(data, key, ntype)</c> over the same text. A
    /// local copy of the decision could drift; a forward cannot.
    /// </para>
    /// <para>
    /// It is <see langword="internal"/> for the same reason its source is: the parity marker test
    /// NAMES this decision instead of duplicating it, so that when the behavioural oracle settles
    /// the text-encoding question there is one member to change and one test to re-baseline. See
    /// DECISION M1 in this file's header.
    /// </para>
    /// </remarks>
    internal static Encoding DataTextEncoding => HashProvider.InputTextEncoding;

    /// <summary>
    /// The encoding used to turn a <c>string</c> KEY into key bytes. This is DECISION D1's encoding
    /// half, borrowed from <see cref="LegacyDefaults.KeyMaterialEncoding"/> rather than restated.
    /// </summary>
    /// <value>The same encoding instance the symmetric cipher surface uses for a string key.</value>
    /// <remarks>
    /// <para>
    /// Key material is key material wherever it enters this folder, so the key axis reads the
    /// KEY-MATERIAL decision rather than the payload one. Only D1's ENCODING half is used here;
    /// its truncate-or-pad LENGTH half is deliberately not applied, because HMAC has no fixed key
    /// length - see DECISION M2.
    /// </para>
    /// <para>
    /// This encoding and <see cref="DataTextEncoding"/> resolve to the same encoding today, and a
    /// test ASSERTS THAT THEY AGREE rather than assuming it, so that re-baselining either decision
    /// against the oracle surfaces as a build-visible failure instead of a silent parity fork.
    /// </para>
    /// <para>
    /// Its byte conversion never emits a byte-order mark - a mark belongs to a stream preamble,
    /// which conversion does not consult - so no phantom leading bytes can enter key material and
    /// silently change a key.
    /// </para>
    /// </remarks>
    internal static Encoding KeyTextEncoding => LegacyDefaults.KeyMaterialEncoding;

    // ==========================================================================================
    //  THE FOUR KEYED `Hash` OVERLOADS - THE COMPLETE data-BY-key CROSS PRODUCT
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23, :L24, :L25, :L26
    //  ------------------------------------------------------------------------------------------
    //  Presented in LEGACY DECLARATION ORDER so the file reads against the oracle line by line.
    //  All four are spellings of ONE BEHAVIOUR, not four behaviours: each converts whichever of its
    //  two axes is text through the borrowed encoding decision for that axis and then delegates to
    //  the single dispatch below, so no overload can drift from another. A test proves that for an
    //  ASCII data-and-key pair all four agree.
    //
    //  OVERLOAD RESOLUTION HAS ONE SHARP EDGE, worth stating because it is the only way a caller
    //  can be surprised. The ARGUMENT TYPES select the cell, and every real call site passes typed
    //  material - so text resolves to a text axis and a byte array to a byte axis, unambiguously.
    //  A BARE `null` LITERAL on an axis is the exception: it is applicable to both the string and
    //  the byte-array overload of that axis, so it does not compile. Pass a typed null instead,
    //  which is what a caller probing the null guard means anyway.
    //
    //  A CALLER'S BYTE ARRAY IS NEVER MUTATED. The two blob-key overloads pass the caller's array
    //  through as a read-only span; only the two string-key overloads make a copy, and only those
    //  copies are wiped.
    // ==========================================================================================
    #region Keyed Hash - n_crypto.sru:L23-L26

    /// <summary>
    /// Computes the keyed authenticator of text under a text key and returns it in printable form.
    /// </summary>
    /// <param name="data">
    /// The text to authenticate. An EMPTY string is valid and yields the authenticator of zero bytes
    /// under the given key, which is a fixed, non-empty result. Converted to bytes through
    /// <see cref="DataTextEncoding"/>.
    /// </param>
    /// <param name="key">
    /// The key, as text. Used AS RAW KEY BYTES through <see cref="KeyTextEncoding"/> with NO KEY
    /// DERIVATION of any kind - see DECISION M2. ANY LENGTH IS ACCEPTED, empty included, because
    /// HMAC normalizes the key itself and the legacy declares no length precondition.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>: standard
    /// padded single-line Base64 over the raw bytes. See this file's header for the width each
    /// algorithm produces.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator. Reachable in practice for
    /// <see cref="Enums.CRYPTO_HASH_MD5"/> on a host enforcing FIPS 140; see this file's header for
    /// why that failure is allowed to propagate rather than being answered with another algorithm.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string Hash(readonly string data, readonly string key,
    /// readonly long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23], whose
    /// implementation lives in the closed native binary and cannot be read. This is the shape the
    /// oracle's own demo exercises, with <see cref="Enums.CRYPTO_HASH_SHA256"/>
    /// [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449].
    /// </para>
    /// <para>
    /// THIS OVERLOAD MAKES THE ONLY COPY OF KEY MATERIAL IN THE PAIR IT BELONGS TO, so that copy is
    /// zeroed in a <c>finally</c> - on the failure paths as well as the success path. The caller's
    /// own string is immutable and is not touched.
    /// </para>
    /// </remarks>
    public string Hash(string data, string key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        // Screen the algorithm BEFORE the key is materialised, so an unsupported identifier cannot
        // cause key bytes to exist in memory at all.
        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        byte[] keyBytes = KeyTextEncoding.GetBytes(key);

        try
        {
            return EncodeDigest(ComputeKeyedDigest(DataTextEncoding.GetBytes(data), keyBytes, algorithm));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    /// <summary>
    /// Computes the keyed authenticator of text under a binary key and returns it in printable form.
    /// </summary>
    /// <param name="data">
    /// The text to authenticate. An EMPTY string is valid. Converted to bytes through
    /// <see cref="DataTextEncoding"/>.
    /// </param>
    /// <param name="key">
    /// The key bytes, used exactly as supplied with NO DERIVATION and NO LENGTH ADJUSTMENT - see
    /// DECISION M2. An EMPTY array is accepted. THE ARRAY IS READ AND NEVER MODIFIED: it belongs to
    /// the caller, so this type does not wipe it, because doing so would be an observable side
    /// effect the legacy never had.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function string Hash(readonly string data, readonly blob key, readonly
    /// long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L24]. Blob-shaped key support was
    /// a later addition to the legacy surface, which is why the cross product exists at all rather
    /// than a single text-keyed form.
    /// </remarks>
    public string Hash(string data, byte[] key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        // No copy of the key is made, so there is nothing for this overload to wipe.
        return EncodeDigest(ComputeKeyedDigest(DataTextEncoding.GetBytes(data), key, algorithm));
    }

    /// <summary>
    /// Computes the keyed authenticator of binary data under a text key and returns it in printable
    /// form.
    /// </summary>
    /// <param name="data">
    /// The bytes to authenticate. An EMPTY array is valid. NO CHARACTER ENCODING IS APPLIED to this
    /// axis - the caller has already decided what its bytes are - which is the distinction from the
    /// text-data overloads and the reason DECISION M1's data half does not reach here.
    /// </param>
    /// <param name="key">
    /// The key, as text. Used AS RAW KEY BYTES through <see cref="KeyTextEncoding"/> with NO KEY
    /// DERIVATION - see DECISION M2. ANY LENGTH IS ACCEPTED, empty included.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function string Hash(readonly blob data, readonly string key, readonly
    /// long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L25]. The encoded key copy this
    /// overload makes is wiped in a <c>finally</c>; the caller's data array is read and never
    /// modified.
    /// </remarks>
    public string Hash(byte[] data, string key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        byte[] keyBytes = KeyTextEncoding.GetBytes(key);

        try
        {
            return EncodeDigest(ComputeKeyedDigest(data, keyBytes, algorithm));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    /// <summary>
    /// Computes the keyed authenticator of binary data under a binary key and returns it in printable
    /// form.
    /// </summary>
    /// <param name="data">
    /// The bytes to authenticate. An EMPTY array is valid. No character encoding is applied.
    /// </param>
    /// <param name="key">
    /// The key bytes, used exactly as supplied with NO DERIVATION and NO LENGTH ADJUSTMENT - see
    /// DECISION M2. An EMPTY array is accepted. THE ARRAY IS READ AND NEVER MODIFIED.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="data"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string Hash(readonly blob data, readonly blob key, readonly
    /// long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L26].
    /// </para>
    /// <para>
    /// THIS IS THE ENCODING-FREE CELL of the cross product, and therefore the one against which the
    /// published known-answer vectors are asserted directly: both axes are bytes, so no text
    /// decision stands between a caller's input and the computation. The other three cells reduce to
    /// it once their text axes have been converted, which is what makes the four-cell agreement test
    /// meaningful rather than circular.
    /// </para>
    /// </remarks>
    public string Hash(byte[] data, byte[] key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(key);

        // Hoisted rather than inlined into the argument list purely so that the screen-then-compute
        // ordering reads identically in all four cells; no key copy exists in this overload either
        // way.
        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        return EncodeDigest(ComputeKeyedDigest(data, key, algorithm));
    }

    #endregion

    // ==========================================================================================
    //  THE TWO KEYED `HashFile` OVERLOADS - THE KEY-TYPE AXIS ONLY
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L28, :L29
    //  ------------------------------------------------------------------------------------------
    //  There are two rather than four because `filename` is ALWAYS a string in the legacy: no
    //  blob-filename form is declared and none is invented. Both stream the file in bounded memory
    //  and both open it identically, through the one helper below, so the keyed file path cannot
    //  diverge from itself - or, since it uses the same options and buffer length, from the unkeyed
    //  file path on HashProvider.
    //
    //  ORDERING IS CONTRACT, NOT INCIDENT. In both overloads the null guards run, then the algorithm
    //  is screened, and only then is the filesystem touched. So an unsupported or unkeyable
    //  identifier fails WITHOUT the process having opened - or revealed the existence of - any path,
    //  and without key bytes having been materialised.
    // ==========================================================================================
    #region Keyed HashFile - n_crypto.sru:L28-L29

    /// <summary>
    /// Computes the keyed authenticator of a file's contents under a text key and returns it in
    /// printable form.
    /// </summary>
    /// <param name="filename">
    /// The path of the file to authenticate. Read from its beginning to its end; a ZERO-LENGTH FILE
    /// is valid and yields the authenticator of zero bytes under the given key.
    /// </param>
    /// <param name="key">
    /// The key, as text. Used AS RAW KEY BYTES through <see cref="KeyTextEncoding"/> with NO KEY
    /// DERIVATION - see DECISION M2. ANY LENGTH IS ACCEPTED, empty included.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>, equal to
    /// the result of the corresponding <c>Hash</c> overload over the same bytes and the same key.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filename"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is empty or malformed.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="filename"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Part of the path does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The path is a directory, or the process may not read it.
    /// </exception>
    /// <exception cref="IOException">
    /// The file could not be opened or read - for example because another process holds it open for
    /// writing.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string HashFile(readonly string filename, readonly string key,
    /// readonly long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L28].
    /// </para>
    /// <para>
    /// FAILURE SHAPE. What the closed binary returned for a missing or unreadable file is
    /// unobservable, so the framework's own file exception is allowed to PROPAGATE UNCHANGED rather
    /// than a sentinel return value being invented. A defined error is preferable to a guessed one,
    /// and an empty string would be indistinguishable from a legitimate result of some future
    /// encoding rule. The encoded key copy is wiped in a <c>finally</c>, so it does not survive a
    /// file-open or read failure.
    /// </para>
    /// <para>
    /// EXPOSURE CONSIDERATION FOR THE ENDPOINT AUTHOR, carried forward from the unkeyed file member
    /// because it applies identically here. The published cryptographic service contract enumerates
    /// keyed HMAC but DOES NOT ENUMERATE A FILE-HASH OPERATION, and a caller-supplied filesystem path
    /// reachable from a network endpoint is an arbitrary-file-read primitive: a caller could confirm
    /// the existence of a path, and could authenticate a file it cannot otherwise read. An in-process
    /// library called by its own application could never have been that. This type registers no
    /// route and opens no listener, so it creates no such exposure by itself; publishing this member
    /// is a decision to be taken knowingly at the endpoint layer, where it can be expressed as an
    /// authorization and allow-list concern, and the safe default is not to publish it. NO PATH
    /// VALIDATION IS ADDED HERE, because a path filter would change observable behaviour for the
    /// in-process callers the legacy had.
    /// </para>
    /// </remarks>
    public string HashFile(string filename, string key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(key);

        // Screened before the filesystem is touched and before key bytes exist - see the region
        // banner above; the ordering is part of the contract rather than incidental.
        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        byte[] keyBytes = KeyTextEncoding.GetBytes(key);

        try
        {
            using FileStream source = OpenForSequentialRead(filename);

            return EncodeDigest(ComputeKeyedDigest(source, keyBytes, algorithm));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    /// <summary>
    /// Computes the keyed authenticator of a file's contents under a binary key and returns it in
    /// printable form.
    /// </summary>
    /// <param name="filename">
    /// The path of the file to authenticate. A ZERO-LENGTH FILE is valid.
    /// </param>
    /// <param name="key">
    /// The key bytes, used exactly as supplied with NO DERIVATION and NO LENGTH ADJUSTMENT - see
    /// DECISION M2. An EMPTY array is accepted. THE ARRAY IS READ AND NEVER MODIFIED.
    /// </param>
    /// <param name="ntype">
    /// One of the five keyed algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_SHA512"/>.
    /// </param>
    /// <returns>
    /// The authenticator encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>, equal to
    /// the result of <see cref="Hash(byte[], byte[], long)"/> over the same bytes and the same key.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filename"/> or <paramref name="key"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not a published hash type, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form - see DECISION M3.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="filename"/> is empty or malformed.</exception>
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="filename"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Part of the path does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The path is a directory, or the process may not read it.
    /// </exception>
    /// <exception cref="IOException">The file could not be opened or read.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>public function string HashFile(readonly string filename, readonly blob key,
    /// readonly long ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L29]. The failure shape
    /// and the endpoint exposure consideration are identical to
    /// <see cref="HashFile(string, string, long)"/> and are documented there; this overload
    /// additionally makes no copy of key material, so it has nothing to wipe.
    /// </remarks>
    public string HashFile(string filename, byte[] key, long ntype)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(key);

        HashAlgorithmName algorithm = ResolveKeyedAlgorithm(ntype);

        using FileStream source = OpenForSequentialRead(filename);

        return EncodeDigest(ComputeKeyedDigest(source, key, algorithm));
    }

    #endregion

    // ==========================================================================================
    //  PRIVATE SEAMS - ONE OF EACH, WHICH IS THE POINT
    //  ------------------------------------------------------------------------------------------
    //      ResolveKeyedAlgorithm   the ONLY validation and the ONLY algorithm selection
    //      ComputeKeyedDigest      two shapes, in-memory and streamed, both keyed identically
    //      OpenForSequentialRead   the ONLY place a file is opened
    //      EncodeDigest            the ONLY output-encoding point, DECISION D4
    //
    //  All six public members funnel through these, so the accepted set, the CRC32 ruling, the key
    //  handling and the output convention are consistent across the whole surface by construction
    //  rather than by six careful copies. This mirrors HashProvider's shape deliberately, which is
    //  what keeps the keyed and unkeyed halves of one legacy surface aligned.
    //
    //  NONE OF THESE RETAINS KEY MATERIAL. The key travels as a read-only span, is consumed by a
    //  keyed algorithm object created and disposed inside a single call, and is never copied into a
    //  field, a static or a cache.
    // ==========================================================================================
    #region Private seams

    /// <summary>
    /// Validates <paramref name="ntype"/> against the published set, applies DECISION M3's keyed
    /// CRC32 ruling, and selects the algorithm arm. This is THE single dispatch of this type.
    /// </summary>
    /// <param name="ntype">The candidate algorithm identifier.</param>
    /// <returns>The framework algorithm name for one of the five keyed arms.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the six published identifiers, or is
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>, which has no keyed form.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Validation and selection are ONE operation deliberately. Splitting them would allow a call
    /// path that selects without validating, and the selection below treats its final arm as SHA-512
    /// precisely because the two screens have already narrowed the input to exactly five values - so
    /// an unscreened value would silently become a SHA-512 authenticator instead of failing.
    /// </para>
    /// <para>
    /// The first screen delegates to <see cref="LegacyDefaults.IsSupportedHashType"/> and never to a
    /// local allowed-set, because that predicate is shared with the unkeyed hash provider and with
    /// the RSA signature surface [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and three divergent
    /// copies of one set is how the three come to disagree.
    /// </para>
    /// <para>
    /// THE SECOND SCREEN IS THIS FILE'S OWN NARROWING, and it is the only place in the folder where a
    /// keyed surface accepts fewer identifiers than the predicate admits. It returns THE SAME FAILURE
    /// SHAPE as the first - the same exception type naming the same parameter - with distinct wording,
    /// so a caller can tell "that number is not a hash type" from "that hash type has no keyed form".
    /// It NEVER falls back to an unkeyed checksum: doing so would discard the caller's key silently,
    /// which is the one outcome worse than an error. The full reasoning is DECISION M3 in this file's
    /// header.
    /// </para>
    /// <para>
    /// ORDER MATTERS BETWEEN THE TWO SCREENS. The published-set screen runs first so that an
    /// out-of-range identifier gets the general message rather than the CRC32-specific one, which
    /// would be actively misleading.
    /// </para>
    /// </remarks>
    private static HashAlgorithmName ResolveKeyedAlgorithm(long ntype)
    {
        if (!LegacyDefaults.IsSupportedHashType(ntype))
        {
            throw new ArgumentOutOfRangeException(nameof(ntype), ntype, UnsupportedHashTypeMessage);
        }

        if (ntype == Enums.CRYPTO_HASH_CRC32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ntype),
                ntype,
                KeyedChecksumUnsupportedMessage);
        }

        return ntype switch
        {
            Enums.CRYPTO_HASH_MD5 => HashAlgorithmName.MD5,
            Enums.CRYPTO_HASH_SHA1 => HashAlgorithmName.SHA1,
            Enums.CRYPTO_HASH_SHA256 => HashAlgorithmName.SHA256,
            Enums.CRYPTO_HASH_SHA384 => HashAlgorithmName.SHA384,

            // The two screens above admit exactly five values and four are named, so this arm is
            // reachable ONLY for Enums.CRYPTO_HASH_SHA512. It is written as the catch-all rather
            // than as a fifth named arm because a switch expression over `long` requires a total set
            // of arms, and a named SHA-512 arm plus an unreachable catch-all would be dead code that
            // no test could cover and that the coverage gate would count against this file.
            _ => HashAlgorithmName.SHA512,
        };
    }

    /// <summary>
    /// Computes a raw keyed authenticator over data already held in memory.
    /// </summary>
    /// <param name="data">The bytes to authenticate, possibly empty.</param>
    /// <param name="key">
    /// The key bytes, possibly empty. Consumed as a read-only span, so nothing is copied and the
    /// caller's buffer - if the span came from one - is neither modified nor retained.
    /// </param>
    /// <param name="algorithm">The arm chosen by <see cref="ResolveKeyedAlgorithm"/>.</param>
    /// <returns>The raw authenticator bytes, not yet encoded.</returns>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE KEYED ALGORITHM OBJECT IS CREATED, USED AND DISPOSED INSIDE THIS CALL and is NEVER cached
    /// in a field. That serves both rules at once: it is the thread-safety requirement, because such
    /// objects carry mutable digest state and are not safe to share - a cached instance would corrupt
    /// results under concurrency in a way that looks like a hashing defect - and it is the
    /// no-retention requirement, because the object holds a derived copy of the key for as long as it
    /// lives, and disposal is what releases it.
    /// </para>
    /// <para>
    /// The framework's keyed factory performs HMAC's own key normalization, which is why DECISION M2
    /// forbids applying a length rule here: a short key is zero-padded to the hash's block size and a
    /// long one is hashed down first, as the construction defines.
    /// </para>
    /// </remarks>
    private static byte[] ComputeKeyedDigest(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> key,
        HashAlgorithmName algorithm)
    {
        using IncrementalHash hash = IncrementalHash.CreateHMAC(algorithm, key);

        hash.AppendData(data);

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Computes a raw keyed authenticator by reading a stream to its end in bounded-size chunks.
    /// </summary>
    /// <param name="source">The stream to read, from its current position to its end.</param>
    /// <param name="key">The key bytes, possibly empty, consumed as a read-only span.</param>
    /// <param name="algorithm">The arm chosen by <see cref="ResolveKeyedAlgorithm"/>.</param>
    /// <returns>The raw authenticator bytes, not yet encoded.</returns>
    /// <exception cref="IOException">The stream could not be read.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested authenticator.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the shape that makes the file members safe over a file of arbitrary size: memory use
    /// is one buffer of <see cref="FileReadBufferLengthBytes"/> plus the authenticator, independent
    /// of the input's length. A ZERO-LENGTH STREAM is valid and yields the authenticator of zero
    /// bytes, because the loop body never executes.
    /// </para>
    /// <para>
    /// It is read SYNCHRONOUSLY and deliberately so. The legacy surface is synchronous - all six
    /// declarations return a value rather than signalling completion - and introducing an
    /// asynchronous variant would add public surface the legacy never had and break the six-member
    /// census. The endpoint layer, which owns the request thread, is where an asynchronous boundary
    /// belongs if one is ever wanted.
    /// </para>
    /// <para>
    /// The read buffer holds PAYLOAD, not key material, so it is not wiped: the payload is the
    /// caller's own file content, and wiping a buffer that never held a key would be theatre rather
    /// than hygiene.
    /// </para>
    /// </remarks>
    private static byte[] ComputeKeyedDigest(
        Stream source,
        ReadOnlySpan<byte> key,
        HashAlgorithmName algorithm)
    {
        using IncrementalHash hash = IncrementalHash.CreateHMAC(algorithm, key);

        byte[] buffer = new byte[FileReadBufferLengthBytes];
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Opens a file for sequential reading. This is THE single place this type touches the
    /// filesystem.
    /// </summary>
    /// <param name="filename">The path to open.</param>
    /// <returns>An open, readable stream positioned at the beginning of the file.</returns>
    /// <remarks>
    /// <para>
    /// Every option here matches the unkeyed file member on <see cref="HashProvider"/> exactly, so
    /// that the keyed and unkeyed file paths read a file the same way and a difference in result
    /// can never be attributed to how it was opened. The access pattern is stated to the operating
    /// system, and the buffer length is shared with the read loop so the stream does not buffer a
    /// second time underneath it.
    /// </para>
    /// <para>
    /// The share mode is READ, so another process holding the file open for reading does not block
    /// this call while one holding it for writing does. NO PATH VALIDATION, normalization or
    /// allow-listing happens here - see the exposure note on
    /// <see cref="HashFile(string, string, long)"/>: filtering paths would change observable
    /// behaviour for the in-process callers the legacy had, and the decision to expose this
    /// capability at all belongs to the endpoint layer.
    /// </para>
    /// </remarks>
    private static FileStream OpenForSequentialRead(string filename) =>
        new(
            filename,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileReadBufferLengthBytes,
            FileOptions.SequentialScan);

    /// <summary>
    /// Renders a raw authenticator printable. This is THE single output-encoding point of this type.
    /// </summary>
    /// <param name="digest">The raw authenticator bytes.</param>
    /// <returns>The encoded authenticator.</returns>
    /// <remarks>
    /// The encoding is not chosen here and must never be chosen here: it is
    /// <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>, DECISION D4, so that unkeyed digests,
    /// keyed authenticators, symmetric ciphertext, RSA ciphertext and RSA signatures all carry one
    /// convention and the behavioural oracle can adjudicate that convention in one place. Routing
    /// through the injected <see cref="EncodingProvider"/> rather than calling a conversion primitive
    /// directly is what makes "one convention" a shared code path instead of a shared intention -
    /// and it is the same injected instance <see cref="HashProvider"/> uses, which is what keeps the
    /// keyed and unkeyed halves of one legacy surface from drifting apart.
    /// </remarks>
    private string EncodeDigest(byte[] digest) =>
        _encoding.BlobToString(digest, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    #endregion
}
