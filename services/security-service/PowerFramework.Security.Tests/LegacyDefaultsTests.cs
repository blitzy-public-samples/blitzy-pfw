// ==============================================================================================
//  LegacyDefaultsTests - parity and preservation tests for the cryptographic default catalogue
//  --------------------------------------------------------------------------------------------
//  COVERS         services/security-service/PowerFramework.Security/Crypto/LegacyDefaults.cs
//  ORACLE         ws_objects/pfw.shared.pbl.src/enums.sru:L921-L969 (the 30 CRYPTO_* constants)
//                 ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73 (the 65 declarations)
//
//  WHAT THESE TESTS ARE FOR, WHICH IS NOT WHAT A TEST IS USUALLY FOR
//  --------------------------------------------------------------------------------------------
//  This is a characterization suite, so several assertions below deliberately PIN WEAK BEHAVIOUR
//  IN PLACE. A test asserting that the default symmetric cipher mode is ECB, or that no
//  key-derivation function is available, is not endorsing either fact - it is making sure that
//  neither can be "improved" silently, because either improvement would stop this service from
//  decrypting values the legacy framework already wrote.
//
//  The desynchronization guards are the most important tests here. Every default arm in the
//  catalogue takes its value from a constant in the shared kernel, and the tests in the first
//  region assert that equality directly. If someone edits the ported constant catalogue, these
//  fail immediately and name the arm that drifted, rather than the drift surfacing later as
//  ciphertext that cannot be read back.
//
//  No test in this file contains key material, a passphrase or a credential. Where a test needs
//  key-shaped input it uses obviously synthetic, non-secret bytes or ASCII letters, and the
//  cipher key and block lengths asserted below are published algorithm facts rather than secrets.
// ==============================================================================================

using PowerFramework.Security.Crypto;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Verifies that the preserved legacy cryptographic defaults, the legacy-exact identifier sets,
/// the cipher metrics table and the shared substitution rules all behave as the behavioural
/// oracle requires.
/// </summary>
public sealed class LegacyDefaultsTests
{
    // ==========================================================================================
    //  DESYNCHRONIZATION GUARDS
    //  Each default arm must equal the shared-kernel constant it is valued from, so that an edit
    //  to the ported constant catalogue cannot silently change this service's behaviour.
    // ==========================================================================================

    [Fact]
    public void SymmetricModeDefault_EqualsItsSourceConstant_AndResolvesToEcb()
    {
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);

        // enums.sru:L946 - the default resolves to ECB. Preserved deliberately; must not become CBC.
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, LegacyDefaults.SYMMETRIC_MODE_DEFAULT);
    }

    [Fact]
    public void RsaPaddingDefault_EqualsItsSourceConstant_AndResolvesToPkcs1()
    {
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_DEFAULT, LegacyDefaults.RSA_PADDING_DEFAULT);

        // enums.sru:L951 - the default resolves to PKCS#1 v1.5, not OAEP.
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_PKCS1, LegacyDefaults.RSA_PADDING_DEFAULT);
        Assert.NotEqual(Enums.CRYPTO_RSA_PADDING_OAEP, LegacyDefaults.RSA_PADDING_DEFAULT);
    }

    [Fact]
    public void RandomStringFlagsDefault_EqualsItsSourceConstant_AndExcludesSymbols()
    {
        Assert.Equal(Enums.CRYPTO_RNDSTRING_DEFAULT, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);

        // randomstring.srf:L11 composes NUMBER + ALPHABET, so SYMBOL is deliberately excluded.
        Assert.Equal(
            Enums.CRYPTO_RNDSTRING_NUMBER + Enums.CRYPTO_RNDSTRING_ALPHABET,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);
        Assert.Equal(
            0u,
            LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT & Enums.CRYPTO_RNDSTRING_SYMBOL);
    }

    [Fact]
    public void GuidFlagsDefault_EqualsItsSourceConstant_AndIsBracketedAndSeparated()
    {
        Assert.Equal(Enums.CRYPTO_GUID_DEFAULT, LegacyDefaults.GUID_FLAGS_DEFAULT);

        // guid.srf:L11 composes BRACKET + SEPARATOR, so both bits are set.
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET,
            LegacyDefaults.GUID_FLAGS_DEFAULT & Enums.CRYPTO_GUID_INCLUDE_BRACKET);
        Assert.Equal(
            Enums.CRYPTO_GUID_INCLUDE_SEPARATOR,
            LegacyDefaults.GUID_FLAGS_DEFAULT & Enums.CRYPTO_GUID_INCLUDE_SEPARATOR);
    }

    [Fact]
    public void StringPayloadEncoding_EqualsItsSourceConstant_AndIsBase64()
    {
        // DECISION D4 - enums.sru:L924. One uniform encoding for every string-shaped payload.
        Assert.Equal(Enums.CRYPTO_ENCODING_BASE64, LegacyDefaults.STRING_PAYLOAD_ENCODING);
        Assert.NotEqual(Enums.CRYPTO_ENCODING_HEX, LegacyDefaults.STRING_PAYLOAD_ENCODING);
    }

    [Fact]
    public void SmallestLegalRsaKeySize_EqualsItsSourceConstant_AndRemains1024()
    {
        // enums.sru:L965, exercised by u_cst_tabpage_utility_crypto.sru:L699.
        Assert.Equal(Enums.CRYPTO_RSA_BITS_1024, LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);
        Assert.Equal(1024, LegacyDefaults.RSA_SMALLEST_LEGAL_KEY_SIZE_BITS);
    }

    // ==========================================================================================
    //  CAPABILITY FACTS - THE PRESERVED ABSENCES
    //  Every one of these must stay false. Flipping any of them would be a behavioural change.
    // ==========================================================================================

    [Fact]
    public void EveryWeakness_RemainsPreserved()
    {
        // No PBKDF2, scrypt, bcrypt or Argon2 and no salt anywhere in n_crypto.sru:L9-L73.
        Assert.False(LegacyDefaults.KEY_DERIVATION_AVAILABLE);

        // Mode set is ECB, CBC, CFB only [enums.sru:L943-L945]; no GCM, CCM or Poly1305.
        Assert.False(LegacyDefaults.AUTHENTICATED_ENCRYPTION_AVAILABLE);

        // No padding parameter in any of the 32 symmetric overloads; PKCS5 only [logfile.md:L1377].
        Assert.False(LegacyDefaults.BLOCK_PADDING_SELECTABLE);

        // The legacy declares no no-padding constant and rejects the request. A preserved refusal.
        Assert.False(LegacyDefaults.RSA_NO_PADDING_SUPPORTED);

        // GenRSAKey takes any unsigned integer; imposing a floor would reject legacy-valid input.
        Assert.False(LegacyDefaults.RSA_MINIMUM_KEY_SIZE_ENFORCED);
    }

    // ==========================================================================================
    //  SUPPORTED-VALUE PREDICATES - THE IDENTIFIER SETS, EXACTLY
    // ==========================================================================================

    [Theory]
    // The six published hash types, enums.sru:L928-L933. MD5 and SHA1 stay; CRC32 stays.
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    [InlineData(3L, true)]
    [InlineData(4L, true)]
    [InlineData(5L, true)]
    // Everything outside the set is rejected, in both directions and at the extremes.
    [InlineData(-1L, false)]
    [InlineData(6L, false)]
    [InlineData(7L, false)]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MaxValue, false)]
    public void IsSupportedHashType_AcceptsExactlyTheSixPublishedTypes(long ntype, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedHashType(ntype));

    [Fact]
    public void IsSupportedHashType_AcceptsEveryNamedHashConstant()
    {
        // The same set serves Hash, RSASign and VerifyRSASign [enums.sru:L927], so the RSA
        // provider's signature algorithm screens against this predicate too.
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_MD5));
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_SHA1));
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_SHA256));
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_SHA384));
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_SHA512));
        Assert.True(LegacyDefaults.IsSupportedHashType(Enums.CRYPTO_HASH_CRC32));
    }

    [Theory]
    // The five published cipher types, enums.sru:L936-L940. DES and 3DES stay.
    [InlineData((ushort)0, true)]
    [InlineData((ushort)1, true)]
    [InlineData((ushort)2, true)]
    [InlineData((ushort)3, true)]
    [InlineData((ushort)4, true)]
    // The parameter is unsigned, so only the upper bound can be exceeded.
    [InlineData((ushort)5, false)]
    [InlineData((ushort)6, false)]
    [InlineData(ushort.MaxValue, false)]
    public void IsSupportedSymmetricType_AcceptsExactlyTheFivePublishedTypes(
        ushort ntype,
        bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedSymmetricType(ntype));

    [Theory]
    // The three published modes, enums.sru:L943-L945.
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    // No authenticated mode is a member, and none may be added.
    [InlineData(-1L, false)]
    [InlineData(3L, false)]
    [InlineData(4L, false)]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MaxValue, false)]
    public void IsSupportedSymmetricMode_AcceptsExactlyEcbCbcAndCfb(long mode, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedSymmetricMode(mode));

    [Theory]
    // The two published padding schemes, enums.sru:L949-L950.
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    // No-padding is explicitly unsupported: the legacy declares no constant for it and rejects it.
    [InlineData(-1L, false)]
    [InlineData(2L, false)]
    [InlineData(3L, false)]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MaxValue, false)]
    public void IsSupportedRsaPadding_AcceptsExactlyPkcs1AndOaep(long padding, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedRsaPadding(padding));

    [Fact]
    public void IsSupportedRsaPadding_RejectsEverythingOutsideTheTwoNamedConstants()
    {
        Assert.True(LegacyDefaults.IsSupportedRsaPadding(Enums.CRYPTO_RSA_PADDING_PKCS1));
        Assert.True(LegacyDefaults.IsSupportedRsaPadding(Enums.CRYPTO_RSA_PADDING_OAEP));

        // The refusal is preserved legacy behaviour, and this is the member that records why.
        Assert.False(LegacyDefaults.RSA_NO_PADDING_SUPPORTED);
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, true)]
    [InlineData(-1L, false)]
    [InlineData(2L, false)]
    public void IsSupportedEncoding_AcceptsExactlyBase64AndHex(long encoding, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.IsSupportedEncoding(encoding));

    [Theory]
    // ECB consumes no initialization vector at all; CBC and CFB both require one.
    [InlineData(0L, false)]
    [InlineData(1L, true)]
    [InlineData(2L, true)]
    // An unpublished mode answers false rather than throwing.
    [InlineData(-1L, false)]
    [InlineData(3L, false)]
    public void ModeUsesInitializationVector_IsTrueOnlyForCbcAndCfb(long mode, bool expected) =>
        Assert.Equal(expected, LegacyDefaults.ModeUsesInitializationVector(mode));

    [Fact]
    public void ModeUsesInitializationVector_IsFalseForTheDefaultMode()
    {
        // Because the default is ECB, the mode-omitting overloads never need a synthesised IV.
        Assert.False(
            LegacyDefaults.ModeUsesInitializationVector(LegacyDefaults.SYMMETRIC_MODE_DEFAULT));
    }

    // ==========================================================================================
    //  THE CIPHER METRICS TABLE
    //  Key and block lengths are published algorithm facts, not secrets.
    // ==========================================================================================

    [Theory]
    [InlineData((ushort)0, 8, 8)]    // DES    - 8-byte key,  8-byte block
    [InlineData((ushort)1, 24, 8)]   // 3DES   - 24-byte key, 8-byte block
    [InlineData((ushort)2, 16, 16)]  // AES128 - 16-byte key, 16-byte block
    [InlineData((ushort)3, 24, 16)]  // AES192 - 24-byte key, 16-byte block
    [InlineData((ushort)4, 32, 16)]  // AES256 - 32-byte key, 16-byte block
    public void TryGetSymmetricCipherMetrics_ReturnsThePublishedSizes(
        ushort ntype,
        int expectedKeyLength,
        int expectedBlockLength)
    {
        Assert.True(
            LegacyDefaults.TryGetSymmetricCipherMetrics(ntype, out SymmetricCipherMetrics metrics));

        Assert.Equal(ntype, metrics.CipherType);
        Assert.Equal(expectedKeyLength, metrics.KeyLengthBytes);
        Assert.Equal(expectedBlockLength, metrics.BlockLengthBytes);

        // An initialization vector is exactly one block wide.
        Assert.Equal(expectedBlockLength, metrics.IvLengthBytes);
    }

    [Theory]
    [InlineData((ushort)5)]
    [InlineData((ushort)6)]
    [InlineData(ushort.MaxValue)]
    public void TryGetSymmetricCipherMetrics_ReportsFailureWithoutThrowing(ushort ntype)
    {
        // The non-throwing path a provider maps onto the legacy invalid-argument return code.
        Assert.False(
            LegacyDefaults.TryGetSymmetricCipherMetrics(ntype, out SymmetricCipherMetrics metrics));
        Assert.Equal(default, metrics);
    }

    [Fact]
    public void GetSymmetricCipherMetrics_ThrowsForAnUnpublishedType()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => { LegacyDefaults.GetSymmetricCipherMetrics(ushort.MaxValue); });

        Assert.Equal("ntype", error.ParamName);
    }

    [Fact]
    public void GetSymmetricCipherMetrics_AgreesWithTheTryOverload()
    {
        foreach (SymmetricCipherMetrics expected in LegacyDefaults.AllSymmetricCipherMetrics)
        {
            Assert.Equal(
                expected,
                LegacyDefaults.GetSymmetricCipherMetrics((ushort)expected.CipherType));
        }
    }

    [Fact]
    public void AllSymmetricCipherMetrics_HoldsExactlyTheFivePublishedCiphersInIdentifierOrder()
    {
        IReadOnlyList<SymmetricCipherMetrics> table = LegacyDefaults.AllSymmetricCipherMetrics;

        Assert.Equal(5, table.Count);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_TYPE_DES, table[0].CipherType);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_TYPE_3DES, table[1].CipherType);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_TYPE_AES128, table[2].CipherType);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_TYPE_AES192, table[3].CipherType);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_TYPE_AES256, table[4].CipherType);

        // Every published cipher type has a row, and every row is a published cipher type.
        foreach (SymmetricCipherMetrics row in table)
        {
            Assert.True(LegacyDefaults.IsSupportedSymmetricType((ushort)row.CipherType));
            Assert.True(row.KeyLengthBytes > 0);
            Assert.True(row.BlockLengthBytes > 0);
        }
    }

    // ==========================================================================================
    //  DECISION D1 - NO KEY DERIVATION: TRUNCATE IF LONG, ZERO-PAD IF SHORT
    // ==========================================================================================

    [Fact]
    public void KeyMaterialEncoding_IsUtf8AndNeverEmitsAByteOrderMark()
    {
        const string material = "abc";

        byte[] encoded = LegacyDefaults.KeyMaterialEncoding.GetBytes(material);

        // Exactly one byte per ASCII character. A byte-order mark or any widening would change the
        // length, and a phantom leading byte would silently change every key derived from a string.
        Assert.Equal(material.Length, encoded.Length);

        // ASCII identity, asserted per byte rather than against a byte-array literal, so the
        // expectation is stated independently of the encoder under test.
        Assert.Equal((byte)'a', encoded[0]);
        Assert.Equal((byte)'b', encoded[1]);
        Assert.Equal((byte)'c', encoded[2]);
    }

    [Fact]
    public void NormalizeKeyMaterial_LeavesExactLengthMaterialUnchanged()
    {
        byte[] material = [1, 2, 3, 4, 5, 6, 7, 8];

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(material, 8);

        Assert.Equal(material, normalized);
    }

    [Fact]
    public void NormalizeKeyMaterial_TruncatesOverLongMaterialKeepingTheLeadingBytes()
    {
        byte[] material = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(material, 8);

        // Truncation silently discards entropy the caller believed it supplied. Preserved.
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, normalized);
    }

    [Fact]
    public void NormalizeKeyMaterial_ZeroPadsOverShortMaterialOnTheRight()
    {
        byte[] material = [1, 2, 3];

        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(material, 8);

        // Zero-padding silently manufactures key bytes the caller never chose. Preserved.
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0, 0, 0 }, normalized);
    }

    [Fact]
    public void NormalizeKeyMaterial_TurnsEmptyMaterialIntoAllZeroes()
    {
        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial(string.Empty, 16);

        Assert.Equal(16, normalized.Length);
        Assert.All(normalized, actual => Assert.Equal(0, actual));
    }

    [Fact]
    public void NormalizeKeyMaterial_AppliesTheSameRuleToStringAndBlobMaterial()
    {
        // The two overloads share one implementation precisely so a string key and the equivalent
        // blob key can never be normalised differently.
        const string material = "ABCDEFGHIJ";

        byte[] fromString = LegacyDefaults.NormalizeKeyMaterial(material, 8);
        byte[] fromBlob = LegacyDefaults.NormalizeKeyMaterial(
            LegacyDefaults.KeyMaterialEncoding.GetBytes(material),
            8);

        Assert.Equal(fromBlob, fromString);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void NormalizeKeyMaterial_AlwaysReturnsExactlyTheRequestedLength(int requiredLength)
    {
        // Every key length in the metrics table, from material that is far too short.
        byte[] normalized = LegacyDefaults.NormalizeKeyMaterial("k", requiredLength);

        Assert.Equal(requiredLength, normalized.Length);
    }

    [Fact]
    public void NormalizeKeyMaterial_RejectsNullMaterial()
    {
        // Declared as a typed local rather than passed as a bare `null`, because a null literal is
        // ambiguous across the string and span overloads under this language version's implicit
        // span conversions. The type is what selects the overload; see the remark on the string
        // overload in LegacyDefaults.
        string? material = null;

        Assert.Throws<ArgumentNullException>(
            () => { LegacyDefaults.NormalizeKeyMaterial(material!, 16); });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NormalizeKeyMaterial_RejectsANonPositiveLength(int requiredLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { LegacyDefaults.NormalizeKeyMaterial("k", requiredLength); });

        Assert.Throws<ArgumentOutOfRangeException>(
            () => { LegacyDefaults.NormalizeKeyMaterial(new byte[] { 1 }, requiredLength); });
    }

    [Fact]
    public void NormalizeKeyMaterial_ReturnsAFreshBufferEachTime()
    {
        byte[] first = LegacyDefaults.NormalizeKeyMaterial("k", 8);
        byte[] second = LegacyDefaults.NormalizeKeyMaterial("k", 8);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    // ==========================================================================================
    //  DECISION D3 - THE ALL-ZERO INITIALIZATION VECTOR, COMPUTED AND NEVER WRITTEN DOWN
    // ==========================================================================================

    [Theory]
    [InlineData(8)]   // the DES and 3DES block length
    [InlineData(16)]  // the AES block length
    public void CreateZeroInitializationVector_IsAllZeroesOfTheBlockLength(int blockLength)
    {
        byte[] iv = LegacyDefaults.CreateZeroInitializationVector(blockLength);

        Assert.Equal(blockLength, iv.Length);
        Assert.All(iv, actual => Assert.Equal(0, actual));
    }

    [Fact]
    public void CreateZeroInitializationVector_MatchesEveryBlockLengthInTheMetricsTable()
    {
        foreach (SymmetricCipherMetrics metrics in LegacyDefaults.AllSymmetricCipherMetrics)
        {
            byte[] iv = LegacyDefaults.CreateZeroInitializationVector(metrics.IvLengthBytes);

            Assert.Equal(metrics.BlockLengthBytes, iv.Length);
        }
    }

    [Fact]
    public void CreateZeroInitializationVector_ReturnsAFreshBufferEachTime()
    {
        byte[] first = LegacyDefaults.CreateZeroInitializationVector(16);
        byte[] second = LegacyDefaults.CreateZeroInitializationVector(16);

        // A caller that overwrites one must not affect any other operation.
        Assert.NotSame(first, second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateZeroInitializationVector_RejectsANonPositiveLength(int blockLength) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => { LegacyDefaults.CreateZeroInitializationVector(blockLength); });
}
