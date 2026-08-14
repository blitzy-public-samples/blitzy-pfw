// ==============================================================================================
//  HmacProviderTests - the characterization suite that pins
//  PowerFramework.Security.Crypto.HmacProvider
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/HmacProvider.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L23-L26   keyed Hash     x4
//                     ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L28-L29   keyed HashFile x2
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L927-L933    the six hash types
//                     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449
//                                                                          the one keyed call site
//                     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L281
//                                                                          the only CRC32 call
//                                                                          site, which is UNKEYED
//                     all READ-ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE CAN AND CANNOT BE EVIDENCE OF
//  --------------------------------------------------------------------------------------------
//  n_crypto is a PBNI class over the closed pfw.dll: the .sru declares 65 prototypes and no body
//  exists anywhere in the repository. What separates this suite from its siblings is that HMAC is
//  a PUBLICLY SPECIFIED construction with PUBLISHED KNOWN-ANSWER VECTORS, so the raw computation
//  IS externally verifiable here in a way that, say, the cipher feedback width is not. Two layers
//  therefore need distinguishing:
//
//      * THE COMPUTATION IS PINNED ABSOLUTELY. The RFC 2202 and RFC 4231 vectors below are
//        published test data for HMAC itself. If a vector fails, the implementation is wrong -
//        no oracle is needed to adjudicate it.
//      * THE PRESENTATION IS PINNED AS A DECISION, NOT AS TRUTH. Two observables sit between the
//        caller and that computation and neither can be read from the repository: the output
//        encoding of the returned string (DECISION D4) and the text encoding applied to a string
//        data or key axis (DECISION M1, borrowed from H1 and D1). Both are asserted here as THE
//        DOCUMENTED DECISION so a change is visible in a diff, never as legacy-verified fact.
//
//  The vectors are consequently expressed in their PUBLISHED HEXADECIMAL FORM and converted to the
//  D4 payload encoding at the point of comparison. That way the literal in this file can be
//  compared character for character against the RFC, while the assertion still exercises the real
//  output convention.
//
//  A third observable is genuinely unanswerable and is documented rather than guessed: what the
//  closed binary did when handed CRC32 through a KEYED overload. DECISION M3 narrows it to a
//  defined failure, and the tests below pin that failure - including, explicitly, that it is NOT a
//  silent unkeyed checksum.
//
//  KEY HYGIENE IN THIS FILE (constraint C-F)
//  --------------------------------------------------------------------------------------------
//  EVERY KEY IN THIS FILE IS EITHER A PUBLISHED RFC TEST VECTOR OR IS SYNTHETIC AND OBVIOUSLY SO.
//  The RFC keys are public test data from IETF documents, cited inline by RFC number and test-case
//  number; the rest is COMPUTED from an index by SyntheticBytes or built from a repeated visible
//  pattern by SyntheticText. Nothing is copied from anywhere else: not from the repository's known
//  hardcoded-secret sites, not from the demo's unmasked key and vector fields, not from the test
//  configuration object's embedded cipher key, and not from any certificate or PEM block.
//
//  IN PARTICULAR, THE HARDCODED KEYED-HASH KEY AT u_cst_tabpage_utility_crypto.sru:L449 IS NOT
//  REPRODUCED. That call site is cited above because it proves the 3-argument keyed form is in live
//  use with SHA-256, and for no other reason; its literal is one of the repository's known secret
//  sites and copying it into a test would be a violation of exactly the mandate this port exists to
//  discharge. The expected values below are digests of published RFC inputs, so no literal here is
//  a credential and none could be mistaken for one.
// ==============================================================================================

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

// ALIASED RATHER THAN IMPORTED, deliberately. Importing System.Text wholesale makes the identifier
// `EncodingProvider` ambiguous, because the shared framework declares a type of that name for
// registering code pages and this folder declares its own port of the legacy encoding surface. An
// alias keeps `Encoding.ASCII` readable in the test bodies while leaving `EncodingProvider`
// unambiguously the crypto one, which is the type the constructor assertions have to name.
using Encoding = System.Text.Encoding;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Characterization tests for <see cref="HmacProvider"/>.
/// </summary>
public sealed class HmacProviderTests
{
    /// <summary>
    /// The five algorithm identifiers that have a keyed form [enums.sru:L928-L932].
    /// </summary>
    /// <remarks>
    /// <see cref="Enums.CRYPTO_HASH_CRC32"/> is deliberately absent: it is a published hash type but
    /// has no keyed form, and DECISION M3 makes that a defined failure. It is exercised by its own
    /// tests rather than by the grid.
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
    /// The system under test, with its one real dependency rather than a substitute.
    /// </summary>
    /// <remarks>
    /// The encoding provider is stateless and its own suite already pins it, so exercising the real
    /// one here is what makes the DECISION D4 assertions meaningful: a fake would only prove that
    /// this file agrees with itself.
    /// </remarks>
    private readonly HmacProvider _provider = new(new EncodingProvider());

    /// <summary>
    /// The unkeyed provider, used ONLY to prove that a keyed call never degenerates into an unkeyed
    /// one - see the DECISION M3 tests.
    /// </summary>
    /// <remarks>
    /// It shares an encoding provider instance shape with the keyed one so that a difference in
    /// result can never be attributed to a difference in output convention.
    /// </remarks>
    private readonly HashProvider _unkeyed = new(new EncodingProvider());

    // ==========================================================================================
    //  THEORY DATA
    // ==========================================================================================

    /// <summary>
    /// The full four-by-five overload-by-algorithm grid the plan names as this file's parity matrix.
    /// </summary>
    /// <returns>One row per cell: the cross-product cell index 0 to 3, then the algorithm.</returns>
    /// <remarks>
    /// The cell index selects which of the four <c>Hash</c> overloads
    /// <see cref="InvokeCell(int, byte[], byte[], long)"/> dispatches to, so the grid covers all 20
    /// combinations without twenty near-identical test methods.
    /// </remarks>
    public static TheoryData<int, long> OverloadAndAlgorithmGrid()
    {
        TheoryData<int, long> data = new();

        for (int cell = 0; cell < 4; cell++)
        {
            foreach (long ntype in KeyedHashTypes)
            {
                data.Add(cell, ntype);
            }
        }

        return data;
    }

    /// <summary>
    /// The five keyed algorithm identifiers alone, for tests that do not vary the overload.
    /// </summary>
    /// <returns>One row per keyed algorithm identifier.</returns>
    public static TheoryData<long> KeyedAlgorithms()
    {
        TheoryData<long> data = new();

        foreach (long ntype in KeyedHashTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    /// <summary>
    /// The four cross-product cells alone, for tests that do not vary the algorithm.
    /// </summary>
    /// <returns>One row per cell index.</returns>
    public static TheoryData<int> CrossProductCells()
    {
        TheoryData<int> data = new();

        for (int cell = 0; cell < 4; cell++)
        {
            data.Add(cell);
        }

        return data;
    }

    /// <summary>
    /// The published RFC known-answer vectors, one row per algorithm per test case.
    /// </summary>
    /// <returns>
    /// One row of algorithm identifier, key bytes, message bytes and the expected authenticator in
    /// the RFC's own hexadecimal spelling.
    /// </returns>
    /// <remarks>
    /// <para>
    /// SOURCES, cited so every literal below can be checked against a public document rather than
    /// against this file. RFC 2202 supplies the HMAC-MD5 and HMAC-SHA-1 cases; RFC 4231 supplies the
    /// HMAC-SHA-2 cases. Test case numbering is the RFCs' own.
    /// </para>
    /// <para>
    /// Note the one asymmetry in RFC 2202 test case 1, faithfully reproduced: the HMAC-MD5 case uses
    /// a SIXTEEN-byte key while the HMAC-SHA-1 case uses a TWENTY-byte key, each matching its hash's
    /// digest length. Test case 2 uses a short ASCII key for both, and RFC 4231 test cases 6 and 7
    /// use a 131-byte key - longer than the 64-byte block of SHA-256 - which is what exercises HMAC's
    /// own hash-the-long-key normalization and therefore DECISION M2.
    /// </para>
    /// </remarks>
    public static TheoryData<long, string, string, string> PublishedVectors()
    {
        // Key and message material is GENERATED from the RFCs' own descriptions rather than written
        // out, which is both faithful - the RFCs themselves say "0x0b repeated 20 times" - and the
        // reason this file contains no long key-shaped literal. The only literals below are the
        // expected authenticators, which are digests and not credentials.
        string shortRepeatedKeyMd5 = RepeatedHex(0x0b, 16);
        string shortRepeatedKeySha = RepeatedHex(0x0b, 20);
        string greeting = AsciiHex("Hi There");

        string asciiKey = AsciiHex("Jefe");
        string question = AsciiHex("what do ya want for nothing?");

        string longRepeatedKey = RepeatedHex(0xaa, 20);
        string repeatedPayload = RepeatedHex(0xdd, 50);

        string countingKey = CountingHex(1, 25);
        string otherRepeatedPayload = RepeatedHex(0xcd, 50);

        string overBlockKey = RepeatedHex(0xaa, 131);
        string overBlockNotice = AsciiHex("Test Using Larger Than Block-Size Key - Hash Key First");

        return new TheoryData<long, string, string, string>
        {
            // RFC 2202 test case 1 - HMAC-MD5.
            {
                Enums.CRYPTO_HASH_MD5,
                shortRepeatedKeyMd5,
                greeting,
                "9294727a3638bb1c13f48ef8158bfc9d"
            },

            // RFC 2202 test case 2 - HMAC-MD5.
            {
                Enums.CRYPTO_HASH_MD5,
                asciiKey,
                question,
                "750c783e6ab0b503eaa86e310a5db738"
            },

            // RFC 2202 test case 1 - HMAC-SHA-1.
            {
                Enums.CRYPTO_HASH_SHA1,
                shortRepeatedKeySha,
                greeting,
                "b617318655057264e28bc0b6fb378c8ef146be00"
            },

            // RFC 2202 test case 2 - HMAC-SHA-1.
            {
                Enums.CRYPTO_HASH_SHA1,
                asciiKey,
                question,
                "effcdf6ae5eb2fa2d27416d5f184df9c259a7c79"
            },

            // RFC 4231 test case 1 - HMAC-SHA-256.
            {
                Enums.CRYPTO_HASH_SHA256,
                shortRepeatedKeySha,
                greeting,
                "b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"
            },

            // RFC 4231 test case 2 - HMAC-SHA-256.
            {
                Enums.CRYPTO_HASH_SHA256,
                asciiKey,
                question,
                "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843"
            },

            // RFC 4231 test case 3 - HMAC-SHA-256.
            {
                Enums.CRYPTO_HASH_SHA256,
                longRepeatedKey,
                repeatedPayload,
                "773ea91e36800e46854db8ebd09181a72959098b3ef8c122d9635514ced565fe"
            },

            // RFC 4231 test case 4 - HMAC-SHA-256.
            {
                Enums.CRYPTO_HASH_SHA256,
                countingKey,
                otherRepeatedPayload,
                "82558a389a443c0ea4cc819899f2083a85f0faa3e578f8077a2e3ff46729665b"
            },

            // RFC 4231 test case 6 - HMAC-SHA-256, key longer than the hash block.
            {
                Enums.CRYPTO_HASH_SHA256,
                overBlockKey,
                overBlockNotice,
                "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54"
            },

            // RFC 4231 test case 1 - HMAC-SHA-384.
            {
                Enums.CRYPTO_HASH_SHA384,
                shortRepeatedKeySha,
                greeting,
                "afd03944d84895626b0825f4ab46907f15f9dadbe4101ec682aa034c7cebc59c" +
                "faea9ea9076ede7f4af152e8b2fa9cb6"
            },

            // RFC 4231 test case 2 - HMAC-SHA-384.
            {
                Enums.CRYPTO_HASH_SHA384,
                asciiKey,
                question,
                "af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e" +
                "8e2240ca5e69e2c78b3239ecfab21649"
            },

            // RFC 4231 test case 1 - HMAC-SHA-512.
            {
                Enums.CRYPTO_HASH_SHA512,
                shortRepeatedKeySha,
                greeting,
                "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cde" +
                "daa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854"
            },

            // RFC 4231 test case 2 - HMAC-SHA-512.
            {
                Enums.CRYPTO_HASH_SHA512,
                asciiKey,
                question,
                "164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea250554" +
                "9758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737"
            },
        };
    }

    // ==========================================================================================
    //  THE CENSUS - 4 + 2 = 6, WITH THE STRUCTURAL RULES ENFORCED BY REFLECTION
    // ==========================================================================================

    /// <summary>
    /// The published keyed surface is exactly four <c>Hash</c> and two <c>HashFile</c> overloads.
    /// </summary>
    /// <remarks>
    /// The folder reconciles 65 legacy declarations as 63 ported plus 2 deliberately non-ported, and
    /// this type is the 6 of that 63. A seventh public member here - a convenience overload, a
    /// Try-variant, a byte-returning shortcut or a promoted private seam - would silently break that
    /// arithmetic. Reflection is used deliberately: a hand-written list would drift from the file,
    /// whereas counting the real members cannot.
    /// </remarks>
    [Fact]
    public void PublishedSurfaceIsExactlySixOverloads()
    {
        Assert.Equal(4, PublicOverloads("Hash").Count);
        Assert.Equal(2, PublicOverloads("HashFile").Count);
        Assert.Equal(6, AllPublicMethods().Count);
    }

    /// <summary>
    /// NO UNKEYED OVERLOAD LEAKS INTO THIS TYPE. Every public member takes three parameters.
    /// </summary>
    /// <remarks>
    /// The unkeyed declarations at n_crypto.sru:L21-L22 and :L27 take TWO parameters and belong to
    /// <see cref="HashProvider"/>. This is the assertion that makes the split with that type total
    /// rather than merely intended, and it is what the folder's 3-and-6 division depends on: a
    /// two-parameter overload appearing here would be counted twice across the two files.
    /// </remarks>
    [Fact]
    public void NoUnkeyedOverloadAppearsHere()
    {
        foreach (MethodInfo method in AllPublicMethods())
        {
            ParameterInfo[] parameters = method.GetParameters();

            Assert.Equal(3, parameters.Length);
            Assert.Equal("ntype", parameters[2].Name);
            Assert.Equal(typeof(long), parameters[2].ParameterType);

            // The middle parameter is the key on every member, and it is the presence of that
            // parameter that makes the member keyed.
            Assert.Equal("key", parameters[1].Name);
        }
    }

    /// <summary>
    /// All six members return <see cref="string"/>, matching all six legacy declarations.
    /// </summary>
    /// <remarks>
    /// The legacy returns text rather than bytes and the oracle's own caller assigns the result
    /// straight into a plain text control [u_cst_tabpage_utility_crypto.sru:L449], so a
    /// <c>byte[]</c>-returning member would be surface the legacy never had.
    /// </remarks>
    [Fact]
    public void EveryPublishedMemberReturnsText()
    {
        Assert.All(AllPublicMethods(), method => Assert.Equal(typeof(string), method.ReturnType));
    }

    /// <summary>
    /// The four <c>Hash</c> overloads are the COMPLETE cross product of the data and key axes: all
    /// four cells present, none omitted and none collapsed into a span-based single method.
    /// </summary>
    /// <remarks>
    /// The four-way shape is the observable legacy surface [n_crypto.sru:L23-L26], so the exact
    /// parameter-type pairs are asserted rather than just the count. The two <c>HashFile</c>
    /// overloads carry only the key axis, because a filename is always text and the legacy declares
    /// no blob-filename form.
    /// </remarks>
    [Fact]
    public void CrossProductIsComplete()
    {
        Assert.NotNull(HashOverload(typeof(string), typeof(string)));
        Assert.NotNull(HashOverload(typeof(string), typeof(byte[])));
        Assert.NotNull(HashOverload(typeof(byte[]), typeof(string)));
        Assert.NotNull(HashOverload(typeof(byte[]), typeof(byte[])));

        Assert.NotNull(typeof(HmacProvider).GetMethod(
            "HashFile",
            [typeof(string), typeof(string), typeof(long)]));
        Assert.NotNull(typeof(HmacProvider).GetMethod(
            "HashFile",
            [typeof(string), typeof(byte[]), typeof(long)]));
    }

    /// <summary>
    /// The only public constructor takes the encoding provider and nothing else - no configuration,
    /// no options and no key.
    /// </summary>
    /// <remarks>
    /// This is the structural half of the key-handling contract: a key can reach this type ONLY on a
    /// call, because there is no constructor parameter through which application settings, an
    /// environment variable or a key store could be injected. A configuration or options parameter
    /// appearing here would silently reintroduce exactly that route.
    /// </remarks>
    [Fact]
    public void ConstructionTakesNoConfigurationAndNoKey()
    {
        ConstructorInfo[] constructors = typeof(HmacProvider).GetConstructors();

        ConstructorInfo constructor = Assert.Single(constructors);
        ParameterInfo parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(EncodingProvider), parameter.ParameterType);
    }

    /// <summary>
    /// The constructor rejects a null encoding provider rather than failing later on first use.
    /// </summary>
    [Fact]
    public void ConstructionRejectsNullEncodingProvider()
    {
        ArgumentNullException failure =
            Assert.Throws<ArgumentNullException>(() => new HmacProvider(null!));

        Assert.Equal("encoding", failure.ParamName);
    }

    // ==========================================================================================
    //  THE PUBLISHED KNOWN-ANSWER VECTORS - THE ONE EXTERNALLY VERIFIABLE LAYER
    // ==========================================================================================

    /// <summary>
    /// Every published RFC vector is reproduced exactly, through the byte-and-byte cell.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <param name="keyHex">The RFC's key, hexadecimal.</param>
    /// <param name="messageHex">The RFC's message, hexadecimal.</param>
    /// <param name="expectedHex">The RFC's expected authenticator, hexadecimal.</param>
    /// <remarks>
    /// <para>
    /// This is the assertion that needs no behavioural oracle: HMAC is publicly specified and these
    /// are its published test vectors, so a failure here means the implementation is wrong rather
    /// than that a decision needs adjudicating.
    /// </para>
    /// <para>
    /// The byte-and-byte cell is chosen deliberately because it is the ENCODING-FREE one: no text
    /// decision stands between the caller's input and the computation, so the vector tests the
    /// computation alone. The expected value is converted from the RFC's hexadecimal into the port's
    /// DECISION D4 output encoding at the point of comparison, which is what lets the literal above
    /// be checked character for character against the RFC while the assertion still exercises the
    /// real output convention.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PublishedVectors))]
    public void PublishedVectorIsReproduced(
        long ntype,
        string keyHex,
        string messageHex,
        string expectedHex)
    {
        byte[] key = Convert.FromHexString(keyHex);
        byte[] message = Convert.FromHexString(messageHex);

        Assert.Equal(EncodedFromHex(expectedHex), _provider.Hash(message, key, ntype));
    }

    /// <summary>
    /// The two RFC vectors whose key and message are both printable ASCII are reproduced through ALL
    /// FOUR cells, not just the byte-and-byte one.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <remarks>
    /// This is what ties the cross product to the published vectors: the three text-bearing cells
    /// must reduce to the encoding-free one, and for ASCII material they do regardless of how the
    /// text-encoding question of DECISION M1 is eventually settled. The material is RFC 2202 and RFC
    /// 4231 test case 2 - key <c>Jefe</c>, message <c>what do ya want for nothing?</c> - which is
    /// published test data and not a credential.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedAlgorithms))]
    public void AsciiPublishedVectorIsReproducedThroughEveryCell(long ntype)
    {
        const string key = "Jefe";
        const string message = "what do ya want for nothing?";

        byte[] keyBytes = Encoding.ASCII.GetBytes(key);
        byte[] messageBytes = Encoding.ASCII.GetBytes(message);

        string reference = _provider.Hash(messageBytes, keyBytes, ntype);

        Assert.Equal(reference, _provider.Hash(message, key, ntype));
        Assert.Equal(reference, _provider.Hash(message, keyBytes, ntype));
        Assert.Equal(reference, _provider.Hash(messageBytes, key, ntype));
    }

    // ==========================================================================================
    //  THE FOUR-BY-FIVE GRID - THE CROSS PRODUCT IS FOUR SPELLINGS OF ONE BEHAVIOUR
    // ==========================================================================================

    /// <summary>
    /// Every cell of the four-by-five overload-by-algorithm grid produces the same authenticator for
    /// the same ASCII data and key.
    /// </summary>
    /// <param name="cell">The cross-product cell index, 0 to 3.</param>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <remarks>
    /// The cross product must be FOUR SPELLINGS OF ONE BEHAVIOUR rather than four behaviours. This is
    /// the grid the coverage gate reads, and it is what would catch an overload that converted its
    /// text axis differently or skipped the shared dispatch.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OverloadAndAlgorithmGrid))]
    public void EveryGridCellAgreesWithTheEncodingFreeCell(int cell, long ntype)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(64));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(24));

        Assert.Equal(_provider.Hash(data, key, ntype), InvokeCell(cell, data, key, ntype));
    }

    /// <summary>
    /// A different key produces a different authenticator, and a different payload does too, in
    /// every cell.
    /// </summary>
    /// <param name="cell">The cross-product cell index, 0 to 3.</param>
    /// <remarks>
    /// The trivial-but-essential assertion that the key is actually consumed. A cell that ignored its
    /// key would still pass the agreement test above if all four ignored it, so this is the check
    /// that makes the agreement meaningful.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CrossProductCells))]
    public void KeyAndPayloadBothAffectTheResult(int cell)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(40));
        byte[] otherData = Encoding.ASCII.GetBytes(SyntheticText(41));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(20));
        byte[] otherKey = Encoding.ASCII.GetBytes(SyntheticText(21));

        string baseline = InvokeCell(cell, data, key, Enums.CRYPTO_HASH_SHA256);

        Assert.NotEqual(baseline, InvokeCell(cell, data, otherKey, Enums.CRYPTO_HASH_SHA256));
        Assert.NotEqual(baseline, InvokeCell(cell, otherData, key, Enums.CRYPTO_HASH_SHA256));
    }

    /// <summary>
    /// The five algorithm arms are five distinct behaviours: no two produce the same authenticator
    /// for the same data and key.
    /// </summary>
    /// <remarks>
    /// This is what would catch a dispatch table whose arms had been transposed or collapsed - in
    /// particular the SHA-512 catch-all arm silently swallowing an unscreened identifier.
    /// </remarks>
    [Fact]
    public void EveryAlgorithmArmIsDistinct()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(32));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(16));

        string[] results = [.. KeyedHashTypes.Select(ntype => _provider.Hash(data, key, ntype))];

        Assert.Equal(results.Length, results.Distinct().Count());
    }

    // ==========================================================================================
    //  DECISION M3 - THE KEYED CRC32 ARM
    // ==========================================================================================

    /// <summary>
    /// Every one of the six members rejects <see cref="Enums.CRYPTO_HASH_CRC32"/>, because there is
    /// no keyed CRC32 construction.
    /// </summary>
    /// <remarks>
    /// The failure is asserted on ALL SIX members rather than one, because the whole point of the
    /// single shared dispatch is that no member can answer this question differently. The exception
    /// names the <c>ntype</c> parameter, which is the same shape the unsupported-identifier arm uses.
    /// </remarks>
    [Fact]
    public void KeyedChecksumIsRejectedByEveryMember()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(8));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));
        string text = SyntheticText(8);

        string path = CreateTemporaryFile(data);

        try
        {
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.Hash(text, text, Enums.CRYPTO_HASH_CRC32)).ParamName);
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.Hash(text, key, Enums.CRYPTO_HASH_CRC32)).ParamName);
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.Hash(data, text, Enums.CRYPTO_HASH_CRC32)).ParamName);
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.Hash(data, key, Enums.CRYPTO_HASH_CRC32)).ParamName);
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.HashFile(path, text, Enums.CRYPTO_HASH_CRC32)).ParamName);
            Assert.Equal(
                "ntype",
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => _provider.HashFile(path, key, Enums.CRYPTO_HASH_CRC32)).ParamName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The keyed CRC32 arm does NOT silently return an unkeyed checksum - the single outcome DECISION
    /// M3 names as worse than the error.
    /// </summary>
    /// <remarks>
    /// A fallback would discard the caller's key without telling it, returning a plausible-looking
    /// eight-character string that any downstream comparison would accept while providing no
    /// authentication whatever. This test computes what that fallback WOULD have produced, from the
    /// unkeyed provider, and asserts that no keyed member returns it - so the defect is detectable
    /// even if someone reintroduces it as a "convenience".
    /// </remarks>
    [Fact]
    public void KeyedChecksumDoesNotFallBackToTheUnkeyedChecksum()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(48));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(16));

        string unkeyedChecksum = _unkeyed.Hash(data, Enums.CRYPTO_HASH_CRC32);

        // The unkeyed arm really is available and really does answer - so the keyed refusal below is
        // a deliberate narrowing rather than an absent capability.
        Assert.NotEmpty(unkeyedChecksum);

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.Hash(data, key, Enums.CRYPTO_HASH_CRC32));

        Assert.DoesNotContain(unkeyedChecksum, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The keyed CRC32 failure and the unsupported-identifier failure are the same SHAPE with
    /// DIFFERENT wording, so a caller can tell the two conditions apart.
    /// </summary>
    /// <remarks>
    /// CRC32 IS a published hash type, so reusing the unsupported-type wording would be actively
    /// misleading: the identifier is legal, the keyed combination is not. The message must therefore
    /// state the reason, and it must not be mistakable for the general message.
    /// </remarks>
    [Fact]
    public void KeyedChecksumFailureIsDistinguishableFromAnUnsupportedIdentifier()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(8));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));

        ArgumentOutOfRangeException checksumFailure = Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.Hash(data, key, Enums.CRYPTO_HASH_CRC32));
        ArgumentOutOfRangeException unsupportedFailure = Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.Hash(data, key, Enums.CRYPTO_HASH_CRC32 + 1));

        Assert.Equal(checksumFailure.ParamName, unsupportedFailure.ParamName);
        Assert.NotEqual(checksumFailure.Message, unsupportedFailure.Message);

        // The keyed-CRC32 message must say WHY, so that the narrowing reads as a documented decision
        // at the point a caller meets it and not only in the source.
        Assert.Contains("no keyed form", checksumFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An identifier outside the published set is rejected on the same shape as the CRC32 arm.
    /// </summary>
    /// <param name="ntype">A candidate identifier outside 0 to 5.</param>
    /// <remarks>
    /// The screen delegates to <see cref="LegacyDefaults.IsSupportedHashType"/>, so this test also
    /// pins that the keyed surface never grows its own allowed-set. The boundaries either side of the
    /// published range are included because an off-by-one in the screen is the likely defect.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(6L)]
    [InlineData(42L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void UnsupportedIdentifierIsRejected(long ntype)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(8));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.Hash(data, key, ntype));

        Assert.Equal("ntype", failure.ParamName);
        Assert.Equal(ntype, failure.ActualValue);
    }

    /// <summary>
    /// An unsupported or unkeyable identifier fails WITHOUT the filesystem having been touched.
    /// </summary>
    /// <remarks>
    /// The ordering is contract rather than incident: it is what stops a rejected algorithm
    /// identifier from being usable to probe whether a path exists. If the file were opened first,
    /// the two conditions would be distinguishable by which exception came back, which is an
    /// information leak an in-process library could not have had.
    /// </remarks>
    [Fact]
    public void FileMembersScreenTheIdentifierBeforeTouchingTheFilesystem()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"pfw-absent-{Guid.NewGuid():N}.bin");
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));

        Assert.False(File.Exists(missing));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.HashFile(missing, key, Enums.CRYPTO_HASH_CRC32));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.HashFile(missing, key, Enums.CRYPTO_HASH_CRC32 + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _provider.HashFile(missing, SyntheticText(8), Enums.CRYPTO_HASH_CRC32));
    }

    // ==========================================================================================
    //  DECISION M2 - ANY KEY LENGTH IS ACCEPTED AND NO LENGTH RULE IS APPLIED
    // ==========================================================================================

    /// <summary>
    /// Keys of every length succeed: empty, one byte, exactly the hash block length, and longer than
    /// the block length.
    /// </summary>
    /// <param name="keyLength">The key length in bytes.</param>
    /// <remarks>
    /// This is the positive half of DECISION M2. HMAC normalizes its own key - zero-padding a short
    /// one up to the block size and hashing a long one down first - so no length rule belongs in the
    /// provider, and NO MINIMUM IS ENFORCED because enforcing one would reject input the legacy
    /// accepted. 64 is the block length of SHA-256; 0 and 1 sit below every block length and 200 sits
    /// above all of them.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(200)]
    public void KeyOfAnyLengthIsAccepted(int keyLength)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(32));
        byte[] key = SyntheticBytes(keyLength, keyLength + 1);

        string result = _provider.Hash(data, key, Enums.CRYPTO_HASH_SHA256);

        // 32 raw bytes render as 44 padded Base64 characters - see the width table in the production
        // file's header. The point is that a key of this length produced a well-formed result at all.
        Assert.Equal(44, result.Length);
    }

    /// <summary>
    /// An empty key is accepted on every member and on every algorithm, and is not treated as a
    /// missing key.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <remarks>
    /// An empty key yields a perfectly well-formed authenticator that authenticates nothing
    /// meaningful. That is a KNOWN LEGACY WEAKNESS which is preserved rather than corrected: the
    /// legacy declarations carry no length precondition, so rejecting an empty key would break a
    /// caller that works today. It is distinguished from a NULL key, which is rejected.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedAlgorithms))]
    public void EmptyKeyIsAcceptedRatherThanTreatedAsMissing(long ntype)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(16));

        string fromBytes = _provider.Hash(data, Array.Empty<byte>(), ntype);
        string fromText = _provider.Hash(data, string.Empty, ntype);

        Assert.NotEmpty(fromBytes);
        Assert.Equal(fromBytes, fromText);
    }

    /// <summary>
    /// The cipher surface's truncate-or-pad key rule is deliberately NOT applied here: an over-long
    /// key is used in full rather than being cut to a cipher key length.
    /// </summary>
    /// <remarks>
    /// The negative half of DECISION M2, and the assertion that would fail if somebody "completed"
    /// this file by routing the key through the shared normalizer. If the key had been truncated to
    /// any fixed length, a longer key and its leading prefix of that length would authenticate
    /// identically. They must not.
    /// </remarks>
    [Fact]
    public void OverLongKeyIsNotTruncatedToACipherKeyLength()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(24));
        byte[] longKey = SyntheticBytes(40, 3);

        foreach (int prefixLength in (int[])[8, 16, 24, 32])
        {
            byte[] prefix = longKey[..prefixLength];

            Assert.NotEqual(
                _provider.Hash(data, prefix, Enums.CRYPTO_HASH_SHA256),
                _provider.Hash(data, longKey, Enums.CRYPTO_HASH_SHA256));
        }
    }

    /// <summary>
    /// The key normalization actually in force is HMAC's own, which is the construction's rather than
    /// the provider's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two published properties of HMAC are asserted, and asserting them is what proves the provider
    /// adds no normalization of its own:
    /// </para>
    /// <para>
    /// ZERO-PADDING TO THE BLOCK LENGTH. A key shorter than the 64-byte SHA-256 block is used as
    /// itself followed by zero bytes up to that block, so a short key and that same key explicitly
    /// zero-extended to the block length authenticate identically.
    /// </para>
    /// <para>
    /// HASHING A KEY LONGER THAN THE BLOCK. A key longer than the block is replaced by its own
    /// digest, so a 131-byte key and the 32-byte SHA-256 digest of that key authenticate identically.
    /// The digest is computed here with the framework primitive directly, so this test's reference
    /// does not come from the system under test.
    /// </para>
    /// </remarks>
    [Fact]
    public void KeyNormalizationIsTheConstructionsOwn()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(24));

        byte[] shortKey = SyntheticBytes(10, 5);
        byte[] zeroExtendedKey = new byte[64];
        shortKey.CopyTo(zeroExtendedKey, 0);

        Assert.Equal(
            _provider.Hash(data, shortKey, Enums.CRYPTO_HASH_SHA256),
            _provider.Hash(data, zeroExtendedKey, Enums.CRYPTO_HASH_SHA256));

        byte[] overBlockKey = SyntheticBytes(131, 7);

        Assert.Equal(
            _provider.Hash(data, SHA256.HashData(overBlockKey), Enums.CRYPTO_HASH_SHA256),
            _provider.Hash(data, overBlockKey, Enums.CRYPTO_HASH_SHA256));
    }

    // ==========================================================================================
    //  DECISION M1 - THE BORROWED TEXT ENCODINGS
    // ==========================================================================================

    /// <summary>
    /// The data-axis text encoding is the SAME NAMED DECISION the unkeyed path uses, not a copy.
    /// </summary>
    /// <remarks>
    /// The parity marker for DECISION M1's data half. It names the decision rather than duplicating
    /// it, so that when the behavioural oracle settles the text-encoding question there is one member
    /// to change and one test to re-baseline. Asserting instance identity is what proves the borrow
    /// is a forward rather than a restatement that could drift.
    /// </remarks>
    [Fact]
    public void DataTextEncodingIsBorrowedFromTheUnkeyedPath()
    {
        Assert.Same(HashProvider.InputTextEncoding, HmacProvider.DataTextEncoding);
    }

    /// <summary>
    /// The key-axis text encoding is DECISION D1's encoding half, shared with the cipher surface.
    /// </summary>
    /// <remarks>
    /// Key material is key material wherever it enters the folder, so the key axis reads the
    /// key-material decision rather than the payload one.
    /// </remarks>
    [Fact]
    public void KeyTextEncodingIsBorrowedFromTheKeyMaterialDecision()
    {
        Assert.Same(LegacyDefaults.KeyMaterialEncoding, HmacProvider.KeyTextEncoding);
    }

    /// <summary>
    /// The two borrowed decisions AGREE, so a caller cannot get one answer from the keyed path and a
    /// differently-encoded one from the unkeyed path over the same text.
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed precisely because they are two independent decisions owned by two
    /// different files. If either is ever re-baselined against the behavioural oracle without the
    /// other, this test fails - which is the point: the divergence becomes a build-visible failure
    /// instead of a silent parity fork.
    /// </remarks>
    [Fact]
    public void TheTwoBorrowedTextEncodingsAgree()
    {
        Assert.Equal(
            HmacProvider.DataTextEncoding.WebName,
            HmacProvider.KeyTextEncoding.WebName);

        byte[] probe = HmacProvider.DataTextEncoding.GetBytes(SyntheticText(26));

        Assert.Equal(probe, HmacProvider.KeyTextEncoding.GetBytes(SyntheticText(26)));
    }

    /// <summary>
    /// Text axes are converted through the borrowed encodings and nothing else, including for
    /// non-ASCII input.
    /// </summary>
    /// <remarks>
    /// The non-ASCII case is the ONE INPUT on which the text-encoding decision is observable, so it is
    /// asserted against the named decision rather than against a hard-coded byte sequence. That keeps
    /// this test correct whichever way the oracle eventually settles DECISION M1, while still proving
    /// that no second conversion rule has crept in between the caller and the computation.
    /// </remarks>
    [Fact]
    public void TextAxesAreConvertedThroughTheBorrowedEncodings()
    {
        const string data = "PowerFramework \u4e2d\u6587 payload";
        const string key = "\u5bc6\u94a5 material";

        byte[] dataBytes = HmacProvider.DataTextEncoding.GetBytes(data);
        byte[] keyBytes = HmacProvider.KeyTextEncoding.GetBytes(key);

        string reference = _provider.Hash(dataBytes, keyBytes, Enums.CRYPTO_HASH_SHA256);

        Assert.Equal(reference, _provider.Hash(data, key, Enums.CRYPTO_HASH_SHA256));
        Assert.Equal(reference, _provider.Hash(data, keyBytes, Enums.CRYPTO_HASH_SHA256));
        Assert.Equal(reference, _provider.Hash(dataBytes, key, Enums.CRYPTO_HASH_SHA256));
    }

    // ==========================================================================================
    //  DECISION D4 - THE OUTPUT ENCODING, PINNED AS A DECISION AND NOT AS TRUTH
    // ==========================================================================================

    /// <summary>
    /// Every authenticator is rendered in the shared payload encoding, at the documented width, and a
    /// keyed authenticator is the same length as the unkeyed digest of the same algorithm.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <remarks>
    /// The width table in the production file's header is what callers size columns and buffers from,
    /// so it is asserted rather than left as prose. The output form itself is DECISION D4, owned by
    /// the shared defaults rather than by this file, so the expected text is produced BY THAT
    /// DECISION here rather than written down - if D4 is re-baselined this test follows it instead of
    /// contradicting it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedAlgorithms))]
    public void AuthenticatorIsRenderedInTheSharedPayloadEncoding(long ntype)
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(32));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(16));

        string keyed = _provider.Hash(data, key, ntype);

        // Same length as the unkeyed digest of the same algorithm, because HMAC outputs one digest of
        // the underlying hash. This is the cross-check between this file's width table and the unkeyed
        // provider's.
        Assert.Equal(_unkeyed.Hash(data, ntype).Length, keyed.Length);

        // Round-trips through the same shared encoding decision the provider applied, which is what
        // makes the assertion about the DECISION rather than about a literal.
        byte[] raw = new EncodingProvider().StringToBlob(keyed, LegacyDefaults.STRING_PAYLOAD_ENCODING);

        Assert.Equal(DigestLengthBytes(ntype), raw.Length);
    }

    // ==========================================================================================
    //  NULL POLICY - REJECTED, NEVER COERCED, AND IDENTICALLY ACROSS ALL SIX MEMBERS
    // ==========================================================================================

    /// <summary>
    /// A null data, key or filename is rejected by name on every member.
    /// </summary>
    /// <remarks>
    /// Treating null as empty would return the authenticator of the empty input - a perfectly
    /// well-formed, plausible-looking string - and so would convert a caller defect into a silent
    /// wrong answer that no downstream test could distinguish from a correct one. Typed nulls are used
    /// throughout because a bare null literal is applicable to both overloads of an axis and therefore
    /// does not compile, which the production file documents.
    /// </remarks>
    [Fact]
    public void NullArgumentIsRejectedByName()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(8));
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));
        string text = SyntheticText(8);

        const string nullText = null!;
        const byte[] nullBytes = null!;

        Assert.Equal("data", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(nullText, text, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(text, nullText, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("data", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(nullText, key, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(text, nullBytes, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("data", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(nullBytes, text, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(data, nullText, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("data", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(nullBytes, key, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.Hash(data, nullBytes, Enums.CRYPTO_HASH_SHA256)).ParamName);

        Assert.Equal("filename", Assert.Throws<ArgumentNullException>(
            () => _provider.HashFile(nullText, text, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.HashFile(text, nullText, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("filename", Assert.Throws<ArgumentNullException>(
            () => _provider.HashFile(nullText, key, Enums.CRYPTO_HASH_SHA256)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(
            () => _provider.HashFile(text, nullBytes, Enums.CRYPTO_HASH_SHA256)).ParamName);
    }

    /// <summary>
    /// An empty payload is valid on every algorithm and yields a fixed, non-empty authenticator.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    [Theory]
    [MemberData(nameof(KeyedAlgorithms))]
    public void EmptyPayloadIsValid(long ntype)
    {
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(16));

        string fromBytes = _provider.Hash(Array.Empty<byte>(), key, ntype);

        Assert.NotEmpty(fromBytes);
        Assert.Equal(fromBytes, _provider.Hash(string.Empty, key, ntype));
    }

    // ==========================================================================================
    //  THE FILE MEMBERS
    // ==========================================================================================

    /// <summary>
    /// A keyed file authenticator equals the keyed authenticator of the same bytes, for content of
    /// every shape: empty, smaller than one read buffer, and spanning several read buffers.
    /// </summary>
    /// <param name="ntype">The algorithm identifier under test.</param>
    /// <remarks>
    /// The multi-buffer case is the one that matters. The file members STREAM in 80 KiB chunks, so a
    /// payload larger than that exercises the incremental append loop - the exact path where a
    /// mishandled final partial read or a reused buffer would corrupt the result. 200000 bytes spans
    /// three chunks and ends mid-chunk deliberately. Both key overloads are covered, and no repository
    /// binary is hashed: every file here is created by the test and deleted afterwards.
    /// </remarks>
    [Theory]
    [MemberData(nameof(KeyedAlgorithms))]
    public void FileAuthenticatorMatchesTheInMemoryAuthenticator(long ntype)
    {
        byte[] textKey = Encoding.ASCII.GetBytes(SyntheticText(20));
        string keyText = SyntheticText(20);

        foreach (int contentLength in (int[])[0, 1, 117, 81920, 200000])
        {
            byte[] content = SyntheticBytes(contentLength, contentLength + 2);
            string path = CreateTemporaryFile(content);

            try
            {
                string expected = _provider.Hash(content, textKey, ntype);

                Assert.Equal(expected, _provider.HashFile(path, textKey, ntype));
                Assert.Equal(expected, _provider.HashFile(path, keyText, ntype));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// A missing file surfaces the framework's own file exception rather than a sentinel return.
    /// </summary>
    /// <remarks>
    /// What the closed binary returned for a missing file is unobservable, so a defined error is
    /// chosen over a guessed one. An empty string would be indistinguishable from a legitimate result
    /// of some future encoding rule, and a null would be indistinguishable from an unset value.
    /// </remarks>
    [Fact]
    public void MissingFileSurfacesTheFrameworkFailure()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"pfw-absent-{Guid.NewGuid():N}.bin");
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));

        Assert.False(File.Exists(missing));

        Assert.Throws<FileNotFoundException>(
            () => _provider.HashFile(missing, key, Enums.CRYPTO_HASH_SHA256));
        Assert.Throws<FileNotFoundException>(
            () => _provider.HashFile(missing, SyntheticText(8), Enums.CRYPTO_HASH_SHA256));
    }

    /// <summary>
    /// An unreadable path surfaces the framework's own access failure unchanged.
    /// </summary>
    /// <remarks>
    /// A directory is used as the unreadable path because it is the one case reproducible on any host
    /// without manipulating permissions, and because the process runs as root in the container where
    /// this suite executes, which makes a permission-denied case unreachable there.
    /// </remarks>
    [Fact]
    public void UnreadablePathSurfacesTheFrameworkFailure()
    {
        byte[] key = Encoding.ASCII.GetBytes(SyntheticText(8));

        Assert.Throws<UnauthorizedAccessException>(
            () => _provider.HashFile(Path.GetTempPath(), key, Enums.CRYPTO_HASH_SHA256));
    }

    // ==========================================================================================
    //  KEY HYGIENE AND CONCURRENCY
    // ==========================================================================================

    /// <summary>
    /// NO FIELD ON THIS TYPE CAN HOLD KEY MATERIAL. The only field is the injected encoding provider,
    /// and there is no static state at all.
    /// </summary>
    /// <remarks>
    /// Asserted structurally rather than by inspection, because "we do not cache the key" is exactly
    /// the kind of claim that decays. A byte array, a string, a keyed algorithm instance or any static
    /// field appearing here would be a place key material could survive a call.
    /// </remarks>
    [Fact]
    public void NoFieldCanHoldKeyMaterial()
    {
        FieldInfo[] instanceFields = typeof(HmacProvider)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        FieldInfo field = Assert.Single(instanceFields);

        Assert.Equal(typeof(EncodingProvider), field.FieldType);

        // Constants are compile-time literals rather than storage, so only genuine static STORAGE is
        // rejected here - a static field is the one place a value could outlive every call.
        Assert.DoesNotContain(
            typeof(HmacProvider)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            candidate => !candidate.IsLiteral);
    }

    /// <summary>
    /// NO EXCEPTION MESSAGE CARRIES KEY CONTENT. A distinctive key is used and no failure message
    /// mentions it.
    /// </summary>
    /// <remarks>
    /// The failure paths are where key material most plausibly escapes, because a diagnostic message
    /// is written to be helpful. Every reachable failure of every member is exercised with a key
    /// whose text is unmistakable, and the message, the parameter name and the reported actual value
    /// are all checked.
    /// </remarks>
    [Fact]
    public void NoFailureMessageCarriesKeyContent()
    {
        const string distinctiveKey = "zzz-key-marker-zzz";
        byte[] distinctiveKeyBytes = Encoding.ASCII.GetBytes(distinctiveKey);
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(8));
        string missing = Path.Combine(Path.GetTempPath(), $"pfw-absent-{Guid.NewGuid():N}.bin");

        List<Exception> failures =
        [
            Record.Exception(() => _provider.Hash(data, distinctiveKeyBytes, Enums.CRYPTO_HASH_CRC32))!,
            Record.Exception(() => _provider.Hash(data, distinctiveKey, Enums.CRYPTO_HASH_CRC32))!,
            Record.Exception(() => _provider.Hash(data, distinctiveKeyBytes, 999L))!,
            Record.Exception(() => _provider.Hash(data, distinctiveKey, 999L))!,
            Record.Exception(() => _provider.HashFile(missing, distinctiveKeyBytes, Enums.CRYPTO_HASH_SHA256))!,
            Record.Exception(() => _provider.HashFile(missing, distinctiveKey, Enums.CRYPTO_HASH_SHA256))!,
        ];

        Assert.All(failures, failure =>
        {
            Assert.NotNull(failure);
            Assert.DoesNotContain("zzz-key-marker-zzz", failure.ToString(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A CALLER'S KEY ARRAY IS NEVER MODIFIED, on any member that takes one.
    /// </summary>
    /// <remarks>
    /// The provider wipes the copies it makes for itself but must not touch a buffer the caller still
    /// owns: zeroing it would be an observable side effect the legacy never had, and a caller reusing
    /// its key for a second call would silently get a different answer. This is therefore the boundary
    /// between hygiene and behaviour change, asserted rather than described.
    /// </remarks>
    [Fact]
    public void CallerKeyArrayIsNeverModified()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(16));
        byte[] key = SyntheticBytes(24, 9);
        byte[] pristine = [.. key];

        string path = CreateTemporaryFile(data);

        try
        {
            string first = _provider.Hash(data, key, Enums.CRYPTO_HASH_SHA256);
            Assert.Equal(pristine, key);

            string second = _provider.Hash(Encoding.ASCII.GetString(data), key, Enums.CRYPTO_HASH_SHA256);
            Assert.Equal(pristine, key);

            _provider.HashFile(path, key, Enums.CRYPTO_HASH_SHA256);
            Assert.Equal(pristine, key);

            // The key survived intact, so a repeat call must still agree with the first.
            Assert.Equal(first, _provider.Hash(data, key, Enums.CRYPTO_HASH_SHA256));
            Assert.Equal(first, second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The type is safe to call concurrently from many threads with different keys and algorithms.
    /// </summary>
    /// <remarks>
    /// The keyed algorithm object carries mutable digest state and is not safe to share, so a cached
    /// instance would corrupt results under concurrency in a way that looks like a hashing defect
    /// rather than a threading one. One instance serves every request in the running service, so this
    /// is a production property and not a hypothetical.
    /// </remarks>
    [Fact]
    public void ConcurrentUseProducesCorrectResults()
    {
        byte[] data = Encoding.ASCII.GetBytes(SyntheticText(96));

        string[] expected =
            [.. KeyedHashTypes.Select((ntype, index) =>
                _provider.Hash(data, SyntheticBytes(16, index), ntype))];

        ConcurrentBag<string> failures = [];

        Parallel.For(0, 400, iteration =>
        {
            int index = iteration % KeyedHashTypes.Length;
            string actual = _provider.Hash(data, SyntheticBytes(16, index), KeyedHashTypes[index]);

            if (!string.Equals(actual, expected[index], StringComparison.Ordinal))
            {
                failures.Add($"iteration {iteration}, algorithm index {index}");
            }
        });

        Assert.Empty(failures);
    }

    // ==========================================================================================
    //  HELPERS - synthetic material only, computed rather than written down
    // ==========================================================================================

    /// <summary>
    /// Dispatches to one of the four cross-product cells by index.
    /// </summary>
    /// <param name="cell">
    /// 0 for text data and text key, 1 for text data and byte key, 2 for byte data and text key,
    /// 3 for byte data and byte key - the order of n_crypto.sru:L23 to :L26.
    /// </param>
    /// <param name="data">The payload, as bytes; decoded to text for the text-data cells.</param>
    /// <param name="key">The key, as bytes; decoded to text for the text-key cells.</param>
    /// <param name="ntype">The algorithm identifier.</param>
    /// <returns>The authenticator that cell produces.</returns>
    /// <remarks>
    /// Callers pass ASCII-representable material so that decoding back to text is lossless and the
    /// four cells are genuinely comparable. The dispatch exists so the four-by-five grid is one theory
    /// rather than twenty near-identical methods.
    /// </remarks>
    private string InvokeCell(int cell, byte[] data, byte[] key, long ntype) => cell switch
    {
        0 => _provider.Hash(Encoding.ASCII.GetString(data), Encoding.ASCII.GetString(key), ntype),
        1 => _provider.Hash(Encoding.ASCII.GetString(data), key, ntype),
        2 => _provider.Hash(data, Encoding.ASCII.GetString(key), ntype),
        _ => _provider.Hash(data, key, ntype),
    };

    /// <summary>
    /// Reflects the public instance overloads of one method name.
    /// </summary>
    /// <param name="name">The method name, either <c>Hash</c> or <c>HashFile</c>.</param>
    /// <returns>Every public instance overload declared with that name.</returns>
    private static List<MethodInfo> PublicOverloads(string name) =>
        [.. AllPublicMethods().Where(method => method.Name == name)];

    /// <summary>
    /// Reflects every public instance method the type declares, excluding inherited members and
    /// property accessors.
    /// </summary>
    /// <returns>The declared public instance methods.</returns>
    /// <remarks>
    /// Property accessors are filtered out so that the census counts published operations rather than
    /// compiler-generated members; the type declares no public property today, and this keeps the
    /// count honest if one is ever added for an unrelated reason.
    /// </remarks>
    private static List<MethodInfo> AllPublicMethods() =>
        [.. typeof(HmacProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)];

    /// <summary>
    /// Reflects one cell of the <c>Hash</c> cross product by its two axis types.
    /// </summary>
    /// <param name="dataType">The data-axis parameter type.</param>
    /// <param name="keyType">The key-axis parameter type.</param>
    /// <returns>The overload, or null if that cell is missing.</returns>
    private static MethodInfo? HashOverload(Type dataType, Type keyType) =>
        typeof(HmacProvider).GetMethod("Hash", [dataType, keyType, typeof(long)]);

    /// <summary>
    /// The raw digest length of an algorithm, in bytes.
    /// </summary>
    /// <param name="ntype">One of the five keyed algorithm identifiers.</param>
    /// <returns>The digest length in bytes.</returns>
    /// <remarks>
    /// Written from the algorithms' published digest lengths rather than measured from the system
    /// under test, so that the width assertions have an independent reference.
    /// </remarks>
    private static int DigestLengthBytes(long ntype) => ntype switch
    {
        _ when ntype == Enums.CRYPTO_HASH_MD5 => 16,
        _ when ntype == Enums.CRYPTO_HASH_SHA1 => 20,
        _ when ntype == Enums.CRYPTO_HASH_SHA256 => 32,
        _ when ntype == Enums.CRYPTO_HASH_SHA384 => 48,
        _ => 64,
    };

    /// <summary>
    /// Renders an expected authenticator from its published hexadecimal form into the port's output
    /// encoding.
    /// </summary>
    /// <param name="hex">The RFC's hexadecimal spelling of the expected value.</param>
    /// <returns>The same value in the shared payload encoding.</returns>
    /// <remarks>
    /// The conversion goes through <see cref="LegacyDefaults.STRING_PAYLOAD_ENCODING"/> and the real
    /// encoding provider, so the comparison exercises DECISION D4 rather than assuming it, while the
    /// literal in the theory data stays in the form the RFC publishes.
    /// </remarks>
    private static string EncodedFromHex(string hex) =>
        new EncodingProvider().BlobToString(
            Convert.FromHexString(hex),
            LegacyDefaults.STRING_PAYLOAD_ENCODING);

    /// <summary>
    /// Writes a temporary file with the given content and returns its path.
    /// </summary>
    /// <param name="content">The bytes to write. May be empty.</param>
    /// <returns>The path of the created file, which the caller must delete.</returns>
    /// <remarks>
    /// Every file this suite authenticates is created here and deleted afterwards. NO REPOSITORY
    /// BINARY IS EVER HASHED: the legacy tree is read-only behavioural oracle, and a test that
    /// depended on the bytes of a shipped native library would also be a test that broke whenever
    /// that library changed.
    /// </remarks>
    private static string CreateTemporaryFile(byte[] content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pfw-hmac-{Guid.NewGuid():N}.bin");

        File.WriteAllBytes(path, content);

        return path;
    }

    /// <summary>
    /// Produces obviously synthetic byte material of a requested length.
    /// </summary>
    /// <param name="length">The length in bytes. Zero is allowed.</param>
    /// <param name="seed">A discriminator, so two calls can differ deterministically.</param>
    /// <returns>A fresh buffer.</returns>
    /// <remarks>
    /// COMPUTED FROM AN INDEX, never a literal, so this file contains nothing outside the cited RFC
    /// vectors that could be mistaken for real key material and copies nothing from any of the
    /// repository's known hardcoded-secret sites.
    /// </remarks>
    private static byte[] SyntheticBytes(int length, int seed)
    {
        byte[] material = new byte[length];

        for (int index = 0; index < length; index++)
        {
            material[index] = (byte)((index * 31) + (seed * 17) + 53);
        }

        return material;
    }

    /// <summary>
    /// Produces obviously synthetic text material of a requested length.
    /// </summary>
    /// <param name="length">The length in characters.</param>
    /// <returns>A visibly synthetic, ASCII-only string.</returns>
    /// <remarks>
    /// BUILT FROM A REPEATED VISIBLE PATTERN so that it is self-evidently not a credential. It is
    /// ASCII only, which keeps it inside the range where the borrowed text encodings are
    /// byte-identical to the legacy's own conversion, and which is also what makes decoding it back
    /// from bytes lossless in <see cref="InvokeCell"/>.
    /// </remarks>
    private static string SyntheticText(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(index => (char)('a' + (index % 26))));

    /// <summary>
    /// Renders one byte repeated a number of times as hexadecimal.
    /// </summary>
    /// <param name="value">The byte value.</param>
    /// <param name="count">How many times it repeats.</param>
    /// <returns>The hexadecimal spelling of the repeated bytes.</returns>
    /// <remarks>
    /// This is how RFC 2202 and RFC 4231 themselves describe most of their keys and messages - "0x0b
    /// repeated 20 times" - so generating them is more faithful than transcribing them, and it is why
    /// this file carries no long key-shaped literal.
    /// </remarks>
    private static string RepeatedHex(byte value, int count) =>
        Convert.ToHexString(Repeat(value, count));

    /// <summary>
    /// Renders an ascending run of byte values as hexadecimal.
    /// </summary>
    /// <param name="first">The first value, inclusive.</param>
    /// <param name="count">How many values the run contains.</param>
    /// <returns>The hexadecimal spelling of the run.</returns>
    /// <remarks>
    /// RFC 4231 test case 4 uses the ascending run 0x01 to 0x19 as its key; generating it keeps the
    /// intent legible and avoids another long literal.
    /// </remarks>
    private static string CountingHex(int first, int count) =>
        Convert.ToHexString([.. Enumerable.Range(first, count).Select(value => (byte)value)]);

    /// <summary>
    /// Renders ASCII text as hexadecimal.
    /// </summary>
    /// <param name="text">The text, which must be ASCII.</param>
    /// <returns>The hexadecimal spelling of its ASCII bytes.</returns>
    /// <remarks>
    /// The RFCs give their messages in both quoted-ASCII and hexadecimal form, so converting the
    /// quoted form keeps the theory data readable while the values remain exactly the published ones.
    /// </remarks>
    private static string AsciiHex(string text) =>
        Convert.ToHexString(Encoding.ASCII.GetBytes(text));

    /// <summary>
    /// Produces one byte repeated a number of times.
    /// </summary>
    /// <param name="value">The byte value.</param>
    /// <param name="count">How many times it repeats.</param>
    /// <returns>A fresh buffer.</returns>
    private static byte[] Repeat(byte value, int count)
    {
        byte[] material = new byte[count];

        Array.Fill(material, value);

        return material;
    }
}
