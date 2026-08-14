// ==============================================================================================
//  HashProvider - the UNKEYED half of the legacy cryptographic hashing surface
//  --------------------------------------------------------------------------------------------
//  ORACLE         ws_objects/pfw.crypto.pbl.src/n_crypto.sru
//                 86 lines carrying exactly 65 `public function` declarations at L9-L73 and NO
//                 IMPLEMENTATION BODY ANYWHERE IN THE REPOSITORY. L8 declares
//                 `global type n_crypto from nonvisualobject native "pfw.dll"`, so every one of
//                 those 65 behaviours lives inside a closed native binary for which no C++ source
//                 exists here or anywhere else it can be read from.
//
//  WRAPPERS       ws_objects/pfw.crypto.pbl.src/md5.srf
//                 ws_objects/pfw.crypto.pbl.src/sha1.srf
//                 ws_objects/pfw.crypto.pbl.src/sha256.srf
//                 Three global function objects, TWO OVERLOADS EACH, all six returning `string`
//                 and all six delegating straight into the native `Hash` with a fixed algorithm
//                 identifier. They are the only .srf files that reach the unkeyed hash surface.
//
//  CONSTANTS      ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933
//                 The six hash-type constants, inside the `/*--- Crypto ---*/` block that opens at
//                 L921 and closes at L969. A stray duplicate `/*--- End Crypto ---*/` marker
//                 appears far below at L999 and is a trap: the authoritative block ends at L969.
//
//  CALL SITES     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L281, :L404, :L489
//                 The behavioural evidence quoted throughout this header.
//
//  READ ONLY      Every ws_objects/** path named in this file is READ ONLY. It is the behavioural
//                 oracle for parity testing, never an edit target. Locators are provenance for a
//                 decision and nothing more: no legacy file is edited, moved, reformatted or
//                 re-exported by this port. That applies to the demo objects as much as to the
//                 framework ones, including the demo objects that carry hardcoded credentials.
//
//  ==============================================================================================
//  THE CENSUS THIS FILE SATISFIES: 3 + 6 = 9 PUBLIC MEMBERS
//  ==============================================================================================
//  Because the DLL is closed, every member below is a SUBSTITUTION against
//  System.Security.Cryptography rather than a translation of legacy code. There is no legacy body
//  to port; there are only declarations to satisfy and observed behaviour to preserve.
//
//      legacy declaration                                            locator           C# member
//      ----------------------------------------------------------    --------------    ----------
//      public function string Hash(readonly string data,             n_crypto.sru      Hash
//                                 readonly long ntype)               :L21              (string)
//      public function string Hash(readonly blob data,               n_crypto.sru      Hash
//                                 readonly long ntype)               :L22              (byte[])
//      public function string HashFile(readonly string filename,      n_crypto.sru      HashFile
//                                     readonly long ntype)           :L27
//                                                                                      ---- 3 ----
//      global function string md5(readonly blob data)                md5.srf:L11-L13   Md5(byte[])
//      global function string md5(readonly string str)               md5.srf:L15-L17   Md5(string)
//      global function string sha1(readonly blob data)               sha1.srf:L11-L13  Sha1(byte[])
//      global function string sha1(readonly string str)              sha1.srf:L15-L17  Sha1(string)
//      global function string sha256(readonly blob data)             sha256.srf:L11-13 Sha256(byte[])
//      global function string sha256(readonly string str)            sha256.srf:L15-17 Sha256(string)
//                                                                                      ---- 6 ----
//                                                                                      TOTAL   9
//
//  This is a HARD CENSUS, not a lower bound. LegacyDefaults' folder-wide reconciliation assigns
//  HashProvider exactly 3 of the 65 declarations, and the six providers together account for
//  3 + 5 + 3 + 6 + 32 + 14 = 63 ported plus 2 deliberately non-ported. A tenth public member here
//  - a convenience overload, a Try-variant, a byte[]-returning shortcut or a promoted helper -
//  would silently break that arithmetic, so the count is asserted by a unit test rather than left
//  to inspection.
//
//  THE SIX WRAPPERS DELEGATE; THEY DO NOT DUPLICATE. Each legacy wrapper body is one line:
//  `return n_crypto.Hash(data, Enums.CRYPTO_HASH_MD5)` [md5.srf:L12] and its five siblings. Each
//  C# wrapper is therefore a one-line call into this type's own `Hash` with the corresponding
//  `Enums.CRYPTO_HASH_*` value. Re-implementing the dispatch inside a wrapper is precisely how a
//  wrapper and its dispatcher come to disagree, so none of the six touches an algorithm directly.
//
//  ==============================================================================================
//  WHAT IS DELIBERATELY ABSENT: THE ENTIRE KEYED HALF
//  ==============================================================================================
//  n_crypto.sru declares SIX `Hash` overloads and THREE `HashFile` overloads. This file owns only
//  the unkeyed ones. The split is total and non-overlapping, and it is a boundary rather than a
//  convenience:
//
//      :L21-L22    Hash, unkeyed, 2 overloads          THIS FILE
//      :L23-L26    Hash, keyed, 4 overloads            HmacProvider - the {string,blob} data by
//                                                      {string,blob} key cross product
//      :L27        HashFile, unkeyed, 1 overload       THIS FILE
//      :L28-L29    HashFile, keyed, 2 overloads        HmacProvider
//
//  NO MEMBER OF THIS FILE ACCEPTS A KEY, AND NONE MAY EVER BE ADDED. That is not tidiness. A key
//  parameter here would put key material into a type that has no key-handling contract, no key
//  normalization rule and no reason to exist near one; it would also make the keyed and unkeyed
//  surfaces indistinguishable to a caller who reads only the member list. HMAC is a different
//  primitive with a different security contract, and it lives with its own normalization rule in
//  its own file.
//
//  Following from that, THIS FILE HOLDS NO KEY MATERIAL OF ANY KIND - no key, no initialization
//  vector, no passphrase, no certificate, no PEM block and no credential, whether as a value, a
//  default argument, sample data or an example in documentation. Nothing from any of the
//  repository's known hardcoded-secret sites is reproduced here in any form. In particular the
//  hardcoded keyed-hash key in the oracle's own demo object is HmacProvider's reference reading
//  and is deliberately not quoted, paraphrased or partially transcribed anywhere in this file.
//
//  IT ALSO LOGS NOTHING, and the absence of a logger is deliberate rather than an omission. This
//  type sees whatever a caller is hashing - a password, a document, a token - so anything it
//  logged would be exactly the payload that must not be logged. There is no ILogger field, no
//  ILogger constructor parameter and no diagnostic write anywhere below.
//
//  ==============================================================================================
//  THE SIX ALGORITHM ARMS, AND WHY ALL SIX STAY
//  ==============================================================================================
//  The published set is exactly six values [enums.sru:L928-L933]: MD5 = 0, SHA1 = 1, SHA256 = 2,
//  SHA384 = 3, SHA512 = 4, CRC32 = 5. The source comment one line above them, at L927, records
//  that this same set is the algorithm parameter for `Hash`, `RSASign` AND `VerifyRSASign` alike,
//  which is why the RSA provider screens against the same predicate rather than growing its own.
//
//  Validation goes through `LegacyDefaults.IsSupportedHashType` and NEVER through a local
//  allowed-set. Six providers each maintaining their own copy of one set is exactly how the six
//  come to disagree, and a divergence would show up as an algorithm this provider accepts and the
//  signature provider rejects, or the reverse.
//
//  MD5 AND SHA-1 ARE RETAINED. Both are broken for collision resistance - MD5 catastrophically,
//  SHA-1 practically - and neither may be removed, deprecated, gated behind a flag or quietly
//  redirected to a stronger algorithm. Removing an arm would reject input the legacy accepted, and
//  redirecting one would change every digest the legacy ever produced, so any value stored under
//  the old digest would stop matching. Both are annotated at their point of reproduction instead.
//
//  A note that belongs with MD5 specifically: on a host configured to enforce FIPS 140, the
//  platform may refuse to produce an MD5 digest at all, surfacing as a cryptographic or
//  platform-support failure from the framework rather than from this type. That is an ENVIRONMENT
//  PROPERTY, not a licence to substitute a different algorithm. The failure is allowed to
//  propagate unchanged; silently answering with a SHA-256 digest instead would be a behavioural
//  change disguised as robustness.
//
//  ==============================================================================================
//  DECISION H1 - THE STRING-TO-BYTES STEP IS UTF-8. THIS IS THE FILE'S HIGHEST-VALUE ORACLE
//                QUESTION.
//  ==============================================================================================
//  `Hash(readonly string data, readonly long ntype)` [n_crypto.sru:L21] must turn text into bytes
//  before it can digest anything, and it does so INSIDE THE CLOSED BINARY. The choice is therefore
//  invisible from the repository, and it is not cosmetic: a UTF-8 choice and an ANSI choice produce
//  DIFFERENT DIGESTS for any input containing a non-ASCII character.
//
//  The evidence is pointed but not conclusive, and it cuts both ways:
//
//      * Where the oracle's own demo needs an explicit text-to-bytes conversion it writes
//        `Blob(mle_result.Text, EncodingANSI!)` [u_cst_tabpage_utility_crypto.sru:L419]. So the
//        framework's own explicit conversions are ANSI.
//      * Yet the same demo passes a BARE STRING to `Hash` at :L404, with no conversion at all -
//        `mle_result.Text = _crypto.Hash(mle_result.Text, Enums.CRYPTO_HASH_SHA256)`. The DLL
//        chose, and it did not record what it chose.
//      * PowerBuilder strings are UTF-16 internally, so the DLL had to convert to something, and
//        both UTF-8 and the active ANSI code page were available to it.
//
//  THE DECISION IS UTF-8, for the same reason DECISION D1 in LegacyDefaults chose UTF-8 for key
//  material: UTF-8 is byte-identical to an ANSI conversion across the whole ASCII range, which is
//  what every field in the oracle's own demo is typed into, whereas PowerBuilder's ANSI conversion
//  is code-page and locale dependent and therefore NOT REPRODUCIBLE IN A LINUX CONTAINER AT ALL.
//  Choosing ANSI would mean choosing a code page, and choosing a code page would be inventing a
//  fact the repository does not contain.
//
//  ASCII-ONLY INPUTS ARE UNAFFECTED by this decision, which is exactly why the demo's own examples
//  could never have revealed the difference, and why the published known-answer vectors for MD5 and
//  the SHA family are valid tests of this file regardless of how H1 is eventually settled.
//
//  The decision lives in ONE named place, `InputTextEncoding` below, so that when the behavioural
//  oracle settles it the correction is one member and nothing else anywhere. Per the parity
//  evidence model this is a REASONED CHOICE AND NOT A MEASUREMENT: byte-exact parity for a
//  non-ASCII input is verifiable only by capturing the legacy output for a workflow and comparing
//  it with the target output for the same workflow, and it is never claimed here as verified.
//
//  ==============================================================================================
//  DECISION H2 - CRC32 IS IMPLEMENTED IN THIS REPOSITORY, NOT TAKEN FROM A PACKAGE
//  ==============================================================================================
//  CRC32 is the sixth arm [enums.sru:L933] and it is NOT A CRYPTOGRAPHIC HASH. It is a checksum:
//  it detects accidental corruption and offers NO COLLISION RESISTANCE AND NO PREIMAGE RESISTANCE
//  whatever. Forging an input with a chosen CRC32 is trivial arithmetic. It must never be used
//  where a cryptographic digest is required, and a caller that reaches for it for an integrity or
//  signature purpose is making a mistake the legacy also permitted.
//
//  It stays regardless, for two reasons. It is published, so removing it would reject input the
//  legacy accepted. And it is genuinely exercised rather than a dead arm: the oracle's own demo
//  calls `HashFile("pfw.dll", Enums.CRYPTO_HASH_CRC32)` at u_cst_tabpage_utility_crypto.sru:L281.
//
//  BUILD, NOT BUY, and the reasoning is recorded because it is a dependency decision:
//
//      * There is NO CRC32 in the Base Class Library. Unlike MD5 and the SHA family, it is not a
//        substitution that System.Security.Cryptography can satisfy.
//      * `System.IO.Hashing.Crc32` exists, but it lives in a separate package that is NOT among
//        the pins in the repository-root central package version file. Central package management
//        is mandatory and that file is not this file's to change, so adding a package here is out
//        of scope - and a versionless reference to an unpinned package fails restore outright.
//      * The algorithm is thirty lines of table-driven arithmetic with published test vectors, so
//        an in-repository implementation is both sufficient and independently verifiable.
//
//  THE VARIANT is the standard IEEE 802.3 / PKZIP reflected CRC-32: reflected polynomial
//  0xEDB88320, initial register all ones, final complement. The in-repository evidence for that
//  choice is real but is a REASONED INFERENCE AND NOT A CERTAINTY: zlib is one of the eleven
//  upstream libraries the framework attributes [ws_objects/pfw.demos.pbl.src/w_about.srw:L119,
//  carried forward into the root NOTICE], zlib's `crc32` is precisely this variant, and the closed
//  pfw.dll almost certainly exposed it rather than hand-rolling a different one. Like H1, it is
//  oracle-only verifiable and is not claimed as verified.
//
//  ==============================================================================================
//  DECISION H3 - A NULL ARGUMENT IS REJECTED, NEVER COERCED
//  ==============================================================================================
//  Nullable reference types are enabled, so a null `data` or `filename` is already a compile-time
//  warning at a caller that can see it - and warnings are errors in this repository. A null can
//  still arrive from a nullable-oblivious caller, from deserialization or from reflection, so the
//  behaviour is defined rather than left to a NullReferenceException.
//
//  ALL NINE MEMBERS BEHAVE IDENTICALLY: null throws ArgumentNullException naming the parameter.
//  The rejected alternative is worth recording. Treating null as empty would return the digest of
//  the empty input - a perfectly well-formed, plausible-looking string - and so would convert a
//  caller defect into a silent wrong answer that no test downstream could distinguish from a
//  correct one. PowerBuilder's null-string semantics have no faithful mapping here in any case:
//  what the closed binary did with a null argument is unobservable, so this is a defined error
//  chosen over a guess.
//
//  AN EMPTY INPUT IS VALID AND IS NOT AN ERROR. An empty array, an empty string and a zero-length
//  file all produce the well-defined digest of zero bytes, which for every one of the six arms is
//  a fixed, non-empty result.
//
//  ==============================================================================================
//  DECISION H4 - HashFile PROPAGATES THE FILESYSTEM FAILURE, AND CARRIES AN EXPOSURE NOTE
//  ==============================================================================================
//  What the closed binary returned for a missing or unreadable file is unobservable. The choice
//  here is to let the framework's own file exception propagate unchanged - FileNotFoundException,
//  DirectoryNotFoundException, UnauthorizedAccessException, IOException, PathTooLongException,
//  ArgumentException for a malformed or empty path - rather than to invent a sentinel return. A
//  defined error is preferable to a guessed one, and an empty string would be indistinguishable
//  from a legitimate result of some future encoding rule.
//
//  THE ALGORITHM IDENTIFIER IS SCREENED BEFORE THE FILE IS OPENED. That ordering is deliberate: an
//  unsupported algorithm must fail without the process having touched the filesystem at all.
//
//  EXPOSURE NOTE FOR THE ENDPOINT AUTHOR, which is the reason this paragraph exists in code rather
//  than only in a document. `HashFile` is reproduced faithfully because it is one of the 63 ported
//  declarations, AND IT IS PUBLISHED: the cryptographic contract projects all 63 legacy overloads onto
//  17 operations, of which `POST /v1/crypto/hash-file` is operation 2 and `POST /v1/crypto/hmac-file`
//  is operation 4 [shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, the overload census].
//
//  WHAT THE CONTRACT DOES NOT PUBLISH IS A PATH, AND THAT IS THE WHOLE OF THE ANSWER. A
//  caller-supplied filesystem path reachable from a network endpoint would be an arbitrary-file-read
//  oracle - a caller could confirm that a path exists and could digest a file it cannot otherwise
//  read, neither of which an in-process library called by its own application could ever have been.
//  So the wire carries an OPAQUE SERVER-RESOLVED `fileRef`, exactly as it carries `keyRef` for key
//  material and for the same reason: the endpoint resolves the reference against its own configured,
//  allow-listed store, an unknown reference is a plain 404, and a reference outside that store cannot
//  be constructed by a caller at all. There is no path to traverse because there is no path.
//
//  WHAT THAT NARROWS, STATED HERE TOO SO A READER OF THIS FILE SEES IT: the legacy hashes any file its
//  process could open, and the published operations hash any file the deployment has been CONFIGURED to
//  expose. That is a real reduction in reach and it is deliberate.
//
//  THIS FILE REGISTERS NO ROUTE AND OPENS NO LISTENER, and it still takes a resolved path rather than a
//  reference - deliberately, because the resolution is the ENDPOINT's job. Two things follow for whoever
//  writes that layer. The reference-to-path resolution and its allow-list live there, where they can be
//  expressed as authorization and configuration. And no path validation is added HERE, because a path
//  filter would change observable behaviour for the in-process callers the legacy had and would smuggle
//  a policy decision into a digest routine.
//
//  ==============================================================================================
//  THE DIGEST OUTPUT ENCODING IS NOT A DECISION OF THIS FILE - IT IS DECISION D4
//  ==============================================================================================
//  All nine legacy declarations return `string`, not `blob`, and the oracle's own demo assigns the
//  result straight into a plain multi-line text control - `Hash` at
//  u_cst_tabpage_utility_crypto.sru:L404, `HashFile` at :L281 and :L489. The digest is therefore
//  ALREADY TEXT-ENCODED when it leaves the function.
//
//  The corroborating detail that makes this a decision record rather than a formatting note:
//  `Hash` and `HashFile` do NOT route through the caller-driven blob-to-text surface. The demo
//  calls `BlobToString` and `StringToBlob` separately and explicitly, at :L419 and :L434 for
//  Base64 and at :L562 and :L577 for hexadecimal, while the digest at :L404 arrives printable with
//  no conversion call anywhere in sight. So the digest's text encoding is INTERNAL to the closed
//  DLL, and there is no parameter through which a caller could have selected it.
//
//  This file therefore does not choose. It applies `LegacyDefaults.STRING_PAYLOAD_ENCODING`
//  through the injected encoding provider, which is DECISION D4 - Base64, uniform across hash
//  digests, symmetric ciphertext, RSA ciphertext and RSA signatures. LegacyDefaults is the single
//  decision record, including the honest statement that a lowercase hexadecimal digest is the more
//  common convention for MD5 and the SHA family specifically and is the most plausible way that one
//  uniform rule turns out to be wrong for exactly these three groups. Centralising it is what lets
//  the oracle adjudicate it in one place and, if it is wrong, correct it in one place. NO SECOND
//  ENCODING CONVENTION IS INTRODUCED HERE, and that includes the CRC32 arm.
//
//  The resulting widths, documented because callers size columns and buffers from them, are
//  standard padded single-line Base64 over the raw digest bytes:
//
//      arm       digest bytes    Base64 characters
//      -------   ------------    -----------------------------------------
//      MD5                 16    24, the last two of which are padding
//      SHA1                20    28, the last one of which is padding
//      SHA256              32    44, the last one of which is padding
//      SHA384              48    64, with no padding
//      SHA512              64    88, the last two of which are padding
//      CRC32                4     8, the last two of which are padding
//
//  CRC32's four bytes are laid out MOST SIGNIFICANT BYTE FIRST, which is what makes the encoded
//  form correspond to the conventional written spelling of a CRC-32 value. Byte order is
//  unobservable from the repository like everything else in this paragraph, so it too is a recorded
//  decision.
//
//  ==============================================================================================
//  TWO LEGACY IDIOMS WITH NO ANALOGUE TO REPRODUCE
//  ==============================================================================================
//  A reader comparing this file with the legacy will look for these and should find them absent by
//  design rather than by oversight.
//
//      * THE LAZY-CONSTRUCTION GUARD. Every wrapper body opens with
//        `if Not IsValid(n_crypto) then n_crypto = Create n_crypto` [md5.srf:L11 and :L15,
//        sha1.srf:L11 and :L15, sha256.srf:L11 and :L15]. It exists because the instance it guards
//        is a global that any caller may reach before anything has constructed it.
//      * THE TYPE-SHADOWING GLOBAL. `global n_crypto n_crypto` [n_crypto.sru:L75] declares an auto
//        instance whose name collides with its own type name, which is legal only because
//        PowerBuilder resolves one flat global namespace and offers no container.
//
//  In this port the equivalent object is a CONTAINER-MANAGED INSTANCE SERVICE consumed by
//  constructor injection: never a global, never a static, never lazily self-constructing, and
//  therefore with nothing to check before use. There is nothing to port, only a collision to avoid.
//
//  CONCURRENCY. This type is stateless apart from the injected provider and is safe to call from
//  any number of requests at once. Specifically, NO MUTABLE HASH ALGORITHM INSTANCE IS EVER CACHED
//  IN A FIELD: the framework's incremental hash objects are not thread-safe, so one is created,
//  used and disposed inside a single call. The only static data below is an immutable precomputed
//  lookup table and an immutable encoding instance, both write-once and read-only thereafter.
// ==============================================================================================

using System.Security.Cryptography;
using System.Text;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// Computes unkeyed message digests and checksums, reproducing the legacy cryptographic library's
/// unkeyed hashing surface at ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21, :L22 and :L27
/// together with the six global function wrappers built on it in md5.srf, sha1.srf and sha256.srf.
/// </summary>
/// <remarks>
/// <para>
/// Nine public members exactly: three from the native class declarations and six from the wrappers.
/// The six algorithm identifiers are the only six the legacy publishes,
/// <see cref="Enums.CRYPTO_HASH_MD5"/> through <see cref="Enums.CRYPTO_HASH_CRC32"/>
/// [ws_objects/pfw.shared.pbl.src/enums.sru:L928-L933]; no seventh algorithm, no convenience
/// overload and no raw-bytes variant is offered.
/// </para>
/// <para>
/// HANDLES NO KEY MATERIAL. The keyed overloads of the same legacy surface -
/// four <c>Hash</c> at n_crypto.sru:L23-L26 and two <c>HashFile</c> at :L28-L29 - are a different
/// primitive with a different security contract and belong to the keyed provider. No member of this
/// type accepts a key, and none may be added.
/// </para>
/// <para>
/// LOGS NOTHING. This type sees whatever a caller is hashing, so anything it logged would be
/// exactly the payload that must not be logged. There is deliberately no logger.
/// </para>
/// <para>
/// RETURNS TEXT, NOT BYTES. Every member returns the digest already encoded, because all nine
/// legacy declarations return a string and the oracle's own caller assigns the result straight into
/// a text control [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L404]. The
/// encoding is not chosen here: it is <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>,
/// DECISION D4, applied through the injected <see cref="EncodingProvider"/>.
/// </para>
/// <para>
/// STATELESS AND THREAD-SAFE. Apart from the injected provider there is no field, no cache and no
/// mutable static state, and no hash algorithm instance is ever retained between calls, so one
/// instance serves any number of concurrent requests. It is registered in the service container by
/// the composition root and consumed by constructor injection; it is never reached through a global
/// or a static.
/// </para>
/// <para>
/// REGISTERS NO ROUTE. See DECISION H4 in this file's header for the exposure consideration that
/// <see cref="HashFile"/> carries for whoever writes the endpoint layer.
/// </para>
/// </remarks>
public sealed class HashProvider
{
    /// <summary>
    /// The single message text for the unsupported-algorithm failure, so that the one condition has
    /// one wording as well as one shape wherever it is reached from.
    /// </summary>
    /// <remarks>
    /// It describes the published set rather than defining it. The definition is
    /// <see cref="LegacyDefaults.IsSupportedHashType"/>, which is the only gate this type consults;
    /// this string must never become a second, drifting statement of what is allowed.
    /// </remarks>
    private const string UnsupportedHashTypeMessage =
        "Not a published hash type. The set is exactly MD5 (0), SHA1 (1), SHA256 (2), SHA384 (3), " +
        "SHA512 (4) and CRC32 (5) [ws_objects/pfw.shared.pbl.src/enums.sru:L928-L933].";

    /// <summary>
    /// The buffer length used when digesting a file, in bytes.
    /// </summary>
    /// <remarks>
    /// 80 KiB, which is the buffer length the framework itself uses for stream copying, so it is a
    /// conventional value rather than a tuned one. It exists because
    /// <see cref="HashFile(string, long)"/> must STREAM: the file the oracle's own demo digests is
    /// a multi-megabyte native binary [u_cst_tabpage_utility_crypto.sru:L281 and :L489], and
    /// reading a caller-named file whole into memory would make its size a memory-exhaustion lever.
    /// No performance claim is attached to this number; the requirement it satisfies is bounded
    /// memory, not throughput.
    /// </remarks>
    private const int FileReadBufferLengthBytes = 81920;

    /// <summary>
    /// The injected encoding provider through which every digest is rendered printable.
    /// </summary>
    /// <remarks>
    /// Injected rather than constructed so that the digest encoding of this type, of the keyed
    /// provider, of the symmetric cipher provider and of the RSA provider is demonstrably the same
    /// code path and not four similar ones.
    /// </remarks>
    private readonly EncodingProvider _encoding;

    /// <summary>
    /// Initializes a new instance of the <see cref="HashProvider"/> class.
    /// </summary>
    /// <param name="encoding">
    /// The provider that renders a raw digest printable. Supplied by the container.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="encoding"/> is null.</exception>
    /// <remarks>
    /// This constructor is the replacement for the legacy's lazy-construction guard
    /// [ws_objects/pfw.crypto.pbl.src/md5.srf:L11 and :L15] and for the type-shadowing global auto
    /// instance it guards [n_crypto.sru:L75]. A container-managed instance has nothing to check
    /// before use, so neither idiom has an analogue to reproduce - see this file's header.
    /// </remarks>
    public HashProvider(EncodingProvider encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        _encoding = encoding;
    }

    /// <summary>
    /// The encoding used to turn a <see cref="string"/> input into the bytes that are digested,
    /// which is UTF-8. This is DECISION H1, and it is the single place the choice is made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The closed native binary performed this conversion internally
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21], so the choice is unobservable from the
    /// repository and is a REASONED DECISION RATHER THAN A MEASURED FACT. UTF-8 is chosen for the
    /// same reason <see cref="LegacyDefaults.KeyMaterialEncoding"/> chooses it: it is byte-identical
    /// to an ANSI conversion across the whole ASCII range, which is what every field in the
    /// oracle's own demo is typed into, whereas PowerBuilder's ANSI conversion is code-page and
    /// locale dependent and therefore not reproducible in a Linux container at all.
    /// </para>
    /// <para>
    /// A UTF-8 choice and an ANSI choice produce DIFFERENT DIGESTS for any non-ASCII input, so this
    /// is a real parity fork and the highest-value oracle question in this file. ASCII-only inputs
    /// are unaffected, which is why the published known-answer vectors remain valid tests either
    /// way. See DECISION H1 in this file's header for the full evidence.
    /// </para>
    /// <para>
    /// <see cref="Encoding.UTF8"/> emits no byte-order mark from
    /// <see cref="Encoding.GetBytes(string)"/> - the preamble is a stream-writing concern - so no
    /// mark can leak into a digest. The instance is immutable and shared, which is what makes this
    /// static member read-only shared data rather than mutable static state.
    /// </para>
    /// <para>
    /// It is <see langword="internal"/> rather than private for exactly one reason: the parity
    /// marker test names this decision instead of duplicating it, so that when the oracle settles
    /// H1 there is one member to change and one test to re-baseline.
    /// </para>
    /// </remarks>
    internal static Encoding InputTextEncoding => Encoding.UTF8;

    // ==========================================================================================
    //  THE THREE NATIVE-DERIVED MEMBERS
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21, :L22, :L27
    // ==========================================================================================
    #region Native declarations - n_crypto.sru:L21-L22, :L27

    /// <summary>
    /// Computes the digest of binary data with the requested algorithm and returns it in printable
    /// form.
    /// </summary>
    /// <param name="data">
    /// The bytes to digest. An EMPTY array is valid and yields the well-defined digest of zero
    /// bytes, which for every algorithm is a fixed, non-empty result.
    /// </param>
    /// <param name="ntype">
    /// One of the six published algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>.
    /// </param>
    /// <returns>
    /// The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>: standard padded
    /// single-line Base64 over the raw digest bytes. See this file's header for the width each
    /// algorithm produces.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the six published algorithm identifiers.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested digest. Reachable in practice for
    /// <see cref="Enums.CRYPTO_HASH_MD5"/> on a host enforcing FIPS 140; see this file's header for
    /// why that failure is allowed to propagate rather than being answered with another algorithm.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string Hash(readonly blob data, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L22], whose implementation lives in the closed
    /// native binary and cannot be read.
    /// </para>
    /// <para>
    /// This is the in-memory shape of the surface. It performs no character encoding at all - the
    /// caller has already decided what its bytes are - which is the distinction from
    /// <see cref="Hash(string, long)"/> and the reason DECISION H1 applies only to that one.
    /// </para>
    /// </remarks>
    public string Hash(byte[] data, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);

        // ResolveAlgorithm both screens ntype and selects the arm, so an unsupported identifier
        // fails before a single byte is read. Argument evaluation is left to right, so the screen
        // runs before ComputeDigest is entered.
        return EncodeDigest(ComputeDigest(data, ResolveAlgorithm(ntype)));
    }

    /// <summary>
    /// Computes the digest of text with the requested algorithm and returns it in printable form.
    /// </summary>
    /// <param name="data">
    /// The text to digest. An EMPTY string is valid and yields the digest of zero bytes. The text is
    /// converted to bytes per DECISION H1 - see <see cref="InputTextEncoding"/>.
    /// </param>
    /// <param name="ntype">
    /// One of the six published algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>.
    /// </param>
    /// <returns>
    /// The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the six published algorithm identifiers.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested digest.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string Hash(readonly string data, readonly long ntype)</c>
    /// [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L21]. This is the overload the oracle's own demo
    /// calls with a bare string at
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L404.
    /// </para>
    /// <para>
    /// PARITY FORK. The text-to-bytes step happened inside the closed binary and is therefore
    /// unobservable. It is resolved to UTF-8 here as DECISION H1, which is byte-identical to the
    /// framework's explicit ANSI conversions across the ASCII range and divergent from them outside
    /// it. Byte-exact parity for a non-ASCII input is verifiable only against the behavioural
    /// oracle and is not claimed.
    /// </para>
    /// <para>
    /// It applies the encoding decision and then DELEGATES to <see cref="Hash(byte[], long)"/>, so
    /// the algorithm dispatch, the validation and the output encoding are reached through exactly
    /// one path rather than two.
    /// </para>
    /// </remarks>
    public string Hash(string data, long ntype)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Hash(InputTextEncoding.GetBytes(data), ntype);
    }

    /// <summary>
    /// Computes the digest of a file's contents with the requested algorithm and returns it in
    /// printable form, reading the file as a stream.
    /// </summary>
    /// <param name="filename">
    /// The path of the file to digest. An EMPTY or zero-length file is valid and yields the digest
    /// of zero bytes.
    /// </param>
    /// <param name="ntype">
    /// One of the six published algorithm identifiers, <see cref="Enums.CRYPTO_HASH_MD5"/> through
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/>.
    /// </param>
    /// <returns>
    /// The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>. For the same
    /// bytes it is identical to what <see cref="Hash(byte[], long)"/> returns, and a unit test
    /// asserts that equivalence.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="filename"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the six published algorithm identifiers. This is
    /// screened BEFORE the file is opened, so an unsupported identifier never causes a filesystem
    /// access.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="filename"/> is empty or contains characters the platform rejects in a path.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// No file exists at <paramref name="filename"/>.
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// Part of the directory portion of <paramref name="filename"/> does not exist.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The path denotes a directory, or the process lacks read permission for it.
    /// </exception>
    /// <exception cref="IOException">
    /// The file could not be read - it is locked for exclusive use, or the read failed.
    /// </exception>
    /// <exception cref="PathTooLongException">
    /// <paramref name="filename"/> exceeds the platform's maximum path length.
    /// </exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the requested digest.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>public function string HashFile(readonly string filename, readonly long
    /// ntype)</c> [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L27]. The oracle's own demo calls it
    /// twice, with CRC32 at ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L281 and
    /// with SHA1 at :L489, both over a multi-megabyte native binary.
    /// </para>
    /// <para>
    /// STREAMED IN BOUNDED MEMORY, never read whole. The file is opened for sequential reading and
    /// consumed in <see cref="FileReadBufferLengthBytes"/> chunks, so a caller-named file's size is
    /// not a memory-exhaustion lever. Every algorithm arm, the CRC32 one included, supports
    /// incremental consumption for exactly this reason.
    /// </para>
    /// <para>
    /// FAILURE SHAPE - DECISION H4. What the closed binary returned for a missing or unreadable
    /// file is unobservable, so the framework's own file exception is allowed to propagate unchanged
    /// rather than a sentinel return value being invented. The file is opened with a read-only share
    /// mode, so another process holding the file open for reading does not block this call while one
    /// holding it for writing does.
    /// </para>
    /// <para>
    /// EXPOSURE CONSIDERATION FOR THE ENDPOINT AUTHOR. The published cryptographic service contract
    /// does NOT enumerate a file-hash operation, and a caller-supplied path reachable from a network
    /// endpoint is an arbitrary-file-read primitive that an in-process library could never have
    /// been. This type registers no route, so it creates no such exposure by itself; publishing this
    /// member is a decision to be taken knowingly at the boundary, where an authorization and
    /// allow-list policy can be expressed. NO PATH VALIDATION IS ADDED HERE, because a path filter
    /// would change observable behaviour for the in-process callers the legacy had. See DECISION H4
    /// in this file's header.
    /// </para>
    /// </remarks>
    public string HashFile(string filename, long ntype)
    {
        ArgumentNullException.ThrowIfNull(filename);

        // Screen the algorithm identifier BEFORE touching the filesystem, so that an unsupported
        // value cannot be used to probe for the existence of a path. This is the one place where
        // the resolution is deliberately hoisted out of the argument list to make that ordering
        // explicit rather than incidental.
        HashAlgorithmName? algorithm = ResolveAlgorithm(ntype);

        // SequentialScan states the access pattern to the operating system; the buffer length is
        // shared with the read loop so the stream does not buffer a second time underneath it.
        using FileStream source = new(
            filename,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileReadBufferLengthBytes,
            FileOptions.SequentialScan);

        return EncodeDigest(ComputeDigest(source, algorithm));
    }

    #endregion

    // ==========================================================================================
    //  THE SIX WRAPPER MEMBERS
    //  ORACLE  ws_objects/pfw.crypto.pbl.src/md5.srf, sha1.srf, sha256.srf
    //  ------------------------------------------------------------------------------------------
    //  Each legacy wrapper body is a single delegating line, for example
    //  `return n_crypto.Hash(data, Enums.CRYPTO_HASH_MD5)` [md5.srf:L12]. Each member below is the
    //  same single line. NONE OF THEM TOUCHES AN ALGORITHM DIRECTLY: a wrapper that re-implemented
    //  the dispatch could drift from the dispatcher, which is the exact class of defect this shape
    //  removes. A unit theory asserts each wrapper equals the corresponding Hash call.
    //
    //  Note also what is NOT here. There is no `Sha384` or `Sha512` wrapper, because the legacy
    //  publishes no sha384.srf or sha512.srf - those two algorithms are reachable only through
    //  Hash with the identifier. Adding a wrapper for them would invent surface the legacy never
    //  had, and would break the 3 + 6 census.
    // ==========================================================================================
    #region Wrapper declarations - md5.srf, sha1.srf, sha256.srf

    /// <summary>
    /// Computes the MD5 digest of binary data.
    /// </summary>
    /// <param name="data">The bytes to digest. An empty array is valid.</param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce an MD5 digest, which a host enforcing FIPS 140 may do.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>global function string md5(readonly blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/md5.srf:L11-L13], whose body delegates to the native
    /// <c>Hash</c> with <see cref="Enums.CRYPTO_HASH_MD5"/>.
    /// </para>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY. MD5 is comprehensively broken for collision
    /// resistance: colliding inputs can be constructed in seconds on commodity hardware, so an MD5
    /// digest proves nothing about the identity of the data it came from. It is retained because it
    /// is published and because removing it would reject input the legacy accepted, and it is
    /// neither deprecated nor silently redirected to a stronger algorithm - a redirection would
    /// change every digest the legacy ever produced.
    /// </para>
    /// </remarks>
    public string Md5(byte[] data) => Hash(data, Enums.CRYPTO_HASH_MD5);

    /// <summary>
    /// Computes the MD5 digest of text.
    /// </summary>
    /// <param name="str">
    /// The text to digest. Converted to bytes per DECISION H1 - see
    /// <see cref="InputTextEncoding"/>.
    /// </param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce an MD5 digest.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function string md5(readonly string str)</c>
    /// [ws_objects/pfw.crypto.pbl.src/md5.srf:L15-L17]. The parameter keeps the legacy's own name,
    /// <c>str</c>, rather than being harmonised with the blob overload's <c>data</c>: the two
    /// overloads genuinely spell it differently in the source, and the spelling is observable to a
    /// C# caller using a named argument. See the MD5 weakness annotation on
    /// <see cref="Md5(byte[])"/>.
    /// </remarks>
    public string Md5(string str) => Hash(str, Enums.CRYPTO_HASH_MD5);

    /// <summary>
    /// Computes the SHA-1 digest of binary data.
    /// </summary>
    /// <param name="data">The bytes to digest. An empty array is valid.</param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the digest.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Substitutes <c>global function string sha1(readonly blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/sha1.srf:L11-L13], whose body delegates to the native
    /// <c>Hash</c> with <see cref="Enums.CRYPTO_HASH_SHA1"/>.
    /// </para>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY. SHA-1 collision resistance is broken in
    /// practice, including chosen-prefix collisions, so a SHA-1 digest must not be relied on to
    /// identify data. It is retained for the same reason MD5 is: it is published, and removing or
    /// redirecting it would change observable behaviour.
    /// </para>
    /// <para>
    /// This is the algorithm the oracle's own demo passes to the file-hash surface at
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L489.
    /// </para>
    /// </remarks>
    public string Sha1(byte[] data) => Hash(data, Enums.CRYPTO_HASH_SHA1);

    /// <summary>
    /// Computes the SHA-1 digest of text.
    /// </summary>
    /// <param name="str">
    /// The text to digest. Converted to bytes per DECISION H1 - see
    /// <see cref="InputTextEncoding"/>.
    /// </param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the digest.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function string sha1(readonly string str)</c>
    /// [ws_objects/pfw.crypto.pbl.src/sha1.srf:L15-L17]. See the SHA-1 weakness annotation on
    /// <see cref="Sha1(byte[])"/>.
    /// </remarks>
    public string Sha1(string str) => Hash(str, Enums.CRYPTO_HASH_SHA1);

    /// <summary>
    /// Computes the SHA-256 digest of binary data.
    /// </summary>
    /// <param name="data">The bytes to digest. An empty array is valid.</param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the digest.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function string sha256(readonly blob data)</c>
    /// [ws_objects/pfw.crypto.pbl.src/sha256.srf:L11-L13], whose body delegates to the native
    /// <c>Hash</c> with <see cref="Enums.CRYPTO_HASH_SHA256"/>. This is the only one of the three
    /// wrapped algorithms that is not a known legacy weakness, and it is also the algorithm the
    /// oracle's own demo passes to the in-memory surface at
    /// ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L404 and to the signature
    /// surface at :L470 and :L474.
    /// </remarks>
    public string Sha256(byte[] data) => Hash(data, Enums.CRYPTO_HASH_SHA256);

    /// <summary>
    /// Computes the SHA-256 digest of text.
    /// </summary>
    /// <param name="str">
    /// The text to digest. Converted to bytes per DECISION H1 - see
    /// <see cref="InputTextEncoding"/>.
    /// </param>
    /// <returns>The digest encoded per <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
    /// <exception cref="CryptographicException">
    /// The platform refused to produce the digest.
    /// </exception>
    /// <remarks>
    /// Substitutes <c>global function string sha256(readonly string str)</c>
    /// [ws_objects/pfw.crypto.pbl.src/sha256.srf:L15-L17].
    /// </remarks>
    public string Sha256(string str) => Hash(str, Enums.CRYPTO_HASH_SHA256);

    #endregion

    // ==========================================================================================
    //  THE PRIVATE SEAMS - EACH EXISTS EXACTLY ONCE
    //  ------------------------------------------------------------------------------------------
    //  All nine public members funnel through these four, and each of the four is the ONLY place
    //  its concern is expressed:
    //
    //      ResolveAlgorithm   validates the identifier and selects the arm - the single dispatch
    //      ComputeDigest      two shapes, in-memory and streamed, both selecting through the above
    //      EncodeDigest       the single output-encoding point, DECISION D4
    //
    //  Keeping them singular is what makes the six arms, the accepted set and the output convention
    //  consistent across all nine members, and it is the shape the keyed provider mirrors.
    // ==========================================================================================
    #region Private seams

    /// <summary>
    /// Validates <paramref name="ntype"/> against the published set and selects the algorithm arm.
    /// This is THE single algorithm dispatch of this type.
    /// </summary>
    /// <param name="ntype">The candidate algorithm identifier.</param>
    /// <returns>
    /// The framework algorithm name for the five cryptographic arms, or <see langword="null"/> for
    /// the CRC32 arm, which has no framework implementation and is computed by
    /// <see cref="Crc32"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ntype"/> is not one of the six published identifiers.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Validation and selection are ONE operation deliberately. Splitting them would allow a call
    /// path that selects without validating, and the selection below treats its final arm as CRC32
    /// precisely because the screen has already narrowed the input to six values - so an unscreened
    /// value would silently become a checksum instead of failing.
    /// </para>
    /// <para>
    /// The screen delegates to <see cref="LegacyDefaults.IsSupportedHashType"/> and never to a local
    /// allowed-set, because that predicate is shared with the keyed provider and with the RSA
    /// signature surface [ws_objects/pfw.shared.pbl.src/enums.sru:L927], and three divergent copies
    /// of one set is how the three come to disagree.
    /// </para>
    /// </remarks>
    private static HashAlgorithmName? ResolveAlgorithm(long ntype)
    {
        if (!LegacyDefaults.IsSupportedHashType(ntype))
        {
            throw new ArgumentOutOfRangeException(nameof(ntype), ntype, UnsupportedHashTypeMessage);
        }

        return ntype switch
        {
            Enums.CRYPTO_HASH_MD5 => HashAlgorithmName.MD5,
            Enums.CRYPTO_HASH_SHA1 => HashAlgorithmName.SHA1,
            Enums.CRYPTO_HASH_SHA256 => HashAlgorithmName.SHA256,
            Enums.CRYPTO_HASH_SHA384 => HashAlgorithmName.SHA384,
            Enums.CRYPTO_HASH_SHA512 => HashAlgorithmName.SHA512,

            // The screen above admits exactly six values and five are named, so this arm is
            // reachable ONLY for Enums.CRYPTO_HASH_CRC32. It is written as the catch-all rather
            // than as a sixth named arm because a switch expression over `long` requires a total
            // set of arms, and a named CRC32 arm plus an unreachable catch-all would be dead code
            // that no test could cover and that the coverage gate would count against this file.
            _ => null,
        };
    }

    /// <summary>
    /// Computes a raw digest over data already held in memory.
    /// </summary>
    /// <param name="data">The bytes to digest, possibly empty.</param>
    /// <param name="algorithm">
    /// The arm chosen by <see cref="ResolveAlgorithm"/>: a framework algorithm name, or
    /// <see langword="null"/> for CRC32.
    /// </param>
    /// <returns>The raw digest bytes, not yet encoded.</returns>
    /// <remarks>
    /// The incremental hash object is created, used and disposed inside this call and is NEVER
    /// cached in a field. That is a thread-safety requirement rather than a style choice: those
    /// objects carry mutable digest state and are not safe to share, so a cached instance would
    /// corrupt results under concurrency in a way that looks like a hashing defect.
    /// </remarks>
    private static byte[] ComputeDigest(ReadOnlySpan<byte> data, HashAlgorithmName? algorithm)
    {
        if (algorithm is null)
        {
            return Crc32.Finish(Crc32.Update(Crc32.InitialRegister, data));
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(algorithm.Value);
        hash.AppendData(data);
        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Computes a raw digest by reading a stream to its end in bounded-size chunks.
    /// </summary>
    /// <param name="source">The stream to read. It is read from its current position to its end.</param>
    /// <param name="algorithm">
    /// The arm chosen by <see cref="ResolveAlgorithm"/>: a framework algorithm name, or
    /// <see langword="null"/> for CRC32.
    /// </param>
    /// <returns>The raw digest bytes, not yet encoded.</returns>
    /// <exception cref="IOException">The stream could not be read.</exception>
    /// <remarks>
    /// <para>
    /// This is the shape that makes <see cref="HashFile(string, long)"/> safe over a file of
    /// arbitrary size: memory use is one buffer of <see cref="FileReadBufferLengthBytes"/> plus the
    /// digest, independent of the input's length. A zero-length stream is valid and yields the
    /// digest of zero bytes, because neither loop body executes.
    /// </para>
    /// <para>
    /// Both arms consume the input incrementally, which is why the CRC32 implementation is written
    /// as a register-and-update pair rather than as a one-shot function over a whole array.
    /// </para>
    /// </remarks>
    private static byte[] ComputeDigest(Stream source, HashAlgorithmName? algorithm)
    {
        byte[] buffer = new byte[FileReadBufferLengthBytes];

        if (algorithm is null)
        {
            uint register = Crc32.InitialRegister;
            int checksumRead;
            while ((checksumRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                register = Crc32.Update(register, buffer.AsSpan(0, checksumRead));
            }

            return Crc32.Finish(register);
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(algorithm.Value);
        int digestRead;
        while ((digestRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, digestRead);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Renders a raw digest printable. This is THE single output-encoding point of this type.
    /// </summary>
    /// <param name="digest">The raw digest bytes.</param>
    /// <returns>The encoded digest.</returns>
    /// <remarks>
    /// The encoding is not chosen here and must never be chosen here: it is
    /// <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/>, DECISION D4, so that hash digests,
    /// symmetric ciphertext, RSA ciphertext and RSA signatures all carry one convention and the
    /// behavioural oracle can adjudicate that convention in one place. Routing through the injected
    /// <see cref="EncodingProvider"/> rather than calling a conversion primitive directly is what
    /// makes "one convention" a shared code path instead of a shared intention.
    /// </remarks>
    private string EncodeDigest(byte[] digest) =>
        _encoding.BlobToString(digest, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    #endregion

    // ==========================================================================================
    //  THE IN-REPOSITORY CRC32 - DECISION H2
    //  ------------------------------------------------------------------------------------------
    //  Why this exists at all, restated briefly because it is the one algorithm below that is not
    //  a framework substitution: CRC32 is the sixth published arm [enums.sru:L933], the Base Class
    //  Library has no implementation of it, and the package that does have one is not among the
    //  repository's central version pins - which this file may not change and a versionless
    //  reference to an unpinned package fails restore. So it is implemented here, in thirty lines
    //  of table-driven arithmetic with published test vectors. The full reasoning, including the
    //  zlib attribution that makes the IEEE 802.3 reflected variant the reasoned inference, is in
    //  DECISION H2 in this file's header.
    //
    //  IT IS NOT A CRYPTOGRAPHIC HASH. No collision resistance, no preimage resistance: producing
    //  an input with a chosen checksum is trivial arithmetic. It is offered ONLY because the legacy
    //  offers it, it is not gated behind a flag, it is not removed, and it must never be used where
    //  a cryptographic digest is required.
    //
    //  NO SCREAMING_SNAKE IDENTIFIER APPEARS BELOW. The repository's naming-analyzer suppressions
    //  are scoped file by file and this file is not one of them, so the polynomial, the initial
    //  register, the digest length and the table are all PascalCase. The only CRYPTO_* text
    //  anywhere in this file is an `Enums.`-qualified reference or documentation prose.
    // ==========================================================================================
    #region CRC32 - DECISION H2

    /// <summary>
    /// The standard IEEE 802.3 reflected CRC-32 checksum, implemented in this repository because
    /// the Base Class Library has none and the package that does is not centrally pinned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PARAMETERS OF THE VARIANT, stated so it can be checked against a published reference:
    /// reflected polynomial <c>0xEDB88320</c> - the bit-reversal of the normal form
    /// <c>0x04C11DB7</c> - an initial register of all ones, byte-wise reflected processing, and a
    /// final complement. This is the variant used by Ethernet, PKZIP, gzip and zlib, so a published
    /// vector for any of those is a valid test of this code.
    /// </para>
    /// <para>
    /// The evidence for choosing it is a REASONED INFERENCE AND NOT A CERTAINTY: zlib is one of the
    /// eleven upstream libraries the framework attributes
    /// [ws_objects/pfw.demos.pbl.src/w_about.srw:L119] and zlib's checksum is precisely this
    /// variant, so the closed native binary almost certainly exposed it. Byte-exact parity is
    /// verifiable only against the behavioural oracle and is not claimed here.
    /// </para>
    /// <para>
    /// It is written as a register-plus-update pair rather than as a one-shot function so that the
    /// streaming path can consume a file of any size incrementally, exactly as the framework's
    /// incremental hash objects do for the other five arms.
    /// </para>
    /// <para>
    /// THREAD SAFETY. The lookup table is computed once into a read-only field and never mutated,
    /// and every method is a pure function of its arguments, so this type holds no mutable state and
    /// is safe to use concurrently.
    /// </para>
    /// </remarks>
    private static class Crc32
    {
        /// <summary>
        /// The reflected form of the IEEE 802.3 generator polynomial.
        /// </summary>
        /// <remarks>
        /// <c>0xEDB88320</c> is the bit-reversal of the normal-form polynomial <c>0x04C11DB7</c>.
        /// The reflected form is used because the register is shifted RIGHT, which is what makes the
        /// byte-wise table lookup below correct.
        /// </remarks>
        private const uint ReflectedPolynomial = 0xEDB88320u;

        /// <summary>
        /// The number of entries in the byte-wise lookup table, which is one per possible byte
        /// value.
        /// </summary>
        private const int TableLength = 256;

        /// <summary>
        /// The register value a computation starts from and is complemented against on completion,
        /// which is all ones.
        /// </summary>
        /// <remarks>
        /// It serves both roles in this variant - the initial value and the final exclusive-or
        /// operand are both <c>0xFFFFFFFF</c> - so one member expresses both and they cannot drift
        /// apart. An all-ones start is what makes the checksum sensitive to leading zero bytes,
        /// which a zero start would not be.
        /// </remarks>
        internal const uint InitialRegister = 0xFFFFFFFFu;

        /// <summary>
        /// The precomputed byte-wise lookup table.
        /// </summary>
        /// <remarks>
        /// Computed once by the static field initializer and never written to afterwards, so it is
        /// read-only shared data rather than mutable static state. 1 KiB, which is the conventional
        /// space-for-time trade for this algorithm and the reason the update loop needs one lookup
        /// and one shift per byte instead of eight conditional shifts.
        /// </remarks>
        private static readonly uint[] Table = BuildTable();

        /// <summary>
        /// Folds <paramref name="data"/> into <paramref name="register"/> and returns the updated
        /// register.
        /// </summary>
        /// <param name="register">
        /// The running register: <see cref="InitialRegister"/> for the first chunk, then whatever
        /// the previous call returned.
        /// </param>
        /// <param name="data">The chunk to fold in. An empty span leaves the register unchanged.</param>
        /// <returns>The updated register, not yet complemented.</returns>
        /// <remarks>
        /// The register is deliberately NOT complemented here, which is what allows a caller to
        /// invoke this repeatedly over successive chunks of a stream and complement exactly once at
        /// the end. Complementing per chunk would produce a different value for the same bytes
        /// depending on how they happened to be split across reads.
        /// </remarks>
        internal static uint Update(uint register, ReadOnlySpan<byte> data)
        {
            // Hoisted so the loop does not re-read the static field on every byte.
            uint[] table = Table;
            uint current = register;

            foreach (byte value in data)
            {
                current = table[(current ^ value) & 0xFFu] ^ (current >> 8);
            }

            return current;
        }

        /// <summary>
        /// Completes a computation and returns the checksum as four bytes.
        /// </summary>
        /// <param name="register">The register returned by the final <see cref="Update"/> call.</param>
        /// <returns>
        /// The checksum, MOST SIGNIFICANT BYTE FIRST, so that the encoded form corresponds to the
        /// conventional written spelling of a CRC-32 value.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The final complement - an exclusive-or with <see cref="InitialRegister"/> - is part of the
        /// variant, not a decoration: omitting it yields a different checksum for every input, and
        /// the published vectors would all fail.
        /// </para>
        /// <para>
        /// BYTE ORDER IS A RECORDED DECISION. Nothing in the repository reveals which order the
        /// closed binary used, and a checksum is a number rather than a byte sequence, so an order
        /// had to be chosen. Most-significant-byte-first is chosen because it makes the four bytes
        /// read in the same order as the hexadecimal spelling a reader would compare against. The
        /// four bytes then go through the same output encoding as the other five arms - DECISION D4,
        /// eight Base64 characters of which the last two are padding - so callers see one consistent
        /// output convention rather than a special case for the checksum.
        /// </para>
        /// </remarks>
        internal static byte[] Finish(uint register)
        {
            uint result = register ^ InitialRegister;

            return
            [
                (byte)(result >> 24),
                (byte)(result >> 16),
                (byte)(result >> 8),
                (byte)result,
            ];
        }

        /// <summary>
        /// Builds the byte-wise lookup table from <see cref="ReflectedPolynomial"/>.
        /// </summary>
        /// <returns>A table of <see cref="TableLength"/> entries, one per byte value.</returns>
        /// <remarks>
        /// Each entry is the register state after folding in a single byte from a zero register, so
        /// the eight-iteration inner loop is exactly the bit-at-a-time form of the algorithm,
        /// evaluated once per byte value here instead of once per input byte at run time. The table
        /// is generated rather than written out as a literal so that it is derived from the stated
        /// polynomial and cannot silently disagree with it.
        /// </remarks>
        private static uint[] BuildTable()
        {
            uint[] table = new uint[TableLength];

            for (uint index = 0; index < TableLength; index++)
            {
                uint register = index;

                for (int bit = 0; bit < 8; bit++)
                {
                    // Shift right because the polynomial is in reflected form; fold the polynomial
                    // in only when the bit leaving the register is set.
                    register = (register & 1u) != 0u
                        ? ReflectedPolynomial ^ (register >> 1)
                        : register >> 1;
                }

                table[index] = register;
            }

            return table;
        }
    }

    #endregion
}
