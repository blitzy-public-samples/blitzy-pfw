// ==================================================================================================
//  InternalTlsTrustTests - THE ANCHOR EVERY OUTBOUND INTERNAL CHANNEL VERIFIES ITS PEER AGAINST
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   InternalTlsTrust (internal, declared in Program.cs)
//            PowerFramework.Gateway.Configuration.GatewayOptions.InternalTlsTrustOptions
//
//  WHY THIS TYPE EXISTS AT ALL
//  ------------------------------------------------------------------------------------------------
//  Security and DataServices both terminate TLS with certificates issued by the LOCAL certificate
//  authority the generation recipe in docs/ARCHITECTURE.md 9.3.1 creates, and that authority is in no
//  container's operating-system trust store. Left on platform default trust, every outbound channel
//  Gateway opens - token issuance, the two gRPC channels, the bearer handler's key-set backchannel and
//  the three readiness probes - rejects the certificate it is presented, so the documented topology
//  cannot connect at all.
//
//  WHAT THESE TESTS ARE ACTUALLY GUARDING
//  ------------------------------------------------------------------------------------------------
//  Three properties, in descending order of how badly a regression would hurt:
//
//    1. THE SEAM NARROWS TRUST AND NEVER RELAXES IT. The applied policy must trust the configured
//       anchor and NOTHING else - TrustMode CustomRootTrust with the anchor in the custom store. A
//       regression to X509ChainTrustMode.System, or to an empty custom store, would restore the very
//       platform-trust behaviour that made the topology unreachable while looking configured.
//    2. AN UNREADABLE ANCHOR IS FAIL-FAST. A deployment that meant to pin trust and cannot has already
//       lost every authenticated call it would make, so it must refuse to start - the same posture
//       ws_objects/pfw.pbl.src/pfw.sra:L111-L144 takes for a structural fault.
//    3. UNSET IS A LEGITIMATE POSTURE, NOT A MISSING ONE. A deployment whose internal certificates are
//       publicly issued, or whose image already carries the anchor, leaves the path empty and the
//       platform decides. That path must neither throw nor silently install an empty trust store,
//       which would reject everything.
//
//  NO KEY MATERIAL AND NO REAL CERTIFICATE FILE IS COMMITTED BY THIS FILE. Every certificate below is
//  generated in-memory at test time and written to a uniquely named temporary file that the test
//  deletes, so nothing durable is produced and no fixture has to be trusted.
// ==================================================================================================

using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PowerFramework.Gateway.Configuration;
using Xunit;

namespace PowerFramework.Gateway.Tests;

public sealed class InternalTlsTrustTests
{
    /// <summary>
    /// Writes a freshly generated self-signed certificate to a temporary PEM file.
    /// </summary>
    /// <returns>The path, which the caller deletes.</returns>
    /// <remarks>
    /// A self-signed certificate is a legitimate ROOT, which is exactly what an anchor bundle carries,
    /// so this produces the real shape rather than an approximation of it.
    /// </remarks>
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
            $"blitzy_gateway_trust_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, anchor.ExportCertificatePem());

        return path;
    }

    [Fact]
    public void AnUnsetAnchorLeavesPlatformTrustInPlaceAndIsNotAFault()
    {
        InternalTlsTrust trust = new(new GatewayOptions.InternalTlsTrustOptions());

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
            InternalTlsTrust trust = new(new GatewayOptions.InternalTlsTrustOptions
            {
                TrustedCaPath = path,
            });

            Assert.True(trust.IsPinned);

            using SocketsHttpHandler handler = new();

            trust.Apply(handler);

            X509ChainPolicy policy = Assert.IsType<X509ChainPolicy>(
                handler.SslOptions.CertificateChainPolicy);

            // THE THREE ASSERTIONS THAT MATTER. Custom root trust is what excludes the machine's public
            // roots; the anchor being present in the custom store is what makes the local authority
            // acceptable; and no verification flag is relaxed, so an expired or wrongly-purposed
            // certificate still fails.
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
            InternalTlsTrust trust = new(new GatewayOptions.InternalTlsTrustOptions
            {
                TrustedCaPath = path,
            });

            using SocketsHttpHandler first = new();
            using SocketsHttpHandler second = new();

            trust.Apply(first);
            trust.Apply(second);

            // X509ChainPolicy is not documented as thread-safe and a handler may be used concurrently,
            // so sharing one instance across handlers would be a latent data race. One file read, one
            // policy per consumer.
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
            $"blitzy_gateway_trust_absent_{Guid.NewGuid():n}.pem");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new InternalTlsTrust(new GatewayOptions.InternalTlsTrustOptions
            {
                TrustedCaPath = missing,
            }));

        // The configuration KEY is named so an operator can act; the PATH is not, because a startup
        // record must not publish a container's secret mount layout.
        Assert.Contains(nameof(GatewayOptions.InternalTls), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(missing, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileCarryingNoCertificateRefusesToStart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_gateway_trust_empty_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, "# no PEM block here at all\n");

        try
        {
            // An empty bundle is refused rather than treated as "unset": the deployment asked for a
            // pinned anchor, and silently falling back to platform trust would answer a different
            // question from the one it asked.
            _ = Assert.Throws<InvalidOperationException>(
                () => new InternalTlsTrust(new GatewayOptions.InternalTlsTrustOptions
                {
                    TrustedCaPath = path,
                }));
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
        GatewayOptions.InternalTlsTrustOptions options = new() { TrustedCaPath = configured };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public void AWhitespaceAnchorPathIsRefusedByStartupValidation()
    {
        GatewayOptions options = new();
        options.InternalTls.TrustedCaPath = "   ";

        System.ComponentModel.DataAnnotations.ValidationResult[] results =
            [.. options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options))];

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(GatewayOptions.InternalTls)));
    }
}
