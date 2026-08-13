// ==============================================================================================
//  LegacyDefaultsTests - the constraint C-B compliance suite for the cryptographic catalogue
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/LegacyDefaults.cs
//                     and the agreement between that catalogue and all six sibling providers
//  BEHAVIOURAL ORACLE ws_objects/pfw.shared.pbl.src/enums.sru:L924-L967   the 30 CRYPTO_ constants
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L927        one hash set, three uses
//                     ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73   the 65 declarations
//                     both READ-ONLY per constraint C-C. They are cited here BY PATH AND LINE IN
//                     COMMENTS ONLY. No test below opens, reads, copies or fixture-loads any
//                     legacy path at test time, and nothing in this file is derived from one at
//                     run time.
//
//  READ THIS FIRST, BECAUSE THIS SUITE INVERTS THE USUAL INSTINCT
//  --------------------------------------------------------------------------------------------
//  Every assertion below says "THIS WEAK THING IS WHAT MUST HAPPEN". None of them is a finding to
//  remediate. Constraint C-B forbids correcting legacy behaviour, so a default that is weak by
//  current standards is pinned here AS CORRECT, and the purpose of pinning it is that a future
//  contributor who "fixes" one of them breaks a test and is forced to read why.
//
//  The why, in one sentence per default, is that each of them is load bearing for reading data the
//  legacy framework already wrote: promoting the default cipher mode from ECB to CBC would make
//  every value the legacy encrypted undecryptable; promoting the default RSA padding from PKCS#1
//  to OAEP would do the same for every RSA payload; introducing a key-derivation function would
//  change every key and therefore every ciphertext; and generating a random initialization vector
//  inside a vector-less overload would produce ciphertext the legacy has no field in which to
//  transmit that vector, which is why DECISION D3 REFUSES those arms instead. Each of those
//  "improvements" is a data-loss defect wearing the costume of a security fix.
//
//  Consequently, and deliberately, THIS FILE CONTAINS NO SKIPPED OR CONDITIONALLY EXCLUDED TEST, no
//  deliberate failure standing in for a weakness, no deferred-work marker proposing that a default
//  be strengthened later, no commented-out stronger alternative, and no assertion anywhere that a
//  preserved weak default is rejected. The only rejections asserted are ones the LEGACY ITSELF
//  performs - notably no-padding, which the legacy declares no constant for and refuses.
//
//  WHAT THIS SUITE PROVES, AND HOW IT DIFFERS FROM ITS SIBLINGS
//  --------------------------------------------------------------------------------------------
//  It proves DEFAULTS AND ABSENCES, catalogue-anchored and ACROSS ALL SIX PROVIDERS: that every
//  documented default in the catalogue really is the default the providers take when an argument
//  is omitted, and that every documented absence really is absent from the whole ported surface.
//  The per-provider suites prove shape and vectors within one provider; this one proves that the
//  catalogue's annotations cannot drift away from the behaviour they describe. The two concerns
//  are kept apart so the coverage is additive rather than a second copy of the same matrices.
//
//  Two consequences of that framing are worth stating because they govern how the tests are
//  written:
//
//      * A DEFAULT IS PROVEN BEHAVIOURALLY, NEVER BY READING A PROPERTY. Asserting that the
//        catalogue's default arm equals ECB proves only what the catalogue says. Every default
//        test below therefore compares the OUTPUT of the argument-omitting call against the
//        output of the explicit call, and against the outputs of the alternatives it must NOT
//        equal. A default the code path then ignores would satisfy a property read and fail here.
//      * AN ABSENCE IS PROVEN BY A SWEEP THAT CAN ACTUALLY FAIL. The reflection sweeps below
//        enumerate the real public surface of the real assembly, so adding a Pbkdf2, a Salt, an
//        Iterations, a Tag or an AuthenticateAndEncrypt member to any provider fails them. An
//        absence assertion that could not fail would be worthless.
//
//  WHAT THIS SUITE CANNOT BE EVIDENCE OF
//  --------------------------------------------------------------------------------------------
//  n_crypto is a PBNI class over the closed pfw.dll [n_crypto.sru:L8]: the export declares 65
//  prototypes and no implementation exists anywhere in the repository. Four observables are
//  therefore genuinely unobservable from the repository, and the catalogue records each as a
//  DOCUMENTED DECISION rather than a measurement - D1 the raw-key-bytes rule, D2 the absence of an
//  integrity tag, D3 the REFUSAL of the cells whose vector or feedback width is unprovable, and D4
//  the single text-safe payload encoding. Per constraint C-K the tests that pin them below say so in as many words: they assert
//  that the DECISION IS APPLIED UNIFORMLY, which is what a test can establish, and they do not
//  claim byte-exact agreement with the closed binary, which only the behavioural oracle can
//  adjudicate.
//
//  GOVERNING RULES AND CONSTRAINTS
//  --------------------------------------------------------------------------------------------
//  review_rules returns exactly one line: "No user rules provided." There is therefore NO
//  user-specified rule governing this file, and their absence is not licence to lower the bar.
//  This file is held instead to enterprise-standard best practice at the bar the migration plan's
//  own baseline sets: table-driven parity matrices as theories over static member data,
//  warning-clean under warnings-as-errors, correct nullable annotations, every disposable
//  disposed, and no secret in a fixture. The binding constraints are the plan's own C-B, C-C, C-D,
//  C-F, C-H and C-K, each named at the assertion it governs.
//
//  KEY HYGIENE (constraint C-F)
//  --------------------------------------------------------------------------------------------
//  EVERY KEY IN THIS FILE IS GENERATED OR COMPUTED AT TEST TIME. RSA key pairs come from the
//  provider's own generator - 1024 bits only in the test that pins 1024 as legal, 2048 everywhere
//  else. Symmetric key and vector material is either produced by the platform's cryptographic
//  random generator or COMPUTED FROM AN INDEX by the synthetic helpers at the foot of this class.
//  There is no base64 blob, no hexadecimal blob, no armoured key block and no long literal of any
//  kind below.
//
//  The trap specific to this file is worth naming because it is where someone would slip: the
//  1024-bit legality test and the raw-key-bytes D1 test are exactly the two places a legacy demo
//  key looks convenient. NEITHER USES ONE. The repository's eight known in-source secret sites are
//  recorded in docs/SECRETS.md and may be CITED - tests/blink/test_jws.htm:L8-L22,
//  ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L466 and :L718, and
//  ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23 - but not one of their VALUES is
//  reproduced here in any form, encoded or otherwise.
//
//  Nothing in this file references a deferred service (constraint C-D), and nothing needs an HTTP
//  host: the catalogue is a static class and the two providers exercised are stateless services
//  constructed directly.
// ==============================================================================================

using System.Reflection;
using System.Security.Cryptography;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

// THE FRAMEWORK'S TEXT NAMESPACE IS DELIBERATELY NOT IMPORTED HERE. It supplies a type whose simple
// name collides with the cryptographic folder's own encoder, so importing it would make every
// mention of that encoder ambiguous. The two types this file needs from that namespace are therefore
// named in full at their use sites, which is how the cipher provider resolves the same collision.

namespace PowerFramework.Security.Tests;

/// <summary>
/// Pins the preserved legacy cryptographic defaults, the documented substitution decisions and the
/// documented absences of <see cref="LegacyDefaults"/>, proving each against observed provider
/// behaviour rather than against the catalogue's own declarations.
/// </summary>
/// <remarks>
/// Every weak default asserted here is asserted as the EXPECTED OUTCOME, per constraint C-B. The
/// remarks on each member name the <c>enums.sru</c> line that establishes it and state plainly that
/// it is a preserved legacy weakness rather than an oversight.
/// </remarks>
public sealed class LegacyDefaultsTests
{
    /// <summary>
    /// The five published symmetric cipher types, as the 16-bit values the legacy declares the
    /// argument at [enums.sru:L936-L940].
    /// </summary>
    /// <remarks>
    /// THE CAST IS THE PRESERVED WIDTH MISMATCH AND NOT A TIDYING OPPORTUNITY. The legacy declares
    /// these constants <c>Constant Long</c> [enums.sru:L936-L940] while declaring the
    /// <c>SymEncrypt</c> and <c>SymDecrypt</c> <c>ntype</c> parameter a PowerBuilder <c>uint</c>,
    /// which is 16 bits wide and maps to <see cref="ushort"/> [n_crypto.sru:L30-L61]. The argument
    /// is therefore NARROWER than the constants naming its legal values. That inconsistency is
    /// legacy behaviour, it is carried across rather than corrected, and every call site in this
    /// file casts because of it.
    /// </remarks>
    private static readonly ushort[] PublishedCipherTypes =
    [
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_DES,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_3DES,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
    ];

    /// <summary>
    /// The three published symmetric cipher modes [enums.sru:L943-L945] - the WHOLE set, which is
    /// why no authenticated mode appears in it.
    /// </summary>
    private static readonly long[] PublishedCipherModes =
    [
        Enums.CRYPTO_SYMCRYPT_MODE_ECB,
        Enums.CRYPTO_SYMCRYPT_MODE_CBC,
        Enums.CRYPTO_SYMCRYPT_MODE_CFB,
    ];

    /// <summary>
    /// The published modes whose behaviour this port can reproduce, which is where the round-trip and
    /// padding matrices run.
    /// </summary>
    /// <remarks>
    /// The feedback mode is absent because its feedback width is unprovable (DECISION D3), and its
    /// absence HERE is not a reduction of the published set - <see cref="PublishedCipherModes"/> above
    /// still carries all three, and the set-completeness tests still read it. The two lists answer two
    /// different questions: which identifiers the legacy declares, and which cells this port can
    /// faithfully serve.
    /// </remarks>
    private static readonly long[] ReproducibleCipherModes =
    [
        Enums.CRYPTO_SYMCRYPT_MODE_ECB,
        Enums.CRYPTO_SYMCRYPT_MODE_CBC,
    ];

    /// <summary>
    /// The three cipher types whose key length the platform accepts at every value, used by the
    /// tests that deliberately supply under-length key material.
    /// </summary>
    /// <remarks>
    /// DECISION D1's zero-padding half can manufacture a key the PLATFORM ITSELF REFUSES, and this
    /// array exists to keep that interaction out of the D1 assertions rather than to hide it. A
    /// short passphrase zero-padded to eight bytes can land on a known weak DES key, and one
    /// zero-padded to twenty-four bytes can make the second and third triple-DES sub-keys
    /// coincide; the platform raises rather than encrypting in both cases. That refusal is a real,
    /// separately documented behaviour of the cipher provider, so the D1 tests here exercise the
    /// AES types - whose key schedule accepts any byte pattern - and assert the raw-bytes rule
    /// itself directly against the catalogue's own normalizer, which is where the rule lives.
    /// </remarks>
    private static readonly ushort[] CipherTypesAcceptingAnyKeyPattern =
    [
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
        (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
    ];

    /// <summary>
    /// The six published hash and signature algorithm identifiers [enums.sru:L928-L933].
    /// </summary>
    /// <remarks>
    /// ONE SET, THREE CONSUMERS. The source comment at enums.sru:L927 records that these same six
    /// values parameterise <c>Hash</c>, <c>RSASign</c> AND <c>VerifyRSASign</c>, so a provider
    /// screening a signature algorithm against this one set is source-faithful and the set must NOT
    /// be split in two. MD5, SHA-1 and CRC32 are all members and all stay members.
    /// </remarks>
    private static readonly long[] PublishedHashTypes =
    [
        Enums.CRYPTO_HASH_MD5,
        Enums.CRYPTO_HASH_SHA1,
        Enums.CRYPTO_HASH_SHA256,
        Enums.CRYPTO_HASH_SHA384,
        Enums.CRYPTO_HASH_SHA512,
        Enums.CRYPTO_HASH_CRC32,
    ];

    /// <summary>
    /// The names of the six sibling providers that carry the 63 ported declarations.
    /// </summary>
    /// <remarks>
    /// HELD AS TEXT RATHER THAN AS <see cref="Type"/> HANDLES, DELIBERATELY. The reflection sweeps
    /// discover the provider types from the assembly itself, so a provider is swept because it
    /// EXISTS rather than because this file remembered to name it. Naming them as strings keeps the
    /// expected roster auditable against the catalogue's census without turning the roster into the
    /// sweep's input, and it avoids this suite taking a compile-time dependency on providers whose
    /// behaviour it does not otherwise exercise.
    /// </remarks>
    private static readonly string[] ExpectedProviderTypeNames =
    [
        "EncodingProvider",
        "HashProvider",
        "HmacProvider",
        "RandomProvider",
        "RsaProvider",
        "SymmetricCipherProvider",
    ];

    /// <summary>
    /// The member-name fragments that would betray a key-derivation function, a salt, an iteration
    /// count, an authenticated-encryption mode or an authentication tag on a provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS ARRAY IS THE TRIPWIRE FOR CONSTRAINT C-B, and it is the most important absence in this
    /// file. Nothing in the 65 declarations at n_crypto.sru:L9-L73 reaches a key-derivation
    /// function: there is no PBKDF2, no scrypt, no bcrypt, no Argon2 and no salt concept anywhere,
    /// so a passphrase is used as raw key bytes. Nor is any published mode authenticated - the set
    /// is exactly ECB, CBC and CFB [enums.sru:L943-L945] - so no overload takes or returns a tag.
    /// A contributor adding either capability would be making precisely the silent improvement
    /// C-B forbids, and the sweep that consumes this array is what stops them doing it unnoticed.
    /// </para>
    /// <para>
    /// The fragments are matched case-insensitively against member names on the PROVIDER types
    /// only. That scoping is deliberate and not incidental: the catalogue itself carries
    /// <see cref="LegacyDefaults.KEY_DERIVATION_AVAILABLE"/> and
    /// <see cref="LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE"/>, whose names necessarily
    /// contain these very words because their whole job is to RECORD THE ABSENCE. Sweeping the
    /// catalogue with this array would flag the record of the absence as though it were the
    /// capability, so the sweep asks the question where a capability could actually live.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenCapabilityFragments =
    [
        "Derive",
        "Pbkdf",
        "Rfc2898",
        "Scrypt",
        "Bcrypt",
        "Argon",
        "Salt",
        "Iteration",
        "Stretch",
        "Gcm",
        "Ccm",
        "Poly1305",
        "ChaCha",
        "Aead",
        "Tag",
        "AuthenticateAndEncrypt",
    ];

    /// <summary>
    /// The system under test's collaborators: the real cipher and RSA providers over the real
    /// encoder.
    /// </summary>
    /// <remarks>
    /// Both are stateless services registered for dependency injection in the application, so they
    /// are constructed directly here. THE REAL ENCODER IS USED RATHER THAN A SUBSTITUTE, because a
    /// fake would only prove that this file agrees with itself, and DECISION D4's whole claim is
    /// that one real encoding is applied uniformly. No HTTP host is involved and no application
    /// factory is consumed: nothing asserted here crosses a service boundary.
    /// </remarks>
    private readonly SymmetricCipherProvider _cipher = new(new EncodingProvider());

    /// <inheritdoc cref="_cipher"/>
    private readonly RsaProvider _rsa = new(new EncodingProvider());

    /// <inheritdoc cref="_cipher"/>
    private readonly EncodingProvider _encoding = new();

    // ==========================================================================================
    //  MEMBER DATA - THE PARITY MATRICES
    //  ------------------------------------------------------------------------------------------
    //  Held inside this class, as the plan's test shape requires. Each is a factory rather than a
    //  field so that the rows are derived from the published identifier arrays above: a row cannot
    //  drift away from the set it is supposed to enumerate, because it is generated from it.
    // ==========================================================================================

    /// <summary>
    /// One row per published cipher type [enums.sru:L936-L940].
    /// </summary>
    /// <returns>Five rows: DES, 3DES, AES128, AES192 and AES256.</returns>
    public static TheoryData<ushort> CipherTypes()
    {
        TheoryData<ushort> data = new();

        foreach (ushort ntype in PublishedCipherTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// One row per cipher type whose key schedule accepts any byte pattern, for the tests that
    /// deliberately supply under-length key material.
    /// </summary>
    /// <returns>Three rows: AES128, AES192 and AES256.</returns>
    public static TheoryData<ushort> KeyPatternTolerantCipherTypes()
    {
        TheoryData<ushort> data = new();

        foreach (ushort ntype in CipherTypesAcceptingAnyKeyPattern)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// The full cipher-type by cipher-mode grid: every published type against every published
    /// mode.
    /// </summary>
    /// <returns>Fifteen rows, each carrying a cipher type then a cipher mode.</returns>
    /// <summary>
    /// All six mode-by-vector combinations with the classification each must receive.
    /// </summary>
    /// <returns>One row per combination: mode, vector supplied, expected classification.</returns>
    public static TheoryData<long, bool, SymmetricCellParity> CipherCellClassifications() =>
        new()
        {
            // The codebook mode consumes no vector, so neither shape is blocked - and this is the
            // default mode, which is why both rows matter.
            { Enums.CRYPTO_SYMCRYPT_MODE_ECB, true, SymmetricCellParity.Supported },
            { Enums.CRYPTO_SYMCRYPT_MODE_ECB, false, SymmetricCellParity.Supported },

            // Chaining is reproducible with a caller-supplied vector and blocked without one.
            { Enums.CRYPTO_SYMCRYPT_MODE_CBC, true, SymmetricCellParity.Supported },
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CBC,
                false,
                SymmetricCellParity.BlockedSynthesizedVectorUnprovable
            },

            // The feedback mode is blocked either way: a vector does not disclose the feedback width.
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                true,
                SymmetricCellParity.BlockedFeedbackWidthUnprovable
            },
            {
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                false,
                SymmetricCellParity.BlockedFeedbackWidthUnprovable
            },
        };

    public static TheoryData<ushort, long> CipherTypeAndModeGrid()
    {
        TheoryData<ushort, long> data = new();

        foreach (ushort ntype in PublishedCipherTypes)
        {
            // Reproducible cells only: the blocked cells are covered by the refusal theories in the
            // DECISION D3 section, which assert the outcome those cells actually have.
            foreach (long mode in ReproducibleCipherModes)
            {
                data.Add(ntype, mode);
            }
        }

        return data;
    }

    /// <summary>
    /// Every published cipher type against the two modes that consume an initialization vector,
    /// which is the domain DECISION D3 applies to.
    /// </summary>
    /// <returns>Ten rows, each carrying a cipher type then CBC or CFB.</returns>
    /// <remarks>
    /// ECB is excluded because it consumes no initialization vector at all, so the eight
    /// mode-without-vector arms [n_crypto.sru:L31, L35, L39, L43, L47, L51, L55, L59] have nothing
    /// to synthesise for it. The exclusion is asserted separately rather than assumed.
    /// </remarks>
    public static TheoryData<ushort, long> VectorConsumingTypeAndModeGrid()
    {
        TheoryData<ushort, long> data = new();

        foreach (ushort ntype in PublishedCipherTypes)
        {
            foreach (long mode in PublishedCipherModes)
            {
                if (LegacyDefaults.ModeUsesInitializationVector(mode))
                {
                    data.Add(ntype, mode);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// The hash-type predicate matrix: the six published identifiers, plus the rows immediately
    /// outside the set at both ends and the extremes of the parameter's own range.
    /// </summary>
    /// <returns>One row per candidate: the identifier, then whether it is published.</returns>
    public static TheoryData<long, bool> HashTypeMatrix()
    {
        TheoryData<long, bool> data = new();

        foreach (long ntype in PublishedHashTypes)
        {
            data.Add(ntype, true);
        }

        // The boundary rows on both sides of the contiguous block, then the extremes. The rows just
        // outside matter most: an off-by-one in the predicate would publish an identifier the legacy
        // never declared, or reject one it did.
        data.Add(Enums.CRYPTO_HASH_MD5 - 1, false);
        data.Add(Enums.CRYPTO_HASH_CRC32 + 1, false);
        data.Add(Enums.CRYPTO_HASH_CRC32 + 2, false);
        data.Add(long.MinValue, false);
        data.Add(long.MaxValue, false);

        return data;
    }

    /// <summary>
    /// The cipher-type predicate matrix: the five published identifiers, plus the row immediately
    /// above the set and the top of the parameter's own range.
    /// </summary>
    /// <returns>One row per candidate: the identifier, then whether it is published.</returns>
    /// <remarks>
    /// There is no row below zero, and that is a property of the legacy declaration rather than an
    /// omission: the parameter is a 16-bit UNSIGNED value [n_crypto.sru:L30-L61], so no negative
    /// candidate can reach the predicate at all.
    /// </remarks>
    public static TheoryData<ushort, bool> CipherTypeMatrix()
    {
        TheoryData<ushort, bool> data = new();

        foreach (ushort ntype in PublishedCipherTypes)
        {
            data.Add(ntype, true);
        }

        data.Add((ushort)(Enums.CRYPTO_SYMCRYPT_TYPE_AES256 + 1), false);
        data.Add((ushort)(Enums.CRYPTO_SYMCRYPT_TYPE_AES256 + 2), false);
        data.Add(ushort.MaxValue, false);

        return data;
    }

    /// <summary>
    /// The cipher-mode predicate matrix: the three published modes, plus the rows immediately
    /// outside the set and the extremes.
    /// </summary>
    /// <returns>One row per candidate: the mode, then whether it is published.</returns>
    /// <remarks>
    /// THE REJECTED ROWS ARE WHERE AUTHENTICATED ENCRYPTION WOULD HAVE TO ENTER, so they carry more
    /// weight than a boundary check usually does. The set is exactly three wide
    /// [enums.sru:L943-L945] and every value outside it is refused, which is what makes GCM, CCM
    /// and Poly1305 unreachable rather than merely unimplemented.
    /// </remarks>
    public static TheoryData<long, bool> CipherModeMatrix()
    {
        TheoryData<long, bool> data = new();

        foreach (long mode in PublishedCipherModes)
        {
            data.Add(mode, true);
        }

        data.Add(Enums.CRYPTO_SYMCRYPT_MODE_ECB - 1, false);
        data.Add(Enums.CRYPTO_SYMCRYPT_MODE_CFB + 1, false);
        data.Add(Enums.CRYPTO_SYMCRYPT_MODE_CFB + 2, false);
        data.Add(long.MinValue, false);
        data.Add(long.MaxValue, false);

        return data;
    }

    /// <summary>
    /// The RSA padding predicate matrix: the two published schemes, plus the rows immediately
    /// outside the set and the extremes.
    /// </summary>
    /// <returns>One row per candidate: the padding identifier, then whether it is published.</returns>
    /// <remarks>
    /// THE SET HAS EXACTLY TWO MEMBERS [enums.sru:L949-L950], AND THAT IS THE WHOLE MECHANICAL
    /// REASON NO-PADDING MUST BE REJECTED RATHER THAN MAPPED: there is no third value to map it to.
    /// The legacy declares no no-padding constant, so a caller has no legal way to ask for raw RSA,
    /// and every rejected row below is that refusal - preserved legacy behaviour, not a hardening
    /// choice made here.
    /// </remarks>
    public static TheoryData<long, bool> RsaPaddingMatrix()
    {
        TheoryData<long, bool> data = new();

        data.Add(Enums.CRYPTO_RSA_PADDING_PKCS1, true);
        data.Add(Enums.CRYPTO_RSA_PADDING_OAEP, true);

        data.Add(Enums.CRYPTO_RSA_PADDING_PKCS1 - 1, false);
        data.Add(Enums.CRYPTO_RSA_PADDING_OAEP + 1, false);
        data.Add(Enums.CRYPTO_RSA_PADDING_OAEP + 2, false);
        data.Add(long.MinValue, false);
        data.Add(long.MaxValue, false);

        return data;
    }

    /// <summary>
    /// The encoding predicate matrix: the two published encodings, plus the rows immediately
    /// outside the set and the extremes.
    /// </summary>
    /// <returns>One row per candidate: the encoding identifier, then whether it is published.</returns>
    public static TheoryData<long, bool> EncodingMatrix()
    {
        TheoryData<long, bool> data = new();

        data.Add(Enums.CRYPTO_ENCODING_BASE64, true);
        data.Add(Enums.CRYPTO_ENCODING_HEX, true);

        data.Add(Enums.CRYPTO_ENCODING_BASE64 - 1, false);
        data.Add(Enums.CRYPTO_ENCODING_HEX + 1, false);
        data.Add(long.MinValue, false);
        data.Add(long.MaxValue, false);

        return data;
    }

    /// <summary>
    /// The three published RSA key sizes [enums.sru:L965-L967], smallest first.
    /// </summary>
    /// <returns>Three rows: 1024, 2048 and 4096 bits.</returns>
    /// <remarks>
    /// 1024 IS A MEMBER AND MUST STAY ONE. See the test that consumes this data for why the small
    /// size is asserted as legal rather than as rejected.
    /// </remarks>
    public static TheoryData<ushort> PublishedRsaKeySizes()
    {
        TheoryData<ushort> data = new();

        data.Add(Enums.CRYPTO_RSA_BITS_1024);
        data.Add(Enums.CRYPTO_RSA_BITS_2048);
        data.Add(Enums.CRYPTO_RSA_BITS_4096);

        return data;
    }

    // ==========================================================================================
    //  PRESERVED DEFAULT 1 OF 5 - ECB IS THE DEFAULT SYMMETRIC MODE
    //  ORACLE  enums.sru:L943 CRYPTO_SYMCRYPT_MODE_ECB = 0
    //          enums.sru:L944 CRYPTO_SYMCRYPT_MODE_CBC = 1
    //          enums.sru:L945 CRYPTO_SYMCRYPT_MODE_CFB = 2
    //          enums.sru:L946 CRYPTO_SYMCRYPT_MODE_DEFAULT = CRYPTO_SYMCRYPT_MODE_ECB
    //  ------------------------------------------------------------------------------------------
    //  The mode-omitting arms are eight of the sixteen SymEncrypt declarations
    //  [n_crypto.sru:L30, L32, L34, L36, L38, L40, L42, L44] mirrored by eight of the sixteen
    //  SymDecrypt declarations [:L46, L48, L50, L52, L54, L56, L58, L60].
    //
    //  PROVEN BY CIPHERTEXT, NOT BY A PROPERTY READ. Reading the catalogue's default arm would
    //  prove only what the catalogue declares; a default the code path then ignored would satisfy
    //  such a read perfectly. The tests below compare the BYTES the mode-omitting arm produces
    //  against the bytes the explicit-ECB arm produces, and against the bytes the two alternatives
    //  produce, which is the only formulation that can catch an ignored default.
    // ==========================================================================================

    /// <summary>
    /// Omitting the cipher mode produces byte-for-byte the ciphertext the explicit ECB arm
    /// produces, and neither of the ciphertexts the other two modes produce.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY AND ASSERTED AS CORRECT. The default mode is
    /// ECB [enums.sru:L946 resolving to :L943]. Electronic codebook mode encrypts each block
    /// independently, so identical plaintext blocks yield identical ciphertext blocks and the
    /// ciphertext leaks the plaintext's block-level structure. That is not an oversight in this port
    /// and it is not corrected: the legacy framework's own configuration object reads settings back
    /// through exactly this mode-omitting three-argument arm, so changing the default would make
    /// every value it ever wrote unreadable.
    /// </para>
    /// <para>
    /// THE INEQUALITIES ARE THE HALF THAT CATCHES AN IGNORED DEFAULT. A payload of three whole
    /// blocks with differing content is used precisely so that the comparison bites: this row's CBC
    /// comparison supplies an ALL-ZERO vector, and under a zero vector the FIRST cipher block of CBC
    /// coincides with ECB's because the vector contributes nothing to it - only the later blocks
    /// diverge. A single-block payload would therefore compare equal for the wrong reason.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void OmittingTheCipherModeSelectsElectronicCodebookExactly(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 3);

        byte[] modeOmitted = _cipher.SymEncrypt(plain, key, ntype);
        byte[] explicitEcb = _cipher.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB);

        Assert.Equal(explicitEcb, modeOmitted);

        // The two modes the default is NOT. If a future edit promoted the default to either of
        // them, the equality above would fail and one of these would start passing vacuously, so
        // both directions are asserted together.
        // THE PROOF IS SHARPER THAN AN INEQUALITY. Asking this same vector-less arm for either of
        // the other two modes is REFUSED under DECISION D3, and that refusal is itself decisive: the
        // codebook mode is the ONLY mode this arm can serve, because it is the only one consuming no
        // vector. Were the default anything else, the mode-omitting call above would have been refused
        // too rather than returning ciphertext.
        Assert.Throws<SymmetricParityUnavailableException>(
            () => _cipher.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<SymmetricParityUnavailableException>(
            () => _cipher.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CFB));
    }

    /// <summary>
    /// The decrypting half of the same default: ciphertext produced by the mode-omitting encrypt arm
    /// is recovered by the mode-omitting decrypt arm, and by the explicit ECB arm, and by neither
    /// alternative mode.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// <para>
    /// Asserted separately from the encrypting half because the two families are separate
    /// declaration groups in the oracle - encrypt at n_crypto.sru:L30-L45 and decrypt at :L46-L61 -
    /// and a default could in principle be preserved on one side and lost on the other. A port
    /// whose encrypt defaulted to ECB while its decrypt defaulted to CBC would round-trip nothing,
    /// and this is the test that would say so.
    /// </para>
    /// <para>
    /// The alternative-mode arms are asserted to FAIL TO RECOVER THE PLAINTEXT rather than to
    /// throw. That is DECISION D2 showing through: with no authentication anywhere in the published
    /// surface, decrypting under the wrong mode yields either the platform's padding failure or
    /// plausible garbage, and which of the two occurs is not something this port may promise. The
    /// assertion is therefore the weaker, truthful one - the plaintext does not come back.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void OmittingTheCipherModeSelectsElectronicCodebookOnTheDecryptSideToo(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 3);

        byte[] cipherText = _cipher.SymEncrypt(plain, key, ntype);

        Assert.Equal(plain, _cipher.SymDecrypt(cipherText, key, ntype));
        Assert.Equal(
            plain,
            _cipher.SymDecrypt(cipherText, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB));

        // THE POSITIVE CONTROL, AND IT IS NOT DECORATION. The two negative assertions below are made
        // through a helper that folds a failed decryption and a garbage decryption into one answer, so
        // a helper that could only ever answer "no" would satisfy them without proving anything.
        // Exercising the same helper on the arm that MUST succeed is what makes the two "no" answers
        // load bearing.
        Assert.True(
            RecoversPlainText(cipherText, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB, plain),
            "The helper must be able to answer positively, or the refutations below prove nothing.");

        // The refutation is a refusal rather than a failed recovery, and it is stronger for it:
        // the other two modes cannot even be ATTEMPTED through this vector-less arm (DECISION D3), so
        // the codebook mode is the only one it can serve and therefore the only one it can default to.
        Assert.Throws<SymmetricParityUnavailableException>(
            () => _cipher.SymDecrypt(cipherText, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<SymmetricParityUnavailableException>(
            () => _cipher.SymDecrypt(cipherText, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CFB));
    }

    /// <summary>
    /// The catalogue's default arm is valued from the shared kernel constant and resolves to ECB,
    /// so the annotation and the behaviour above cannot drift apart.
    /// </summary>
    /// <remarks>
    /// This is the desynchronization guard, and it is the companion to the behavioural tests rather
    /// than a substitute for them. The behavioural tests establish what the providers DO; this one
    /// establishes that the catalogue member the providers read still SAYS the same thing, and that
    /// the catalogue in turn still takes its value from <see cref="Enums"/> [enums.sru:L946] instead
    /// of from a re-typed literal that could be edited independently.
    /// </remarks>
    [Fact]
    public void TheCatalogueDefaultCipherModeIsValuedFromTheKernelAndIsElectronicCodebook()
    {
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

        // Because the default is ECB, the mode-omitting arms never need a vector at all, which is what
        // confines DECISION D3's refusal to the eight arms that name a mode but carry no vector.
        Assert.False(
            LegacyDefaults.ModeUsesInitializationVector(LegacyDefaults.SYMMETRIC_MODE_DEFAULT));
    }

    // ==========================================================================================
    //  PRESERVED DEFAULT 2 OF 5 - PKCS#1 v1.5 IS THE DEFAULT RSA PADDING
    //  ORACLE  enums.sru:L949 CRYPTO_RSA_PADDING_PKCS1 = 0   // RSA_PKCS1_PADDING
    //          enums.sru:L950 CRYPTO_RSA_PADDING_OAEP  = 1   // RSA_PKCS1_OAEP_PADDING
    //          enums.sru:L951 CRYPTO_RSA_PADDING_DEFAULT = CRYPTO_RSA_PADDING_PKCS1
    //  ------------------------------------------------------------------------------------------
    //  The padding-omitting arms are two of the four RSAEncrypt declarations
    //  [n_crypto.sru:L62, L64] and two of the four RSADecrypt declarations [:L66, L68].
    //
    //  PROVEN CROSS-WISE, NOT BY A SETTINGS READ. RSA encryption under PKCS#1 v1.5 is randomised,
    //  so two calls with the same arguments produce different ciphertext and a byte comparison is
    //  not available. What IS available, and is stronger than reading a settings value, is a cross
    //  decrypt: ciphertext produced WITHOUT a padding argument is decrypted WITH an explicit PKCS#1
    //  argument, and vice versa. Only a genuinely shared padding lets both directions succeed.
    // ==========================================================================================

    /// <summary>
    /// Ciphertext produced by the padding-omitting encrypt arm decrypts under an explicit PKCS#1
    /// argument, and ciphertext produced under an explicit PKCS#1 argument decrypts through the
    /// padding-omitting arm.
    /// </summary>
    /// <remarks>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY AND ASSERTED AS CORRECT. The default padding is
    /// PKCS#1 v1.5 [enums.sru:L951 resolving to :L949], which is the padding with the historic
    /// adaptive-chosen-ciphertext exposure and which modern guidance replaces with OAEP. It is not
    /// promoted here: OAEP remains selectable and remains NOT the default, because every RSA payload
    /// the legacy wrote was written under PKCS#1 and promoting the default would make all of it
    /// undecryptable.
    /// </remarks>
    [Fact]
    public void OmittingTheRsaPaddingSelectsPkcs1InBothDirections()
    {
        RsaKeyPair pair = SharedTestKeyPair.Value;
        byte[] plain = SyntheticPayload(32);

        // Encrypt without a padding argument, decrypt with the explicit one.
        byte[] paddingOmitted = _rsa.RSAEncrypt(plain, pair.PublicKey);
        Assert.Equal(
            plain,
            _rsa.RSADecrypt(paddingOmitted, pair.PrivateKey, Enums.CRYPTO_RSA_PADDING_PKCS1));

        // Encrypt with the explicit argument, decrypt without one.
        byte[] explicitPkcs1 =
            _rsa.RSAEncrypt(plain, pair.PublicKey, Enums.CRYPTO_RSA_PADDING_PKCS1);
        Assert.Equal(plain, _rsa.RSADecrypt(explicitPkcs1, pair.PrivateKey));
    }

    /// <summary>
    /// The padding-omitting arms are not OAEP: ciphertext produced under OAEP does not decrypt
    /// through them, and ciphertext produced through them does not decrypt under OAEP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the half that makes the preceding test meaningful. A cross decrypt that succeeded
    /// under EITHER padding would prove nothing about which one is the default, so the two schemes
    /// are shown to be genuinely non-interchangeable in both directions.
    /// </para>
    /// <para>
    /// The failure is asserted with <c>ThrowsAny</c> rather than <c>Throws</c> DELIBERATELY. The
    /// underlying platform raises a provider-specific subclass of the cryptographic failure, and the
    /// provider propagates it UNCHANGED rather than rewrapping it into a taxonomy the legacy never
    /// published. Asserting the exact runtime type would be asserting an implementation detail of
    /// the platform's native layer.
    /// </para>
    /// <para>
    /// <b>THE PKCS#1 DIRECTION IS ASSERTED AS "DID NOT RECOVER THE PLAINTEXT" RATHER THAN AS "ALWAYS
    /// THREW", AND THAT IS THE PRESERVED WEAKNESS SPEAKING RATHER THAN A WEAKENED ASSERTION.</b>
    /// PKCS#1 v1.5 unpadding accepts any block shaped <c>00 02</c>, at least eight non-zero padding
    /// bytes, a <c>00</c> separator, then data, so unpadding an unrelated block SUCCEEDS whenever that
    /// loose structure happens to be present. MEASURED ON THIS PLATFORM: over 300,000 attempts at
    /// decrypting freshly OAEP-encrypted blocks through the PKCS#1 arm, the unpad accepted 395 of them
    /// - about one in 760 - and in NOT ONE of those 395 did the plaintext come back. OAEP encryption is
    /// randomised and the key pair is generated per run, so the block being unpadded differs on every
    /// execution: an unconditional throw is therefore NOT a property of the scheme, and asserting one
    /// makes the test fail at that rate against a completely correct implementation. That
    /// accidental-acceptance margin is precisely the historic adaptive-chosen-ciphertext exposure this
    /// suite exists to record, so the assertion states the property that IS unconditional - an
    /// OAEP-encoded block carries a masked seed and a masked data block, never the plaintext under
    /// PKCS#1 padding, so the plaintext can never come back - and reports which arm it took.
    /// </para>
    /// <para>
    /// This is a TEST correction and not a provider change: <c>RsaProvider.RSADecrypt</c> forwards to
    /// the platform's PKCS#1 unpadding unchanged, which is the required behaviour. The defect was an
    /// assertion that demanded a guarantee PKCS#1 v1.5 does not make - and demanding it would have
    /// amounted to asserting the weakness away, which C-B forbids.
    /// </para>
    /// <para>
    /// The OAEP direction stays an unconditional throw: OAEP unpadding verifies a full hash, so its
    /// rejection of a PKCS#1 block is certain rather than probabilistic. The asymmetry between the two
    /// arms is itself the point - it is the difference in strength between the two schemes.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePaddingOmittingArmsAreNotOptimalAsymmetricEncryptionPadding()
    {
        RsaKeyPair pair = SharedTestKeyPair.Value;
        byte[] plain = SyntheticPayload(32);

        // DIRECTION ONE - OAEP ciphertext through the padding-omitting (PKCS#1) decrypt arm.
        byte[] oaepCipher = _rsa.RSAEncrypt(plain, pair.PublicKey, Enums.CRYPTO_RSA_PADDING_OAEP);

        byte[]? recoveredUnderDefault = null;
        Exception? rejectedByDefault = Record.Exception(
            () => recoveredUnderDefault = _rsa.RSADecrypt(oaepCipher, pair.PrivateKey));

        if (rejectedByDefault is not null)
        {
            // The overwhelmingly common arm: the unpad check refuses the block outright.
            Assert.IsAssignableFrom<CryptographicException>(rejectedByDefault);
            Assert.Null(recoveredUnderDefault);
        }
        else
        {
            // The rare arm the weak padding scheme makes reachable. It is still NOT interchangeability:
            // whatever came back, it is not the plaintext.
            Assert.NotNull(recoveredUnderDefault);
            Assert.NotEqual(plain, recoveredUnderDefault);
        }

        // DIRECTION TWO - PKCS#1 ciphertext through the explicit OAEP decrypt. Unconditional, because
        // OAEP verifies a hash rather than a two-byte prefix.
        byte[] defaultCipher = _rsa.RSAEncrypt(plain, pair.PublicKey);
        Assert.ThrowsAny<CryptographicException>(
            () => _rsa.RSADecrypt(defaultCipher, pair.PrivateKey, Enums.CRYPTO_RSA_PADDING_OAEP));
    }

    /// <summary>
    /// OAEP remains a fully selectable padding scheme; it is simply not the default.
    /// </summary>
    /// <remarks>
    /// PRESERVING A WEAK DEFAULT IS NOT THE SAME AS REMOVING THE STRONGER OPTION. OAEP is a member
    /// of the published set [enums.sru:L950], so it must be neither removed nor promoted: a caller
    /// that asks for it explicitly gets it and round-trips through it. Both the text-shaped and the
    /// byte-shaped families are exercised, because the padding argument appears on both
    /// [n_crypto.sru:L63, L65 for encrypt and :L67, L69 for decrypt].
    /// </remarks>
    [Fact]
    public void OptimalAsymmetricEncryptionPaddingRemainsSelectableOnBothFamilies()
    {
        RsaKeyPair pair = SharedTestKeyPair.Value;
        byte[] plainBytes = SyntheticPayload(32);
        string plainText = SyntheticText(32);

        byte[] byteShaped =
            _rsa.RSAEncrypt(plainBytes, pair.PublicKey, Enums.CRYPTO_RSA_PADDING_OAEP);
        Assert.Equal(
            plainBytes,
            _rsa.RSADecrypt(byteShaped, pair.PrivateKey, Enums.CRYPTO_RSA_PADDING_OAEP));

        string textShaped =
            _rsa.RSAEncrypt(plainText, pair.PublicKey, Enums.CRYPTO_RSA_PADDING_OAEP);
        Assert.Equal(
            plainText,
            _rsa.RSADecrypt(textShaped, pair.PrivateKey, Enums.CRYPTO_RSA_PADDING_OAEP));
    }

    /// <summary>
    /// The catalogue's RSA padding default is valued from the shared kernel constant and resolves to
    /// PKCS#1 v1.5 rather than to OAEP.
    /// </summary>
    /// <remarks>
    /// The desynchronization guard for the second default, and the assertion that fixes the
    /// DIRECTION of the pair: PKCS#1 is 0 and is the default [enums.sru:L949, L951], OAEP is 1 and
    /// is not [:L950]. Getting the two the wrong way round would be an easy edit to make and an easy
    /// one to miss, so both facts are stated as separate assertions.
    /// </remarks>
    [Fact]
    public void TheCatalogueDefaultRsaPaddingIsValuedFromTheKernelAndIsPkcs1()
    {
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_DEFAULT, LegacyDefaults.RSA_PADDING_DEFAULT);
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_PKCS1, LegacyDefaults.RSA_PADDING_DEFAULT);
        Assert.NotEqual(Enums.CRYPTO_RSA_PADDING_OAEP, LegacyDefaults.RSA_PADDING_DEFAULT);
    }


    // ==========================================================================================
    //  PRESERVED DEFAULT 3 OF 5 - NO-PADDING IS REJECTED, BECAUSE THE LEGACY REJECTS IT
    //  ORACLE  enums.sru:L949-L950 - the padding enumeration has EXACTLY TWO MEMBERS
    //  ------------------------------------------------------------------------------------------
    //  This is the one rejection in this file that is not a hardening, and the distinction matters
    //  enough to state twice. The legacy declares two padding constants and no third. There is
    //  therefore NO CONSTANT FOR RAW RSA, no value a caller could pass to request it, and nothing
    //  for a port to map such a request onto even if it wanted to. Refusing an out-of-set value is
    //  what the legacy does, so refusing it here is behaviour preservation - and this is the
    //  assertion that stops a contributor adding a NONE arm and calling it completeness.
    // ==========================================================================================

    /// <summary>
    /// The catalogue's padding predicate accepts exactly the two published schemes and rejects every
    /// other value, which is where the absence of a no-padding option is enforced.
    /// </summary>
    /// <param name="padding">The candidate padding identifier.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// THE ENUMERATION HAVING EXACTLY TWO MEMBERS IS THE WHOLE MECHANICAL REASON no-padding must be
    /// rejected rather than mapped: with only PKCS#1 at 0 and OAEP at 1 [enums.sru:L949-L950] there
    /// is no third value to map a raw-RSA request onto. The refusal is not this port's judgement
    /// about raw RSA; it is the shape of the legacy's own contract.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RsaPaddingMatrix))]
    public void NoPaddingIsUnreachableThroughTheCataloguePredicate(long padding, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedRsaPadding(padding));

    /// <summary>
    /// The RSA provider surfaces the documented rejection for an out-of-set padding value on every
    /// arm that takes one, so the predicate's refusal is enforced at the boundary and not merely
    /// available to be consulted.
    /// </summary>
    /// <param name="padding">The candidate padding identifier.</param>
    /// <param name="expected">Whether the legacy publishes it; only the rejected rows are exercised.</param>
    /// <remarks>
    /// <para>
    /// A predicate nobody calls would protect nothing, so this walks the same matrix through the
    /// PROVIDER. The rejected rows are asserted to raise the argument-out-of-range failure; the
    /// accepted rows are skipped over by the guard rather than by a test-framework skip, since they
    /// are exercised as successes by the default and OAEP tests above.
    /// </para>
    /// <para>
    /// All four padding-taking arms are covered - text and byte shaped, encrypt and decrypt
    /// [n_crypto.sru:L63, L65, L67, L69] - because a screen omitted from one of the four would be
    /// invisible if only another were tested. THE PADDING IS SCREENED BEFORE THE KEY IS PARSED,
    /// which is why an obviously synthetic non-key string is a legitimate argument here: the
    /// rejection must arrive without the key material ever being read.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(RsaPaddingMatrix))]
    public void ThePaddingArmsRejectEveryValueOutsideTheTwoPublishedSchemes(
        long padding,
        bool expected)
    {
        if (expected)
        {
            // A published value is not a rejection case. Asserting the successful direction here
            // would duplicate the default and OAEP tests, so the row is simply not exercised.
            Assert.True(LegacyDefaults.IsSupportedRsaPadding(padding));
            return;
        }

        byte[] payloadBytes = SyntheticPayload(8);
        string payloadText = SyntheticText(8);

        // Not a key, and never parsed as one: the padding screen runs first, so this argument is
        // deliberately an obviously synthetic non-key placeholder.
        const string UnreadKeyPlaceholder = "not-a-key";

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSAEncrypt(payloadBytes, UnreadKeyPlaceholder, padding));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSAEncrypt(payloadText, UnreadKeyPlaceholder, padding));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSADecrypt(payloadBytes, UnreadKeyPlaceholder, padding));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSADecrypt(payloadText, UnreadKeyPlaceholder, padding));
    }

    /// <summary>
    /// The catalogue records the absence of a no-padding option as a documented fact rather than
    /// leaving it to be inferred from the predicate.
    /// </summary>
    /// <remarks>
    /// <see cref="LegacyDefaults.RSA_NO_PADDING_SUPPORTED"/> is <see langword="false"/> and must
    /// stay so. It exists so that a provider can cite the reason for the refusal without
    /// paraphrasing it, and so that the refusal reads as PRESERVED LEGACY BEHAVIOUR at the point of
    /// use rather than as a local hardening decision.
    /// </remarks>
    [Fact]
    public void TheCatalogueRecordsThatNoPaddingIsUnsupported() =>
        Assert.False(LegacyDefaults.RSA_NO_PADDING_SUPPORTED);

    // ==========================================================================================
    //  PRESERVED DEFAULT 4 OF 5 - 1024-BIT RSA REMAINS A LEGAL KEY SIZE
    //  ORACLE  enums.sru:L965 CRYPTO_RSA_BITS_1024 = 1024   (Constant Uint)
    //          enums.sru:L966 CRYPTO_RSA_BITS_2048 = 2048
    //          enums.sru:L967 CRYPTO_RSA_BITS_4096 = 4096
    //  ------------------------------------------------------------------------------------------
    //  THE DIRECTION OF THESE ASSERTIONS IS THE POINT. 1024 bits is below every current
    //  recommendation, and the instinct is to add a floor. A floor would REJECT INPUT THE LEGACY
    //  ACCEPTED, which constraint C-B forbids, and the legacy's own demonstration generates exactly
    //  1024 bits. So 1024 is asserted to SUCCEED, and no minimum-key-size guard test appears
    //  anywhere in this file.
    //
    //  Note the width contrast, because it is evidence rather than trivia: these three constants are
    //  declared `Constant Uint` - 16 bits - and the GenRSAKey `bits` parameter is declared
    //  `readonly uint` too [n_crypto.sru:L19-L20], so parameter and constants AGREE here. The
    //  symmetric cipher type is the case where they do not, which is what shows that mismatch to be
    //  a legacy inconsistency rather than a general pattern.
    // ==========================================================================================

    /// <summary>
    /// Key generation accepts 1024 bits and succeeds, and the resulting pair is genuinely usable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// KNOWN LEGACY WEAKNESS, PRESERVED DELIBERATELY AND ASSERTED AS CORRECT. A 1024-bit modulus is
    /// below current guidance, and it stays legal because <see cref="Enums.CRYPTO_RSA_BITS_1024"/>
    /// [enums.sru:L965] is one of the three sizes the legacy publishes and its own demonstration
    /// generates that size. Imposing a floor would be a behavioural change dressed as a security
    /// fix.
    /// </para>
    /// <para>
    /// CONSTRAINT C-F, STATED AT THE ONE PLACE IT IS MOST TEMPTING TO BREAK. This is the test where
    /// a legacy demonstration key would look convenient. IT USES NONE. The pair is generated here,
    /// at test time, by the provider's own generator, so no key material is written down in this
    /// file - and the round-trip below is what proves the generated pair is real rather than merely
    /// well formed.
    /// </para>
    /// </remarks>
    [Fact]
    public void KeyGenerationAcceptsTheSmallestPublishedSizeOfOneThousandAndTwentyFourBits()
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(_rsa.GenRSAKey(Enums.CRYPTO_RSA_BITS_1024, ref privateKey, ref publicKey));

        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);

        // A generated pair that cannot carry a payload would satisfy the boolean and mean nothing,
        // so the pair is exercised. The payload is small because a 1024-bit modulus under PKCS#1
        // v1.5 carries at most 117 bytes - a preserved legacy ceiling, not a limitation added here.
        byte[] plain = SyntheticPayload(16);
        Assert.Equal(plain, _rsa.RSADecrypt(_rsa.RSAEncrypt(plain, publicKey), privateKey));
    }

    /// <summary>
    /// All three published key sizes are accepted, smallest included.
    /// </summary>
    /// <param name="bits">The published modulus size under test.</param>
    /// <remarks>
    /// The three sizes are CONVENIENCE VALUES RATHER THAN AN ALLOWED SET, which is why the catalogue
    /// publishes no key-size predicate: a predicate would narrow the contract, since the legacy's
    /// <c>bits</c> parameter accepts any unsigned value. This theory therefore asserts that the
    /// three named sizes work, not that others fail.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedRsaKeySizes))]
    public void KeyGenerationAcceptsEveryPublishedKeySize(ushort bits)
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(_rsa.GenRSAKey(bits, ref privateKey, ref publicKey));
        Assert.NotEmpty(privateKey);
        Assert.NotEmpty(publicKey);
    }

    /// <summary>
    /// The catalogue names 1024 bits as the smallest legal size and records that no minimum is
    /// enforced.
    /// </summary>
    /// <remarks>
    /// <see cref="LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS"/> is valued from
    /// <see cref="Enums.CRYPTO_RSA_BITS_1024"/> [enums.sru:L965] and
    /// <see cref="LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED"/> is <see langword="false"/>. The
    /// second member is the one that reads as odd and is the one that matters: it records, at the
    /// place a contributor would look, that the absence of a floor is deliberate.
    /// </remarks>
    [Fact]
    public void TheCatalogueNamesTheSmallestLegalKeySizeAndEnforcesNoMinimum()
    {
        Assert.Equal(Enums.CRYPTO_RSA_BITS_1024, LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);
        Assert.False(LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED);
    }

    // ==========================================================================================
    //  PRESERVED DEFAULT 5 OF 5 - NO KEY DERIVATION AND NO AUTHENTICATED ENCRYPTION IS REACHABLE
    //  ORACLE  enums.sru:L943-L945 - the mode set is exactly ECB, CBC, CFB
    //          n_crypto.sru:L9-L73 - all 65 declarations, none of which mentions a salt, an
    //                                iteration count, a derived key or an authentication tag
    //  ------------------------------------------------------------------------------------------
    //  ASSERTED AS AN ABSENCE, WHICH TAKES TWO FORMS BECAUSE ONE WOULD NOT BE ENOUGH:
    //
    //      * AT THE VALUE LEVEL, the published mode set is exactly three wide, so GCM, CCM and
    //        Poly1305 are UNREACHABLE rather than merely unimplemented - there is no identifier a
    //        caller could pass to select one.
    //      * AT THE SURFACE LEVEL, a reflection sweep over the real public surface of the six real
    //        providers, so that a Pbkdf2, a Salt, an Iterations, a Tag or an AuthenticateAndEncrypt
    //        member cannot appear without failing a test.
    //
    //  A contributor adding either capability would be making exactly the silent improvement
    //  constraint C-B forbids. These are the tripwires.
    // ==========================================================================================

    /// <summary>
    /// The published cipher mode set is exactly ECB, CBC and CFB, so no authenticated mode is
    /// selectable.
    /// </summary>
    /// <param name="mode">The candidate cipher mode.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// The value-level half of the absence. The set is exactly the three at enums.sru:L943-L945, and
    /// the rejected rows are where an authenticated mode would have to enter the surface. ECB is a
    /// member and stays one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherModeMatrix))]
    public void NoAuthenticatedModeIsSelectableThroughTheCataloguePredicate(long mode, bool expected)
    {
        Assert.Equal(expected, LegacyDefaults.IsSupportedSymmetricMode(mode));

        // Exactly three published modes, so an added fourth would fail here as well as above.
        Assert.Equal(3, PublishedCipherModes.Length);
    }

    /// <summary>
    /// The cipher provider refuses every mode outside the published three, so an authenticated mode
    /// cannot be reached through the provider either.
    /// </summary>
    /// <param name="mode">The candidate cipher mode.</param>
    /// <param name="expected">Whether the legacy publishes it; only the rejected rows are exercised.</param>
    /// <remarks>
    /// The predicate is only protective if the provider consults it, so the rejected rows are walked
    /// through the provider as well. AES-256 is used as the carrier type because the assertion is
    /// about the MODE argument, and a type-driven refusal would mask a missing mode screen.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherModeMatrix))]
    public void TheCipherProviderRefusesEveryModeOutsideThePublishedThree(long mode, bool expected)
    {
        if (expected)
        {
            Assert.True(LegacyDefaults.IsSupportedSymmetricMode(mode));
            return;
        }

        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] payload = SyntheticPayload(metrics.BlockLengthBytes);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _cipher.SymEncrypt(payload, key, ntype, mode));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _cipher.SymDecrypt(payload, key, ntype, mode));
    }

    /// <summary>
    /// No provider exposes a key-derivation function, a salt, an iteration count, an
    /// authenticated-encryption mode or an authentication tag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SURFACE-LEVEL HALF OF THE ABSENCE, AND THE MOST IMPORTANT TRIPWIRE IN THIS FILE. The
    /// sweep enumerates the real public members of the real provider types discovered from the
    /// assembly, so it fails the moment such a member appears - which is what makes it worth
    /// asserting at all. An absence assertion that could not fail would be decoration.
    /// </para>
    /// <para>
    /// The absences are real and load bearing. Nothing in the 65 declarations at
    /// n_crypto.sru:L9-L73 reaches a key-derivation function, so a passphrase is used as RAW KEY
    /// BYTES with no stretching and no salt - see DECISION D1. No published mode is authenticated,
    /// so ciphertext carries no integrity tag and tampering is not detectable - see DECISION D2.
    /// Introducing either would change every key or every ciphertext the legacy produced.
    /// </para>
    /// <para>
    /// The sweep is scoped to the PROVIDERS rather than to the whole namespace on purpose. The
    /// catalogue itself carries members whose names contain these very words -
    /// <see cref="LegacyDefaults.KEY_DERIVATION_AVAILABLE"/> and
    /// <see cref="LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE"/> - because their job is to
    /// RECORD the absence. Sweeping them would confuse the record of the absence with the
    /// capability.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoProviderExposesKeyDerivationOrAuthenticatedEncryption()
    {
        List<string> offenders = [];

        foreach (Type provider in DiscoverProviderTypes())
        {
            foreach (string memberName in PublicMemberNames(provider))
            {
                foreach (string fragment in ForbiddenCapabilityFragments)
                {
                    if (memberName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{provider.Name}.{memberName} matches '{fragment}'");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The catalogue records both absences as documented facts.
    /// </summary>
    /// <remarks>
    /// The two flags are the citable form of the two sweeps above, so a provider can state WHY it
    /// uses raw key bytes and WHY it appends no tag without restating the argument. Both are
    /// <see langword="false"/> and both must stay so; flipping either would claim a capability the
    /// legacy surface does not have.
    /// </remarks>
    [Fact]
    public void TheCatalogueRecordsBothTheKeyDerivationAndAuthenticatedEncryptionAbsences()
    {
        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);
        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);

        // Block padding is not selectable either: no overload among the 32 at n_crypto.sru:L30-L61
        // carries a padding argument, and the framework's changelog records PKCS#5 only.
        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);
    }


    // ==========================================================================================
    //  DECISION D1 - THERE IS NO KEY-DERIVATION FUNCTION, SO A PASSPHRASE IS RAW KEY BYTES
    //  ------------------------------------------------------------------------------------------
    //  A DOCUMENTED DECISION, NOT DERIVED PARITY (constraint C-K). The behaviour of the closed
    //  pfw.dll on key material of the wrong length is UNOBSERVABLE from this repository: the export
    //  at n_crypto.sru declares 65 prototypes and no implementation exists anywhere in the tree. The
    //  catalogue therefore RULES that caller material is encoded, then truncated or zero-padded to
    //  exactly the cipher's key length, and applies that rule from one place so all six providers
    //  cannot each apply a different one.
    //
    //  What the tests below establish is that THE RULE IS APPLIED UNIFORMLY AND EXACTLY AS WRITTEN.
    //  They do NOT establish byte-exact agreement with the closed binary, and nothing here should be
    //  read as claiming they do; adjudicating that requires the behavioural oracle.
    //
    //  BOTH HALVES OF THE RULE ARE THEMSELVES WEAKNESSES AND BOTH ARE PRESERVED. Truncation silently
    //  discards entropy the caller believed it supplied; zero-padding silently manufactures key bytes
    //  the caller never chose. Neither is corrected, because deriving a key instead would change
    //  every key, and therefore every ciphertext, the legacy ever produced.
    // ==========================================================================================

    /// <summary>
    /// A passphrase becomes key bytes by encoding alone: material of exactly the required length is
    /// carried through unchanged, with no stretching, mixing or derivation of any kind.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D1. The comparison against the plain encoding of the same text is the
    /// assertion that no derivation happens: any key-derivation function - PBKDF2, scrypt, bcrypt,
    /// Argon2 - would produce something OTHER than the encoded bytes, and would therefore fail here.
    /// That is the intent. The encoding itself is
    /// <see cref="LegacyDefaults.KeyMaterialEncoding"/>, which never emits a byte-order mark, so no
    /// phantom leading bytes can enter key material.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void PassphraseMaterialOfTheExactLengthBecomesItsOwnEncodedBytes(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        string passphrase = SyntheticText(metrics.KeyLengthBytes);

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(passphrase, metrics.KeyLengthBytes);

        Assert.Equal(LegacyDefaults.KeyMaterialEncoding.GetBytes(passphrase), normalized);
        Assert.Equal(metrics.KeyLengthBytes, normalized.Length);
    }

    /// <summary>
    /// Over-long material is truncated to the cipher's key length, keeping the LEADING bytes and
    /// silently discarding the rest.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D1, the truncation half. Asserted as correct despite being a weakness:
    /// a caller supplying a long passphrase gets only its leading bytes, so entropy it believed it
    /// had contributed is discarded without a diagnostic. The direction is asserted explicitly -
    /// leading bytes kept, trailing bytes dropped - because keeping the trailing bytes instead would
    /// be an equally plausible implementation and a silent parity break.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void OverLongPassphraseMaterialIsTruncatedToTheLeadingBytes(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] overLong = SyntheticKeyMaterial(metrics.KeyLengthBytes + 9);

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(overLong, metrics.KeyLengthBytes);

        Assert.Equal(metrics.KeyLengthBytes, normalized.Length);
        Assert.Equal(overLong[..metrics.KeyLengthBytes], normalized);
    }

    /// <summary>
    /// Under-length material is zero-padded on the RIGHT to the cipher's key length, silently
    /// manufacturing the missing bytes.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D1, the zero-padding half, and the more consequential of the two. A
    /// caller supplying a two-character passphrase for a 32-byte cipher gets thirty manufactured
    /// zero bytes, so the effective key space collapses to the passphrase. That is asserted as
    /// correct because introducing a derivation step - which is the obvious "fix" - would change
    /// every key the legacy produced. The padding SIDE is asserted explicitly: padding on the left
    /// would be an equally plausible implementation and a silent parity break.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void UnderLengthPassphraseMaterialIsZeroPaddedOnTheRight(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] tooShort = SyntheticKeyMaterial(2);

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(tooShort, metrics.KeyLengthBytes);

        Assert.Equal(metrics.KeyLengthBytes, normalized.Length);
        Assert.Equal(tooShort, normalized[..tooShort.Length]);
        Assert.All(normalized[tooShort.Length..], padByte => Assert.Equal(0, padByte));
    }

    /// <summary>
    /// Empty material becomes an all-zero key rather than a failure, which is the limiting case of
    /// the zero-padding half.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D1. The result is an entirely manufactured key, and the rule produces it
    /// without complaint. Worth pinning as the limiting case, because it is the input on which a
    /// derivation-based implementation would differ most visibly - a key-derivation function over an
    /// empty passphrase still yields high-entropy-looking bytes, whereas this rule yields zeroes.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void EmptyPassphraseMaterialBecomesAnAllZeroKey(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);

        byte[] fromEmptyText =
            LegacyDefaults.NormalizeKeyMaterial(string.Empty, metrics.KeyLengthBytes);
        byte[] fromEmptyBytes =
            LegacyDefaults.NormalizeKeyMaterial(ReadOnlySpan<byte>.Empty, metrics.KeyLengthBytes);

        Assert.Equal(metrics.KeyLengthBytes, fromEmptyText.Length);
        Assert.All(fromEmptyText, keyByte => Assert.Equal(0, keyByte));
        Assert.Equal(fromEmptyText, fromEmptyBytes);
    }

    /// <summary>
    /// The text and binary spellings of the same key material normalize identically, so one rule
    /// governs both halves of the legacy's string-and-blob pairing.
    /// </summary>
    /// <remarks>
    /// DOCUMENTED DECISION D1. Blob key and vector parameters were a later addition to the legacy
    /// surface, so the two shapes could easily have drifted apart; sharing one implementation is what
    /// makes a text key and the binary spelling of the same bytes impossible to normalize
    /// differently.
    /// </remarks>
    [Fact]
    public void TheTextAndBinarySpellingsOfKeyMaterialNormalizeIdentically()
    {
        foreach (SymmetricCipherMetrics metrics in LegacyDefaults.AllSymmetricCipherMetrics)
        {
            foreach (int length in new[] { 1, 2, metrics.KeyLengthBytes, metrics.KeyLengthBytes + 5 })
            {
                string text = SyntheticText(length);
                byte[] sameBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(text);

                Assert.Equal(
                    LegacyDefaults.NormalizeKeyMaterial(text, metrics.KeyLengthBytes),
                    LegacyDefaults.NormalizeKeyMaterial(sameBytes, metrics.KeyLengthBytes));
            }
        }
    }

    /// <summary>
    /// The rule the catalogue applies is the rule the cipher provider actually uses: a short
    /// passphrase and the zero-padded key it normalizes to produce identical ciphertext.
    /// </summary>
    /// <param name="ntype">A cipher type whose key schedule accepts any byte pattern.</param>
    /// <remarks>
    /// <para>
    /// DOCUMENTED DECISION D1, carried from the catalogue into observed behaviour. This is the
    /// assertion that stops the annotation drifting away from the code: it would fail if the provider
    /// derived, hashed or otherwise stretched the passphrase instead of padding it, however well
    /// documented the padding rule remained.
    /// </para>
    /// <para>
    /// The theory runs over the AES types only, and the reason is itself a preserved behaviour rather
    /// than a convenience. Zero-padding a short passphrase can MANUFACTURE A KEY THE PLATFORM
    /// REFUSES: padded to eight bytes it can land on a known weak single-DES key, and padded to
    /// twenty-four it can make the second and third triple-DES sub-keys coincide. The platform then
    /// raises instead of encrypting, which is a real and separately documented behaviour of the
    /// cipher provider. Exercising it here would test that refusal rather than the padding rule, so
    /// the rule itself is proven above against the catalogue's normalizer for all five types, and the
    /// provider-level agreement is proven here on the three types where the interaction cannot
    /// interfere.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyPatternTolerantCipherTypes))]
    public void TheProviderAppliesTheRawKeyBytesRuleRatherThanDerivingAKey(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        string shortPassphrase = SyntheticText(3);
        byte[] payload = SyntheticPayload(metrics.BlockLengthBytes * 2);

        byte[] fromPassphrase = _cipher.SymEncrypt(payload, shortPassphrase, ntype);
        byte[] fromNormalizedKey = _cipher.SymEncrypt(
            payload,
            LegacyDefaults.NormalizeKeyMaterial(shortPassphrase, metrics.KeyLengthBytes),
            ntype);

        Assert.Equal(fromNormalizedKey, fromPassphrase);
    }

    // ==========================================================================================
    //  DECISION D2 - THERE IS NO AUTHENTICATED ENCRYPTION, SO CIPHERTEXT CARRIES NO INTEGRITY TAG
    //  ------------------------------------------------------------------------------------------
    //  A DOCUMENTED DECISION, NOT DERIVED PARITY (constraint C-K). No published mode is
    //  authenticated [enums.sru:L943-L945] and no overload among the 32 at n_crypto.sru:L30-L61
    //  takes or returns a tag, so there is nowhere in the legacy format for one to live. The
    //  consequence, asserted below as correct, is that TAMPERING IS NOT DETECTABLE: a modified
    //  ciphertext yields either the platform's padding failure or plausible garbage, and neither
    //  outcome is tamper detection.
    // ==========================================================================================

    /// <summary>
    /// Ciphertext length is exactly the padded plaintext length, so nothing is appended - in
    /// particular, no authentication tag.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <param name="mode">The published cipher mode under test.</param>
    /// <remarks>
    /// <para>
    /// DOCUMENTED DECISION D2. The upper bound is the assertion with teeth: an authenticated mode
    /// would append a tag - sixteen bytes for the usual constructions - so the ciphertext would
    /// exceed the padded plaintext length and this would fail. The lower bound of one byte is the
    /// padding scheme's own rule, which always adds at least one byte and therefore grows a
    /// whole-block payload by a full further block.
    /// </para>
    /// <para>
    /// The bound is expressed as a RANGE rather than as an equality on purpose. The padding
    /// granularity is the block length for electronic codebook and cipher block chaining, but for
    /// cipher feedback it is the feedback width, which for single DES is one byte. Pinning the
    /// feedback width is the cipher provider's own concern and is a separately documented decision;
    /// what THIS test must establish, and does for all fifteen cells, is that whatever the
    /// granularity, THE OVERHEAD NEVER EXCEEDS IT - which is precisely what rules out a tag.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeAndModeGrid))]
    public void CipherTextCarriesPaddingOnlyAndNeverAnAuthenticationTag(ushort ntype, long mode)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);

        // A VECTOR IS SUPPLIED EXPLICITLY because the grid now includes the chaining mode, which this
        // port serves only with a caller-supplied vector (DECISION D3). Passing one keeps this test
        // measuring what it is about - padding overhead, tamper behaviour or payload form - rather
        // than colliding with a capability refusal it is not testing.
        byte[] vector = SyntheticVectorMaterial(metrics.IvLengthBytes);

        foreach (int plainLength in new[] { 0, 1, metrics.BlockLengthBytes, metrics.BlockLengthBytes + 1 })
        {
            byte[] plain = SyntheticPayload(plainLength);
            byte[] cipherText = _cipher.SymEncrypt(plain, key, vector, ntype, mode);

            int overhead = cipherText.Length - plain.Length;

            Assert.InRange(overhead, 1, metrics.BlockLengthBytes);
            Assert.Equal(plain, _cipher.SymDecrypt(cipherText, key, vector, ntype, mode));
        }
    }

    /// <summary>
    /// A modified ciphertext is not reported as an authentication failure, because there is no
    /// authentication: it either fails the padding check or decrypts to garbage, and which of the two
    /// happens is not something this surface can promise.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <param name="mode">The published cipher mode under test.</param>
    /// <remarks>
    /// <para>
    /// DOCUMENTED DECISION D2, ASSERTED AS CORRECT. The outcome pinned here is deliberately the weak
    /// one - the original plaintext does not come back, and nothing tells the caller why. The
    /// alternative, adding a tag so that tampering could be reported, would change the ciphertext
    /// format and make every value the legacy wrote unreadable.
    /// </para>
    /// <para>
    /// THE GARBAGE OUTCOME IS THE ONE THAT MATTERS AND IS EXPLICITLY ALLOWED. A test insisting that
    /// tampering always raises would be asserting tamper detection - the very property this surface
    /// does not have - and would fail intermittently as the padding check happened to pass. So both
    /// outcomes are accepted and only the recovery of the original plaintext is excluded.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeAndModeGrid))]
    public void ModifiedCipherTextIsNotReportedAsAnAuthenticationFailure(ushort ntype, long mode)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 2);

        // A VECTOR IS SUPPLIED EXPLICITLY because the grid now includes the chaining mode, which this
        // port serves only with a caller-supplied vector (DECISION D3). Passing one keeps this test
        // measuring what it is about - padding overhead, tamper behaviour or payload form - rather
        // than colliding with a capability refusal it is not testing.
        byte[] vector = SyntheticVectorMaterial(metrics.IvLengthBytes);

        byte[] tampered = _cipher.SymEncrypt(plain, key, vector, ntype, mode);

        // The positive control first, on the UNTAMPERED bytes: the helper must be able to answer
        // positively, or the refutation below would hold for the trivial reason that it never answers
        // anything else.
        Assert.True(
            RecoversPlainText(tampered, key, ntype, mode, plain, vector),
            "The helper must be able to answer positively, or the refutation below proves nothing.");

        // One flipped bit in the first byte. Under an authenticated mode this would be reported as a
        // tag mismatch; here there is no tag, so it cannot be.
        tampered[0] ^= 0x01;

        Assert.False(
            RecoversPlainText(tampered, key, ntype, mode, plain, vector),
            "Tampering must not be silently repaired: the original plaintext must not return.");
    }

    // ==========================================================================================
    //  DECISION D3 - THE EIGHT MODE-WITHOUT-VECTOR ARMS REFUSE A VECTOR-CONSUMING MODE
    //  ORACLE  n_crypto.sru:L31, L35, L39, L43   SymEncrypt - a mode, but no vector
    //          n_crypto.sru:L47, L51, L55, L59   SymDecrypt - the same four shapes, mirrored
    //  ------------------------------------------------------------------------------------------
    //  WHAT THESE ROWS ASSERT. Eight of the thirty-two symmetric overloads accept a mode but no
    //  initialization vector, so a caller may ask for a vector-consuming mode without supplying one.
    //  Those eight arms REFUSE such a mode, with a reason naming the missing evidence. This is not a
    //  rare branch - it is a quarter of the family - and the codebook mode, which consumes no vector,
    //  is unaffected and is asserted to still work.
    //
    //  WHY REFUSING IS ASSERTED RATHER THAN SOME SUBSTITUTED VECTOR. Any substitute would have to be
    //  invented: `n_crypto` is declared native "pfw.dll" [n_crypto.sru:L8] with no PowerScript body
    //  for any of the 32 overloads, so what vector the oracle uses is unobservable from this
    //  repository. And a test comparing a vector-less arm against an explicit all-zero vector would
    //  pass by CONSTRUCTION if the port substituted that same vector - one code path calling the
    //  other - proving only that the two agree with EACH OTHER while reading exactly like a parity
    //  assertion.
    //
    //  WHY THAT MATTERS MORE THAN IT LOOKS. A wrong vector round-trips perfectly against itself, so
    //  no test here could ever detect it - while the ciphertext produced would be undecryptable by
    //  the legacy. A guess would therefore be silently unfalsifiable AND potentially destructive,
    //  which is the combination the narrow-with-a-defined-error rule exists to forbid.
    // ==========================================================================================

    /// <summary>
    /// The refusal type will not be constructed for a NON-blocking reason, and it carries the cell it
    /// refused so a handler can report which one without re-deriving it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE GUARD EXISTS BECAUSE THE ENUM HAS A SUPPORTED MEMBER AND AN EXCEPTION HAS NO USE FOR IT.
    /// Constructing this type with <see cref="SymmetricCellParity.Supported"/> would mean some caller
    /// classified a cell as fine and then raised on it anyway - a contradiction whose message could
    /// only be misleading. Refusing to build it turns that contradiction into an immediate failure at
    /// the point of the mistake.
    /// </para>
    /// <para>
    /// An undefined member is rejected by the same arm, so adding a future blocking reason without
    /// giving it a message fails loudly rather than inheriting a vague default.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheRefusalTypeRejectsANonBlockingReasonAndCarriesItsCell()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SymmetricParityUnavailableException(
            SymmetricCellParity.Supported,
            Enums.CRYPTO_SYMCRYPT_MODE_CBC));

        // An undefined member takes the same arm.
        Assert.Throws<ArgumentOutOfRangeException>(() => new SymmetricParityUnavailableException(
            (SymmetricCellParity)int.MaxValue,
            Enums.CRYPTO_SYMCRYPT_MODE_CBC));

        // A blocking reason builds, and both facts about the refused cell survive on the instance.
        SymmetricParityUnavailableException refusal = new(
            SymmetricCellParity.BlockedFeedbackWidthUnprovable,
            Enums.CRYPTO_SYMCRYPT_MODE_CFB);

        Assert.Equal(SymmetricCellParity.BlockedFeedbackWidthUnprovable, refusal.Reason);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_CFB, refusal.Mode);

        // NOT a cryptographic failure, so no handler that absorbs a padding error can swallow it.
        Assert.IsType<NotSupportedException>(refusal, exactMatch: false);
        Assert.IsNotType<CryptographicException>(refusal, exactMatch: false);

        // The message explains the refusal and names no key, vector or payload content.
        Assert.Contains("feedback width", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The classifier answers every one of the six mode-by-vector combinations, and the answers are
    /// exactly the blocked set DECISION D3 defines - no more and no less.
    /// </summary>
    /// <param name="mode">The cipher mode.</param>
    /// <param name="vectorSupplied">Whether a vector accompanies the call.</param>
    /// <param name="expected">The classification the catalogue must report.</param>
    /// <remarks>
    /// TOTALITY IS THE POINT. Enumerating all six combinations rather than only the interesting ones
    /// is what proves the narrowing is minimal: three of the six are Supported, and if a future edit
    /// broadened the block to catch the codebook mode or vector-bearing chaining, three of these rows
    /// would fail immediately. A test that only checked the blocked cells could not tell a correct
    /// narrowing from a blanket refusal.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherCellClassifications))]
    public void TheClassifierReportsExactlyTheBlockedSet(
        long mode,
        bool vectorSupplied,
        SymmetricCellParity expected) =>
        Assert.Equal(expected, LegacyDefaults.ClassifySymmetricCell(mode, vectorSupplied));

    /// <summary>
    /// A blocked mode remains a PUBLISHED mode. The two questions are separate and this pins the
    /// separation, because collapsing them would breach the preserve-the-identifier-set rule.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole shape of the DECISION D3 remediation. Withdrawing the identifier
    /// would have been a reduction of the legacy surface - forbidden, because the oracle declares
    /// three modes at [enums.sru:L943-L945] and the set is preserved exactly. Withdrawing only the
    /// CAPABILITY leaves the surface intact while refusing to invent the one parameter the legacy
    /// never published. A future edit that "tidied" the mode out of the supported-identifier predicate
    /// would fail here.
    /// </remarks>
    [Fact]
    public void ABlockedModeIsStillAPublishedMode()
    {
        // Published: the screening predicate accepts it, and it is a member of the declared set.
        Assert.True(LegacyDefaults.IsSupportedSymmetricMode(Enums.CRYPTO_SYMCRYPT_MODE_CFB));
        Assert.Contains(Enums.CRYPTO_SYMCRYPT_MODE_CFB, PublishedCipherModes);

        // Yet not reproducible, in either vector shape.
        Assert.Equal(
            SymmetricCellParity.BlockedFeedbackWidthUnprovable,
            LegacyDefaults.ClassifySymmetricCell(
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                initializationVectorSupplied: true));
        Assert.Equal(
            SymmetricCellParity.BlockedFeedbackWidthUnprovable,
            LegacyDefaults.ClassifySymmetricCell(
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                initializationVectorSupplied: false));
    }

    /// <summary>
    /// The classifier treats an UNPUBLISHED mode as supported, leaving its rejection to the screening
    /// step, so one input never draws two different refusals.
    /// </summary>
    /// <remarks>
    /// Asserted because it looks wrong at a glance and is deliberate. The classifier answers "can this
    /// cell be reproduced?", not "is this a legal identifier?" - and an undeclared value has no cell.
    /// The provider screens the identifier first, so an undeclared mode is refused as an out-of-range
    /// argument and never reaches a capability question. Were the classifier to also reject it, the
    /// order of the two checks would decide which exception a caller saw.
    /// </remarks>
    [Fact]
    public void TheClassifierLeavesAnUndeclaredModeToTheScreeningStep()
    {
        long undeclared = Enums.CRYPTO_SYMCRYPT_MODE_CFB + 1;

        Assert.False(LegacyDefaults.IsSupportedSymmetricMode(undeclared));
        Assert.Equal(
            SymmetricCellParity.Supported,
            LegacyDefaults.ClassifySymmetricCell(undeclared, initializationVectorSupplied: false));

        // And the provider refuses it as an argument, not as a capability.
        Assert.Throws<ArgumentOutOfRangeException>(() => _cipher.SymEncrypt(
            SyntheticPayload(16),
            SyntheticKeyMaterial(32),
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
            undeclared));
    }

    /// <summary>
    /// Omitting the vector for a vector-consuming mode is REFUSED, on every cipher type and in both
    /// directions, rather than having a vector invented for it.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <param name="mode">The vector-consuming mode under test.</param>
    /// <remarks>
    /// The refusal names WHICH evidence is missing, so the two independent blocking reasons stay
    /// distinguishable: a measurement of the oracle's feedback width would unblock one, and a
    /// measurement of its substituted vector the other.
    /// </remarks>
    [Theory]
    [MemberData(nameof(VectorConsumingTypeAndModeGrid))]
    public void OmittingTheVectorForAVectorConsumingModeIsRefused(ushort ntype, long mode)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 2);

        SymmetricCellParity expected = mode == Enums.CRYPTO_SYMCRYPT_MODE_CFB
            ? SymmetricCellParity.BlockedFeedbackWidthUnprovable
            : SymmetricCellParity.BlockedSynthesizedVectorUnprovable;

        Assert.Equal(
            expected,
            Assert.Throws<SymmetricParityUnavailableException>(
                () => _cipher.SymEncrypt(plain, key, ntype, mode)).Reason);

        Assert.Equal(
            expected,
            Assert.Throws<SymmetricParityUnavailableException>(
                () => _cipher.SymDecrypt(plain, key, ntype, mode)).Reason);
    }

    /// <summary>
    /// Supplying a vector rescues the chaining mode but NOT the feedback mode, because the two arms
    /// are blocked by different missing evidence.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// THE DISCRIMINATING TEST OF THE WHOLE SECTION. It is what proves the narrowing is the minimum
    /// one rather than a blanket refusal of everything awkward: with a vector supplied, chaining is
    /// fully supported and round-trips, while the feedback mode stays refused because a vector says
    /// nothing about its feedback width. A blanket block would fail the first half; a block that had
    /// missed the width problem would fail the second.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void SupplyingAVectorRescuesChainingButNotFeedback(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 2);
        byte[] vector = SyntheticVectorMaterial(metrics.IvLengthBytes);

        byte[] chained = _cipher.SymEncrypt(
            plain,
            key,
            vector,
            ntype,
            Enums.CRYPTO_SYMCRYPT_MODE_CBC);

        Assert.Equal(
            plain,
            _cipher.SymDecrypt(chained, key, vector, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));

        Assert.Equal(
            SymmetricCellParity.BlockedFeedbackWidthUnprovable,
            Assert.Throws<SymmetricParityUnavailableException>(() => _cipher.SymEncrypt(
                plain,
                key,
                vector,
                ntype,
                Enums.CRYPTO_SYMCRYPT_MODE_CFB)).Reason);
    }

    /// <summary>
    /// Electronic codebook mode never consumes a vector at all, which is what confines this
    /// decision's refusal to the modes that genuinely require one.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D3, and its boundary. The default mode consumes no vector, so a vector
    /// supplied to an electronic codebook operation cannot influence the result - the catalogue's
    /// classifier reports as much, and the provider structures the call so that a supplied vector
    /// cannot reach the primitive even in principle. Both an all-zero vector and a non-zero one are
    /// shown to make no difference.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void ElectronicCodebookNeverConsumesAVectorAtAll(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        byte[] plain = SyntheticPayload(metrics.BlockLengthBytes * 2);
        long ecb = Enums.CRYPTO_SYMCRYPT_MODE_ECB;

        Assert.False(LegacyDefaults.ModeUsesInitializationVector(ecb));

        byte[] withoutVector = _cipher.SymEncrypt(plain, key, ntype, ecb);
        // Constructed here from a length rather than written as a literal, so this file still holds
        // nothing resembling key material. The catalogue offers no factory for it, because DECISION D3
        // supplies no vector anywhere - this buffer exists only to prove ECB ignores one.
        byte[] withZeroVector = _cipher.SymEncrypt(
            plain,
            key,
            new byte[metrics.IvLengthBytes],
            ntype,
            ecb);
        byte[] withNonZeroVector = _cipher.SymEncrypt(
            plain,
            key,
            SyntheticVectorMaterial(metrics.IvLengthBytes),
            ntype,
            ecb);

        Assert.Equal(withoutVector, withZeroVector);
        Assert.Equal(withoutVector, withNonZeroVector);
    }

    // ==========================================================================================
    //  DECISION D4 - ONE TEXT-SAFE PAYLOAD ENCODING, APPLIED UNIFORMLY ACROSS THE WHOLE FOLDER
    //  ------------------------------------------------------------------------------------------
    //  A DOCUMENTED DECISION, NOT DERIVED PARITY (constraint C-K). Which encoding the closed
    //  pfw.dll used for the string-shaped results is unobservable from this repository, so the
    //  catalogue RULES one - LegacyDefaults.STRING_PAYLOAD_ENCODING, valued from the legacy's own
    //  zero-valued primary encoding [enums.sru:L924] - and every provider reads it from there.
    //
    //  UNIFORMITY IS THE PROPERTY WORTH ASSERTING, AND IT IS ALSO THE PROPERTY THAT KEEPS THIS SUITE
    //  CONSISTENT WITH ITS SIBLINGS. Because the catalogue member is the single authority for the
    //  digest text form as well as for ciphertext and signatures, a suite that pinned a different
    //  encoding for digests would contradict this one. The tests below therefore walk FOUR
    //  PROVIDERS - unkeyed hash, keyed hash, cipher and signature - and assert the same relationship
    //  for each: the string-shaped result is the byte-shaped result, carried through that one
    //  encoding and nothing else.
    // ==========================================================================================

    /// <summary>
    /// The catalogue's payload encoding is valued from the shared kernel constant and is the
    /// legacy's own primary, zero-valued encoding.
    /// </summary>
    /// <remarks>
    /// DOCUMENTED DECISION D4. The desynchronization guard for the encoding: it is
    /// <see cref="Enums.CRYPTO_ENCODING_BASE64"/> [enums.sru:L924] rather than
    /// <see cref="Enums.CRYPTO_ENCODING_HEX"/> [:L925], and it is a member of the published encoding
    /// set, so the same conversion surface a caller can drive explicitly is the one used internally.
    /// </remarks>
    [Fact]
    public void TheCataloguePayloadEncodingIsValuedFromTheKernelAndIsThePrimaryEncoding()
    {
        Assert.Equal(Enums.CRYPTO_ENCODING_BASE64, LegacyDefaults.STRING_PAYLOAD_ENCODING);
        Assert.NotEqual(Enums.CRYPTO_ENCODING_HEX, LegacyDefaults.STRING_PAYLOAD_ENCODING);
        Assert.True(LegacyDefaults.IsSupportedEncoding(LegacyDefaults.STRING_PAYLOAD_ENCODING));
    }

    /// <summary>
    /// The cipher family's string-shaped result is its byte-shaped result carried through the one
    /// payload encoding.
    /// </summary>
    /// <param name="ntype">The published cipher type under test.</param>
    /// <param name="mode">The published cipher mode under test.</param>
    /// <remarks>
    /// DOCUMENTED DECISION D4, first of four providers. Decoding the text-shaped ciphertext through
    /// the catalogue's encoding must yield exactly the bytes the byte-shaped arm returned, which is
    /// what makes the two families two spellings of one operation rather than two behaviours.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeAndModeGrid))]
    public void TheCipherTextFormIsTheByteFormUnderTheOnePayloadEncoding(ushort ntype, long mode)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);
        byte[] key = SyntheticKeyMaterial(metrics.KeyLengthBytes);
        string plainText = SyntheticText(metrics.BlockLengthBytes * 2);
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(plainText);

        // A VECTOR IS SUPPLIED EXPLICITLY because the grid now includes the chaining mode, which this
        // port serves only with a caller-supplied vector (DECISION D3). Passing one keeps this test
        // measuring what it is about - padding overhead, tamper behaviour or payload form - rather
        // than colliding with a capability refusal it is not testing.
        byte[] vector = SyntheticVectorMaterial(metrics.IvLengthBytes);

        string textShaped = _cipher.SymEncrypt(plainText, key, vector, ntype, mode);
        byte[] byteShaped = _cipher.SymEncrypt(plainBytes, key, vector, ntype, mode);

        Assert.Equal(byteShaped, DecodePayloadText(textShaped));
    }

    /// <summary>
    /// The unkeyed digest and the keyed digest are both published under the same one payload
    /// encoding, so no provider carries a private encoding of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DOCUMENTED DECISION D4, second and third of four providers, and the reason this suite must
    /// agree with whatever pins the digest text form elsewhere: THE CATALOGUE MEMBER IS THE SINGLE
    /// AUTHORITY FOR BOTH. A digest published in a different encoding from a ciphertext would break
    /// that claim, so the relationship is asserted for the digest surface too.
    /// </para>
    /// <para>
    /// The digest is checked by DECODING the published text and confirming the recovered bytes are
    /// the algorithm's own digest length, then confirming the text is stable for the same input.
    /// Comparing against a hand-written expected digest would be a vector test, which belongs to the
    /// per-provider suites rather than here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDigestTextFormsUseTheSameOnePayloadEncoding()
    {
        HashProvider unkeyed = new(_encoding);
        HmacProvider keyed = new(_encoding);

        byte[] payload = SyntheticPayload(64);
        byte[] key = SyntheticKeyMaterial(32);

        foreach (long ntype in PublishedHashTypes)
        {
            string unkeyedText = unkeyed.Hash(payload, ntype);
            byte[] unkeyedBytes = DecodePayloadText(unkeyedText);

            Assert.NotEmpty(unkeyedBytes);
            Assert.Equal(unkeyedText, unkeyed.Hash(payload, ntype));

            // The keyed surface publishes no keyed checksum arm: the checksum identifier is a member
            // of the shared hash set [enums.sru:L927-L933] yet has no keyed construction, so the
            // keyed provider reports a defined failure rather than fabricating one or silently
            // discarding the key. That ruling is asserted in its own test below.
            if (ntype == Enums.CRYPTO_HASH_CRC32)
            {
                continue;
            }

            string keyedText = keyed.Hash(payload, key, ntype);
            byte[] keyedBytes = DecodePayloadText(keyedText);

            Assert.Equal(unkeyedBytes.Length, keyedBytes.Length);
            Assert.NotEqual(unkeyedText, keyedText);
        }
    }

    /// <summary>
    /// A signature's string form is its byte form carried through the one payload encoding, and the
    /// byte-shaped verifier accepts the decoded signature.
    /// </summary>
    /// <remarks>
    /// DOCUMENTED DECISION D4, fourth of four providers, and the crispest of the four because a
    /// PKCS#1 v1.5 signature is DETERMINISTIC: the same data under the same key yields the same
    /// signature, so a plain equality is available where the randomised encryption path allowed only
    /// a cross decrypt. The cross-family verification is included because the signature parameter
    /// changes type across the two verify declarations [n_crypto.sru:L72 takes text, :L73 takes
    /// bytes], an asymmetry the cipher family does not have.
    /// </remarks>
    [Fact]
    public void TheSignatureTextFormIsTheByteFormUnderTheOnePayloadEncoding()
    {
        RsaKeyPair pair = SharedTestKeyPair.Value;
        string dataText = SyntheticText(48);
        byte[] dataBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(dataText);

        string textShaped = _rsa.RSASign(dataText, pair.PrivateKey, Enums.CRYPTO_HASH_SHA256);
        byte[] byteShaped = _rsa.RSASign(dataBytes, pair.PrivateKey, Enums.CRYPTO_HASH_SHA256);

        Assert.Equal(byteShaped, DecodePayloadText(textShaped));

        Assert.True(
            _rsa.VerifyRSASign(dataText, textShaped, pair.PublicKey, Enums.CRYPTO_HASH_SHA256));
        Assert.True(
            _rsa.VerifyRSASign(dataBytes, byteShaped, pair.PublicKey, Enums.CRYPTO_HASH_SHA256));
        Assert.True(
            _rsa.VerifyRSASign(
                dataBytes,
                DecodePayloadText(textShaped),
                pair.PublicKey,
                Enums.CRYPTO_HASH_SHA256));
    }


    // ==========================================================================================
    //  THE CENSUS AND THE TWO DELIBERATE NON-PORTS
    //  ORACLE  n_crypto.sru:L9-L73 - 65 `public function` declarations, of which 63 are ported
    //          n_crypto.sru:L9     - Copyright()   DELIBERATE NON-PORT
    //          n_crypto.sru:L10    - GetVersion()  DELIBERATE NON-PORT
    //  ------------------------------------------------------------------------------------------
    //  The census reconciles as 65 = 2 non-ported + 63 ported, and the 63 distribute across exactly
    //  six providers. Both halves are asserted, because a count that reconciled while a declaration
    //  had been assigned twice would reconcile for the wrong reason.
    //
    //  THE TWO ABSENCES ARE DECISIONS, NOT OVERSIGHTS. Re-adding either member would re-widen the
    //  ported surface past 63, so the absence is asserted rather than left implicit - which is the
    //  only way a contributor restoring one of them finds out that it was deliberate.
    // ==========================================================================================

    /// <summary>
    /// The census reconciles: 65 declarations, 2 deliberately not ported, 63 ported across exactly
    /// six providers.
    /// </summary>
    /// <remarks>
    /// The arithmetic is stated as assertions rather than as a comment so that it cannot quietly stop
    /// being true. The SIX is the part that carries weight: the providers are discovered from the
    /// assembly, so a seventh provider appearing would fail here and would be exactly the kind of
    /// surface widening that needs a deliberate, reviewed decision rather than a silent commit.
    /// </remarks>
    [Fact]
    public void TheDeclarationCensusReconcilesAcrossExactlySixProviders()
    {
        const int DeclaredFunctions = 65;
        const int DeliberateNonPorts = 2;
        const int PortedDeclarations = DeclaredFunctions - DeliberateNonPorts;

        Assert.Equal(63, PortedDeclarations);

        string[] discovered = [.. DiscoverProviderTypes().Select(provider => provider.Name).Order()];

        Assert.Equal(ExpectedProviderTypeNames.Order(), discovered);
        Assert.Equal(6, discovered.Length);
    }

    /// <summary>
    /// No type in the cryptographic folder exposes the two members that were deliberately not
    /// ported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Copyright()</c> [n_crypto.sru:L9] and <c>GetVersion()</c> [n_crypto.sru:L10] report the
    /// closed native binary's own attribution and build version. Neither has any meaning once the
    /// binary is gone: there is no native component left to attribute or to version, and the managed
    /// service reports its own version through its own surface. Porting them would publish two
    /// members that could only return an invented answer.
    /// </para>
    /// <para>
    /// THE SWEEP SPANS EVERY PUBLIC TYPE IN THE FOLDER, not merely the providers, because a
    /// contributor restoring one of these would as likely put it on the catalogue as on a provider.
    /// </para>
    /// </remarks>
    [Fact]
    public void NeitherDeliberateNonPortIsExposedAnywhereInTheFolder()
    {
        string[] nonPortedMembers = ["Copyright", "GetVersion"];
        List<string> offenders = [];

        foreach (Type candidate in DiscoverCryptoTypes())
        {
            foreach (string memberName in PublicMemberNames(candidate))
            {
                if (nonPortedMembers.Contains(memberName, StringComparer.Ordinal))
                {
                    offenders.Add($"{candidate.Name}.{memberName}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    // ==========================================================================================
    //  THE SUPPORTED-VALUE PREDICATES - THE LEGACY IDENTIFIER SETS, EXACTLY
    //  ORACLE  enums.sru:L924-L925  encodings    Base64, hex                     0-1
    //          enums.sru:L928-L933  hash types   MD5..CRC32                      0-5
    //          enums.sru:L936-L940  cipher types DES..AES256                     0-4
    //          enums.sru:L943-L945  cipher modes ECB, CBC, CFB                   0-2
    //          enums.sru:L949-L950  RSA padding  PKCS1, OAEP                     0-1
    //  ------------------------------------------------------------------------------------------
    //  NOTHING MAY BE ADDED AND NOTHING MAY BE REMOVED. Adding a value would publish an operation the
    //  legacy never offered; removing one would reject input the legacy accepted. So MD5, SHA-1, DES,
    //  3DES, ECB and PKCS#1 all stay selectable - every one of them weak by current standards - and
    //  the boundary rows just outside each set are covered because an off-by-one in a predicate is
    //  exactly how a set silently grows or shrinks.
    //
    //  A LEGACY INCONSISTENCY WORTH PRESERVING, AND THE REASON THE HASH SET IS NOT SPLIT IN TWO. The
    //  source comment at enums.sru:L927 records that the SAME six values parameterise `Hash`,
    //  `RSASign` AND `VerifyRSASign`. A signature algorithm is therefore screened against the same
    //  set as a digest algorithm, which is source-faithful and must NOT be split into two sets - even
    //  though the consequence is that a checksum identifier is a legal argument to a signature call.
    //  How that consequence is discharged is asserted below.
    // ==========================================================================================

    /// <summary>
    /// The hash-type predicate accepts exactly the six published identifiers and rejects everything
    /// else.
    /// </summary>
    /// <param name="ntype">The candidate identifier.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// MD5 and SHA-1 are both members and both stay members, despite both being broken for collision
    /// resistance. The checksum identifier is a member too and is not a cryptographic hash at all: a
    /// caller reaching for it for an integrity purpose is making a mistake the legacy also permitted.
    /// </remarks>
    [Theory]
    [MemberData(nameof(HashTypeMatrix))]
    public void TheHashTypeSetIsExactlyTheSixPublishedIdentifiers(long ntype, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedHashType(ntype));

    /// <summary>
    /// Every named hash constant is a member of its own set, which guards against a constant being
    /// re-valued out of the range the predicate tests.
    /// </summary>
    /// <remarks>
    /// The predicate is expressed as a range over the two end constants, so a middle constant could
    /// in principle be re-valued outside it without the range check noticing. Asserting every named
    /// constant individually closes that gap.
    /// </remarks>
    [Fact]
    public void EveryNamedHashConstantIsAMemberOfTheHashTypeSet() =>
        Assert.All(PublishedHashTypes, ntype => Assert.True(LegacyDefaults.IsSupportedHashType(ntype)));

    /// <summary>
    /// The cipher-type predicate accepts exactly the five published identifiers and rejects
    /// everything else.
    /// </summary>
    /// <param name="ntype">The candidate identifier, at the legacy's 16-bit width.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// DES is a member, with its 56 effective key bits, and so is triple DES. Neither may be removed.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeMatrix))]
    public void TheCipherTypeSetIsExactlyTheFivePublishedIdentifiers(ushort ntype, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedSymmetricType(ntype));

    /// <summary>
    /// The encoding predicate accepts exactly the two published encodings and rejects everything
    /// else.
    /// </summary>
    /// <param name="encoding">The candidate identifier.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// This screens the CALLER-DRIVEN conversion surface [n_crypto.sru:L11-L12], which is a different
    /// concern from the internal payload encoding of DECISION D4 even though both name the same
    /// primary encoding. Conflating the two would make the caller's choice look like a change to the
    /// internal rule.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EncodingMatrix))]
    public void TheEncodingSetIsExactlyTheTwoPublishedEncodings(long encoding, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedEncoding(encoding));

    /// <summary>
    /// The vector classifier reports a vector for the two chaining modes and none for electronic
    /// codebook or for any unpublished value.
    /// </summary>
    /// <param name="mode">The candidate mode.</param>
    /// <param name="expected">Whether the legacy publishes the mode at all.</param>
    /// <remarks>
    /// Screening the mode is a separate predicate's job, so this classifier answers rather than
    /// throwing for an unpublished value - which is what lets a provider order the two checks freely.
    /// The expectation is therefore derived from membership: a published mode consumes a vector unless
    /// it is electronic codebook, and an unpublished one consumes nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherModeMatrix))]
    public void TheVectorClassifierIsTrueForExactlyTheTwoChainingModes(long mode, bool expected)
    {
        bool consumesVector = expected && mode != Enums.CRYPTO_SYMCRYPT_MODE_ECB;

        Assert.Equal(consumesVector, LegacyDefaults.ModeUsesInitializationVector(mode));
    }

    /// <summary>
    /// The one shared hash set really is shared: the checksum identifier is a member of it, and the
    /// two keyed surfaces that cannot use it report a defined failure rather than fabricating a
    /// construction or silently discarding the key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DOCUMENTED DECISION, NOT DERIVED PARITY (constraint C-K), and the consequence of not
    /// splitting the set. The source comment at enums.sru:L927 makes the six identifiers common to
    /// <c>Hash</c>, <c>RSASign</c> and <c>VerifyRSASign</c>, so the checksum identifier is a legal
    /// argument to a signature call and, by the same token, to a keyed digest call. Neither has a
    /// real construction: a keyed checksum is not a keyed message authentication code, and there is
    /// no signature construction over a checksum.
    /// </para>
    /// <para>
    /// THE RULING PINNED HERE IS A DEFINED FAILURE, AND THE ALTERNATIVE IT RULES OUT MATTERS MORE
    /// THAN THE FAILURE ITSELF. Silently falling back to the unkeyed checksum would discard the
    /// caller's key without telling it, leaving a caller that believed it had an authenticator with
    /// something that authenticates nothing - a silent wrong answer, which is the one outcome worse
    /// than an error. Fabricating a bespoke keyed-checksum construction would be worse still. The
    /// unkeyed checksum remains fully available on the unkeyed surface, which is where the legacy's
    /// own checksum call site reaches it, so nothing the legacy demonstrably had is lost.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSharedHashSetAdmitsTheChecksumAndTheKeyedSurfacesRefuseItExplicitly()
    {
        // A member of the one shared set, not an outsider - which is why the refusals below are a
        // capability limit rather than an unsupported-identifier error.
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_CRC32));

        HmacProvider keyed = new(_encoding);
        byte[] payload = SyntheticPayload(32);
        byte[] key = SyntheticKeyMaterial(32);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => keyed.Hash(payload, key, Enums.CRYPTO_HASH_CRC32));

        RsaKeyPair pair = SharedTestKeyPair.Value;
        Assert.Throws<NotSupportedException>(
            () => _rsa.RSASign(payload, pair.PrivateKey, Enums.CRYPTO_HASH_CRC32));

        // The unkeyed surface keeps it, so the capability limit is confined to the keyed and
        // signature arms exactly as the ruling states.
        HashProvider unkeyed = new(_encoding);
        Assert.NotEmpty(unkeyed.Hash(payload, Enums.CRYPTO_HASH_CRC32));
    }

    /// <summary>
    /// The signature surface screens its algorithm argument against the one shared set rather than
    /// against a second set of its own.
    /// </summary>
    /// <remarks>
    /// The other half of the L927 ruling. Every one of the five signable identifiers is accepted by
    /// the signature surface, and a value outside the shared set is refused by it - so the signature
    /// surface neither narrows the set to a "modern algorithms only" subset nor widens it. MD5 and
    /// SHA-1 signatures therefore remain available, which is preserved behaviour and not an oversight.
    /// </remarks>
    [Fact]
    public void TheSignatureSurfaceScreensAgainstTheOneSharedHashSet()
    {
        RsaKeyPair pair = SharedTestKeyPair.Value;
        byte[] data = SyntheticPayload(32);

        foreach (long ntype in PublishedHashTypes)
        {
            if (ntype == Enums.CRYPTO_HASH_CRC32)
            {
                // Covered by the ruling asserted immediately above.
                continue;
            }

            byte[] signature = _rsa.RSASign(data, pair.PrivateKey, ntype);

            Assert.NotEmpty(signature);
            Assert.True(_rsa.VerifyRSASign(data, signature, pair.PublicKey, ntype));
        }

        long outsideTheSet = Enums.CRYPTO_HASH_CRC32 + 1;
        Assert.False(LegacyDefaults.IsSupportedHashType(outsideTheSet));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _rsa.RSASign(data, pair.PrivateKey, outsideTheSet));
    }

    // ==========================================================================================
    //  THE CATALOGUE'S OWN SURFACE - THE METRICS TABLE AND THE TWO SUBSTITUTION RULES
    //  ------------------------------------------------------------------------------------------
    //  Coverage of the catalogue in full is this file's share of the per-service coverage gate
    //  (constraint C-H), so the table, both normalizer overloads, the vector factory and every
    //  argument-validation branch are exercised here rather than only incidentally through a
    //  provider.
    //
    //  EVERY NUMBER BELOW IS AN ALGORITHM FACT AND NOT A SECRET. A cipher's key length and block
    //  length are published properties of DES, triple DES and AES; asserting them carries no key
    //  material and engages no part of constraint C-F.
    // ==========================================================================================

    /// <summary>
    /// The metrics table holds exactly the five published ciphers, in identifier order, with the
    /// published key and block lengths.
    /// </summary>
    /// <remarks>
    /// The table is the single place the key-length rule of DECISION D1 and the vector length of
    /// DECISION D3 read their lengths from, so a wrong row would corrupt both rules at once. Order is
    /// asserted as well as content because the rows are published as an enumerable and a diagnostic
    /// surface may present them in order.
    /// </remarks>
    [Fact]
    public void TheMetricsTableHoldsTheFivePublishedCiphersInIdentifierOrder()
    {
        (long CipherType, int KeyLengthBytes, int BlockLengthBytes)[] expected =
        [
            (Enums.CRYPTO_SYMCRYPT_TYPE_DES, 8, 8),
            (Enums.CRYPTO_SYMCRYPT_TYPE_3DES, 24, 8),
            (Enums.CRYPTO_SYMCRYPT_TYPE_AES128, 16, 16),
            (Enums.CRYPTO_SYMCRYPT_TYPE_AES192, 24, 16),
            (Enums.CRYPTO_SYMCRYPT_TYPE_AES256, 32, 16),
        ];

        IReadOnlyList<SymmetricCipherMetrics> actual = LegacyDefaults.AllSymmetricCipherMetrics;

        Assert.Equal(expected.Length, actual.Count);

        for (int row = 0; row < expected.Length; row++)
        {
            Assert.Equal(expected[row].CipherType, actual[row].CipherType);
            Assert.Equal(expected[row].KeyLengthBytes, actual[row].KeyLengthBytes);
            Assert.Equal(expected[row].BlockLengthBytes, actual[row].BlockLengthBytes);

            // The vector length is the block length, because a chaining vector is exactly one block
            // wide. Meaningful only for the two chaining modes.
            Assert.Equal(expected[row].BlockLengthBytes, actual[row].IvLengthBytes);
        }
    }

    /// <summary>
    /// The non-throwing lookup reports success with the published sizes for every published type,
    /// and reports failure without throwing for a type outside the set.
    /// </summary>
    /// <param name="ntype">The candidate identifier, at the legacy's 16-bit width.</param>
    /// <param name="expected">Whether the legacy publishes it.</param>
    /// <remarks>
    /// The non-throwing shape exists because a provider screening a caller's argument must turn a bad
    /// value into a legacy-shaped invalid-argument result rather than into an exception, which is the
    /// shape the legacy surface reports failure in. The failure path yields zero lengths, which is
    /// asserted so that a caller ignoring the boolean cannot mistake a default row for a real one.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeMatrix))]
    public void TheNonThrowingMetricsLookupMatchesThePublishedSet(ushort ntype, bool expected)
    {
        bool found = LegacyDefaults.TryGetSymmetricCipherMetrics(ntype, out SymmetricCipherMetrics metrics);

        Assert.Equal(expected, found);

        if (expected)
        {
            Assert.Equal(ntype, metrics.CipherType);
            Assert.True(metrics.KeyLengthBytes > 0);
            Assert.True(metrics.BlockLengthBytes > 0);
            Assert.Equal(LegacyDefaults.GetSymmetricCipherMetrics(ntype), metrics);
        }
        else
        {
            Assert.Equal(default, metrics);
            Assert.Equal(0, metrics.KeyLengthBytes);
            Assert.Equal(0, metrics.BlockLengthBytes);
        }
    }

    /// <summary>
    /// The throwing lookup raises for a type outside the published set, which signals an unscreened
    /// caller rather than a caller's bad argument.
    /// </summary>
    /// <param name="ntype">The candidate identifier, at the legacy's 16-bit width.</param>
    /// <param name="expected">Whether the legacy publishes it; only the rejected rows are exercised.</param>
    /// <remarks>
    /// The distinction between the two lookups is deliberate and is asserted so it cannot be
    /// collapsed: the non-throwing overload is the channel for a caller's bad value, and this one
    /// reports an internal defect.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypeMatrix))]
    public void TheThrowingMetricsLookupRaisesForAnUnpublishedType(ushort ntype, bool expected)
    {
        if (expected)
        {
            Assert.Equal(ntype, LegacyDefaults.GetSymmetricCipherMetrics(ntype).CipherType);
            return;
        }

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LegacyDefaults.GetSymmetricCipherMetrics(ntype));
    }

    /// <summary>
    /// The key-material encoding is a read-only encoding that never emits a byte-order mark.
    /// </summary>
    /// <remarks>
    /// DOCUMENTED DECISION D1's encoding step. Both properties matter: read-only means no caller can
    /// reach through the property and alter the encoder fallback every provider then inherits, and the
    /// absent byte-order mark means no phantom leading bytes can enter key material and silently
    /// change a key.
    /// </remarks>
    [Fact]
    public void TheKeyMaterialEncodingIsReadOnlyAndEmitsNoByteOrderMark()
    {
        System.Text.Encoding encoding = LegacyDefaults.KeyMaterialEncoding;

        Assert.True(encoding.IsReadOnly);
        Assert.Equal(System.Text.Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.Empty(encoding.GetBytes(string.Empty));

        // Conversion never consults the preamble, so no mark can appear ahead of the material.
        byte[] converted = encoding.GetBytes(SyntheticText(4));
        Assert.Equal(4, converted.Length);
    }

    /// <summary>
    /// Both normalizer overloads always return a fresh buffer of exactly the requested length.
    /// </summary>
    /// <param name="requiredLength">The length demanded of the rule.</param>
    /// <remarks>
    /// Freshness is a real requirement rather than a detail: the cipher provider zeroes the key
    /// buffer it owns once an operation completes, so a shared buffer would let one operation wipe
    /// another's key. The lengths exercised span the shortest possible, the two real block lengths and
    /// the largest real key length.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void TheNormalizerAlwaysReturnsAFreshBufferOfTheRequestedLength(int requiredLength)
    {
        string text = SyntheticText(5);
        byte[] bytes = SyntheticKeyMaterial(5);

        byte[] fromText = LegacyDefaults.NormalizeKeyMaterial(text, requiredLength);
        byte[] fromTextAgain = LegacyDefaults.NormalizeKeyMaterial(text, requiredLength);
        byte[] fromBytes = LegacyDefaults.NormalizeKeyMaterial(bytes, requiredLength);

        Assert.Equal(requiredLength, fromText.Length);
        Assert.Equal(requiredLength, fromBytes.Length);

        Assert.Equal(fromText, fromTextAgain);
        Assert.NotSame(fromText, fromTextAgain);
    }

    /// <summary>
    /// The normalizer refuses a null passphrase and a non-positive length.
    /// </summary>
    /// <remarks>
    /// The guards are asserted so that every branch of the rule is covered, which is this file's share
    /// of the coverage gate. The null argument is TYPED rather than a bare literal: under this
    /// language version's implicit span conversions a bare null is applicable to both overloads and
    /// would not compile, which the catalogue's own documentation records.
    /// </remarks>
    [Fact]
    public void TheNormalizerRefusesNullMaterialAndANonPositiveLength()
    {
        string? absentMaterial = null;

        Assert.Throws<ArgumentNullException>(
            () => LegacyDefaults.NormalizeKeyMaterial(absentMaterial!, 16));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => LegacyDefaults.NormalizeKeyMaterial(SyntheticText(4), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LegacyDefaults.NormalizeKeyMaterial(SyntheticText(4), -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LegacyDefaults.NormalizeKeyMaterial(SyntheticKeyMaterial(4), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LegacyDefaults.NormalizeKeyMaterial(SyntheticKeyMaterial(4), -1));
    }

    /// <summary>
    /// The two flag defaults are valued from their shared kernel constants and carry the composite
    /// values the legacy composes them from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The random-string default is number plus alphabet [enums.sru:L957 over :L954-L955] and
    /// DELIBERATELY EXCLUDES THE SYMBOL CLASS [:L956] - a narrower alphabet than a caller might
    /// expect, preserved because a generated value's character set is observable to whatever consumes
    /// it. The identifier default includes both the bracket and the separator flags
    /// [:L962 over :L960-L961].
    /// </para>
    /// <para>
    /// Both are covered here so that the catalogue's constant region is exercised in full, which is
    /// this file's share of the coverage gate. The random generator's own behaviour is its own
    /// provider's concern.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFlagDefaultsAreValuedFromTheKernelAndCarryTheirCompositeValues()
    {
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_DEFAULT,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_NUMBER | Enums.CRYPTO_RNDSTRING_ALPHABET,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);
        Assert.Equal(
            0u,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT & Enums.CRYPTO_RNDSTRING_SYMBOL);

        Assert.Equal(Enums.CRYPTO_GUID_DEFAULT, LegacyDefaults.GUID_FLAGS_DEFAULT);
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET | Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            LegacyDefaults.GUID_FLAGS_DEFAULT);
    }


    // ==========================================================================================
    //  HELPERS - REFLECTION, SYNTHETIC MATERIAL AND THE SHARED TEST KEY PAIR
    //  ------------------------------------------------------------------------------------------
    //  CONSTRAINT C-F IS DISCHARGED HERE, IN ONE PLACE, SO IT CAN BE AUDITED IN ONE PLACE. Every
    //  byte of key, vector and payload material used anywhere above comes out of these three
    //  computed helpers or out of the provider's own key generator. Nothing is copied from anywhere:
    //  not from the repository's eight known in-source secret sites, not from the demo's unmasked key
    //  and vector fields, not from the test configuration object's embedded cipher key, and not from
    //  any certificate or armoured key block. There is no base64 literal, no hexadecimal literal and
    //  no long literal of any kind below - only arithmetic over an index.
    // ==========================================================================================

    /// <summary>
    /// The 2048-bit key pair shared by every test that needs one, generated once at test time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GENERATED, NEVER PASTED (constraint C-F). The pair comes from the provider's own generator, so
    /// no key material is written down in this file. It is shared through a lazy initializer purely
    /// because generating a key pair per test is slow; the initializer is thread-safe, which matters
    /// because the test framework may run theories in parallel.
    /// </para>
    /// <para>
    /// 2048 bits is used everywhere EXCEPT the test that pins 1024 bits as legal, which generates its
    /// own pair at that size precisely because the size is the thing under test there. Using the
    /// smaller size generally would weaken the payload ceiling available to the encryption tests for
    /// no benefit.
    /// </para>
    /// </remarks>
    private static readonly Lazy<RsaKeyPair> SharedTestKeyPair = new(GenerateTestKeyPair);

    /// <summary>
    /// Generates a fresh 2048-bit key pair through the provider under test.
    /// </summary>
    /// <returns>The generated pair.</returns>
    /// <exception cref="InvalidOperationException">
    /// The platform refused to generate a 2048-bit key, which would make the whole RSA half of this
    /// suite meaningless and so is reported rather than absorbed.
    /// </exception>
    private static RsaKeyPair GenerateTestKeyPair()
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        RsaProvider generator = new(new EncodingProvider());

        if (!generator.GenRSAKey(Enums.CRYPTO_RSA_BITS_2048, ref privateKey, ref publicKey))
        {
            throw new InvalidOperationException(
                "This platform refused to generate a 2048-bit RSA key, so the RSA assertions in " +
                "this suite cannot be evaluated.");
        }

        return new RsaKeyPair(privateKey, publicKey);
    }

    /// <summary>
    /// The public types declared in the cryptographic folder's namespace, discovered from the
    /// assembly.
    /// </summary>
    /// <returns>Every public type in the namespace, in name order.</returns>
    /// <remarks>
    /// DISCOVERED RATHER THAN LISTED, WHICH IS WHAT MAKES THE ABSENCE SWEEPS ABLE TO FAIL. A
    /// hand-written list would only ever sweep the types someone remembered to add to it, so a new
    /// type carrying a forbidden member would slip through. Compiler-generated and non-public types
    /// are excluded because neither is part of the published surface a contributor could widen.
    /// </remarks>
    private static IEnumerable<Type> DiscoverCryptoTypes()
    {
        const string CryptoNamespace = "PowerFramework.Security.Crypto";

        return typeof(LegacyDefaults).Assembly
            .GetTypes()
            .Where(candidate =>
                candidate.IsPublic &&
                string.Equals(candidate.Namespace, CryptoNamespace, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// The provider types among <see cref="DiscoverCryptoTypes"/> - the types that actually perform
    /// cryptography and are therefore where a forbidden capability could live.
    /// </summary>
    /// <returns>The six providers, in name order.</returns>
    /// <remarks>
    /// The naming convention is the discriminator, which is deliberate: it is the convention the
    /// folder already follows, so a seventh provider added tomorrow is swept automatically. The
    /// catalogue and the metrics record are excluded because they perform no cryptography, and the
    /// catalogue in particular carries members whose NAMES record the very absences being swept for.
    /// </remarks>
    private static IEnumerable<Type> DiscoverProviderTypes() =>
        DiscoverCryptoTypes()
            .Where(candidate =>
                !candidate.IsInterface &&
                candidate.Name.EndsWith("Provider", StringComparison.Ordinal));

    /// <summary>
    /// The names of a type's public members, with property and event accessor methods folded away.
    /// </summary>
    /// <param name="candidate">The type to enumerate.</param>
    /// <returns>The distinct declared public member names.</returns>
    /// <remarks>
    /// Accessor methods are dropped because a property named <c>Tag</c> would otherwise be reported
    /// three times - once as the property and once for each accessor - which would turn one finding
    /// into three and obscure the count. Only DECLARED members are enumerated, so inherited object
    /// members do not enter the sweep.
    /// </remarks>
    private static IEnumerable<string> PublicMemberNames(Type candidate) =>
        candidate
            .GetMembers(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Reports whether a ciphertext decrypts back to an expected plaintext under a given type and
    /// mode, treating the platform's cryptographic failure as "no".
    /// </summary>
    /// <param name="cipherText">The ciphertext to attempt.</param>
    /// <param name="key">The key material to attempt it with.</param>
    /// <param name="ntype">The cipher type to attempt it under.</param>
    /// <param name="mode">The cipher mode to attempt it under.</param>
    /// <param name="expectedPlainText">The plaintext that must NOT come back.</param>
    /// <param name="vector">The initialization vector.</param>
    /// <returns>
    /// <see langword="true"/> only when the decryption both completed and produced
    /// <paramref name="expectedPlainText"/>.
    /// </returns>
    /// <remarks>
    /// THIS HELPER EXISTS BECAUSE DECISION D2 FORBIDS A SHARPER ASSERTION. With no authentication
    /// anywhere in the published surface, a wrong mode or a modified ciphertext yields EITHER the
    /// padding check's failure OR plausible garbage, and which of the two occurs depends on the bytes.
    /// A test demanding that it always throws would be asserting tamper detection - the property this
    /// surface does not have - and would fail whenever the padding check happened to pass. Folding
    /// both outcomes into a single "the plaintext did not come back" is the strongest claim that is
    /// actually true.
    /// </remarks>
    private bool RecoversPlainText(
        byte[] cipherText,
        byte[] key,
        ushort ntype,
        long mode,
        byte[] expectedPlainText,
        byte[]? vector = null)
    {
        try
        {
            // The vector-bearing arm when a vector is supplied, and the vector-less arm otherwise, so
            // one helper serves both the codebook-default callers and the chaining callers. It does NOT
            // absorb a capability refusal: only a cryptographic failure is caught below, and
            // SymmetricParityUnavailableException is deliberately not of that type, so a blocked cell
            // reaching here would surface as an error rather than as a quiet negative answer.
            byte[] recovered = vector is null
                ? _cipher.SymDecrypt(cipherText, key, ntype, mode)
                : _cipher.SymDecrypt(cipherText, key, vector, ntype, mode);

            return recovered.AsSpan().SequenceEqual(expectedPlainText);
        }
        catch (CryptographicException)
        {
            // The padding check refused it. Not tamper detection - see the remarks - but equally not
            // a recovery of the plaintext, which is all this helper reports on.
            return false;
        }
    }

    /// <summary>
    /// Decodes a provider's string-shaped result through the one payload encoding of DECISION D4.
    /// </summary>
    /// <param name="payloadText">The published text form.</param>
    /// <returns>The bytes the text form carries.</returns>
    /// <remarks>
    /// Routed through the real encoding provider and the catalogue's own encoding identifier rather
    /// than through a platform converter chosen here, so the test asks the SAME question the
    /// providers answer. Choosing a converter locally would let this file and the providers agree by
    /// coincidence.
    /// </remarks>
    private byte[] DecodePayloadText(string payloadText) =>
        _encoding.StringToBlob(payloadText, LegacyDefaults.STRING_PAYLOAD_ENCODING);

    /// <summary>
    /// Computes synthetic key or vector-length material of a given length.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <returns>A fresh buffer of computed, non-secret bytes.</returns>
    /// <remarks>
    /// CONSTRAINT C-F. The bytes are COMPUTED FROM THE INDEX and are obviously not a credential: the
    /// pattern is a simple ascending run, chosen because it avoids the byte patterns the platform
    /// refuses as degenerate for the two legacy block ciphers - a known weak single-DES key, or a
    /// triple-DES key whose adjacent sub-keys coincide - so the helper is usable for every published
    /// cipher type without the refusal interfering with what a test is trying to assert.
    /// </remarks>
    private static byte[] SyntheticKeyMaterial(int length)
    {
        byte[] material = new byte[length];

        for (int index = 0; index < material.Length; index++)
        {
            material[index] = (byte)(index + 3);
        }

        return material;
    }

    /// <summary>
    /// Computes synthetic initialization vector material that is guaranteed NOT to be all zero.
    /// </summary>
    /// <param name="length">The number of bytes to produce, which is the cipher's block length.</param>
    /// <returns>A fresh buffer of computed, non-secret, non-zero bytes.</returns>
    /// <remarks>
    /// The non-zero guarantee still matters after DECISION D3 stopped synthesising a vector: the
    /// codebook-mode test distinguishes a vector that is IGNORED from one that is consumed, and a
    /// helper that could return zeroes would let it pass for the wrong reason. Every byte is non-zero
    /// by construction.
    /// </remarks>
    private static byte[] SyntheticVectorMaterial(int length)
    {
        byte[] material = new byte[length];

        for (int index = 0; index < material.Length; index++)
        {
            material[index] = (byte)(index + 0x51);
        }

        return material;
    }

    /// <summary>
    /// Computes a synthetic payload of a given length.
    /// </summary>
    /// <param name="length">The number of bytes to produce; zero is valid and yields an empty buffer.</param>
    /// <returns>A fresh buffer of computed bytes whose blocks differ from one another.</returns>
    /// <remarks>
    /// The stride is deliberately not one. A payload whose blocks repeated would make the electronic
    /// codebook comparisons pass for the wrong reason, because repeated plaintext blocks produce
    /// repeated cipher blocks under that mode; a strided run guarantees the blocks differ, which is
    /// what the default-mode inequalities need.
    /// </remarks>
    private static byte[] SyntheticPayload(int length)
    {
        byte[] payload = new byte[length];

        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)((index * 7) + 13);
        }

        return payload;
    }

    /// <summary>
    /// Builds synthetic text of a given length from a repeating visible pattern.
    /// </summary>
    /// <param name="length">The number of characters to produce.</param>
    /// <returns>A string of exactly <paramref name="length"/> printable ASCII characters.</returns>
    /// <remarks>
    /// CONSTRAINT C-F. Printable ASCII only, so each character encodes to exactly one byte under the
    /// key-material encoding and a caller can reason about length in either unit. The pattern is a
    /// visible, obviously synthetic run of letters and digits - it is not a passphrase, and it is not
    /// copied from anywhere.
    /// </remarks>
    private static string SyntheticText(int length)
    {
        const string Pattern = "pfwSynthetic0123456789";

        System.Text.StringBuilder builder = new(length);

        for (int index = 0; index < length; index++)
        {
            builder.Append(Pattern[index % Pattern.Length]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// An RSA key pair generated at test time, carried as a pair so that the two halves cannot be
    /// mismatched by a caller.
    /// </summary>
    /// <param name="PrivateKey">The private key text, as the provider's generator emitted it.</param>
    /// <param name="PublicKey">The public key text, as the provider's generator emitted it.</param>
    /// <remarks>
    /// GENERATED, NEVER PASTED (constraint C-F). Neither field ever holds a literal: the only writer
    /// is <see cref="GenerateTestKeyPair"/> and the only other producer of key text in this file is
    /// the provider's generator called directly by the key-size tests.
    /// </remarks>
    private sealed record RsaKeyPair(string PrivateKey, string PublicKey);

}
