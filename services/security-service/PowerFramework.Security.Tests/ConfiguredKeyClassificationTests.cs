// ==================================================================================================
//  ConfiguredKeyClassificationTests.cs - A DEPLOYMENT FAULT IS ANSWERED AS A DEPLOYMENT FAULT
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  On contract C-02 a caller never sends key material: it sends an OPAQUE REFERENCE, and this service
//  resolves that reference against material the DEPLOYMENT configured. The caller therefore cannot see
//  the material, cannot measure it and cannot fix it - which makes the attribution of a failure a
//  substantive part of the contract rather than a cosmetic choice of status code.
//
//  Two configured-material faults are DECIDABLE before the caller's payload is touched:
//    * The material imports as no RSA key structure at all.
//    * The material imports and carries the PUBLIC half only, while the operation needs the private one
//      - decryption and signing. This is the case that used to reach the platform, raise the same
//      cryptographic failure an unprocessable payload raises, and be reported to the caller as a 400
//      about its own payload. Every such report was addressed to the wrong party.
//
//  One fault is NOT decidable, and these rows say so rather than pretending otherwise: a configured key
//  of the right kind that is simply the WRONG key fails identically to a corrupt payload, because this
//  surface has no authenticated encryption and therefore carries no integrity tag to separate them. That
//  is a preserved legacy weakness [n_crypto.sru:L62-L69 declare no tag, no salt, no derivation], and the
//  published contract states it at the operation.
//
//  EVERY REFUSAL ROW HERE HAS A POSITIVE COUNTERPART. A screen that refused every key would satisfy all
//  the refusal rows, and would break the whole surface - so the accepting cases are asserted with equal
//  weight: a public key is ACCEPTED where the public half suffices, and a key pair is accepted
//  everywhere, because the public half is derivable from the private one.
// ==================================================================================================

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The RSA provider classifies configured material into the three kinds the boundary needs.
/// </summary>
/// <remarks>
/// <para>
/// THE CLASSIFICATION IS THREE STATES WIDE ON PURPOSE, and these rows pin all three. Unusable and
/// public-only are both deployment faults where a private half is required, but they are DIFFERENT
/// faults - one says the configured value is not a key, the other that it is the wrong half of one - and
/// an operator fixing either needs to know which it has.
/// </para>
/// <para>
/// No key literal appears in this file. Every key is generated at test time through the provider itself,
/// which is the same rule the rest of this suite follows and the reason the secrets sweep finds nothing
/// here.
/// </para>
/// </remarks>
public sealed class RsaKeyClassificationTests
{
    /// <summary>A key pair is classified as a pair, from both of the two accepted encodings.</summary>
    /// <param name="armoured">Whether to generate the armoured form or the bare Base64 one.</param>
    /// <remarks>
    /// BOTH ENCODINGS ARE DRIVEN because the provider accepts both unconditionally, and a classification
    /// that recognised only one would refuse half the keys a deployment could legitimately configure -
    /// which would be a far worse defect than the one being fixed.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKeyPairIsClassifiedAsAPair(bool armoured)
    {
        (string privateKey, _) = GeneratePair(armoured);

        Assert.Equal(RsaKeyKind.KeyPair, CryptoFixture.Rsa.ClassifyKey(privateKey));
    }

    /// <summary>The public half alone is classified as public only, from both accepted encodings.</summary>
    /// <param name="armoured">Whether to generate the armoured form or the bare Base64 one.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePublicHalfIsClassifiedAsPublicOnly(bool armoured)
    {
        (_, string publicKey) = GeneratePair(armoured);

        Assert.Equal(RsaKeyKind.PublicOnly, CryptoFixture.Rsa.ClassifyKey(publicKey));
    }

    /// <summary>Material that is no RSA structure at all is classified as unusable.</summary>
    /// <param name="material">The configured value.</param>
    /// <remarks>
    /// The rows cover the three shapes a misconfiguration actually takes: a value that is not a key,
    /// a value that is valid Base64 of something that is not a key, and a value that is blank. All three
    /// are the same answer to the boundary, and none of them raises - the classification's contract is
    /// to answer rather than to throw.
    /// </remarks>
    [Theory]
    [InlineData("this-is-not-an-rsa-key")]
    [InlineData("bm90LWEta2V5")]
    [InlineData("")]
    [InlineData("   ")]
    public void MaterialThatIsNoKeyIsClassifiedAsUnusable(string material)
    {
        Assert.Equal(RsaKeyKind.Unusable, CryptoFixture.Rsa.ClassifyKey(material));
    }

    /// <summary>A null value is refused as an argument fault rather than answered as a kind.</summary>
    /// <remarks>
    /// A DELIBERATE ASYMMETRY WITH THE BLANK ROW ABOVE. Blank material is a real configured value and is
    /// therefore classified; a null reference is a defect in the calling code, which no configuration can
    /// produce and no classification should absorb.
    /// </remarks>
    [Fact]
    public void ANullValueIsAnArgumentFault() =>
        Assert.Throws<ArgumentNullException>(() => CryptoFixture.Rsa.ClassifyKey(null!));

    /// <summary>Generates a pair in one of the two accepted encodings.</summary>
    /// <param name="armoured">Whether to request the armoured form.</param>
    /// <returns>The private half and the public half.</returns>
    private static (string PrivateKey, string PublicKey) GeneratePair(bool armoured)
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(
            CryptoFixture.Rsa.GenRSAKey(
                Enums.CRYPTO_RSA_BITS_2048,
                ref privateKey,
                ref publicKey,
                armoured),
            "The platform generates a key pair of this published size in both accepted encodings.");

        return (privateKey, publicKey);
    }
}

/// <summary>
/// The four RSA operations attribute a configured-material fault to the deployment, not the caller.
/// </summary>
/// <remarks>
/// Driven through the handlers directly rather than over a host, because the property under test is the
/// STATUS AND RETURN CODE a handler chooses, and every collaborator it needs is injectable. The
/// host-level counterpart of the same posture - that a 500 carries the single published error shape - is
/// asserted where the pipeline is booted.
/// </remarks>
public sealed class ConfiguredKeyAttributionTests
{
    /// <summary>The reference the pair's private half sits under.</summary>
    private const string PrivateRef = "rsa-private";

    /// <summary>The reference the pair's public half sits under.</summary>
    private const string PublicRef = "rsa-public";

    /// <summary>The reference unusable material sits under.</summary>
    private const string BrokenRef = "rsa-broken";

    /// <summary>Material that imports as no RSA structure, used for the unusable rows.</summary>
    /// <remarks>
    /// READABLE ENGLISH RATHER THAN KEY-SHAPED TEXT (C-F). A realistic-looking key literal would be
    /// exactly the class of artifact the repository-wide secrets sweep exists to eliminate, and it would
    /// survive a copy-and-paste into somewhere that mattered; this value has none of a key's structure,
    /// so a reviewer and a secret scanner reach the same conclusion about it without decoding anything.
    /// </remarks>
    private const string UnusableMaterial = "this-configured-value-is-not-a-key";

    /// <summary>
    /// A public-only key is a SERVER fault on the two operations that need the private half.
    /// </summary>
    /// <param name="signing">Whether to drive signing; otherwise decryption.</param>
    /// <remarks>
    /// <para>
    /// THIS IS THE ROW THE FINDING EXISTS FOR. Before the screen, this request reached the platform, the
    /// platform raised the same cryptographic failure an undecryptable payload raises, and the boundary
    /// answered 400 - telling the caller its payload was at fault when the caller had supplied only an
    /// opaque reference and the deployment had configured the wrong half of a key.
    /// </para>
    /// <para>
    /// The detail is asserted to name the fault specifically rather than merely to be a 500, because the
    /// two decidable configured-material faults have different fixes and an operator has to be able to
    /// tell them apart from the response alone.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APublicOnlyKeyWhereThePrivateHalfIsNeededIsAServerFault(bool signing)
    {
        CryptoReferenceResolver store = PairStore();

        ProblemHttpResult problem = signing
            ? CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                new RsaSignRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = PublicRef,
                    HashType = Enums.CRYPTO_HASH_SHA256,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers))
            : CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
                new RsaCipherRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = PublicRef,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INTERNAL_ERROR, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);

        // NOT the caller's status, stated explicitly: this is the exact substitution the finding names.
        Assert.NotEqual(StatusCodes.Status400BadRequest, problem.StatusCode);

        Assert.NotNull(problem.ProblemDetails.Detail);
        Assert.Contains("public key", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Contains("private half", problem.ProblemDetails.Detail, StringComparison.Ordinal);

        // The two decidable faults are distinguishable from the body alone.
        Assert.DoesNotContain(
            "could not be used as an RSA key",
            problem.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Unusable configured material is a SERVER fault on all four RSA operations.
    /// </summary>
    /// <param name="operation">Which operation to drive.</param>
    /// <remarks>
    /// THE VERIFICATION ROW IS THE ONE WORTH HAVING. That operation deliberately collapses a malformed
    /// signature into a FALSE VERDICT with a 200, so an unusable key answered the same way would report
    /// "this signature does not verify" when the truth is "this deployment has no key to verify with" -
    /// a false negative a caller could not distinguish from a genuine one. Screening the material first
    /// is what keeps the two apart.
    /// </remarks>
    [Theory]
    [InlineData("rsaEncrypt")]
    [InlineData("rsaDecrypt")]
    [InlineData("rsaSign")]
    [InlineData("rsaVerify")]
    public void UnusableConfiguredMaterialIsAServerFaultOnEveryOperation(string operation)
    {
        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BrokenRef] = UnusableMaterial,
            });

        ProblemHttpResult problem = Refuse(operation, store, BrokenRef);

        Assert.Equal(RetCode.E_INTERNAL_ERROR, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);

        // Nothing about the configured material reaches the body, in any part.
        string rendered = JsonSerializer.Serialize(problem.ProblemDetails);

        Assert.DoesNotContain(UnusableMaterial, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(BrokenRef, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// The configured material is screened BEFORE the caller's payload is decoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ORDERING IS THE FIX, not merely the status. This request is wrong in TWO ways at once: the
    /// configured key is the public half where decryption needs the private one, AND the payload is not
    /// decodable in the form the request declares. Only one of the two can be reported, and it must be
    /// the deployment's - because the caller can act on a payload verdict, would find nothing wrong with
    /// its payload's encoding for the operation it asked for, and has no way to reach the real cause.
    /// </para>
    /// <para>
    /// A row that asserted only the status would pass with the screen AFTER the payload decode, since
    /// both orderings produce a 4xx or a 5xx; asserting WHICH one distinguishes them.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheConfiguredKeyIsScreenedBeforeTheCallerPayload()
    {
        CryptoReferenceResolver store = PairStore();

        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                // Not valid base64, so the blob-form decode would refuse it as the caller's payload.
                Data = "%%%not-base64%%%",
                PayloadForm = PayloadForm.BLOB,
                KeyRef = PublicRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
        Assert.Equal(RetCode.E_INTERNAL_ERROR, CryptoFixture.RetCodeOf(problem));

        Assert.NotNull(problem.ProblemDetails.Detail);
        Assert.Contains("private half", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE POSITIVE ARM: a public-only key is accepted where the public half suffices.
    /// </summary>
    /// <remarks>
    /// Encryption and verification take the public half on the legacy declarations they substitute
    /// [n_crypto.sru:L62-L65, :L72-L73], so refusing a public key on either would break the surface the
    /// screen is supposed to protect. Both are driven here for that reason.
    /// </remarks>
    [Fact]
    public void APublicOnlyKeyIsAcceptedWhereThePublicHalfSuffices()
    {
        CryptoReferenceResolver store = PairStore();

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = "short asymmetric payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = PublicRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.NotEmpty(encrypted.Data);

        PayloadResponse signature = CryptoFixture.Success(CryptoEndpoints.RsaSign(
            new RsaSignRequest
            {
                Data = "signed payload",
                PayloadForm = PayloadForm.STRING,
                KeyRef = PrivateRef,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        RsaVerifyResponse verdict = CryptoFixture.Success(CryptoEndpoints.RsaVerify(
            new RsaVerifyRequest
            {
                Data = "signed payload",
                Signature = signature.Data,
                PayloadForm = PayloadForm.STRING,
                KeyRef = PublicRef,
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.True(verdict.Valid);
    }

    /// <summary>
    /// THE SECOND POSITIVE ARM: a key pair is accepted on the public-half operations too.
    /// </summary>
    /// <remarks>
    /// The public half is DERIVABLE from the private one, so a deployment that configures one reference
    /// holding the pair and points both an encrypt and a decrypt at it is configured correctly - and a
    /// screen that required the exact half would refuse it. There is deliberately no fourth key kind for
    /// "private only" for the same reason.
    /// </remarks>
    [Fact]
    public void AKeyPairIsAcceptedWhereOnlyThePublicHalfIsNeeded()
    {
        CryptoReferenceResolver store = PairStore();

        const string plaintext = "round trip through one reference";

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = plaintext,
                PayloadForm = PayloadForm.STRING,
                KeyRef = PrivateRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        PayloadResponse decrypted = CryptoFixture.Success(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                Data = encrypted.Data,
                PayloadForm = PayloadForm.STRING,
                KeyRef = PrivateRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(plaintext, decrypted.Data, StringComparer.Ordinal);
    }

    /// <summary>
    /// THE RESIDUAL AMBIGUITY, ASSERTED RATHER THAN LEFT AS A CLAIM.
    /// </summary>
    /// <remarks>
    /// A configured key of the RIGHT kind that is simply the WRONG key is attributed to the caller's
    /// payload, and that is not a defect in the screen - it is the absence of any integrity tag on this
    /// surface, which is a preserved legacy weakness rather than an omission here. Asserting it means the
    /// limitation is recorded in executable form: if a future change ever made the two distinguishable,
    /// this row fails and the limitation is revisited deliberately instead of drifting.
    /// </remarks>
    [Fact]
    public void AWrongButUsableKeyIsAttributedToThePayloadBecauseNoIntegrityTagExists()
    {
        CryptoReferenceResolver first = PairStore();
        CryptoReferenceResolver second = PairStore();

        PayloadResponse encrypted = CryptoFixture.Success(CryptoEndpoints.RsaEncrypt(
            new RsaCipherRequest
            {
                Data = "payload encrypted to the first key",
                PayloadForm = PayloadForm.STRING,
                KeyRef = PublicRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            first,
            CryptoFixture.Loggers));

        // Decrypted with a DIFFERENT pair's private half: the material is the right KIND, so the screen
        // passes it, and the platform then fails exactly as it would on a corrupt payload.
        ProblemHttpResult problem = CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
            new RsaCipherRequest
            {
                Data = encrypted.Data,
                PayloadForm = PayloadForm.STRING,
                KeyRef = PrivateRef,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Encodings,
            second,
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_DATA, CryptoFixture.RetCodeOf(problem));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    /// <summary>Builds a store holding a freshly generated pair under both references.</summary>
    /// <returns>The store.</returns>
    /// <remarks>
    /// A NEW PAIR PER CALL, which is what lets the wrong-key row above hold two stores that disagree.
    /// Generated through the provider so that no key literal exists in this file.
    /// </remarks>
    private static CryptoReferenceResolver PairStore()
    {
        string privateKey = string.Empty;
        string publicKey = string.Empty;

        Assert.True(
            CryptoFixture.Rsa.GenRSAKey(Enums.CRYPTO_RSA_BITS_2048, ref privateKey, ref publicKey),
            "The platform generates a key pair of this published size.");

        return CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PrivateRef] = privateKey,
                [PublicRef] = publicKey,
            });
    }

    /// <summary>Drives one RSA operation and unwraps its refusal.</summary>
    /// <param name="operation">The operation identifier.</param>
    /// <param name="store">The reference store.</param>
    /// <param name="keyRef">The reference to name in the request.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier names no RSA operation.</exception>
    private static ProblemHttpResult Refuse(
        string operation,
        CryptoReferenceResolver store,
        string keyRef) => operation switch
        {
            "rsaEncrypt" => CryptoFixture.Rejection(CryptoEndpoints.RsaEncrypt(
                new RsaCipherRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = keyRef,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers)),
            "rsaDecrypt" => CryptoFixture.Rejection(CryptoEndpoints.RsaDecrypt(
                new RsaCipherRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = keyRef,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers)),
            "rsaSign" => CryptoFixture.Rejection(CryptoEndpoints.RsaSign(
                new RsaSignRequest
                {
                    Data = "payload",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = keyRef,
                    HashType = Enums.CRYPTO_HASH_SHA256,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers)),
            "rsaVerify" => CryptoFixture.Rejection(CryptoEndpoints.RsaVerify(
                new RsaVerifyRequest
                {
                    Data = "payload",
                    Signature = "AQID",
                    PayloadForm = PayloadForm.STRING,
                    KeyRef = keyRef,
                    HashType = Enums.CRYPTO_HASH_SHA256,
                },
                CryptoFixture.Caller(),
                CryptoFixture.Rsa,
                CryptoFixture.Encodings,
                store,
                CryptoFixture.Loggers)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "No such operation."),
        };
}
