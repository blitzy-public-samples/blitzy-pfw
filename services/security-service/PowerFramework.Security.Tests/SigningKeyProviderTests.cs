// ==================================================================================================
//  SigningKeyProviderTests - the public/private split, the fail-fast set, and the wire shape
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS SUITE PROVES, IN ORDER OF SEVERITY
//
//    1. NO PRIVATE COMPONENT IS EVER PUBLISHED. Asserted over the SERIALIZED JSON rather than over the
//       object model, by name, for every private and symmetric member the key-set format defines. This
//       is the highest-severity property in the whole service: one leaked private member on an
//       anonymous route hands any caller the ability to mint tokens indistinguishable from real ones.
//    2. THE TWO PROJECTIONS ARE ONE KEY PAIR. Proved by signing with the private credential and
//       verifying against the published modulus and exponent, and separately against the in-process
//       verification key. A split that publishes the wrong key would look perfect and verify nothing.
//    3. EVERY FAULT REFUSES TO CONSTRUCT. Absent, blank, unusable and public-only material, a blank key
//       identifier and an unsupported algorithm each raise, so a misconfigured issuer never starts. In
//       particular NO EPHEMERAL KEY IS EVER GENERATED: a run with no material supplied must fail, not
//       succeed with a key the provider made up, because that would silently invalidate every token the
//       other services hold and would differ on every restart.
//    4. NO FAILURE ECHOES THE CONFIGURED VALUE. Every message is asserted to name the CONFIGURATION KEY
//       and to contain no fragment of the material - which is also why every assertion here can be
//       written without the value being needed to satisfy it.
//    5. THE PUBLISHED DOCUMENT AGREES WITH THE PUBLISHED CONTRACT. The member set is checked against
//       the key schema in shared/PowerFramework.Contracts/OpenApi/security.v1.yaml, read from disk, so
//       the two cannot drift silently. Where they differ the document wins and the provider changes.
//
//  KEY HYGIENE IN THE TESTS THEMSELVES, WHICH MATTERS AS MUCH AS IN THE CODE UNDER TEST
//
//    EVERY KEY USED HERE IS GENERATED AT RUN TIME, ONCE, IN THIS PROCESS. No key, armour delimiter,
//    encoded run, certificate, password or token literal appears in this file, and nothing is copied
//    from any of the repository's known hardcoded-secret sites - in particular nothing from
//    tests/blink/test_jws.htm:L8-L23, which is the anti-pattern the type under test exists to replace
//    and whose values are never reproduced in any form, transformed or otherwise. A generated key is
//    not merely adequate here, it is better: it proves the provider reads what it is given rather than
//    recognising a fixture.
//
//    Both accepted encodings are exercised in both of their structures - armoured text and base64 of
//    the bare binary form, each in the algorithm-tagged and the older key-specific structure - because
//    the migration record establishes that legacy private-key material genuinely exists in both shapes
//    [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20, where the armoured output is an OPTIONAL
//    fourth argument] and neither may be refused.
//
//  NO KEY-SIZE FLOOR IS ASSERTED ANYWHERE, DELIBERATELY. The oracle keeps 1024-bit RSA as a
//  first-class legal size [ws_objects/pfw.shared.pbl.src/enums.sru:L965], so a test demanding that a
//  short key be refused would pin a silent correction of legacy behaviour. The test key is 2048-bit
//  because the minting library applies its own minimum when it creates a signature provider, and that
//  is that library's rule rather than the provider's policy.
// ==================================================================================================

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Characterizes <see cref="SigningKeyProvider"/>: its fail-fast set, the public/private split, and
/// the wire shape of the key set it publishes.
/// </summary>
public sealed class SigningKeyProviderTests
{
    /// <summary>
    /// The six members the published key carries, and the only six it may carry.
    /// </summary>
    private static readonly string[] ExpectedKeyMembers = ["kty", "use", "alg", "kid", "n", "e"];

    /// <summary>
    /// Every private and symmetric member the key-set format defines. NONE may appear in the published
    /// document, and each is asserted absent BY NAME rather than inferred from the member count.
    /// </summary>
    private static readonly string[] ForbiddenKeyMembers = ["d", "p", "q", "dp", "dq", "qi", "k"];

    /// <summary>
    /// The one key pair every test in this class uses, generated in this process at construction.
    /// </summary>
    private static readonly GeneratedKeyMaterial Material = new();

    /// <summary>
    /// A key identifier for the tests. An identifier, carrying no key material.
    /// </summary>
    private const string TestKeyId = "signing-key-under-test";

    /// <summary>
    /// An issuer identity for the tests, using a reserved name that can never resolve.
    /// </summary>
    private const string TestIssuer = "https://issuer.invalid";

    // ----------------------------------------------------------------------------------------------
    // 1. FAIL FAST - the host must refuse to start, and no key may ever be invented
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Absent material refuses to construct, and NO key is generated to paper over it.
    /// </summary>
    [Fact]
    public void AbsentSigningMaterialRefusesToConstruct()
    {
        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(signingKey: null));

        // The message must send an operator to the configuration key rather than describing a value.
        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure.Message,
            StringComparison.Ordinal);

        // The absent case is reported as ITS OWN fault, distinct from unusable material, because the
        // two have different fixes.
        Assert.Contains("is not set", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Blank material refuses to construct, whitespace included.
    /// </summary>
    /// <param name="signingKey">The blank material.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void BlankSigningMaterialRefusesToConstruct(string signingKey)
    {
        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(signingKey));

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Random bytes are not an asymmetric key. This is the case that actually happens, because random
    /// bytes look like a plausible way to produce a signing value.
    /// </summary>
    [Fact]
    public void RandomBytesRefuseToConstructAndAreNeverEchoed()
    {
        // Generated here, so nothing resembling key material is written into this file.
        string randomMaterial = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(randomMaterial));

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure.Message,
            StringComparison.Ordinal);

        // THE POINT OF THIS ASSERTION: the diagnostic names the key and says nothing about the value.
        AssertMessageDisclosesNothingAbout(failure.Message, randomMaterial);
    }

    /// <summary>
    /// Material that is neither armoured nor base64 at all refuses to construct - the third-encoding
    /// arm, which exists because there is no third encoding.
    /// </summary>
    [Fact]
    public void MaterialInNoAcceptedEncodingRefusesToConstruct()
    {
        // The dash makes it invalid base64 even after the decoder's whitespace tolerance.
        const string unreadable = "not-signing-material";

        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(unreadable));

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure.Message,
            StringComparison.Ordinal);
        AssertMessageDisclosesNothingAbout(failure.Message, unreadable);
    }

    /// <summary>
    /// A PUBLIC key refuses to construct, reported as its own distinct fault.
    /// </summary>
    /// <remarks>
    /// NOT A HYPOTHETICAL BRANCH. Armoured import of a public key SUCCEEDS on this platform, so without
    /// this rejection an issuer configured with the public half would start, satisfy the readiness gate
    /// its dependents wait on, and then fail to sign every token while looking healthy from outside.
    /// </remarks>
    [Fact]
    public void PublicOnlyMaterialRefusesToConstruct()
    {
        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => CreateProvider(Material.PublicPem));

        Assert.Contains(
            SecurityOptions.SigningKeyEnvironmentVariableName,
            failure.Message,
            StringComparison.Ordinal);

        // Distinguished from generic unusability, because the fix is different: supply the other half.
        Assert.Contains("NO PRIVATE COMPONENT", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A blank key identifier refuses to construct: without it the published key and the token header
    /// have nothing to agree on and a rollover cannot be expressed.
    /// </summary>
    /// <param name="keyId">The blank identifier.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankKeyIdentifierRefusesToConstruct(string keyId)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(Material.Pkcs8Base64, keyId: keyId));

        Assert.Contains(
            SecurityOptions.SectionName + ":SigningKeyId",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An algorithm outside the closed asymmetric set refuses to construct, and the refusal names the
    /// permitted values.
    /// </summary>
    /// <param name="algorithm">The rejected identifier.</param>
    /// <remarks>
    /// The keyed-hash entries matter most: switching to one is the obvious wrong answer to a rejected
    /// random string, and it would collapse the sole-issuer topology, since a symmetric key in an
    /// anonymously published set IS the signing secret and every verifier holding it becomes a
    /// co-signer. The probabilistic family is refused because the legacy declares no identifier for it
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L949-L951 declares exactly two padding members], and
    /// the differently cased spelling because these identifiers are case-sensitive.
    /// </remarks>
    [Theory]
    [InlineData("HS256")]
    [InlineData("HS384")]
    [InlineData("HS512")]
    [InlineData("PS256")]
    [InlineData("ES256")]
    [InlineData("none")]
    [InlineData("rs256")]
    [InlineData("RS256 ")]
    [InlineData("")]
    [InlineData("RSA-OAEP")]
    public void UnsupportedAlgorithmRefusesToConstruct(string algorithm)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => CreateProvider(Material.Pkcs8Base64, algorithm: algorithm));

        Assert.Contains(
            SecurityOptions.SectionName + ":SigningAlgorithm",
            failure.Message,
            StringComparison.Ordinal);

        // Composed from the same three symbols the resolver switches on, so the message cannot
        // describe a set the check does not enforce.
        Assert.Contains(SecurityAlgorithms.RsaSha256, failure.Message, StringComparison.Ordinal);
        Assert.Contains(SecurityAlgorithms.RsaSha384, failure.Message, StringComparison.Ordinal);
        Assert.Contains(SecurityAlgorithms.RsaSha512, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A miswired composition root is reported as an argument fault rather than a configuration one.
    /// </summary>
    [Fact]
    public void NullOptionsRefuseToConstruct() =>
        Assert.Throws<ArgumentNullException>(() => new SigningKeyProvider(null!));

    /// <summary>
    /// EVERY algorithm the options validator admits is accepted by the provider.
    /// </summary>
    /// <remarks>
    /// The cross-check that keeps two files in one service from disagreeing about their own contract:
    /// the validator publishes a closed set and documents that this provider is what reads the setting,
    /// so a value that passes validation must not then be refused here. Written against the validator's
    /// own published list rather than a copy of it, so widening that list without widening the provider
    /// breaks this test rather than production.
    /// </remarks>
    [Fact]
    public void EveryPermittedAlgorithmIsAccepted()
    {
        Assert.NotEmpty(SecurityOptionsValidator.PermittedSigningAlgorithms);

        foreach (string algorithm in SecurityOptionsValidator.PermittedSigningAlgorithms)
        {
            using SigningKeyProvider provider =
                CreateProvider(Material.Pkcs8Base64, algorithm: algorithm);

            Assert.Equal(algorithm, provider.SigningCredentials.Algorithm);
            Assert.Equal(algorithm, provider.PublishedKeySet.Keys[0].Algorithm);
        }
    }

    /// <summary>
    /// Both accepted encodings, in both of their structures, construct successfully.
    /// </summary>
    /// <remarks>
    /// Neither shape may be refused: the legacy generator's armoured output is an OPTIONAL fourth
    /// argument [ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L19-L20] so legacy material genuinely
    /// exists in both, and the single-line base64 form is the only shape an environment file can carry
    /// because that format has no line continuation. All four resolve to the SAME key, which is the
    /// second half of the assertion: an encoding must not change the identity of what was configured.
    /// </remarks>
    [Fact]
    public void EveryAcceptedEncodingResolvesTheSameKey()
    {
        string[] encodings =
        [
            Material.Pkcs8Pem,
            Material.Pkcs1Pem,
            Material.Pkcs8Base64,
            Material.Pkcs1Base64,
        ];

        foreach (string encoding in encodings)
        {
            using SigningKeyProvider provider = CreateProvider(encoding);

            PublishedJsonWebKey published = provider.PublishedKeySet.Keys[0];

            Assert.Equal(Material.PublicModulus, published.Modulus);
            Assert.Equal(Material.PublicExponent, published.Exponent);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // 2. THE PUBLIC/PRIVATE SPLIT - the highest-severity assertions in the suite
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The serialized key set carries EXACTLY the six permitted members, and none of the private or
    /// symmetric ones.
    /// </summary>
    /// <remarks>
    /// Asserted over the SERIALIZED JSON, not the object model, because that is what a caller receives:
    /// a member absent from the object graph but reintroduced by a serializer setting would pass an
    /// object-model assertion and still leak. Both the framework's web defaults and the serializer's
    /// bare defaults are exercised, so no naming policy or option set can change the outcome.
    /// </remarks>
    [Fact]
    public void PublishedKeySetSerializesExactlyTheSixPermittedMembers()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        foreach (JsonSerializerOptions serializerOptions in SerializerOptionSets())
        {
            string json = JsonSerializer.Serialize(provider.PublishedKeySet, serializerOptions);

            using JsonDocument document = JsonDocument.Parse(json);

            // The set itself declares one member and no other.
            Assert.Equal(
                ["keys"],
                document.RootElement.EnumerateObject().Select(static member => member.Name));

            JsonElement keys = document.RootElement.GetProperty("keys");

            Assert.Equal(JsonValueKind.Array, keys.ValueKind);

            JsonElement key = Assert.Single(keys.EnumerateArray());

            Assert.Equal(
                ExpectedKeyMembers,
                key.EnumerateObject().Select(static member => member.Name));

            // Absence asserted BY NAME, one at a time, so a failure says which member appeared.
            foreach (string forbidden in ForbiddenKeyMembers)
            {
                Assert.False(
                    key.TryGetProperty(forbidden, out _),
                    FormattableString.Invariant(
                        $"The published key must never carry the member '{forbidden}'."));
            }
        }
    }

    /// <summary>
    /// The published key declares the asymmetric family and the verification use, never the symmetric
    /// family.
    /// </summary>
    [Fact]
    public void PublishedKeyDeclaresTheAsymmetricFamilyAndVerificationUse()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        string json = JsonSerializer.Serialize(provider.PublishedKeySet, WebSerializerOptions());

        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement key = document.RootElement.GetProperty("keys")[0];

        Assert.Equal("RSA", key.GetProperty("kty").GetString());
        Assert.Equal("sig", key.GetProperty("use").GetString());
        Assert.Equal(SecurityAlgorithms.RsaSha256, key.GetProperty("alg").GetString());

        // The symmetric family identifier must not appear anywhere in the document, under any member.
        Assert.DoesNotContain("oct", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The verification key carries no private component, while the signing credential's key does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The public projection is built from parameters exported WITHOUT the private components, so the
    /// private exponent and prime factors are structurally absent rather than present and unpublished.
    /// This asserts both halves of that in one place, because either half alone would be satisfied by a
    /// broken implementation.
    /// </para>
    /// <para>
    /// The status enumeration is used rather than the boolean indicator, and that is measured rather
    /// than preferred: the boolean is deprecated on the asymmetric key type and, with warnings treated
    /// as errors, referencing it does not compile. It is also declared on the asymmetric subtype rather
    /// than on the base key type, which is why each key is narrowed first.
    /// </para>
    /// </remarks>
    [Fact]
    public void VerificationKeyIsPublicOnlyAndSigningKeyIsNot()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        RsaSecurityKey verification = Assert.IsType<RsaSecurityKey>(provider.PublicVerificationKey);
        RsaSecurityKey signing = Assert.IsType<RsaSecurityKey>(provider.SigningCredentials.Key);

        Assert.Equal(PrivateKeyStatus.DoesNotExist, verification.PrivateKeyStatus);
        Assert.Equal(PrivateKeyStatus.Exists, signing.PrivateKeyStatus);

        // Not merely "reports no private key" - the components themselves are absent.
        Assert.Null(verification.Parameters.D);
        Assert.Null(verification.Parameters.P);
        Assert.Null(verification.Parameters.Q);
        Assert.Null(verification.Parameters.DP);
        Assert.Null(verification.Parameters.DQ);
        Assert.Null(verification.Parameters.InverseQ);
        Assert.NotNull(verification.Parameters.Modulus);
        Assert.NotNull(verification.Parameters.Exponent);
    }

    /// <summary>
    /// Nothing derived from the private key appears in the serialized public surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A belt-and-braces assertion over the whole rendered document rather than over member names: it
    /// would catch a private component smuggled into a member that is permitted, which a member-set
    /// assertion cannot.
    /// </para>
    /// <para>
    /// EVERY ONE OF THESE IS A BOOLEAN OVER A FIXED MESSAGE, NOT AN <c>Assert.DoesNotContain</c>. The
    /// needle in each case is live RSA private key material - the PKCS#8 and PKCS#1 encodings of this
    /// suite's key and its six private components - and `DoesNotContain` renders BOTH its needle and its
    /// haystack into the failure message. The failure case of this row is exactly the case in which the
    /// private key IS in the document, so the obvious spelling would write the whole key, twice, into the
    /// test output, the CI log and every artifact that ingests them - which is the disclosure defect
    /// CWE-532 describes and which constraint C-F forbids. The useful diagnostic is WHICH projection
    /// leaked, and that is prose. See SensitiveValueAssertions.cs for the full reasoning.
    /// </para>
    /// </remarks>
    [Fact]
    public void SerializedPublicSurfaceContainsNoPrivateMaterial()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        string json = JsonSerializer.Serialize(provider.PublishedKeySet, WebSerializerOptions());

        Assert.False(
            SensitiveValueAssertions.Carries(json, Material.Pkcs8Base64),
            "The published key set carried the configured PKCS#8 private key encoding. Neither the "
                + "document nor this message renders it.");

        Assert.False(
            SensitiveValueAssertions.Carries(json, Material.Pkcs1Base64),
            "The published key set carried the PKCS#1 encoding of the private key. Neither the document "
                + "nor this message renders it.");

        for (int index = 0; index < Material.PrivateComponents.Length; index++)
        {
            // The component is identified by its POSITION rather than by its value, so a failure names
            // which private component leaked without reproducing any part of it.
            Assert.False(
                SensitiveValueAssertions.Carries(json, Material.PrivateComponents[index]),
                "The published key set carried private RSA component at index "
                    + index.ToString(CultureInfo.InvariantCulture)
                    + " of the private-component set. Neither the component nor the document is "
                    + "rendered here.");
        }
    }

    /// <summary>
    /// A signature made with the private credential verifies against the PUBLISHED material and against
    /// the in-process verification key, proving all three projections are one key pair.
    /// </summary>
    /// <remarks>
    /// This is what makes the split meaningful rather than merely tidy: a provider that published a
    /// different key would satisfy every absence assertion above and verify nothing at all. The
    /// verification is performed twice on purpose - once by rebuilding a key from the two published
    /// members, which is exactly what a consumer's stock handler does, and once through the key handed
    /// to this service's own inbound handler in process.
    /// </remarks>
    [Fact]
    public void SignatureFromTheCredentialVerifiesAgainstThePublishedMaterial()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        byte[] payload = Encoding.UTF8.GetBytes("payload for the round-trip assertion");

        SigningCredentials credentials = provider.SigningCredentials;

        // The same fallback the minting library itself applies: a credential's own factory is optional
        // and is left unset by the provider, so the KEY's factory is what serves. Measured rather than
        // assumed - reading the credential's factory directly is null here.
        CryptoProviderFactory factory =
            credentials.CryptoProviderFactory ?? credentials.Key.CryptoProviderFactory;

        SignatureProvider signer = factory.CreateForSigning(credentials.Key, credentials.Algorithm);

        byte[] signature;

        try
        {
            signature = signer.Sign(payload);
        }
        finally
        {
            factory.ReleaseSignatureProvider(signer);
        }

        Assert.NotEmpty(signature);

        // (a) A consumer's path: rebuild the key from the two published members alone.
        PublishedJsonWebKey published = provider.PublishedKeySet.Keys[0];

        RSAParameters rebuilt = new()
        {
            Modulus = Base64Url.DecodeFromChars(published.Modulus),
            Exponent = Base64Url.DecodeFromChars(published.Exponent),
        };

        using RSA verifier = RSA.Create(rebuilt);

        Assert.True(
            verifier.VerifyData(
                payload,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1),
            "The signature must verify against the published modulus and exponent.");

        // A tampered payload must NOT verify, so the assertion above cannot pass vacuously.
        byte[] tampered = [.. payload];
        tampered[0] ^= 0xFF;

        Assert.False(
            verifier.VerifyData(
                tampered,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1),
            "A tampered payload must not verify.");

        // (b) This service's own inbound path, in process.
        SignatureProvider inProcess =
            factory.CreateForVerifying(provider.PublicVerificationKey, credentials.Algorithm);

        try
        {
            Assert.True(
                inProcess.Verify(payload, signature),
                "The signature must verify against the in-process verification key.");
        }
        finally
        {
            factory.ReleaseSignatureProvider(inProcess);
        }
    }

    /// <summary>
    /// The identifier the key set publishes, the identifier on the credential's key, and the configured
    /// setting are one value.
    /// </summary>
    /// <remarks>
    /// The identifier is set ON the key rather than restated by the issuer, which is why the token
    /// header cannot disagree with the published set: the minting library reads it from the key it is
    /// given. This asserts the mechanism rather than trusting it.
    /// </remarks>
    [Fact]
    public void PublishedIdentifierMatchesTheConfiguredSettingAndTheCredentialKey()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        Assert.Equal(TestKeyId, provider.PublishedKeySet.Keys[0].KeyId);
        Assert.Equal(TestKeyId, provider.SigningCredentials.Key.KeyId);
        Assert.Equal(TestKeyId, provider.PublicVerificationKey.KeyId);
    }

    /// <summary>
    /// The published members are base64url without padding, as the key-set format requires.
    /// </summary>
    /// <remarks>
    /// NOT the same encoding as the legacy payload selector
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L924]: conflating the two would publish padded,
    /// non-url-safe members that a stock consumer cannot read. Asserted by the absence of the three
    /// characters that distinguish the two alphabets, and by a successful decode.
    /// </remarks>
    [Fact]
    public void PublishedMembersAreUrlSafeAndUnpadded()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        PublishedJsonWebKey published = provider.PublishedKeySet.Keys[0];

        foreach (string member in new[] { published.Modulus, published.Exponent })
        {
            Assert.NotEmpty(member);
            Assert.DoesNotContain("=", member, StringComparison.Ordinal);
            Assert.DoesNotContain("+", member, StringComparison.Ordinal);
            Assert.DoesNotContain("/", member, StringComparison.Ordinal);
            Assert.NotEmpty(Base64Url.DecodeFromChars(member));
        }
    }

    /// <summary>
    /// Exactly one key is published, because exactly one key is configured.
    /// </summary>
    /// <remarks>
    /// The set's shape admits more so that a rollover is EXPRESSIBLE and a consumer selects by
    /// identifier; publishing more would require rotation machinery, which this phase does not add.
    /// </remarks>
    [Fact]
    public void ExactlyOneKeyIsPublished()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        Assert.Single(provider.PublishedKeySet.Keys);
    }

    // ----------------------------------------------------------------------------------------------
    // 3. LIFETIME - resolved once, disposed once
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Every member answers the same instance on every access, so the import is not repeated per
    /// request.
    /// </summary>
    /// <remarks>
    /// A lifetime property rather than a cache: there is no eviction, no expiry and no refresh, because
    /// there is one key and it does not change while the process runs. Identity is the assertion,
    /// because equality would also hold for a freshly rebuilt projection.
    /// </remarks>
    [Fact]
    public void EveryMemberAnswersTheSameInstance()
    {
        using SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        Assert.Same(provider.SigningCredentials, provider.SigningCredentials);
        Assert.Same(provider.PublicVerificationKey, provider.PublicVerificationKey);
        Assert.Same(provider.PublishedKeySet, provider.PublishedKeySet);
    }

    /// <summary>
    /// After disposal no member hands out a key whose platform handle has been released, and disposing
    /// twice is harmless.
    /// </summary>
    [Fact]
    public void DisposedProviderRefusesEveryMemberAndDisposesIdempotently()
    {
        SigningKeyProvider provider = CreateProvider(Material.Pkcs8Base64);

        provider.Dispose();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.SigningCredentials);
        Assert.Throws<ObjectDisposedException>(() => provider.PublicVerificationKey);
        Assert.Throws<ObjectDisposedException>(() => provider.PublishedKeySet);
    }

    // ----------------------------------------------------------------------------------------------
    // 4. CONTRACT AGREEMENT - the published document wins on the wire
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The member set this provider publishes agrees with the key schema in the published contract
    /// document, which is read from disk rather than restated here.
    /// </summary>
    /// <remarks>
    /// WHERE THE TWO DIFFER, THE DOCUMENT WINS AND THE PROVIDER CHANGES, because the document is what a
    /// consumer's stock handler reads. Three things are checked: every member the provider emits is
    /// declared by the schema, every member the schema REQUIRES is emitted, and the schema declares
    /// none of the private or symmetric members. The schema additionally forbids undeclared members, so
    /// the first check is what makes the emitted set conformant rather than merely plausible.
    /// </remarks>
    [Fact]
    public void PublishedMemberSetAgreesWithTheContractDocument()
    {
        string[] schemaLines = ReadKeySchemaLines();

        HashSet<string> declared = DeclaredSchemaMembers(schemaLines);

        Assert.Contains("additionalProperties: false", schemaLines.Select(static line => line.Trim()));

        foreach (string emitted in ExpectedKeyMembers)
        {
            Assert.Contains(emitted, declared);
        }

        foreach (string forbidden in ForbiddenKeyMembers)
        {
            Assert.DoesNotContain(forbidden, declared);
        }

        foreach (string required in RequiredSchemaMembers(schemaLines))
        {
            Assert.Contains(required, ExpectedKeyMembers);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a provider over an in-memory options instance.
    /// </summary>
    /// <param name="signingKey">The signing material, or <see langword="null"/> to omit it.</param>
    /// <param name="keyId">The key identifier.</param>
    /// <param name="algorithm">The algorithm identifier.</param>
    /// <returns>A new provider.</returns>
    /// <remarks>
    /// Configuration is the ONLY ingress the type under test has, which is what lets every branch above
    /// be reached without a host, a file, an environment variable or a network.
    /// </remarks>
    private static SigningKeyProvider CreateProvider(
        string? signingKey,
        string keyId = TestKeyId,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        SecurityOptions options = new()
        {
            Issuer = TestIssuer,
            SigningKeyId = keyId,
            SigningAlgorithm = algorithm,
            SigningKey = signingKey,
        };

        return new SigningKeyProvider(Options.Create(options));
    }

    /// <summary>
    /// Asserts that a failure message names no part of the configured value.
    /// </summary>
    /// <param name="message">The failure message.</param>
    /// <param name="material">The configured value the message must not disclose.</param>
    /// <remarks>
    /// <para>
    /// Checked in windows rather than only in full, so a message that quoted a fragment would still
    /// fail. The window is deliberately short: a long window would only catch a wholesale echo.
    /// </para>
    /// <para>
    /// BOOLEANS OVER FIXED MESSAGES, BECAUSE THE NEEDLE IS THE SECRET. Every caller passes configured key
    /// material, and the sliding window means a naive spelling would hand a FRAGMENT of that material to
    /// an assertion overload that renders its needle - so a failure would publish the very bytes the row
    /// exists to prove absent, and would publish them into a CI log that outlives the key. The offending
    /// window is reported by its OFFSET rather than by its content, which is what an operator needs in
    /// order to find the echo in the message being produced.
    /// </para>
    /// </remarks>
    private static void AssertMessageDisclosesNothingAbout(string message, string material)
    {
        Assert.False(
            SensitiveValueAssertions.Carries(message, material),
            "The failure message echoed the configured signing material in full. Neither the message nor "
                + "the material is rendered here.");

        const int windowLength = 8;

        for (int start = 0; start + windowLength <= material.Length; start++)
        {
            string window = material.Substring(start, windowLength);

            Assert.False(
                SensitiveValueAssertions.Carries(message, window),
                "The failure message echoed a fragment of the configured signing material: the "
                    + windowLength.ToString(CultureInfo.InvariantCulture)
                    + "-character window at offset "
                    + start.ToString(CultureInfo.InvariantCulture)
                    + ". The window is identified by position rather than by content, so neither the "
                    + "fragment nor the message is rendered here.");
        }
    }

    /// <summary>
    /// The serializer option sets the published document is asserted under.
    /// </summary>
    /// <returns>The framework's web defaults, then the serializer's bare defaults.</returns>
    private static IEnumerable<JsonSerializerOptions> SerializerOptionSets()
    {
        yield return WebSerializerOptions();
        yield return new JsonSerializerOptions();
    }

    /// <summary>
    /// The serializer options the framework applies to a JSON result.
    /// </summary>
    /// <returns>Web defaults, which apply a camel-case naming policy.</returns>
    private static JsonSerializerOptions WebSerializerOptions() =>
        new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the published key schema out of the contract document.
    /// </summary>
    /// <returns>The schema's own lines, excluding the schema name line.</returns>
    private static string[] ReadKeySchemaLines()
    {
        string documentPath = LocateContractDocument();
        string[] lines = File.ReadAllLines(documentPath);

        const string schemaHeader = "    JsonWebKey:";

        int start = Array.IndexOf(lines, schemaHeader);

        Assert.True(
            start >= 0,
            FormattableString.Invariant(
                $"The contract document at '{documentPath}' no longer declares '{schemaHeader}'."));

        List<string> block = [];

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];

            // A non-blank line at or left of the schema's own indentation ends the block.
            if (line.Length > 0 && !line.StartsWith("     ", StringComparison.Ordinal))
            {
                break;
            }

            block.Add(line);
        }

        Assert.NotEmpty(block);

        return [.. block];
    }

    /// <summary>
    /// Extracts the member names the key schema declares.
    /// </summary>
    /// <param name="schemaLines">The schema block.</param>
    /// <returns>The declared member names.</returns>
    /// <remarks>
    /// A member declaration sits at exactly eight spaces of indentation, under the six-space
    /// <c>properties</c> mapping; anything nested inside a member sits deeper, and anything belonging to
    /// the schema itself sits shallower. Parsed textually rather than with a document reader because
    /// this project references none, and a textual read is sufficient for a member-name comparison.
    /// </remarks>
    private static HashSet<string> DeclaredSchemaMembers(string[] schemaLines)
    {
        HashSet<string> members = new(StringComparer.Ordinal);

        bool insideProperties = false;

        foreach (string line in schemaLines)
        {
            if (line.Trim() == "properties:")
            {
                insideProperties = true;

                continue;
            }

            if (!insideProperties)
            {
                continue;
            }

            int indent = line.Length - line.TrimStart(' ').Length;

            if (indent != 8 || !line.EndsWith(':'))
            {
                continue;
            }

            members.Add(line.Trim().TrimEnd(':'));
        }

        Assert.NotEmpty(members);

        return members;
    }

    /// <summary>
    /// Extracts the member names the key schema requires.
    /// </summary>
    /// <param name="schemaLines">The schema block.</param>
    /// <returns>The required member names.</returns>
    private static string[] RequiredSchemaMembers(string[] schemaLines)
    {
        string? required = schemaLines
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.StartsWith("required:", StringComparison.Ordinal));

        Assert.NotNull(required);

        string list = required[(required.IndexOf('[', StringComparison.Ordinal) + 1)..]
            .TrimEnd(']');

        return [.. list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>
    /// Locates the published contract document by walking up from the test assembly.
    /// </summary>
    /// <returns>The absolute path of the document.</returns>
    private static string LocateContractDocument()
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            string marker = Path.Combine(candidate.FullName, "PowerFramework.slnx");

            if (File.Exists(marker))
            {
                string documentPath = Path.Combine(
                    candidate.FullName,
                    "shared",
                    "PowerFramework.Contracts",
                    "OpenApi",
                    "security.v1.yaml");

                Assert.True(
                    File.Exists(documentPath),
                    FormattableString.Invariant(
                        $"The contract document is missing at '{documentPath}'."));

                return documentPath;
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"No repository root was found above '{AppContext.BaseDirectory}'."));
    }

    /// <summary>
    /// One RSA key pair, generated in this process, rendered in every encoding the tests need.
    /// </summary>
    /// <remarks>
    /// GENERATED RATHER THAN WRITTEN DOWN. No key literal appears in this file, and nothing is copied
    /// from any hardcoded-secret site in the repository. Generating also proves more than a fixture
    /// would: the provider must read what it is handed rather than recognise a known value. 2048 bits
    /// because that is the size at and above which the provider stops remarking on the modulus
    /// (SecurityOptions.LegacyWeakSigningKeySizeBits), so these cases run with no weak-key warning in
    /// the way; the minting library's separate asymmetric minimum is satisfied by the same value. NO
    /// SIZE IS REFUSED: the legacy allowance of 1024 bits
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L965] holds for the issuer identity as well as for
    /// C-02's key-GENERATION surface, and SigningKeyPolicyTests pins the two together.
    /// </remarks>
    private sealed class GeneratedKeyMaterial
    {
        /// <summary>
        /// Generates the pair and renders it.
        /// </summary>
        internal GeneratedKeyMaterial()
        {
            using RSA key = RSA.Create(2048);

            Pkcs8Pem = key.ExportPkcs8PrivateKeyPem();
            Pkcs1Pem = key.ExportRSAPrivateKeyPem();
            Pkcs8Base64 = Convert.ToBase64String(key.ExportPkcs8PrivateKey());
            Pkcs1Base64 = Convert.ToBase64String(key.ExportRSAPrivateKey());
            PublicPem = key.ExportSubjectPublicKeyInfoPem();

            RSAParameters parameters = key.ExportParameters(includePrivateParameters: true);

            PublicModulus = Base64Url.EncodeToString(RequireComponent(parameters.Modulus));
            PublicExponent = Base64Url.EncodeToString(RequireComponent(parameters.Exponent));

            // Rendered in the SAME encoding the published document uses, so that asserting their
            // absence from that document is a meaningful comparison rather than a different alphabet.
            PrivateComponents =
            [
                Base64Url.EncodeToString(RequireComponent(parameters.D)),
                Base64Url.EncodeToString(RequireComponent(parameters.P)),
                Base64Url.EncodeToString(RequireComponent(parameters.Q)),
                Base64Url.EncodeToString(RequireComponent(parameters.DP)),
                Base64Url.EncodeToString(RequireComponent(parameters.DQ)),
                Base64Url.EncodeToString(RequireComponent(parameters.InverseQ)),
            ];
        }

        /// <summary>The private key as armoured algorithm-tagged text.</summary>
        internal string Pkcs8Pem { get; }

        /// <summary>The private key as armoured key-specific text.</summary>
        internal string Pkcs1Pem { get; }

        /// <summary>The private key as base64 of the algorithm-tagged binary structure.</summary>
        internal string Pkcs8Base64 { get; }

        /// <summary>The private key as base64 of the key-specific binary structure.</summary>
        internal string Pkcs1Base64 { get; }

        /// <summary>The PUBLIC key as armoured text, for the public-only rejection.</summary>
        internal string PublicPem { get; }

        /// <summary>The expected published modulus.</summary>
        internal string PublicModulus { get; }

        /// <summary>The expected published exponent.</summary>
        internal string PublicExponent { get; }

        /// <summary>
        /// Every private component, encoded as the published document would encode it, so their absence
        /// from that document can be asserted directly.
        /// </summary>
        internal string[] PrivateComponents { get; }

        /// <summary>
        /// Requires an exported component to be present.
        /// </summary>
        /// <param name="component">The exported component.</param>
        /// <returns>The component.</returns>
        private static byte[] RequireComponent(byte[]? component)
        {
            Assert.NotNull(component);

            return component;
        }
    }
}
