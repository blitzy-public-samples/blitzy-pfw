// ==================================================================================================
//  THE CALLER-CERTIFICATE POSTURE: ONE REVOCATION DIAL, AND THE CEILING THAT SUBSTITUTES FOR IT
//
//  WHAT WAS WRONG, STATED AS A FACT ABOUT THE CODE RATHER THAN AS A REFERENCE TO A REPORT
//  A caller certificate is judged in TWO places on this service, and the two disagreed. The listener's
//  validation callback built its chain with revocation checking hardcoded off; the issuance-credential
//  check in Tokens/ClientCertificateTrust.cs read `Security:ClientCertificateRevocationMode` and honoured
//  it. So a deployment whose authority DOES publish revocation information could ask the issuance path
//  for a check and had no way at all to ask the handshake for one - one credential, two postures, one of
//  them unreachable from configuration (CWE-295). Both now read the one setting.
//
//  AND THE SECOND HALF, WHICH IS THE ONE THAT WAS ONLY EVER A COMMENT. Every operational surface in this
//  repository argued that short certificate lifetimes are what substitute for revocation on a topology
//  whose authority publishes none, and the issuance recipe used `-days 30` accordingly. Nothing enforced
//  it: a deployment that issued a ten-year caller certificate was accepted without complaint, and that
//  certificate is the credential that mints tokens for every audience its subject is granted. It is a
//  ceiling now, armed only while revocation is not being checked - because a deployment that CAN withdraw
//  a certificate needs no lifetime rule invented on its behalf.
//
//  WHY THE DEFAULT IS NOT THE STRICT VALUE, MEASURED RATHER THAN ASSERTED. The rows below build the
//  certificate shape docs/ARCHITECTURE.md §9.3.1 generates - a local authority with no distribution point
//  and no responder - and show that a chain verifying cleanly with no revocation check FAILS under both
//  stricter modes with an indeterminate status. A strict default would refuse every caller on a clean
//  bring-up, so "production default Online" is a recommendation for a deployment with a real PKI and not
//  a value this repository can ship. That is why the ceiling exists at all.
//
//  NO CREDENTIAL IS COMMITTED BY ANY ROW (constraint C-F). Every key is generated in memory for the
//  duration of one assertion, every anchor is written to a temporary file the row deletes, and no
//  certificate, key or path is recorded in a message.
//
//  RULES POSITION. No user rules were provided for this project; nothing is invented in their place. What
//  governs this file is the enterprise baseline of AAP 0.7.2 with constraints C-B, C-F, C-G and C-K.
// ==================================================================================================

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The revocation posture and the lifetime ceiling, at both places a caller certificate is judged.
/// </summary>
public sealed class CallerCertificatePostureTests
{
    /// <summary>The subject every generated caller certificate carries.</summary>
    /// <remarks>
    /// A rostered caller name, so a row that accidentally reached the name-reconciliation path would
    /// exercise the same subject the roster declares rather than an unrelated one.
    /// </remarks>
    private const string CallerCommonName = "powerframework-gateway";

    /// <summary>
    /// The shipped ceiling is three times the documented issuance window, and it is a real bound.
    /// </summary>
    /// <remarks>
    /// The documented recipe issues 30-day material, so a deployment following it sits nowhere near the
    /// ceiling: the ceiling exists to refuse the years-long certificate, not to police the weeks-long one.
    /// A row rather than a comment because a default silently raised to a decade would leave the control
    /// present in form and absent in effect.
    /// </remarks>
    [Fact]
    public void TheShippedCeilingIsAThirdOfADecadeRatherThanADecade()
    {
        SecurityOptions shipped = new();

        Assert.Equal(90, shipped.MaxCallerCertificateLifetimeDays);
        Assert.Equal(ClientCertificateRevocationModes.NoCheck, shipped.ClientCertificateRevocationMode);
    }

    /// <summary>
    /// The listener's restated ceiling constants agree with the options type they duplicate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DUPLICATION IS NECESSARY AND THEREFORE HAS TO BE POLICED. The listener's HTTPS defaults are
    /// configured on the host builder, before any options instance can be resolved, so the validator that
    /// decides which certificates complete a handshake cannot read the options type - it restates that
    /// type's default and its declared upper bound as constants of its own. Two spellings of one number is
    /// how one of them drifts, and a drift here is silent in the worst direction: a listener whose ceiling
    /// had been raised past the validator's range would ACCEPT for the whole of startup a certificate the
    /// service is about to refuse to run with.
    /// </para>
    /// <para>
    /// Read by reflection because both constants are private, which is where they belong: nothing outside
    /// the validator should be able to select the ceiling the handshake applies.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheListenersRestatedCeilingAgreesWithTheOptionsType()
    {
        static int ConstantOf(string name) => (int)(typeof(CallerCertificateTrust)
            .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetRawConstantValue()
            ?? throw new InvalidOperationException(
                $"CallerCertificateTrust no longer declares '{name}'. If the restated constant was "
                    + "removed because the validator can now read the options type directly, delete this "
                    + "row; if it was renamed, rename it here. Leaving it absent removes the only guard "
                    + "against the two numbers drifting apart."));

        Assert.Equal(
            new SecurityOptions().MaxCallerCertificateLifetimeDays,
            ConstantOf("DefaultMaximumLifetimeDays"));

        System.ComponentModel.DataAnnotations.RangeAttribute range = Assert.IsType<
            System.ComponentModel.DataAnnotations.RangeAttribute>(
            typeof(SecurityOptions)
                .GetProperty(nameof(SecurityOptions.MaxCallerCertificateLifetimeDays))!
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RangeAttribute), false)
                .Single());

        Assert.Equal(range.Maximum, ConstantOf("MaximumConfigurableLifetimeDays"));
    }

    /// <summary>
    /// A caller certificate whose declared window exceeds the ceiling is refused, and one inside it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both arms in one row, deliberately: a refusal assertion on its own is satisfied by an
    /// implementation that refuses everything, which is the failure mode a compensating control is most
    /// likely to have. The two certificates differ in nothing but their declared window.
    /// </para>
    /// <para>
    /// THE WINDOW IS DECLARED, NOT REMAINING. Both certificates below are currently valid, and the long
    /// one is refused anyway - which is the property that stops a caller waiting the check out. Measuring
    /// the remaining validity instead would accept a ten-year certificate for nine of those years.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeclaredWindowOverTheCeilingIsRefusedAndOneUnderItIsNot()
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 compliant = authority.IssueCaller(TimeSpan.FromDays(30));
        using X509Certificate2 overlong = authority.IssueCaller(TimeSpan.FromDays(365));

        ClientCertificateTrust trust = authority.BuildIssuanceTrust(
            ClientCertificateRevocationModes.NoCheck,
            maximumLifetimeDays: 90);

        Assert.Equal(ClientCertificateTrustState.Trusted, trust.Evaluate(compliant));
        Assert.Equal(ClientCertificateTrustState.Untrusted, trust.Evaluate(overlong));

        trust.Dispose();
    }

    /// <summary>
    /// The listener reaches the same verdict on the same two certificates.
    /// </summary>
    /// <remarks>
    /// This is the row that closes the disagreement the file's banner describes. Before the fix the
    /// listener had no ceiling and no configurable posture at all, so the over-long certificate completed
    /// the handshake and only the issuance path could have refused it - and only if the deployment had
    /// configured the path that the handshake ignored.
    /// </remarks>
    [Fact]
    public void TheListenerReachesTheSameVerdictAsTheIssuanceCheck()
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 compliant = authority.IssueCaller(TimeSpan.FromDays(30));
        using X509Certificate2 overlong = authority.IssueCaller(TimeSpan.FromDays(365));

        CallerCertificateTrust listener = authority.BuildListenerTrust(
            ClientCertificateRevocationModes.NoCheck,
            maximumLifetimeDays: 90);

        Assert.True(listener.Validate(compliant, chain: null, SslPolicyErrors.None));
        Assert.False(listener.Validate(overlong, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// The ceiling stops applying when the deployment can withdraw a certificate instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scoping is the design rather than a loophole: a deployment that selects a real revocation
    /// posture has a PKI that can revoke, so a lifetime rule imposed there would be policy invented in
    /// this service. The row is expressed as a REFUSAL FOR A DIFFERENT REASON, which is the only honest
    /// way to assert it on this topology - under <c>Online</c> the documented authority's own leaf is
    /// refused for an indeterminate revocation status, so what the row shows is that the compliant and the
    /// over-long certificate become indistinguishable once the ceiling is disarmed.
    /// </para>
    /// <para>
    /// The measurement behind that claim is the next row.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCeilingIsDisarmedWhenRevocationIsChecked()
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 compliant = authority.IssueCaller(TimeSpan.FromDays(30));
        using X509Certificate2 overlong = authority.IssueCaller(TimeSpan.FromDays(365));

        CallerCertificateTrust listener = authority.BuildListenerTrust(
            ClientCertificateRevocationModes.Online,
            maximumLifetimeDays: 90);

        Assert.Equal(
            listener.Validate(compliant, chain: null, SslPolicyErrors.None),
            listener.Validate(overlong, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// 🔴 Why the shipped default cannot be the strict one, measured on the documented topology.
    /// </summary>
    /// <remarks>
    /// The certificate shape is the one <c>docs/ARCHITECTURE.md</c> §9.3.1 generates: a local authority
    /// created by two <c>openssl</c> invocations, publishing no distribution point and running no
    /// responder. Under no revocation check its leaf verifies; under either stricter mode the chain fails
    /// with an indeterminate status, because no verification flag ignores one. Shipping a strict default
    /// would therefore refuse every caller certificate on a clean bring-up - which is why the ceiling
    /// above exists rather than the strict mode being the default.
    /// </remarks>
    [Fact]
    public void TheStricterPosturesRefuseTheDocumentedAuthoritysOwnLeaf()
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 caller = authority.IssueCaller(TimeSpan.FromDays(30));

        CallerCertificateTrust permissive = authority.BuildListenerTrust(
            ClientCertificateRevocationModes.NoCheck,
            maximumLifetimeDays: 90);

        Assert.True(permissive.Validate(caller, chain: null, SslPolicyErrors.None));

        foreach (string strict in (string[])
            [ClientCertificateRevocationModes.Offline, ClientCertificateRevocationModes.Online])
        {
            CallerCertificateTrust checking = authority.BuildListenerTrust(strict, maximumLifetimeDays: 90);

            Assert.False(
                checking.Validate(caller, chain: null, SslPolicyErrors.None),
                $"Under '{strict}' the documented authority's own leaf must be refused for an "
                    + "indeterminate revocation status. If this passes, an unknown status is being read as "
                    + "'not revoked', which is the arm the posture exists to not have.");
        }
    }

    /// <summary>
    /// Every recognised spelling of the posture is honoured at the listener.
    /// </summary>
    /// <remarks>
    /// Asserted through observable behaviour rather than by reading a private field: under the permissive
    /// posture the documented authority's leaf verifies and under either strict one it does not, so the
    /// verdict itself reports which mode was selected. Case-insensitive, because a deployment writing
    /// <c>nocheck</c> has named a mode this service implements and refusing it would be a spelling rule
    /// invented here.
    /// </remarks>
    [Theory]
    [InlineData("NoCheck", true)]
    [InlineData("nocheck", true)]
    [InlineData("  NoCheck  ", true)]
    [InlineData("Offline", false)]
    [InlineData("OFFLINE", false)]
    [InlineData("Online", false)]
    [InlineData("online", false)]
    public void EveryRecognisedSpellingIsHonouredAtTheListener(string configured, bool expectedVerdict)
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 caller = authority.IssueCaller(TimeSpan.FromDays(30));

        CallerCertificateTrust listener =
            authority.BuildListenerTrust(configured, maximumLifetimeDays: 90);

        Assert.Equal(expectedVerdict, listener.Validate(caller, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// An unparseable posture takes the STRICTEST reading at the listener, not the weakest.
    /// </summary>
    /// <remarks>
    /// The options validator refuses the same value a moment later with the key named, so this reading
    /// governs only the handshakes that could land in between. It is the strict one because quietly
    /// selecting the weakest posture for a typo is exactly how a security setting degrades invisibly -
    /// the deployment would believe it had asked for a check it is not performing.
    /// </remarks>
    [Theory]
    [InlineData("Sometimes")]
    [InlineData("NoCheque")]
    public void AnUnparseablePostureTakesTheStrictestReading(string configured)
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 caller = authority.IssueCaller(TimeSpan.FromDays(30));

        CallerCertificateTrust listener =
            authority.BuildListenerTrust(configured, maximumLifetimeDays: 90);

        Assert.False(listener.Validate(caller, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// An absent ceiling value falls back to the shipped default rather than to no ceiling.
    /// </summary>
    /// <remarks>
    /// A control that disappears when its key is missing is not a control. Both the absent case and the
    /// out-of-range case are covered, because the listener is configured BEFORE the options validator
    /// runs and would otherwise use a value the validator is about to refuse.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_000)]
    public void AnAbsentOrOutOfRangeCeilingFallsBackToTheShippedDefault(int? configured)
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 overlong = authority.IssueCaller(TimeSpan.FromDays(365));

        CallerCertificateTrust listener = authority.BuildListenerTrust(
            ClientCertificateRevocationModes.NoCheck,
            configured);

        Assert.False(listener.Validate(overlong, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// A raised ceiling is honoured, so the control is configurable rather than fixed.
    /// </summary>
    /// <remarks>
    /// The complement of the refusal rows: a deployment that has decided a longer window is acceptable can
    /// say so, and a control nobody can adjust is a control operators route around.
    /// </remarks>
    [Fact]
    public void ARaisedCeilingIsHonoured()
    {
        using CertificateAuthority authority = CertificateAuthority.Create();

        using X509Certificate2 caller = authority.IssueCaller(TimeSpan.FromDays(365));

        CallerCertificateTrust listener = authority.BuildListenerTrust(
            ClientCertificateRevocationModes.NoCheck,
            maximumLifetimeDays: 400);

        Assert.True(listener.Validate(caller, chain: null, SslPolicyErrors.None));
    }

    /// <summary>
    /// A local authority and the caller certificates it issues, held on disk only for one assertion.
    /// </summary>
    /// <remarks>
    /// The anchor has to be a FILE because both trust types read a PEM path - which is the production
    /// ingress and therefore the one worth exercising. It is written under the process temporary directory
    /// and deleted on dispose, and it carries no private key: an anchor is public material, and a trust
    /// anchor with a key beside it would mean this test could issue certificates for the topology.
    /// </remarks>
    private sealed class CertificateAuthority : IDisposable
    {
        private readonly X509Certificate2 _authority;
        private readonly string _anchorPath;

        private CertificateAuthority(X509Certificate2 authority, string anchorPath)
        {
            _authority = authority;
            _anchorPath = anchorPath;
        }

        /// <summary>Creates an authority and writes its public half to a temporary PEM file.</summary>
        /// <returns>The authority, which the caller disposes.</returns>
        internal static CertificateAuthority Create()
        {
            using RSA key = RSA.Create(2048);

            CertificateRequest request = new(
                "CN=powerframework-local-ca",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                    true));

            // THE AUTHORITY'S OWN WINDOW IS WIDE BECAUSE THE PLATFORM REQUIRES IT, NOT BECAUSE THE
            // TOPOLOGY DOES. The documented recipe issues a 30-day authority; the platform refuses to
            // issue a leaf whose window falls outside its issuer's, so a fixture that has to produce a
            // deliberately OVER-LONG leaf needs an issuer wide enough to contain it. The ceiling under
            // test applies to the presented leaf and never to the anchor, so widening the anchor changes
            // nothing the rows assert.
            X509Certificate2 authority = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-500),
                DateTimeOffset.UtcNow.AddDays(500));

            string path = Path.Combine(
                Path.GetTempPath(),
                $"blitzy_adhoc_test_ca_{Guid.NewGuid():N}.pem");

            File.WriteAllText(path, authority.ExportCertificatePem());

            return new CertificateAuthority(authority, path);
        }

        /// <summary>
        /// Issues a caller certificate declaring the given window, centred so that it is currently valid.
        /// </summary>
        /// <param name="declaredWindow">The window between not-before and not-after.</param>
        /// <returns>The certificate, which the caller disposes.</returns>
        /// <remarks>
        /// CENTRED ON NOW, which is what makes the long certificate's refusal attributable to the ceiling
        /// rather than to expiry or to a future start: both certificates are valid at the moment of the
        /// assertion and differ only in how long they claim to be.
        /// </remarks>
        internal X509Certificate2 IssueCaller(TimeSpan declaredWindow)
        {
            using RSA key = RSA.Create(2048);

            CertificateRequest request = new(
                $"CN={CallerCommonName}",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], false));

            DateTimeOffset midpoint = DateTimeOffset.UtcNow;

            return request.Create(
                _authority,
                midpoint - (declaredWindow / 2),
                midpoint + (declaredWindow / 2),
                RandomNumberGenerator.GetBytes(16));
        }

        /// <summary>Builds the issuance-credential trust decision over this authority.</summary>
        /// <param name="revocationMode">The configured posture.</param>
        /// <param name="maximumLifetimeDays">The configured ceiling.</param>
        /// <returns>The trust decision maker.</returns>
        internal ClientCertificateTrust BuildIssuanceTrust(
            string revocationMode,
            int maximumLifetimeDays) =>
            new(
                Options.Create(new SecurityOptions
                {
                    ClientCertificateAuthorityPath = _anchorPath,
                    ClientCertificateRevocationMode = revocationMode,
                    MaxCallerCertificateLifetimeDays = maximumLifetimeDays,
                }),
                TimeProvider.System,
                NullLogger<ClientCertificateTrust>.Instance);

        /// <summary>Builds the listener's trust decision over this authority.</summary>
        /// <param name="revocationMode">The configured posture, in any spelling.</param>
        /// <param name="maximumLifetimeDays">The configured ceiling, which may be absent.</param>
        /// <returns>The validator the HTTPS defaults would be given.</returns>
        internal CallerCertificateTrust BuildListenerTrust(
            string? revocationMode,
            int? maximumLifetimeDays) =>
            CallerCertificateTrust.Load(_anchorPath, revocationMode, maximumLifetimeDays);

        /// <inheritdoc/>
        public void Dispose()
        {
            _authority.Dispose();

            if (File.Exists(_anchorPath))
            {
                File.Delete(_anchorPath);
            }
        }
    }
}
