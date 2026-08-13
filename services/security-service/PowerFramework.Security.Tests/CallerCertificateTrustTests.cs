// ==================================================================================================
//  CallerCertificateTrustTests - WHOSE CLIENT CERTIFICATES THE ISSUANCE EDGE ACCEPTS
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   CallerCertificateTrust (internal, declared in Program.cs)
//            PowerFramework.Security.Configuration.SecurityMutualTlsOptions
//
//  THE VULNERABILITY THIS TYPE CLOSES, STATED PLAINLY
//  ------------------------------------------------------------------------------------------------
//  `POST /v1/tokens` is authenticated by CLIENT CERTIFICATE and by nothing else, because a caller
//  cannot present a bearer token in order to obtain its first bearer token. Endpoints/TokenEndpoints.cs
//  reads the presented certificate's COMMON NAME and reconciles it against the claimed subject.
//
//  A NAME PROVES NOTHING ON ITS OWN. Unless the chain behind that certificate is verified against a
//  known authority, any caller can generate a self-signed certificate whose common name is
//  `powerframework-gateway` and be minted a Gateway token for every audience Gateway is permitted. The
//  documented topology issues caller certificates from a LOCAL authority that is in no container's
//  operating-system trust store, so the platform could not make that decision either - which left the
//  strongest identity claim in the system resting on an unverified string.
//
//  WHAT THESE TESTS GUARD
//  ------------------------------------------------------------------------------------------------
//    1. A CERTIFICATE FROM THE CONFIGURED AUTHORITY IS ACCEPTED, so the documented topology works.
//    2. A SELF-SIGNED CERTIFICATE IS REFUSED even when its common name is a real caller identity -
//       this is the impersonation case, and it is the one that matters most.
//    3. A CERTIFICATE FROM A DIFFERENT AUTHORITY IS REFUSED, so pinning is to THIS anchor rather than
//       to "any CA-looking thing".
//    4. NO CERTIFICATE IS REFUSED.
//    5. WITH NO ANCHOR CONFIGURED the platform's own verdict is returned unchanged - neither an
//       unconditional accept (which would be the bypass an explicit anchor exists to prevent) nor an
//       unconditional refuse
//       (which would make a publicly-issued or image-trusted deployment unable to accept any caller).
//    6. AN UNREADABLE ANCHOR IS FAIL-FAST, and the failure never echoes the configured path.
//
//  NO KEY MATERIAL IS COMMITTED BY THIS FILE. Every certificate is generated in-memory per test and
//  every temporary file is deleted by the test that wrote it.
// ==================================================================================================

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PowerFramework.Security.Configuration;
using Xunit;

namespace PowerFramework.Security.Tests;

public sealed class CallerCertificateTrustTests
{
    /// <summary>
    /// Creates a self-signed certificate authority.
    /// </summary>
    /// <param name="commonName">The authority's name.</param>
    /// <returns>The authority, including its key so it can sign.</returns>
    private static X509Certificate2 CreateAuthority(string commonName)
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    /// <summary>
    /// Issues a client certificate from an authority.
    /// </summary>
    /// <param name="authority">The signing authority.</param>
    /// <param name="commonName">The caller identity the certificate establishes.</param>
    /// <returns>The issued certificate, public part only - a chain check needs no private key.</returns>
    private static X509Certificate2 IssueCallerCertificate(
        X509Certificate2 authority,
        string commonName)
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));

        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.2", "Client Authentication")],
            critical: false));

        return request.Create(
            authority,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(12),
            [.. Guid.NewGuid().ToByteArray()]);
    }

    /// <summary>
    /// Creates a self-signed certificate that is NOT an authority - the impersonation case.
    /// </summary>
    /// <param name="commonName">The caller identity it falsely claims.</param>
    /// <returns>The certificate.</returns>
    /// <summary>
    /// The revocation posture the shipped settings file declares, passed explicitly by every row.
    /// </summary>
    /// <remarks>
    /// EXPLICIT RATHER THAN DEFAULTED, because the loader takes no default for it and deliberately so: a
    /// host that omitted the posture would silently run on one nobody chose, which is the class of defect
    /// an optional parameter on a security decision invites. Spelling it here keeps every row below
    /// asserting the behaviour of the SHIPPED posture rather than of an implicit one.
    /// </remarks>
    private const string ShippedRevocationMode = ClientCertificateRevocationModes.NoCheck;

    /// <summary>The declared-window ceiling the shipped settings file declares, in days.</summary>
    /// <remarks>
    /// Every certificate this file builds declares a window of one day or twelve hours, so no row is near
    /// the ceiling and none of them is asserting the ceiling by accident. The ceiling has its own rows in
    /// <c>CallerCertificateLifetimeCeilingTests</c>.
    /// </remarks>
    private const int ShippedLifetimeDays = 90;

    private static X509Certificate2 CreateSelfSignedCaller(string commonName)
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            $"CN={commonName}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false,
            hasPathLengthConstraint: false,
            pathLengthConstraint: 0,
            critical: true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(12));
    }

    /// <summary>
    /// Writes an authority's public certificate to a temporary PEM file.
    /// </summary>
    /// <param name="authority">The authority to publish.</param>
    /// <returns>The path, which the caller deletes.</returns>
    private static string WriteAnchorPem(X509Certificate2 authority)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_security_clientca_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, authority.ExportCertificatePem());

        return path;
    }

    [Fact]
    public void ACallerCertificateFromTheConfiguredAuthorityIsAccepted()
    {
        using X509Certificate2 authority = CreateAuthority("powerframework-local-ca-test");
        using X509Certificate2 caller =
            IssueCallerCertificate(authority, "powerframework-gateway");

        string anchor = WriteAnchorPem(authority);

        try
        {
            CallerCertificateTrust trust = CallerCertificateTrust.Load(anchor, ShippedRevocationMode, ShippedLifetimeDays);

            Assert.True(trust.IsPinned);

            // The platform's own verdict is UntrustedRoot, because a local authority is not in the
            // machine store - and the whole point is that this decision is made against the configured
            // anchor rather than against the machine store.
            Assert.True(trust.Validate(caller, chain: null, SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            File.Delete(anchor);
        }
    }

    [Fact]
    public void ASelfSignedCertificateClaimingARealCallerIdentityIsRefused()
    {
        using X509Certificate2 authority = CreateAuthority("powerframework-local-ca-test");
        using X509Certificate2 impostor = CreateSelfSignedCaller("powerframework-gateway");

        string anchor = WriteAnchorPem(authority);

        try
        {
            CallerCertificateTrust trust = CallerCertificateTrust.Load(anchor, ShippedRevocationMode, ShippedLifetimeDays);

            // THE IMPERSONATION CASE. The common name is byte-identical to a real caller identity, so
            // the endpoint's name reconciliation would have passed. Only the chain check stops it.
            Assert.False(trust.Validate(
                impostor,
                chain: null,
                SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            File.Delete(anchor);
        }
    }

    [Fact]
    public void ACallerCertificateFromADifferentAuthorityIsRefused()
    {
        using X509Certificate2 trusted = CreateAuthority("powerframework-local-ca-test");
        using X509Certificate2 foreign = CreateAuthority("some-other-authority");
        using X509Certificate2 caller =
            IssueCallerCertificate(foreign, "powerframework-dataservices");

        string anchor = WriteAnchorPem(trusted);

        try
        {
            CallerCertificateTrust trust = CallerCertificateTrust.Load(anchor, ShippedRevocationMode, ShippedLifetimeDays);

            // Pinning is to THIS authority, not to "anything with a CA basic constraint".
            Assert.False(trust.Validate(
                caller,
                chain: null,
                SslPolicyErrors.RemoteCertificateChainErrors));
        }
        finally
        {
            File.Delete(anchor);
        }
    }

    [Fact]
    public void AnAbsentCertificateIsRefusedWhetherOrNotAnAnchorIsConfigured()
    {
        CallerCertificateTrust unpinned = CallerCertificateTrust.Load(
            clientCaPath: null,
            ShippedRevocationMode,
            ShippedLifetimeDays);

        Assert.False(unpinned.IsPinned);
        Assert.False(unpinned.Validate(certificate: null, chain: null, SslPolicyErrors.None));

        using X509Certificate2 authority = CreateAuthority("powerframework-local-ca-test");
        string anchor = WriteAnchorPem(authority);

        try
        {
            CallerCertificateTrust pinned = CallerCertificateTrust.Load(anchor, ShippedRevocationMode, ShippedLifetimeDays);

            Assert.False(pinned.Validate(certificate: null, chain: null, SslPolicyErrors.None));
        }
        finally
        {
            File.Delete(anchor);
        }
    }

    [Theory]
    [InlineData(SslPolicyErrors.None, true)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch, false)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable, false)]
    public void WithNoAnchorConfiguredThePlatformsVerdictIsReturnedUnchanged(
        SslPolicyErrors errors,
        bool expected)
    {
        using X509Certificate2 caller = CreateSelfSignedCaller("powerframework-gateway");

        CallerCertificateTrust trust = CallerCertificateTrust.Load(
            clientCaPath: "   ",
            ShippedRevocationMode,
            ShippedLifetimeDays);

        Assert.False(trust.IsPinned);

        // NEITHER an unconditional accept - which would be the bypass this whole type replaces - NOR an
        // unconditional refuse, which would leave a deployment whose caller CA is already in the image
        // unable to accept any caller at all.
        Assert.Equal(expected, trust.Validate(caller, chain: null, errors));
    }

    [Fact]
    public void AnUnreadableAnchorRefusesToStartAndNeverEchoesThePath()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_security_clientca_absent_{Guid.NewGuid():n}.pem");

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => CallerCertificateTrust.Load(missing, ShippedRevocationMode, ShippedLifetimeDays));

        Assert.Contains(
            nameof(SecurityMutualTlsOptions.ClientCaPath),
            failure.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(missing, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileCarryingNoCertificateRefusesToStart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"blitzy_security_clientca_empty_{Guid.NewGuid():n}.pem");

        File.WriteAllText(path, "not a certificate\n");

        try
        {
            _ = Assert.Throws<InvalidOperationException>(() => CallerCertificateTrust.Load(path, ShippedRevocationMode, ShippedLifetimeDays));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("/run/secrets/security-mtls/client-ca.crt", true)]
    public void TheOptionGroupReportsWhetherCallerTrustIsPinned(string configured, bool expected)
    {
        SecurityMutualTlsOptions options = new() { ClientCaPath = configured };

        Assert.Equal(expected, options.IsConfigured);
    }

    [Fact]
    public void TheOptionGroupCarriesNoMemberThatCouldHoldAPrivateKey()
    {
        // C-F, asserted structurally rather than by review: an anchor with its private key beside it
        // would let THIS service issue the very caller identities it authenticates, so a compromise
        // here would become a compromise of every caller rather than of the signing key alone.
        string[] members =
        [
            .. typeof(SecurityMutualTlsOptions)
                .GetProperties()
                .Select(static property => property.Name),
        ];

        Assert.Equal(
            [nameof(SecurityMutualTlsOptions.ClientCaPath), nameof(SecurityMutualTlsOptions.IsConfigured)],
            members.Order(StringComparer.Ordinal).ToArray());
    }
}
