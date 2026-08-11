// ==================================================================================================
//  InternalTlsTrustTests - THE ANCHOR EVERY OUTBOUND DATASERVICES CHANNEL VERIFIES ITS PEER AGAINST
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   InternalTlsTrust (internal, declared in Program.cs)
//            PowerFramework.DataServices.Configuration.InternalTlsTrustOptions
//
//  WHY THIS TYPE EXISTS AT ALL
//  ------------------------------------------------------------------------------------------------
//  Persistence and Security both terminate TLS with certificates issued by the LOCAL certificate
//  authority the generation recipe in docs/ARCHITECTURE.md 9.3.1 creates, and that authority is in no
//  container's operating-system trust store. Left on platform default trust, every outbound channel
//  this service opens - the four Persistence gRPC channels, the Security token and crypto channel, and
//  the bearer handler's key-set backchannel - rejects the certificate it is presented, so the service
//  can obtain no token and reach not one of its 35 Persistence RPCs.
//
//  WHAT THESE TESTS ARE ACTUALLY GUARDING
//  ------------------------------------------------------------------------------------------------
//    1. THE SEAM NARROWS TRUST AND NEVER RELAXES IT. The applied policy must trust the configured
//       anchor and NOTHING else - TrustMode CustomRootTrust with the anchor in the custom store. A
//       regression to X509ChainTrustMode.System, or to an empty custom store, would restore the very
//       platform-trust behaviour that made the topology unreachable while looking configured.
//    2. AN UNREADABLE ANCHOR IS FAIL-FAST, matching the posture
//       ws_objects/pfw.pbl.src/pfw.sra:L111-L144 takes for a structural fault, and the failure never
//       echoes the configured path.
//    3. UNSET IS A LEGITIMATE POSTURE, NOT A MISSING ONE - it means platform default trust, and it must
//       neither throw nor install an empty custom trust store, which would reject every peer.
//
//  NO KEY MATERIAL IS COMMITTED BY THIS FILE. Every certificate is generated in-memory per test and
//  every temporary file is deleted by the test that wrote it.
// ==================================================================================================

using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PowerFramework.DataServices.Configuration;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class InternalTlsTrustTests
{
    /// <summary>
    /// Writes a freshly generated self-signed certificate authority to a temporary PEM file.
    /// </summary>
    /// <returns>The path, which the caller deletes.</returns>
    private static string WriteAnchorPem()
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            "CN=powerframework-local-ca-test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));

        using X509Certificate2 anchor = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));

        string path = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_dataservices_trust_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, anchor.ExportCertificatePem());

        return path;
    }

    [Fact]
    public void AnUnsetAnchorLeavesPlatformTrustInPlaceAndIsNotAFault()
    {
        InternalTlsTrust trust = new(new InternalTlsTrustOptions());

        Assert.False(trust.IsPinned);

        using SocketsHttpHandler handler = new();

        trust.Apply(handler);

        // The property is left ALONE rather than assigned an empty policy. An empty custom trust store
        // under CustomRootTrust would reject every peer, which is the opposite of "platform default".
        Assert.Null(handler.SslOptions.CertificateChainPolicy);
    }

    [Fact]
    public void AConfiguredAnchorPinsTrustToItAndToNothingElse()
    {
        string path = WriteAnchorPem();

        try
        {
            InternalTlsTrust trust = new(new InternalTlsTrustOptions { TrustedCaPath = path });

            Assert.True(trust.IsPinned);

            using SocketsHttpHandler handler = new();

            trust.Apply(handler);

            X509ChainPolicy policy = Assert.IsType<X509ChainPolicy>(
                handler.SslOptions.CertificateChainPolicy);

            Assert.Equal(X509ChainTrustMode.CustomRootTrust, policy.TrustMode);
            Assert.Single(policy.CustomTrustStore);
            Assert.Equal(X509VerificationFlags.NoFlag, policy.VerificationFlags);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EachHandlerReceivesItsOwnPolicyInstance()
    {
        string path = WriteAnchorPem();

        try
        {
            InternalTlsTrust trust = new(new InternalTlsTrustOptions { TrustedCaPath = path });

            using SocketsHttpHandler first = new();
            using SocketsHttpHandler second = new();

            trust.Apply(first);
            trust.Apply(second);

            // X509ChainPolicy is not documented as thread-safe and a handler may be used concurrently,
            // so sharing one instance across handlers would be a latent data race.
            Assert.NotSame(
                first.SslOptions.CertificateChainPolicy,
                second.SslOptions.CertificateChainPolicy);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnAbsentAnchorFileRefusesToStartAndNeverEchoesThePath()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_dataservices_trust_absent_{Guid.NewGuid():n}.pem");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new InternalTlsTrust(new InternalTlsTrustOptions { TrustedCaPath = missing }));

        Assert.Contains(
            nameof(InternalTlsTrustOptions.TrustedCaPath),
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(missing, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileCarryingNoCertificateRefusesToStart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_dataservices_trust_empty_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, "# no PEM block here at all\n");

        try
        {
            // An empty bundle is refused rather than treated as "unset": the deployment asked for a
            // pinned anchor, and silently falling back to platform trust would answer a different
            // question from the one it asked.
            _ = Assert.Throws<InvalidOperationException>(
                () => new InternalTlsTrust(new InternalTlsTrustOptions { TrustedCaPath = path }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("/run/secrets/internal-tls/ca.crt", true)]
    public void TheOptionGroupReportsWhetherTrustIsPinned(string configured, bool expected)
    {
        InternalTlsTrustOptions options = new() { TrustedCaPath = configured };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public void TheOptionGroupCarriesNoMemberThatCouldHoldAPrivateKey()
    {
        // Asserted structurally rather than by review: an anchor with its private key beside it would
        // mean this service could ISSUE certificates for the internal topology, which the sole-issuer
        // topology forbids it (constraint C-G).
        string[] members =
        [
            .. typeof(InternalTlsTrustOptions)
                .GetProperties()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            [nameof(InternalTlsTrustOptions.IsConfigured), nameof(InternalTlsTrustOptions.TrustedCaPath)],
            members);
    }
}
