// ==================================================================================================
//  SecurityClientCryptoTests.cs
//  Conformance tests for the contract C-02 half of Clients/SecurityClient.cs, plus the value types
//  that half publishes.
//  ------------------------------------------------------------------------------------------------
//  THE BAR IS SCHEMA CONFORMANCE, NOT CALL-SITE PARITY, AND THAT IS DELIBERATE. The legacy n_crypto is
//  not used anywhere in the three libraries this service and its downstream are ported from, so there
//  is no legacy call site here to be in parity with and fabricating one would be a new feature. What
//  these tests therefore pin is exactly what a consumer depends on: the published path, the member
//  spellings, WHICH MEMBERS ARE PRESENT AND WHICH ARE ABSENT, the enum encoding, and the status
//  handling.
//
//  MEMBER ABSENCE IS TESTED AS CAREFULLY AS MEMBER PRESENCE. An optional member being omitted reaches a
//  different legacy overload from the same member carrying its default, so several tests below assert
//  that a request does NOT contain a member. Those are the tests that keep the preserved legacy
//  weaknesses preserved rather than quietly strengthened.
//
//  NO SECRET, CREDENTIAL, KEY, CERTIFICATE OR REFERENCE VALUE IN THIS FILE IS REAL, and none is copied
//  from anywhere in the repository.
// ==================================================================================================

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Clients;
using PowerFramework.DataServices.Configuration;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class SecurityClientCryptoTests
{
    private const string FakeKeyRef = "keyref-for-tests";
    private const string FakeIvRef = "ivref-for-tests";
    private const string FakeFileRef = "fileref-for-tests";

    private static readonly byte[] SamplePayload = [1, 2, 3, 4];

    private static (SecurityClient Client, RecordingHandler Handler) CreateClient(
        RecordingHandler handler)
    {
        HttpClient httpClient = new(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://security.invalid/", UriKind.Absolute),
        };

        DataServicesOptions options = new();
        options.Security.BaseAddress = "https://security.invalid/";

        SecurityClient client = new(
            httpClient,
            Options.Create(options),
            NullLogger<SecurityClient>.Instance,
            new MutableClock());

        return (client, handler);
    }

    private static JsonElement Body(RecordingHandler handler, int index = 0) =>
        JsonDocument.Parse(handler.Bodies[index]).RootElement;

    private static string Path(RecordingHandler handler, int index = 0) =>
        handler.Requests[index].RequestUri?.AbsolutePath ?? string.Empty;

    // ==============================================================================================
    //  GROUP 1 - encoding and blob conversion  [n_crypto.sru:L11-L13]
    // ==============================================================================================

    [Fact]
    public async Task StringToBlobAsync_SendsTheLegacyEncodingArgumentAndDecodesTheTransportBase64()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"AQIDBA==\"}");
        (SecurityClient client, _) = CreateClient(handler);

        byte[] decoded = await client.StringToBlobAsync(
            "AQIDBA==",
            Enums.CRYPTO_ENCODING_BASE64,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/encoding/string-to-blob", Path(handler));
        JsonElement body = Body(handler);
        Assert.Equal("AQIDBA==", body.GetProperty("data").GetString());
        Assert.Equal(Enums.CRYPTO_ENCODING_BASE64, body.GetProperty("encoding").GetInt64());
        Assert.Equal(SamplePayload, decoded);
    }

    [Fact]
    public async Task StringToBlobAsync_CarriesTheHexEncodingAndStillReadsABase64Result()
    {
        // The two encodings answer different questions: the ARGUMENT says how to interpret the input,
        // and the base64 in the response is the JSON transport encoding. Conflating them would be a
        // behavioural change.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"AQIDBA==\"}");
        (SecurityClient client, _) = CreateClient(handler);

        byte[] decoded = await client.StringToBlobAsync(
            "01020304",
            Enums.CRYPTO_ENCODING_HEX,
            TestContext.Current.CancellationToken);

        Assert.Equal(Enums.CRYPTO_ENCODING_HEX, Body(handler).GetProperty("encoding").GetInt64());
        Assert.Equal(SamplePayload, decoded);
    }

    [Fact]
    public async Task BlobToStringAsync_CarriesTheBytesAsTransportBase64()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"01020304\"}");
        (SecurityClient client, _) = CreateClient(handler);

        string encoded = await client.BlobToStringAsync(
            SamplePayload,
            Enums.CRYPTO_ENCODING_HEX,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/encoding/blob-to-string", Path(handler));
        Assert.Equal("AQIDBA==", Body(handler).GetProperty("data").GetString());
        Assert.Equal("01020304", encoded);
    }

    [Fact]
    public async Task ReverseBlobAsync_CarriesBothHalvesOfTheLegacyOutcome()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"BAMCAQ==\",\"succeeded\":true}");
        (SecurityClient client, _) = CreateClient(handler);

        ReversedBlob result = await client.ReverseBlobAsync(
            SamplePayload,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/encoding/blob-reverse", Path(handler));
        Assert.Equal<byte[]>([4, 3, 2, 1], result.Data.ToArray());
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ReverseBlobAsync_PreservesAFalseLegacyReturnValueInsideASuccessfulResponse()
    {
        // The legacy boolean is carried unchanged. It is NOT promoted into a status, because a caller may
        // be branching on it.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"\",\"succeeded\":false}");
        (SecurityClient client, _) = CreateClient(handler);

        ReversedBlob result = await client.ReverseBlobAsync(
            SamplePayload,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(result.Data.IsEmpty);
    }

    [Fact]
    public async Task ConversionOperations_RefuseAnUndeclaredEncoding()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.StringToBlobAsync("x", 2, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.BlobToStringAsync(SamplePayload, -1, TestContext.Current.CancellationToken));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DecodeOfAnOffContractByteMemberIsATypedFailure()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"not base64 at all!!\"}");
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.StringToBlobAsync(
                "x",
                Enums.CRYPTO_ENCODING_BASE64,
                TestContext.Current.CancellationToken));

        Assert.Equal("stringToBlob", failure.OperationId);
    }

    // ==============================================================================================
    //  GROUP 2 - random generation  [n_crypto.sru:L14-L18]
    // ==============================================================================================

    [Fact]
    public async Task GenerateRandomBlobAsync_SendsTheSizeAndDecodesTheResult()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"AQIDBA==\"}");
        (SecurityClient client, _) = CreateClient(handler);

        byte[] generated = await client.GenerateRandomBlobAsync(
            4,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/random/blob", Path(handler));
        Assert.Equal(4u, Body(handler).GetProperty("size").GetUInt32());
        Assert.Equal(SamplePayload, generated);
    }

    [Fact]
    public async Task GenerateRandomBlobAsync_AcceptsTheContractMaximumAndRefusesAnythingAbove()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"data\":\"\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.GenerateRandomBlobAsync(
            SecurityClient.MaximumRandomSize,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GenerateRandomBlobAsync(
                SecurityClient.MaximumRandomSize + 1,
                TestContext.Current.CancellationToken));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GenerateRandomStringAsync_OmitsTheFlagSetWhenItIsNotSupplied()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"abc\"}");
        (SecurityClient client, _) = CreateClient(handler);

        string value = await client.GenerateRandomStringAsync(
            8,
            flags: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/random/string", Path(handler));

        // Omitted, not defaulted: an absent flag set reaches the single-argument legacy overload.
        Assert.False(Body(handler).TryGetProperty("flags", out _));
        Assert.Equal("abc", value);
    }

    [Fact]
    public async Task GenerateRandomStringAsync_CarriesASuppliedFlagSetVerbatim()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"abc\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.GenerateRandomStringAsync(
            8,
            Enums.CRYPTO_RNDSTRING_DEFAULT,
            TestContext.Current.CancellationToken);

        Assert.Equal(3u, Body(handler).GetProperty("flags").GetUInt32());
    }

    [Fact]
    public async Task GenerateRandomStringAsync_DoesNotFilterAnUnrecognisedFlagBit()
    {
        // The contract declares this member's domain as the whole unsigned range rather than an
        // enumeration, so rejecting an unrecognised bit would narrow a surface the legacy does not.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"abc\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.GenerateRandomStringAsync(8, 64u, TestContext.Current.CancellationToken);

        Assert.Equal(64u, Body(handler).GetProperty("flags").GetUInt32());
    }

    [Fact]
    public async Task GenerateGuidAsync_SendsAnEmptyObjectWhenNoFlagsAreSupplied()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"{00000000-0000-0000-0000-000000000000}\"}");
        (SecurityClient client, _) = CreateClient(handler);

        string value = await client.GenerateGuidAsync(
            flags: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/random/guid", Path(handler));

        // An empty object reaches the no-argument legacy overload and yields the default form.
        Assert.Empty(Body(handler).EnumerateObject());
        Assert.Equal("{00000000-0000-0000-0000-000000000000}", value);
    }

    [Fact]
    public async Task GenerateGuidAsync_CarriesTheLegacyDefaultFlagSumWhenAskedFor()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"value\":\"x\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.GenerateGuidAsync(
            Enums.CRYPTO_GUID_DEFAULT,
            TestContext.Current.CancellationToken);

        Assert.Equal(3u, Body(handler).GetProperty("flags").GetUInt32());
    }

    // ==============================================================================================
    //  GROUP 3 - RSA key generation  [n_crypto.sru:L19-L20]
    // ==============================================================================================

    [Fact]
    public async Task GenerateRsaKeyAsync_ReturnsThePublicKeyAndAReferenceButNeverAPrivateKey()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"publicKey\":\"public-key-material-placeholder\",\"keyRef\":\"" + FakeKeyRef
            + "\",\"bits\":2048,\"pemFormat\":true}");
        (SecurityClient client, _) = CreateClient(handler);

        GeneratedRsaKey generated = await client.GenerateRsaKeyAsync(
            Enums.CRYPTO_RSA_BITS_2048,
            pemFormat: true,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/rsa/keys", Path(handler));
        Assert.Equal("public-key-material-placeholder", generated.PublicKey);
        Assert.Equal(FakeKeyRef, generated.KeyRef);
        Assert.Equal(2048, generated.Bits);
        Assert.True(generated.PemFormat);

        // The narrowing is structural: there is no member of any name through which a private key could
        // be carried.
        Assert.DoesNotContain(
            typeof(GeneratedRsaKey).GetProperties().Select(property => property.Name),
            name => name.Contains("Private", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateRsaKeyAsync_OmitsTheFormatSwitchWhenItIsNotSupplied()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"publicKey\":\"p\",\"keyRef\":\"" + FakeKeyRef + "\",\"bits\":2048}");
        (SecurityClient client, _) = CreateClient(handler);

        GeneratedRsaKey generated = await client.GenerateRsaKeyAsync(
            Enums.CRYPTO_RSA_BITS_2048,
            pemFormat: null,
            TestContext.Current.CancellationToken);

        // Omitted, so the three-argument legacy overload is reached.
        Assert.False(Body(handler).TryGetProperty("pemFormat", out _));
        Assert.Null(generated.PemFormat);
    }

    [Fact]
    public async Task GenerateRsaKeyAsync_StillAcceptsTheLegacyMinimumKeySize()
    {
        // PRESERVED LEGACY WEAKNESS 7. 1024 is not removed from the accepted set and no minimum is
        // enforced, because the legacy enforces none.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"publicKey\":\"p\",\"keyRef\":\"" + FakeKeyRef + "\",\"bits\":1024}");
        (SecurityClient client, _) = CreateClient(handler);

        GeneratedRsaKey generated = await client.GenerateRsaKeyAsync(
            Enums.CRYPTO_RSA_BITS_1024,
            pemFormat: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1024, Body(handler).GetProperty("bits").GetInt32());
        Assert.Equal(1024, generated.Bits);
    }

    [Theory]
    [InlineData("{\"publicKey\":\"p\",\"keyRef\":\"\",\"bits\":2048}")]
    [InlineData("{\"publicKey\":\"p\",\"keyRef\":\"k\",\"bits\":-1}")]
    [InlineData("{\"publicKey\":\"p\",\"keyRef\":\"k\",\"bits\":70000}")]
    public async Task GenerateRsaKeyAsync_RefusesAnOffContractResponse(string json)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, json);
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GenerateRsaKeyAsync(2048, null, TestContext.Current.CancellationToken));

        Assert.Equal("generateRsaKey", failure.OperationId);
    }

    // ==============================================================================================
    //  GROUPS 4 AND 5 - the unkeyed and keyed digest families  [n_crypto.sru:L21-L29]
    // ==============================================================================================

    [Fact]
    public async Task HashAsync_ReturnsAStringDigestEvenForABlobPayload()
    {
        // PRESERVED LEGACY BEHAVIOUR. All six legacy hash overloads return a string, so the result form
        // does NOT follow the payload form here. The response schema carries no form selector at all.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"digest\":\"deadbeef\"}");
        (SecurityClient client, _) = CreateClient(handler);

        string digest = await client.HashAsync(
            CryptoPayload.FromBlob(SamplePayload),
            Enums.CRYPTO_HASH_SHA256,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/hash", Path(handler));
        JsonElement body = Body(handler);
        Assert.Equal("BLOB", body.GetProperty("payloadForm").GetString());
        Assert.Equal("AQIDBA==", body.GetProperty("data").GetString());
        Assert.Equal(Enums.CRYPTO_HASH_SHA256, body.GetProperty("hashType").GetInt64());
        Assert.Equal("deadbeef", digest);
    }

    [Fact]
    public async Task HashAsync_CarriesAStringPayloadVerbatim()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"digest\":\"d\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.HashAsync(
            CryptoPayload.FromString("plain text"),
            Enums.CRYPTO_HASH_MD5,
            TestContext.Current.CancellationToken);

        JsonElement body = Body(handler);
        Assert.Equal("STRING", body.GetProperty("payloadForm").GetString());
        Assert.Equal("plain text", body.GetProperty("data").GetString());
    }

    [Theory]
    [InlineData(Enums.CRYPTO_HASH_MD5)]
    [InlineData(Enums.CRYPTO_HASH_SHA1)]
    [InlineData(Enums.CRYPTO_HASH_SHA256)]
    [InlineData(Enums.CRYPTO_HASH_SHA384)]
    [InlineData(Enums.CRYPTO_HASH_SHA512)]
    [InlineData(Enums.CRYPTO_HASH_CRC32)]
    public async Task HashAsync_AcceptsAllSixDeclaredSelectorsIncludingTheWeakOnes(long hashType)
    {
        // PRESERVED LEGACY WEAKNESS 8. MD5 is accepted, and so is CRC32 - which is a checksum rather
        // than a cryptographic hash. Neither is filtered out.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"digest\":\"d\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.HashAsync(
            CryptoPayload.FromString("x"),
            hashType,
            TestContext.Current.CancellationToken);

        Assert.Equal(hashType, Body(handler).GetProperty("hashType").GetInt64());
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(6L)]
    public async Task HashOperations_RefuseAnUndeclaredSelector(long hashType)
    {
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);
        CryptoPayload payload = CryptoPayload.FromString("x");
        CancellationToken token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.HashAsync(payload, hashType, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.HmacAsync(payload, FakeKeyRef, hashType, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.HashFileAsync(FakeFileRef, hashType, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.HmacFileAsync(FakeFileRef, FakeKeyRef, hashType, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.RsaSignAsync(payload, FakeKeyRef, hashType, token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HmacAsync_CarriesAnOpaqueReferenceAndNoKeyMaterial()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"digest\":\"d\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.HmacAsync(
            CryptoPayload.FromString("x"),
            FakeKeyRef,
            Enums.CRYPTO_HASH_SHA256,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/hmac", Path(handler));
        JsonElement body = Body(handler);
        Assert.Equal(FakeKeyRef, body.GetProperty("keyRef").GetString());
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.False(body.TryGetProperty("key", out _));
    }

    [Fact]
    public async Task FileDigestOperations_CarryAReferenceAndNeverAPath()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.OK, "{\"digest\":\"d\"}")
            .Enqueue(HttpStatusCode.OK, "{\"digest\":\"d\"}");
        (SecurityClient client, _) = CreateClient(handler);
        CancellationToken token = TestContext.Current.CancellationToken;

        await client.HashFileAsync(FakeFileRef, Enums.CRYPTO_HASH_SHA256, token);
        await client.HmacFileAsync(FakeFileRef, FakeKeyRef, Enums.CRYPTO_HASH_SHA256, token);

        Assert.Equal("/v1/crypto/hash-file", Path(handler));
        Assert.Equal("/v1/crypto/hmac-file", Path(handler, 1));

        foreach (int index in (int[])[0, 1])
        {
            JsonElement body = Body(handler, index);
            Assert.Equal(FakeFileRef, body.GetProperty("fileRef").GetString());
            Assert.False(body.TryGetProperty("path", out _));
            Assert.False(body.TryGetProperty("filename", out _));
        }
    }

    [Fact]
    public async Task KeyedOperations_RefuseAnEmptyReference()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);
        CryptoPayload payload = CryptoPayload.FromString("x");
        CancellationToken token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.HmacAsync(payload, "   ", Enums.CRYPTO_HASH_SHA256, token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.HashFileAsync(string.Empty, Enums.CRYPTO_HASH_SHA256, token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SymmetricEncryptAsync(
                payload,
                string.Empty,
                Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                null,
                null,
                token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.SymmetricEncryptAsync(
                payload,
                FakeKeyRef,
                Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                "  ",
                null,
                token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.RsaEncryptAsync(payload, string.Empty, null, token));

        Assert.Empty(handler.Requests);
    }

    // ==============================================================================================
    //  GROUP 6 - the symmetric pair  [n_crypto.sru:L30-L61]
    // ==============================================================================================

    [Fact]
    public async Task SymmetricEncryptAsync_OmitsTheVectorAndTheModeWhenNeitherIsSupplied()
    {
        // PRESERVED LEGACY WEAKNESSES 1 AND 4. Omitting the mode is what makes the service apply ECB, and
        // there is no padding member on this request at all because not one of the thirty-two symmetric
        // overloads has one. Both are honoured by NOT interfering.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"cipher\"}");
        (SecurityClient client, _) = CreateClient(handler);

        CryptoPayload result = await client.SymmetricEncryptAsync(
            CryptoPayload.FromString("plain"),
            FakeKeyRef,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
            ivRef: null,
            mode: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/symmetric/encrypt", Path(handler));
        JsonElement body = Body(handler);
        Assert.False(body.TryGetProperty("ivRef", out _));
        Assert.False(body.TryGetProperty("mode", out _));
        Assert.False(body.TryGetProperty("padding", out _));
        Assert.Equal(4, body.EnumerateObject().Count());
        Assert.Equal(PayloadForm.STRING, result.Form);
        Assert.Equal("cipher", result.AsString());
    }

    [Fact]
    public async Task SymmetricEncryptAsync_DistinguishesAnExplicitEcbFromAnOmittedMode()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"BLOB\",\"data\":\"AQIDBA==\"}");
        (SecurityClient client, _) = CreateClient(handler);

        CryptoPayload result = await client.SymmetricEncryptAsync(
            CryptoPayload.FromBlob(SamplePayload),
            FakeKeyRef,
            Enums.CRYPTO_SYMCRYPT_TYPE_AES128,
            FakeIvRef,
            Enums.CRYPTO_SYMCRYPT_MODE_ECB,
            TestContext.Current.CancellationToken);

        JsonElement body = Body(handler);
        Assert.Equal(FakeIvRef, body.GetProperty("ivRef").GetString());
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, body.GetProperty("mode").GetInt64());
        Assert.Equal(PayloadForm.BLOB, result.Form);
        Assert.Equal(SamplePayload, result.AsBlob().ToArray());
    }

    [Fact]
    public async Task SymmetricDecryptAsync_UsesItsOwnPublishedPath()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"plain\"}");
        (SecurityClient client, _) = CreateClient(handler);

        CryptoPayload result = await client.SymmetricDecryptAsync(
            CryptoPayload.FromString("cipher"),
            FakeKeyRef,
            Enums.CRYPTO_SYMCRYPT_TYPE_3DES,
            ivRef: null,
            mode: Enums.CRYPTO_SYMCRYPT_MODE_CBC,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/symmetric/decrypt", Path(handler));
        Assert.Equal("plain", result.AsString());
    }

    [Theory]
    [InlineData(Enums.CRYPTO_SYMCRYPT_TYPE_DES)]
    [InlineData(Enums.CRYPTO_SYMCRYPT_TYPE_3DES)]
    [InlineData(Enums.CRYPTO_SYMCRYPT_TYPE_AES128)]
    [InlineData(Enums.CRYPTO_SYMCRYPT_TYPE_AES192)]
    [InlineData(Enums.CRYPTO_SYMCRYPT_TYPE_AES256)]
    public async Task SymmetricOperations_AcceptAllFiveDeclaredCiphers(long cipherType)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"c\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.SymmetricEncryptAsync(
            CryptoPayload.FromString("p"),
            FakeKeyRef,
            cipherType,
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(cipherType, Body(handler).GetProperty("cipherType").GetInt64());
    }

    [Fact]
    public async Task SymmetricOperations_RefuseAnUndeclaredCipherOrMode()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);
        CryptoPayload payload = CryptoPayload.FromString("p");
        CancellationToken token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SymmetricEncryptAsync(payload, FakeKeyRef, 5, null, null, token));

        // PRESERVED LEGACY WEAKNESS 6. The mode set is exactly ECB, CBC and CFB, so there is NO
        // authenticated mode to select and a value outside the three is refused rather than forwarded.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SymmetricDecryptAsync(
                payload,
                FakeKeyRef,
                Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                null,
                3,
                token));

        Assert.Empty(handler.Requests);
    }

    // ==============================================================================================
    //  GROUPS 7 AND 8 - the RSA cipher pair and the signature pair  [n_crypto.sru:L62-L73]
    // ==============================================================================================

    [Fact]
    public async Task RsaEncryptAsync_OmitsThePaddingWhenItIsNotSupplied()
    {
        // PRESERVED LEGACY WEAKNESS 2. Omitting the member is what selects PKCS#1 v1.5 on the service
        // side, so no default is substituted here.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"cipher\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.RsaEncryptAsync(
            CryptoPayload.FromString("plain"),
            FakeKeyRef,
            padding: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/rsa/encrypt", Path(handler));
        JsonElement body = Body(handler);
        Assert.False(body.TryGetProperty("padding", out _));
        Assert.Equal(3, body.EnumerateObject().Count());
    }

    [Theory]
    [InlineData(Enums.CRYPTO_RSA_PADDING_PKCS1)]
    [InlineData(Enums.CRYPTO_RSA_PADDING_OAEP)]
    public async Task RsaCipherOperations_AcceptBothDeclaredPaddings(long padding)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"c\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.RsaDecryptAsync(
            CryptoPayload.FromString("c"),
            FakeKeyRef,
            padding,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/rsa/decrypt", Path(handler));
        Assert.Equal(padding, Body(handler).GetProperty("padding").GetInt64());
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(3L)]
    [InlineData(-1L)]
    public async Task RsaCipherOperations_RefuseNoPaddingWithADefinedError(long padding)
    {
        // PRESERVED LEGACY WEAKNESS 3. The legacy declares exactly two padding constants and no
        // no-padding constant, so a request for it has no representable value and is REFUSED rather than
        // invented into the surface.
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);
        CryptoPayload payload = CryptoPayload.FromString("p");
        CancellationToken token = TestContext.Current.CancellationToken;

        ArgumentOutOfRangeException failure = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.RsaEncryptAsync(payload, FakeKeyRef, padding, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.RsaDecryptAsync(payload, FakeKeyRef, padding, token));

        Assert.Contains("No-padding is not selectable", failure.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RsaSignAsync_ReturnsTheSignatureInThePayloadsOwnFamily()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"BLOB\",\"data\":\"AQIDBA==\"}");
        (SecurityClient client, _) = CreateClient(handler);

        CryptoPayload signature = await client.RsaSignAsync(
            CryptoPayload.FromBlob(SamplePayload),
            FakeKeyRef,
            Enums.CRYPTO_HASH_SHA256,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/rsa/sign", Path(handler));
        Assert.Equal(PayloadForm.BLOB, signature.Form);
        Assert.Equal(SamplePayload, signature.AsBlob().ToArray());
    }

    [Fact]
    public async Task RsaSignAsync_AcceptsAChecksumAsASignatureHash()
    {
        // PRESERVED LEGACY WEAKNESS 8 in its sharpest form: the SAME six-member set governs the
        // signature hash, so CRC32 is a legal selector here.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"STRING\",\"data\":\"s\"}");
        (SecurityClient client, _) = CreateClient(handler);

        await client.RsaSignAsync(
            CryptoPayload.FromString("x"),
            FakeKeyRef,
            Enums.CRYPTO_HASH_CRC32,
            TestContext.Current.CancellationToken);

        Assert.Equal(Enums.CRYPTO_HASH_CRC32, Body(handler).GetProperty("hashType").GetInt64());
    }

    [Fact]
    public async Task RsaVerifyAsync_CarriesOneFormSelectorForBothValues()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"valid\":true}");
        (SecurityClient client, _) = CreateClient(handler);

        bool valid = await client.RsaVerifyAsync(
            CryptoPayload.FromBlob(SamplePayload),
            CryptoPayload.FromBlob(SamplePayload),
            FakeKeyRef,
            Enums.CRYPTO_HASH_SHA512,
            TestContext.Current.CancellationToken);

        Assert.Equal("/v1/crypto/rsa/verify", Path(handler));
        JsonElement body = Body(handler);
        Assert.Equal(5, body.EnumerateObject().Count());
        Assert.Equal("BLOB", body.GetProperty("payloadForm").GetString());
        Assert.Equal("AQIDBA==", body.GetProperty("signature").GetString());
        Assert.True(valid);
    }

    [Fact]
    public async Task RsaVerifyAsync_ReportsAFalseOutcomeAsASuccessfulAnswer()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"valid\":false}");
        (SecurityClient client, _) = CreateClient(handler);

        bool valid = await client.RsaVerifyAsync(
            CryptoPayload.FromString("x"),
            CryptoPayload.FromString("s"),
            FakeKeyRef,
            Enums.CRYPTO_HASH_SHA256,
            TestContext.Current.CancellationToken);

        Assert.False(valid);
    }

    [Fact]
    public async Task RsaVerifyAsync_RefusesAMismatchedPayloadAndSignatureFamily()
    {
        // The legacy declares no mixed overload, and the request carries ONE form selector, so a
        // mismatched pair is not expressible on this wire.
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);

        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(
            () => client.RsaVerifyAsync(
                CryptoPayload.FromString("x"),
                CryptoPayload.FromBlob(SamplePayload),
                FakeKeyRef,
                Enums.CRYPTO_HASH_SHA256,
                TestContext.Current.CancellationToken));

        Assert.Equal("signature", failure.ParamName);
        Assert.Empty(handler.Requests);
    }

    // ==============================================================================================
    //  Null-argument discipline across the whole surface
    // ==============================================================================================

    [Fact]
    public async Task PayloadTakingOperations_RejectANullPayload()
    {
        RecordingHandler handler = new();
        (SecurityClient client, _) = CreateClient(handler);
        CancellationToken token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.HashAsync(null!, Enums.CRYPTO_HASH_SHA256, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.HmacAsync(null!, FakeKeyRef, Enums.CRYPTO_HASH_SHA256, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.SymmetricEncryptAsync(
                null!,
                FakeKeyRef,
                Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                null,
                null,
                token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.RsaEncryptAsync(null!, FakeKeyRef, null, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.RsaSignAsync(null!, FakeKeyRef, Enums.CRYPTO_HASH_SHA256, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.RsaVerifyAsync(
                CryptoPayload.FromString("x"),
                null!,
                FakeKeyRef,
                Enums.CRYPTO_HASH_SHA256,
                token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.StringToBlobAsync(null!, Enums.CRYPTO_ENCODING_BASE64, token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.GetTokenAsync(null!, token).AsTask());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Constructor_RejectsEveryNullDependency()
    {
        HttpClient httpClient = new(new RecordingHandler());
        IOptions<DataServicesOptions> options = Options.Create(new DataServicesOptions());

        Assert.Throws<ArgumentNullException>(() => new SecurityClient(
            null!,
            options,
            NullLogger<SecurityClient>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SecurityClient(
            httpClient,
            null!,
            NullLogger<SecurityClient>.Instance));
        Assert.Throws<ArgumentNullException>(() => new SecurityClient(
            httpClient,
            options,
            null!));
        Assert.Throws<ArgumentNullException>(() => new SecurityClient(
            httpClient,
            options,
            NullLogger<SecurityClient>.Instance,
            null!));

        httpClient.Dispose();
    }

    // ==============================================================================================
    //  THE CONTRACT-LEVEL SECRETS RULE, ASSERTED MECHANICALLY
    // ==============================================================================================

    [Fact]
    public void NoWireRequestTypeCanCarryRawKeyMaterial()
    {
        // A REQUEST CARRYING RAW KEY MATERIAL MUST BE IMPOSSIBLE TO CONSTRUCT. Every keyed member of the
        // legacy surface took key bytes as an ordinary parameter; this boundary replaced all of them
        // with opaque references, and the property below is what keeps that true as the file changes.
        string[] forbiddenFragments =
        [
            "key",
            "secret",
            "password",
            "passphrase",
            "credential",
            "certificate",
            "token",
            "iv",
            "salt",
        ];

        string[] permittedNames = ["KeyRef", "IvRef", "FileRef", "PublicKey", "PemFormat"];

        IEnumerable<Type> requestTypes = typeof(SecurityClient).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "PowerFramework.DataServices.Clients")
            .Where(type => type.Name.EndsWith("RequestBody", StringComparison.Ordinal));

        // The sixteen published request schemas, so a future deletion cannot make this test vacuous.
        Assert.Equal(16, requestTypes.Count());

        foreach (Type type in requestTypes)
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                if (permittedNames.Contains(property.Name, StringComparer.Ordinal))
                {
                    // A reference is a handle the Security service resolves; it carries no material and
                    // is the mechanism the rule is implemented with rather than a violation of it. The
                    // two remaining names are a PUBLIC key and a format switch, neither of which is
                    // secret.
                    continue;
                }

                foreach (string fragment in forbiddenFragments)
                {
                    Assert.DoesNotContain(fragment, property.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void TheIssuanceRequestCarriesNoCredentialMemberAtAll()
    {
        Type type = typeof(SecurityClient).Assembly.GetType(
            "PowerFramework.DataServices.Clients.TokenIssuanceRequestBody",
            throwOnError: true)!;

        string[] members =
            [.. type.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)];

        // Exactly three members, because caller identity comes from the transport rather than the body.
        // There is no client secret, no password, no API key, no client assertion and no key material,
        // and no member one could be smuggled into either.
        Assert.Equal(["Audience", "Scopes", "Subject"], members);
    }

    // ==============================================================================================
    //  The published value types
    // ==============================================================================================

    [Fact]
    public void PayloadFormTravelsAsTheSpellingTheContractDeclares()
    {
        Assert.Equal("\"STRING\"", JsonSerializer.Serialize(PayloadForm.STRING));
        Assert.Equal("\"BLOB\"", JsonSerializer.Serialize(PayloadForm.BLOB));
    }

    [Fact]
    public void CryptoPayloadRefusesToReinterpretOneOverloadFamilyAsTheOther()
    {
        CryptoPayload text = CryptoPayload.FromString("value");
        CryptoPayload bytes = CryptoPayload.FromBlob(SamplePayload);

        Assert.Equal(PayloadForm.STRING, text.Form);
        Assert.Equal(PayloadForm.BLOB, bytes.Form);
        Assert.Equal("value", text.AsString());
        Assert.Equal(SamplePayload, bytes.AsBlob().ToArray());

        Assert.Throws<InvalidOperationException>(() => text.AsBlob());
        Assert.Throws<InvalidOperationException>(() => bytes.AsString());
        Assert.Throws<ArgumentNullException>(() => CryptoPayload.FromString(null!));
    }

    [Fact]
    public void CryptoPayloadAcceptsAnEmptyValueInEitherFamily()
    {
        // The legacy accepts an empty string and an empty blob, so refusing either here would narrow the
        // surface without a contract saying so.
        Assert.Equal(string.Empty, CryptoPayload.FromString(string.Empty).AsString());
        Assert.True(CryptoPayload.FromBlob(ReadOnlyMemory<byte>.Empty).AsBlob().IsEmpty);
    }

    [Fact]
    public void EveryTypeThatHoldsSensitiveValuesRedactsItsOwnDescription()
    {
        string payload = CryptoPayload.FromString("super sensitive plaintext").ToString();
        Assert.Contains("[REDACTED]", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("super sensitive plaintext", payload, StringComparison.Ordinal);

        string token = new ServiceToken(
            "fake.not-a-real-token.value",
            "Bearer",
            DateTimeOffset.UnixEpoch,
            ["scope"]).ToString();
        Assert.Contains("[REDACTED]", token, StringComparison.Ordinal);
        Assert.DoesNotContain("fake.not-a-real-token.value", token, StringComparison.Ordinal);

        string generated = new GeneratedRsaKey("public-material", FakeKeyRef, 2048, true).ToString();
        Assert.Contains("[REDACTED]", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public-material", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeKeyRef, generated, StringComparison.Ordinal);

        string reversed = new ReversedBlob(SamplePayload, true).ToString();
        Assert.Contains("[REDACTED]", reversed, StringComparison.Ordinal);
    }

    [Fact]
    public void NoWireTypeGeneratesAMemberPrintingDescription()
    {
        // The wire types are classes rather than records precisely so that formatting one cannot print a
        // token, a plaintext, a ciphertext, a digest, a signature or a reference.
        IEnumerable<Type> wireTypes = typeof(SecurityClient).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "PowerFramework.DataServices.Clients")
            .Where(type => type.Name.EndsWith("RequestBody", StringComparison.Ordinal)
                || type.Name.EndsWith("ResponseBody", StringComparison.Ordinal));

        Assert.Equal(26, wireTypes.Count());

        foreach (Type type in wireTypes)
        {
            MethodInfo? toString = type.GetMethod(nameof(object.ToString), Type.EmptyTypes);

            Assert.NotNull(toString);
            Assert.Equal(typeof(object), toString.DeclaringType);
        }
    }

    [Fact]
    public void ServiceTokenRequestValidatesItselfAgainstThePublishedSchema()
    {
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest(" ", "a", ["s"]));
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest("s", " ", ["s"]));
        Assert.Throws<ArgumentNullException>(() => new ServiceTokenRequest("s", "a", null!));
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest("s", "a", []));
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest("s", "a", [" "]));
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest("s", "a", ["has space"]));
        Assert.Throws<ArgumentException>(() => new ServiceTokenRequest("s", "a", ["dup", "dup"]));

        ServiceTokenRequest request = new("s", "a", ["one", "two"]);
        Assert.Equal(["one", "two"], request.Scopes);
    }

    [Fact]
    public void ServiceTokenCopiesItsGrantedScopeSetDefensively()
    {
        List<string> granted = ["one"];
        ServiceToken token = new("fake", "Bearer", DateTimeOffset.UnixEpoch, granted);
        granted.Add("two");

        Assert.Single(token.GrantedScopes);
        Assert.Throws<ArgumentNullException>(
            () => new ServiceToken(null!, "Bearer", DateTimeOffset.UnixEpoch, []));
        Assert.Throws<ArgumentNullException>(
            () => new GeneratedRsaKey(null!, FakeKeyRef, 2048, null));
    }

    // ==============================================================================================
    //  CONSTANT ACCURACY - diffed against the legacy catalogue this client consumes
    // ==============================================================================================

    [Fact]
    public void TheLegacyConstantsThisClientReliesOnAreTheOnesTheOracleDeclares()
    {
        // enums.sru:L924-L925
        Assert.Equal(0, Enums.CRYPTO_ENCODING_BASE64);
        Assert.Equal(1, Enums.CRYPTO_ENCODING_HEX);

        // enums.sru:L928-L933 - one set for the unkeyed hash, the keyed hash AND the RSA signature hash
        Assert.Equal(0, Enums.CRYPTO_HASH_MD5);
        Assert.Equal(5, Enums.CRYPTO_HASH_CRC32);

        // enums.sru:L936-L940
        Assert.Equal(0, Enums.CRYPTO_SYMCRYPT_TYPE_DES);
        Assert.Equal(4, Enums.CRYPTO_SYMCRYPT_TYPE_AES256);

        // enums.sru:L943-L946 - ECB IS THE DEFAULT MODE, and there is no authenticated mode
        Assert.Equal(0, Enums.CRYPTO_SYMCRYPT_MODE_ECB);
        Assert.Equal(2, Enums.CRYPTO_SYMCRYPT_MODE_CFB);
        Assert.Equal(Enums.CRYPTO_SYMCRYPT_MODE_ECB, Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT);

        // enums.sru:L949-L951 - PKCS#1 IS THE DEFAULT PADDING and there are only two members
        Assert.Equal(0, Enums.CRYPTO_RSA_PADDING_PKCS1);
        Assert.Equal(1, Enums.CRYPTO_RSA_PADDING_OAEP);
        Assert.Equal(Enums.CRYPTO_RSA_PADDING_PKCS1, Enums.CRYPTO_RSA_PADDING_DEFAULT);

        // enums.sru:L954-L957 and L960-L962 - both default flag sets are sums
        Assert.Equal(3u, Enums.CRYPTO_RNDSTRING_DEFAULT);
        Assert.Equal(3u, Enums.CRYPTO_GUID_DEFAULT);

        // enums.sru:L965-L967 - 1024 REMAINS LEGAL
        Assert.Equal(1024, Enums.CRYPTO_RSA_BITS_1024);
        Assert.Equal(4096, Enums.CRYPTO_RSA_BITS_4096);
    }

    [Fact]
    public void TheContractsRandomSizeMaximumIsPublishedRatherThanInvented()
    {
        Assert.Equal(1_048_576u, SecurityClient.MaximumRandomSize);
    }

    // ==============================================================================================
    //  OFF-CONTRACT RESPONSES - every one is a typed failure, never a silently coerced value
    // ==============================================================================================

    [Fact]
    public async Task ABlobShapedResultThatIsNotValidBase64IsATypedFailure()
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.OK,
            "{\"payloadForm\":\"BLOB\",\"data\":\"not base64 at all!!\"}");
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.SymmetricEncryptAsync(
                CryptoPayload.FromString("p"),
                FakeKeyRef,
                Enums.CRYPTO_SYMCRYPT_TYPE_AES256,
                null,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("symmetricEncrypt", failure.OperationId);
        Assert.Equal((int)HttpStatusCode.OK, failure.StatusCode);
        Assert.IsType<FormatException>(failure.InnerException);
    }

    [Theory]
    [InlineData("{\"digest\":123}")]
    [InlineData("{\"notTheMember\":\"x\"}")]
    [InlineData("{ this is not json ")]
    public async Task ASuccessBodyThatDoesNotMatchTheSchemaIsATypedFailure(string json)
    {
        RecordingHandler handler = new RecordingHandler().Enqueue(HttpStatusCode.OK, json);
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.HashAsync(
                CryptoPayload.FromString("x"),
                Enums.CRYPTO_HASH_SHA256,
                TestContext.Current.CancellationToken));

        Assert.Equal("hash", failure.OperationId);
        Assert.IsType<JsonException>(failure.InnerException, exactMatch: false);
    }

    [Fact]
    public async Task ARefusalCarryingAMalformedProblemBodyStillReportsItsStatus()
    {
        // The status is the substantive answer. Losing it behind a deserialization failure would be
        // strictly worse than reporting it with no detail.
        RecordingHandler handler = new RecordingHandler().Enqueue(
            HttpStatusCode.NotFound,
            "{ not a problem document",
            "application/problem+json");
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.HashFileAsync(
                FakeFileRef,
                Enums.CRYPTO_HASH_SHA256,
                TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.NotFound, failure.StatusCode);
        Assert.Equal("hashFile", failure.OperationId);
        Assert.Null(failure.Title);
        Assert.Null(failure.RetCode);
    }

    [Fact]
    public async Task ARefusalWithNoBodyAtAllStillReportsItsStatus()
    {
        RecordingHandler handler = new RecordingHandler()
            .Enqueue(HttpStatusCode.Unauthorized, json: null);
        (SecurityClient client, _) = CreateClient(handler);

        SecurityClientException failure = await Assert.ThrowsAsync<SecurityClientException>(
            () => client.GenerateGuidAsync(null, TestContext.Current.CancellationToken));

        Assert.Equal((int)HttpStatusCode.Unauthorized, failure.StatusCode);
    }

    [Fact]
    public void TheFailureTypeOffersTheThreeStandardExceptionConstructors()
    {
        SecurityClientException bare = new();
        SecurityClientException described = new("a description");
        SecurityClientException caused = new("a description", new InvalidOperationException("cause"));

        Assert.NotNull(bare.Message);
        Assert.Equal("a description", described.Message);
        Assert.IsType<InvalidOperationException>(caused.InnerException);

        // Every projected member is absent until a response supplies it, and a missing return code is
        // neither succeeded nor failed.
        Assert.Null(bare.OperationId);
        Assert.Null(bare.StatusCode);
        Assert.Null(bare.ProblemType);
        Assert.Null(bare.Detail);
        Assert.Null(bare.Instance);
        Assert.Null(bare.RetCode);
        Assert.False(bare.RetCodeIsSucceeded);
        Assert.False(bare.RetCodeIsFailed);
        Assert.False(bare.RetCodeIsCancelled);
    }
}
