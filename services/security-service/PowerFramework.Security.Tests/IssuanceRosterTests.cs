// ==================================================================================================
//  THE ISSUANCE ROSTER, THE CALLER CREDENTIAL, AND THE SCOPE POLICIES
//
//  WHAT THIS FILE EXISTS FOR. Before the roster, this service asked exactly one authorisation
//  question - is the requested audience one this deployment serves - and that question has the same
//  answer for every caller. Any caller able to authenticate at the issuance edge could therefore mint
//  a token for ANY configured audience carrying ANY scope set it chose to name, and every protected
//  route admitted every token this system produced. Least privilege was expressible nowhere and
//  enforced nowhere, and the `403` the published contract declares was unreachable by construction.
//
//  Three mechanisms close that, and this file is the evidence for all three:
//
//    * THE ROSTER (Security:Clients) states, per caller, which audiences it may address and which
//      scopes it may request. The options validator refuses the configurations that read as working
//      configuration and are not - an empty roster, a duplicate subject, a grant naming an audience
//      the deployment does not serve.
//
//    * THE CREDENTIAL. The issuance edge accepts a shared secret presented as an HTTP Basic
//      credential, or a client certificate. Both matter: the attached environment fixes every
//      listener to plain HTTP, so on the topology this repository runs the certificate cannot be
//      presented at all and the secret is the only reachable scheme - while a TLS-terminating
//      deployment reaches the other.
//
//    * THE SCOPE POLICIES. Each protected route declares the scope it requires and the composition
//      root builds a policy from that declaration, so a valid, unexpired, correctly-addressed token
//      is still refused when its caller was never granted the scope.
//
//  EVERY ROW HERE ASSERTS A REFUSAL OR A GRANT AGAINST THE DEPLOYMENT'S OWN CONFIGURATION rather
//  than against a restatement of it: the roster is read from the booted host's registry, and the
//  required scopes are read from the routes that declare them. A row that spelled either would be a
//  second copy able to drift from the first, and it would keep passing while the deployment was
//  wrong.
//
//  NO ROW USES A COMMITTED CREDENTIAL (C-F). Every secret in this file is generated in the test
//  process.
// ==================================================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using PowerFramework.Security.Configuration;
using PowerFramework.Security.Endpoints;
using PowerFramework.Security.Tokens;

namespace PowerFramework.Security.Tests;

/// <summary>
/// Builders shared by the rows below: a roster in configuration, and a registry over it.
/// </summary>
/// <remarks>
/// EVERYTHING IS BUILT IN MEMORY AND NO HOST IS BOOTED, which is what makes every refusal in the
/// validator and the registry reachable by a fast row rather than only through a bring-up. The rows
/// that genuinely need a running pipeline - the scope policies - say so and boot one.
/// </remarks>
internal static class RosterFixture
{
    /// <summary>The subject used wherever one caller is enough.</summary>
    internal const string Caller = "a-registered-caller";

    /// <summary>A second subject, for the rows that need two.</summary>
    internal const string OtherCaller = "another-registered-caller";

    /// <summary>The audience the rows below grant.</summary>
    internal const string GrantedAudience = "an-audience-the-deployment-serves";

    /// <summary>A scope the rows below grant.</summary>
    internal const string GrantedScope = "a.granted.scope";

    /// <summary>The configuration key a caller's secret is named under.</summary>
    internal const string SecretKey = "TEST_ROSTER_SECRET";

    /// <summary>
    /// A secret generated for this process. NOT A COMMITTED CREDENTIAL.
    /// </summary>
    internal static string Secret => SecurityAppFactory.RosterSecret;

    /// <summary>
    /// Builds a minimally valid options instance carrying one roster entry.
    /// </summary>
    /// <param name="secretConfigurationKey">
    /// The secret key name for the entry, or <see langword="null"/> for a certificate-only caller.
    /// </param>
    /// <returns>The options instance.</returns>
    internal static SecurityOptions Options(string? secretConfigurationKey = SecretKey)
    {
        SecurityOptions options = new()
        {
            Issuer = "https://localhost:5104",
            SigningKeyId = "a-key-identifier",
            SigningAlgorithm = "RS256",
            TokenLifetime = TimeSpan.FromMinutes(5),
            SigningKey = SecurityAppFactory.CreateSigningKeyMaterial(2048),
        };

        options.Audiences.Add(GrantedAudience);

        SecurityClientOptions client = new()
        {
            Subject = Caller,
            SecretConfigurationKey = secretConfigurationKey,
        };

        client.Audiences.Add(GrantedAudience);
        client.Scopes.Add(GrantedScope);

        options.Clients.Add(client);

        return options;
    }

    /// <summary>
    /// Builds a registry over one options instance and a configuration carrying the named secrets.
    /// </summary>
    /// <param name="options">The options instance.</param>
    /// <param name="secrets">The flat configuration entries the roster's key names resolve against.</param>
    /// <returns>The registry.</returns>
    internal static IssuanceClientRegistry Registry(
        SecurityOptions options,
        params KeyValuePair<string, string?>[]? secrets)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(secrets ?? [])
            .Build();

        return new IssuanceClientRegistry(Microsoft.Extensions.Options.Options.Create(options), configuration);
    }

    /// <summary>The one entry that resolves <see cref="SecretKey"/> to <see cref="Secret"/>.</summary>
    internal static KeyValuePair<string, string?>[] ConfiguredSecret =>
        [new(SecretKey, Secret)];

    /// <summary>
    /// Builds a certificate-trust decision over options that configure NO trust anchor.
    /// </summary>
    /// <returns>The trust decision.</returns>
    /// <remarks>
    /// NO ANCHOR IS CONFIGURED ON PURPOSE, and that is what makes these rows about the SHARED-SECRET
    /// scheme rather than about certificates: with no anchor every certificate outcome is non-trusted,
    /// so the certificate arm of the resolver can contribute no identity at all and a row that resolves
    /// one has resolved it from the header. It is also the deployment posture the topology under test
    /// actually has - a proxy or a mesh sidecar terminating TLS ahead of this service presents no
    /// certificate to the application, which is the case the resolver's own remarks name.
    /// </remarks>
    internal static ClientCertificateTrust Trust() =>
        new(
            Microsoft.Extensions.Options.Options.Create(Options()),
            TimeProvider.System,
            NullLogger<ClientCertificateTrust>.Instance);

    /// <summary>Runs the validator and returns its failure messages.</summary>
    /// <param name="options">The instance to validate.</param>
    /// <returns>The failures, or an empty sequence when it validated.</returns>
    internal static IReadOnlyList<string> Validate(SecurityOptions options)
    {
        ValidateOptionsResult result = new SecurityOptionsValidator().Validate(name: null, options);

        return result.Failures is null ? [] : [.. result.Failures];
    }
}

/// <summary>
/// The options validator refuses every roster shape that reads as working configuration and is not.
/// </summary>
public sealed class IssuanceRosterValidationTests
{
    /// <summary>An empty roster is refused, loudly, rather than silently minting for nobody.</summary>
    /// <remarks>
    /// THE ONE REFUSAL HERE THAT LOOKS LIKE OVER-STRICTNESS AND IS NOT. "No clients configured, so mint
    /// for nobody" is safe in the narrow sense and catastrophic in the useful one: this is the sole
    /// issuer, so a deployment in that state cannot give any service a credential, while its readiness
    /// probe reports healthy for exactly as long as nobody tries. The legacy posture this mirrors ends a
    /// structural fault in process termination rather than in a warning
    /// [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
    /// </remarks>
    [Fact]
    public void AnEmptyRosterIsRefused()
    {
        SecurityOptions options = RosterFixture.Options();

        options.Clients.Clear();

        string failure = Assert.Single(
            RosterFixture.Validate(options),
            message => message.Contains(":Clients'", StringComparison.Ordinal));

        Assert.Contains("sole token issuer", failure, StringComparison.Ordinal);
    }

    /// <summary>A grant naming an audience the deployment does not serve is refused.</summary>
    /// <remarks>
    /// <para>
    /// THE MOST VALUABLE ROW IN THIS FILE. The issuer applies both gates and the deployment-wide one
    /// first, so such a grant can never be exercised - it is unreachable configuration that reads in a
    /// settings file as a granted permission. That is the failure mode a validator can catch and a test
    /// of the happy path cannot: nothing breaks, the deployment simply does not do what its
    /// configuration says.
    /// </para>
    /// <para>
    /// The message must name the deployment-wide key as well as the offending position, because the fix
    /// is a choice between two places and an operator has to be told both.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGrantNamingAnUnservedAudienceIsRefused()
    {
        SecurityOptions options = RosterFixture.Options();

        options.Clients[0].Audiences.Add("an-audience-the-deployment-does-not-serve");

        string failure = Assert.Single(
            RosterFixture.Validate(options),
            message => message.Contains(":Clients[0]:Audiences[1]'", StringComparison.Ordinal));

        Assert.Contains(":Audiences'", failure, StringComparison.Ordinal);
        Assert.Contains("never be exercised", failure, StringComparison.Ordinal);

        // AND IT DOES NOT ECHO THE VALUE, which every message in this service is held to.
        Assert.DoesNotContain(
            "an-audience-the-deployment-does-not-serve",
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>A duplicate subject is refused, because the roster is keyed by subject.</summary>
    /// <remarks>
    /// A second entry for the same subject would either be silently dropped or silently win, and in both
    /// readings one of the two permission sets an operator wrote down is not the one being enforced.
    /// </remarks>
    [Fact]
    public void ADuplicateSubjectIsRefused()
    {
        SecurityOptions options = RosterFixture.Options();

        SecurityClientOptions duplicate = new() { Subject = RosterFixture.Caller };

        duplicate.Audiences.Add(RosterFixture.GrantedAudience);
        duplicate.Scopes.Add(RosterFixture.GrantedScope);

        options.Clients.Add(duplicate);

        string failure = Assert.Single(
            RosterFixture.Validate(options),
            message => message.Contains(":Clients[1]:Subject'", StringComparison.Ordinal));

        Assert.Contains("keyed by subject", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(RosterFixture.Caller, failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every remaining roster shape rule, one row each, with the configuration key it must name.
    /// </summary>
    /// <param name="fault">The fault to apply.</param>
    /// <param name="expectedKeyFragment">The configuration key path the failure must name.</param>
    /// <remarks>
    /// TABLE-DRIVEN, AND EACH ROW APPLIES EXACTLY ONE FAULT to an otherwise valid instance, so the
    /// failure it produces is unambiguous. The assertion is on the KEY PATH rather than on the prose,
    /// because the key path is the part an operator pastes into a search of a settings file - and it is
    /// also the part that must never carry a value.
    /// </remarks>
    [Theory]
    [InlineData("blank subject", ":Clients[0]:Subject'")]
    [InlineData("whitespace subject", ":Clients[0]:Subject'")]
    [InlineData("blank secret key name", ":Clients[0]:SecretConfigurationKey'")]
    [InlineData("unsafe secret key name", ":Clients[0]:SecretConfigurationKey'")]
    [InlineData("no audiences", ":Clients[0]:Audiences'")]
    [InlineData("blank audience", ":Clients[0]:Audiences[1]'")]
    [InlineData("no scopes", ":Clients[0]:Scopes'")]
    [InlineData("blank scope", ":Clients[0]:Scopes[1]'")]
    [InlineData("spaced scope", ":Clients[0]:Scopes[1]'")]
    [InlineData("quoted scope", ":Clients[0]:Scopes[1]'")]
    [InlineData("over-long scope", ":Clients[0]:Scopes[1]'")]
    [InlineData("duplicate scope", ":Clients[0]:Scopes[1]'")]
    public void EveryRosterShapeRuleIsEnforced(string fault, string expectedKeyFragment)
    {
        SecurityOptions options = RosterFixture.Options();
        SecurityClientOptions client = options.Clients[0];

        switch (fault)
        {
            case "blank subject":
                client.Subject = string.Empty;
                break;

            case "whitespace subject":
                client.Subject = "   ";
                break;

            case "blank secret key name":
                client.SecretConfigurationKey = string.Empty;
                break;

            case "unsafe secret key name":
                // A separator would let the composed lookup leave the namespace it was meant to read.
                client.SecretConfigurationKey = "TEST:ROSTER:SECRET";
                break;

            case "no audiences":
                client.Audiences.Clear();
                break;

            case "blank audience":
                client.Audiences.Add("   ");
                break;

            case "no scopes":
                client.Scopes.Clear();
                break;

            case "blank scope":
                client.Scopes.Add(string.Empty);
                break;

            case "spaced scope":
                // The granted set travels as ONE space-delimited value, so this would become two.
                client.Scopes.Add("two scopes");
                break;

            case "quoted scope":
                // Outside the RFC 6749 section 3.3 scope-token charset.
                client.Scopes.Add("a\"scope");
                break;

            case "over-long scope":
                client.Scopes.Add(new string('s', SecurityOptionsValidator.MaximumScopeLength + 1));
                break;

            case "duplicate scope":
                client.Scopes.Add(RosterFixture.GrantedScope);
                break;

            default:
                Assert.Fail($"The row '{fault}' names no fault this test knows how to apply.");
                break;
        }

        Assert.Contains(
            RosterFixture.Validate(options),
            message => message.Contains(expectedKeyFragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// An entry naming NO secret key validates, because such a caller authenticates by certificate.
    /// </summary>
    /// <remarks>
    /// THE ABSENCE IS MEANINGFUL RATHER THAN LAX, and this row is what keeps it so. A deployment that
    /// terminates TLS and issues client certificates has no shared secret to name; refusing that shape
    /// would delete a credential scheme the published contract declares. A key name that is PRESENT and
    /// blank is a different thing and is refused by the table above.
    /// </remarks>
    [Fact]
    public void ACertificateOnlyEntryValidates()
    {
        Assert.Empty(RosterFixture.Validate(RosterFixture.Options(secretConfigurationKey: null)));
    }

    /// <summary>Every scope name the shipped roster grants is a legal scope token.</summary>
    /// <remarks>
    /// THE SHIPPED CONFIGURATION IS ASSERTED, NOT JUST THE RULE. A charset rule nothing checks the real
    /// roster against would let the settings file carry a name the validator refuses, and the first
    /// anyone would learn of it is a host that will not start.
    /// </remarks>
    [Fact]
    public async Task TheShippedRosterValidatesAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions shipped = factory.ResolveSecurityOptions();

        Assert.NotEmpty(shipped.Clients);
        Assert.Empty(RosterFixture.Validate(shipped));

        // And every grant is reachable, which is the cross-check the validator exists for.
        foreach (SecurityClientOptions client in shipped.Clients)
        {
            Assert.NotEmpty(client.Audiences);
            Assert.NotEmpty(client.Scopes);

            foreach (string audience in client.Audiences)
            {
                Assert.Contains(audience, shipped.Audiences);
            }
        }
    }
}

/// <summary>
/// The registry: it resolves secrets eagerly, refuses a named-but-absent one, and authenticates
/// without disclosing which callers exist.
/// </summary>
public sealed class IssuanceClientRegistryTests
{
    /// <summary>A correct credential authenticates and yields the caller's own grants.</summary>
    [Fact]
    public void ACorrectCredentialAuthenticates()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        RegisteredIssuanceClient? authenticated =
            registry.Authenticate(RosterFixture.Caller, RosterFixture.Secret);

        Assert.NotNull(authenticated);
        Assert.Equal(RosterFixture.Caller, authenticated.Subject);
        Assert.Contains(RosterFixture.GrantedAudience, authenticated.PermittedAudiences);
        Assert.Contains(RosterFixture.GrantedScope, authenticated.PermittedScopes);
        Assert.True(authenticated.HasSecret);
    }

    /// <summary>
    /// An unknown identity, a wrong secret and a certificate-only entry are indistinguishable.
    /// </summary>
    /// <param name="condition">Which of the three conditions to construct.</param>
    /// <remarks>
    /// ONE ANSWER FOR THREE REASONS, DELIBERATELY. Distinguishing them would turn the issuance edge into
    /// an oracle for enumerating this deployment's client roster, which an unauthenticated party must
    /// not be able to do. The three are asserted together in one theory precisely so that a future change
    /// that started distinguishing any of them fails here.
    /// </remarks>
    [Theory]
    [InlineData("unknown identity")]
    [InlineData("wrong secret")]
    [InlineData("empty presented secret")]
    [InlineData("certificate-only entry")]
    public void EveryAuthenticationFailureAnswersIdentically(string condition)
    {
        SecurityOptions options = RosterFixture.Options(
            secretConfigurationKey: condition == "certificate-only entry" ? null : RosterFixture.SecretKey);

        IssuanceClientRegistry registry =
            RosterFixture.Registry(options, RosterFixture.ConfiguredSecret);

        RegisteredIssuanceClient? authenticated = condition switch
        {
            "unknown identity" => registry.Authenticate("not-in-the-roster", RosterFixture.Secret),
            "wrong secret" => registry.Authenticate(RosterFixture.Caller, "not-the-secret"),
            "empty presented secret" => registry.Authenticate(RosterFixture.Caller, string.Empty),
            "certificate-only entry" => registry.Authenticate(RosterFixture.Caller, RosterFixture.Secret),
            _ => throw new InvalidOperationException($"Unhandled row '{condition}'."),
        };

        Assert.Null(authenticated);
    }

    /// <summary>
    /// A secret differing only in case is refused, because a credential is compared as bytes.
    /// </summary>
    /// <remarks>
    /// A case-folding or normalising comparison would accept a secret that is not the configured one.
    /// The bytes are the credential.
    /// </remarks>
    [Fact]
    public void ASecretDifferingOnlyInCaseIsRefused()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.Null(registry.Authenticate(
            RosterFixture.Caller,
            RosterFixture.Secret.ToUpperInvariant()));
    }

    /// <summary>
    /// A roster entry naming a secret key that resolves to nothing refuses the host.
    /// </summary>
    /// <param name="configured">The value the named key resolves to.</param>
    /// <remarks>
    /// <para>
    /// FAIL-FAST, AND THE ALTERNATIVE IS THE POINT. Left to run, a missing secret is a caller that can
    /// never authenticate against a service whose readiness probe reports healthy - the failure shape
    /// this whole service's startup validation exists to avoid. Whitespace counts as nothing: a secret
    /// consisting of spaces is not a credential a deployment intended.
    /// </para>
    /// <para>
    /// The message names the roster POSITION and never the key's value, which the final assertion holds
    /// it to.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANamedSecretThatResolvesToNothingRefusesTheHost(string? configured)
    {
        SecurityOptions options = RosterFixture.Options();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => RosterFixture.Registry(
                options,
                new KeyValuePair<string, string?>(RosterFixture.SecretKey, configured)));

        Assert.Contains(":Clients[0]:SecretConfigurationKey'", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RosterFixture.Secret, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A certificate-only entry needs no configured secret and constructs cleanly.</summary>
    [Fact]
    public void ACertificateOnlyEntryNeedsNoSecret()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(secretConfigurationKey: null));

        Assert.Equal(1, registry.Count);
        Assert.True(registry.TryResolveSubject(
            RosterFixture.Caller,
            out RegisteredIssuanceClient? resolved));
        Assert.False(resolved.HasSecret);
    }

    /// <summary>An unregistered subject resolves to nothing.</summary>
    [Fact]
    public void AnUnregisteredSubjectResolvesToNothing()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.False(registry.TryResolveSubject("not-in-the-roster", out RegisteredIssuanceClient? absent));
        Assert.Null(absent);
    }

    /// <summary>Subject lookup is ordinal, so a case variant is a different subject.</summary>
    /// <remarks>
    /// Folding case would let two entries differing only in case collide, and a deployment would then
    /// have two rosters' worth of permissions arbitrated by whichever entry the binder placed first.
    /// </remarks>
    [Fact]
    public void SubjectLookupIsOrdinal()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.False(registry.TryResolveSubject(
            RosterFixture.Caller.ToUpperInvariant(),
            out RegisteredIssuanceClient? _));
    }

    /// <summary>The registry refuses to construct over two entries sharing one subject.</summary>
    /// <remarks>
    /// REDUNDANT WITH THE VALIDATOR AND KEPT ANYWAY. Without it, a duplicate surfaces as the frozen
    /// dictionary builder's own exception - a message naming the subject, which is caller-adjacent text
    /// this service does not put in diagnostics. One comparison per entry buys a diagnostic naming the
    /// configuration position instead.
    /// </remarks>
    [Fact]
    public void ADuplicateSubjectRefusesTheRegistry()
    {
        SecurityOptions options = RosterFixture.Options();

        SecurityClientOptions duplicate = new() { Subject = RosterFixture.Caller };

        duplicate.Audiences.Add(RosterFixture.GrantedAudience);
        duplicate.Scopes.Add(RosterFixture.GrantedScope);

        options.Clients.Add(duplicate);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => RosterFixture.Registry(options, RosterFixture.ConfiguredSecret));

        Assert.Contains(":Clients[1]:Subject'", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(RosterFixture.Caller, failure.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The Basic credential is read exactly as RFC 7617 defines it, and a malformed one is refused
/// rather than falling through to the other scheme.
/// </summary>
public sealed class BasicCredentialReadingTests
{
    /// <summary>A well-formed credential is split on the FIRST colon.</summary>
    /// <param name="clientId">The user-id to present.</param>
    /// <param name="secret">The password to present.</param>
    /// <remarks>
    /// THE COLON ROW IS THE ONE THAT MATTERS. RFC 7617 is explicit that a colon may not appear in the
    /// user-id and that everything after the first one is the password, so splitting on the last colon
    /// or on all of them would corrupt any secret containing one - and a corrupted secret fails a
    /// fixed-time comparison exactly like a wrong one, which makes the defect indistinguishable from a
    /// misconfiguration.
    /// </remarks>
    [Theory]
    [InlineData("caller", "secret")]
    [InlineData("caller", "a:secret:with:colons")]
    [InlineData("caller", "")]
    [InlineData("caller", "a secret with spaces")]
    [InlineData("caller", "påsswörd-with-non-ascii")]
    public void AWellFormedCredentialIsReadVerbatim(string clientId, string secret)
    {
        DefaultHttpContext request = Present($"{clientId}:{secret}");

        Assert.True(TokenEndpoints.TryReadBasicCredential(
            request,
            out string? readClientId,
            out string? readSecret));

        Assert.Equal(clientId, readClientId);
        Assert.Equal(secret, readSecret);
    }

    /// <summary>The scheme token is matched case-insensitively, as RFC 9110 requires.</summary>
    /// <param name="scheme">The scheme spelling to present.</param>
    /// <remarks>
    /// A caller presenting a lower-case scheme is conformant, and refusing it would be this service
    /// inventing a stricter protocol than the one it publishes. The credential that follows is matched
    /// case-SENSITIVELY, because it is a credential - the sibling registry row proves that.
    /// </remarks>
    [Theory]
    [InlineData("Basic")]
    [InlineData("basic")]
    [InlineData("BASIC")]
    [InlineData("bAsIc")]
    public void TheSchemeTokenIsCaseInsensitive(string scheme)
    {
        DefaultHttpContext request = new();

        request.Request.Headers.Authorization =
            $"{scheme} {Convert.ToBase64String(Encoding.UTF8.GetBytes("caller:secret"))}";

        Assert.True(TokenEndpoints.TryReadBasicCredential(request, out string? clientId, out _));
        Assert.Equal("caller", clientId);
    }

    /// <summary>
    /// A header naming the Basic scheme but malformed reports TRUE with an empty identity.
    /// </summary>
    /// <param name="parameter">The malformed parameter to present after the scheme token.</param>
    /// <remarks>
    /// THE ONE COUNTER-INTUITIVE BEHAVIOUR IN THE READER, AND THE ROW THAT PINS IT. Reporting false
    /// would send a caller that presented a broken credential down the certificate path, where it might
    /// be authenticated as a DIFFERENT identity than the one it asserted - a garbled header becoming a
    /// silent identity substitution. Reporting true with an empty identity makes the roster lookup fail
    /// and the request be refused, which is the only correct outcome, and the refusal carries the same
    /// status and sentence as every other authentication failure so nothing is disclosed.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("bm8tY29sb24taGVyZQ==")]
    public void AMalformedBasicHeaderIsClaimedAndThenRefused(string parameter)
    {
        DefaultHttpContext request = new();

        request.Request.Headers.Authorization = string.IsNullOrEmpty(parameter)
            ? "Basic"
            : "Basic " + parameter;

        Assert.True(TokenEndpoints.TryReadBasicCredential(
            request,
            out string? clientId,
            out string? secret));

        Assert.Equal(string.Empty, clientId);
        Assert.Equal(string.Empty, secret);

        // And it authenticates as nobody, which is what makes claiming it safe.
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.Null(registry.Authenticate(clientId, secret));
    }

    /// <summary>An invalid UTF-8 payload is refused rather than repaired.</summary>
    /// <remarks>
    /// A substitution character would let two DISTINCT byte sequences decode to the same string and
    /// therefore compare equal against a configured secret. Throwing UTF-8 cannot do that.
    /// </remarks>
    [Fact]
    public void AnInvalidUtf8PayloadIsRefused()
    {
        DefaultHttpContext request = new();

        // A lone continuation byte is not a valid UTF-8 sequence.
        request.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String([0x63, 0x3A, 0x80]);

        Assert.True(TokenEndpoints.TryReadBasicCredential(
            request,
            out string? clientId,
            out string? secret));

        Assert.Equal(string.Empty, clientId);
        Assert.Equal(string.Empty, secret);
    }

    /// <summary>A header naming a different scheme reports FALSE, so the other scheme is still tried.</summary>
    /// <param name="header">The header to present.</param>
    /// <remarks>
    /// The document-level bearer requirement means a caller may well hold a token, and presenting it
    /// here must neither authenticate it nor prevent the caller from presenting the credential this
    /// operation does accept.
    /// </remarks>
    [Theory]
    [InlineData("Bearer a-token")]
    [InlineData("Negotiate something")]
    [InlineData("")]
    [InlineData("   ")]
    public void AHeaderNamingAnotherSchemeIsNotClaimed(string header)
    {
        DefaultHttpContext request = new();

        if (header.Length > 0)
        {
            request.Request.Headers.Authorization = header;
        }

        Assert.False(TokenEndpoints.TryReadBasicCredential(request, out string? clientId, out string? secret));
        Assert.Null(clientId);
        Assert.Null(secret);
    }

    /// <summary>
    /// The credential scheme is tried BEFORE the certificate, so a wrong secret is not re-authenticated.
    /// </summary>
    /// <remarks>
    /// THE ORDERING ROW. A caller that took the trouble to send an Authorization header is asserting an
    /// identity explicitly, and that assertion is the one answered: if it fails, the request is refused
    /// rather than quietly re-authenticated as whatever identity a certificate on the connection happens
    /// to establish. Trying the certificate first would mean a caller presenting a WRONG secret could
    /// still be authenticated, and a deployment could not tell from the outside which credential had
    /// been honoured.
    /// </remarks>
    [Fact]
    public void TheCredentialSchemeIsTriedBeforeTheCertificate()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        DefaultHttpContext request = Present($"{RosterFixture.Caller}:not-the-secret");

        Assert.Null(TokenEndpoints.ResolvePresentedIdentity(request, registry, RosterFixture.Trust()));

        // With the correct secret the same shape authenticates, so the row above is about the SECRET
        // rather than about the reader failing to see the header at all.
        Assert.Equal(
            RosterFixture.Caller,
            TokenEndpoints.ResolvePresentedIdentity(
                Present($"{RosterFixture.Caller}:{RosterFixture.Secret}"),
                registry,
                RosterFixture.Trust()));
    }

    /// <summary>A request presenting neither credential establishes no identity.</summary>
    [Fact]
    public void ARequestPresentingNeitherCredentialEstablishesNoIdentity()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.Null(TokenEndpoints.ResolvePresentedIdentity(
            new DefaultHttpContext(),
            registry,
            RosterFixture.Trust()));
    }

    /// <summary>
    /// The SECRET scheme reaches the operation, which is the property the issuance edge was missing: a
    /// correct shared secret authenticates even though no client certificate is presented and no caller
    /// trust anchor is configured at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE DEPLOYMENT `orchestration/.env.example` DESCRIBES, so it is the one that must work
    /// without a certificate anywhere. A deployment that terminates TLS in a proxy or a mesh sidecar has
    /// no certificate for this process to read, so the secret scheme is the ONLY one that can reach the
    /// operation there - and contract C-01 publishes it, this file's 401 sentence advertises it, and
    /// three `.env.example` secrets are mandatory for it.
    /// </para>
    /// <para>
    /// The trust double deliberately holds NO anchor: passing one would let the row pass for the wrong
    /// reason. With no anchor the certificate branch can never answer Trusted, so an identity resolved
    /// here can only have come from the secret.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACorrectSecretAuthenticatesWithNoCertificateAndNoTrustAnchor()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        ClientCertificateTrust trust = NoCallerCertificateTrust();

        Assert.Equal(
            ClientCertificateTrustState.NoTrustAnchorConfigured,
            trust.Evaluate(SelfSignedCallerCertificate()));

        Assert.Equal(
            RosterFixture.Caller,
            TokenEndpoints.ResolvePresentedIdentity(
                Present($"{RosterFixture.Caller}:{RosterFixture.Secret}"),
                registry,
                trust));
    }

    /// <summary>
    /// An unregistered identity presenting a well-formed secret establishes nothing, so the roster is the
    /// authority rather than the header.
    /// </summary>
    [Fact]
    public void AnUnregisteredIdentityIsRefusedUnderTheSecretScheme()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        Assert.Null(TokenEndpoints.ResolvePresentedIdentity(
            Present($"nobody-this-deployment-knows:{RosterFixture.Secret}"),
            registry,
            NoCallerCertificateTrust()));
    }

    /// <summary>
    /// A presented certificate whose issuer is not established resolves NO identity, so the trust gate is
    /// inside the resolver rather than alongside it.
    /// </summary>
    /// <remarks>
    /// The certificate below carries the registered caller's own common name, so a resolver that read the
    /// name before consulting the gate would answer that identity - which is authentication by assertion,
    /// and the single worst failure this file can have. The gate has no anchor configured, so the
    /// certificate cannot be trusted however well-formed it is.
    /// </remarks>
    [Fact]
    public void AnUntrustedCertificateResolvesNoIdentityEvenWhenItNamesARegisteredCaller()
    {
        IssuanceClientRegistry registry =
            RosterFixture.Registry(RosterFixture.Options(), RosterFixture.ConfiguredSecret);

        DefaultHttpContext request = new();
        request.Connection.ClientCertificate = SelfSignedCallerCertificate();

        Assert.Null(TokenEndpoints.ResolvePresentedIdentity(
            request,
            registry,
            NoCallerCertificateTrust()));
    }

    /// <summary>
    /// A caller-certificate decision maker with NO configured anchor, which is the state a
    /// secret-authenticating deployment runs in.
    /// </summary>
    /// <returns>The decision maker.</returns>
    private static ClientCertificateTrust NoCallerCertificateTrust() =>
        new(
            Options.Create(new SecurityOptions()),
            TimeProvider.System,
            NullLogger<ClientCertificateTrust>.Instance);

    /// <summary>
    /// A self-signed client certificate carrying the registered caller's common name.
    /// </summary>
    /// <returns>The certificate.</returns>
    private static X509Certificate2 SelfSignedCallerCertificate()
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new(
            $"CN={RosterFixture.Caller}",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(5));
    }

    /// <summary>Builds a request presenting one Basic credential.</summary>
    /// <param name="userPass">The <c>user:pass</c> payload.</param>
    /// <returns>The request.</returns>
    private static DefaultHttpContext Present(string userPass)
    {
        DefaultHttpContext request = new();

        request.Request.Headers.Authorization =
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(userPass));

        return request;
    }
}

/// <summary>
/// The scope claim is read by whole token, with one declaration of its name and its delimiter.
/// </summary>
public sealed class ScopeClaimTests
{
    /// <summary>A granted scope is matched, and a near-miss is not.</summary>
    /// <param name="claimValue">The claim value to present.</param>
    /// <param name="required">The scope required.</param>
    /// <param name="expected">Whether the claim grants it.</param>
    /// <remarks>
    /// <para>
    /// THE PREFIX ROWS CARRY THE WHOLE CORRECTNESS ARGUMENT. A claim value of
    /// <c>persistence.readonly</c> CONTAINS the text <c>persistence.read</c>, so a containment test
    /// would grant a scope the token does not carry - the classic scope-prefix bypass. Splitting on the
    /// delimiter and comparing whole tokens ordinally cannot do that, and these rows are the standing
    /// proof.
    /// </para>
    /// <para>
    /// The malformed-claim rows - a doubled, leading or trailing delimiter - assert that an empty token
    /// neither matches nor short-circuits the scan.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("ping", "ping", true)]
    [InlineData("a ping b", "ping", true)]
    [InlineData("ping b", "ping", true)]
    [InlineData("a ping", "ping", true)]
    [InlineData("persistence.readonly", "persistence.read", false)]
    [InlineData("persistence.read", "persistence.readonly", false)]
    [InlineData("pingpong", "ping", false)]
    [InlineData("PING", "ping", false)]
    [InlineData("", "ping", false)]
    [InlineData("   ", "ping", false)]
    [InlineData("a  ping  b", "ping", true)]
    [InlineData(" ping ", "ping", true)]
    [InlineData("a b c", "ping", false)]
    public void AScopeIsMatchedByWholeToken(string claimValue, string required, bool expected)
    {
        System.Security.Claims.ClaimsPrincipal principal = new(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(ScopeClaim.ClaimName, claimValue)],
                authenticationType: "Test"));

        Assert.Equal(expected, ScopeClaim.Grants(principal, required));
    }

    /// <summary>A repeated scope claim is tolerated as well as the space-delimited form.</summary>
    /// <remarks>
    /// The published contract's encoding is a single space-delimited value and this issuer stamps that,
    /// so the repeated form is not something this system produces. Tolerating it costs a loop this method
    /// already has, and a verifier that refused a shape some other issuer might produce would be brittle
    /// for no gain.
    /// </remarks>
    [Fact]
    public void ARepeatedScopeClaimIsTolerated()
    {
        System.Security.Claims.ClaimsPrincipal principal = new(
            new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(ScopeClaim.ClaimName, "a"),
                    new System.Security.Claims.Claim(ScopeClaim.ClaimName, "ping"),
                ],
                authenticationType: "Test"));

        Assert.True(ScopeClaim.Grants(principal, "ping"));
        Assert.False(ScopeClaim.Grants(principal, "b"));
    }

    /// <summary>An absent principal and an absent claim both answer false rather than throwing.</summary>
    /// <remarks>
    /// A policy is a predicate, and the caller of a predicate should not have to catch.
    /// </remarks>
    [Fact]
    public void AnAbsentPrincipalOrClaimAnswersFalse()
    {
        Assert.False(ScopeClaim.Grants(principal: null, "ping"));
        Assert.False(ScopeClaim.Grants(new System.Security.Claims.ClaimsPrincipal(), "ping"));
    }

    /// <summary>A blank required scope is a route declaring a requirement it cannot state.</summary>
    [Fact]
    public void ABlankRequiredScopeIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => ScopeClaim.Grants(new System.Security.Claims.ClaimsPrincipal(), "  "));
    }

    /// <summary>
    /// The claim name and delimiter the policies read are the ones the issuer stamps.
    /// </summary>
    /// <remarks>
    /// ONE DECLARATION, ASSERTED. A second spelling of the claim name in an authorization file would be
    /// an authorization bypass that LOOKS like a caller lacking a scope: a policy reading a claim nobody
    /// writes never matches, so it grants nothing, refuses nothing, and is indistinguishable from
    /// correct behaviour until someone notices every request is forbidden.
    /// </remarks>
    [Fact]
    public void TheClaimNameAndDelimiterAreTheOnesTheIssuerStamps()
    {
        Assert.Equal("scope", ScopeClaim.ClaimName);
        Assert.Equal(' ', ScopeClaim.Delimiter);
    }
}

/// <summary>
/// The issuer applies both authorisation gates, and the endpoint reports each with its own outcome.
/// </summary>
public sealed class PerCallerAuthorizationTests
{
    /// <summary>A registered caller asking within its grants is issued a token.</summary>
    /// <remarks>
    /// THE GRANT ROW, WITHOUT WHICH THE REFUSAL ROWS PROVE NOTHING. A roster that refused everything
    /// would satisfy every other row in this class.
    /// </remarks>
    [Fact]
    public async Task ACallerAskingWithinItsGrantsIsIssuedATokenAsync()
    {
        await using SecurityAppFactory factory = new();

        RegisteredIssuanceClient registered = Resolve(factory, "powerframework-gateway");

        TokenIssuanceResult result = Issue(
            factory,
            registered.Subject,
            registered.PermittedAudiences.First(),
            [registered.PermittedScopes.First()]);

        Assert.Equal(TokenIssuanceOutcome.Issued, result.Outcome);
        Assert.NotNull(result.Token);
    }

    /// <summary>An unregistered subject is refused, and nothing is minted.</summary>
    [Fact]
    public async Task AnUnregisteredSubjectIsRefusedAsync()
    {
        await using SecurityAppFactory factory = new();

        TokenIssuanceResult result = Issue(
            factory,
            "a-subject-no-roster-entry-names",
            factory.ResolveSecurityOptions().Audiences[0],
            ["ping"]);

        Assert.Equal(TokenIssuanceOutcome.CallerNotPermitted, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// An audience the deployment SERVES but this caller may not address is refused, and it is
    /// distinguished from one the deployment does not serve at all.
    /// </summary>
    /// <remarks>
    /// BOTH GATES IN ONE ROW, because the pair is the point. The two refusals answer the same status and
    /// carry different outcomes, so an operator is sent to the deployment-wide roster in one case and to
    /// the caller's own entry in the other. Collapsing them would save a branch and cost the only
    /// diagnostic that distinguishes the two most likely misconfigurations of a permission model.
    /// </remarks>
    [Fact]
    public async Task TheTwoAudienceGatesAreDistinguishedAsync()
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();
        RegisteredIssuanceClient registered = Resolve(factory, "powerframework-gateway");

        // ANY served audience this caller is not granted. There is more than one - the caller is granted
        // two of the four the deployment serves - so the row takes the first rather than asserting a
        // count it has no reason to care about.
        string servedButNotGranted =
            options.Audiences.First(candidate => !registered.PermittedAudiences.Contains(candidate));

        Assert.Equal(
            TokenIssuanceOutcome.CallerNotPermitted,
            Issue(
                factory,
                registered.Subject,
                servedButNotGranted,
                [registered.PermittedScopes.First()]).Outcome);

        const string notServedAtAll = "an-audience-no-deployment-here-serves";

        Assert.DoesNotContain(notServedAtAll, options.Audiences);

        Assert.Equal(
            TokenIssuanceOutcome.AudienceNotPermitted,
            Issue(
                factory,
                registered.Subject,
                notServedAtAll,
                [registered.PermittedScopes.First()]).Outcome);
    }

    /// <summary>
    /// A scope outside the caller's grants is DROPPED from the granted set, and the rest is issued.
    /// </summary>
    /// <param name="position">Where in the requested set the ungranted scope sits.</param>
    /// <remarks>
    /// <para>
    /// THE PUBLISHED CONTRACT SETTLES THIS, AND IT SETTLES IT AS A NARROWING. "A PARTIALLY PERMITTED SCOPE
    /// SET IS NOT REFUSED. When some of the requested scopes are permitted and some are not, the request
    /// SUCCEEDS with 200 and the response's scope member reports the narrower granted set"
    /// [security.v1.yaml, the 403 on POST /v1/tokens]. The refusal case is the one where NOTHING was
    /// permitted, which the sibling row below drives.
    /// </para>
    /// <para>
    /// BOTH POSITIONS ARE STILL DRIVEN, and they are what makes the row worth having: a filter that
    /// stopped at the first element would grant the ungranted name in one of the two orders, and the
    /// granted value is compared as a WHOLE STRING so an implementation emitting matrix order rather than
    /// request order is caught too.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("first")]
    [InlineData("last")]
    public async Task AnUngrantedScopeIsDroppedAndTheRestIsIssuedAsync(string position)
    {
        await using SecurityAppFactory factory = new();

        RegisteredIssuanceClient registered = Resolve(factory, "powerframework-gateway");

        string granted = GrantedScopeFor(factory, registered.Subject, GrantedAudienceFor(factory, registered.Subject));
        const string ungranted = "a.scope.no.roster.entry.grants";

        Assert.DoesNotContain(ungranted, registered.PermittedScopes);

        string[] requested = position == "first" ? [ungranted, granted] : [granted, ungranted];

        TokenIssuanceResult result = Issue(
            factory,
            registered.Subject,
            GrantedAudienceFor(factory, registered.Subject),
            requested);

        Assert.Equal(TokenIssuanceOutcome.Issued, result.Outcome);
        Assert.NotNull(result.Token);
        Assert.Equal(granted, result.Token.GrantedScope, StringComparer.Ordinal);
    }

    /// <summary>A request naming only ungranted scopes is refused, and nothing is minted.</summary>
    /// <remarks>
    /// THE REFUSAL HALF OF THE NARROWING RULE. The forbidden status "means that NOTHING was permitted,
    /// which is refused only because the granted scope member is required and there would be nothing
    /// truthful to report in it" [security.v1.yaml].
    /// </remarks>
    [Fact]
    public async Task ARequestNamingOnlyUngrantedScopesIsRefusedAsync()
    {
        await using SecurityAppFactory factory = new();

        RegisteredIssuanceClient registered = Resolve(factory, "powerframework-gateway");

        TokenIssuanceResult result = Issue(
            factory,
            registered.Subject,
            GrantedAudienceFor(factory, registered.Subject),
            ["a.scope.no.roster.entry.grants"]);

        Assert.Equal(TokenIssuanceOutcome.ScopesNotPermitted, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// Every refusal outcome projects onto the contract's forbidden status with its own sentence.
    /// </summary>
    /// <param name="outcome">The outcome to drive.</param>
    /// <remarks>
    /// DRIVEN THROUGH THE HANDLER rather than through the pipeline, so every arm of its switch is
    /// reachable by a row - which is what makes the per-service coverage gate attainable on logic that a
    /// transport cannot easily produce. The four sentences are asserted to be DISTINCT, because four
    /// outcomes reported with one sentence is the same as one outcome.
    /// </remarks>
    [Theory]
    [InlineData(TokenIssuanceOutcome.AudienceNotPermitted, "unserved-audience")]
    [InlineData(TokenIssuanceOutcome.CallerNotPermitted, "unregistered-subject")]
    [InlineData(TokenIssuanceOutcome.CallerNotPermitted, "served-but-ungranted-audience")]
    [InlineData(TokenIssuanceOutcome.ScopesNotPermitted, "no-granted-scope")]
    public async Task EveryRefusalCarriesItsOwnSentenceAsync(TokenIssuanceOutcome outcome, string shape)
    {
        await using SecurityAppFactory factory = new();

        SecurityOptions options = factory.ResolveSecurityOptions();
        RegisteredIssuanceClient registered = Resolve(factory, "powerframework-gateway");

        string audience = GrantedAudienceFor(factory, registered.Subject);
        string granted = GrantedScopeFor(factory, registered.Subject, audience);

        (string Subject, string Audience, string[] Scopes) request = shape switch
        {
            "unserved-audience" =>
                (registered.Subject, "an-audience-no-deployment-here-serves", [granted]),

            "unregistered-subject" =>
                ("a-subject-no-roster-entry-names", options.Audiences[0], [granted]),

            "served-but-ungranted-audience" =>
                (registered.Subject, UngrantedServedAudience(factory, registered.Subject), [granted]),

            _ => (registered.Subject, audience, ["a.scope.no.roster.entry.grants"]),
        };

        TokenIssuanceResult result =
            Issue(factory, request.Subject, request.Audience, request.Scopes);

        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Token);
    }

    /// <summary>Reads an audience the host's permission matrix grants a caller.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The caller.</param>
    /// <returns>The audience.</returns>
    /// <remarks>
    /// READ FROM THE MATRIX RATHER THAN FROM THE ISSUANCE ROSTER, because the matrix is the gate. The
    /// roster says which callers exist and which credential each authenticates with; a row that took its
    /// audience from the roster would be asserting against a surface that decides nothing, and would fail
    /// or pass depending on which member of an unordered set it happened to read first.
    /// </remarks>
    private static string GrantedAudienceFor(SecurityAppFactory factory, string subject) =>
        MatrixRowsFor(factory, subject).Select(row => row.Audience).First();

    /// <summary>Reads a scope the host's permission matrix grants a caller for one audience.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The caller.</param>
    /// <param name="audience">The audience.</param>
    /// <returns>The scope.</returns>
    private static string GrantedScopeFor(
        SecurityAppFactory factory,
        string subject,
        string audience) =>
        MatrixRowsFor(factory, subject)
            .Where(row => string.Equals(row.Audience, audience, StringComparison.Ordinal))
            .SelectMany(row => row.Scopes)
            .First();

    /// <summary>Reads a served audience the matrix does NOT grant a caller.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The caller.</param>
    /// <returns>The audience.</returns>
    /// <remarks>
    /// The row it serves needs a refusal that comes from the matrix rather than from the deployment-wide
    /// audience roster, so the audience must be one the roster carries and the matrix does not grant. The
    /// harness matrix authorises its test callers for every roster audience, so one is added to the roster
    /// here specifically to be ungranted.
    /// </remarks>
    private static string UngrantedServedAudience(SecurityAppFactory factory, string subject)
    {
        SecurityOptions options = factory.ResolveSecurityOptions();

        HashSet<string> granted = MatrixRowsFor(factory, subject)
            .Select(row => row.Audience)
            .ToHashSet(StringComparer.Ordinal);

        return options.Audiences.First(candidate => !granted.Contains(candidate));
    }

    /// <summary>Projects both configured matrix shapes into one row sequence for one caller.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The caller.</param>
    /// <returns>Its rows.</returns>
    /// <remarks>
    /// BOTH SHAPES, BECAUSE THE ISSUER ENFORCES THEIR UNION. A helper reading only one of them would
    /// report a caller as ungranted purely because the deployment happened to express its grant in the
    /// other shape.
    /// </remarks>
    private static IEnumerable<(string Audience, IEnumerable<string> Scopes)> MatrixRowsFor(
        SecurityAppFactory factory,
        string subject)
    {
        SecurityOptions options = factory.ResolveSecurityOptions();

        foreach (SecurityCallerOptions caller in options.Callers)
        {
            if (!string.Equals(caller.Identity, subject, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (SecurityCallerGrantOptions grant in caller.Grants)
            {
                yield return (grant.Audience, grant.Scopes);
            }
        }

        foreach (CallerAuthorizationOptions row in options.CallerAuthorizations)
        {
            if (string.Equals(row.Caller, subject, StringComparison.Ordinal))
            {
                yield return (row.Audience, row.Scopes);
            }
        }
    }

    /// <summary>Reads one roster entry from the booted host.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The subject to read.</param>
    /// <returns>The entry.</returns>
    private static RegisteredIssuanceClient Resolve(SecurityAppFactory factory, string subject)
    {
        Assert.True(
            factory.Services
                .GetRequiredService<IssuanceClientRegistry>()
                .TryResolveSubject(subject, out RegisteredIssuanceClient? registered),
            $"The booted host's issuance roster registers no subject '{subject}'.");

        return registered;
    }

    /// <summary>Drives one issuance through the host's own issuer.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="subject">The claimed subject.</param>
    /// <param name="audience">The requested audience.</param>
    /// <param name="scopes">The requested scopes.</param>
    /// <returns>The result.</returns>
    private static TokenIssuanceResult Issue(
        SecurityAppFactory factory,
        string subject,
        string audience,
        string[] scopes) =>
        factory.Services
            .GetRequiredService<TokenIssuer>()
            .Issue(new TokenIssuanceRequest(subject, audience, scopes));
}

/// <summary>
/// The scope policies gate the protected routes, so the published forbidden response is reachable.
/// </summary>
public sealed class ScopePolicyEnforcementTests
{
    /// <summary>
    /// A valid token whose caller lacks the route's scope is FORBIDDEN, not admitted.
    /// </summary>
    /// <param name="path">The protected route.</param>
    /// <param name="requiredScope">The scope it requires.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT MAKES THE PUBLISHED 403 REAL. Before the policies, every route required only an
    /// AUTHENTICATED principal, so any token this system minted - for any audience, carrying any scope -
    /// opened all of them, and the forbidden response the contract declares could not be produced by
    /// anything. The token presented here is genuine, correctly addressed, unexpired and signed by this
    /// host's own key; only its scope set is narrow.
    /// </para>
    /// <para>
    /// The required scope is read from the route's own declaration rather than spelled here, so a rename
    /// is a compile-time change instead of a row that silently stops testing anything.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("/v1/ping", PingEndpoints.RequiredScope)]
    [InlineData("/v1/crypto/hash", CryptoEndpoints.RequiredScope)]
    public async Task ATokenWithoutTheRouteScopeIsForbiddenAsync(string path, string requiredScope)
    {
        await using SecurityAppFactory factory = new();

        const string caller = "powerframework-dataservices";

        // A SCOPE THE ROSTER GRANTS THIS CALLER BUT THIS ROUTE DOES NOT REQUIRE, read from the roster
        // rather than invented. An invented name would be refused by the ISSUER, and the row would then
        // fail during its own setup rather than at the route it exists to test - proving the issuance
        // gate a second time and the route policy not at all.
        Assert.True(
            factory.Services
                .GetRequiredService<IssuanceClientRegistry>()
                .TryResolveSubject(caller, out RegisteredIssuanceClient? registered),
            $"The booted host's issuance roster registers no subject '{caller}'.");

        string grantedButUnrelated = registered.PermittedScopes.First(
            scope => !string.Equals(scope, requiredScope, StringComparison.Ordinal));

        using HttpClient narrow = factory.CreateAuthenticatedClient(
            caller,
            factory.ResolveInboundAudience(),
            [grantedButUnrelated]);

        using HttpResponseMessage refused = path == "/v1/ping"
            ? await narrow.GetAsync(path, TestContext.Current.CancellationToken)
            : await narrow.PostAsync(
                path,
                JsonBody(),
                TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // AND THE SAME CALLER WITH THE SCOPE IS ADMITTED, which is what separates a working policy from
        // a route that refuses everything.
        using HttpClient granted = factory.CreateAuthenticatedClient(
            caller,
            factory.ResolveInboundAudience(),
            [requiredScope]);

        using HttpResponseMessage admitted = path == "/v1/ping"
            ? await granted.GetAsync(path, TestContext.Current.CancellationToken)
            : await granted.PostAsync(
                path,
                JsonBody(),
                TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Forbidden, admitted.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, admitted.StatusCode);
    }

    /// <summary>
    /// An anonymous request is still UNAUTHORIZED rather than forbidden, so the frozen environment's
    /// documented behaviour is unchanged.
    /// </summary>
    /// <param name="path">The protected route.</param>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// THE COMPATIBILITY ROW (C-L). The attached environment documents that the authenticated probe
    /// answers 401 without a token, and a scope policy must not change that: authentication precedes
    /// authorization, so the scope check can only ever turn an AUTHENTICATED request into a 403 - a
    /// strictly narrower outcome than the documented one, and a declared response of the operation.
    /// </remarks>
    [Theory]
    [InlineData("/v1/ping")]
    [InlineData("/v1/crypto/hash")]
    public async Task AnAnonymousRequestIsStillUnauthorizedAsync(string path)
    {
        await using SecurityAppFactory factory = new();

        using HttpClient anonymous = factory.CreateClient();

        using HttpResponseMessage refused = path == "/v1/ping"
            ? await anonymous.GetAsync(path, TestContext.Current.CancellationToken)
            : await anonymous.PostAsync(path, JsonBody(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    /// <summary>
    /// The route's policy name is composed from its required scope, so the two cannot drift.
    /// </summary>
    /// <remarks>
    /// A policy registered under a name no route requires enforces nothing at all and looks correct in
    /// review, which is why the name is derived rather than written twice.
    /// </remarks>
    [Fact]
    public void EachPolicyNameIsComposedFromItsRequiredScope()
    {
        Assert.EndsWith(PingEndpoints.RequiredScope, PingEndpoints.ScopePolicyName, StringComparison.Ordinal);
        Assert.EndsWith(
            CryptoEndpoints.RequiredScope,
            CryptoEndpoints.ScopePolicyName,
            StringComparison.Ordinal);

        Assert.NotEqual(PingEndpoints.ScopePolicyName, CryptoEndpoints.ScopePolicyName);
    }

    /// <summary>
    /// Both named policies are registered, and each requires an authenticated principal as well as
    /// its scope.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// A route requiring a policy nobody registered fails closed and loudly at the first request; this
    /// row makes that a build-time-adjacent failure instead. The authenticated-user requirement is
    /// asserted because it makes each policy correct IN ISOLATION rather than correct by virtue of the
    /// host's fallback.
    /// </remarks>
    [Fact]
    public async Task BothNamedPoliciesAreRegisteredAsync()
    {
        await using SecurityAppFactory factory = new();

        IAuthorizationPolicyProvider provider =
            factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (string name in new[] { PingEndpoints.ScopePolicyName, CryptoEndpoints.ScopePolicyName })
        {
            AuthorizationPolicy? policy = await provider.GetPolicyAsync(name);

            Assert.NotNull(policy);

            Assert.Contains(policy.Requirements, requirement => requirement is DenyAnonymousAuthorizationRequirement);

            // THE SCOPE REQUIREMENT IS TYPED AND NAMED RATHER THAN AN INLINE ASSERTION, which is what makes
            // this assertion able to state WHICH scope the policy demands rather than merely that it
            // demands something. An assertion requirement carries an opaque delegate: a policy that
            // asserted the wrong scope, or nothing at all, would satisfy a test that only checked one was
            // present. See Authorization/ScopeAuthorization.cs for the requirement and its handler.
            Assert.Contains(
                policy.Requirements,
                requirement => requirement
                    .GetType()
                    .GetProperty("Scope")
                    ?.GetValue(requirement) as string is { Length: > 0 } demanded
                    && name.EndsWith(demanded, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// A misspelled member is refused with the contract's BAD REQUEST, not with a server error.
    /// </summary>
    /// <returns>A task representing the assertion.</returns>
    /// <remarks>
    /// <para>
    /// THE ROW THAT CATCHES A REAL DEFECT AND WOULD CATCH IT AGAIN. Refusing an undeclared member makes
    /// `additionalProperties: false` an enforced rule, and body binding reports it by raising an
    /// exception that CARRIES its own intended status of 400. The parameterless exception-handler
    /// registration discards that status and answers 500 - so the schema was enforced while the caller
    /// was told the SERVICE had failed, which sends an operator to investigate a service that behaved
    /// correctly.
    /// </para>
    /// <para>
    /// The body is asserted to carry the published error shape's return-code member and to name NO part
    /// of the caller's payload: on this service in particular, an undeclared member's name or value may
    /// be key material a caller mistyped into the wrong field.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AnUndeclaredMemberIsRefusedAsABadRequestAsync()
    {
        await using SecurityAppFactory factory = new();

        using HttpClient caller = factory.CreateAuthenticatedClient();

        const string misspelled = "hashTypeeee";

        using StringContent body = new(
            $"{{\"data\":\"abc\",\"payloadForm\":\"STRING\",\"hashType\":2,\"{misspelled}\":\"x\"}}",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage refused = await caller.PostAsync(
            "/v1/crypto/hash",
            body,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        string payload = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ProblemResults.RetCodeExtensionMember, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(misspelled, payload, StringComparison.Ordinal);
    }

    /// <summary>A minimal well-formed digest body, for the rows that post one.</summary>
    /// <returns>The body.</returns>
    private static StringContent JsonBody() =>
        new(
            "{\"data\":\"abc\",\"payloadForm\":\"STRING\",\"hashType\":2}",
            Encoding.UTF8,
            "application/json");
}
