// ==================================================================================================
//  ClientCertificateTrustTests.cs - THE PUBLISHED 401 FOR AN UNTRUSTED CERTIFICATE IS A BEHAVIOUR
//  ------------------------------------------------------------------------------------------------
//  WHAT THESE ROWS PROTECT
//  shared/PowerFramework.Contracts/OpenApi/security.v1.yaml declares a 401 on POST /v1/tokens whose
//  meaning is "no client certificate was presented, OR the certificate presented is not trusted", and
//  docs/ARCHITECTURE.md carries the same table. Without an explicit anchor the second half of that
//  sentence would be a promise about the CONTAINER'S OS TRUST STORE rather than about anything this service
//  does - the operation would read the connection's certificate and its common name and honour the name.
//  These rows are what make it a behaviour of this service.
//
//  WHY AN EXPLICIT ANCHOR RATHER THAN THE MACHINE STORE, ASSERTED RATHER THAN ARGUED
//  A .NET base image already trusts every public root shipped in it. With the anchor left implicit, the
//  set of issuers able to mint a caller identity for this system is as wide as the public web PKI,
//  stated nowhere and under nobody's control - and, decisively for a test suite, INDISTINGUISHABLE from
//  a correct configuration: no in-process observation separates "trusts our authority" from "trusts
//  everything". Naming the anchor makes the boundary narrow, explicit, and exactly what the row below
//  called AnUntrustedCertificateIsRefused measures.
//
//  THE THREE LEVELS THESE ROWS WORK AT, AND WHY EACH IS NEEDED
//    * The trust layer directly, because it is the type that decides and its four states are worth
//      pinning one at a time.
//    * A booted host, because the property that actually matters is that the ISSUANCE OPERATION
//      consults the decision before it reads the certificate's name - which no unit test of either
//      part can observe.
//    * Startup, because a configured-but-unreadable anchor must refuse the host rather than surface on
//      the first token request, and because the fail-closed no-anchor posture must NOT refuse it.
//
//  EVERY REFUSAL ROW HAS A POSITIVE COUNTERPART. A gate that refused everything would satisfy every
//  refusal here while making the system unable to obtain a single token, which is a worse outcome than
//  the defect being fixed.
// ==================================================================================================

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerFramework.Security.Configuration;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The trust layer answers each of its four states for the condition that produces it.
/// </summary>
/// <remarks>
/// Driven directly, with no host, because the decision is a pure function of a certificate, an anchor
/// and a clock - all three injectable. The host-level suite below asserts the different property that
/// the operation consults this decision at all.
/// </remarks>
public sealed class ClientCertificateTrustDecisionTests
{
    /// <summary>An absent certificate is the first state, and no chain is built for it.</summary>
    [Fact]
    public void NoCertificateIsItsOwnState()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);

        Assert.Equal(ClientCertificateTrustState.NoCertificate, trust.Evaluate(certificate: null));
    }

    /// <summary>
    /// With no anchor configured, NOTHING is trusted - including a certificate that would otherwise be.
    /// </summary>
    /// <remarks>
    /// THE FAIL-CLOSED DIRECTION, AND THE ROW THAT PROVES WHICH WAY IT FAILS. A deployment that never
    /// configured an authority has stated no issuer it trusts, and the only answer consistent with that
    /// is to trust none - the opposite mistake, treating "nothing configured" as "accept anything", is
    /// the exact shape of an authentication bypass.
    /// </remarks>
    [Fact]
    public void WithNoAnchorNothingIsTrusted()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: false);
        using X509Certificate2 issued = IssuanceFixture.CreateCallerCertificate("powerframework-gateway");

        Assert.False(trust.HasTrustAnchor);
        Assert.Equal(ClientCertificateTrustState.NoTrustAnchorConfigured, trust.Evaluate(issued));
    }

    /// <summary>
    /// THE POSITIVE ARM: a certificate issued by the configured authority establishes itself.
    /// </summary>
    [Fact]
    public void ACertificateFromTheConfiguredAuthorityIsTrusted()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);
        using X509Certificate2 issued = IssuanceFixture.CreateCallerCertificate("powerframework-gateway");

        Assert.True(trust.HasTrustAnchor);
        Assert.Equal(ClientCertificateTrustState.Trusted, trust.Evaluate(issued));
    }

    /// <summary>
    /// A well-formed certificate chaining to nothing this deployment configured is untrusted.
    /// </summary>
    /// <remarks>
    /// THE CASE THE WHOLE FINDING IS ABOUT. This certificate is perfectly well formed, inside its
    /// validity window, and carries whatever common name its author chose - which is precisely what an
    /// attacker can produce unaided. Before the anchor was named, this certificate's common name was
    /// read and honoured as the caller identity.
    /// </remarks>
    [Fact]
    public void AnUntrustedCertificateIsRefused()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);
        using X509Certificate2 forged =
            IssuanceFixture.CreateUntrustedCallerCertificate("powerframework-gateway");

        Assert.Equal(ClientCertificateTrustState.Untrusted, trust.Evaluate(forged));
    }

    /// <summary>An expired certificate from the configured authority is untrusted.</summary>
    /// <remarks>
    /// ISSUED BY THE ANCHOR ON PURPOSE, so the ONLY thing wrong with it is its window. A self-signed
    /// expired certificate would be refused for two reasons and would prove neither.
    /// </remarks>
    [Fact]
    public void AnExpiredCertificateIsRefused()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);
        using X509Certificate2 expired =
            IssuanceFixture.CreateExpiredCallerCertificate("powerframework-gateway");

        Assert.Equal(ClientCertificateTrustState.Untrusted, trust.Evaluate(expired));
    }

    /// <summary>
    /// The validity window is measured against the INJECTED clock, in both directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DETERMINISM SEAM APPLIES HERE TOO, and this row is what makes that assertable. The same
    /// certificate is trusted at an instant inside its window and untrusted at one after it, with nothing
    /// changing but the clock - so a characterization run under a fixed clock judges certificates
    /// reproducibly instead of against whatever the wall clock happened to say.
    /// </para>
    /// <para>
    /// The before-window arm is included because an early clock and a late one are different code paths in
    /// the comparison, and a one-sided check would pass while accepting a not-yet-valid certificate.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheValidityWindowIsMeasuredAgainstTheInjectedClock()
    {
        DateTimeOffset instant = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

        using X509Certificate2 issued = IssuanceFixture.CreateCallerCertificateValidAt(
            "powerframework-gateway",
            instant);

        using ClientCertificateTrust inside = Trust(anchorConfigured: true, now: instant);
        using ClientCertificateTrust after = Trust(anchorConfigured: true, now: instant.AddHours(1));
        using ClientCertificateTrust before = Trust(anchorConfigured: true, now: instant.AddHours(-1));

        Assert.Equal(ClientCertificateTrustState.Trusted, inside.Evaluate(issued));
        Assert.Equal(ClientCertificateTrustState.Untrusted, after.Evaluate(issued));
        Assert.Equal(ClientCertificateTrustState.Untrusted, before.Evaluate(issued));
    }

    /// <summary>
    /// A certificate declaring server authentication only is refused, though it chains and is current.
    /// </summary>
    /// <remarks>
    /// A LISTENER'S CERTIFICATE IS NOT A CALLER CREDENTIAL. A deployment issues both from the same local
    /// authority - the repository's own generation recipe does exactly that - so without this check the
    /// service's own server certificate would be a usable caller credential for whatever identity its
    /// common name carries.
    /// </remarks>
    [Fact]
    public void AServerOnlyCertificateIsRefused()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);
        using X509Certificate2 serverOnly =
            IssuanceFixture.CreateServerOnlyCallerCertificate("powerframework-gateway");

        Assert.Equal(ClientCertificateTrustState.Untrusted, trust.Evaluate(serverOnly));
    }

    /// <summary>
    /// A certificate declaring NO extended key usage is unrestricted and is therefore trusted.
    /// </summary>
    /// <remarks>
    /// THE SPECIFICATION'S OWN READING RATHER THAN A LENIENCY, and a load-bearing one here: the extension
    /// exists to NARROW a certificate's purposes, so its absence narrows nothing - and the openssl recipe
    /// in this repository's own documentation produces leaf certificates with no extension at all.
    /// Requiring it would refuse the certificates this project tells a developer to create.
    /// </remarks>
    [Fact]
    public void ACertificateDeclaringNoUsageIsUnrestricted()
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true);
        using X509Certificate2 unrestricted =
            IssuanceFixture.CreateCallerCertificate("powerframework-gateway");

        Assert.Empty(unrestricted.Extensions.OfType<X509EnhancedKeyUsageExtension>());
        Assert.Equal(ClientCertificateTrustState.Trusted, trust.Evaluate(unrestricted));
    }

    /// <summary>Each recognised revocation mode constructs, and an unrecognised one refuses.</summary>
    /// <param name="mode">The configured mode name.</param>
    /// <remarks>
    /// <para>
    /// THE THREE MODES ARE ASSERTED TO CONSTRUCT rather than to reach a revocation source, because
    /// reaching one would make the row depend on a network and on a responder neither the test nor the
    /// deployment controls. What is assertable in process - and what a misconfiguration would break - is
    /// that a named mode is accepted and translated rather than silently ignored.
    /// </para>
    /// <para>
    /// The casing rows are deliberate: a settings file writing <c>nocheck</c> has named a mode this
    /// service implements, and refusing it would be a spelling rule invented here.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("NoCheck")]
    [InlineData("nocheck")]
    [InlineData("Offline")]
    [InlineData("OFFLINE")]
    [InlineData("Online")]
    public void EveryRecognisedRevocationModeIsAccepted(string mode)
    {
        using ClientCertificateTrust trust = Trust(anchorConfigured: true, revocationMode: mode);

        Assert.True(trust.HasTrustAnchor);
    }

    /// <summary>An unrecognised revocation mode refuses to construct rather than defaulting.</summary>
    /// <param name="mode">The configured mode name.</param>
    /// <remarks>
    /// DEFAULTING WOULD MAKE A TYPO SELECT THE WEAKEST MODE, which is the failure a security setting must
    /// not have. A blank mode is included because an unset variable is the way this setting most often
    /// arrives wrong, and it must refuse rather than fall back.
    /// </remarks>
    [Theory]
    [InlineData("Always")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnUnrecognisedRevocationModeRefusesToConstruct(string mode)
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => Trust(anchorConfigured: true, revocationMode: mode));

        Assert.Contains(
            "Security:ClientCertificateRevocationMode",
            refusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>The refusal never quotes the configured mode back.</summary>
    /// <remarks>
    /// SEPARATE FROM THE ROW ABOVE, because that row's blank cases make a non-echo assertion vacuous - an
    /// empty needle is contained in every string. This one uses a distinctive non-blank value so the
    /// assertion means something, and it is worth asserting at all because a startup diagnostic is written
    /// to a log that is shipped and retained.
    /// </remarks>
    [Fact]
    public void TheRevocationModeRefusalEchoesNothing()
    {
        const string Distinctive = "ThisIsNotARecognisedRevocationModeName";

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => Trust(anchorConfigured: true, revocationMode: Distinctive));

        Assert.DoesNotContain(Distinctive, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A configured anchor path that cannot be read refuses to construct.</summary>
    /// <remarks>
    /// The failure names the configuration key and NOT the path. A path is deployment topology and belongs
    /// in a mount definition rather than in a log line that is shipped and retained; naming the key sends
    /// an operator to the setting that is wrong, which is the actionable half.
    /// </remarks>
    [Fact]
    public void AnUnreadableAnchorRefusesToConstructWithoutEchoingItsPath()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "pfw-absent-anchor-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".crt");

        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => Trust(anchorConfigured: true, anchorPath: missing));

        Assert.Contains(
            "Security:ClientCertificateAuthorityPath",
            refusal.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(missing, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>An anchor file that holds no certificate refuses to construct.</summary>
    /// <remarks>
    /// A DEPLOYMENT THAT MOUNTED A FILE EXPECTED IT TO CARRY AN AUTHORITY. Treating an empty one as
    /// "configure nothing" would convert a mount that silently failed into a service that silently
    /// refuses every caller - a fault presenting as a policy.
    /// </remarks>
    [Fact]
    public void AnEmptyAnchorFileRefusesToConstruct()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "pfw-empty-anchor-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _ = Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "client-ca.crt");

        try
        {
            File.WriteAllText(path, string.Empty);

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => Trust(anchorConfigured: true, anchorPath: path));

            Assert.Contains(
                "Security:ClientCertificateAuthorityPath",
                refusal.Message,
                StringComparison.Ordinal);

            Assert.DoesNotContain(path, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Builds a trust layer over a chosen anchor, clock and revocation mode.</summary>
    /// <param name="anchorConfigured">Whether to point at an anchor at all.</param>
    /// <param name="now">The instant the clock reports, or <see langword="null"/> for the real one.</param>
    /// <param name="revocationMode">The configured mode name.</param>
    /// <param name="anchorPath">An anchor path to use instead of the suite's own.</param>
    /// <returns>The trust layer, which the caller owns.</returns>
    private static ClientCertificateTrust Trust(
        bool anchorConfigured,
        DateTimeOffset? now = null,
        string revocationMode = ClientCertificateRevocationModes.NoCheck,
        string? anchorPath = null)
    {
        SecurityOptions options = new()
        {
            ClientCertificateRevocationMode = revocationMode,
            ClientCertificateAuthorityPath = anchorConfigured
                ? anchorPath ?? IssuanceFixture.ClientCertificateAuthorityPath
                : string.Empty,
        };

        return new ClientCertificateTrust(
            Options.Create(options),
            now is DateTimeOffset instant ? new FixedClock(instant) : TimeProvider.System,
            NullLogger<ClientCertificateTrust>.Instance);
    }

    /// <summary>A clock that reports one instant and never advances.</summary>
    /// <remarks>
    /// Declared here rather than reused from a sibling file because those doubles are nested inside the
    /// suites that own them, and a shared double in a third place would couple three files to one another
    /// for the sake of five lines.
    /// </remarks>
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _instant;

        internal FixedClock(DateTimeOffset instant) => _instant = instant;

        public override DateTimeOffset GetUtcNow() => _instant;
    }
}

/// <summary>
/// The issuance operation consults the trust decision, and does so before it reads an identity.
/// </summary>
/// <remarks>
/// A BOOTED HOST IS THE ONLY VANTAGE POINT FOR THIS. The unit rows above prove the decision is correct;
/// these prove the operation asks for it - which is the property that would silently disappear if a
/// future change reordered the handler, and the property the published <c>401</c> depends on.
/// </remarks>
public sealed class ClientCertificateTrustOperationTests
{
    /// <summary>The identity every row here claims, matching the certificate's common name.</summary>
    private const string Caller = IssuanceFixture.CallerIdentity;

    /// <summary>
    /// THE POSITIVE ARM: a certificate the configured authority issued mints a token.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// First in the file deliberately. Every row below asserts a refusal, and a gate that refused every
    /// certificate would satisfy all of them while leaving the system unable to obtain a single token.
    /// </remarks>
    [Fact]
    public async Task ATrustedCertificateMintsATokenAsync()
    {
        using X509Certificate2 trusted = IssuanceFixture.CreateCallerCertificate(Caller);

        Assert.Equal(HttpStatusCode.OK, await IssueAsync(trusted));
    }

    /// <summary>
    /// A certificate chaining to no configured authority is refused, however well formed it is.
    /// </summary>
    /// <param name="claimed">The identity the forged certificate claims.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// BOTH A REAL ROSTER IDENTITY AND AN INVENTED ONE, because the two would fail at different places if
    /// the trust gate were absent: the first would MINT A TOKEN for a service it is not, and the second
    /// would be refused by the subject reconciliation and so would look like a working system. Only the
    /// first row detects the defect, and only having both proves the refusal is about trust rather than
    /// about the name.
    /// </remarks>
    [Theory]
    [InlineData(Caller)]
    [InlineData("powerframework-elsewhere")]
    public async Task AnUntrustedCertificateMintsNothingAsync(string claimed)
    {
        using X509Certificate2 forged = IssuanceFixture.CreateUntrustedCallerCertificate(claimed);

        Assert.Equal(HttpStatusCode.Unauthorized, await IssueAsync(forged, claimed));
    }

    /// <summary>An expired certificate from the configured authority mints nothing.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AnExpiredCertificateMintsNothingAsync()
    {
        using X509Certificate2 expired = IssuanceFixture.CreateExpiredCallerCertificate(Caller);

        Assert.Equal(HttpStatusCode.Unauthorized, await IssueAsync(expired));
    }

    /// <summary>A server-authentication-only certificate mints nothing.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AServerOnlyCertificateMintsNothingAsync()
    {
        using X509Certificate2 serverOnly = IssuanceFixture.CreateServerOnlyCallerCertificate(Caller);

        Assert.Equal(HttpStatusCode.Unauthorized, await IssueAsync(serverOnly));
    }

    /// <summary>
    /// An untrusted certificate and a trusted one that establishes no identity answer identically.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE RESPONSE IS NOT A PROBE, AND THIS IS THE ROW THAT KEEPS IT THAT WAY. Both conditions are
    /// answered by the OPERATION, and a body that differed between them would let a caller discover
    /// whether its certificate chains to this deployment's configured authority - a fact about the
    /// deployment that nobody outside it needs. Compared as bodies rather than as statuses, because a
    /// differing sentence is exactly the leak a status comparison misses.
    /// </para>
    /// <para>
    /// AN ABSENT CERTIFICATE IS DELIBERATELY NOT IN THIS COMPARISON, and the reason is a layer rather
    /// than a leniency: that request never reaches the operation at all - the route's own authorization
    /// policy refuses it first, and the resulting body is the framework's generic challenge shaped by the
    /// problem-details service. Both carry the same 401 and the same return code, which is what the
    /// contract publishes; only the sentence differs, and it differs because a different layer wrote it.
    /// Measured rather than assumed: the absent arm is asserted below to be a 401 of its own.
    /// </para>
    /// <para>
    /// THE TRACE IDENTIFIER IS EXCLUDED FROM THE COMPARISON because the problem-details service stamps a
    /// fresh one per request, so any whole-body comparison across two requests would differ on it alone
    /// and prove nothing. Everything else is compared verbatim.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUntrustedCertificateAndAnUnusableOneAnswerIdenticallyAsync()
    {
        using X509Certificate2 forged = IssuanceFixture.CreateUntrustedCallerCertificate(Caller);
        using X509Certificate2 nameless = IssuanceFixture.CreateCertificateWithoutCommonName();

        string untrusted = await IssueBodyAsync(forged);
        string unusable = await IssueBodyAsync(nameless);

        Assert.Equal(untrusted, unusable, StringComparer.Ordinal);

        // THE SENTENCE IS PRESENT RATHER THAN MERELY EQUAL, so the row cannot pass on two empty bodies.
        //
        // The fragment asserted is the OPENING of the operation's own refusal detail, and it is worded for
        // a credential rather than for a certificate on purpose: this operation accepts a shared secret as
        // an HTTP Basic credential as well as a client certificate, and the refusal folds every way of
        // failing either into ONE sentence - which is the disclosure property the whole row is about. A
        // certificate-specific refusal sentence would itself tell an unauthenticated caller which scheme
        // it had been judged under, which is exactly what the shared sentence withholds.
        Assert.Contains(
            "No usable caller credential was presented",
            untrusted,
            StringComparison.Ordinal);
    }

    /// <summary>An absent certificate is still refused with the published status.</summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// The companion to the comparison above: the arm excluded from it is asserted on its own terms, so
    /// that excluding it cannot hide a regression in which an absent certificate stopped being refused.
    /// </remarks>
    [Fact]
    public async Task AnAbsentCertificateMintsNothingAsync() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await IssueAsync(certificate: null));

    /// <summary>Posts one issuance request and reports the status.</summary>
    /// <param name="certificate">The certificate the connection carries.</param>
    /// <param name="subject">The claimed subject, defaulting to the roster caller.</param>
    /// <returns>The status.</returns>
    private static async Task<HttpStatusCode> IssueAsync(
        X509Certificate2? certificate,
        string subject = Caller)
    {
        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(subject: subject, audience: Caller),
            TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    /// <summary>
    /// Posts one issuance request and reports the refusal body with its trace identifier removed.
    /// </summary>
    /// <param name="certificate">The certificate the connection carries, or none.</param>
    /// <returns>The body, less the per-request trace member.</returns>
    /// <remarks>
    /// THE TRACE MEMBER IS THE ONLY THING REMOVED, and it is removed because the problem-details service
    /// stamps a fresh one per request - so comparing two whole bodies would differ on it alone and prove
    /// nothing. Removing it by REBUILDING the object from its remaining members, rather than by editing
    /// the text, means a member added later is compared rather than silently skipped.
    /// </remarks>
    private static async Task<string> IssueBodyAsync(X509Certificate2? certificate)
    {
        await using IssuanceHostFactory factory = new(certificate);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            new Uri(IssuanceFixture.IssuancePath, UriKind.Relative),
            IssuanceFixture.Body(subject: Caller, audience: Caller),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(payload);

        return string.Join(
            '\n',
            document.RootElement.EnumerateObject()
                .Where(static member => !string.Equals(member.Name, "traceId", StringComparison.Ordinal))
                .Select(static member => member.Name + '=' + member.Value.ToString())
                .Order(StringComparer.Ordinal));
    }
}

/// <summary>
/// The client-trust configuration is validated at startup, and the fail-closed posture starts.
/// </summary>
/// <remarks>
/// Two opposite properties, and both matter. A configured anchor that cannot be read must REFUSE the
/// host, because the deployment declared which authority may mint caller identities and this service
/// cannot read it, so every issuance request it will ever receive is already doomed. An UNSET anchor must
/// NOT refuse the host, because a deployment that does not issue tokens still serves the key set, the
/// discovery document, the C-02 operations and the readiness probe the other three services gate on.
/// </remarks>
public sealed class ClientCertificateTrustStartupTests
{
    /// <summary>An unreadable anchor path stops the host, without echoing the path.</summary>
    /// <returns>A task representing the assertion.</returns>
    [Fact]
    public async Task AnUnreadableAnchorStopsTheHostAsync()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            "pfw-absent-anchor-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".crt");

        await using SecurityAppFactory factory = new()
        {
            ShapeOptions = options => options.ClientCertificateAuthorityPath = missing,
        };

        InvalidOperationException refusal =
            Assert.Throws<InvalidOperationException>(() => factory.ResolveSecurityOptions());

        Assert.Contains(
            "Security:ClientCertificateAuthorityPath",
            refusal.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(missing, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host with NO anchor starts, serves everything else, and issues nothing.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE FAIL-CLOSED POSTURE IN FULL, AND ITS COST STATED EXACTLY. Issuance refuses every caller -
    /// including one presenting a certificate that any anchored host would honour - while the anonymous
    /// readiness probe still answers, so the compose health condition the other three services wait on is
    /// unaffected. A row asserting only the refusal would not distinguish this from a host that failed to
    /// start at all.
    /// </remarks>
    [Fact]
    public async Task AHostWithNoAnchorStartsAndIssuesNothingAsync()
    {
        using X509Certificate2 trusted = IssuanceFixture.CreateCallerCertificate(
            IssuanceFixture.CallerIdentity);

        await using SecurityAppFactory factory = new()
        {
            ShapeOptions = options => options.ClientCertificateAuthorityPath = string.Empty,
        };

        // Starting the host is itself half the assertion: an unset anchor is a posture, not a fault.
        _ = factory.ResolveSecurityOptions();

        ClientCertificateTrust trust = factory.Services.GetRequiredService<ClientCertificateTrust>();

        Assert.False(trust.HasTrustAnchor);
        Assert.Equal(ClientCertificateTrustState.NoTrustAnchorConfigured, trust.Evaluate(trusted));

        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage health = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    /// <summary>
    /// A white-space anchor path is a populated setting carrying nothing, and is refused.
    /// </summary>
    /// <remarks>
    /// The shape a substitution that expanded to nothing produces. Silently treating it as unset would
    /// turn a deployment that INTENDED mutual TLS into one that quietly refuses every caller, which is a
    /// fault presenting as a policy. Asserted against the validator directly, because the options
    /// validator is the layer that owns a value-decidable rule.
    /// </remarks>
    [Fact]
    public void AWhiteSpaceAnchorPathIsRefusedByTheValidator()
    {
        SecurityOptions options = Bootable();
        options.ClientCertificateAuthorityPath = "   ";

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "Security:ClientCertificateAuthorityPath",
                StringComparison.Ordinal));
    }

    /// <summary>An unrecognised revocation mode is refused by the validator, naming the key.</summary>
    [Fact]
    public void AnUnrecognisedRevocationModeIsRefusedByTheValidator()
    {
        SecurityOptions options = Bootable();
        options.ClientCertificateRevocationMode = "Whenever";

        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.NotNull(result.Failures);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(
                "Security:ClientCertificateRevocationMode",
                StringComparison.Ordinal));

        Assert.DoesNotContain(
            result.Failures,
            failure => failure.Contains("Whenever", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE POSITIVE ARM OF THE VALIDATOR: an UNSET anchor and the default mode raise no failure.
    /// </summary>
    /// <remarks>
    /// Without this row the two above would pass against a validator that refused every configuration,
    /// and the fail-closed posture would be indistinguishable from an unstartable service.
    /// </remarks>
    [Fact]
    public void AnUnsetAnchorRaisesNoFailure()
    {
        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, Bootable());

        Assert.True(result.Succeeded);
    }

    /// <summary>Builds an options instance that is otherwise valid.</summary>
    /// <returns>The instance.</returns>
    /// <remarks>
    /// Every member other than the one under test is populated so that a row's assertion is about ITS
    /// rule: a failure list containing five unrelated entries would let a row pass on the wrong one. The
    /// signing key is generated rather than embedded, so no key literal exists in this file.
    /// </remarks>
    private static SecurityOptions Bootable()
    {
        using RSA key = RSA.Create(SecurityAppFactory.DefaultSigningKeySizeInBits);

        SecurityOptions options = new()
        {
            Issuer = "https://security.powerframework.test",
            SigningKey = key.ExportPkcs8PrivateKeyPem(),
            SigningKeyId = "powerframework-security-signing-1",
        };

        options.Audiences.Add("powerframework-gateway");

        // AN ISSUANCE-ROSTER ENTRY, BECAUSE AN EMPTY ROSTER IS ITSELF A REFUSAL. `Security:Clients` is the
        // credential directory, and this service - the sole token issuer - refuses to start with none, since
        // no caller could then authenticate while its readiness probe reported healthy. Every row here is
        // about a DIFFERENT rule, so the entry exists only to keep the failure list free of an unrelated
        // one. It names no secret key, which is the certificate-only shape a TLS-terminating deployment
        // uses and is valid on its own.
        options.Clients.Add(new SecurityClientOptions { Subject = "powerframework-gateway" });

        return options;
    }
}
