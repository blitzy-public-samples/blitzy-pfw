// ==================================================================================================
//  ClientCertificateTrust.cs - THE TRUST DECISION BEHIND THE ONE ROUTE THAT MINTS A TOKEN
//  ------------------------------------------------------------------------------------------------
//  ROLE
//  Answers exactly one question, in four states: may the identity carried by this caller certificate
//  be honoured? It is consulted by the token operation BEFORE that operation reads the certificate's
//  common name, so an identity is never reconciled against a claim until the certificate carrying it
//  has been established as trustworthy.
//
//  WHY IT EXISTS AS A TYPE
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml publishes a 401 on POST /v1/tokens whose
//  declared meaning is "no client certificate was presented, OR the certificate presented is not
//  trusted". The second half of that sentence is a behaviour, and a behaviour needs somewhere to live
//  that a test can drive and a reader can audit. Leaving it to the container's OS trust store - which
//  an earlier plan for this service proposed - fails on both counts: that store already trusts every
//  public root in the base image, so the set of issuers able to mint a caller identity would be as
//  wide as the public web PKI and stated nowhere; and no in-process observation can distinguish
//  "trusts our certificate authority" from "trusts everything", so the property could not be asserted
//  at all.
//
//  WHAT IT CHECKS, AND IN WHAT ORDER
//    1. A certificate was presented at all.
//    2. This deployment configured a trust anchor at all. With none, NOTHING is trusted - the
//       fail-closed direction - and the operation answers the same 401 it answers for a caller that
//       presented nothing.
//    3. The certificate chains to that anchor and ONLY to that anchor: the chain is built with custom
//       root trust, so a certificate chaining to a public root in the machine store is untrusted here
//       unless that root is also the configured anchor.
//    4. It is inside its validity window, measured against the INJECTED clock rather than the ambient
//       one, so a characterization recording under a fixed clock is reproducible.
//    5. Revocation, to the depth the deployment configured. An indeterminate status is a REFUSAL under
//       either checking mode; there is no arm that reads "could not tell" as "not revoked".
//    6. Its extended key usage, where it declares one, includes client authentication. A certificate
//       declaring none is unrestricted by definition and passes, which is the standard reading and the
//       one the repository's own generation recipe depends on.
//
//  WHAT IT DELIBERATELY DOES NOT DO
//    * It does not report ANYTHING about the certificate it rejected - not a thumbprint, not a subject,
//      not an issuer, not a serial number, not a chain status string. The four-state answer is the
//      whole return value precisely so it cannot become a channel for the material it inspected, and
//      the operation's refusal is the same sentence for every state but the last.
//    * It does not authenticate anything other than the issuance edge. The three other services hold
//      VERIFICATION material only and are not issuers, and every other route on this service is either
//      anonymous by contract or bearer-authenticated.
//    * It does not replace the listener's own client-certificate validation. Both run: the transport
//      refuses an untrusted certificate at the handshake in a deployed topology, and this type is the
//      check that still holds when the transport is terminated elsewhere, is configured differently, or
//      is a test host with no TLS at all. Two independent checks, deliberately, because the transport's
//      is unobservable from inside the process and this one is unobservable from outside it.
//
//  LEGACY REFERENCE (read only: constraint C-C)
//  There is no legacy analogue. PowerFramework is a library with no listener and no caller identity of
//  any kind, so client-certificate trust is net-new. What IS ported is the posture: a structural
//  configuration fault ends the process rather than degrading past it
//  [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, ending in HALT CLOSE at :L143]. A configured anchor path
//  that cannot be read is exactly such a fault and refuses the host.
// ==================================================================================================

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Configuration;

namespace PowerFramework.Security.Tokens;

/// <summary>
/// Why a caller certificate may or may not have its identity honoured.
/// </summary>
/// <remarks>
/// FOUR STATES RATHER THAN A BOOLEAN, because the operator-facing record and the caller-facing response
/// need different granularity. The response collapses the first three into ONE status and ONE sentence
/// so that a caller cannot probe which condition it hit; the log record distinguishes them, because
/// "this deployment has no anchor" and "this certificate does not chain to it" send an operator to two
/// different places.
/// </remarks>
internal enum ClientCertificateTrustState
{
    /// <summary>No certificate was presented on the connection.</summary>
    NoCertificate,

    /// <summary>
    /// A certificate was presented, but this deployment configured no trust anchor - so there is nothing
    /// to establish it against and nothing is trusted.
    /// </summary>
    NoTrustAnchorConfigured,

    /// <summary>
    /// A certificate was presented and this deployment has an anchor, but the certificate failed to
    /// establish itself against it - by chain, validity, revocation or key usage.
    /// </summary>
    Untrusted,

    /// <summary>The certificate established itself. Its identity may be read and reconciled.</summary>
    Trusted,
}

/// <summary>
/// Establishes whether a caller certificate may have its identity honoured by the token operation.
/// </summary>
/// <remarks>
/// <para>
/// A SINGLETON THAT LOADS ITS ANCHOR ONCE, AT CONSTRUCTION. The composition root resolves it eagerly at
/// startup, so a configured-but-unreadable anchor refuses the host rather than surfacing on the first
/// issuance request - the same shape, and for the same reason, as the signing material and the
/// caller-side mutual-TLS identities in the two services that call this one.
/// </para>
/// <para>
/// THE CHAIN IS REBUILT PER EVALUATION AND NOTHING IS CACHED ACROSS CALLS. A chain object carries
/// per-evaluation state and its own element collection, and reusing one would make two concurrent
/// evaluations observe each other. The anchor collection is immutable after construction and is the only
/// state this type holds.
/// </para>
/// </remarks>
internal sealed class ClientCertificateTrust : IDisposable
{
    /// <summary>
    /// The client-authentication extended key usage identifier, as the specification assigns it.
    /// </summary>
    /// <remarks>
    /// Written as the identifier rather than resolved from a friendly name, because a friendly name is
    /// localized by the platform on some hosts and an identifier is not.
    /// </remarks>
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>The any-purpose extended key usage identifier.</summary>
    /// <remarks>
    /// A certificate declaring this is unrestricted, so it satisfies the client-authentication
    /// requirement without naming it. Honoured because refusing it would be a rule invented here.
    /// </remarks>
    private const string AnyPurposeOid = "2.5.29.37.0";

    private readonly X509Certificate2Collection _anchors;
    private readonly X509RevocationMode _revocationMode;

    /// <summary>
    /// The longest declared validity window a caller certificate may carry, or <see langword="null"/>
    /// when revocation is being checked and the ceiling therefore does not apply.
    /// </summary>
    /// <remarks>
    /// NULL IS THE "NOT APPLICABLE" STATE RATHER THAN AN UNLIMITED ONE, and the distinction is worth the
    /// nullable: a deployment that selected a real revocation posture has a PKI that can withdraw a
    /// certificate, so the ceiling has nothing left to compensate for and enforcing it there would be a
    /// lifetime policy invented in this file. Resolved once at construction so the two settings are read
    /// together exactly once.
    /// </remarks>
    private readonly TimeSpan? _maximumLifetime;

    private readonly TimeProvider _clock;
    private readonly ILogger<ClientCertificateTrust> _logger;

    /// <summary>
    /// Loads the configured trust anchor, refusing to construct when a configured path cannot be read.
    /// </summary>
    /// <param name="options">The bound configuration contract.</param>
    /// <param name="clock">The clock seam the validity window is measured against.</param>
    /// <param name="logger">The operator channel a refusal is recorded on.</param>
    /// <exception cref="ArgumentNullException">A collaborator is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A trust anchor path is configured but cannot be read, or holds no certificate.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THE THREE FAILURE MESSAGES NAME THE CONFIGURATION KEY AND NEVER THE PATH. A path is deployment
    /// topology and belongs in a mount definition rather than in a log line that is shipped and retained;
    /// naming the key sends an operator to the setting that is wrong, which is the actionable half.
    /// </para>
    /// <para>
    /// AN UNSET PATH CONSTRUCTS SUCCESSFULLY AND TRUSTS NOTHING. That is the fail-closed state, and it is
    /// recorded once at startup at warning level so that a deployment which meant to configure mutual TLS
    /// and did not learns about it before its first caller does, rather than from a stream of
    /// indistinguishable 401s. The record is scoped to the CERTIFICATE credential: C-01 accepts a Basic
    /// credential from the roster as well, and that half mints normally with no anchor configured, so a
    /// record claiming issuance refuses everything would send an operator after an outage that is not
    /// happening.
    /// </para>
    /// </remarks>
    public ClientCertificateTrust(
        IOptions<SecurityOptions> options,
        TimeProvider clock,
        ILogger<ClientCertificateTrust> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        SecurityOptions configured = options.Value;

        _clock = clock;
        _logger = logger;
        _revocationMode = ResolveRevocationMode(configured.ClientCertificateRevocationMode);

        // THE COMPENSATING CONTROL, ARMED ONLY WHEN IT COMPENSATES FOR SOMETHING. With no revocation
        // check there is no way to withdraw a certificate, so its declared validity window is the only
        // bound on a stolen one and the configured ceiling is enforced; with a check in place the
        // deployment can withdraw one, and imposing a lifetime rule would be this file inventing policy.
        _maximumLifetime = _revocationMode == X509RevocationMode.NoCheck
            ? TimeSpan.FromDays(configured.MaxCallerCertificateLifetimeDays)
            : null;

        _anchors = LoadAnchors(configured.ClientCertificateAuthorityPath);

        if (_anchors.Count == 0)
        {
            // 🔴 QUALIFIED TO THE CREDENTIAL IT ACTUALLY AFFECTS, WHICH IS ONE OF TWO. Saying that issuance
            // "will refuse every request" here would be false, and measurably so: contract C-01 accepts
            // EITHER of two caller credentials on POST /v1/tokens - an HTTP Basic secret from the roster, or
            // a client certificate - and the Basic half is untouched by a missing anchor and keeps minting
            // throughout. An operator reading an unqualified outage sentence would go looking for a total
            // outage that is not happening, and might restart or roll back a service whose primary
            // credential path is working.
            //
            // BOTH CONFIGURATION KEYS ARE NAMED, because either one may supply this anchor: the
            // issuance key is authoritative when set, and the composition root adopts the listener's
            // published variable when it is not. Naming only one would send an operator to the key that is
            // not the one their deployment uses.
            _logger.LogWarning(
                "No client-certificate trust anchor is configured, so no caller certificate can "
                + "establish an identity and CERTIFICATE-BASED token issuance is unavailable. Issuance "
                + "by HTTP Basic credential from the configured roster is unaffected and continues to "
                + "mint. Configure Security:ClientCertificateAuthorityPath, or the deployment variable "
                + "that supplies Security:MutualTls:ClientCaPath, to enable the certificate credential. "
                + "This is a fail-closed state, not a fault: every other route on this service remains "
                + "available.");
        }
    }

    /// <summary>Whether this deployment configured a trust anchor at all.</summary>
    /// <remarks>
    /// Exposed for the composition root's startup report and for a test to assert the fail-closed state
    /// directly. It reports a COUNT-DERIVED BOOLEAN and never the anchors themselves.
    /// </remarks>
    internal bool HasTrustAnchor => _anchors.Count > 0;

    /// <summary>
    /// Establishes whether a presented certificate's identity may be honoured.
    /// </summary>
    /// <param name="certificate">
    /// The certificate the connection carries, or <see langword="null"/> when none was presented.
    /// </param>
    /// <returns>The state that describes the outcome.</returns>
    /// <remarks>
    /// <para>
    /// THE ORDER OF THE GATES IS THE CHEAPEST DECIDABLE ONE FIRST, and that ordering carries no
    /// information to a caller because every non-trusted state produces the same response. It matters
    /// only for cost: a request with no certificate, and every request to a deployment with no anchor,
    /// is answered without building a chain at all.
    /// </para>
    /// <para>
    /// THE VALIDITY WINDOW IS ENFORCED TWICE OVER, AND BOTH ARE WANTED. The chain policy is given the
    /// injected clock as its verification time, so the platform applies the window as part of chain
    /// building; the explicit comparison afterwards is what holds if a future policy change ever ignored
    /// a time-nesting status, and it is the arm a test can drive with a moved clock without constructing
    /// a chain. Neither reports the window's bounds.
    /// </para>
    /// </remarks>
    internal ClientCertificateTrustState Evaluate(X509Certificate2? certificate)
    {
        if (certificate is null)
        {
            return ClientCertificateTrustState.NoCertificate;
        }

        if (_anchors.Count == 0)
        {
            return ClientCertificateTrustState.NoTrustAnchorConfigured;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            _logger.LogWarning(
                "A caller certificate was refused because it is outside its validity window. No part "
                + "of the certificate is recorded.");

            return ClientCertificateTrustState.Untrusted;
        }

        if (!HasClientAuthenticationUsage(certificate))
        {
            _logger.LogWarning(
                "A caller certificate was refused because its declared extended key usage does not "
                + "permit client authentication. No part of the certificate is recorded.");

            return ClientCertificateTrustState.Untrusted;
        }

        // THE LIFETIME CEILING, CHECKED BEFORE THE CHAIN IS BUILT. Ordered here for the reason the usage
        // check above is ordered where it is: a certificate this deployment will not accept on its own
        // terms should be refused before any work is spent verifying who issued it, and before any
        // network lookup a stricter revocation mode might perform. It measures the DECLARED window -
        // NotAfter minus NotBefore - which is a fixed property of the certificate, so a caller cannot
        // wait the check out; expiry itself is a different question and chain building already answers it.
        // Spelled exactly as the validity-window check above spells it, so the two cannot read the
        // certificate's dates two different ways.
        TimeSpan declaredLifetime =
            certificate.NotAfter.ToUniversalTime() - certificate.NotBefore.ToUniversalTime();

        if (_maximumLifetime is { } ceiling && declaredLifetime > ceiling)
        {
            _logger.LogWarning(
                "A caller certificate was refused because the validity window it declares is longer "
                + "than this deployment permits while revocation is not being checked. With no way to "
                + "withdraw a certificate, its lifetime is the only bound on a compromised one. Raise "
                + "Security:MaxCallerCertificateLifetimeDays, or re-issue the caller certificate with a "
                + "shorter window, or configure a revocation posture other than NoCheck - in which case "
                + "this ceiling no longer applies. No part of the certificate is recorded.");

            return ClientCertificateTrustState.Untrusted;
        }

        using X509Chain chain = new();

        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = _revocationMode;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;

        // NOT ONE VERIFICATION FLAG IS RELAXED. The default is to ignore nothing, and it is restated
        // rather than left implicit because every value other than this one weakens the check - an
        // ignored unknown revocation status or an ignored time nesting is precisely the "could not tell,
        // so allow" arm this file exists to not have.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        chain.ChainPolicy.CustomTrustStore.AddRange(_anchors);

        bool built = chain.Build(certificate);

        if (!built)
        {
            _logger.LogWarning(
                "A caller certificate was refused because it does not chain to this deployment's "
                + "configured trust anchor, or its revocation status could not be established. No part "
                + "of the certificate and no chain detail is recorded.");
        }

        // The chain's element certificates are platform-owned copies; disposing them keeps no partially
        // read certificate waiting for finalization.
        foreach (X509ChainElement element in chain.ChainElements)
        {
            element.Certificate.Dispose();
        }

        return built ? ClientCertificateTrustState.Trusted : ClientCertificateTrustState.Untrusted;
    }

    /// <summary>Releases the loaded anchors.</summary>
    public void Dispose()
    {
        foreach (X509Certificate2 anchor in _anchors)
        {
            anchor.Dispose();
        }

        _anchors.Clear();
    }

    /// <summary>
    /// Reports whether a certificate's declared extended key usage permits client authentication.
    /// </summary>
    /// <param name="certificate">The presented certificate.</param>
    /// <returns><see langword="true"/> when client authentication is permitted.</returns>
    /// <remarks>
    /// A CERTIFICATE DECLARING NO EXTENDED KEY USAGE AT ALL IS UNRESTRICTED, which is the specification's
    /// own reading and is not a leniency invented here: the extension exists to NARROW a certificate's
    /// purposes, so its absence narrows nothing. It also happens to be what the repository's own
    /// generation recipe produces, so requiring the extension would refuse the certificates this
    /// project's documentation tells a developer to create.
    /// <para>
    /// Where the extension IS present it is honoured exactly: client authentication, or the any-purpose
    /// identifier which subsumes it. A certificate declaring only server authentication is refused, which
    /// is the case worth having - a service's own listener certificate is not a caller credential.
    /// </para>
    /// </remarks>
    private static bool HasClientAuthenticationUsage(X509Certificate2 certificate)
    {
        bool declaresAny = false;

        foreach (X509Extension extension in certificate.Extensions)
        {
            if (extension is not X509EnhancedKeyUsageExtension usage)
            {
                continue;
            }

            declaresAny = true;

            foreach (Oid oid in usage.EnhancedKeyUsages)
            {
                if (string.Equals(oid.Value, ClientAuthenticationOid, StringComparison.Ordinal) ||
                    string.Equals(oid.Value, AnyPurposeOid, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return !declaresAny;
    }

    /// <summary>
    /// Translates the configured revocation mode name onto the platform's own enumeration.
    /// </summary>
    /// <param name="configured">The configured name.</param>
    /// <returns>The platform mode.</returns>
    /// <exception cref="InvalidOperationException">The name is not one this service implements.</exception>
    /// <remarks>
    /// THE UNRECOGNISED ARM THROWS RATHER THAN DEFAULTING, even though the options validator already
    /// refuses the same value and therefore reaches this first in a hosted run. Defaulting would make a
    /// typo silently select the weakest mode, and a security setting that degrades quietly when
    /// misspelled is worse than one that refuses to start. The two checks are independent on purpose: a
    /// unit test constructing this type directly bypasses the validator entirely.
    /// </remarks>
    private static X509RevocationMode ResolveRevocationMode(string configured) => configured switch
    {
        _ when string.Equals(configured, ClientCertificateRevocationModes.NoCheck, StringComparison.OrdinalIgnoreCase)
            => X509RevocationMode.NoCheck,
        _ when string.Equals(configured, ClientCertificateRevocationModes.Offline, StringComparison.OrdinalIgnoreCase)
            => X509RevocationMode.Offline,
        _ when string.Equals(configured, ClientCertificateRevocationModes.Online, StringComparison.OrdinalIgnoreCase)
            => X509RevocationMode.Online,
        _ => throw new InvalidOperationException(
            "Configuration key 'Security:ClientCertificateRevocationMode' names a revocation mode this "
            + "service does not implement. The recognised values are "
            + string.Join(", ", ClientCertificateRevocationModes.Recognised)
            + ", compared without regard to case. This message never echoes the configured value."),
    };

    /// <summary>
    /// Reads the configured trust anchor file, or answers an empty collection when none is configured.
    /// </summary>
    /// <param name="path">The configured path, which may be empty.</param>
    /// <returns>The anchors, which the caller owns.</returns>
    /// <exception cref="InvalidOperationException">
    /// A path is configured but cannot be read, or holds no certificate.
    /// </exception>
    /// <remarks>
    /// <para>
    /// PEM ONLY, AND THAT IS THE SAME SECRETS CONTROL THE LISTENER'S CERTIFICATE USES (C-F). A PKCS#12
    /// bundle needs a password, and a password is exactly the material this service's configuration may
    /// never carry; the surest way not to acquire one is to have nowhere to put it. The platform's
    /// PEM-collection import reads a file holding one anchor or a whole chain of them, which is what lets
    /// a deployment rotate by appending the successor before retiring the predecessor.
    /// </para>
    /// <para>
    /// AN EMPTY FILE IS A FAULT AND NOT AN UNSET ANCHOR. A deployment that mounted a file expected it to
    /// carry an authority, and treating an empty one as "configure nothing" would convert a mount that
    /// silently failed into a service that silently refuses every caller.
    /// </para>
    /// </remarks>
    private static X509Certificate2Collection LoadAnchors(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return [];
        }

        X509Certificate2Collection anchors = [];

        try
        {
            anchors.ImportFromPemFile(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or CryptographicException
            or ArgumentException
            or NotSupportedException)
        {
            throw new InvalidOperationException(
                "The file named by configuration key 'Security:ClientCertificateAuthorityPath' could "
                + "not be read as a PEM certificate collection. Point it at a readable PEM file holding "
                + "the issuing certificate authority, or leave it entirely unset to run without a "
                + "client trust anchor. This message never echoes the configured path.",
                exception);
        }

        if (anchors.Count == 0)
        {
            throw new InvalidOperationException(
                "The file named by configuration key 'Security:ClientCertificateAuthorityPath' was "
                + "read but holds no certificate. A mounted anchor file carrying nothing would leave "
                + "this service refusing every caller with no indication why. This message never "
                + "echoes the configured path.");
        }

        return anchors;
    }
}
