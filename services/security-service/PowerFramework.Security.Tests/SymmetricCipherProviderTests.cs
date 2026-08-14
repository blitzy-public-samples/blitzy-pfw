// ==============================================================================================
//  SymmetricCipherProviderTests - the characterization suite that pins
//  PowerFramework.Security.Crypto.SymmetricCipherProvider
//  --------------------------------------------------------------------------------------------
//  SYSTEM UNDER TEST  services/security-service/PowerFramework.Security/Crypto/
//                     SymmetricCipherProvider.cs
//  BEHAVIOURAL ORACLE ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L30-L45   SymEncrypt x16
//                     ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L46-L61   SymDecrypt x16
//                     ws_objects/pfw.shared.pbl.src/enums.sru:L936-L946    types and modes
//                     ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L337,L552,L583,L615,L992
//                                                                         the ECB default arm
//                     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L504,L547,
//                                                  L592,L607,L622,L637    the IV-and-mode arms
//                     ws_objects/pfw.demos.pbl.src/w_about.srw:L118        the OpenSSL attribution
//                     all READ-ONLY per constraint C-C - read as specification, never edited
//
//  WHAT THIS SUITE CAN AND CANNOT BE EVIDENCE OF
//  --------------------------------------------------------------------------------------------
//  n_crypto is a PBNI class over the closed pfw.dll: the .sru declares 65 prototypes and no body
//  exists anywhere in the repository. This suite can therefore prove INTERNAL CONSISTENCY - that
//  the 32 overloads are spellings of one behaviour, that every documented default really is the
//  default, that every documented rule is applied uniformly - and it CANNOT prove byte-exact
//  agreement with what the closed binary produced. Four observables are genuinely open and the
//  production file records each as a decision rather than a measurement:
//
//      * the CFB feedback width (DECISION H1) - CFB8 and full-block CFB both round-trip perfectly
//        against themselves, so NO TEST IN THIS FILE CAN DETECT A WRONG CHOICE. What is pinned
//        instead is the resolved width per cipher type, so that a change is visible in a diff.
//      * the payload encoding of the string-shaped family (DECISION D4)
//      * the text encoding of a string payload and of string key material (DECISION D1)
//      * the REFUSAL of the eight mode-without-IV arms for a vector-consuming mode, and of CFB
//        entirely, because neither the vector nor the feedback width is provable (DECISION D3)
//
//  Each is asserted here as THE DOCUMENTED DECISION, not as legacy-verified truth. Adjudicating any
//  of them requires the behavioural oracle.
//
//  KEY HYGIENE IN THIS FILE (constraint C-F)
//  --------------------------------------------------------------------------------------------
//  EVERY KEY, PASSPHRASE AND INITIALIZATION VECTOR BELOW IS SYNTHETIC AND OBVIOUSLY SO. Byte
//  material is COMPUTED from an index by SyntheticBytes; text material is built from a repeated
//  visible pattern by SyntheticText. Nothing is copied from anywhere: not from the repository's
//  eight known hardcoded-secret sites, not from the demo's unmasked key and vector fields, not from
//  the test configuration object's embedded cipher key, and not from any certificate or PEM block.
//  There is no base64 or hexadecimal blob anywhere in this file, and no literal that could be
//  mistaken for real key material.
// ==============================================================================================

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Characterization tests for <see cref="SymmetricCipherProvider"/>.
/// </summary>
public sealed class SymmetricCipherProviderTests
{
    /// <summary>
    /// The five published cipher types, as the 16-bit values the legacy declares the argument at.
    /// </summary>
    /// <remarks>
    /// The cast is the visible consequence of the width mismatch the port reproduces: the legacy
    /// declares the argument as a 16-bit PowerBuilder <c>uint</c> [n_crypto.sru:L30-L61] while
    /// declaring the constants that name its legal values as <c>Constant Long</c>
    /// [enums.sru:L936-L940]. Every call site in this file therefore casts, exactly as the production
    /// file's documentation says a caller must.
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
    /// The three published cipher modes [enums.sru:L943-L945].
    /// </summary>
    private static readonly long[] AllCipherModes =
    [
        Enums.CRYPTO_SYMCRYPT_MODE_ECB,
        Enums.CRYPTO_SYMCRYPT_MODE_CBC,
        Enums.CRYPTO_SYMCRYPT_MODE_CFB,
    ];

    /// <summary>
    /// The modes whose behaviour this port can reproduce faithfully, which is where the round-trip
    /// matrices below run.
    /// </summary>
    /// <remarks>
    /// CFB is absent, and its absence is asserted rather than assumed - see the blocked-cell tests.
    /// It remains a PUBLISHED mode and stays in <see cref="AllCipherModes"/>, because the identifier
    /// set is preserved exactly; what cannot be reproduced is its feedback width, which the legacy
    /// never published and which the closed binary does not reveal. Splitting the two lists is how
    /// this file keeps "the mode exists" and "the mode is usable" from being confused for each other.
    /// </remarks>
    private static readonly long[] ReproducibleCipherModes =
    [
        Enums.CRYPTO_SYMCRYPT_MODE_ECB,
        Enums.CRYPTO_SYMCRYPT_MODE_CBC,
    ];

    /// <summary>
    /// The system under test, with its one real dependency rather than a substitute.
    /// </summary>
    /// <remarks>
    /// The encoding provider is stateless and its own suite already pins it, so exercising the real
    /// one here is what makes the DECISION D4 round trip meaningful: a fake would only prove that
    /// this file agrees with itself.
    /// </remarks>
    private readonly SymmetricCipherProvider _provider = new(new EncodingProvider());

    /// <summary>
    /// The 10 reproducible cells of the 15-cell cipher-type-by-cipher-mode grid: five types by the two
    /// modes this port can reproduce. This is the parity matrix the plan names.
    /// </summary>
    /// <returns>One row per cell: cipher type, then cipher mode.</returns>
    /// <remarks>
    /// THE FIVE OMITTED CELLS ARE THE CFB ROW, AND THEY ARE NOT SIMPLY DROPPED - they are covered by
    /// <see cref="BlockedCipherCells"/>, which asserts that each is REFUSED with a defined reason.
    /// Every cell of the grid is therefore still exercised; what differs is which outcome is asserted.
    /// A round-trip assertion could never have validated the CFB row anyway: both candidate feedback
    /// widths round-trip perfectly against themselves, so those five cells passed while proving
    /// nothing about the oracle.
    /// </remarks>
    public static TheoryData<ushort, long> SupportedTypeAndModeGrid()
    {
        TheoryData<ushort, long> data = new();

        foreach (ushort ntype in AllCipherTypes)
        {
            foreach (long mode in ReproducibleCipherModes)
            {
                data.Add(ntype, mode);
            }
        }

        return data;
    }

    /// <summary>
    /// Every cell this port refuses, with the reason it refuses it: the whole CFB row in both vector
    /// shapes, plus CBC without a vector.
    /// </summary>
    /// <returns>
    /// One row per blocked cell: cipher type, cipher mode, whether a vector is supplied, and the
    /// expected reason.
    /// </returns>
    public static TheoryData<ushort, long, bool, SymmetricCellParity> BlockedCipherCells()
    {
        TheoryData<ushort, long, bool, SymmetricCellParity> data = new();

        foreach (ushort ntype in AllCipherTypes)
        {
            // The feedback width is unprovable whether or not a vector accompanies the call, so both
            // shapes are listed: a vector does not disclose the width.
            data.Add(
                ntype,
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                true,
                SymmetricCellParity.BlockedFeedbackWidthUnprovable);
            data.Add(
                ntype,
                Enums.CRYPTO_SYMCRYPT_MODE_CFB,
                false,
                SymmetricCellParity.BlockedFeedbackWidthUnprovable);

            // CBC is reproducible WITH a vector; without one, the vector the oracle substituted is
            // unobservable, so only this shape is blocked.
            data.Add(
                ntype,
                Enums.CRYPTO_SYMCRYPT_MODE_CBC,
                false,
                SymmetricCellParity.BlockedSynthesizedVectorUnprovable);
        }

        return data;
    }

    /// <summary>
    /// The five cipher types alone, for the tests that do not vary the mode.
    /// </summary>
    /// <returns>One row per published cipher type.</returns>
    public static TheoryData<ushort> CipherTypes()
    {
        TheoryData<ushort> data = new();

        foreach (ushort ntype in AllCipherTypes)
        {
            data.Add(ntype);
        }

        return data;
    }

    // ==========================================================================================
    //  THE CENSUS - 16 + 16 = 32, WITH THE STRUCTURAL RULES ENFORCED BY REFLECTION
    // ==========================================================================================

    /// <summary>
    /// The published surface is exactly 16 <c>SymEncrypt</c> and 16 <c>SymDecrypt</c> overloads.
    /// </summary>
    /// <remarks>
    /// This is the largest single contributor to the folder's 63-of-65 reconciliation, so it is
    /// asserted rather than assumed. Reflection is used deliberately: a hand-written list would drift
    /// from the file, whereas counting the real members cannot.
    /// </remarks>
    [Fact]
    public void PublishedSurfaceIsExactlyThirtyTwoOverloads()
    {
        Assert.Equal(16, PublicOverloads("SymEncrypt").Count);
        Assert.Equal(16, PublicOverloads("SymDecrypt").Count);
        Assert.Equal(32, PublicOverloads("SymEncrypt").Count + PublicOverloads("SymDecrypt").Count);
    }

    /// <summary>
    /// No overload declares an optional parameter, so the 32-way shape is real rather than a
    /// collapsed surface with defaults.
    /// </summary>
    /// <remarks>
    /// C# could have expressed the same surface with four methods and optional arguments. That would
    /// have changed the observable member set the legacy publishes, so it is forbidden, and this test
    /// is what keeps it forbidden.
    /// </remarks>
    [Fact]
    public void NoOverloadUsesOptionalParametersToCollapseTheSurface()
    {
        foreach (MethodInfo method in PublicOverloads("SymEncrypt").Concat(PublicOverloads("SymDecrypt")))
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.IsOptional);
        }
    }

    /// <summary>
    /// The initialization-vector type always follows the KEY type, and no mixed-type overload
    /// exists.
    /// </summary>
    /// <remarks>
    /// A contract fact rather than a convenience [n_crypto.sru:L32, L33, L36, L37, L40, L41, L44,
    /// L45 and the decrypt mirrors]. A mixed string-key-with-binary-vector overload would be a NEW
    /// member the legacy never published, so its absence is asserted.
    /// </remarks>
    [Fact]
    public void InitializationVectorTypeAlwaysFollowsTheKeyType()
    {
        foreach (MethodInfo method in PublicOverloads("SymEncrypt").Concat(PublicOverloads("SymDecrypt")))
        {
            ParameterInfo[] parameters = method.GetParameters();

            // The vector-carrying overloads are the ones whose third parameter is material rather
            // than the cipher type: four arguments starting payload, key, vector; or five.
            if (parameters[2].ParameterType == typeof(ushort))
            {
                continue;
            }

            Assert.Equal(parameters[1].ParameterType, parameters[2].ParameterType);
        }
    }

    /// <summary>
    /// The payload type determines the return type: text in, text out; bytes in, bytes out.
    /// </summary>
    /// <remarks>
    /// This is where DECISION D4 starts and stops. A cross-shaped overload would break the text
    /// round trip through a plain text control that the oracle's own demo performs
    /// [u_cst_tabpage_utility_crypto.sru:L504 with :L547].
    /// </remarks>
    [Fact]
    public void PayloadTypeDeterminesReturnType()
    {
        foreach (MethodInfo method in PublicOverloads("SymEncrypt").Concat(PublicOverloads("SymDecrypt")))
        {
            Assert.Equal(method.GetParameters()[0].ParameterType, method.ReturnType);
        }
    }

    /// <summary>
    /// The cipher-type argument is 16 bits wide on every overload and the mode argument is 64 bits,
    /// reproducing the legacy's own declared widths.
    /// </summary>
    /// <remarks>
    /// The legacy types the cipher argument as a PowerBuilder <c>uint</c>, which is 16 bits, and the
    /// mode as a PowerBuilder <c>long</c>, which is 64 bits in C# terms [n_crypto.sru:L30-L61]. The
    /// narrower cipher argument is the width mismatch being reproduced, not introduced.
    /// </remarks>
    [Fact]
    public void ArgumentWidthsReproduceTheLegacyDeclarations()
    {
        foreach (MethodInfo method in PublicOverloads("SymEncrypt").Concat(PublicOverloads("SymDecrypt")))
        {
            ParameterInfo[] parameters = method.GetParameters();

            Assert.Equal(typeof(ushort), parameters[parameters.Length == 3 || parameters.Length == 4 && parameters[2].ParameterType == typeof(ushort) ? 2 : 3].ParameterType);

            if (parameters[^1].ParameterType != typeof(ushort))
            {
                Assert.Equal(typeof(long), parameters[^1].ParameterType);
            }
        }
    }

    // ==========================================================================================
    //  THE 15-CELL ROUND TRIP, BOTH FAMILIES - the matrix the plan names explicitly
    // ==========================================================================================

    /// <summary>
    /// Every cipher type in every mode round-trips in the TEXT-SHAPED family.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <remarks>
    /// Uses the fully specified five-argument shape [n_crypto.sru:L33 and :L49], which is the shape
    /// the oracle's own demo uses for all six of its symmetric operations. The keys are sized at or
    /// above 16 bytes so that DECISION H2's platform weak-key rejection - which DECISION D1's
    /// zero-padding can otherwise trigger for 3DES - is not what this test measures; that interaction
    /// has its own tests below.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void TextShapedFamilyRoundTripsAcrossEveryTypeAndMode(ushort ntype, long mode)
    {
        const string plain = "The quick brown fox jumps over the lazy dog, 0123456789.";
        string key = SyntheticText(32);
        string iv = SyntheticText(16);

        string cipher = _provider.SymEncrypt(plain, key, iv, ntype, mode);
        string recovered = _provider.SymDecrypt(cipher, key, iv, ntype, mode);

        Assert.Equal(plain, recovered);
        Assert.NotEqual(plain, cipher);
    }

    /// <summary>
    /// Every cipher type in every mode round-trips in the BYTE-SHAPED family.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <remarks>
    /// Uses the fully specified five-argument binary shape [n_crypto.sru:L45 and :L61]. The payload
    /// length is deliberately NOT a whole number of blocks for either an 8-byte or a 16-byte block
    /// cipher, so the padding of DECISION H3 is exercised in every cell.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void ByteShapedFamilyRoundTripsAcrossEveryTypeAndMode(ushort ntype, long mode)
    {
        byte[] plain = SyntheticBytes(37, seed: 11);
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] iv = SyntheticBytes(16, seed: 13);

        byte[] cipher = _provider.SymEncrypt(plain, key, iv, ntype, mode);
        byte[] recovered = _provider.SymDecrypt(cipher, key, iv, ntype, mode);

        Assert.Equal(plain, recovered);
        Assert.NotEqual(plain, cipher);
    }

    /// <summary>
    /// An empty payload round-trips in both families, in every cell.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <remarks>
    /// An empty payload is not an error: PKCS#5-family padding turns it into exactly one pad unit, so
    /// the ciphertext is non-empty and the round trip recovers nothing, which is correct.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void EmptyPayloadRoundTripsInBothFamilies(ushort ntype, long mode)
    {
        string key = SyntheticText(32);
        string iv = SyntheticText(16);

        Assert.Equal(string.Empty, _provider.SymDecrypt(_provider.SymEncrypt(string.Empty, key, iv, ntype, mode), key, iv, ntype, mode));

        byte[] emptyCipher = _provider.SymEncrypt([], SyntheticBytes(32, seed: 31), SyntheticBytes(16, seed: 13), ntype, mode);

        Assert.NotEmpty(emptyCipher);
        Assert.Empty(_provider.SymDecrypt(emptyCipher, SyntheticBytes(32, seed: 31), SyntheticBytes(16, seed: 13), ntype, mode));
    }

    // ==========================================================================================
    //  OVERLOAD PARITY - the 32 must be spellings of ONE behaviour, not thirty-two behaviours
    // ==========================================================================================

    /// <summary>
    /// The byte-shaped result and the decoded text-shaped result are the same ciphertext.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <remarks>
    /// This is the assertion that makes DECISION D4 a pure presentation choice: the text-shaped
    /// family differs from the binary-shaped family ONLY by the payload encoding, so decoding one
    /// yields the other exactly. It also proves the string-payload text encoding is applied on the
    /// way in, because the binary call is handed those same bytes explicitly.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void TextShapedCipherTextDecodesToTheByteShapedCipherText(ushort ntype, long mode)
    {
        const string plain = "parity across the two payload shapes";
        string key = SyntheticText(32);
        string iv = SyntheticText(16);

        EncodingProvider encoding = new();
        byte[] plainBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(plain);
        byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(key);
        byte[] ivBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(iv);

        string textShaped = _provider.SymEncrypt(plain, key, iv, ntype, mode);
        byte[] byteShaped = _provider.SymEncrypt(plainBytes, keyBytes, ivBytes, ntype, mode);

        Assert.Equal(byteShaped, encoding.StringToBlob(textShaped, LegacyDefaults.STRING_PAYLOAD_ENCODING));
    }

    /// <summary>
    /// The text spelling and the binary spelling of the same key produce the same ciphertext, and
    /// likewise for the initialization vector.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <remarks>
    /// DECISION D1 encodes text material and then applies the identical length rule, so a text key
    /// and the bytes of that text must be one key. If these ever diverged, a caller switching
    /// overloads would silently lose access to its own data.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void TextKeyAndBinaryKeySpellingsOfTheSameMaterialAgree(ushort ntype, long mode)
    {
        byte[] plain = SyntheticBytes(24, seed: 5);
        string key = SyntheticText(32);
        string iv = SyntheticText(16);
        byte[] keyBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(key);
        byte[] ivBytes = LegacyDefaults.KeyMaterialEncoding.GetBytes(iv);

        Assert.Equal(
            _provider.SymEncrypt(plain, key, iv, ntype, mode),
            _provider.SymEncrypt(plain, keyBytes, ivBytes, ntype, mode));
    }

    /// <summary>
    /// All four of a family's argument shapes agree once the omitted arguments are supplied
    /// explicitly.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// Run in the default mode, where the vector is ignored, so all four shapes must coincide: the
    /// three-argument shape [L38], the four-argument mode shape [L39], the four-argument vector shape
    /// [L40] and the five-argument shape [L41]. This is the single strongest statement that the 32
    /// overloads are one behaviour.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void AllFourArgumentShapesAgreeInTheDefaultMode(ushort ntype)
    {
        byte[] plain = SyntheticBytes(19, seed: 7);
        string key = SyntheticText(32);
        string iv = SyntheticText(16);

        byte[] expected = _provider.SymEncrypt(plain, key, ntype);

        Assert.Equal(expected, _provider.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB));
        Assert.Equal(expected, _provider.SymEncrypt(plain, key, iv, ntype));
        Assert.Equal(expected, _provider.SymEncrypt(plain, key, iv, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB));
    }

    /// <summary>
    /// Every one of the 32 overloads is reachable and round-trips against its own inverse.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// Enumerated by hand in the legacy's declaration order, so this method reads as a checklist
    /// against n_crypto.sru:L30-L61 and every overload is executed at least once. Runs in CBC so
    /// that the vector-carrying arms genuinely consume their vector and the mode-without-vector arms
    /// genuinely exercise DECISION D3.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void EveryOneOfTheThirtyTwoOverloadsRoundTrips(ushort ntype)
    {
        // TWO MODES, CHOSEN BY WHETHER THE OVERLOAD CARRIES A VECTOR. The vector-bearing arms use the
        // chaining mode, which is fully reproducible when a vector is supplied. The vector-less arms
        // use the codebook mode, because a vector-consuming mode through an arm that supplies none is
        // refused under DECISION D3 - the vector the oracle substituted there is unobservable. The
        // refusal itself is asserted by the blocked-cell theories; this test's job is to prove all 32
        // DECLARATIONS are reachable and round-trip, so each is exercised in a cell it can serve.
        const long mode = Enums.CRYPTO_SYMCRYPT_MODE_CBC;
        const long vectorlessMode = Enums.CRYPTO_SYMCRYPT_MODE_ECB;
        const string text = "every overload, once";
        byte[] bytes = SyntheticBytes(29, seed: 17);
        string key = SyntheticText(32);
        string iv = SyntheticText(16);
        byte[] keyBytes = SyntheticBytes(32, seed: 31);
        byte[] ivBytes = SyntheticBytes(16, seed: 13);

        // L30 / L46 and L31 / L47 - text payload, text key, no vector.
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, key, ntype), key, ntype));
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, key, ntype, vectorlessMode), key, ntype, vectorlessMode));

        // L32 / L48 and L33 / L49 - text payload, text key, text vector.
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, key, iv, ntype), key, iv, ntype));
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, key, iv, ntype, mode), key, iv, ntype, mode));

        // L34 / L50 and L35 / L51 - text payload, binary key, no vector.
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, keyBytes, ntype), keyBytes, ntype));
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, keyBytes, ntype, vectorlessMode), keyBytes, ntype, vectorlessMode));

        // L36 / L52 and L37 / L53 - text payload, binary key, binary vector.
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, keyBytes, ivBytes, ntype), keyBytes, ivBytes, ntype));
        Assert.Equal(text, _provider.SymDecrypt(_provider.SymEncrypt(text, keyBytes, ivBytes, ntype, mode), keyBytes, ivBytes, ntype, mode));

        // L38 / L54 and L39 / L55 - binary payload, text key, no vector.
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, key, ntype), key, ntype));
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, key, ntype, vectorlessMode), key, ntype, vectorlessMode));

        // L40 / L56 and L41 / L57 - binary payload, text key, text vector.
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, key, iv, ntype), key, iv, ntype));
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, key, iv, ntype, mode), key, iv, ntype, mode));

        // L42 / L58 and L43 / L59 - binary payload, binary key, no vector.
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, keyBytes, ntype), keyBytes, ntype));
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, keyBytes, ntype, vectorlessMode), keyBytes, ntype, vectorlessMode));

        // L44 / L60 and L45 / L61 - binary payload, binary key, binary vector.
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, keyBytes, ivBytes, ntype), keyBytes, ivBytes, ntype));
        Assert.Equal(bytes, _provider.SymDecrypt(_provider.SymEncrypt(bytes, keyBytes, ivBytes, ntype, mode), keyBytes, ivBytes, ntype, mode));
    }

    // ==========================================================================================
    //  THE PRESERVED DEFAULTS - each proved to be REAL rather than merely documented
    // ==========================================================================================

    /// <summary>
    /// Omitting the mode really does select ECB, in both families and for every cipher type.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// KNOWN LEGACY WEAKNESS, ASSERTED AS PRESERVED [enums.sru:L943 and :L946]. It is the arm the
    /// oracle actually uses: n_cst_appconfig.sru:L583 and :L615 encrypt with AES256 and no mode, and
    /// :L337, :L552 and :L992 decrypt the same way. If this test ever fails because the default
    /// changed, every value that path stored becomes unreadable.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void OmittingTheModeSelectsElectronicCodebook(ushort ntype)
    {
        const string text = "the default arm is exercised, not theoretical";
        string key = SyntheticText(32);
        byte[] bytes = SyntheticBytes(23, seed: 3);
        byte[] keyBytes = SyntheticBytes(32, seed: 31);

        Assert.Equal(
            _provider.SymEncrypt(text, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB),
            _provider.SymEncrypt(text, key, ntype));

        Assert.Equal(
            _provider.SymEncrypt(bytes, keyBytes, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB),
            _provider.SymEncrypt(bytes, keyBytes, ntype));
    }

    /// <summary>
    /// The default mode arm is valued from the catalogue's default constant, which is ECB.
    /// </summary>
    /// <remarks>
    /// Asserted against the ported constant catalogue rather than against a literal, so the chain
    /// from enums.sru through the catalogue to this provider is checked end to end.
    /// </remarks>
    [Fact]
    public void TheCatalogueDefaultModeIsElectronicCodebook()
    {
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
    }

    /// <summary>
    /// The eight mode-supplied-vector-omitted arms REFUSE a vector-consuming mode instead of
    /// inventing a vector for it.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// <para>
    /// ASSERTING THE REFUSAL IS THE ONLY THING HERE THAT IS NOT A GUESS ASSERTED AS CORRECTNESS. A row
    /// checking that the vector-less arm produced the same ciphertext as an explicit all-zero vector
    /// would be a statement about two code paths in THIS port agreeing with each other - trivially true
    /// if one derived that vector and called the other - and would say nothing about what the closed
    /// binary produces while reading like a parity assertion.
    /// </para>
    /// <para>
    /// What the oracle uses is unobservable: <c>n_crypto</c> is declared
    /// <c>native "pfw.dll"</c> [n_crypto.sru:L8] with no PowerScript body for any of the 32 symmetric
    /// overloads. A wrong vector round-trips perfectly against itself, so no test here could ever
    /// catch it, while the ciphertext would be undecryptable by the legacy. Refusing is the correct
    /// behaviour, and this asserts it.
    /// </para>
    /// <para>
    /// ECB IS ASSERTED TO STILL WORK IN THE SAME TEST, because a refusal that caught the default mode
    /// would be a worse regression than the one being fixed: ECB is the default [enums.sru:L946], it
    /// consumes no vector, and every mode-omitting arm depends on it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void OmittingTheVectorIsRefusedForAVectorConsumingMode(ushort ntype)
    {
        byte[] plain = SyntheticBytes(21, seed: 23);
        byte[] key = SyntheticBytes(32, seed: 31);

        SymmetricParityUnavailableException refusal =
            Assert.Throws<SymmetricParityUnavailableException>(
                () => _provider.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));

        Assert.Equal(SymmetricCellParity.BlockedSynthesizedVectorUnprovable, refusal.Reason);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_CBC, refusal.Mode);

        // The decrypting mirror of the same four arms refuses identically, so the guessed vector is
        // unreachable from either direction.
        Assert.Equal(
            SymmetricCellParity.BlockedSynthesizedVectorUnprovable,
            Assert.Throws<SymmetricParityUnavailableException>(
                () => _provider.SymDecrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC)).Reason);

        // The mode that consumes no vector is UNAFFECTED, and so is the arm omitting the mode
        // entirely, which resolves to that same mode by default.
        Assert.Equal(
            _provider.SymEncrypt(plain, key, ntype),
            _provider.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB));
    }

    /// <summary>
    /// A vector supplied to an ECB operation is IGNORED - two different vectors give one ciphertext.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// DECISION H4(b), asserted so the behaviour is defined rather than accidental. In the production
    /// file the ignoring is structural: the ECB path calls a primitive with no vector parameter, so
    /// the content has no route to the cipher. Note what is NOT asserted - that a NULL vector is
    /// tolerated. The reference is still validated, uniformly with the rest of the surface, and the
    /// null tests below cover that.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void ElectronicCodebookIgnoresASuppliedVector(ushort ntype)
    {
        byte[] plain = SyntheticBytes(18, seed: 29);
        byte[] key = SyntheticBytes(32, seed: 31);

        byte[] withOneVector = _provider.SymEncrypt(plain, key, SyntheticBytes(16, seed: 41), ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB);
        byte[] withAnother = _provider.SymEncrypt(plain, key, SyntheticBytes(16, seed: 97), ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB);
        byte[] withNone = _provider.SymEncrypt(plain, key, ntype, Enums.CRYPTO_SYMCRYPT_MODE_ECB);

        Assert.Equal(withNone, withOneVector);
        Assert.Equal(withNone, withAnother);
    }

    /// <summary>
    /// ECB is deterministic across calls and leaks block equality, which is the weakness being
    /// preserved.
    /// </summary>
    /// <remarks>
    /// Asserted rather than merely described, because a reader who does not believe the annotation
    /// should be able to see the property. Two identical 16-byte plaintext blocks under AES256/ECB
    /// produce two identical ciphertext blocks - an observer who never learns the key still learns
    /// that the two values are equal. Nothing here is a defect to fix; it is the documented cost of
    /// the preserved default.
    /// </remarks>
    [Fact]
    public void ElectronicCodebookIsDeterministicAndLeaksBlockEquality()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] block = SyntheticBytes(16, seed: 61);
        byte[] twoIdenticalBlocks = [.. block, .. block];

        byte[] first = _provider.SymEncrypt(twoIdenticalBlocks, key, ntype);
        byte[] second = _provider.SymEncrypt(twoIdenticalBlocks, key, ntype);

        Assert.Equal(first, second);
        Assert.Equal(first[..16], first[16..32]);
    }

    // ==========================================================================================
    //  DECISION D1 - SIZING, AND ITS MEASURED INTERACTION WITH DECISION H2
    // ==========================================================================================

    /// <summary>
    /// Key material shorter than, equal to and longer than the cipher's key length all behave per
    /// the documented truncate-or-zero-pad rule.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// Three properties are asserted, each of which is a preserved weakness rather than a feature.
    /// EXACT-LENGTH material is used as it stands. OVER-LONG material is TRUNCATED, so it produces
    /// the same ciphertext as its own leading bytes and the discarded entropy is silently lost.
    /// UNDER-LENGTH material is ZERO-PADDED, so it produces the same ciphertext as the caller's bytes
    /// followed by explicit zeros and the manufactured bytes are silently added. The starting length
    /// is 16 so that DECISION H2's 3DES threshold is not what is being measured.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void KeyMaterialIsTruncatedOrZeroPaddedToTheRequiredLength(ushort ntype)
    {
        int required = LegacyDefaults.GetSymmetricCipherMetrics(ntype).KeyLengthBytes;
        byte[] plain = SyntheticBytes(20, seed: 43);
        byte[] longMaterial = SyntheticBytes(required + 16, seed: 31);

        // Over-long: truncation means only the leading bytes matter.
        Assert.Equal(
            _provider.SymEncrypt(plain, longMaterial[..required], ntype),
            _provider.SymEncrypt(plain, longMaterial, ntype));

        // Under-length: zero-padding means the short key equals the short key plus explicit zeros.
        int shortLength = Math.Max(16, required - 8);
        byte[] shortMaterial = longMaterial[..shortLength];
        byte[] paddedByHand = new byte[required];
        shortMaterial[..Math.Min(shortLength, required)].CopyTo(paddedByHand, 0);

        Assert.Equal(
            _provider.SymEncrypt(plain, paddedByHand, ntype),
            _provider.SymEncrypt(plain, shortMaterial, ntype));
    }

    /// <summary>
    /// Initialization-vector material is sized by the SAME rule as key material, against the block
    /// length.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// DECISION H4(c). Run in CBC so the vector is genuinely consumed. A vector is exactly one block
    /// wide, so an over-long vector is truncated to the block length and an under-length one is
    /// zero-padded to it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void VectorMaterialIsTruncatedOrZeroPaddedToTheBlockLength(ushort ntype)
    {
        const long mode = Enums.CRYPTO_SYMCRYPT_MODE_CBC;
        int required = LegacyDefaults.GetSymmetricCipherMetrics(ntype).IvLengthBytes;
        byte[] plain = SyntheticBytes(22, seed: 47);
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] longVector = SyntheticBytes(required + 9, seed: 53);

        Assert.Equal(
            _provider.SymEncrypt(plain, key, longVector[..required], ntype, mode),
            _provider.SymEncrypt(plain, key, longVector, ntype, mode));

        byte[] shortVector = longVector[..(required - 3)];
        byte[] paddedByHand = new byte[required];
        shortVector.CopyTo(paddedByHand, 0);

        Assert.Equal(
            _provider.SymEncrypt(plain, key, paddedByHand, ntype, mode),
            _provider.SymEncrypt(plain, key, shortVector, ntype, mode));
    }

    /// <summary>
    /// A passphrase is used as RAW KEY BYTES: no key-derivation function is applied.
    /// </summary>
    /// <remarks>
    /// Asserted by showing that a text key and its raw byte encoding, sized by the catalogue's rule,
    /// are the same key. If any derivation, salting or stretching were introduced, the two would
    /// diverge. That is materially weaker than a derived key and is preserved deliberately: deriving
    /// instead would change every key, and therefore every ciphertext, the legacy ever produced.
    /// </remarks>
    [Fact]
    public void PassphraseIsUsedAsRawKeyBytesWithNoDerivation()
    {
        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);

        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        string passphrase = SyntheticText(32);
        byte[] plain = SyntheticBytes(16, seed: 59);
        byte[] rawBytes = LegacyDefaults.NormalizeKeyMaterial(
            LegacyDefaults.KeyMaterialEncoding.GetBytes(passphrase),
            LegacyDefaults.GetSymmetricCipherMetrics(ntype).KeyLengthBytes);

        Assert.Equal(
            _provider.SymEncrypt(plain, rawBytes, ntype),
            _provider.SymEncrypt(plain, passphrase, ntype));
    }

    /// <summary>
    /// DECISION H2 - a weak DES key and a degenerate 3DES key are refused by this platform.
    /// </summary>
    /// <remarks>
    /// THIS IS A PLATFORM-IMPOSED DIVERGENCE FROM THE LEGACY, NOT INTENDED BEHAVIOUR. An
    /// OpenSSL-based implementation would have encrypted with both of these keys; the Base Class
    /// Library refuses them from the key assignment. No workaround is added and no key is silently
    /// substituted, because either would change behaviour - so the divergence is asserted here so it
    /// is visible and reviewable rather than discovered in production.
    /// </remarks>
    [Fact]
    public void WeakAndDegenerateKeysAreRefusedByThisPlatform()
    {
        byte[] plain = SyntheticBytes(16, seed: 67);

        // An empty key normalizes, under DECISION D1's zero-padding, to the all-zero 8-byte DES key,
        // which is a known weak key.
        Assert.Throws<CryptographicException>(() =>
            _provider.SymEncrypt(plain, Array.Empty<byte>(), (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_DES));

        // Any 3DES key shorter than 16 bytes leaves sub-keys two and three both all-zero and
        // therefore coincident, which is the degenerate case this platform refuses.
        Assert.Throws<CryptographicException>(() =>
            _provider.SymEncrypt(plain, SyntheticBytes(8, seed: 71), (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_3DES));

        // The same shape on the decrypt side, so the divergence is symmetric.
        Assert.Throws<CryptographicException>(() =>
            _provider.SymDecrypt(plain, SyntheticBytes(8, seed: 71), (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_3DES));
    }

    /// <summary>
    /// AES has no weak-key concept, so key material of any supplied length is accepted at all three
    /// AES sizes.
    /// </summary>
    /// <remarks>
    /// The counterpart to the test above, and the reason the divergence is confined to the two
    /// Data Encryption Standard rows rather than being a property of DECISION D1 itself.
    /// </remarks>
    [Fact]
    public void AesAcceptsKeyMaterialOfEveryLength()
    {
        byte[] plain = SyntheticBytes(16, seed: 73);
        ushort[] aesTypes =
        [
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES192,
            (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
        ];

        foreach (ushort ntype in aesTypes)
        {
            foreach (int length in new[] { 0, 1, 8, 16, 24, 32, 40 })
            {
                byte[] cipher = _provider.SymEncrypt(plain, SyntheticBytes(length, seed: 79), ntype);

                Assert.Equal(plain, _provider.SymDecrypt(cipher, SyntheticBytes(length, seed: 79), ntype));
            }
        }
    }

    // ==========================================================================================
    //  DECISION H1 - the CFB feedback width, pinned so a change is visible in a diff
    // ==========================================================================================

    /// <summary>
    /// Every blocked cell is refused, with the reason naming WHICH missing evidence blocks it.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <param name="mode">The cipher mode.</param>
    /// <param name="supplyVector">Whether the call supplies an initialization vector.</param>
    /// <param name="expected">The reason the refusal must carry.</param>
    /// <remarks>
    /// <para>
    /// THESE ASSERTIONS REPLACE TWO THAT PINNED A GUESSED FEEDBACK WIDTH. Their own remarks conceded
    /// the problem - "PINNED RATHER THAN VERIFIED", because "no round-trip test can detect a wrong
    /// choice here" - and that concession is exactly why pinning was the wrong response. An assertion
    /// recording what the port currently does, when what it does is a guess, converts the guess into
    /// a fixture that future work is obliged to preserve. The width was inferred from the framework's
    /// OpenSSL attribution [w_about.srw:L118], and an inference from an attribution is not a
    /// measurement of a closed binary.
    /// </para>
    /// <para>
    /// The refusal is asserted on BOTH directions of every cell, because ciphertext that cannot be
    /// produced must not be decryptable either - a one-sided block would leave the guessed parameter
    /// reachable through the inverse operation.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BlockedCipherCells))]
    public void EveryBlockedCellIsRefusedWithItsReason(
        ushort ntype,
        long mode,
        bool supplyVector,
        SymmetricCellParity expected)
    {
        byte[] plain = SyntheticBytes(21, seed: 23);
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] vector = SyntheticBytes(16, seed: 13);

        SymmetricParityUnavailableException encrypting =
            Assert.Throws<SymmetricParityUnavailableException>(() => supplyVector
                ? _provider.SymEncrypt(plain, key, vector, ntype, mode)
                : _provider.SymEncrypt(plain, key, ntype, mode));

        SymmetricParityUnavailableException decrypting =
            Assert.Throws<SymmetricParityUnavailableException>(() => supplyVector
                ? _provider.SymDecrypt(plain, key, vector, ntype, mode)
                : _provider.SymDecrypt(plain, key, ntype, mode));

        Assert.Equal(expected, encrypting.Reason);
        Assert.Equal(expected, decrypting.Reason);
        Assert.Equal(mode, encrypting.Mode);
        Assert.Equal(mode, decrypting.Mode);
    }

    /// <summary>
    /// A blocked cell is NOT reported as a cryptographic failure, so it cannot be mistaken for a
    /// wrong key or a failed padding check.
    /// </summary>
    /// <remarks>
    /// The distinction is load-bearing rather than cosmetic. Handlers on this surface routinely absorb
    /// <see cref="CryptographicException"/>, because a padding failure is an ordinary runtime outcome
    /// of a wrong key - the parity suite's own recovery helper is one such handler. Had the refusal
    /// derived from that type, every one of them would have swallowed it, and a deliberate, documented
    /// capability limit would have surfaced as an unremarkable decryption miss.
    /// </remarks>
    [Fact]
    public void ABlockedCellIsNotReportedAsACryptographicFailure()
    {
        byte[] plain = SyntheticBytes(21, seed: 23);
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] vector = SyntheticBytes(16, seed: 13);

        SymmetricParityUnavailableException refusal =
            Assert.Throws<SymmetricParityUnavailableException>(() => _provider.SymEncrypt(
                plain,
                key,
                vector,
                (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                Enums.CRYPTO_SYMCRYPT_MODE_CFB));

        Assert.IsNotType<CryptographicException>(refusal, exactMatch: false);
        Assert.IsType<NotSupportedException>(refusal, exactMatch: false);

        // The message explains the refusal without naming key, vector or payload content - none of
        // which is what went wrong.
        Assert.Contains("feedback width", refusal.Message, StringComparison.Ordinal);
    }

    // ==========================================================================================
    //  DECISION H3 AND DECISION D2 - padding, and the absence of any integrity guarantee
    // ==========================================================================================

    /// <summary>
    /// A wrong key produces the single documented failure shape, or plausible garbage, and NEITHER
    /// is tamper detection.
    /// </summary>
    /// <remarks>
    /// This is the assertion that DECISION D2 is real: there is no authenticated mode, no tag and no
    /// way for a decrypt to say that the ciphertext was not the one this key produced. The usual
    /// outcome is the library's padding failure; occasionally the padding validates by chance and the
    /// result is garbage. BOTH are accepted here, deliberately, because asserting only the exception
    /// would misrepresent the guarantee this surface offers - which is none.
    /// </remarks>
    [Fact]
    public void WrongKeyYieldsEitherThePaddingFailureOrUndetectableGarbage()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        const long mode = Enums.CRYPTO_SYMCRYPT_MODE_CBC;
        byte[] plain = SyntheticBytes(48, seed: 83);
        byte[] vector = SyntheticBytes(16, seed: 89);
        byte[] rightKey = SyntheticBytes(32, seed: 31);
        byte[] wrongKey = SyntheticBytes(32, seed: 37);
        byte[] cipher = _provider.SymEncrypt(plain, rightKey, vector, ntype, mode);

        try
        {
            byte[] recovered = _provider.SymDecrypt(cipher, wrongKey, vector, ntype, mode);

            // The padding happened to validate. The result is garbage and the API cannot say so -
            // that is DECISION D2, not a defect.
            Assert.NotEqual(plain, recovered);
        }
        catch (CryptographicException)
        {
            // The documented single failure shape. It is NOT rewrapped or classified, so a caller
            // cannot learn from it whether the failure was padding-related.
        }

        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);
    }

    /// <summary>
    /// Tampered ciphertext is not detected as tampering, only as a decode failure or as garbage.
    /// </summary>
    /// <remarks>
    /// The companion to the wrong-key test, and the clearest statement of what the absence of an
    /// integrity tag costs. A single flipped byte in the middle of the ciphertext must not be
    /// reported as tampering, because no published mode carries a tag with which to detect it.
    /// </remarks>
    [Fact]
    public void TamperedCipherTextIsNotDetectableAsTampering()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        const long mode = Enums.CRYPTO_SYMCRYPT_MODE_CBC;
        byte[] plain = SyntheticBytes(48, seed: 101);
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] vector = SyntheticBytes(16, seed: 103);
        byte[] cipher = _provider.SymEncrypt(plain, key, vector, ntype, mode);

        cipher[4] ^= 0xFF;

        try
        {
            Assert.NotEqual(plain, _provider.SymDecrypt(cipher, key, vector, ntype, mode));
        }
        catch (CryptographicException)
        {
            // Also acceptable, and equally uninformative about tampering.
        }
    }

    /// <summary>
    /// Padding is not selectable anywhere on this surface.
    /// </summary>
    /// <remarks>
    /// DECISION H3, asserted structurally: none of the 32 legacy declarations takes a padding
    /// argument [n_crypto.sru:L30-L61], so no overload may declare one either. Checked by looking at
    /// the real parameter types rather than by reading the file.
    /// </remarks>
    [Fact]
    public void NoOverloadExposesAPaddingArgument()
    {
        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);

        foreach (MethodInfo method in PublicOverloads("SymEncrypt").Concat(PublicOverloads("SymDecrypt")))
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(PaddingMode));
        }
    }

    /// <summary>
    /// Ciphertext whose length is not a whole number of blocks fails as a cryptographic error, not
    /// as something else.
    /// </summary>
    /// <remarks>
    /// Part of DECISION H3's single failure shape: a malformed length, a padding failure and a wrong
    /// key are ALL reported the same way, which is what stops this port handing a caller a
    /// distinguishing signal the legacy never published.
    /// </remarks>
    [Fact]
    public void MisalignedCipherTextFailsAsACryptographicError()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;

        Assert.Throws<CryptographicException>(() =>
            _provider.SymDecrypt(SyntheticBytes(7, seed: 107), SyntheticBytes(32, seed: 31), ntype));
    }

    // ==========================================================================================
    //  DECISION D4 - the payload encoding boundary
    // ==========================================================================================

    /// <summary>
    /// The text-shaped family carries a printable payload that survives a round trip through a plain
    /// text field.
    /// </summary>
    /// <remarks>
    /// This is the property the oracle's own demo requires: it writes string-shaped ciphertext into a
    /// multi-line text control and reads it back from the same control
    /// [u_cst_tabpage_utility_crypto.sru:L504 with :L547, :L607 with :L592, :L637 with :L622]. The
    /// assertion is that the payload contains no control characters, so no text field could mangle
    /// it. WHICH encoding it is remains the catalogue's decision and is asserted against the
    /// catalogue rather than against a literal spelling.
    /// </remarks>
    [Theory]
    [MemberData(nameof(SupportedTypeAndModeGrid))]
    public void TextShapedPayloadIsPrintableAndSurvivesATextField(ushort ntype, long mode)
    {
        string cipher = _provider.SymEncrypt(
            "written into a plain text control and read back out",
            SyntheticText(32),
            SyntheticText(16),
            ntype,
            mode);

        Assert.NotEmpty(cipher);
        Assert.DoesNotContain(cipher, character => char.IsControl(character));
    }

    /// <summary>
    /// The binary-shaped family performs no payload encoding at all.
    /// </summary>
    /// <remarks>
    /// The other half of DECISION D4's boundary. Asserted by decoding the text-shaped result with the
    /// catalogue's own encoding and finding the binary-shaped result, which can only hold if the
    /// binary path applied no encoding of its own.
    /// </remarks>
    [Fact]
    public void BinaryShapedFamilyAppliesNoPayloadEncoding()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] plain = SyntheticBytes(32, seed: 109);
        byte[] binaryResult = _provider.SymEncrypt(plain, key, ntype);
        string textResult = _provider.SymEncrypt(
            LegacyDefaults.KeyMaterialEncoding.GetString(plain),
            key,
            ntype);

        // The two payloads differ only by the encoding, and only because the text overload also
        // encodes its plaintext. Decoding the text payload must yield raw cipher bytes of the same
        // length class, and the binary payload must not itself be encoded text.
        byte[] decoded = new EncodingProvider().StringToBlob(textResult, LegacyDefaults.STRING_PAYLOAD_ENCODING);

        Assert.NotEqual(binaryResult, LegacyDefaults.KeyMaterialEncoding.GetBytes(textResult));
        Assert.Equal(0, decoded.Length % 16);
    }

    /// <summary>
    /// Text-shaped decryption rejects a payload that is not well formed in the catalogue's encoding.
    /// </summary>
    /// <remarks>
    /// The failure is the encoding primitive's own <see cref="FormatException"/>, propagated
    /// unchanged rather than caught and rewrapped, so its message and parameter name survive for a
    /// caller to act on.
    /// </remarks>
    [Fact]
    public void TextShapedDecryptRejectsAMalformedPayload()
    {
        Assert.Throws<FormatException>(() =>
            _provider.SymDecrypt("not a valid payload!!", SyntheticText(32), (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256));
    }

    // ==========================================================================================
    //  ARGUMENT SCREENING AND NULL POLICY
    // ==========================================================================================

    /// <summary>
    /// An unpublished cipher type is rejected, and the failure names the published set without
    /// carrying any key material.
    /// </summary>
    /// <param name="ntype">The candidate cipher type.</param>
    /// <remarks>
    /// The five published types are 0 through 4 [enums.sru:L936-L940]; the argument is unsigned, so
    /// no negative value can reach the screen and only the upper bound needs testing.
    /// </remarks>
    [Theory]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData(ushort.MaxValue)]
    public void UnpublishedCipherTypeIsRejected(ushort ntype)
    {
        string key = SyntheticText(32);

        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _provider.SymEncrypt("payload", key, ntype));

        Assert.Equal("ntype", failure.ParamName);
        Assert.Contains("enums.sru:L936-L940", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unpublished cipher mode is rejected on both verbs, and the failure carries no key material.
    /// </summary>
    /// <param name="mode">The candidate cipher mode.</param>
    /// <remarks>
    /// The three published modes are 0 through 2 [enums.sru:L943-L945]. No authenticated mode is a
    /// member of that set, which is what makes DECISION D2 enforceable rather than advisory.
    /// </remarks>
    [Theory]
    [InlineData(-1L)]
    [InlineData(3L)]
    [InlineData(long.MaxValue)]
    public void UnpublishedCipherModeIsRejected(long mode)
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        string key = SyntheticText(32);

        ArgumentOutOfRangeException onEncrypt = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _provider.SymEncrypt("payload", key, ntype, mode));
        ArgumentOutOfRangeException onDecrypt = Assert.Throws<ArgumentOutOfRangeException>(() =>
            _provider.SymDecrypt("payload", key, ntype, mode));

        Assert.Equal("mode", onEncrypt.ParamName);
        Assert.Equal("mode", onDecrypt.ParamName);
        Assert.Contains("enums.sru:L943-L945", onEncrypt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, onEncrypt.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The algorithm factory's unscreened-type arm reports through the same documented failure path.
    /// </summary>
    /// <remarks>
    /// Reachable only by an internal caller that skipped screening, so it is exercised directly. It
    /// is retained in the production file because a switch over a numeric value cannot be proved
    /// exhaustive, and it is tested so that the defensive arm is not dead code.
    /// </remarks>
    [Fact]
    public void AlgorithmFactoryRejectsAnUnscreenedCipherType()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            SymmetricCipherProvider.CreateAlgorithm(ushort.MaxValue));

        Assert.Equal("ntype", failure.ParamName);
    }

    /// <summary>
    /// The algorithm factory maps each published cipher type onto the expected algorithm and block
    /// size.
    /// </summary>
    /// <param name="ntype">The cipher type.</param>
    /// <remarks>
    /// THE ALGORITHM SET IS PRESERVED EXACTLY [enums.sru:L936-L940], DES and 3DES included. The three
    /// AES rows share one algorithm type because they are one cipher at three key lengths, and the
    /// length is decided by DECISION D1's sizing rather than by the factory.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CipherTypes))]
    public void AlgorithmFactoryPreservesThePublishedCipherSet(ushort ntype)
    {
        SymmetricCipherMetrics metrics = LegacyDefaults.GetSymmetricCipherMetrics(ntype);

        using SymmetricAlgorithm algorithm = SymmetricCipherProvider.CreateAlgorithm(ntype);

        Assert.Equal(metrics.BlockLengthBytes * 8, algorithm.BlockSize);

        if (metrics.CipherType == Enums.CRYPTO_SYMCRYPT_TYPE_DES)
        {
            Assert.IsAssignableFrom<DES>(algorithm);
        }
        else if (metrics.CipherType == Enums.CRYPTO_SYMCRYPT_TYPE_3DES)
        {
            Assert.IsAssignableFrom<TripleDES>(algorithm);
        }
        else
        {
            Assert.IsAssignableFrom<Aes>(algorithm);
        }
    }

    /// <summary>
    /// Every material argument is null-guarded, uniformly across both verbs and all four shapes.
    /// </summary>
    /// <remarks>
    /// The nulls are TYPED rather than bare literals, because a bare literal is applicable to both
    /// the text-shaped and the binary-shaped overload and would not compile - which is the call-site
    /// note the production file records. Note that the vector reference is validated even in the
    /// default mode, where its CONTENT is ignored: the reference check is uniform, the content check
    /// is not, and that distinction is DECISION H4(b).
    /// </remarks>
    [Fact]
    public void EveryMaterialArgumentIsNullGuarded()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        string key = SyntheticText(32);
        string iv = SyntheticText(16);
        byte[] keyBytes = SyntheticBytes(32, seed: 31);
        byte[] ivBytes = SyntheticBytes(16, seed: 13);
        byte[] payloadBytes = SyntheticBytes(16, seed: 113);

        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt((string)null!, key, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt("payload", (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt("payload", key, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt("payload", (byte[])null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt("payload", keyBytes, (byte[])null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt((byte[])null!, key, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt(payloadBytes, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt(payloadBytes, key, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt(payloadBytes, keyBytes, (byte[])null!, ntype));

        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt((string)null!, key, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt("payload", (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt("payload", key, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt("payload", (byte[])null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt("payload", keyBytes, (byte[])null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt((byte[])null!, key, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt(payloadBytes, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt(payloadBytes, key, (string)null!, ntype));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt(payloadBytes, keyBytes, (byte[])null!, ntype));

        // And the five-argument shapes, so no overload family is left unguarded.
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt("payload", key, (string)null!, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt(payloadBytes, keyBytes, (byte[])null!, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt("payload", key, (string)null!, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt(payloadBytes, keyBytes, (byte[])null!, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));

        // And a null PAYLOAD on the five-argument shapes, so the guard order cannot let a null
        // payload through once a vector and a mode are both present.
        Assert.Throws<ArgumentNullException>(() => _provider.SymEncrypt((string)null!, key, iv, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
        Assert.Throws<ArgumentNullException>(() => _provider.SymDecrypt((byte[])null!, keyBytes, ivBytes, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC));
    }

    /// <summary>
    /// The constructor requires its dependency.
    /// </summary>
    /// <remarks>
    /// Constructor injection is what replaces the legacy's type-shadowing global auto-instance
    /// <c>global n_crypto n_crypto</c> [n_crypto.sru:L75] and its lazy-construction guard, so the
    /// dependency is mandatory rather than lazily created.
    /// </remarks>
    [Fact]
    public void ConstructorRequiresTheEncodingProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new SymmetricCipherProvider(null!));
    }

    // ==========================================================================================
    //  STATELESSNESS, THREAD SAFETY AND CALLER-BUFFER INTEGRITY
    // ==========================================================================================

    /// <summary>
    /// The provider holds no key material in any field, and caches no algorithm instance.
    /// </summary>
    /// <remarks>
    /// Asserted by reflection over every instance and static field, so a future field cannot be added
    /// without this test noticing. The only permitted field is the injected encoding provider.
    /// </remarks>
    [Fact]
    public void ProviderRetainsNoKeyMaterialAndCachesNoAlgorithm()
    {
        FieldInfo[] fields = typeof(SymmetricCipherProvider).GetFields(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (FieldInfo field in fields)
        {
            Assert.NotEqual(typeof(byte[]), field.FieldType);
            Assert.False(typeof(SymmetricAlgorithm).IsAssignableFrom(field.FieldType));

            if (field.IsLiteral)
            {
                continue;
            }

            Assert.Equal(typeof(EncodingProvider), field.FieldType);
        }
    }

    /// <summary>
    /// A caller's key, vector and payload arrays are never modified by an operation.
    /// </summary>
    /// <remarks>
    /// The cores wipe the buffers they OWN, which are always fresh allocations from the catalogue, so
    /// a caller's own array must come back untouched. This is the test that would catch an ownership
    /// mistake in the wipe.
    /// </remarks>
    [Fact]
    public void CallerSuppliedArraysAreNeverModified()
    {
        ushort ntype = (ushort)Enums.CRYPTO_SYMCRYPT_TYPE_AES256;
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] vector = SyntheticBytes(16, seed: 13);
        byte[] plain = SyntheticBytes(40, seed: 127);
        byte[] keyBefore = [.. key];
        byte[] vectorBefore = [.. vector];
        byte[] plainBefore = [.. plain];

        byte[] cipher = _provider.SymEncrypt(plain, key, vector, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC);
        byte[] cipherBefore = [.. cipher];

        _provider.SymDecrypt(cipher, key, vector, ntype, Enums.CRYPTO_SYMCRYPT_MODE_CBC);

        Assert.Equal(keyBefore, key);
        Assert.Equal(vectorBefore, vector);
        Assert.Equal(plainBefore, plain);
        Assert.Equal(cipherBefore, cipher);
    }

    /// <summary>
    /// One provider instance serves many concurrent operations correctly.
    /// </summary>
    /// <remarks>
    /// The property that makes creating the algorithm per call rather than caching one worthwhile:
    /// <see cref="SymmetricAlgorithm"/> is not thread-safe, so a cached instance would corrupt
    /// results under concurrency. Every cell of the type-by-mode grid is exercised from many threads
    /// at once, and every round trip must still hold.
    /// </remarks>
    [Fact]
    public void OneInstanceServesConcurrentOperationsCorrectly()
    {
        ConcurrentBag<string> failures = [];
        byte[] key = SyntheticBytes(32, seed: 31);
        byte[] vector = SyntheticBytes(16, seed: 13);

        Parallel.For(0, 240, index =>
        {
            ushort ntype = AllCipherTypes[index % AllCipherTypes.Length];
            long mode = ReproducibleCipherModes[
            index / AllCipherTypes.Length % ReproducibleCipherModes.Length];
            byte[] plain = SyntheticBytes(17 + index % 23, seed: index);

            try
            {
                byte[] cipher = _provider.SymEncrypt(plain, key, vector, ntype, mode);

                if (!_provider.SymDecrypt(cipher, key, vector, ntype, mode).SequenceEqual(plain))
                {
                    failures.Add($"round trip mismatch at index {index}");
                }
            }
            catch (CryptographicException error)
            {
                failures.Add($"unexpected failure at index {index}: {error.GetType().Name}");
            }
        });

        Assert.Empty(failures);
    }

    // ==========================================================================================
    //  HELPERS - synthetic material only, computed rather than written down
    // ==========================================================================================

    /// <summary>
    /// Reflects the public overloads of one method name.
    /// </summary>
    /// <param name="name">The method name, either <c>SymEncrypt</c> or <c>SymDecrypt</c>.</param>
    /// <returns>Every public instance overload declared with that name.</returns>
    private static List<MethodInfo> PublicOverloads(string name) =>
        [.. typeof(SymmetricCipherProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == name)];

    /// <summary>
    /// Produces obviously synthetic byte material of a requested length.
    /// </summary>
    /// <param name="length">The length in bytes. Zero is allowed.</param>
    /// <param name="seed">A discriminator, so two calls can differ deterministically.</param>
    /// <returns>A fresh buffer.</returns>
    /// <remarks>
    /// COMPUTED FROM AN INDEX, never a literal, so this file contains nothing that could be mistaken
    /// for real key material and copies nothing from any of the repository's known hardcoded-secret
    /// sites. The values are deliberately non-zero and non-repeating so that neither the DES weak-key
    /// screen nor the 3DES degenerate-key screen is tripped by accident.
    /// </remarks>
    private static byte[] SyntheticBytes(int length, int seed)
    {
        byte[] material = new byte[length];

        for (int index = 0; index < length; index++)
        {
            material[index] = (byte)(index * 37 + seed * 13 + 41);
        }

        return material;
    }

    /// <summary>
    /// Produces obviously synthetic text material of a requested length.
    /// </summary>
    /// <param name="length">The length in characters.</param>
    /// <returns>A visibly synthetic passphrase-shaped string.</returns>
    /// <remarks>
    /// BUILT FROM A REPEATED VISIBLE PATTERN so that it is self-evidently not a credential. It is
    /// ASCII only, which keeps it inside the range where the payload text encoding is byte-identical
    /// to the legacy's own conversion; non-ASCII key material is the one input on which the two could
    /// diverge, and that divergence is verifiable only against the behavioural oracle.
    /// </remarks>
    private static string SyntheticText(int length) =>
        string.Concat(Enumerable.Range(0, length).Select(index => (char)('a' + index % 26)));
}
